#!/usr/bin/env python3
"""Four source-built owner math/input/geometry cases, using maintained Commands.

The separate historical OwnerMath22 suite and browser/host authority are not
accepted by this native fixture. No process engine or owner implementation copy.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import xml.etree.ElementTree as ET

COMMON = "apps/Web/Tests/ci/run-ordinary-native.py"
COMMON_SHA = "a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38"
PROJECT = "apps/Web/Mathematics/Tests/MathOwner.InputGeometry.Tests.csproj"
LEDGER = "apps/Web/Mathematics/Tests/ci/math-native-four-source-pins.json"
LEDGER_SHA = "2b4d6ec01d4b14150207a1ee983c037256fcbd8de0f89be420ca1cc8c2d599b9"
CLASS = "NineToOne.Web.Mathematics.OwnerBoundaryTests.MathOwnerInputGeometryTests"
CASES = [
    "Actual_keyboard_equation_error_retains_last_valid_pixels_identity_and_source_until_restore",
    "Native_keyboard_coordinates_emit_same_typed_response_and_rejected_decimal_retains_previous_response",
    "Outside_plot_and_right_pointer_release_preserve_graph_before_real_left_click_advances_once",
    "Failed_native_precision_projection_preserves_existing_pixels_and_exact_canonical_source",
]
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def sha(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def write(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def source_check(root):
    ledger = root / LEDGER
    if ledger.is_symlink() or not ledger.is_file() or sha(ledger) != LEDGER_SHA:
        raise RuntimeError("Reviewed math source ledger changed; explicit review/rebind required")
    rows = json.loads(ledger.read_text())["sources"]
    seen = set()
    for row in rows:
        relative = Path(row["path"])
        if relative.is_absolute() or ".." in relative.parts or row["path"] in seen:
            raise RuntimeError("Source pins must be unique checkout-relative paths")
        seen.add(row["path"])
        path = root / relative
        if path.is_symlink() or not path.is_file() or not path.resolve().is_relative_to(root):
            raise RuntimeError("Source is not a contained regular file: " + row["path"])
        if path.stat().st_size != row["bytes"] or sha(path) != row["sha256"]:
            raise RuntimeError("Reviewed source body changed: " + row["path"])
    return rows


def runtime_pins(directory):
    rows = []
    for path in sorted(directory.rglob("*")):
        if path.is_file():
            if path.is_symlink() or not path.resolve().is_relative_to(directory):
                raise RuntimeError("Runtime body escapes actual target directory")
            rows.append({"path": str(path.relative_to(directory)),
                         "bytes": path.stat().st_size, "sha256": sha(path)})
    return rows


def read_trx(path):
    if path.is_symlink() or not path.is_file():
        raise RuntimeError("Actual complete TRX absent")
    doc = ET.parse(path).getroot()
    summary = doc.find("t:ResultSummary", NS)
    counters = doc.find("t:ResultSummary/t:Counters", NS)
    definitions = []
    for unit in doc.findall("t:TestDefinitions/t:UnitTest", NS):
        method = unit.find("t:TestMethod", NS)
        definitions.append({"id": unit.get("id"), "name": unit.get("name"),
                            "className": None if method is None else method.get("className"),
                            "method": None if method is None else method.get("name")})
    results = [{"testId": item.get("testId"), "testName": item.get("testName"),
                "outcome": item.get("outcome"), "executionId": item.get("executionId")}
               for item in doc.findall("t:Results/t:UnitTestResult", NS)]
    return {"bytes": path.stat().st_size, "sha256": sha(path),
            "summaryOutcome": None if summary is None else summary.get("outcome"),
            "counters": None if counters is None else dict(counters.attrib),
            "definitions": definitions, "results": results}


def validate_trx(actual):
    counters = actual["counters"] or {}
    for key, expected in {"total": 4, "executed": 4, "passed": 4,
                          "failed": 0, "notExecuted": 0}.items():
        if key not in counters or int(counters[key]) != expected:
            raise RuntimeError("Actual TRX count differs: " + key)
    for key in ("error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
                "notRunnable", "disconnected", "inProgress", "pending"):
        if key in counters and int(counters[key]) != 0:
            raise RuntimeError("Actual TRX incomplete/error state: " + key)
    definitions, results = actual["definitions"], actual["results"]
    if (len(definitions) != 4 or len(results) != 4
            or any(item["className"] != CLASS for item in definitions)
            or {item["method"] for item in definitions} != set(CASES)
            or len({item["id"] for item in definitions}) != 4
            or any(not item["id"] for item in definitions)):
        raise RuntimeError("TRX discovery differs from exact reviewed four methods")
    if (len({item["testId"] for item in results}) != 4
            or {item["testId"] for item in results} != {item["id"] for item in definitions}
            or any(item["outcome"] != "Passed" for item in results)
            or actual["summaryOutcome"] not in ("Completed", "Passed")):
        raise RuntimeError("Actual four unique owner tests did not all complete/pass")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--expected-commit", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    root = Path.cwd().resolve()
    output = args.output
    if (output.is_symlink() or output.absolute() != output.resolve()
            or output.exists() or output.resolve().is_relative_to(root)):
        raise RuntimeError("Require fresh canonical task-owned output outside checkout")
    output.mkdir(parents=True)
    diagnostics = output / "diagnostics"
    diagnostics.mkdir()
    results_dir = diagnostics / "test-results"
    results_dir.mkdir()
    trx = results_dir / "math-owner-input-geometry.trx"
    result = {"status": "NOT_RUN", "accepted": False, "project": PROJECT,
              "expectedCases": CASES, "expected": 4, "sourceCommit": None,
              "compiler": "NOT_RUN", "native": "NOT_RUN",
              "scope": "Original native CUI/Skia/ScottPlot input, pixels, geometry and canonical typed models; no browser, durable host, actor/ACL or Forms embedding acceptance",
              "history": "Original source01/02 and existing OwnerMath22 evidence remain separate"}
    commands = None
    before = None
    env = os.environ.copy()
    for variable, directory in {
        "DOTNET_CLI_HOME": "cli", "NUGET_PACKAGES": "nuget",
        "NUGET_HTTP_CACHE_PATH": "http", "NUGET_PLUGINS_CACHE_PATH": "plugins",
        "XDG_CACHE_HOME": "font-cache", "TMPDIR": "tmp", "TMP": "tmp", "TEMP": "tmp",
        "HAVEN_DATA_DIR": "fixture-data",
    }.items():
        owned = output / directory
        owned.mkdir(exist_ok=True)
        env[variable] = str(owned)
    env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE="false", DOTNET_CLI_TELEMETRY_OPTOUT="1",
               DOTNET_NOLOGO="1", MSBUILDDISABLENODEREUSE="1")
    try:
        common = root / COMMON
        if common.is_symlink() or not common.is_file() or sha(common) != COMMON_SHA:
            raise RuntimeError("Maintained a57 Commands changed; explicit review/rebind required")
        spec = importlib.util.spec_from_file_location("math_four_ordinary_commands", common)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        commands = module.Commands(diagnostics, env, root)
        head = commands.run("git-head", ["git", "rev-parse", "HEAD"], 15).strip()
        if not re.fullmatch(r"[0-9a-f]{40}", args.expected_commit) or head != args.expected_commit:
            raise RuntimeError("Actual checkout differs from github.sha")
        result["sourceCommit"] = head
        commands.run("git-clean-before", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
        commands.run("source-tree-pins", ["git", "ls-tree", "-r", "HEAD"], 15)
        commands.run("native-submodule-pins", ["git", "submodule", "status", "--recursive", "--",
                     "framework/CUI/vendor/Avalonia/external/XamlX", "framework/CUI/vendor/Avalonia/external/Avalonia.DBus"], 15)
        before = source_check(root)
        write(diagnostics / "source-before.json", before)
        if commands.run("dotnet-version", ["dotnet", "--version"], 30).strip() != "10.0.401":
            raise RuntimeError("Require reviewed SDK10.0.401")
        commands.run("dotnet-info", ["dotnet", "--info"], 30)
        artifacts = output / "artifacts"
        task = artifacts / "bin/Avalonia.Build.Tasks/release/Avalonia.Build.Tasks.dll"
        props = ["-p:SelfContained=false", "-p:UseSharedCompilation=false",
                 "-p:AvaloniaBuildTasksLocation=" + str(task),
                 "-p:CreateHardLinksForCopyAdditionalFilesIfPossible=false",
                 "-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=false",
                 "-p:CreateHardLinksForCopyLocalIfPossible=false",
                 "-p:CreateHardLinksForPublishFilesIfPossible=false"]
        commands.run("restore", ["dotnet", "restore", PROJECT, "--artifacts-path", str(artifacts),
                     "-r", "linux-x64", "--configfile", str(root / "NuGet.Config"),
                     "--disable-build-servers", "-p:Configuration=Release", "-m:1", "-nodeReuse:false"] + props, 600)
        result["compiler"] = "STARTED"
        commands.run("build", ["dotnet", "build", PROJECT, "--no-restore", "-c", "Release",
                     "--artifacts-path", str(artifacts), "-r", "linux-x64", "--disable-build-servers",
                     "-m:1", "-nodeReuse:false"] + props, 1200)
        result["compiler"] = "EXIT0"
        evaluated = module.parse_sdk(commands.run("target-properties", ["dotnet", "msbuild", PROJECT,
                     "-p:Configuration=Release", "-p:RuntimeIdentifier=linux-x64", "-p:UseArtifactsOutput=true",
                     "-p:ArtifactsPath=" + str(artifacts), "-nodeReuse:false",
                     "-getProperty:TargetPath,TargetDir,TargetFramework,RuntimeIdentifier,UseAppHost,IsTestProject,AvaloniaBuildTasksLocation"] + props, 30))
        write(diagnostics / "target-properties.json", evaluated)
        target = Path(evaluated["TargetPath"]).resolve()
        if (evaluated["TargetFramework"] != "net10.0" or evaluated["RuntimeIdentifier"] != "linux-x64"
                or evaluated["IsTestProject"].lower() != "true"
                or not target.is_relative_to(artifacts) or not target.is_file()
                or Path(evaluated["TargetDir"]).resolve() != target.parent
                or Path(evaluated["AvaloniaBuildTasksLocation"]).resolve() != task.resolve()
                or not task.is_file()):
            raise RuntimeError("Actual isolated owner test target/framework/RID/task differs")
        result["actualOwnerTask"] = {"bytes": task.stat().st_size, "sha256": sha(task)}
        runtime = runtime_pins(target.parent)
        write(diagnostics / "reported-emitted-runtime.json", runtime)
        result["native"] = "STARTED"
        test_error = None
        try:
            commands.run("test", ["dotnet", "test", PROJECT, "--no-build", "--no-restore",
                         "-c", "Release", "--artifacts-path", str(artifacts), "-r", "linux-x64",
                         "--disable-build-servers", "-m:1", "-nodeReuse:false",
                         "--logger", "trx;LogFileName=math-owner-input-geometry.trx",
                         "--results-directory", str(results_dir)] + props, 180)
        except Exception as error:
            test_error = error
            result["actualTestCommandException"] = repr(error)
        result["actualTestCommandRecord"] = commands.records[-1]
        if trx.is_file():
            result["actualTRX"] = read_trx(trx)
            write(diagnostics / "trx-summary.json", result["actualTRX"])
        if test_error is not None:
            result["native"] = "FAIL"
            raise test_error
        if "actualTRX" not in result:
            raise RuntimeError("Actual TRX absent after native test command")
        validate_trx(result["actualTRX"])
        after_runtime = runtime_pins(target.parent)
        write(diagnostics / "reported-emitted-runtime-after.json", after_runtime)
        if runtime != after_runtime:
            raise RuntimeError("Actual owner/runtime bodies changed during test command")
        result.update(status="PASS", native="PASS", executed=4, passed=4, failed=0, notRun=0)
    except Exception as error:
        result.update(status="FAIL", error=repr(error))
    finally:
        try:
            after = source_check(root)
            write(diagnostics / "source-after.json", after)
            if before is not None and before != after:
                raise RuntimeError("Reviewed source bodies changed during ordinary build/test")
            if commands is not None:
                commands.run("git-clean-after", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
        except Exception as error:
            result.update(status="FAIL", sourceCustodyError=repr(error))
        records = [] if commands is None else commands.records
        closed = bool(records) and all(
            item["error"] is None and item["exit"] == 0 and item["normalEOF"]
            and item["familyClosed"] and item["finalECHILD"] and not item["signals"]
            and all(birth["gone"] for birth in item["births"]) for item in records)
        result["allRecordedFamiliesNormalClosed"] = closed
        if not closed:
            result["status"] = "FAIL"
        result["accepted"] = result["status"] == "PASS" and closed
        result["binaryCustodyQualification"] = "Generated runtime/task hash metadata only; binaries, packages, caches and fixture stores are not uploaded"
        result["resourceQualification"] = "Ordinary isolated CI; no local 100MiB cumulative-growth or full-platform acceptance claim"
        write(diagnostics / "result.json", result)
    print(json.dumps(result, indent=2))
    return 0 if result["accepted"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
