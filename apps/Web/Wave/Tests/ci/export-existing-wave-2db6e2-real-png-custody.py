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
import zlib

REPOSITORY = "CroakyJake12/9to1"
RUN = 37247109783
ARTIFACT = 11319847192
SOURCE_COMMIT = "2db6e230bba48517844b6550da90b9355bf3c1b4"
ARCHIVE_BYTES = 2524194
ARCHIVE_SHA256 = "27221c108a180282ab94176142b99f831f8f8b0613bb44074493f19c728add0c"
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
            "SOURCE_RUN_NOT_FAILED")


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


MAX_CUSTODY_BYTES = 163377
CUSTODY_CHUNK_BYTES = 32768
MAX_CUSTODY_FRAME_BYTES = 65536
EXPECTED_MEMBER_ROWS_SHA256 = "94b34c06ea023943b498348a9651ecb357d2beb7e3c77f07e603d226c53f004e"
SELECTED_MEMBER_PATHS = (
    "wr-run/raw/390-section-0.png",
    "wr-run/raw/390-section-1.png",
    "wr-run/raw/390-section-2.png",
    "wr-run/raw/390-section-3.png",
    "wr-run/raw/390-section-4.png",
    "wr-run/raw/390-section-5.png",
)
REPRESENTATIVES = (
    ("wr-run/raw/390-section-0.png", 81034, "e929a2329f375e96784e7a5950db64aa0a2cec4a1517e760d08055db09e6e850", 2469961220),
    ("wr-run/raw/390-section-3.png", 82343, "40f99c2643bc05fafc20b6cbddf8d69bffec3d3b646cebadc70341ffcb8f529b", 3267296584),
)

def complete_png_custody(body, members, member_rows_sha, source_sha):
    require(len(members) == 58 and member_rows_sha == EXPECTED_MEMBER_ROWS_SHA256,
            "EXACT_ORIGINAL_MEMBER_MAP")
    index = {item["path"]: item for item in members}
    require(len(index) == len(members), "ORIGINAL_MEMBER_INDEX_MISMATCH")
    require(len(SELECTED_MEMBER_PATHS) == 6
            and len(set(SELECTED_MEMBER_PATHS)) == 6, "EXACT_SIX_UNIQUE_PNGS")
    data_by_path = {}
    with zipfile.ZipFile(io.BytesIO(body)) as archive:
        for path in SELECTED_MEMBER_PATHS:
            require(path in index, "EXACT_PNG_ABSENT")
            record = index[path]
            require(not record["directory"] and record["mode"] == 33188,
                    "EXACT_PNG_MODE_MISMATCH")
            data = archive.read(path)
            require(len(data) == record["bytes"] and sha(data) == record["sha256"]
                    and zlib.crc32(data) == record["crc32"], "PNG_FULL_READBACK")
            data_by_path[path] = data
    representatives = []
    offset = 0
    for path, length, digest, crc in REPRESENTATIVES:
        data = data_by_path[path]
        require(len(data) == length and sha(data) == digest and zlib.crc32(data) == crc,
                "PINNED_REPRESENTATIVE_MISMATCH")
        representatives.append({**index[path], "concatOffset": offset})
        offset += length
    require(offset == MAX_CUSTODY_BYTES, "EXACT_COMPLETE_PNG_BYTE_BUDGET")
    aliases = []
    for position, path in enumerate(SELECTED_MEMBER_PATHS):
        representative = representatives[0 if position < 3 else 1]
        require(data_by_path[path] == data_by_path[representative["path"]],
                "FULL_ORIGINAL_PNG_BYTE_EQUALITY")
        aliases.append({**index[path], "representative": representative["path"],
                        "concatOffset": representative["concatOffset"]})
    complete = b"".join(data_by_path[row["path"]] for row in representatives)
    require(len(complete) == MAX_CUSTODY_BYTES, "EXACT_CONCAT_BYTES")
    common = {"schema": "team-b-fixed-failed-wave-complete-png-custody-v1",
              "scope": "Complete six original recorded first-case PNGs via two full-byte-equal representatives; not keyboard-failure-time images or full responsive acceptance.",
              "sourceRun": RUN, "sourceArtifact": ARTIFACT, "sourceCommit": SOURCE_COMMIT,
              "archiveBytes": ARCHIVE_BYTES, "archiveSha256": ARCHIVE_SHA256,
              "custodyWorkflowCommit": os.environ.get("GITHUB_SHA"),
              "sourceSha256": source_sha, "memberCount": len(members),
              "originalMemberRowsSha256": member_rows_sha,
              "wholeOriginalShaAndAllMemberCRCRead": True,
              "completeUniqueBytes": len(complete), "concatSha256": sha(complete),
              "fullOriginalPngBytes": sum(len(value) for value in data_by_path.values()),
              "representatives": representatives, "aliases": aliases, "partCount": 5}
    frames = []
    for position, (start, length) in enumerate(((0, 32768), (32768, 32768), (65536, 32768), (98304, 32768), (131072, 32305)), 1):
        payload = complete[start:start + length]
        require(len(payload) == length and length <= CUSTODY_CHUNK_BYTES,
                "EXACT_PARTITION")
        frame = {**common, "part": position, "offset": start, "bytes": length,
                 "sha256": sha(payload), "base64": base64.b64encode(payload).decode("ascii")}
        raw = (json.dumps(frame, separators=(",", ":")) + "\n").encode()
        require(len(raw) <= MAX_CUSTODY_FRAME_BYTES, "CUSTODY_FRAME_WIRE_LIMIT")
        frames.append(raw)
    require(sum(len(raw) for raw in frames) <= MAX_METADATA_BYTES,
            "CUSTODY_FRAMES_TOTAL_METADATA_LIMIT")
    return common, frames


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
              "scope": "Separate complete recorded first-case PNG custody from a failed run; no app rerun, failure-time screenshot claim, or native/browser acceptance."}
    frames = []
    try:
        token = os.environ.get("GITHUB_TOKEN")
        require(bool(token), "READ_ONLY_ACTIONS_TOKEN_REQUIRED")
        require(os.environ.get("GITHUB_REPOSITORY") == REPOSITORY
                and os.environ.get("GITHUB_REF") == "refs/heads/custody/team-b-wave-2db6e2-real-png-custody-20261005",
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
        require(len(member_raw) <= MAX_CUSTODY_FRAME_BYTES, "MEMBER_MANIFEST_WIRE_LIMIT")
        member_rows_sha = sha(json.dumps(members, sort_keys=True, separators=(",", ":")).encode())
        write_owned(output, "diagnostics/original-members.json", member_raw)
        print("ORIGINAL_MEMBERS: " + member_raw.decode("utf-8").rstrip("\n"), flush=True)
        common, frames = complete_png_custody(body, members, member_rows_sha, sha(script_before))
        for position, raw in enumerate(frames, 1):
            write_owned(output, "diagnostics/complete-png-part%02d.json" % position, raw)
        require(script.read_bytes() == script_before, "SOURCE_CHANGED")
        result.update(state="PASS_COMPLETE_ORIGINAL_PNG_CUSTODY",
                      completeUniqueBytes=common["completeUniqueBytes"],
                      concatSha256=common["concatSha256"],
                      fullOriginalPngBytes=common["fullOriginalPngBytes"],
                      partCount=5, partReceiptSha256=[sha(raw) for raw in frames],
                      partWireBytes=[len(raw) for raw in frames],
                      originalMemberRowsSha256=member_rows_sha,
                      memberCount=len(members), sourceSha256=sha(script_before),
                      ownedBytes=allocation(output), freeBytes=shutil.disk_usage(output).free)
    except Exception as error:
        result.update(state="FAIL", errorCode=str(error) if isinstance(error, CustodyError)
                      else type(error).__name__)
    raw = (json.dumps(result, indent=2) + "\n").encode()
    require(len(raw) <= MAX_METADATA_BYTES, "RESULT_LIMIT")
    write_owned(output, "diagnostics/result.json", raw)
    if result["state"] == "PASS_COMPLETE_ORIGINAL_PNG_CUSTODY":
        for frame in frames:
            print("COMPLETE_PNG_CUSTODY_PART: " + frame.decode("utf-8").rstrip("\n"))
    print(json.dumps({"state": result["state"], "sourceRun": RUN, "sourceArtifact": ARTIFACT,
                      "originalSha256": ARCHIVE_SHA256,
                      "errorCode": result.get("errorCode"),
                      "completeUniqueBytes": result.get("completeUniqueBytes"),
                      "concatSha256": result.get("concatSha256")}))
    return 0 if result["state"] == "PASS_COMPLETE_ORIGINAL_PNG_CUSTODY" else 1


if __name__ == "__main__":
    raise SystemExit(main())
