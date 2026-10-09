#!/usr/bin/env python3
"""Normal source-built Chat supporting controls, using the maintained B1 command custodian.

Expected negative controls remain FAIL outcomes; they are never native acceptance.
Only bounded command logs, hashes and outcome metadata belong in diagnostics.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re

B1_COMMANDS_SHA256 = "a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38"
VARIANTS = ("original", "current")
SOURCE_PINS_SHA256 = "4a0f83ab34769687da1534f154056645008d6f1c04e69a80c186bb9ad419705c"
PROJECT = "apps/Web/AI/Tests/ChatRead.Tests.csproj"


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(chunk)
    return value.hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def check_sources(root, pins):
    for relative, pin in pins["sourceFiles"].items():
        path = root / relative
        if path.is_symlink() or not path.is_file() or path.stat().st_size != pin["bytes"] or digest(path) != pin["sha256"]:
            raise RuntimeError("Reviewed Chat/owner source changed: " + relative)


def normal_command(record, expected_exit):
    return (record["name"] == "native" and record["error"] is None and record["exit"] == expected_exit
            and record["exitAfterDrain"] == expected_exit and record["normalEOF"] and record["familyClosed"]
            and record["finalECHILD"] and not record["signals"] and record["births"]
            and all(item["gone"] for item in record["births"]))


def validate_native(variant, pins, log, actual_exit):
    expected = pins["cases"]
    passed = re.findall(r"^PASS ([^\r\n]+)$", log, re.MULTILINE)
    failed = re.findall(r"^FAIL (.*?) System\.[^\r\n]+", log, re.MULTILINE)
    if (len(passed) + len(failed) != 10 or len(set(passed + failed)) != 10
            or set(passed + failed) != set(expected)):
        raise RuntimeError("All ten unique same-oracle controls must complete")
    counts = (9, 1) if variant == "original" else (10, 0)
    summary = f"SUPPORTING_SCRIPTED_INTERFACE_UNIT discovered=10 executed=10 pass={counts[0]} fail={counts[1]} skip=0 notRun=0 timedOut=False"
    if len(re.findall("^" + re.escape(summary) + "$", log, re.MULTILINE)) != 1 or (len(passed), len(failed)) != counts:
        raise RuntimeError("Complete supporting interface-unit counters differ")
    expected_exit = 1 if variant == "original" else 0
    if actual_exit != expected_exit:
        raise RuntimeError("Wrong actual native exit")
    if variant == "original":
        if failed != [pins["expectedOriginalFailedCase"]]:
            raise RuntimeError("Original source failed a different control")
        exact = "FAIL " + failed[0] + " System.InvalidOperationException: " + pins["expectedFailureMessage"]
        if len(re.findall("^" + re.escape(exact) + "$", log, re.MULTILINE)) != 1:
            raise RuntimeError("Original failure mechanism differs from denied-refresh assertion")
    return {"status": "EXPECTED_RED_CONTROL" if variant == "original" else "SUPPORTING_INTERFACE_UNIT_PASS",
            "discovered": 10, "executed": 10, "passed": len(passed), "failed": len(failed),
            "notRun": 0, "skip": 0, "timedOut": False, "failedCases": failed,
            "actualNativeExit": actual_exit, "negativeIsAcceptance": False,
            "qualification": pins["qualification"]}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--variant", required=True, choices=VARIANTS)
    parser.add_argument("--expected-commit", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[4]
    output = args.output.resolve()
    if Path.cwd().resolve() != root or output.exists() or output.is_relative_to(root):
        raise RuntimeError("Require repository cwd and fresh task-owned output outside checkout")
    output.mkdir(parents=True)
    diagnostics = output / "diagnostics"
    diagnostics.mkdir()
    result = {"status": "NOT_RUN", "variant": args.variant, "expected": 10,
              "scope": "Supporting scripted Chat interface units only; provider/durable/browser/current access enforcement NOT_RUN",
              "preservedHistory": "Original nine-control authored fixture and all historical source/evidence remain separate; no results inherited"}
    commands = None
    source_pins = None
    source_pins_sha = None
    try:
        custodian = root / "apps/Web/Tests/ci/run-ordinary-native.py"
        if digest(custodian) != B1_COMMANDS_SHA256:
            raise RuntimeError("Maintained B1 Commands changed; review exact owner revision before execution")
        spec = importlib.util.spec_from_file_location("maintained_b1_native_commands", custodian)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        env = os.environ.copy()
        for variable, directory in {"DOTNET_CLI_HOME": "cli", "NUGET_PACKAGES": "nuget",
                                    "NUGET_HTTP_CACHE_PATH": "http", "NUGET_PLUGINS_CACHE_PATH": "plugins",
                                    "XDG_CACHE_HOME": "font-cache", "TMPDIR": "tmp", "TMP": "tmp", "TEMP": "tmp",
                                    "HAVEN_DATA_DIR": "fixture-data"}.items():
            owned = output / directory
            owned.mkdir(exist_ok=True)
            env[variable] = str(owned)
        env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE="false", DOTNET_CLI_TELEMETRY_OPTOUT="1",
                   DOTNET_NOLOGO="1", MSBUILDDISABLENODEREUSE="1")
        commands = module.Commands(diagnostics, env, root)
        head = commands.run("git-head", ["git", "rev-parse", "HEAD"], 15).strip()
        if not re.fullmatch(r"[0-9a-f]{40}", args.expected_commit) or head != args.expected_commit:
            raise RuntimeError("Actual checkout differs from expected github.sha")
        result["sourceCommit"] = head
        commands.run("git-clean-before", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
        commands.run("tracked-source-tree", ["git", "ls-tree", "-r", "HEAD"], 15)
        commands.run("native-submodule-pins", ["git", "submodule", "status", "--recursive", "--",
                     "framework/CUI/vendor/Avalonia/external/XamlX", "framework/CUI/vendor/Avalonia/external/Avalonia.DBus"], 15)
        if commands.run("dotnet-version", ["dotnet", "--version"], 30).strip() != "10.0.401":
            raise RuntimeError("Requires maintained SDK10.0.401")
        commands.run("dotnet-info", ["dotnet", "--info"], 30)
        project = PROJECT
        directory = root / Path(project).parent
        if digest(directory / "source-pins.json") != SOURCE_PINS_SHA256:
            raise RuntimeError("Reviewed source/oracle pin ledger changed")
        source_pins = json.loads((directory / "source-pins.json").read_text())
        check_sources(root, source_pins)
        source_pins_sha = digest(directory / "source-pins.json")
        artifacts = output / "artifacts"
        owner_task = artifacts / "bin/Avalonia.Build.Tasks/release/Avalonia.Build.Tasks.dll"
        props = ["-p:SelfContained=false", "-p:UseSharedCompilation=false", "-p:ChatAdapterSource=" + str(root / ("apps/Web/AI/Tests/Fixtures/Original/ChatSpaceBrowserReadAdapter.cs" if args.variant == "original" else "apps/Web/AI/Chat/ChatSpaceBrowserReadAdapter.cs")),
                 "-p:AvaloniaBuildTasksLocation=" + str(owner_task),
                 "-p:CreateHardLinksForCopyAdditionalFilesIfPossible=false", "-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=false",
                 "-p:CreateHardLinksForCopyLocalIfPossible=false", "-p:CreateHardLinksForPublishFilesIfPossible=false"]
        commands.run("restore", ["dotnet", "restore", project, "--artifacts-path", str(artifacts), "-r", "linux-x64",
                     "--configfile", str(root / "NuGet.Config"), "--disable-build-servers", "-p:Configuration=Release",
                     "-m:1", "-nodeReuse:false"] + props, 600)
        commands.run("build", ["dotnet", "build", project, "--no-restore", "-c", "Release", "--artifacts-path", str(artifacts),
                     "-r", "linux-x64", "--disable-build-servers", "-m:1", "-nodeReuse:false"] + props, 1200)
        evaluated = module.parse_sdk(commands.run("target-properties", ["dotnet", "msbuild", project,
                     "-p:Configuration=Release", "-p:RuntimeIdentifier=linux-x64", "-p:UseArtifactsOutput=true",
                     "-p:ArtifactsPath=" + str(artifacts), "-nodeReuse:false",
                     "-getProperty:TargetPath,TargetFramework,RuntimeIdentifier,UseAppHost,AvaloniaBuildTasksLocation"] + props, 30))
        target = Path(evaluated["TargetPath"]).resolve()
        apphost = target.with_suffix("")
        if (evaluated["TargetFramework"] != "net10.0" or evaluated["RuntimeIdentifier"] != "linux-x64"
                or evaluated["UseAppHost"].lower() != "true" or not target.is_relative_to(artifacts)
                or not target.is_file() or not apphost.is_file() or not os.access(apphost, os.X_OK) or not owner_task.is_file()):
            raise RuntimeError("Real isolated owner task/entry DLL/apphost closure missing")
        with target.open("rb") as stream:
            if stream.read(2) != b"MZ":
                raise RuntimeError("Actual managed entry header absent")
        with apphost.open("rb") as stream:
            if stream.read(4) != b"\x7fELF":
                raise RuntimeError("Actual Linux apphost header absent")
        write_json(diagnostics / "target-properties.json", evaluated)
        inventory = [{"path": str(file.relative_to(artifacts)), "bytes": file.stat().st_size, "sha256": digest(file)}
                     for file in sorted(artifacts.rglob("*")) if file.is_file() and file.suffix in (".dll", ".pdb")]
        write_json(diagnostics / "actual-native-binary-pins.json", inventory)
        result["entryDLLSHA256"] = digest(target)
        result["apphostSHA256"] = digest(apphost)
        result["nativeStarted"] = True
        expected_exit = 0 if args.variant == "current" else 1
        try:
            log = commands.run("native", [str(apphost)], 120)
        except RuntimeError:
            # Commands rejects nonzero as a command failure. Only a genuine normal, fully drained
            # expected native exit1 is admitted as a negative control, never forced cleanup.
            record = commands.records[-1]
            if expected_exit != 1 or record["name"] != "native" or not normal_command(record, 1):
                raise
            log = (diagnostics / "native.log").read_text(errors="strict")
        native_record = commands.records[-1]
        if not normal_command(native_record, expected_exit):
            raise RuntimeError("Actual native process exit/family is not the required normal result")
        result.update(validate_native(args.variant, source_pins, log, native_record["exit"]))
        result["nativeLogSHA256"] = digest(diagnostics / "native.log")
        result["fixtureSourcePinManifestSHA256"] = source_pins_sha
        check_sources(root, source_pins)
        if digest(directory / "source-pins.json") != source_pins_sha:
            raise RuntimeError("Preserved source pin ledger changed during native execution")
    except Exception as error:
        result.update(status="FAIL", error=repr(error))
    finally:
        if source_pins is not None:
            try:
                check_sources(root, source_pins)
                if digest(directory / "source-pins.json") != source_pins_sha:
                    raise RuntimeError("Source pin ledger changed")
                result["sourceAfterUnchanged"] = True
            except Exception as error:
                result.update(status="FAIL", sourcePinError=repr(error))
        if commands is not None:
            try:
                commands.run("git-clean-after", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
            except Exception as error:
                result.update(status="FAIL", sourceCustodyError=repr(error))
        write_json(diagnostics / "result.json", result)
    print(json.dumps(result, indent=2))
    return 0 if result["status"] in ("SUPPORTING_INTERFACE_UNIT_PASS", "EXPECTED_RED_CONTROL") else 1


if __name__ == "__main__":
    raise SystemExit(main())
