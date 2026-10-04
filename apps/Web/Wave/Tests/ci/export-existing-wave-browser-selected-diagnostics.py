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
RUN = 37230828986
ARTIFACT = 11313603170
SOURCE_COMMIT = "e90c6fab21aebd27cb7d8f330022414a889be8df"
ARCHIVE_BYTES = 1495261
ARCHIVE_SHA256 = "e794fb5e3ab92b8019876f14bb908c340bd0a88be1be53a585ac36122b35fcc7"
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
EXPECTED_ORIGINAL_MEMBER_COUNT = 36
EXPECTED_ORIGINAL_MEMBER_ROWS_SHA256 = "3197a6839bd8bbeb98b415c432a33a7fe6b5dcb0e3687266b503c6e8ff4c2446"
SELECTED_MEMBERS = (
    ("wr-run/diagnostics/status-before.log", 538, "932916bb1844b0abc740ac4b810c65e43674e3107a055e800b7aa739c8ac232a", 1628630904, 33188),
    ("wr-run/diagnostics/status-after.log", 538, "932916bb1844b0abc740ac4b810c65e43674e3107a055e800b7aa739c8ac232a", 1628630904, 33188),
    ("wr-run/diagnostics/commands.json", 2517, "dfb9a95a853a6cdf36143c08120a5c00134acbdc0e02d68dafbb0e6e2f118e47", 3715577370, 33188),
    ("wr-publish/diagnostics/owner-built-assets.json", 1272, "3cba4f74f796c7f0637d64ca1313e36928caa6fa2625d1902d7f8e379e20b1a2", 4050175919, 33188),
    ("wr-publish/diagnostics/commands.json", 18499, "b9d275ffa0e9954a6329e40e2e58b63df97480a23d13df08acbc2b71b7b9ff30", 3098900699, 33188),
)

def selected_diagnostics(body, members):
    require(len(members) == EXPECTED_ORIGINAL_MEMBER_COUNT,
            "EXACT_ORIGINAL_MEMBER_COUNT")
    member_raw = json.dumps(members, sort_keys=True, separators=(",", ":")).encode()
    require(sha(member_raw) == EXPECTED_ORIGINAL_MEMBER_ROWS_SHA256,
            "EXACT_ORIGINAL_MEMBER_ROWS")
    require(len(SELECTED_MEMBERS) == 5
            and len({item[0] for item in SELECTED_MEMBERS}) == 5,
            "EXACT_FIVE_UNIQUE_DIAGNOSTICS")
    require(sum(item[1] for item in SELECTED_MEMBERS) == 23364
            and sum(item[1] for item in SELECTED_MEMBERS) <= MAX_SELECTED_DECODED_BYTES,
            "SELECTED_DECODED_BYTE_LIMIT")
    index = {item["path"]: item for item in members}
    require(len(index) == len(members), "ORIGINAL_MEMBER_INDEX_MISMATCH")
    selected = []
    with zipfile.ZipFile(io.BytesIO(body)) as archive:
        for path, size, expected_sha, crc, mode in SELECTED_MEMBERS:
            require(path in index, "EXACT_DIAGNOSTIC_MEMBER_ABSENT")
            record = index[path]
            require(not record["directory"] and record["bytes"] == size
                    and record["sha256"] == expected_sha and record["crc32"] == crc
                    and record["mode"] == mode, "EXACT_DIAGNOSTIC_BINDING_MISMATCH")
            data = archive.read(path)
            require(len(data) == size and sha(data) == expected_sha,
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
              "scope": "Exact failed original ZIP and five literal source-diagnostic bodies only; no app rerun or browser acceptance."}
    try:
        token = os.environ.get("GITHUB_TOKEN")
        require(bool(token), "READ_ONLY_ACTIONS_TOKEN_REQUIRED")
        require(os.environ.get("GITHUB_REPOSITORY") == REPOSITORY
                and os.environ.get("GITHUB_REF") == "refs/heads/custody/team-b-wave-browser-e90-selected-diagnostics-20261004",
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
        selected = selected_diagnostics(body, members)
        receipt = {"schema": "team-b-fixed-failed-source-diagnostics-v1",
                   **result, "state": "VERIFIED_SELECTED_DIAGNOSTICS",
                   "memberCount": len(members),
                   "originalMemberRowsSha256": EXPECTED_ORIGINAL_MEMBER_ROWS_SHA256,
                   "wholeOriginalShaAndAllMemberCRCRead": True,
                   "selectedDecodedBytes": sum(item["bytes"] for item in selected),
                   "selected": selected, "sourceSha256": sha(script_before)}
        receipt_raw = (json.dumps(receipt, separators=(",", ":")) + "\n").encode()
        require(len(receipt_raw) <= MAX_SELECTED_WIRE_BYTES, "SELECTED_WIRE_BYTE_LIMIT")
        member_raw = (json.dumps(members, indent=2) + "\n").encode()
        require(len(member_raw) <= MAX_METADATA_BYTES, "MEMBER_MANIFEST_LIMIT")
        write_owned(output, "diagnostics/original-members.json", member_raw)
        write_owned(output, "diagnostics/selected-source-diagnostics.json", receipt_raw)
        require(script.read_bytes() == script_before, "SOURCE_CHANGED")
        result.update(state="PASS_SELECTED_ORIGINAL_DIAGNOSTIC_CUSTODY",
                      selectedDecodedBytes=23364, selectedMemberCount=5,
                      selectedWireBytes=len(receipt_raw), selectedReceiptSha256=sha(receipt_raw),
                      originalMemberRowsSha256=EXPECTED_ORIGINAL_MEMBER_ROWS_SHA256,
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
