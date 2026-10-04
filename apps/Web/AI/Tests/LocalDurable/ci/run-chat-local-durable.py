#!/usr/bin/env python3
"""Original local Chat owner SQLite branch/draft/export: no provider or browser."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re

COMMON = "apps/Web/Tests/ci/run-ordinary-native.py"
COMMON_SHA = "a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38"
PROJECT = "apps/Web/AI/Tests/LocalDurable/ChatLocalDurable.Tests.csproj"
LEDGER = "apps/Web/AI/Tests/LocalDurable/source-pins.json"
LEDGER_SHA = "28067721f88653f977f32aa9c105b2e83ad3741ea7f1b2d7c76f97e3c7f22f28"
CASES = ["real-branch-original-history-fresh-process", "real-overwrite-recovery-draft-fresh-process", "real-json-markdown-plain-export-preserves-owner"]

def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""): value.update(chunk)
    return value.hexdigest()

def write(path, value): path.write_text(json.dumps(value, indent=2) + "\n")

def source_check(root):
    ledger = root / LEDGER
    if ledger.is_symlink() or not ledger.is_file() or digest(ledger) != LEDGER_SHA:
        raise RuntimeError("Reviewed owner ledger changed; explicit review/rebind required")
    rows = json.loads(ledger.read_text())["sourceFiles"]
    for relative, pin in rows.items():
        p = Path(relative); actual = root / p
        if p.is_absolute() or ".." in p.parts or actual.is_symlink() or not actual.is_file() or not actual.resolve().is_relative_to(root):
            raise RuntimeError("Source is not checkout-contained regular input")
        if actual.stat().st_size != pin["bytes"] or digest(actual) != pin["sha256"]:
            raise RuntimeError("Reviewed owner body changed: " + relative)
    return rows

def runtime_pins(folder):
    rows = []
    for path in sorted(folder.rglob("*")):
        if path.is_file():
            if path.is_symlink() or not path.resolve().is_relative_to(folder): raise RuntimeError("Runtime input escapes target")
            rows.append({"path": str(path.relative_to(folder)), "bytes": path.stat().st_size, "sha256": digest(path)})
    return rows

def validate_phase(path, mode, log):
    value = json.loads(path.read_text())
    if (value["mode"] != mode or value["exitCode"] != 0 or value["discovered"] != 3
            or value["executed"] != 3 or value["passed"] != 3 or value["failed"] != 0 or value["notRun"] != 0):
        raise RuntimeError("Actual complete three-phase owner counts differ")
    rows = value["outcomes"]
    if len(rows) != 3 or [r["name"] for r in rows] != CASES or any(r["state"] != "PASS" or r["assertions"] <= 0 for r in rows):
        raise RuntimeError("Actual same three owner criteria not all asserted")
    if re.findall(r"^PASS ([^\r\n]+)$", log, re.MULTILINE) != CASES or re.search(r"^FAIL ", log, re.MULTILINE):
        raise RuntimeError("Native stdout does not match actual complete criteria")
    return value

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--expected-commit", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args(); root = Path.cwd().resolve(); output = args.output
    if output.is_symlink() or output.absolute() != output.resolve() or output.exists() or output.resolve().is_relative_to(root):
        raise RuntimeError("Require fresh canonical owned output outside checkout")
    output.mkdir(parents=True); diagnostics = output / "diagnostics"; diagnostics.mkdir()
    result = {"status": "NOT_RUN", "accepted": False, "scenarioCount": 3, "freshNativeProcesses": 2,
              "scope": "Actual original local SQLite owner persistence and export only; browser Chat/controller/catalog/executor/Den/actor/permissions/provider NOT_RUN"}
    env = os.environ.copy(); commands = None; before = None; runtime = None; target = None
    for var, name in {"DOTNET_CLI_HOME":"cli", "NUGET_PACKAGES":"nuget", "NUGET_HTTP_CACHE_PATH":"http", "NUGET_PLUGINS_CACHE_PATH":"plugins",
                      "TMPDIR":"tmp", "TMP":"tmp", "TEMP":"tmp", "XDG_CACHE_HOME":"cache", "HAVEN_DATA_DIR":"fixture-data"}.items():
        owned = output / name; owned.mkdir(exist_ok=True); env[var] = str(owned)
    env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE="false", DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", MSBUILDDISABLENODEREUSE="1")
    fixture = output / "fixture-data"
    try:
        common = root / COMMON
        if common.is_symlink() or digest(common) != COMMON_SHA: raise RuntimeError("Actual maintained a57 Commands changed")
        spec = importlib.util.spec_from_file_location("chat_durable_original_commands", common)
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        commands = module.Commands(diagnostics, env, root)
        head = commands.run("git-head", ["git", "rev-parse", "HEAD"], 15).strip()
        if not re.fullmatch(r"[0-9a-f]{40}", args.expected_commit) or head != args.expected_commit: raise RuntimeError("Actual checkout differs from expected source")
        result["sourceCommit"] = head
        commands.run("git-clean-before", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
        if commands.run("git-status-before", ["git", "status", "--porcelain", "--untracked-files=all"], 15).strip(): raise RuntimeError("Fresh tracked checkout required; no extra source admission")
        commands.run("tracked-source-tree", ["git", "ls-tree", "-r", "HEAD"], 15)
        before = source_check(root); write(diagnostics / "source-before.json", before)
        if commands.run("dotnet-version", ["dotnet", "--version"], 30).strip() != "10.0.401": raise RuntimeError("Require SDK10.0.401")
        commands.run("dotnet-info", ["dotnet", "--info"], 30)
        artifacts = output / "artifacts"
        props = ["-p:SelfContained=false", "-p:UseSharedCompilation=false", "-p:UseAppHost=true",
                 "-p:CreateHardLinksForCopyAdditionalFilesIfPossible=false", "-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=false",
                 "-p:CreateHardLinksForCopyLocalIfPossible=false", "-p:CreateHardLinksForPublishFilesIfPossible=false"]
        commands.run("restore", ["dotnet", "restore", PROJECT, "--artifacts-path", str(artifacts), "-r", "linux-x64",
                     "--configfile", str(root / "NuGet.Config"), "--disable-build-servers", "-p:Configuration=Release", "-m:1", "-nodeReuse:false"] + props, 600)
        commands.run("build", ["dotnet", "build", PROJECT, "--no-restore", "-c", "Release", "--artifacts-path", str(artifacts),
                     "-r", "linux-x64", "--disable-build-servers", "-m:1", "-nodeReuse:false"] + props, 900)
        evaluated = module.parse_sdk(commands.run("target-properties", ["dotnet", "msbuild", PROJECT, "-p:Configuration=Release", "-p:RuntimeIdentifier=linux-x64",
                     "-p:UseArtifactsOutput=true", "-p:ArtifactsPath=" + str(artifacts), "-nodeReuse:false", "-getProperty:TargetPath,TargetDir,TargetFramework,RuntimeIdentifier,UseAppHost"] + props, 30))
        write(diagnostics / "target-properties.json", evaluated)
        target = Path(evaluated["TargetPath"]).resolve(); apphost = target.with_suffix(""); sqlite = target.parent / "libe_sqlite3.so"
        if (evaluated["TargetFramework"] != "net10.0" or evaluated["RuntimeIdentifier"] != "linux-x64" or evaluated["UseAppHost"].lower() != "true"
            or Path(evaluated["TargetDir"]).resolve() != target.parent or not target.is_relative_to(artifacts) or not target.is_file()
            or not apphost.is_file() or not os.access(apphost, os.X_OK) or not sqlite.is_file()): raise RuntimeError("Actual managed/apphost/native SQLite closure absent")
        with target.open("rb") as stream:
            if stream.read(2) != b"MZ": raise RuntimeError("Actual managed entry header missing")
        for path in [apphost, sqlite]:
            with path.open("rb") as stream:
                if stream.read(5) != b"\x7fELF\x02": raise RuntimeError("Actual Linux64 apphost/SQLite required")
        runtime = runtime_pins(target.parent); write(diagnostics / "runtime-before.json", runtime)
        result["entry"] = {"dll": digest(target), "apphost": digest(apphost), "sqlite": digest(sqlite)}
        phases = {}
        for mode in ["seed", "verify"]:
            # Separate original native apphost processes. Commands must fully drain seed
            # before it returns, so verify cannot observe an alive seed provider.
            log = commands.run("native-" + mode, [str(apphost), mode, str(fixture)], 90)
            native = commands.records[-1]
            if not (native["exit"] == 0 and native["exitAfterDrain"] == 0 and native["normalEOF"] and native["familyClosed"]
                    and native["finalECHILD"] and not native["signals"] and native["births"] and all(b["gone"] for b in native["births"])):
                raise RuntimeError("Native phase family did not close normally")
            phases[mode] = validate_phase(fixture / (mode + "-results.json"), mode, log)
            write(diagnostics / (mode + "-results.json"), phases[mode])
        result.update(status="PASS", phases=phases, distinctCriteria=3, phaseExecutions=6)
    except Exception as error:
        result.update(status="FAIL", error=repr(error))
    finally:
        # Preserve actual first failed/incomplete owner result even when Commands
        # refuses its native nonzero exit; fixture DB and identity receipt stay private.
        for mode in ["seed", "verify"]:
            path = fixture / (mode + "-results.json")
            if path.is_file():
                try: write(diagnostics / (mode + "-results.json"), json.loads(path.read_text()))
                except Exception as error: result.update(status="FAIL", resultReadError=repr(error))
        try:
            after = source_check(root); write(diagnostics / "source-after.json", after)
            if before is not None and before != after: raise RuntimeError("Actual owner source changed")
            if target is not None and runtime is not None:
                after_runtime = runtime_pins(target.parent); write(diagnostics / "runtime-after.json", after_runtime)
                if runtime != after_runtime: raise RuntimeError("Actual native runtime changed")
            if commands is not None:
                commands.run("git-clean-after", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
                if commands.run("git-status-after", ["git", "status", "--porcelain", "--untracked-files=all"], 15).strip(): raise RuntimeError("Actual source checkout gained unknown files")
        except Exception as error: result.update(status="FAIL", custodyError=repr(error))
        records = [] if commands is None else commands.records
        closed = bool(records) and all(x["error"] is None and x["exit"] == 0 and x["normalEOF"] and x["familyClosed"] and x["finalECHILD"]
                 and not x["signals"] and all(b["gone"] for b in x["births"]) for x in records)
        result["allCommandFamiliesNormalClosed"] = closed
        result["accepted"] = result["status"] == "PASS" and closed
        result["custodyQualification"] = "Runtime/body hash metadata only; binaries/package bodies/DB/WAL/fixture identity receipt not uploaded. Original local initialization and branch switches are real mutations; not hosted permission/Den acceptance."
        write(diagnostics / "result.json", result)
    print(json.dumps(result, indent=2))
    return 0 if result["accepted"] else 1

if __name__ == "__main__": raise SystemExit(main())
