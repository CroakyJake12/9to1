#!/usr/bin/env python3
"""Normal source-built Picture controls, using the maintained B1 command custodian.

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
VARIANTS = {"production-original": 13, "admission01": 15, "current": 15, "production-original-repaired-markup": 13, "admission01-repaired-markup": 15}
OBSERVER_FAILURES = {
    "candidate-publication-cannot-write-detached-previous-input":
        "Real synchronous candidate observer cannot write a soon-retired previous owner.",
    "candidate-observer-reentrant-navigation-waits-coherent-retirement":
        "Reentrant route sees published candidate and waits actual old-session retirement; it cannot reject an unpublished candidate.",
}
FIRST_ORIGINAL_FAILURE = "independent-target-prepare-and-reject-preserve-active-bitmap-and-canonical"
FIRST_ORIGINAL_MESSAGE = "Preparing another document never mutates the active canonical session or tears down its real Bitmap."


def digest(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for part in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(part)
    return result.hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def check_sources(project_directory, variant, expected_pins):
    for relative, pin in expected_pins["sourceFiles"].items():
        source = project_directory / relative
        if source.is_symlink() or not source.is_file() or source.stat().st_size != pin["bytes"] or digest(source) != pin["sha256"]:
            raise RuntimeError("Preserved controlled source changed: " + relative)
    png = project_directory / expected_pins["png"]["path"]
    pin = expected_pins["png"]
    if not png.is_file() or png.is_symlink() or png.stat().st_size != pin["bytes"] or digest(png) != pin["sha256"]:
        raise RuntimeError("Real reviewed PNG fixture missing/changed; no substitute input")
    if variant == "current":
        for relative, pin in expected_pins["currentActualSource"].items():
            source = project_directory.parent.parent / "Picture" / relative
            if not source.is_file() or source.is_symlink() or source.stat().st_size != pin["bytes"] or digest(source) != pin["sha256"]:
                raise RuntimeError("Current Picture source is not the reviewed admission04: " + relative)


def normal_command(record, expected_exit):
    return (record["error"] is None and record["exit"] == expected_exit
            and record["normalEOF"] and record["familyClosed"] and record["finalECHILD"]
            and not record["signals"] and all(item["gone"] for item in record["births"]))


def validate_native(variant, native, log, actual_exit):
    expected = VARIANTS[variant]
    if variant.endswith("-repaired-markup") and native.get("actualMarkupDiagnostics") != []:
        raise RuntimeError("Repaired-markup control still has actual parser diagnostics")
    if (native["discovered"] != expected or native["executed"] != expected
            or native["notRun"] != 0 or native["prerequisite"] is not None or native["timedOut"]
            or len(native["results"]) != expected or native["exitCode"] != actual_exit):
        raise RuntimeError("Native control discovery/completion/prerequisite differs from required source criteria")
    states = {item["Name"]: item for item in native["results"]}
    if len(states) != expected:
        raise RuntimeError("Duplicate native control name")
    passed = sum(item["state"] == "PASS" for item in states.values())
    failed = sum(item["state"] == "FAIL" for item in states.values())
    if (passed, failed) != (native["passed"], native["failed"]) or passed + failed != expected:
        raise RuntimeError("Native counters do not agree with actual source-written outcomes")
    if len(re.findall(r"^PASS ", log, re.MULTILINE)) != passed or len(re.findall(r"^FAIL ", log, re.MULTILINE)) != failed:
        raise RuntimeError("Native command log differs from durable result outcomes")
    if variant == "current":
        if actual_exit != 0 or (passed, failed) != (15, 0):
            raise RuntimeError("Current actual fifteen controls did not all pass")
        status = "CURRENT_NATIVE_PASS"
    elif variant in ("admission01", "admission01-repaired-markup"):
        actual_failures = {name for name, item in states.items() if item["state"] == "FAIL"}
        if actual_exit != 1 or (passed, failed) != (13, 2) or actual_failures != set(OBSERVER_FAILURES):
            raise RuntimeError("Preserved admission01 did not show exactly the two intended same-oracle observer negatives")
        for name, required in OBSERVER_FAILURES.items():
            if required not in states[name].get("failure", ""):
                raise RuntimeError("Wrong failure mechanism for intended observer control: " + name)
        status = "PRESERVED_ADMISSION01_CONTROL_RED_OBSERVED" if variant == "admission01" else "ADMISSION01_SESSION_REPAIRED_MARKUP_CONTROL_RED_OBSERVED"
    else:
        first = states[FIRST_ORIGINAL_FAILURE]
        if actual_exit != 1 or first["state"] != "FAIL" or FIRST_ORIGINAL_MESSAGE not in first.get("failure", ""):
            raise RuntimeError("Unchanged production did not exhibit the intended eager-mutation negative")
        status = "PRESERVED_PRODUCTION_CONTROL_RED_OBSERVED" if variant == "production-original" else "PRODUCTION_ORIGINAL_SESSION_REPAIRED_MARKUP_CONTROL_RED_OBSERVED"
    # No actual canonical IDs, source paths, bundle receipts, GPS metadata, fixture pixels or stores uploaded.
    return {"status": status, "discovered": expected, "executed": expected,
            "passed": passed, "failed": failed, "notRun": 0,
            "assertions": native["assertions"],
            "outcomes": [{"name": name, "state": item["state"], "assertions": item["assertions"]}
                         for name, item in states.items()],
            "negativeIsAcceptance": False,
            "qualification": "Actual native CUI/Bitmap/codec with controlled media boundary; NOT browser IDB/provider/permissions/full Picture parity"}


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
    result = {"status": "NOT_RUN", "variant": args.variant, "expected": VARIANTS[args.variant],
              "scope": "Controlled actual native Picture criteria only; all browser/provider/durable restart acceptance NOT_RUN",
              "preservedHistory": "Original Picture GPS Preserve RED, old admission01/source-review and all prior browser/native evidence are not replaced"}
    commands = None
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
        project = "apps/Web/Tests/PictureAdmission/PictureAdmission.Tests.csproj"
        directory = root / Path(project).parent
        source_pins = json.loads((directory / "fixture-source-pins.json").read_text())
        check_sources(directory, args.variant, source_pins)
        if args.variant.endswith("-repaired-markup"):
            check_sources(directory, "current", source_pins)
            result["markupControl"] = "Additive preserved Feature/Session/case-body control with only current authored-ID markup; historical fixture CUI remains unchanged"
        source_pins_sha = digest(directory / "fixture-source-pins.json")
        artifacts = output / "artifacts"
        owner_task = artifacts / "bin/Avalonia.Build.Tasks/release/Avalonia.Build.Tasks.dll"
        props = ["-p:SelfContained=false", "-p:UseSharedCompilation=false", "-p:PictureControlVariant=" + args.variant,
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
        write_json(diagnostics / "target-properties.json", evaluated)
        inventory = [{"path": str(file.relative_to(artifacts)), "bytes": file.stat().st_size, "sha256": digest(file)}
                     for file in sorted(artifacts.rglob("*")) if file.is_file() and file.suffix in (".dll", ".pdb")]
        write_json(diagnostics / "actual-native-binary-pins.json", inventory)
        fixture_output = output / "fixture-data/picture"
        result["nativeStarted"] = True
        expected_exit = 0 if args.variant == "current" else 1
        native_command_error = None
        log = None
        try:
            log = commands.run("native", [str(apphost), str(fixture_output)], 120)
        except RuntimeError as error:
            # Retain the real command failure, but capture its actual fixture
            # outcomes before ANY expected-exit or intended-negative gate.
            native_command_error = error
        native_record = commands.records[-1]
        result["actualNativeCommandRecord"] = native_record
        if native_command_error is not None:
            result["actualNativeCommandException"] = repr(native_command_error)
        native_path = fixture_output / "results.json"
        if not native_path.is_file():
            result["nativeResultCaptureState"] = "ABSENT"
            if native_command_error is not None:
                raise native_command_error
            raise RuntimeError("Actual native fixture result is absent; no outcomes invented")
        result["nativeResultSHA256"] = digest(native_path)
        result["nativeResultBytes"] = native_path.stat().st_size
        if result["nativeResultBytes"] > 8 * 1024 * 1024:
            result["nativeResultCaptureState"] = "OVERSIZE_NOT_COPIED"
            raise RuntimeError("Actual controlled native result exceeds bounded8MiB diagnostics; no partial outcome accepted")
        native_bytes = native_path.read_bytes()
        if len(native_bytes) != result["nativeResultBytes"] or hashlib.sha256(native_bytes).hexdigest() != result["nativeResultSHA256"]:
            result["nativeResultCaptureState"] = "CHANGED_DURING_READ_NOT_COPIED"
            raise RuntimeError("Actual native fixture result changed during bounded capture")
        # Root explicitly authorizes full exceptions/stacks/IDs/paths for ONLY
        # this approved synthetic PNG/local native fixture, not private providers.
        (diagnostics / "native-results.json").write_bytes(native_bytes)
        result["nativeResultDiagnostic"] = "native-results.json"
        result["nativeResultCaptureState"] = "RAW_CAPTURED_JSON_NOT_PARSED"
        native = json.loads(native_bytes)
        result["actualNativeCounters"] = {key: native.get(key) for key in
            ("discovered", "executed", "passed", "failed", "notRun", "timedOut", "prerequisite", "assertions", "exitCode")}
        result["nativeResultCaptureState"] = "STRUCTURED_CAPTURED_NOT_VALIDATED"
        # Commands still rejects nonzero/timeout/forced cleanup. Only a genuine
        # normal expected native exit1 is eligible as the existing negative.
        if native_command_error is not None:
            if expected_exit != 1 or native_record["name"] != "native" or not normal_command(native_record, 1):
                raise native_command_error
        if not normal_command(native_record, expected_exit):
            raise RuntimeError("Actual native process exit/family is not the required normal result")
        if log is None:
            log = (diagnostics / "native.log").read_text(errors="strict")
        selected_markup_pin = (source_pins["currentActualSource"]["Picture.cui"]
                               if args.variant == "current" or args.variant.endswith("-repaired-markup")
                               else source_pins["sourceFiles"]["Fixtures/" + ("ProductionOriginal" if args.variant == "production-original" else "Admission01") + "/Picture.cui"])
        if native.get("actualMarkupSHA256", "").lower() != selected_markup_pin["sha256"]:
            raise RuntimeError("Actual embedded Picture markup differs from explicit selected source tuple")
        result.update(validate_native(args.variant, native, log, native_record["exit"]))
        result["fixtureSourcePinManifestSHA256"] = source_pins_sha
        check_sources(directory, args.variant, source_pins)
        if args.variant.endswith("-repaired-markup"):
            check_sources(directory, "current", source_pins)
            result["markupControl"] = "Additive preserved Feature/Session/case-body control with only current authored-ID markup; historical fixture CUI remains unchanged"
        if digest(directory / "fixture-source-pins.json") != source_pins_sha:
            raise RuntimeError("Preserved source pin ledger changed during native execution")
    except Exception as error:
        result.update(status="FAIL", error=repr(error))
    finally:
        if commands is not None:
            try:
                commands.run("git-clean-after", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
            except Exception as error:
                result.update(status="FAIL", sourceCustodyError=repr(error))
        write_json(diagnostics / "result.json", result)
    print(json.dumps(result, indent=2))
    return 0 if result["status"] in ("CURRENT_NATIVE_PASS", "PRESERVED_PRODUCTION_CONTROL_RED_OBSERVED",
                                     "PRESERVED_ADMISSION01_CONTROL_RED_OBSERVED",
                                     "PRODUCTION_ORIGINAL_SESSION_REPAIRED_MARKUP_CONTROL_RED_OBSERVED",
                                     "ADMISSION01_SESSION_REPAIRED_MARKUP_CONTROL_RED_OBSERVED") else 1


if __name__ == "__main__":
    raise SystemExit(main())
