"""Pinned, read-only Hugging Face Hub access for Dulche's background worker."""

import io
import json
import os
import re
from urllib import parse, request

from model_formats import (
    MAX_HEADER,
    MAX_PARAMETERS,
    SHARD,
    artifact_file,
    gguf_header,
    quant,
    safe_relative,
)

HUB = "https://huggingface.co"
COMMIT = re.compile(r"[0-9a-f]{40}")
REPO = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,95}/[A-Za-z0-9][A-Za-z0-9_.-]{0,95}")


class PrefixReader:
    """Forward-only seek over a bounded network prefix, without storing model weights."""

    def __init__(self, stream):
        self.stream = io.BufferedReader(stream, 64 * 1024)
        self.position = 0

    def read(self, n):
        data = self.stream.read(n)
        self.position += len(data)
        return data

    def tell(self):
        return self.position

    def seek(self, n, whence):
        if whence != 1 or n < 0:
            raise ValueError("invalid model metadata offset")
        while n:
            data = self.read(min(n, 65536))
            if not data:
                raise ValueError("truncated remote GGUF metadata")
            n -= len(data)


class HubRedirect(request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, hdrs, newurl):
        dest = parse.urlparse(newurl)
        if (
            dest.scheme != "https"
            or dest.port not in (None, 443)
            or dest.username
            or dest.password
            or not any(
                dest.hostname == suffix or (dest.hostname or "").endswith("." + suffix)
                for suffix in ("huggingface.co", "hf.co", "xethub.hf.co")
            )
        ):
            raise ValueError("Hub redirected outside trusted artifact hosts")
        new = super().redirect_request(req, fp, code, msg, hdrs, newurl)
        if new and dest.hostname != parse.urlparse(req.full_url).hostname:
            new.remove_header("Authorization")
        return new


class Hub:
    def __init__(self):
        self.opener = request.build_opener(HubRedirect())

    def open(self, path, headers=None):
        if not path.startswith("/") or path.startswith("//"):
            raise ValueError("invalid Hub route")
        hdr = {
            "User-Agent": "9to1-Dulche/1",
            "Accept-Encoding": "identity",
            **(headers or {}),
        }
        token = os.environ.get("HF_TOKEN")
        if token:
            hdr["Authorization"] = "Bearer " + token
        return self.opener.open(request.Request(HUB + path, headers=hdr), timeout=25)

    def metadata(self, path):
        with self.open(path) as response:
            data = response.read(16 * 1024**2 + 1)
            if len(data) > 16 * 1024**2:
                raise ValueError("Hub metadata too large")
            return json.loads(data)

    def summary(self, row):
        repo = row["id"]
        gguf = row.get("gguf") or {}
        st = row.get("safetensors") or {}
        card = row.get("cardData") or {}
        tags = row.get("tags") or []
        cfg = row.get("config") or {}
        count = (
            max(
                [v for v in (st.get("total"), gguf.get("total")) if type(v) is int]
                or [0]
            )
            or None
        )
        return {
            "id": repo,
            "repo": repo,
            "name": repo.split("/")[-1],
            "developer": repo.split("/")[0],
            "revision": row.get("sha"),
            "created_at": row.get("createdAt"),
            "updated_at": row.get("lastModified"),
            "tags": tags,
            "capability": row.get("pipeline_tag"),
            "architecture": gguf.get("architecture") or cfg.get("model_type"),
            "parameters": count,
            "active_parameters": None,
            "quantization": quant(repo),
            "context_length": gguf.get("context_length"),
            "license": card.get("license"),
            "gated": bool(row.get("gated")),
            "base_models": card.get("base_model") or [],
            "source": HUB + "/" + repo,
            "estimated_weight_bytes": gguf.get("totalFileSize"),
        }

    def search(self, q):
        text = str(q.get("q", "")).strip()
        if len(text) > 200 or any(ord(c) < 32 for c in text):
            raise ValueError("invalid search query")
        fields = [
            "config",
            "gguf",
            "safetensors",
            "cardData",
            "pipeline_tag",
            "tags",
            "createdAt",
            "lastModified",
            "gated",
            "sha",
        ]
        params = [("search", text), ("limit", "40"), ("sort", "downloads")] + [
            ("expand[]", field) for field in fields
        ]
        items = [
            self.summary(v)
            for v in self.metadata("/api/models?" + parse.urlencode(params))
        ]
        for key in ("architecture", "quantization", "capability"):
            if q.get(key):
                needle = str(q[key]).lower()
                items = [
                    v
                    for v in items
                    if needle
                    in " ".join(
                        str(v.get(k) or "") for k in (key, "tags", "repo")
                    ).lower()
                ]
        if q.get("max_parameters"):
            limit = int(q["max_parameters"])
            items = [v for v in items if v["parameters"] and v["parameters"] <= limit]
        return items

    def detail(self, repo, revision=None):
        if (
            not isinstance(repo, str)
            or not REPO.fullmatch(repo)
            or ".." in repo
            or (revision and not COMMIT.fullmatch(revision))
        ):
            raise ValueError(
                "supply a Hub repository ID and optional full pinned revision"
            )
        path = (
            "/api/models/"
            + repo
            + ("/revision/" + revision if revision else "")
            + "?blobs=true"
        )
        row = self.metadata(path)
        if (
            row.get("id") != repo
            or not COMMIT.fullmatch(row.get("sha", ""))
            or (revision and row["sha"] != revision)
        ):
            raise ValueError("repository identity changed")
        result = self.summary(row)
        files = []
        for f in row.get("siblings", []):
            name = f["rfilename"]
            if not artifact_file(name):
                continue
            lfs = f.get("lfs") or {}
            sha = lfs.get("sha256")
            git = f.get("blobId")
            if type(f.get("size")) is not int or f["size"] <= 0:
                continue
            if not (
                (sha and re.fullmatch("[0-9a-f]{64}", sha))
                or (git and COMMIT.fullmatch(git))
            ):
                continue
            files.append(
                {
                    "path": name,
                    "bytes": f["size"],
                    "sha256": sha,
                    "git_blob": git,
                    "quantization": quant(name),
                }
            )
        result["files"] = files
        result["estimated_weight_bytes"] = (
            sum(
                f["bytes"]
                for f in files
                if f["path"].endswith(".safetensors") and "/" not in f["path"]
            )
            or result["estimated_weight_bytes"]
        )
        result["compatibility"] = (
            "GGUF runtime probe required"
            if any(f["path"].endswith(".gguf") for f in files)
            else "requires compatible SafeTensors runtime"
        )
        result["estimated_ram_bytes"] = None
        result["estimated_vram_bytes"] = None
        result["measured_tokens_per_second"] = None
        return result

    def plan(self, repo, filename, revision):
        data = self.detail(repo, revision)
        count = data.get("parameters")
        if count and count > MAX_PARAMETERS:
            raise ValueError("checkpoint exceeds 500B before download")
        files = data["files"]
        paths = {f["path"]: f for f in files}
        if filename:
            p = safe_relative(filename)
            if filename not in paths or p.suffix.lower() != ".gguf":
                raise ValueError("choose a GGUF file listed in this revision")
            match = SHARD.match(p.name)
            selected = [filename]
            if match:
                prefix, _, n = match.groups()
                if not 1 <= int(n) <= 1024:
                    raise ValueError("unsupported shard count")
                selected = [
                    str(p.with_name(f"{prefix}-{i:05}-of-{n}.gguf"))
                    for i in range(1, int(n) + 1)
                ]
            if any(x not in paths for x in selected):
                raise ValueError("missing remote GGUF shard")
            files = [paths[x] for x in selected]
            # Repository-level Hub GGUF counts may describe a different file in a collection.
            count = 0
            for item in files:
                end = min(MAX_HEADER, item["bytes"]) - 1
                pathname = (
                    "/"
                    + repo
                    + "/resolve/"
                    + data["revision"]
                    + "/"
                    + parse.quote(item["path"], safe="/")
                )
                with self.open(pathname, {"Range": f"bytes=0-{end}"}) as remote:
                    meta, _, elements = gguf_header(PrefixReader(remote), item["bytes"])
                count += elements
                if count > MAX_PARAMETERS:
                    raise ValueError("GGUF tensor tables exceed 500B before download")
                if meta.get("split.count", 1) != len(files):
                    raise ValueError("incomplete GGUF shard group")
        else:
            if not any(f in paths for f in ("config.json", "adapter_config.json")):
                raise ValueError("choose an explicit GGUF checkpoint")
            files = [
                f
                for f in files
                if "/" not in f["path"] and not f["path"].lower().endswith(".gguf")
            ]
            if not any(f["path"].endswith(".safetensors") for f in files):
                raise ValueError("supported SafeTensors weights required")
            if "adapter_config.json" in paths and "config.json" not in paths:
                adapter = self.metadata(
                    "/" + repo + "/resolve/" + data["revision"] + "/adapter_config.json"
                )
                base_id = adapter.get("base_model_name_or_path")
                if not isinstance(base_id, str) or not REPO.fullmatch(base_id):
                    raise ValueError("adapter has no verifiable Hub base")
                base = self.detail(base_id)
                if not base["parameters"] or base["parameters"] > MAX_PARAMETERS:
                    raise ValueError(
                        "adapter base must have a verified parameter count at most 500B"
                    )
            if "config.json" in paths:
                config = self.metadata(
                    "/" + repo + "/resolve/" + data["revision"] + "/config.json"
                )
                if config.get("auto_map"):
                    raise ValueError("remote repository code is not supported")
                if config.get("quantization_config") or (
                    config.get("text_config") or {}
                ).get("quantization_config"):
                    # A packed SafeTensors shape is not a logical model parameter count.
                    bases = data["base_models"]
                    if isinstance(bases, str):
                        bases = [bases]
                    if len(bases) != 1 or bases[0] == repo:
                        raise ValueError(
                            "packed checkpoint requires one verifiable unquantized base model"
                        )
                    base = self.detail(bases[0])
                    base_config = self.metadata(
                        "/"
                        + base["repo"]
                        + "/resolve/"
                        + base["revision"]
                        + "/config.json"
                    )
                    if base_config.get("quantization_config") or (
                        base_config.get("text_config") or {}
                    ).get("quantization_config"):
                        raise ValueError(
                            "base checkpoint is also packed; logical parameter count unavailable"
                        )
                    count = (
                        max(count or 0, base.get("parameters") or 0)
                        if base.get("parameters")
                        else None
                    )
            if not count or count > MAX_PARAMETERS:
                raise ValueError(
                    "verified total parameters required and must be at most 500B before download"
                )
        return {
            "repo": repo,
            "revision": data["revision"],
            "license": data["license"],
            "parameters": count,
            "files": files,
            "target": files[0]["path"] if filename else ".",
            "total_bytes": sum(f["bytes"] for f in files),
        }
