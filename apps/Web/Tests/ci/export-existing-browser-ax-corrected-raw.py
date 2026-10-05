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
RUN = 37256175805
ARTIFACT = 11322567628
SOURCE_COMMIT = "2e0af7715b74c5ce016016332ae95153d07e6ded"
ARCHIVE_BYTES = 335518
ARCHIVE_SHA256 = "8b5f721372b06ef99bd4c0a9d5ddb21415377a3add89c115aa4f857317fe6878"
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
            "SOURCE_RUN_NOT_SUCCEEDED")


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
SELECTED_MEMBER_PATHS = ("ax-corrected17.log", "source-before.json", "source-after.json", "result.json", "corrected-unit-results.json")
ORIGINAL_INPUT_PINS = {"apps/Web/Tests/browser-accessibility.test.mjs":{"bytes":13999,"sha256":"51c9add33de51aecd172ebd3a72a8d54f8b2c9a1bf0cfa3ed267c81da4c9926d"},"apps/Web/wwwroot/browser-accessibility.js":{"bytes":9055,"sha256":"1ab270bbc0fbe648f37495415ce550e09eec0c940280abb980389cb6c21066bc"},"apps/Web/Tests/ci/run-ordinary-native.py":{"bytes":19584,"sha256":"a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38"},"apps/Web/Tests/ci/run-browser-ax-corrected17.py":{"bytes":6819,"sha256":"3b3edfb80f46518340dd8c2c9c076cec08aa269c6e093991485d07f2c12e631b"},".github/workflows/team-b-browser-ax-corrected17.yml":{"bytes":1402,"sha256":"15ee72038f7d1721e4c60c85d8fe3ef17d67a2940429ec131c82114178c4c6b3"}}
EXPECTED_CHECKS = [["native-nonfocusable-remains-out-of-tab-sequence","PASS"],["initial-snapshot-failure-removes-root-and-timer","PASS"],["stable-peers-follow-current-native-order-and-retain-focus","PASS"],["disposed-retained-dom-controls-stop-forwarding-operations","PASS"],["native-host-tab-follows-current-peer-focus-and-skips-disabled","PASS"],["native-host-keeps-other-keys-modifiers-and-empty-view-egress","PASS"],["disposed-native-tab-listener-stops-routing-and-restores-host","PASS"],["removed-native-focusability-guard","EXPECTED_FAILURE_DETECTED"],["restored-timer-before-failed-snapshot","EXPECTED_FAILURE_DETECTED"],["removed-native-order-reconciliation","EXPECTED_FAILURE_DETECTED"],["removed-disposed-event-forwarding-guards","EXPECTED_FAILURE_DETECTED"],["removed-native-tab-arbitration-subscription","EXPECTED_FAILURE_DETECTED"],["removed-actual-native-focus-order","EXPECTED_FAILURE_DETECTED"],["released-projected-claim-refused-focus-does-not-restore-after-reorder","PASS"],["different-current-native-focus-does-not-restore-old-dom-focus-after-reorder","PASS"],["removed-current-projected-claim-ownership","EXPECTED_FAILURE_DETECTED"],["removed-matching-current-native-focus-guard","EXPECTED_FAILURE_DETECTED"]]

def selected_diagnostics(body, members):
    index = {record["path"]: record for record in members}
    require(len(index) == len(members), "ORIGINAL_MEMBER_INDEX_MISMATCH")
    selected, data_by_path = [], {}
    decoded_bytes = 0
    with zipfile.ZipFile(io.BytesIO(body)) as archive:
        for path in SELECTED_MEMBER_PATHS:
            require(path in index, "EXACT_SELECTED_MEMBER_ABSENT")
            record = index[path]
            require(not record["directory"] and record["mode"] == 33188,
                    "EXACT_SELECTED_MEMBER_MODE")
            data = archive.read(path)
            require(len(data) == record["bytes"] and sha(data) == record["sha256"]
                    and zlib.crc32(data) == record["crc32"], "SELECTED_FULL_READBACK")
            decoded_bytes += len(data)
            require(decoded_bytes <= MAX_SELECTED_DECODED_BYTES, "SELECTED_DECODED_BYTE_LIMIT")
            data_by_path[path] = data
            selected.append({**record, "base64": base64.b64encode(data).decode("ascii")})
    before = json.loads(data_by_path["source-before.json"])
    after = json.loads(data_by_path["source-after.json"])
    require(before == after == ORIGINAL_INPUT_PINS, "ORIGINAL_INPUT_CUSTODY_MISMATCH")
    original = json.loads(data_by_path["result.json"])
    require(original.get("state") == "PASS_FAITHFUL17_MOCK_CONTROLS"
            and original.get("sourceCommit") == SOURCE_COMMIT
            and original.get("productBaseCommit") == "caa6712bd5517db2458fcaf8f23e6c97d912f958"
            and original.get("expectedChecks") == original.get("actualChecks") == 17
            and "failure" not in original and "integrityFailure" not in original,
            "EXACT_CORRECTED_UNIT_SCOPE")
    report = json.loads(data_by_path["corrected-unit-results.json"])
    expected_checks = [{"name": name, "result": result} for name, result in EXPECTED_CHECKS]
    require(report.get("checks") == expected_checks and report.get("productParityVerified") is False
            and report.get("sourcePath") == "apps/Web/wwwroot/browser-accessibility.js"
            and report.get("sourceSha256") == ORIGINAL_INPUT_PINS["apps/Web/wwwroot/browser-accessibility.js"]["sha256"]
            and report.get("runnerSha256") == ORIGINAL_INPUT_PINS["apps/Web/Tests/browser-accessibility.test.mjs"]["sha256"],
            "EXACT_SEVENTEEN_NAMED_UNIT_RESULTS")
    require(json.loads(data_by_path["ax-corrected17.log"]) == expected_checks,
            "EXACT_RAW_UNIT_OUTPUT_DIFFERS")
    commands = original.get("commands")
    names = ["git-head", "git-parent", "status-before", "tracked-tree",
             "node-version", "ax-corrected17", "status-after"]
    require(isinstance(commands, list) and [row.get("name") for row in commands] == names,
            "EXACT_ORIGINAL_COMMAND_SET")
    for row in commands:
        expected_exit = 0
        require(row.get("exit") == row.get("exitAfterDrain") == expected_exit
                and row.get("normalEOF") is True and row.get("familyClosed") is True
                and row.get("finalECHILD") is True and row.get("signals") == []
                and row.get("error") is None and row.get("births")
                and all(birth.get("gone") is True for birth in row["births"]),
                "ORIGINAL_COMMAND_DRAIN_NOT_NORMAL")
    require(commands[2].get("bytes") == commands[6].get("bytes") == 0
            and commands[5].get("bytes") == len(data_by_path["ax-corrected17.log"]),
            "ORIGINAL_STATUS_OR_LOG_COUNT")
    return {"mode": "COMPLETE_CORRECTED_SEVENTEEN_UNIT_FILES", "decodedBytes": decoded_bytes,
            "records": selected}


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
              "unitCountBeforeRead": "UNKNOWN",
              "custodyWorkflowCommit": os.environ.get("GITHUB_SHA"),
              "scope": "Existing corrected AX17 complete unit evidence only; no app rerun or native/browser/provider acceptance."}
    try:
        token = os.environ.get("GITHUB_TOKEN")
        require(bool(token), "READ_ONLY_ACTIONS_TOKEN_REQUIRED")
        require(os.environ.get("GITHUB_REPOSITORY") == REPOSITORY
                and os.environ.get("GITHUB_REF") == "refs/heads/custody/team-b-browser-ax-caa6712-corrected-raw-20261005",
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
        receipt = {"schema": "team-b-fixed-corrected-ax17-unit-custody-v1",
                   **result, "state": "VERIFIED_CORRECTED_UNIT_DIAGNOSTICS",
                   "memberCount": len(members),
                   "originalMemberRowsSha256": member_rows_sha,
                   "wholeOriginalShaAndAllMemberCRCRead": True,
                   "selectedDecodedBytes": selection["decodedBytes"],
                   "selectionMode": selection["mode"],
                   "selected": selected, "sourceSha256": sha(script_before)}
        receipt_raw = (json.dumps(receipt, separators=(",", ":")) + "\n").encode()
        require(len(receipt_raw) <= MAX_SELECTED_WIRE_BYTES, "SELECTED_WIRE_BYTE_LIMIT")
        write_owned(output, "diagnostics/selected-original-failure.json", receipt_raw)
        require(script.read_bytes() == script_before, "SOURCE_CHANGED")
        result.update(state="PASS_CORRECTED_UNIT_CUSTODY",
                      selectedDecodedBytes=selection["decodedBytes"], selectedMemberCount=5,
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
    if result["state"] == "PASS_CORRECTED_UNIT_CUSTODY":
        print("SELECTED_DIAGNOSTICS: " + receipt_raw.decode("utf-8").rstrip("\n"))
    print(json.dumps({"state": result["state"], "sourceRun": RUN, "sourceArtifact": ARTIFACT,
                      "originalSha256": ARCHIVE_SHA256,
                      "errorCode": result.get("errorCode"),
                      "selectedDecodedBytes": result.get("selectedDecodedBytes")}))
    return 0 if result["state"] == "PASS_CORRECTED_UNIT_CUSTODY" else 1


if __name__ == "__main__":
    raise SystemExit(main())
