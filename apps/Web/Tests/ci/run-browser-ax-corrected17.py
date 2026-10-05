#!/usr/bin/env python3
"""Faithful mock AX17: original13 plus two behavior negatives and their mutants."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import sys

sys.dont_write_bytecode = True
BASE_COMMIT = "caa6712bd5517db2458fcaf8f23e6c97d912f958"
BRANCH = "refs/heads/custody/team-b-browser-ax-caa6712-corrected-unit-20261005"
FIXTURE = "apps/Web/Tests/browser-accessibility.test.mjs"
ADAPTER = "apps/Web/wwwroot/browser-accessibility.js"
COMMON = "apps/Web/Tests/ci/run-ordinary-native.py"
PINS = {
    FIXTURE: (13999, "51c9add33de51aecd172ebd3a72a8d54f8b2c9a1bf0cfa3ed267c81da4c9926d"),
    ADAPTER: (9055, "1ab270bbc0fbe648f37495415ce550e09eec0c940280abb980389cb6c21066bc"),
    COMMON: (19584, "a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38"),
}

EXPECTED_CHECKS = [["native-nonfocusable-remains-out-of-tab-sequence","PASS"],["initial-snapshot-failure-removes-root-and-timer","PASS"],["stable-peers-follow-current-native-order-and-retain-focus","PASS"],["disposed-retained-dom-controls-stop-forwarding-operations","PASS"],["native-host-tab-follows-current-peer-focus-and-skips-disabled","PASS"],["native-host-keeps-other-keys-modifiers-and-empty-view-egress","PASS"],["disposed-native-tab-listener-stops-routing-and-restores-host","PASS"],["removed-native-focusability-guard","EXPECTED_FAILURE_DETECTED"],["restored-timer-before-failed-snapshot","EXPECTED_FAILURE_DETECTED"],["removed-native-order-reconciliation","EXPECTED_FAILURE_DETECTED"],["removed-disposed-event-forwarding-guards","EXPECTED_FAILURE_DETECTED"],["removed-native-tab-arbitration-subscription","EXPECTED_FAILURE_DETECTED"],["removed-actual-native-focus-order","EXPECTED_FAILURE_DETECTED"],["released-projected-claim-refused-focus-does-not-restore-after-reorder","PASS"],["different-current-native-focus-does-not-restore-old-dom-focus-after-reorder","PASS"],["removed-current-projected-claim-ownership","EXPECTED_FAILURE_DETECTED"],["removed-matching-current-native-focus-guard","EXPECTED_FAILURE_DETECTED"]]

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def write(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--expected-commit", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root, output = Path.cwd().resolve(), args.output.resolve()
    if output.exists() or output.is_relative_to(root):
        raise RuntimeError("Fresh external output required")
    output.mkdir()
    diagnostics = output / "diagnostics"
    diagnostics.mkdir()
    result = {"state": "NOT_RUN", "sourceCommit": args.expected_commit,
              "productBaseCommit": BASE_COMMIT, "expectedChecks": 17,
              "scope": "Faithful mock DOM/provider regression: original13 assertions retained plus two behavior negatives and their mutants; native/browser/provider acceptance NOT_RUN.",
              "rawStreams": "Maintained Commands retains stdout and stderr combined verbatim."}
    commands = None
    before = None
    try:
        if os.environ.get("GITHUB_REF") != BRANCH or os.environ.get("GITHUB_REPOSITORY") != "CroakyJake12/9to1":
            raise RuntimeError("Exact read-only custody branch required")
        before = {}
        for path, (length, digest) in PINS.items():
            actual = root / path
            if actual.stat().st_size != length or sha(actual) != digest:
                raise RuntimeError("Immutable input changed: " + path)
            before[path] = {"bytes": length, "sha256": digest}
        for actual in (Path(__file__).resolve(), root / ".github/workflows/team-b-browser-ax-corrected17.yml"):
            before[str(actual.relative_to(root))] = {"bytes": actual.stat().st_size, "sha256": sha(actual)}
        write(diagnostics / "source-before.json", before)
        spec = importlib.util.spec_from_file_location("maintained_commands", root / COMMON)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        commands = module.Commands(diagnostics, os.environ.copy(), root)
        head = commands.run("git-head", ["git", "rev-parse", "HEAD"], 15).strip()
        parent = commands.run("git-parent", ["git", "rev-parse", "HEAD^"], 15).strip()
        if head != args.expected_commit or parent != BASE_COMMIT:
            raise RuntimeError("Actual source/custody parent differs from fixed tuple")
        if commands.run("status-before", ["git", "status", "--porcelain=v1", "--untracked-files=all"], 15).strip():
            raise RuntimeError("Fresh source required")
        commands.run("tracked-tree", ["git", "ls-tree", "-r", "HEAD"], 15)
        if commands.run("node-version", ["node", "--version"], 15).strip() != "v22.14.0":
            raise RuntimeError("Expected Node22.14.0")
        result["state"] = "RUNNING_FAITHFUL_FIXTURE_AND_ADDED_GUARDS"
        commands.run("ax-corrected17", ["node", FIXTURE, ADAPTER,
                     str(diagnostics / "corrected-unit-results.json")], 120)
        report = json.loads((diagnostics / "corrected-unit-results.json").read_text())
        if report.get("checks") != [{"name": name, "result": result} for name, result in EXPECTED_CHECKS] or report.get("productParityVerified") is not False:
            raise RuntimeError("Complete unchanged identities/results of original13 plus four new checks required")
        if report.get("sourceSha256") != PINS[ADAPTER][1] or report.get("runnerSha256") != PINS[FIXTURE][1]:
            raise RuntimeError("Actual fixture report input pins changed")
        result.update(state="PASS_FAITHFUL17_MOCK_CONTROLS", actualChecks=17)
    except Exception as error:
        result.update(state="FAIL", failure=str(error)[:2048])
    finally:
        try:
            if before is not None:
                after = {path: {"bytes": (root / path).stat().st_size, "sha256": sha(root / path)}
                         for path in before}
                write(diagnostics / "source-after.json", after)
                if after != before:
                    raise RuntimeError("Inputs changed during regression")
            if commands is not None:
                if commands.run("status-after", ["git", "status", "--porcelain=v1", "--untracked-files=all"], 15).strip():
                    raise RuntimeError("Source gained changes")
                result["commands"] = commands.records
        except Exception as error:
            result.update(state="FAIL", integrityFailure=str(error)[:2048])
        write(diagnostics / "result.json", result)
    print(json.dumps(result))
    return 0 if result["state"] == "PASS_FAITHFUL17_MOCK_CONTROLS" else 1

if __name__ == "__main__":
    raise SystemExit(main())
