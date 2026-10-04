#!/usr/bin/env python3
"""Original Mail draft Fact plus real file-backed seed/fresh-process owner controls; reuse maintained ordinary Commands.

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
GATE_SHA = "73d0446a62cc5fd940c77726f503f956c5e76e25674ee03539d52cd1bf2e2101"
PROJECT = "apps/Web/Productivity/Mail/Tests/Mail.OriginalDraft.Tests.csproj"
PROBE = "apps/Web/Productivity/Mail/Tests/Mail.LocalDurable.Tests.csproj"
CASES = ["rich-semantic-drafts-and-attachment-identities-restart", "same-id-update-and-denied-input-preserve-bytes", "durable-delete-and-unreadable-draft-preserved-restart"]
ORIGINAL_TEST_PINS = {'9to1 Workspace/shared/tests/Haven.Desktop.Tests/FileMailDraftStoreTests.cs': '6f75849582ced5a540f8d9ee9e72cebd08ebeac26d084e48b96c363d5d1a5427', '9to1 Workspace/shared/tests/Haven.Desktop.Tests/AssemblyInfo.cs': '476ebbfbe45b12c6e9becf8bc5a79ad2fe6d419638dc8d660a97b6df58429a87', '9to1 Workspace/shared/src/Haven.Infrastructure/Mail/FileMailDraftStore.cs': 'b067b09159b7f7b34075cfe07abd7cd0ee798d11e60d3eb597d11405ca2b7070'}


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
             checked_file(Path(__file__).with_name("original-mail-acceptance.py").resolve(), root),
             checked_file(root / "global.json", root),
             checked_file(root / "NuGet.Config", root)}
    # Exact original source-linked closure and both real public project ports.
    native_project = checked_file(root / PROBE, root)
    paths.add(native_project)
    for actual_project in (project, native_project):
        for item in ET.parse(actual_project).iter():
            if item.tag in ("Compile", "ProjectReference"):
                paths.add(checked_file((actual_project.parent / item.attrib["Include"]).resolve(), root))
    # Whole source trees of the actual three selected managed public projects,
    # including CUI.AI embedded resource. No replacement code/renderer/backend.
    for directory in ("9to1 Workspace/shared/src/Haven.Core", "9to1 Workspace/shared/src/Haven.Application", "framework/CUI/AI"):
        for actual in (root / directory).rglob("*"):
            if actual.is_file() and not any(p in ("bin", "obj") for p in actual.relative_to(root / directory).parts) and actual.suffix in (".cs", ".csproj", ".cui"):
                paths.add(checked_file(actual, root))
    for relative in ("9to1 Workspace/Directory.Build.props", "9to1 Workspace/Directory.Build.targets", "9to1 Workspace/Directory.Packages.props", "9to1 Workspace/shared/src/Directory.Packages.props", "framework/CUI/Directory.Build.props", "framework/CUI/Directory.Build.targets", "framework/CUI/Directory.Packages.props"):
        actual = root / relative
        if actual.exists(): paths.add(checked_file(actual, root))
    for relative, expected in ORIGINAL_TEST_PINS.items():
        require(digest(checked_file(root / relative, root)) == expected,
                "Original Fact or owner isolation policy was changed")
    return [{"path": str(path.relative_to(root)), "bytes": path.stat().st_size,
             "sha256": digest(path)} for path in sorted(paths)]


def write_json(path, data):
    payload = (json.dumps(data, indent=2) + "\n").encode()
    require(len(payload) <= 1024 * 1024, "Bounded diagnostic metadata exceeded")
    path.write_bytes(payload)


def runtime_pins(folder):
    rows = []
    for path in sorted(folder.rglob("*")):
        if path.is_file():
            checked_file(path, folder)
            rows.append({"path": str(path.relative_to(folder)), "bytes": path.stat().st_size, "sha256": digest(path)})
    return rows


def phase_gate(path, mode, log):
    value = json.loads(path.read_text())
    require(value["mode"] == mode and value["exitCode"] == 0 and value["declared"] == value["executed"] == value["passed"] == 3 and value["failed"] == value["notRun"] == 0, "Actual all-three native owner counts differ")
    require([r["name"] for r in value["outcomes"]] == CASES and all(r["state"] == "PASS" and r["assertions"] > 0 for r in value["outcomes"]), "Exact three scenario oracles must all execute")
    require(re.findall(r"^PASS ([^\r\n]+)$", log, re.MULTILINE) == CASES and not re.search(r"^FAIL ", log, re.MULTILINE), "Actual phase stdout differs from results")
    return value


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
    result = {"status": "NOT_RUN", "selectedExpected": 1, "discoveredExpected": 1, "nativeScenarioExpected": 3, "nativePhaseExecutionsExpected": 6,
              "scope": "Original rich semantic Mail draft Fact plus actual file-backed owner update/delete/error/cold-process controls only; no provider/send/browser/conflict-CAS/authority acceptance",
              "sourceCommit": None, "executed": None,
              "preservedHistory": "Original Space-key FAIL and held renderer/owner factory requests remain unchanged. This persisted-domain gate does not replace them."}
    commands = None
    before = None
    gate = None
    started_ns = None
    results = None
    target = None
    runtime = None
    fixture = output / "fixture-data"
    fixture.mkdir()
    try:
        sys.dont_write_bytecode = True
        ordinary = load_source(root / RUNNER_PATH, root, RUNNER_SHA, "mail_ordinary_commands")
        gate = load_source(Path(__file__).with_name("original-mail-acceptance.py").resolve(),
                           root, GATE_SHA, "mail_original_acceptance")
        env = os.environ.copy()
        for variable, directory in {
            "DOTNET_CLI_HOME": "cli", "NUGET_PACKAGES": "nuget",
            "NUGET_HTTP_CACHE_PATH": "http", "NUGET_PLUGINS_CACHE_PATH": "plugins",
            "XDG_CACHE_HOME": "cache", "TMPDIR": "tmp", "TMP": "tmp", "TEMP": "tmp",
        }.items():
            path = output / directory
            path.mkdir(exist_ok=True)
            env[variable] = str(path)
        # Original HOME remains original; original Mail Fact uses its real Path.GetTempPath with owned TMPDIR.
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
        tracked = commands.run("owned-files-tracked", ["git", "ls-files", "--", PROJECT, PROBE,
            "apps/Web/Productivity/Mail/Tests/MailLocalDurableControls.cs", RUNNER_PATH,
            str(Path(__file__).resolve().relative_to(root)),
            str(Path(__file__).with_name("original-mail-acceptance.py").resolve().relative_to(root))], 15)
        require(len(set(tracked.splitlines())) == 6, "All six maintained test/driver inputs must actually be tracked")
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
        require(discovered["accepted"], "Actual unchanged original Mail draft Fact not discovered")
        result["discovered"] = discovered["actual_discovered_count"]
        results = output / "test-results"
        results.mkdir()
        started_ns = time.time_ns()
        result["nativeStarted"] = True
        commands.run("original-mail-draft-fact", ["dotnet", "test", PROJECT, "--no-build", "--no-restore",
            "-c", "Release", "--filter", gate.FILTER,
            "--logger", "trx;LogFileName=mail-original-draft.trx",
            "--results-directory", str(results)] + common, 120)
        actual = gate.trx_gate(results / "mail-original-draft.trx", started_ns)
        write_json(diagnostics / "original-trx-gate.json", actual)
        require(actual["accepted"], "Fresh TRX exact original1/PASS1/skip0 identity gate failed")
        result.update(originalFact={"executed": 1, "passed": 1, "failed": 0, "skipped": 0})
        commands.run("restore-probe", ["dotnet", "restore", PROBE, "--configfile", str(root / "NuGet.Config"), "--disable-parallel", "-p:Configuration=Release", "-p:UseAppHost=true"] + common, 600)
        commands.run("build-probe", ["dotnet", "build", PROBE, "--no-restore", "-c", "Release", "-p:UseAppHost=true"] + common, 900)
        evaluated = ordinary.parse_sdk(commands.run("target-properties", ["dotnet", "msbuild", PROBE,
            "-p:Configuration=Release", "-p:RuntimeIdentifier=linux-x64", "-p:SelfContained=false", "-p:UseSharedCompilation=false", "-p:UseAppHost=true",
            "-p:UseArtifactsOutput=true", "-p:ArtifactsPath=" + str(artifacts), "-nodeReuse:false", "-getProperty:TargetPath,TargetDir,TargetFramework,RuntimeIdentifier,UseAppHost"], 30))
        write_json(diagnostics / "target-properties.json", evaluated)
        target = Path(evaluated["TargetPath"]).resolve(strict=True); apphost = target.with_suffix("")
        require(evaluated["TargetFramework"] == "net10.0" and evaluated["RuntimeIdentifier"] == "linux-x64" and evaluated["UseAppHost"].lower() == "true" and Path(evaluated["TargetDir"]).resolve() == target.parent, "Actual evaluated native entry metadata differs")
        checked_file(target, artifacts); checked_file(apphost, artifacts)
        require(os.access(apphost, os.X_OK), "Actual native apphost not executable")
        with target.open("rb") as stream: require(stream.read(2) == b"MZ", "Actual managed entry missing")
        with apphost.open("rb") as stream: require(stream.read(5) == b"\x7fELF\x02", "Actual Linux64 apphost missing")
        runtime = runtime_pins(target.parent); write_json(diagnostics / "runtime-before.json", runtime)
        phases = {}
        for mode in ("seed", "verify"):
            log = commands.run("native-" + mode, [str(apphost), mode, str(fixture)], 90)
            native = commands.records[-1]
            require(native["exit"] == native["exitAfterDrain"] == 0 and native["normalEOF"] and native["familyClosed"] and native["finalECHILD"] and native["births"] and all(b["gone"] for b in native["births"]) and not native["signals"], "Native phase family must naturally close before next process")
            phases[mode] = phase_gate(fixture / (mode + "-results.json"), mode, log)
            write_json(diagnostics / (mode + "-results.json"), phases[mode])
        require(source_pins(root) == before, "Owner/project/driver source changed")
        result.update(status="PASS", executed=1, passed=1, failed=0, skipped=0, distinctNativeScenarios=3, nativePhaseExecutions=6, phases=phases)
    except Exception as error:
        result.update(status="FAIL", error=repr(error))
    finally:
        if gate is not None and started_ns is not None and results is not None:
            try:
                actual = gate.trx_gate(results / "mail-original-draft.trx", started_ns)
                write_json(diagnostics / "original-trx-gate.json", actual)
                result["actualTrxCounters"] = actual.get("counters")
                if actual.get("counters") is not None:
                    result["executed"] = actual["counters"].get("executed")
                if not actual["accepted"]:
                    result.update(status="FAIL", originalTrxError=actual["violations"])
            except Exception as error:
                result.update(status="FAIL", originalTrxError=repr(error))
        for mode in ("seed", "verify"):
            path = fixture / (mode + "-results.json")
            if path.is_file():
                try: write_json(diagnostics / (mode + "-results.json"), json.loads(path.read_text()))
                except Exception as error: result.update(status="FAIL", phaseResultError=repr(error))
        if target is not None and runtime is not None:
            try:
                after_runtime = runtime_pins(target.parent); write_json(diagnostics / "runtime-after.json", after_runtime)
                require(runtime == after_runtime, "Actual native runtime changed")
            except Exception as error: result.update(status="FAIL", runtimeCustodyError=repr(error))
        if commands is not None:
            try:
                commands.run("git-clean-after", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
                after = source_pins(root); write_json(diagnostics / "source-pins-after.json", after)
                require(before is None or after == before, "Final source custody mismatch")
            except Exception as error:
                result.update(status="FAIL", sourceCustodyError=repr(error))
        records = [] if commands is None else commands.records
        closed = bool(records) and all(c["exit"] == c["exitAfterDrain"] == 0 and c["normalEOF"] and c["familyClosed"] and c["finalECHILD"] and c["error"] is None and c["births"] and all(b["gone"] for b in c["births"]) and not c["signals"] for c in records)
        result["allCommandFamiliesNormalClosed"] = closed
        result["accepted"] = result["status"] == "PASS" and closed
        result["custodyQualification"] = "Full native runtime/source hash metadata only; runtime/package/draft bytes and identity receipt are not uploaded. No provider/conflict/backend-authority/browser parity."
        write_json(diagnostics / "result.json", result)
    print(json.dumps(result, indent=2))
    return 0 if result["accepted"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
