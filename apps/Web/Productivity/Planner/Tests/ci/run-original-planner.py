#!/usr/bin/env python3
"""Source-built original Planner SQLite Fact; reuse maintained ordinary Commands.

This driver imports no test DLL and provides no alternate model/backend/validator.
Only bounded diagnostics/TRX are public artifacts; fixture database/cache/binaries
stay in the disposable runner output. No work executes on module import.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import sys
import time
import xml.etree.ElementTree as ET

RUNNER_PATH = "apps/Web/Tests/ci/run-ordinary-native.py"
# Planned B1 successor02; root must adopt this exact maintained source or review
# a new binding. This is not a claim that the proposal has already been adopted.
RUNNER_SHA = "a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38"
GATE_SHA = "451031f9f331eec1334bc3eb9be4f66b260e7ce407f72743f06ce08bf064ce96"
PROJECT = "apps/Web/Productivity/Planner/Tests/Planner.OriginalSQLite.Tests.csproj"
ORIGINAL_TEST_PINS = {
    "9to1 Workspace/shared/tests/Haven.Infrastructure.Tests/PlannerStructuredConcurrencyTests.cs":
        "ecf3b305522db8b8fdec9cad3d1d3845ef439f437f410aaafc8739cea50864e2",
    "9to1 Workspace/shared/tests/Haven.Infrastructure.Tests/AssemblyInfo.cs":
        "8285427fb7535ccbafebfa3fb58e724495859fd711d40df82a877e0f33fb9640",
}


def require(value, message):
    if not value:
        raise RuntimeError(message)


def digest(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for part in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(part)
    return result.hexdigest()


def checked_file(path, root):
    require(path.is_absolute() and path.resolve(strict=True) == path,
            "Canonical source file required")
    require(path.is_relative_to(root) and
            all(not item.is_symlink() for item in [path, *path.parents]),
            "Contained nonsymlink source file required")
    require(stat.S_ISREG(path.lstat().st_mode), "Regular source file required")
    return path


def load_source(path, root, expected, name):
    checked_file(path, root)
    require(digest(path) == expected, "Reviewed source binding differs: " + name)
    spec = importlib.util.spec_from_file_location(name, path)
    require(spec is not None and spec.loader is not None, "Source loader absent")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    require(digest(path) == expected, "Imported source changed: " + name)
    return module


def source_pins(root):
    project = checked_file(root / PROJECT, root)
    paths = {project, checked_file(Path(__file__).resolve(), root),
             checked_file(root / RUNNER_PATH, root),
             checked_file(Path(__file__).with_name("original-test-acceptance.py").resolve(), root),
             checked_file(root / "global.json", root),
             checked_file(root / "NuGet.Config", root)}
    # Exact original source-linked closure and both real public project ports.
    tree = ET.parse(project)
    for item in tree.iter():
        if item.tag in ("Compile", "ProjectReference"):
            paths.add(checked_file((project.parent / item.attrib["Include"]).resolve(), root))
    for relative, expected in ORIGINAL_TEST_PINS.items():
        require(digest(checked_file(root / relative, root)) == expected,
                "Original Fact or owner isolation policy was changed")
    return [{"path": str(path.relative_to(root)), "bytes": path.stat().st_size,
             "sha256": digest(path)} for path in sorted(paths)]


def write_json(path, data):
    payload = (json.dumps(data, indent=2) + "\n").encode()
    require(len(payload) <= 1024 * 1024, "Bounded diagnostic metadata exceeded")
    path.write_bytes(payload)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--expected-commit", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root = Path.cwd().resolve(strict=True)
    require(re.fullmatch(r"[0-9a-f]{40}", args.expected_commit), "Exact checkout SHA required")
    require(args.output.is_absolute() and not args.output.is_symlink(), "Absolute fresh output required")
    output = args.output.resolve()
    require(not output.exists() and not output.is_relative_to(root), "Fresh output outside checkout required")
    output.mkdir(parents=True)
    diagnostics = output / "diagnostics"
    diagnostics.mkdir()
    result = {"status": "NOT_RUN", "selected": 1, "discoveredExpected": 2,
              "scope": "Original same-process actual SQLite CAS/outbox/delete/restore Fact only",
              "sourceCommit": None, "executed": 0,
              "preservedHistory": "Local original Planner acquisition/native remain NOT_RUN; CI cannot retroactively change local failures or custody qualifications."}
    commands = None
    before = None
    try:
        sys.dont_write_bytecode = True
        ordinary = load_source(root / RUNNER_PATH, root, RUNNER_SHA, "planner_ordinary_commands")
        gate = load_source(Path(__file__).with_name("original-test-acceptance.py").resolve(),
                           root, GATE_SHA, "planner_original_acceptance")
        env = os.environ.copy()
        for variable, directory in {
            "DOTNET_CLI_HOME": "cli", "NUGET_PACKAGES": "nuget",
            "NUGET_HTTP_CACHE_PATH": "http", "NUGET_PLUGINS_CACHE_PATH": "plugins",
            "XDG_CACHE_HOME": "cache", "TMPDIR": "tmp", "TMP": "tmp", "TEMP": "tmp",
        }.items():
            path = output / directory
            path.mkdir(exist_ok=True)
            env[variable] = str(path)
        # Original HOME remains original. Temporary SQLite fixture uses TMPDIR.
        env.update(CI="true", DOTNET_GENERATE_ASPNET_CERTIFICATE="false",
                   DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1",
                   MSBUILDDISABLENODEREUSE="1")
        commands = ordinary.Commands(diagnostics, env, root)
        head = commands.run("git-head", ["git", "rev-parse", "HEAD"], 15).strip()
        require(head == args.expected_commit, "Actual checkout differs from github.sha")
        result["sourceCommit"] = head
        commands.run("git-clean-before", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
        commands.run("source-tree-pins", ["git", "ls-tree", "-r", "HEAD"], 15)
        # A fresh hosted checkout must actually track the newly adopted sources.
        tracked = commands.run("owned-files-tracked", ["git", "ls-files", "--", PROJECT,
            RUNNER_PATH, str(Path(__file__).resolve().relative_to(root)),
            str(Path(__file__).with_name("original-test-acceptance.py").resolve().relative_to(root))], 15)
        require(len(set(tracked.splitlines())) == 4, "All four maintained/adopted CI sources must be tracked")
        before = source_pins(root)
        write_json(diagnostics / "source-pins-before.json", before)
        require(commands.run("dotnet-version", ["dotnet", "--version"], 30).strip() == "10.0.401",
                "Reviewed SDK10.0.401 required")
        commands.run("dotnet-info", ["dotnet", "--info"], 30)
        artifacts = output / "artifacts"
        common = ["--artifacts-path", str(artifacts), "-r", "linux-x64",
                  "-p:SelfContained=false", "-p:UseSharedCompilation=false",
                  "--disable-build-servers", "-m:1", "-nodeReuse:false"]
        commands.run("restore", ["dotnet", "restore", PROJECT, "--configfile",
                     str(root / "NuGet.Config"), "--disable-parallel", "-p:Configuration=Release"] + common, 600)
        commands.run("build", ["dotnet", "build", PROJECT, "--no-restore", "-c", "Release"] + common, 1200)
        discovery = commands.run("discovery", ["dotnet", "test", PROJECT, "--no-build", "--no-restore",
                                 "-c", "Release", "--list-tests"] + common, 60)
        discovered = gate.discovery_gate(discovery)
        write_json(diagnostics / "discovery-gate.json", discovered)
        require(discovered["accepted"], "Actual two unchanged original Facts not discovered")
        result["discovered"] = discovered["actual_discovered_count"]
        results = output / "test-results"
        results.mkdir()
        started_ns = time.time_ns()
        commands.run("selected-original-fact", ["dotnet", "test", PROJECT, "--no-build", "--no-restore",
            "-c", "Release", "--filter", "FullyQualifiedName=" + gate.SELECTED,
            "--logger", "trx;LogFileName=planner-original-concurrency.trx",
            "--results-directory", str(results)] + common, 120)
        actual = gate.trx_gate(results / "planner-original-concurrency.trx", started_ns)
        write_json(diagnostics / "selected-trx-gate.json", actual)
        require(actual["accepted"], "Fresh TRX original selected1/PASS1/skip0 identity gate failed")
        require(source_pins(root) == before, "Owner/project/driver source changed during original run")
        result.update(status="PASS", executed=1, passed=1, failed=0, skipped=0)
    except Exception as error:
        result.update(status="FAIL", error=repr(error))
    finally:
        if commands is not None:
            try:
                commands.run("git-clean-after", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
                require(before is None or source_pins(root) == before, "Final source custody mismatch")
            except Exception as error:
                result.update(status="FAIL", sourceCustodyError=repr(error))
        write_json(diagnostics / "result.json", result)
    print(json.dumps(result, indent=2))
    return 0 if result["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
