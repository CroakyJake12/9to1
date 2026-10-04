#!/usr/bin/env python3
"""Read-only exact original Actions ZIP custody; no application execution."""
import argparse
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
RUN = 37226746160
ARTIFACT = 11312019529
SOURCE_COMMIT = "69cff0de54c22ab46728efa18ed0a32ac1efce03"
ARCHIVE_BYTES = 25407257
ARCHIVE_SHA256 = "ae04b698db600bdd4c3f950ce2dbd003f5fb5f4a73201dc6d18f03ee204e943f"
FRAGMENT_BYTES = 500000
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
    require(run.get("status") == "completed" and run.get("conclusion") == "success",
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


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    require(not output.exists() and not output.is_relative_to(Path.cwd().resolve()), "FRESH_EXTERNAL_OUTPUT_REQUIRED")
    require(shutil.disk_usage(output.parent).free >= MAX_OWNED_BYTES + MIN_FREE_BYTES,
            "INITIAL_RESOURCE_RESERVE")
    output.mkdir()
    script = Path(__file__).resolve()
    script_before = script.read_bytes()
    result = {"state": "NOT_RUN", "sourceRun": RUN, "sourceArtifact": ARTIFACT,
              "sourceCommit": SOURCE_COMMIT, "archiveBytes": ARCHIVE_BYTES,
              "archiveSha256": ARCHIVE_SHA256,
              "custodyWorkflowCommit": os.environ.get("GITHUB_SHA"),
              "scope": "Exact original Actions ZIP integrity and transport only. No app rerun/build or package/native/browser acceptance."}
    try:
        token = os.environ.get("GITHUB_TOKEN")
        require(bool(token), "READ_ONLY_ACTIONS_TOKEN_REQUIRED")
        require(os.environ.get("GITHUB_REPOSITORY") == REPOSITORY
                and os.environ.get("GITHUB_REF") == "refs/heads/custody/team-b-write-package-69cff-original-parts500k-20261004",
                "EXACT_CUSTODY_BRANCH_REQUIRED")
        reader = Reader(token)
        prefix = "https://api.github.com/repos/" + REPOSITORY + "/actions/"
        artifact = json.loads(reader.get(prefix + "artifacts/" + str(ARTIFACT), MAX_METADATA_BYTES, True))
        run = json.loads(reader.get(prefix + "runs/" + str(RUN), MAX_METADATA_BYTES, True))
        validate_metadata(artifact, run)
        # Authenticated API request follows only through an explicitly uncredentialed HTTPS fetch.
        body = reader.get(prefix + "artifacts/" + str(ARTIFACT) + "/zip", ARCHIVE_BYTES, True, True)
        members = inspect_archive(body)
        ranges = fragment_ranges(len(body))
        require(len(ranges) == 51, "EXACT_FIFTY_ONE_FRAGMENTS_REQUIRED")
        fragments = [{"part": i + 1, "offset": offset, "bytes": size,
                      "sha256": sha(body[offset:offset + size])}
                     for i, (offset, size) in enumerate(ranges)]
        manifest = {"schema": "team-b-original-actions-zip-transport-v1", **result,
                    "state": "FULL_ORIGINAL_OUTER_ZIP_VERIFIED", "members": members,
                    "fragments": fragments, "artifactExpired": False,
                    "wholeZipBodyCrcAndShaRead": True, "originalOuterByteIdentity": True}
        manifest_raw = (json.dumps(manifest, indent=2) + "\n").encode()
        require(len(manifest_raw) <= MAX_METADATA_BYTES, "MANIFEST_LIMIT")
        write_owned(output, "original.zip", body)
        write_owned(output, "diagnostics/original-transport-manifest.json", manifest_raw)
        wrappers = []
        for fragment in fragments:
            payload = body[fragment["offset"]:fragment["offset"] + fragment["bytes"]]
            part_receipt = (json.dumps({**fragment, "archiveSha256": ARCHIVE_SHA256,
                                       "manifestSha256": sha(manifest_raw)}, indent=2) + "\n").encode()
            memory = io.BytesIO()
            with zipfile.ZipFile(memory, "w", compression=zipfile.ZIP_STORED) as part:
                part.writestr("original-artifact.zip.part", payload)
                part.writestr("original-transport-manifest.json", manifest_raw)
                part.writestr("part-receipt.json", part_receipt)
            wrapper = memory.getvalue()
            require(len(wrapper) <= 32 * 1024 * 1024, "WRAPPER_LIMIT")
            with zipfile.ZipFile(io.BytesIO(wrapper)) as check:
                require(check.read("original-artifact.zip.part") == payload
                        and check.read("original-transport-manifest.json") == manifest_raw
                        and check.read("part-receipt.json") == part_receipt, "WRAPPER_CRC_READBACK")
            relative = "part%02d/original-outer-part%02d.zip" % (fragment["part"], fragment["part"])
            write_owned(output, relative, wrapper)
            wrappers.append({"part": fragment["part"], "path": relative,
                             "bytes": len(wrapper), "sha256": sha(wrapper)})
        require(script.read_bytes() == script_before, "SOURCE_CHANGED")
        result.update(state="PASS_FULL_ORIGINAL_OUTER_ZIP_CUSTODY_FIFTY_ONE_PARTS",
                      members=members, memberCount=len(members), fragments=fragments, wrappers=wrappers,
                      sourceSha256=sha(script_before), ownedBytes=allocation(output),
                      freeBytes=shutil.disk_usage(output).free,
                      manifestSha256=sha(manifest_raw), noAppRuntimeOrSdk=True)
    except Exception as error:
        # Do not expose tokens, redirect URLs, raw exception URL strings or private paths.
        result.update(state="FAIL", errorCode=str(error) if isinstance(error, CustodyError)
                      else type(error).__name__)
    raw = (json.dumps(result, indent=2) + "\n").encode()
    require(len(raw) <= MAX_METADATA_BYTES, "RESULT_LIMIT")
    write_owned(output, "diagnostics/result.json", raw)
    print(json.dumps({"state": result["state"], "sourceRun": RUN, "sourceArtifact": ARTIFACT,
                      "originalSha256": ARCHIVE_SHA256, "parts": len(result.get("wrappers", []))}))
    return 0 if result["state"] == "PASS_FULL_ORIGINAL_OUTER_ZIP_CUSTODY_FIFTY_ONE_PARTS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
