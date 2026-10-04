#!/usr/bin/env python3
"""Real native Render/CUI/font geometry controls; no Wave action or browser claim."""
import argparse
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import re

COMMANDS_SHA256 = "a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38"
LEDGER_SHA256 = "48e156dc8cdb38d41128774f1f6031648a2b5c392ded3613f265ee89388a4a75"
DIRECTORY = "apps/Web/Wave/Tests"
PROJECT = DIRECTORY + "/WaveResponsiveLayout.Tests.csproj"
LEDGER = DIRECTORY + "/responsive-source-pins.json"
INITIAL = ["Actual native fonts equal requested baseline factor",
           "Actual native client is requested width", "Outer policy is opt-in only"]
OPT_IN = ["Wave native outer extent fits viewport", "Every actual Wave button fits horizontally",
          "Every actual Wave input fits horizontally", "Every full native Wave glyph range is retained",
          "Every full native Wave glyph fits assigned bounds", "Every full native Wave text stays inside root",
          "Actual wrapped siblings never overlap", "Canonical label projections are complete",
          "Native controls keep usable focus and names", "Deliberate waveform keeps local horizontal scrolling"]
GROUPS = [("narrow", 390, 844, "wave-opt-in", 1),
          ("narrow-native-font2", 390, 844, "wave-opt-in", 2),
          ("geometry-stress195-not-zoom", 195, 844, "wave-opt-in", 1),
          ("wide", 1200, 844, "wave-opt-in", 1),
          ("default-narrow", 390, 844, "default", 1),
          ("default-narrow-native-font2", 390, 844, "default", 2),
          ("default-wide", 1200, 844, "default", 1)]


def digest(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(block)
    return result.hexdigest()


def write_json(path, value):
    data = (json.dumps(value, indent=2) + "\n").encode()
    if len(data) > 8 * 1024 * 1024:
        raise RuntimeError("Bounded native diagnostic metadata overflow")
    path.write_bytes(data)


def check_sources(root, ledger):
    for relative, pin in ledger["files"].items():
        path = root / relative
        if (path.is_symlink() or not path.is_file() or path.stat().st_size != pin["bytes"]
                or digest(path) != pin["sha256"]):
            raise RuntimeError("Reviewed selected source differs: " + relative)


def normal(record, actual_exit):
    return (record["error"] is None and record["exit"] == actual_exit
            and record["normalEOF"] and record["familyClosed"] and record["finalECHILD"]
            and not record["signals"] and all(birth["gone"] for birth in record["births"]))


def native_result(native, group, variant, record, log):
    name, width, height, mode, factor = group
    expected_names = INITIAL + (OPT_IN if mode == "wave-opt-in" else ["Default owner host behavior is unchanged"])
    checks = native["checks"]
    if ([item["name"] for item in checks] != expected_names
            or any(type(item["passed"]) is not bool for item in checks)
            or native["width"] != width or native["height"] != height
            or native["fontFactor"] != factor or native["optIn"] != (mode == "wave-opt-in")
            or native["policyPresent"] != (variant == "proposed")):
        raise RuntimeError("Native case inputs/names/policy/completion differ: " + name)
    failed = sum(not item["passed"] for item in checks)
    actual_exit = 1 if failed else 0
    if native["failures"] != failed or not normal(record, actual_exit):
        raise RuntimeError("Native outcome/custody disagrees with completed checks: " + name)
    if log.splitlines() != [("PASS: " if item["passed"] else "FAIL: ") + item["name"] for item in checks]:
        raise RuntimeError("Actual native completion log differs: " + name)
    fonts = native["nativeFonts"]
    if not fonts or any(not math.isfinite(row["before"]) or row["before"] <= 0
                        or not math.isfinite(row["actual"])
                        or abs(row["actual"] - row["before"] * factor) >= 0.000001 for row in fonts):
        raise RuntimeError("Native actual font factor prerequisite failed: " + name)
    if not all(item["passed"] for item in checks[:3]):
        raise RuntimeError("Font/client/host-policy prerequisite cannot qualify a layout negative: " + name)
    return {"name": name, "state": "FAIL" if failed else "PASS", "executed": len(checks),
            "passed": len(checks) - failed, "failed": failed,
            "checks": checks, "nativeExit": record["exit"], "negativeIsAcceptance": False}


def binaries(artifacts):
    return [{"path": str(path.relative_to(artifacts)), "bytes": path.stat().st_size, "sha256": digest(path)}
            for path in sorted(artifacts.rglob("*")) if path.is_file()
            and path.suffix in (".dll", ".pdb", ".so", ".ttf", ".otf")]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--variant", choices=("original", "proposed"), required=True)
    parser.add_argument("--expected-commit", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root, output = Path.cwd().resolve(), args.output.resolve()
    if output.exists() or output.is_relative_to(root):
        raise RuntimeError("Require fresh owned output outside checkout")
    output.mkdir(parents=True)
    diagnostics = output / "diagnostics"
    diagnostics.mkdir()
    result = {"status": "NOT_RUN", "variant": args.variant, "expectedGroups": 7, "expectedChecks": 64,
              "executed": 0, "passed": 0, "failed": 0, "notRun": 64, "groups": [],
              "scope": "Actual native Render/parser/font/glyph geometry with scripted bindings and availability; dispatch always throws. Native font2 and viewport195 are NOT browser zoom. No Wave model/audio/storage/action/AT/full-parity acceptance."}
    commands, ledger, binary_before = None, None, None
    driver = Path(__file__).resolve()
    driver_before = digest(driver)
    try:
        custodian = root / "apps/Web/Tests/ci/run-ordinary-native.py"
        if digest(custodian) != COMMANDS_SHA256:
            raise RuntimeError("Maintained Commands changed; exact owner review required")
        spec = importlib.util.spec_from_file_location("maintained_wave_layout_commands", custodian)
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
            raise RuntimeError("Actual checkout differs from github.sha")
        result["sourceCommit"] = head
        commands.run("git-clean-before", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
        commands.run("tracked-source-tree", ["git", "ls-tree", "-r", "HEAD"], 15)
        commands.run("native-submodule-pins", ["git", "submodule", "status", "--recursive", "--",
                     "framework/CUI/vendor/Avalonia/external/XamlX", "framework/CUI/vendor/Avalonia/external/Avalonia.DBus"], 15)
        if commands.run("dotnet-version", ["dotnet", "--version"], 30).strip() != "10.0.401":
            raise RuntimeError("Requires maintained SDK10.0.401")
        commands.run("dotnet-info", ["dotnet", "--info"], 30)
        if digest(root / LEDGER) != LEDGER_SHA256:
            raise RuntimeError("Reviewed native source ledger changed")
        ledger = json.loads((root / LEDGER).read_text())
        check_sources(root, ledger)
        write_json(diagnostics / "selected-source-before.json", ledger)
        artifacts = output / "artifacts"
        owner_task = artifacts / "bin/Avalonia.Build.Tasks/release/Avalonia.Build.Tasks.dll"
        props = ["-p:SelfContained=false", "-p:UseSharedCompilation=false", "-p:AvaloniaBuildTasksLocation=" + str(owner_task),
                 "-p:CreateHardLinksForCopyAdditionalFilesIfPossible=false", "-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=false",
                 "-p:CreateHardLinksForCopyLocalIfPossible=false", "-p:CreateHardLinksForPublishFilesIfPossible=false"]
        if args.variant == "original":
            props += ["-p:BrowserApplicationSource=" + str(root / DIRECTORY / "Fixtures/Original/BrowserApplication.cs"),
                      "-p:BrowserSurfaceRegistrySource=" + str(root / DIRECTORY / "Fixtures/Original/BrowserSurfaceRegistry.cs")]
        commands.run("restore", ["dotnet", "restore", PROJECT, "--artifacts-path", str(artifacts), "-r", "linux-x64",
                     "--configfile", str(root / "NuGet.Config"), "--disable-build-servers", "-p:Configuration=Release",
                     "-m:1", "-nodeReuse:false"] + props, 600)
        commands.run("build", ["dotnet", "build", PROJECT, "--no-restore", "-c", "Release", "--artifacts-path", str(artifacts),
                     "-r", "linux-x64", "--disable-build-servers", "-m:1", "-nodeReuse:false"] + props, 1200)
        evaluated = module.parse_sdk(commands.run("target-properties", ["dotnet", "msbuild", PROJECT,
                     "-p:Configuration=Release", "-p:RuntimeIdentifier=linux-x64", "-p:UseArtifactsOutput=true",
                     "-p:ArtifactsPath=" + str(artifacts), "-nodeReuse:false",
                     "-getProperty:TargetPath,TargetFramework,RuntimeIdentifier,UseAppHost,AvaloniaBuildTasksLocation,BrowserApplicationSource,BrowserSurfaceRegistrySource"] + props, 30))
        target = Path(evaluated["TargetPath"]).resolve()
        apphost = target.with_suffix("")
        expected_app = root / (DIRECTORY + "/Fixtures/Original/BrowserApplication.cs" if args.variant == "original" else "apps/Web/BrowserApplication.cs")
        expected_registry = root / (DIRECTORY + "/Fixtures/Original/BrowserSurfaceRegistry.cs" if args.variant == "original" else "apps/Web/BrowserSurfaceRegistry.cs")
        if (evaluated["TargetFramework"] != "net10.0" or evaluated["RuntimeIdentifier"] != "linux-x64"
                or evaluated["UseAppHost"].lower() != "true" or not target.is_relative_to(artifacts)
                or not target.is_file() or not apphost.is_file() or not os.access(apphost, os.X_OK)
                or Path(evaluated["AvaloniaBuildTasksLocation"]).resolve() != owner_task or not owner_task.is_file()
                or Path(evaluated["BrowserApplicationSource"]).resolve() != expected_app
                or Path(evaluated["BrowserSurfaceRegistrySource"]).resolve() != expected_registry):
            raise RuntimeError("Actual isolated maintained native/source variant closure missing")
        with target.open("rb") as stream:
            if stream.read(2) != b"MZ":
                raise RuntimeError("Actual managed entry header absent")
        with apphost.open("rb") as stream:
            if stream.read(4) != b"\x7fELF":
                raise RuntimeError("Actual Linux apphost header absent")
        write_json(diagnostics / "target-properties.json", evaluated)
        binary_before = binaries(artifacts)
        write_json(diagnostics / "selected-native-binary-before.json", binary_before)
        result.update(sourceLedgerSHA256=LEDGER_SHA256, driverSHA256=driver_before,
                      entrySHA256=digest(target), apphostSHA256=digest(apphost))
        markup = root / (DIRECTORY + "/Fixtures/Original/Wave.cui" if args.variant == "original" else "apps/Web/Wave/Wave.cui")
        for group in GROUPS:
            name, width, height, mode, factor = group
            report = diagnostics / (name + "-native.json")
            deferred = None
            try:
                commands.run(name, [str(apphost), str(markup), str(report), str(width), str(height), mode, str(factor)], 120)
            except Exception as error:
                deferred = error
            # Preserve actual report BEFORE any expected native-exit/custody/geometry gate.
            if not report.is_file() or report.stat().st_size > 8 * 1024 * 1024:
                raise RuntimeError("Native structured report absent/unbounded; no expected RED inferred: " + name)
            native = json.loads(report.read_text())
            summary = {"name": name, "state": "UNVALIDATED", "nativeResultSHA256": digest(report),
                       "bytes": report.stat().st_size, "actualChecks": native.get("checks"), "actualFailures": native.get("failures")}
            result["groups"].append(summary)
            actual_checks = native.get("checks", [])
            if isinstance(actual_checks, list):
                result["executed"] += len(actual_checks)
                result["passed"] += sum(item.get("passed") is True for item in actual_checks if isinstance(item, dict))
                result["failed"] += sum(item.get("passed") is False for item in actual_checks if isinstance(item, dict))
                result["notRun"] = max(0, 64 - result["executed"])
            write_json(diagnostics / "result.json", result)
            record = commands.records[-1]
            log = (diagnostics / (name + ".log")).read_text(errors="replace")
            validated = native_result(native, group, args.variant, record, log)
            # Nonzero native exits are accepted ONLY via completed same-source checks,
            # exact natural exit1 and ordinary zero-signal EOF/ECHILD custody above.
            if deferred is not None and record["exit"] != 1:
                raise deferred
            summary.update(validated)
        if args.variant == "original":
            # Accept the intended old-layout negative only after every independent
            # group completed and all unchanged default-policy checks passed.
            if any(group["failed"] for group in result["groups"] if group["name"].startswith("default-")):
                raise RuntimeError("Required original/default geometry checks failed")
            critical = result["groups"][0]
            states = {item["name"]: item["passed"] for item in critical["checks"]}
            if states["Wave native outer extent fits viewport"] is not False:
                raise RuntimeError("Original390 did not demonstrate the actual required clipping negative")
            result["status"] = "ORIGINAL_LAYOUT_RED_CONTROL_OBSERVED"
        elif result["passed"] == 64 and result["failed"] == 0:
            result["status"] = "PROPOSED_NATIVE_LAYOUT_PASS"
        else:
            raise RuntimeError("Proposed actual64 native geometry outcomes did not pass")
    except Exception as error:
        result.update(status="FAIL", error=repr(error))
    finally:
        if ledger is not None:
            try:
                check_sources(root, ledger)
                if digest(root / LEDGER) != LEDGER_SHA256 or digest(driver) != driver_before:
                    raise RuntimeError("Source ledger/driver changed during actual execution")
                write_json(diagnostics / "selected-source-after.json", ledger)
                result["selectedSourceAfterUnchanged"] = True
            except Exception as error:
                result.update(status="FAIL", sourceError=repr(error))
        if binary_before is not None:
            try:
                after = binaries(output / "artifacts")
                write_json(diagnostics / "selected-native-binary-after.json", after)
                if after != binary_before or digest(target) != result["entrySHA256"] or digest(apphost) != result["apphostSHA256"]:
                    raise RuntimeError("Selected native entry/closure changed during controls")
                result["selectedBinaryAfterUnchanged"] = True
            except Exception as error:
                result.update(status="FAIL", binaryError=repr(error))
        if commands is not None:
            try:
                commands.run("git-clean-after", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
            except Exception as error:
                result.update(status="FAIL", sourceCustodyError=repr(error))
        write_json(diagnostics / "result.json", result)
    print(json.dumps(result, indent=2))
    return 0 if result["status"] in ("ORIGINAL_LAYOUT_RED_CONTROL_OBSERVED", "PROPOSED_NATIVE_LAYOUT_PASS") else 1


if __name__ == "__main__":
    raise SystemExit(main())
