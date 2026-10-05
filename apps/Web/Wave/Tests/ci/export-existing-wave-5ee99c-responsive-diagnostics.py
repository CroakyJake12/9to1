#!/usr/bin/env python3
"""Read-only exact original Actions ZIP custody; no application execution."""
import argparse
import base64
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import stat
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile

REPOSITORY = "CroakyJake12/9to1"
RUN = 37245186076
ARTIFACT = 11319221458
SOURCE_COMMIT = "5ee99c19d42e9e6ae687c7ef528fd947a8828540"
ARCHIVE_BYTES = 2048750
ARCHIVE_SHA256 = "fc14c78b1d674d8987e7665bef341f242c4497cb054cc6875213a39fface5f04"
FRAGMENT_BYTES = 400000
MAX_OWNED_BYTES = 128 * 1024 * 1024
MIN_FREE_BYTES = 256 * 1024 * 1024
MAX_EXPANDED_BYTES = 256 * 1024 * 1024
MAX_MEMBERS = 4096
MAX_METADATA_BYTES = 2 * 1024 * 1024
DEADLINE_SECONDS = 600


class CustodyError(Exception):
    pass


def require(condition, code):
    if not condition:
        raise CustodyError(code)


def sha(body):
    return hashlib.sha256(body).hexdigest()


def validate_metadata(artifact, run):
    require(artifact.get("id") == ARTIFACT, "ARTIFACT_ID_MISMATCH")
    require(artifact.get("expired") is False, "ARTIFACT_EXPIRED")
    require(artifact.get("size_in_bytes") == ARCHIVE_BYTES, "ARTIFACT_SIZE_MISMATCH")
    require(artifact.get("digest") == "sha256:" + ARCHIVE_SHA256, "ARTIFACT_DIGEST_MISMATCH")
    require(artifact.get("workflow_run", {}).get("id") == RUN, "ARTIFACT_RUN_MISMATCH")
    require(run.get("id") == RUN and run.get("head_sha") == SOURCE_COMMIT,
            "SOURCE_RUN_COMMIT_MISMATCH")
    require(run.get("status") == "completed" and run.get("conclusion") == "failure",
            "SOURCE_RUN_NOT_SUCCESSFUL")


def fragment_ranges(size):
    require(size == ARCHIVE_BYTES, "SPLIT_SIZE_MISMATCH")
    return [(offset, min(FRAGMENT_BYTES, size - offset))
            for offset in range(0, size, FRAGMENT_BYTES)]


def safe_name(name):
    path = PurePosixPath(name)
    require(bool(name) and "\\" not in name and "\x00" not in name
            and not path.is_absolute() and ":" not in name
            and all(part not in ("", ".", "..") for part in name.rstrip("/").split("/")),
            "UNSAFE_ZIP_MEMBER")


def inspect_archive(body):
    require((len(body), sha(body)) == (ARCHIVE_BYTES, ARCHIVE_SHA256), "ARCHIVE_IDENTITY_MISMATCH")
    members = []
    expanded = 0
    with zipfile.ZipFile(io.BytesIO(body)) as archive:
        infos = archive.infolist()
        require(0 < len(infos) <= MAX_MEMBERS and len({i.filename for i in infos}) == len(infos),
                "ZIP_MEMBER_SET_INVALID")
        for info in infos:
            safe_name(info.filename)
            mode = info.external_attr >> 16
            file_type = stat.S_IFMT(mode)
            require(file_type in (0, stat.S_IFREG, stat.S_IFDIR)
                    and not info.flag_bits & 1, "ZIP_UNSUPPORTED_MEMBER_MODE")
            require(info.is_dir() == (file_type == stat.S_IFDIR) or file_type == 0,
                    "ZIP_DIRECTORY_MODE_MISMATCH")
            expanded += info.file_size
            require(expanded <= MAX_EXPANDED_BYTES, "ZIP_EXPANSION_LIMIT")
            data = archive.read(info)  # Full read verifies ZIP CRC; never extract names.
            require(len(data) == info.file_size, "ZIP_MEMBER_SIZE_MISMATCH")
            members.append({"path": info.filename, "bytes": len(data), "sha256": sha(data),
                            "crc32": info.CRC, "mode": mode, "directory": info.is_dir()})
    return members


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class Reader:
    def __init__(self, token):
        self.token = token
        self.opener = urllib.request.build_opener(NoRedirect())
        self.deadline = time.monotonic() + DEADLINE_SECONDS

    def get(self, url, limit, authenticated=False, redirect=False):
        for _ in range(6):
            remaining = self.deadline - time.monotonic()
            require(remaining > 0, "HTTP_DEADLINE")
            parsed = urllib.parse.urlsplit(url)
            require(parsed.scheme == "https" and parsed.hostname and not parsed.username
                    and not parsed.password and parsed.port in (None, 443), "UNSAFE_HTTP_TARGET")
            if authenticated:
                require(parsed.hostname == "api.github.com"
                        and parsed.path.startswith("/repos/" + REPOSITORY + "/actions/"),
                        "AUTH_TARGET_REFUSED")
            headers = {"User-Agent": "Team-B-original-artifact-custody",
                       "Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28"}
            if authenticated:
                headers["Authorization"] = "Bearer " + self.token
            request = urllib.request.Request(url, headers=headers)
            try:
                with self.opener.open(request, timeout=min(45, remaining)) as response:
                    declared = response.headers.get("Content-Length")
                    require(declared is None or int(declared) <= limit, "HTTP_BODY_DECLARED_LIMIT")
                    body = response.read(limit + 1)
                require(len(body) <= limit, "HTTP_BODY_LIMIT")
                return body
            except urllib.error.HTTPError as error:
                try:
                    location = error.headers.get("Location")
                    require(redirect and error.code in (301, 302, 303, 307, 308) and location,
                            "HTTP_STATUS_REFUSED")
                    url = urllib.parse.urljoin(url, location)
                    # Signed provider requests always omit Authorization, including later redirects.
                    authenticated = False
                finally:
                    error.close()
        raise CustodyError("HTTP_REDIRECT_LIMIT")


def allocation(output):
    return sum(p.stat().st_size for p in output.rglob("*") if p.is_file())


def write_owned(output, relative, body):
    require(allocation(output) + len(body) <= MAX_OWNED_BYTES, "OWNED_BYTE_LIMIT")
    require(shutil.disk_usage(output).free - len(body) >= MIN_FREE_BYTES, "FREE_FLOOR")
    target = output / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    require(not target.exists(), "OUTPUT_ALREADY_EXISTS")
    with target.open("xb") as stream:
        stream.write(body)
        stream.flush()
        os.fsync(stream.fileno())
    require((target.stat().st_size, sha(target.read_bytes())) == (len(body), sha(body)), "OUTPUT_READBACK")
    return target


MAX_SELECTED_DECODED_BYTES = 32768
MAX_SELECTED_WIRE_BYTES = 65536
SELECTED_MEMBER_PATHS = (
    "wr-run/raw/results.json",
    "wr-run/diagnostics/wave-responsive-real-browser.log",
    "wr-run/diagnostics/commands.json",
)

def selected_diagnostics(body, members):
    require(len(SELECTED_MEMBER_PATHS) == 3
            and len(set(SELECTED_MEMBER_PATHS)) == 3, "EXACT_THREE_UNIQUE_DIAGNOSTICS")
    index = {item["path"]: item for item in members}
    require(len(index) == len(members), "ORIGINAL_MEMBER_INDEX_MISMATCH")
    records = []
    for path in SELECTED_MEMBER_PATHS:
        require(path in index, "EXACT_DIAGNOSTIC_MEMBER_ABSENT")
        record = index[path]
        require(not record["directory"] and record["mode"] == 33188,
                "EXACT_DIAGNOSTIC_MODE_MISMATCH")
        records.append(record)
    require(sum(item["bytes"] for item in records) <= MAX_SELECTED_DECODED_BYTES,
            "SELECTED_DECODED_BYTE_LIMIT")
    selected = []
    with zipfile.ZipFile(io.BytesIO(body)) as archive:
        for record in records:
            data = archive.read(record["path"])
            require(len(data) == record["bytes"] and sha(data) == record["sha256"],
                    "SELECTED_FULL_BODY_READBACK")
            selected.append({**record, "base64": base64.b64encode(data).decode("ascii")})
    return selected


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    require(not output.exists() and not output.is_relative_to(Path.cwd().resolve()),
            "FRESH_EXTERNAL_OUTPUT_REQUIRED")
    require(shutil.disk_usage(output.parent).free >= MAX_OWNED_BYTES + MIN_FREE_BYTES,
            "INITIAL_RESOURCE_RESERVE")
    output.mkdir()
    script = Path(__file__).resolve()
    script_before = script.read_bytes()
    result = {"state": "NOT_RUN", "sourceRun": RUN, "sourceArtifact": ARTIFACT,
              "sourceCommit": SOURCE_COMMIT, "archiveBytes": ARCHIVE_BYTES,
              "archiveSha256": ARCHIVE_SHA256, "expectedSourceConclusion": "failure",
              "custodyWorkflowCommit": os.environ.get("GITHUB_SHA"),
              "scope": "Exact failed original ZIP and three literal responsive-diagnostic bodies only; no app rerun or browser acceptance."}
    try:
        token = os.environ.get("GITHUB_TOKEN")
        require(bool(token), "READ_ONLY_ACTIONS_TOKEN_REQUIRED")
        require(os.environ.get("GITHUB_REPOSITORY") == REPOSITORY
                and os.environ.get("GITHUB_REF") == "refs/heads/custody/team-b-wave-browser-5ee99c-responsive-diagnostics-20261005",
                "EXACT_CUSTODY_BRANCH_REQUIRED")
        reader = Reader(token)
        prefix = "https://api.github.com/repos/" + REPOSITORY + "/actions/"
        artifact = json.loads(reader.get(prefix + "artifacts/" + str(ARTIFACT),
                                         MAX_METADATA_BYTES, True))
        run = json.loads(reader.get(prefix + "runs/" + str(RUN), MAX_METADATA_BYTES, True))
        validate_metadata(artifact, run)
        body = reader.get(prefix + "artifacts/" + str(ARTIFACT) + "/zip",
                          ARCHIVE_BYTES, True, True)
        members = inspect_archive(body)
        member_raw = (json.dumps(members, separators=(",", ":")) + "\n").encode()
        require(len(member_raw) <= MAX_SELECTED_WIRE_BYTES, "MEMBER_MANIFEST_WIRE_LIMIT")
        member_rows_sha = sha(json.dumps(members, sort_keys=True, separators=(",", ":")).encode())
        write_owned(output, "diagnostics/original-members.json", member_raw)
        print("ORIGINAL_MEMBERS: " + member_raw.decode("utf-8").rstrip("\n"), flush=True)
        selected = selected_diagnostics(body, members)
        receipt = {"schema": "team-b-fixed-failed-responsive-diagnostics-v1",
                   **result, "state": "VERIFIED_SELECTED_DIAGNOSTICS",
                   "memberCount": len(members),
                   "originalMemberRowsSha256": member_rows_sha,
                   "wholeOriginalShaAndAllMemberCRCRead": True,
                   "selectedDecodedBytes": sum(item["bytes"] for item in selected),
                   "selected": selected, "sourceSha256": sha(script_before)}
        receipt_raw = (json.dumps(receipt, separators=(",", ":")) + "\n").encode()
        require(len(receipt_raw) <= MAX_SELECTED_WIRE_BYTES, "SELECTED_WIRE_BYTE_LIMIT")
        write_owned(output, "diagnostics/selected-source-diagnostics.json", receipt_raw)
        require(script.read_bytes() == script_before, "SOURCE_CHANGED")
        result.update(state="PASS_SELECTED_ORIGINAL_DIAGNOSTIC_CUSTODY",
                      selectedDecodedBytes=sum(item["bytes"] for item in selected), selectedMemberCount=3,
                      selectedWireBytes=len(receipt_raw), selectedReceiptSha256=sha(receipt_raw),
                      originalMemberRowsSha256=member_rows_sha,
                      memberCount=len(members), sourceSha256=sha(script_before),
                      ownedBytes=allocation(output), freeBytes=shutil.disk_usage(output).free)
    except Exception as error:
        result.update(state="FAIL", errorCode=str(error) if isinstance(error, CustodyError)
                      else type(error).__name__)
    raw = (json.dumps(result, indent=2) + "\n").encode()
    require(len(raw) <= MAX_METADATA_BYTES, "RESULT_LIMIT")
    write_owned(output, "diagnostics/result.json", raw)
    if result["state"] == "PASS_SELECTED_ORIGINAL_DIAGNOSTIC_CUSTODY":
        print("SELECTED_DIAGNOSTICS: " + receipt_raw.decode("utf-8").rstrip("\n"))
    print(json.dumps({"state": result["state"], "sourceRun": RUN, "sourceArtifact": ARTIFACT,
                      "originalSha256": ARCHIVE_SHA256,
                      "selectedDecodedBytes": result.get("selectedDecodedBytes", 0)}))
    return 0 if result["state"] == "PASS_SELECTED_ORIGINAL_DIAGNOSTIC_CUSTODY" else 1


if __name__ == "__main__":
    raise SystemExit(main())
