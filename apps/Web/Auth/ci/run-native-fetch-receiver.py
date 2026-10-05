#!/usr/bin/env python3
"""SOURCE proposal: strict paired receiver UNIT controls; no issuer/provider/SDK.
Imports the actual pinned a57 Commands. PATCH is not a rebuilt bundle or native
Window/browser/lifetime acceptance. Full original failing TAP remains evidence.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import stat
import sys
import time

sys.dont_write_bytecode = True
ORIGINAL_COMMIT = "5695619c2cf97af4b8f73e1e019ddb575335e0fd"
COMMON_SHA = "a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38"
COMMON_PATH = "apps/Web/Tests/ci/run-ordinary-native.py"
NODE_VERSION = "v22.14.0"
MAX_OWNED = 32 * 1024 * 1024
MIN_FREE = 256 * 1024 * 1024
FINAL_RESERVE = 2 * 1024 * 1024
MAX_FRAME = 65536
CURRENT_ROWS = [
  {
    "path": "apps/Web/Tests/ci/run-ordinary-native.py",
    "bytes": 19584,
    "sha256": "a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38",
    "git": "8289a7b0ade290c3a34f2f81eacf67d760da6cb0"
  },
  {
    "path": "apps/Web/package.json",
    "bytes": 80,
    "sha256": "29314346bb27cb4f6128f30e6532053f056c1aafcb3e8c61ef3ad77d0df45d7c",
    "git": "36a581358635321e66005c80571cc0aeb6839dc4"
  },
  {
    "path": "apps/Web/Auth/browser-public-client.js",
    "bytes": 18256,
    "sha256": "d2aa74927617e1b33b87c5637803784e8da9b14026e70d1d25386571caaf471c",
    "git": "7fe8ac7483edcf9f98541cbe44821dadbcaed982"
  },
  {
    "path": "apps/Web/Auth/account-api-client.js",
    "bytes": 14223,
    "sha256": "cede4b8df0b0664325e3f409bc714aa9aa414742d6011e119d95f47b62561219",
    "git": "97fa18c84cfb3cfc7ef35d96a9bd7035bc687bb5"
  },
  {
    "path": "apps/Web/Auth/configured-accounts.bundle.js",
    "bytes": 74883,
    "sha256": "7d0506d41d5182aad283847e0d7dde073c521a072640447530cde6180885d082",
    "git": "5b68332f3d010402b07e34c02b01f7ea8621005e"
  },
  {
    "path": "apps/Web/Auth/configured-accounts.js",
    "bytes": 8176,
    "sha256": "70c5f9335c391542e26e9446c9c0b1ea9c49199a7c7bacb17e175e2b22d7467f",
    "git": "915b0307178baf90034a3a35e449502a9b50e074"
  },
  {
    "path": "apps/Web/Services/account-service.js",
    "bytes": 3214,
    "sha256": "2ca8fab3b99209fb0fcdc05105fb48a81bbe8158b54156685a7c2b4a609ccab9",
    "git": "744e4ea15093c5d17d755599e3aadd420921df18"
  },
  {
    "path": "apps/Web/Auth/account-api-client.test.js",
    "bytes": 16450,
    "sha256": "71808f781a6ade29163884985512a7b245cba58f1bdc26eaa97175ae758a6e23",
    "git": "f23045e44f5a7da124a5d74eced6d586e841b954"
  },
  {
    "path": "apps/Web/Auth/account-api-client.profile-review.test.js",
    "bytes": 5018,
    "sha256": "7e6fac43d38cea9b498fdeb9441a24d102376f390febc18a97264c7bc4bb6af8",
    "git": "1bd48d0cae918a89b43f80d2004cea99360d86c8"
  },
  {
    "path": "apps/Web/Auth/browser-public-client.test.js",
    "bytes": 9625,
    "sha256": "d60dbca32da6bea4e4d794e5aa6e2802a5f1ab3282e70cb77b95405f030891e2",
    "git": "fff0ce734254f4beb0f8c64d68a8759dce0ea21b"
  },
  {
    "path": "apps/Web/Services/account-service.test.js",
    "bytes": 3769,
    "sha256": "597294730babb72cc0a919c41496bf7601f5167b53cdc7bd7e5c7acc168f3e70",
    "git": "87dd25a1f94a3c2a6efa93b9ac91bf037d683f32"
  },
  {
    "path": "apps/Web/Auth/fetch-receiver.test.js",
    "bytes": 8652,
    "sha256": "f299e8c4e9a38cb9dbd39f3732fbf573843a29eed20945e562c128987ee82e45",
    "git": "806837353b9d58b40c8ecb01594412f62f4703e7"
  }
]
ORIGINAL_ROWS = [
  {
    "path": "apps/Web/package.json",
    "bytes": 80,
    "sha256": "29314346bb27cb4f6128f30e6532053f056c1aafcb3e8c61ef3ad77d0df45d7c",
    "git": "36a581358635321e66005c80571cc0aeb6839dc4"
  },
  {
    "path": "apps/Web/Auth/browser-public-client.js",
    "bytes": 18092,
    "sha256": "18d7885bc847fb969a1d56dda8639106b103bd51abbfdcbc400308926d107697",
    "git": "3d0588ae6ae112b05036df6594d5cceed8dc6f3b"
  },
  {
    "path": "apps/Web/Auth/account-api-client.js",
    "bytes": 14059,
    "sha256": "305d81f13520617d20e42e6832a7ed2436d395fa8624de567d6dcc21cec50f72",
    "git": "832113384783d15eb9622636dbd370fe0df9daf5"
  },
  {
    "path": "apps/Web/Auth/configured-accounts.bundle.js",
    "bytes": 74759,
    "sha256": "4d8801b4be087837763c3a23f117c74b4fd73a290a4ef6e7c2eaf8c4eab396d3",
    "git": "63820d7b4a8a833285070dec65833aee446e6e58"
  },
  {
    "path": "apps/Web/Auth/configured-accounts.js",
    "bytes": 8176,
    "sha256": "70c5f9335c391542e26e9446c9c0b1ea9c49199a7c7bacb17e175e2b22d7467f",
    "git": "915b0307178baf90034a3a35e449502a9b50e074"
  },
  {
    "path": "apps/Web/Services/account-service.js",
    "bytes": 3214,
    "sha256": "2ca8fab3b99209fb0fcdc05105fb48a81bbe8158b54156685a7c2b4a609ccab9",
    "git": "744e4ea15093c5d17d755599e3aadd420921df18"
  },
  {
    "path": "apps/Web/Auth/account-api-client.test.js",
    "bytes": 16450,
    "sha256": "71808f781a6ade29163884985512a7b245cba58f1bdc26eaa97175ae758a6e23",
    "git": "f23045e44f5a7da124a5d74eced6d586e841b954"
  },
  {
    "path": "apps/Web/Auth/account-api-client.profile-review.test.js",
    "bytes": 5018,
    "sha256": "7e6fac43d38cea9b498fdeb9441a24d102376f390febc18a97264c7bc4bb6af8",
    "git": "1bd48d0cae918a89b43f80d2004cea99360d86c8"
  },
  {
    "path": "apps/Web/Auth/browser-public-client.test.js",
    "bytes": 9625,
    "sha256": "d60dbca32da6bea4e4d794e5aa6e2802a5f1ab3282e70cb77b95405f030891e2",
    "git": "fff0ce734254f4beb0f8c64d68a8759dce0ea21b"
  },
  {
    "path": "apps/Web/Services/account-service.test.js",
    "bytes": 3769,
    "sha256": "597294730babb72cc0a919c41496bf7601f5167b53cdc7bd7e5c7acc168f3e70",
    "git": "87dd25a1f94a3c2a6efa93b9ac91bf037d683f32"
  }
]
RECEIVER_NAMES = [
  "broker omitted default fetch uses Window receiver and preserves denial/policy",
  "account API omitted default fetch uses Window receiver and preserves exact wire/policy",
  "broker explicit current global fetch uses Window receiver and preserves denial/policy",
  "account API explicit current global fetch uses Window receiver and preserves exact wire/policy",
  "configured composition forwards its native default without changing cleanup or granting identity",
  "broker captures native transport and receiver before later global replacement",
  "broker preserves injected non-global transport receiver and policy",
  "broker native-default receiver binding preserves caller cancellation",
  "account API captures native transport and receiver before later global replacement",
  "account API preserves injected non-global transport receiver and policy",
  "account API native-default receiver binding preserves caller cancellation"
]
REGRESSION_NAMES = [
  "requires explicit secure origin and cleanup/token boundaries",
  "current account preserves exact wire body and uses protected fetch policy",
  "profile patch preserves omitted fields and explicit nullable clears; no client entitlement body",
  "source conflict and error codes remain unchanged, with revision; no mutation retries",
  "malformed success, invalid identity/schema and invented success are rejected",
  "session list retains source fields and rejects mixed-account or duplicate identities",
  "session mutations clear prior context and exactly use source routes/status",
  "revoked/expired server session invalidates other pending requests and preserves source failure",
  "explicit account/organisation switch drops late private result",
  "cancelled token acquisition and fetch never become successful data",
  "transport errors and missing authentication never log/expose tokens or retry writes",
  "failed cleanup blocks mutation before any dispatch",
  "session mutations bind original token before cleanup can replace/remove token context",
  "concurrent context switch during asynchronous session cleanup prevents dispatch",
  "invalid original token cannot be replaced by a new account token during cleanup",
  "401 from previous context cannot surface after an additional switch while reading its body",
  "real-fetch successful body cancellation remains Cancelled instead of malformed JSON",
  "401 and missing token preserve cleanup failure distinctly",
  "redirect responses and foreign response URL are rejected",
  "streamed real-fetch 401 body survives invalidation; explicit body-phase abort is cancelled",
  "real fetch loopback redirect does not transmit bearer to second origin (transport fixture, not issuer acceptance)",
  "profile update snapshots scalar fields before deferred token acquisition",
  "post-call invalid fields and values cannot enter a validated profile patch",
  "profile success must return exactly expected revision plus one",
  "profile revision rejects unrepresentable next integer before token or network work",
  "sequential server CAS success preserves wire body at normal and safe integer upper bounds",
  "profile patch validates the same single scalar snapshot used for dispatch",
  "caller serialization hooks cannot replace the validated profile fields",
  "null host configuration retains unavailable transport and no sign-in authority",
  "invalid remote HTTP/cross-origin callback/client secret config fails before network",
  "configured but tokenless actions require proven private cleanup and never grant identity",
  "unconfigured private lifetime invalidation awaits owner cleanup and remains unavailable",
  "configured private lifetime invalidation aborts pending sign-in and preserves prepared ability",
  "callback forwards only to same-origin parent and strips one-use code without running app",
  "discovery issuer mismatch rejects and closes owned popup with cleanup",
  "fresh S256/state/nonce and wrong state reject before code exchange",
  "foreign source and origin cannot consume actual pending callback",
  "caller cancellation closes popup and leaves no access token",
  "duplicate callback cannot exchange code twice and unsigned token never grants account",
  "unconfigured bridge reports unavailable and unknown actions never dispatch",
  "bridge forwards reviewed current/profile CAS replies without a parallel model",
  "bridge cancellation and disposal discard late responses and close future calls",
  "bridge preserves original-token mutation through private-view cleanup",
  "controller registration precedes synchronous token-supplier cancellation"
]
ORIGINAL_PASS_NAMES = {
    "broker preserves injected non-global transport receiver and policy",
    "account API preserves injected non-global transport receiver and policy",
}
REGRESSION_FILES = [
    "apps/Web/Auth/account-api-client.test.js",
    "apps/Web/Auth/account-api-client.profile-review.test.js",
    "apps/Web/Auth/browser-public-client.test.js",
    "apps/Web/Services/account-service.test.js",
]

def require(condition):
    if not condition:
        raise RuntimeError("RECEIVER_UNIT_GATE")

def sha_bytes(body):
    return hashlib.sha256(body).hexdigest()

def blob_bytes(body):
    return hashlib.sha1(("blob " + str(len(body)) + "\0").encode() + body).hexdigest()

def read_regular(path):
    item = os.lstat(path)
    require(stat.S_ISREG(item.st_mode) and not stat.S_ISLNK(item.st_mode)
            and item.st_nlink == 1 and os.path.realpath(path) == str(path))
    return path.read_bytes()

def allocation(root):
    total = root.stat().st_blocks * 512
    for path in root.rglob("*"):
        item = os.lstat(path)
        require(not stat.S_ISLNK(item.st_mode))
        total += item.st_blocks * 512
    return total

def reserve(root, output, projected=0):
    used = allocation(output)
    require(used + projected + FINAL_RESERVE <= MAX_OWNED)
    for directory in (root, output):
        require(shutil.disk_usage(directory).free >= MIN_FREE + projected + FINAL_RESERVE)
    return {"allocated": used, "outputFree": shutil.disk_usage(output).free,
            "checkoutFree": shutil.disk_usage(root).free,
            "scope": "Before/after samples plus fixed per-command log bounds; not a measured continuous peak."}

def verify_body(path, row):
    body = read_regular(path)
    require(len(body) == row["bytes"] and sha_bytes(body) == row["sha256"]
            and blob_bytes(body) == row["git"])
    return body

def run_actual(common, commands, root, output, deadline, name, argv, timeout, expected_exit=0):
    reserve(root, output, common.LOG_CAP)
    remaining = deadline - time.monotonic()
    require(remaining > 0)
    failure = None
    try:
        commands.run(name, argv, min(timeout, remaining))
    except Exception as error:
        failure = error
    require(commands.records and commands.records[-1]["name"] == name)
    record = commands.records[-1]
    require(record["error"] is None and record["signals"] == [] and record["familyClosed"] is True
            and record["normalEOF"] is True and record["finalECHILD"] is True
            and record["exit"] == expected_exit and record["exitAfterDrain"] == expected_exit
            and record["births"] and all(row["gone"] for row in record["births"]))
    if expected_exit == 0:
        require(failure is None)
    else:
        require(type(failure) is RuntimeError and str(failure) == name + " exit " + str(expected_exit))
    path = commands.diagnostics / (name + ".log")
    require(path.stat().st_size <= common.LOG_CAP)
    reserve(root, output)
    return path.read_text(encoding="utf-8")

def checkout_guard(common, commands, root, output, deadline, folder, commit, rows, label):
    require(folder.is_dir() and os.path.realpath(folder) == str(folder))
    before = commands.cwd
    commands.cwd = folder
    try:
        actual = run_actual(common, commands, root, output, deadline, label + "-head",
                            ["git", "rev-parse", "HEAD"], 15).strip()
        require(re.fullmatch("[0-9a-f]{40}", commit) is not None and actual == commit)
        status = run_actual(common, commands, root, output, deadline, label + "-clean",
                            ["git", "status", "--porcelain=v1", "--untracked-files=all"], 15)
        require(status == "")
        tree = run_actual(common, commands, root, output, deadline, label + "-tree",
                          ["git", "ls-tree", "-r", "HEAD"], 15)
        tree_records = {}
        for line in tree.splitlines():
            metadata, path = line.split("\t", 1)
            mode, kind, git = metadata.split(" ")
            tree_records[path] = (mode, kind, git)
        for index, row in enumerate(rows):
            require(tree_records.get(row["path"]) == ("100644", "blob", row["git"]))
            body = verify_body(folder / row["path"], row)
            captured = run_actual(common, commands, root, output, deadline,
                label + "-body-" + str(index), ["git", "show", commit + ":" + row["path"]], 15).encode()
            require(captured == body)
    finally:
        commands.cwd = before
    return {"commit": commit, "fileCount": len(rows), "treeSha256": sha_bytes(tree.encode()),
            "rows": rows}

def parse_tap(text, names, expected_failures):
    require(text.startswith("TAP version 13\n") and "\x00" not in text)
    cases = list(re.finditer(r"^(ok|not ok) ([0-9]+) - ([^\r\n]+)$", text, re.MULTILINE))
    require(len(cases) == len(names) and len(set(names)) == len(names))
    require([int(item.group(2)) for item in cases] == list(range(1, len(names) + 1)))
    require({item.group(3) for item in cases} == set(names))
    announced = re.findall(r"^# Subtest: ([^\r\n]+)$", text, re.MULTILINE)
    require(len(announced) == len(names) and set(announced) == set(names))
    plans = re.findall(r"^1\.\.([0-9]+)$", text, re.MULTILINE)
    require(plans == [str(len(names))])
    summaries = {}
    for field in ("tests", "suites", "pass", "fail", "cancelled", "skipped", "todo"):
        values = re.findall(r"^# " + field + r" ([0-9]+)$", text, re.MULTILINE)
        require(len(values) == 1)
        summaries[field] = int(values[0])
    require(summaries == {"tests": len(names), "suites": 0,
        "pass": len(names) - len(expected_failures), "fail": len(expected_failures),
        "cancelled": 0, "skipped": 0, "todo": 0})
    failed = {item.group(3) for item in cases if item.group(1) == "not ok"}
    require(failed == expected_failures)
    rows = []
    for index, item in enumerate(cases):
        segment = text[item.end():cases[index + 1].start() if index + 1 < len(cases) else len(text)]
        code = None
        if item.group(1) == "not ok":
            require(re.findall(r"^  failureType: '([^']+)'$", segment, re.MULTILINE) == ["testCodeFailure"])
            codes = re.findall(r"^  code: '([^']+)'$", segment, re.MULTILINE)
            require(len(codes) == 1 and codes[0] in ("ERR_ASSERTION", "ProviderUnavailable"))
            code = codes[0]
        rows.append({"number": int(item.group(2)), "name": item.group(3),
                     "passed": item.group(1) == "ok", "failureCode": code})
    public_lines = ["TAP version 13"] + [item.group(0) for item in cases]
    public_lines += ["1.." + str(len(names))] + ["# " + k + " " + str(v) for k, v in summaries.items()]
    return {"cases": rows, "counts": summaries, "publicNamedTap": "\n".join(public_lines) + "\n"}

def copy_original_inputs(current, original, graph):
    wanted = ("apps/Web/package.json", "apps/Web/Auth/browser-public-client.js",
              "apps/Web/Auth/account-api-client.js", "apps/Web/Auth/configured-accounts.bundle.js")
    selected = []
    for row in ORIGINAL_ROWS:
        if row["path"] in wanted:
            selected.append((original, row))
    selected.append((current, next(row for row in CURRENT_ROWS
                                  if row["path"] == "apps/Web/Auth/fetch-receiver.test.js")))
    for checkout, row in selected:
        target = graph / row["path"]
        target.parent.mkdir(parents=True, exist_ok=True)
        body = verify_body(checkout / row["path"], row)
        with target.open("xb") as stream:
            stream.write(body)
            stream.flush()
            os.fsync(stream.fileno())
        target.chmod(0o644)
        require(read_regular(target) == body)
    require(len(selected) == 5)
    return [row for _, row in selected]

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--expected-commit", required=True)
    parser.add_argument("--original-checkout", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root = Path.cwd().resolve()
    original = args.original_checkout.resolve()
    output = args.output.resolve()
    require(root != original and not original.is_relative_to(root) and not root.is_relative_to(original))
    require(not output.exists() and not output.is_relative_to(root)
            and not output.is_relative_to(original))
    for directory in (root, output.parent):
        require(shutil.disk_usage(directory).free >= MIN_FREE + MAX_OWNED + FINAL_RESERVE)
    output.mkdir(mode=0o700)
    diagnostics = output / "diagnostics"
    diagnostics.mkdir()
    (output / "tmp").mkdir()
    env = dict(os.environ)
    for key in ("DEBUG", "PWDEBUG", "NODE_DEBUG", "NODE_OPTIONS", "NODE_PATH",
                "GITHUB_TOKEN", "GH_TOKEN", "GIT_TRACE", "GIT_TRACE_PACKET",
                "GIT_CURL_VERBOSE", "GIT_TRACE_CURL", "GIT_TRACE2", "GIT_TRACE2_EVENT",
                "GIT_TRACE2_PERF"):
        env.pop(key, None)
    for key in list(env):
        if key.startswith("CAKE_"):
            env.pop(key, None)
    env.update(PYTHONDONTWRITEBYTECODE="1", TMPDIR=str(output / "tmp"),
               TMP=str(output / "tmp"), TEMP=str(output / "tmp"))
    common_path = root / COMMON_PATH
    require(sha_bytes(read_regular(common_path)) == COMMON_SHA)
    specification = importlib.util.spec_from_file_location("actual_b1_native_custodian", common_path)
    common = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(common)
    commands = common.Commands(diagnostics, env, root)
    deadline = time.monotonic() + 600
    result = {"state": "NOT_RUN", "scope": "Paired B fetch receiver UNIT patch only; no native Window, issuer, authority, lifetime-port193 or rebuilt bundle claim.",
              "originalCommit": ORIGINAL_COMMIT, "sourceCommit": args.expected_commit,
              "bundleTreatment": "Exact two-assignment reviewed PATCH; no rebuild executed."}
    reports = {}
    graph = output / "original-unit-source"
    graph.mkdir()
    current_proof = original_proof = None
    try:
        result["initialResourceSample"] = reserve(root, output)
        current_proof = checkout_guard(common, commands, root, output, deadline,
            root, args.expected_commit, CURRENT_ROWS, "current-before")
        original_proof = checkout_guard(common, commands, root, output, deadline,
            original, ORIGINAL_COMMIT, ORIGINAL_ROWS, "original-before")
        common.write_json(diagnostics / "source-before.json",
                          {"current": current_proof, "original": original_proof})
        node = Path(shutil.which("node", path=env["PATH"])).resolve()
        node_stat = os.lstat(node)
        require(stat.S_ISREG(node_stat.st_mode) and not stat.S_ISLNK(node_stat.st_mode)
                and node_stat.st_nlink == 1)
        node_before = {"bytes": node_stat.st_size, "sha256": common.digest(node)}
        require(run_actual(common, commands, root, output, deadline, "node-version",
                           [str(node), "--version"], 30).strip() == NODE_VERSION)
        result["node"] = {"version": NODE_VERSION, **node_before}
        copied = copy_original_inputs(root, original, graph)
        common.write_json(diagnostics / "original-selected-graph.json", copied)
        commands.cwd = graph
        original_text = run_actual(common, commands, root, output, deadline, "original-receiver",
            [str(node), "--test", "--test-concurrency=1", "apps/Web/Auth/fetch-receiver.test.js"], 120, 1)
        reports["original"] = parse_tap(original_text, RECEIVER_NAMES, set(RECEIVER_NAMES) - ORIGINAL_PASS_NAMES)
        reports["original"]["disposition"] = "EXPECTED_RED_CONTROL_OBSERVED"
        commands.cwd = root
        current_text = run_actual(common, commands, root, output, deadline, "corrected-receiver",
            [str(node), "--test", "--test-concurrency=1", "apps/Web/Auth/fetch-receiver.test.js"], 120)
        reports["corrected"] = parse_tap(current_text, RECEIVER_NAMES, set())
        regression_text = run_actual(common, commands, root, output, deadline, "unchanged-original-regressions",
            [str(node), "--test", "--test-concurrency=1", *REGRESSION_FILES], 120)
        reports["regressions"] = parse_tap(regression_text, REGRESSION_NAMES, set())
        for row in copied:
            verify_body(graph / row["path"], row)
        require(node.stat().st_size == node_before["bytes"] and common.digest(node) == node_before["sha256"])
        result["state"] = "RECEIVER_PATCH_UNIT_PASS"
    except Exception as error:
        result.update(state="FAIL_OR_INCOMPLETE", errorType=type(error).__name__)
    finally:
        commands.cwd = root
        try:
            current_after = checkout_guard(common, commands, root, output, deadline,
                root, args.expected_commit, CURRENT_ROWS, "current-after")
            original_after = checkout_guard(common, commands, root, output, deadline,
                original, ORIGINAL_COMMIT, ORIGINAL_ROWS, "original-after")
            common.write_json(diagnostics / "source-after.json",
                              {"current": current_after, "original": original_after})
            require(current_proof is not None and original_proof is not None
                    and current_after == current_proof and original_after == original_proof)
            result["sourceAfterEqual"] = True
        except Exception as error:
            result.update(state="FAIL_OR_INCOMPLETE", sourceAfterEqual=False,
                          sourceAfterErrorType=type(error).__name__)
        for label in ("original-receiver", "corrected-receiver", "unchanged-original-regressions"):
            path = diagnostics / (label + ".log")
            if path.is_file():
                body = read_regular(path)
                result.setdefault("rawTap", {})[label] = {"bytes": len(body), "sha256": sha_bytes(body)}
        result["reports"] = reports
        result["commands"] = {"count": len(commands.records),
            "allNormalClosed": bool(commands.records) and all(row["familyClosed"] and row["normalEOF"]
                and row["finalECHILD"] and not row["signals"] and row["error"] is None for row in commands.records),
            "receiptSha256": common.digest(diagnostics / "commands.json") if (diagnostics / "commands.json").is_file() else None}
        if not result["commands"]["allNormalClosed"]:
            result["state"] = "FAIL_OR_INCOMPLETE"
        try:
            result["finalResourceSampleBeforeReceipt"] = reserve(root, output)
        except Exception:
            result["state"] = "FAIL_OR_INCOMPLETE"
        raw = (json.dumps(result, indent=2) + "\n").encode()
        require(len(raw) <= MAX_FRAME)
        target = diagnostics / "result.json"
        with target.open("xb") as stream:
            stream.write(raw); stream.flush(); os.fsync(stream.fileno())
        require(read_regular(target) == raw)
        # Final reserve is explicitly allocated for this bounded receipt rewrite.
        try:
            sample = reserve(root, output)
            result["postReceiptResourceSample"] = sample
        except Exception:
            result.update(state="FAIL_OR_INCOMPLETE", postReceiptResourceSample="HOLD")
        final_raw = (json.dumps(result, indent=2) + "\n").encode()
        require(len(final_raw) <= MAX_FRAME)
        with target.open("wb") as stream:
            stream.write(final_raw); stream.flush(); os.fsync(stream.fileno())
        require(read_regular(target) == final_raw)
        if not (allocation(output) <= MAX_OWNED
                and shutil.disk_usage(output).free >= MIN_FREE
                and shutil.disk_usage(root).free >= MIN_FREE):
            result.update(state="FAIL_OR_INCOMPLETE", finalBoundedReadbackResourceGate=False)
            final_raw = (json.dumps(result, indent=2) + "\n").encode()
            require(len(final_raw) <= MAX_FRAME)
            with target.open("wb") as stream:
                stream.write(final_raw); stream.flush(); os.fsync(stream.fileno())
            require(read_regular(target) == final_raw)
    frame = {"schema": "team-b-native-fetch-unit-named-tap-v1",
             "state": result["state"], "sourceCommit": args.expected_commit,
             "originalCommit": ORIGINAL_COMMIT, "scope": result["scope"],
             "original": reports.get("original"), "corrected": reports.get("corrected"),
             "regressions": reports.get("regressions"), "rawTap": result.get("rawTap", {}),
             "commands": result["commands"], "resultSha256": sha_bytes(final_raw)}
    frame_raw = json.dumps(frame, separators=(",", ":")).encode()
    require(len(frame_raw) + len("RECEIVER_UNIT_TAP: \n") <= MAX_FRAME)
    print("RECEIVER_UNIT_TAP: " + frame_raw.decode())
    return 0 if result["state"] == "RECEIVER_PATCH_UNIT_PASS" else 1

if __name__ == "__main__":
    raise SystemExit(main())
