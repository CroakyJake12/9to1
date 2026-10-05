#!/usr/bin/env python3
"""Unchanged mock DOM/provider AX regression at one immutable product cut only."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import sys

sys.dont_write_bytecode = True
BASE_COMMIT = "caa6712bd5517db2458fcaf8f23e6c97d912f958"
BRANCH = "refs/heads/custody/team-b-browser-ax-caa6712-original-unit-20261005"
FIXTURE = "apps/Web/Tests/browser-accessibility.test.mjs"
ADAPTER = "apps/Web/wwwroot/browser-accessibility.js"
COMMON = "apps/Web/Tests/ci/run-ordinary-native.py"
PINS = {
    FIXTURE: (11285, "b399a3d66f6d82f19bc658f1dd78d2895007d411fe2bd64bd61d51d38e31c9aa"),
    ADAPTER: (9055, "1ab270bbc0fbe648f37495415ce550e09eec0c940280abb980389cb6c21066bc"),
    COMMON: (19584, "a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38"),
}

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
              "productBaseCommit": BASE_COMMIT, "expectedChecks": 13,
              "scope": "Existing unchanged mock DOM/provider source regression only; native/browser/provider acceptance NOT_RUN.",
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
        for actual in (Path(__file__).resolve(), root / ".github/workflows/team-b-browser-ax-original13.yml"):
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
        result["state"] = "RUNNING_UNCHANGED_ORIGINAL_FIXTURE"
        commands.run("ax-original13", ["node", FIXTURE, ADAPTER,
                     str(diagnostics / "original-unit-results.json")], 120)
        report = json.loads((diagnostics / "original-unit-results.json").read_text())
        if len(report.get("checks", [])) != 13 or report.get("productParityVerified") is not False:
            raise RuntimeError("Existing fixture complete report shape changed")
        if report.get("sourceSha256") != PINS[ADAPTER][1] or report.get("runnerSha256") != PINS[FIXTURE][1]:
            raise RuntimeError("Actual fixture report input pins changed")
        result.update(state="PASS_UNCHANGED_ORIGINAL13_MOCK_CONTROLS", actualChecks=13)
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
    return 0 if result["state"] == "PASS_UNCHANGED_ORIGINAL13_MOCK_CONTROLS" else 1

if __name__ == "__main__":
    raise SystemExit(main())
