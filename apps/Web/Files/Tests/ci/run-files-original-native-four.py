#!/usr/bin/env python3
"""Four unchanged original native Files/Home owner tests using maintained Commands.
No browser/Worker/installer acceptance; no owner implementation or host copy.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

COMMON = "apps/Web/Tests/ci/run-ordinary-native.py"
COMMON_SHA = "a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38"
PROJECT = "apps/Web/Files/Tests/OriginalNativeOwner4/FilesOriginal.NativeOwner.Tests.csproj"
LEDGER = "apps/Web/Files/Tests/ci/files-original-native-four-source-pins.json"
LEDGER_SHA = "4e0bf630865d730d7c2ab6b83287aa445e6c4f28b7c2d38ebc4f2a47b377bf55"
CASE_PAIRS = [
    {
        "className": "Haven.Desktop.Tests.FilesNativeBrowserSurfaceTests",
        "method": "Native_CUI_lists_canonical_folders_routes_original_package_selection_and_clears_revoked_Home_sources"
    },
    {
        "className": "Haven.Desktop.Tests.FilesNativeBrowserSurfaceTests",
        "method": "Native_Up_uses_canonical_parent_after_nested_navigation_and_denies_stale_original_folder_without_writes"
    },
    {
        "className": "Haven.Desktop.Tests.FilesNativeBrowserAuthorityTests",
        "method": "Actual_Home_bound_browser_and_folder_read_lease_retain_original_actor_and_deny_stale_or_revoked_sources"
    },
    {
        "className": "Haven.Desktop.Tests.FilesNativeChildFolderReadTests",
        "method": "Actual_Home_canonical_child_ancestry_and_trusted_mapping_keep_original_store_root_and_project_receipts"
    }
]
CASES = [item['method'] for item in CASE_PAIRS]
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
        raise RuntimeError("Reviewed original Files source ledger changed; explicit review/rebind required")
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



def owner_scopes(root, commands, phase):
    expected = json.loads((root / LEDGER).read_text())["ownerSubtrees"]
    text = commands.run("owner-scopes-" + phase,
                        ["git", "ls-tree", "HEAD", "--"] + [x["path"] for x in expected], 15)
    actual = {}
    for line in text.splitlines():
        fields, path = line.split("\t", 1)
        mode, kind, identity = fields.split()
        actual[path] = {"path": path, "mode": mode, "type": kind, "gitSha": identity}
    if actual != {x["path"]: x for x in expected}:
        raise RuntimeError("Full original owner subtree identities changed")
    untracked = commands.run("untracked-source-" + phase,
                             ["git", "ls-files", "--others", "--exclude-standard"], 15)
    if untracked.strip():
        raise RuntimeError("Unexpected untracked checkout inputs")
    return list(actual.values())


def resource_sample(output, diagnostics, phase):
    # Command-boundary logical samples, not a continuous/systemwide quota.
    total = diagnostic_bytes = 0
    for path in output.rglob("*"):
        if path.is_symlink():
            raise RuntimeError("Unexpected symlink in task-owned output")
        if path.is_file():
            size = path.stat().st_size
            total += size
            if path.is_relative_to(diagnostics):
                diagnostic_bytes += size
    free = os.statvfs(output).f_bavail * os.statvfs(output).f_frsize
    sample = {"phase": phase, "logicalOwnedBytes": total,
              "diagnosticBytes": diagnostic_bytes, "freeBytes": free,
              "ownedSampleCapBytes": 3 * 1024**3,
              "diagnosticSampleCapBytes": 32 * 1024**2,
              "freeFloorBytes": 256 * 1024**2,
              "projectedRemainingReserveBytes": max(0, 3 * 1024**3 - total)}
    if total + 65536 > 3 * 1024**3 or diagnostic_bytes + 65536 > 32 * 1024**2 or free < 256 * 1024**2 + sample["projectedRemainingReserveBytes"] + 65536:
        raise RuntimeError("Ordinary CI resource sample outside declared bounds")
    return sample


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
                            "method": None if method is None else method.get("name"),
                            "executionId": None if unit.find("t:Execution", NS) is None else unit.find("t:Execution", NS).get("id")})
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
            or {(item["className"], item["method"]) for item in definitions}
               != {(item["className"], item["method"]) for item in CASE_PAIRS}
            or len({item["id"] for item in definitions}) != 4
            or any(not item["id"] for item in definitions)):
        raise RuntimeError("TRX discovery differs from exact reviewed four methods")
    if (len({item["testId"] for item in results}) != 4
            or {item["testId"] for item in results} != {item["id"] for item in definitions}
            or any(item["outcome"] != "Passed" for item in results)
            or actual["summaryOutcome"] not in ("Completed", "Passed")):
        raise RuntimeError("Actual four unique owner tests did not all complete/pass")
    by_id = {item["id"]: item for item in definitions}
    if (len({item["executionId"] for item in definitions}) != 4
            or any(not item["executionId"] for item in definitions)
            or any(not item["executionId"] or item["executionId"] != by_id[item["testId"]]["executionId"]
                   or item["testName"] != by_id[item["testId"]]["name"] for item in results)):
        raise RuntimeError("Actual TRX definition/result name and execution identities differ")


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
    trx = results_dir / "files-original-native-owner.trx"
    result = {"status": "NOT_RUN", "accepted": False, "project": PROJECT,
              "expectedCases": CASES, "expected": 4, "sourceCommit": None,
              "compiler": "NOT_RUN", "native": "NOT_RUN",
              "scope": "Original Files native CUI/Home OS actor, canonical source leases and stale/revoked denial. Trusted mapping and tiny MZ bytes are selection fixtures only, not browser/Worker/installer/Space authority.",
              "history": "Prior B Files17/Sites18/lifecycle14 evidence remains separate; four original methods are unchanged"}
    commands = None
    before = None
    resource_samples = []
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
        spec = importlib.util.spec_from_file_location("files_four_ordinary_commands", common)
        module = importlib.util.module_from_spec(spec)
        sys.dont_write_bytecode = True
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
        result["ownerScopesBefore"] = owner_scopes(root, commands, "before")
        before = source_check(root)
        write(diagnostics / "source-before.json", before)
        resource_samples.append(resource_sample(output, diagnostics, "before-sdk"))
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
        resource_samples.append(resource_sample(output, diagnostics, "after-restore"))
        result["compiler"] = "STARTED"
        commands.run("build", ["dotnet", "build", PROJECT, "--no-restore", "-c", "Release",
                     "--artifacts-path", str(artifacts), "-r", "linux-x64", "--disable-build-servers",
                     "-m:1", "-nodeReuse:false"] + props, 1200)
        result["compiler"] = "EXIT0"
        resource_samples.append(resource_sample(output, diagnostics, "after-build"))
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
        discovered = commands.run("discover", ["dotnet", "test", PROJECT, "--no-build", "--no-restore",
                     "-c", "Release", "--artifacts-path", str(artifacts), "-r", "linux-x64",
                     "--disable-build-servers", "-m:1", "-nodeReuse:false", "--list-tests"] + props, 180)
        expected_names = [x["className"] + "." + x["method"] for x in CASE_PAIRS]
        actual_names = [line.strip() for line in discovered.splitlines()
                        if line.strip().startswith("Haven.Desktop.Tests.")]
        if len(actual_names) != 4 or set(actual_names) != set(expected_names):
            raise RuntimeError("Discovery must contain exactly the four unchanged original methods")
        result["actualDiscoveredNames"] = actual_names
        result["native"] = "STARTED"
        test_error = None
        try:
            commands.run("test", ["dotnet", "test", PROJECT, "--no-build", "--no-restore",
                         "-c", "Release", "--artifacts-path", str(artifacts), "-r", "linux-x64",
                         "--disable-build-servers", "-m:1", "-nodeReuse:false",
                         "--logger", "trx;LogFileName=files-original-native-owner.trx",
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
            if commands is not None:
                result["ownerScopesAfter"] = owner_scopes(root, commands, "after")
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
        result["accepted"] = False
        result["acceptanceAuthority"] = "Complete final stdout and natural driver exit only; disk receipt is deliberately not an early positive marker"
        result["binaryCustodyQualification"] = "Generated runtime/task hash metadata only; binaries, packages, caches and fixture stores are not uploaded"
        result["resourceQualification"] = "3GiB task output including private packages/caches, 32MiB diagnostics and 256MiB free-floor are command-boundary/final logical samples, not hard transient/peak/systemwide quotas. Original HOME/SDK-global writes are not charged or relabelled as isolated. No local resource or full-platform acceptance."
        try:
            resource_samples.append(resource_sample(output, diagnostics, "before-final-receipt"))
            result["resourceSamples"] = resource_samples
            payload = json.dumps(result, indent=2) + "\n"
            if len(payload.encode()) > 65536:
                raise RuntimeError("Finite final receipt exceeded 64KiB")
            (diagnostics / "result.json").write_text(payload)
            final_sample = resource_sample(output, diagnostics, "after-final-receipt")
            result["finalResourceSample"] = final_sample
            result["accepted"] = result["status"] == "PASS" and closed
        except Exception as error:
            result.update(status="FAIL", accepted=False, finalResourceError=repr(error))
    print(json.dumps(result, indent=2))
    return 0 if result["accepted"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
