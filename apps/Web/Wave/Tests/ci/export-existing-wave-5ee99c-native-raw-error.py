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
RUN = 37245186078
ARTIFACT = 11318772209
SOURCE_COMMIT = "5ee99c19d42e9e6ae687c7ef528fd947a8828540"
ARCHIVE_BYTES = 1113997
ARCHIVE_SHA256 = "1986b484e7c37c7b117044be46255c49e4d21358d4ca51c95aad6f6152d85f21"
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


MAX_SELECTED_DECODED_BYTES = 32768
MAX_SELECTED_WIRE_BYTES = 65536
SELECTED_MEMBER_PATHS = (
    "narrow.log",
    "narrow-native-font2.log",
    "geometry-stress195-not-zoom.log",
    "wide.log",
    "default-narrow.log",
    "default-narrow-native-font2.log",
    "default-wide.log",
)
UNIT_NAMES = (
    "real-production-factory-font1-padding-allocation",
    "real-production-factory-font2-padding-allocation",
    "real-native-font1-padding0-nonblank-clip-rejected",
    "real-native-own-height-nonblank-clip-rejected",
    "real-native-trailing-space-advance-is-not-ink-clipping",
    "unsupported-render-transform-fails-closed",
    "unsupported-nonrectangular-clip-fails-closed",
)
INITIAL_NAMES = (
    "Actual native fonts equal requested baseline factor",
    "Actual native client is requested width",
    "Outer policy is opt-in only",
)
OPT_IN_NAMES = (
    "Wave native outer extent fits viewport",
    "Every actual Wave button fits horizontally",
    "Every actual Wave input fits horizontally",
    "Every full native Wave glyph range is retained",
    "Every full native Wave glyph fits assigned bounds",
    "Every full native Wave text stays inside root",
    "Actual wrapped siblings never overlap",
    "Canonical label projections are complete",
    "Native controls keep usable focus and names",
    "Deliberate waveform keeps local horizontal scrolling",
)
UNIT_PREFIX = b"CRITERION_UNIT: "

def selected_diagnostics(body, members):
    require(len(SELECTED_MEMBER_PATHS) == 7
            and len(set(SELECTED_MEMBER_PATHS)) == 7, "EXACT_SEVEN_UNIQUE_LOGS")
    index = {item["path"]: item for item in members}
    require(len(index) == len(members), "ORIGINAL_MEMBER_INDEX_MISMATCH")
    logs = []
    with zipfile.ZipFile(io.BytesIO(body)) as archive:
        for path in SELECTED_MEMBER_PATHS:
            require(path in index, "EXACT_NATIVE_LOG_ABSENT")
            record = index[path]
            require(not record["directory"] and record["mode"] == 33188,
                    "EXACT_NATIVE_LOG_MODE_MISMATCH")
            data = archive.read(path)
            require(len(data) == record["bytes"] and sha(data) == record["sha256"],
                    "NATIVE_LOG_FULL_READBACK")
            logs.append((record, data))
    # This admission map survives a later size/shape refusal. No UNIT or layout
    # acceptance is inferred from its sizes, hashes, or the successful producer.
    admission = {"scope": "Seven full original log identities only; selection not yet accepted.",
                 "fullLogDecodedBytes": sum(len(data) for _, data in logs),
                 "logs": [record for record, _ in logs]}
    print("NATIVE_LOG_ADMISSION: " + json.dumps(admission, separators=(",", ":")), flush=True)
    full_bytes = sum(len(data) for _, data in logs)
    if full_bytes <= MAX_SELECTED_DECODED_BYTES:
        return {"mode": "FULL_ORIGINAL_LOGS", "decodedBytes": full_bytes,
                "records": [{**record, "base64": base64.b64encode(data).decode("ascii")}
                            for record, data in logs]}
    # Preserve exact source bytes rather than reserializing parsed JSON. Prefix
    # and LF/CRLF framing are explicitly reconstructible, not omitted evidence.
    sliced = []
    decoded_bytes = 0
    unit_count = check_count = 0
    for record, data in logs:
        offset = 0
        slices, unit_names, check_names = [], [], []
        for line in data.splitlines(keepends=True):
            if line.endswith(b"\r\n"):
                content, ending = line[:-2], "\r\n"
            elif line.endswith(b"\n"):
                content, ending = line[:-1], "\n"
            else:
                content, ending = line, ""
            if content.startswith(UNIT_PREFIX):
                kind = "criterion-unit"
                prefix, payload = UNIT_PREFIX, content[len(UNIT_PREFIX):]
                unit = json.loads(payload.decode("utf-8"))
                require(isinstance(unit, dict) and set(unit) == {"name", "passed", "evidence"}
                        and type(unit["passed"]) is bool, "UNIT_ORIGINAL_JSON_SHAPE")
                unit_names.append(unit["name"])
            else:
                kind = "geometry-check"
                prefix, payload = b"", content
                text = payload.decode("utf-8")
                require(text.startswith("PASS: ") or text.startswith("FAIL: "),
                        "UNEXPECTED_NATIVE_LOG_LINE")
                check_names.append(text[6:])
            item = {"kind": kind, "byteOffset": offset + len(prefix),
                    "bytes": len(payload), "sha256": sha(payload),
                    "base64": base64.b64encode(payload).decode("ascii"),
                    "lineByteOffset": offset, "lineBytes": len(line),
                    "lineSha256": sha(line), "prefix": prefix.decode("ascii"),
                    "lineEnding": ending}
            reconstructed = prefix + payload + ending.encode("ascii")
            require(reconstructed == line
                    and data[item["byteOffset"]:item["byteOffset"] + item["bytes"]] == payload,
                    "EXACT_LINE_OFFSET_PROOF")
            slices.append(item)
            decoded_bytes += len(payload)
            offset += len(line)
        require(offset == len(data) and tuple(unit_names) == UNIT_NAMES,
                "EXACT_SEVEN_ORIGINAL_UNIT_NAMES")
        expected = INITIAL_NAMES + (("Default owner host behavior is unchanged",)
                                   if record["path"].startswith("default-") else OPT_IN_NAMES)
        require(tuple(check_names) == expected, "EXACT_ORIGINAL_GEOMETRY_NAMES")
        unit_count += len(unit_names)
        check_count += len(check_names)
        sliced.append({**record, "slices": slices})
    require(unit_count == 49 and check_count == 64, "EXACT_RAW_LINE_SET")
    require(decoded_bytes <= MAX_SELECTED_DECODED_BYTES, "SELECTED_DECODED_BYTE_LIMIT")
    return {"mode": "EXACT_ORIGINAL_LINE_SLICES", "decodedBytes": decoded_bytes,
            "unitLines": unit_count, "geometryLines": check_count, "records": sliced}


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
              "archiveSha256": ARCHIVE_SHA256, "expectedSourceConclusion": "success",
              "custodyWorkflowCommit": os.environ.get("GITHUB_SHA"),
              "scope": "Exact successful proposed native ZIP and seven literal native logs or original byte slices only; no app rerun or browser acceptance."}
    try:
        token = os.environ.get("GITHUB_TOKEN")
        require(bool(token), "READ_ONLY_ACTIONS_TOKEN_REQUIRED")
        require(os.environ.get("GITHUB_REPOSITORY") == REPOSITORY
                and os.environ.get("GITHUB_REF") == "refs/heads/custody/team-b-wave-native-5ee99c-raw-error-20261005",
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
        selection = selected_diagnostics(body, members)
        selected = selection["records"]
        receipt = {"schema": "team-b-fixed-success-native-raw-diagnostics-v1",
                   **result, "state": "VERIFIED_SELECTED_DIAGNOSTICS",
                   "memberCount": len(members),
                   "originalMemberRowsSha256": member_rows_sha,
                   "wholeOriginalShaAndAllMemberCRCRead": True,
                   "selectedDecodedBytes": selection["decodedBytes"],
                   "selectionMode": selection["mode"],
                   "selected": selected, "sourceSha256": sha(script_before)}
        receipt_raw = (json.dumps(receipt, separators=(",", ":")) + "\n").encode()
        require(len(receipt_raw) <= MAX_SELECTED_WIRE_BYTES, "SELECTED_WIRE_BYTE_LIMIT")
        write_owned(output, "diagnostics/selected-source-diagnostics.json", receipt_raw)
        require(script.read_bytes() == script_before, "SOURCE_CHANGED")
        result.update(state="PASS_SELECTED_ORIGINAL_DIAGNOSTIC_CUSTODY",
                      selectedDecodedBytes=selection["decodedBytes"], selectedMemberCount=7,
                      selectionMode=selection["mode"],
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
                      "errorCode": result.get("errorCode"),
                      "selectedDecodedBytes": result.get("selectedDecodedBytes", 0)}))
    return 0 if result["state"] == "PASS_SELECTED_ORIGINAL_DIAGNOSTIC_CUSTODY" else 1


if __name__ == "__main__":
    raise SystemExit(main())
