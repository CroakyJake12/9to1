#!/usr/bin/env python3
"""Two isolated source-built native focus/scroll variants; unchanged nine-case fixture.

Original negative results remain failures, never positive browser acceptance.
No protected focus ledgers, product replacement, mock Commands or HOME override.
"""
import argparse
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import sys

COMMON = "apps/Web/Tests/ci/run-ordinary-native.py"
COMMON_SHA = "a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38"
PROJECT = "apps/Web/Tests/BrowserFocusScroll.Tests.csproj"
PROPOSED = "apps/Web/Tests/NativeFocusOwner9/ProposedBrowserAccessibilityBridge.cs"
PINNED = {
    COMMON: (19584, COMMON_SHA),
    PROJECT: (979, "d6df5325108efded38150d15427deec887f910fdc63d4ea9ec6484c7afc48445"),
    "apps/Web/Tests/BrowserFocusScrollRegression.cs": (9324, "9de483f8c22a6cfe3f790134c27d892dea528fec612b5d6c38f0d1d3498b3a42"),
    "apps/Web/BrowserAccessibilityBridge.cs": (6998, "619782a51fd9d845913ae85e9787c48abf24be653fef6a1a653555f735d2f328"),
    PROPOSED: (8872, "c03add21cf0ff7f83555355037b779a0f0415a2dc9970f60c146d942742dad1c"),
    "apps/Web/BrowserAccessibilityJsonContext.cs": (389, "86b6b7b9aa6d39f4b0f51157322fd93ed599a9655c02d8d73ac7f662ab97d851"),
    "framework/CUI/Runtime/CakeOS.Cui.Runtime.csproj": (1315, "7b38ee5f92368a5e47bd433594b3810157a0802c696905c116f82e47185d07c4"),
    "NuGet.Config": (579, "6343458c92e08cdf00e557786aa43b59514ce7b6131bad9b678f77e6c461254e"),
}
CASES = [
    "Real native scroll fixture starts offscreen with nonempty extent",
    "New native focus settles automatic and explicit bring within viewport",
    "Already focused native peer explicitly brings its offscreen target into view",
    "Disabled automatic focus bring preserves explicit native bring",
    "GotFocus render generation change rejects continuation",
    "Real layout callback changing render generation rejects continuation",
    "Native focus callback detaching current control rejects continuation",
    "Native focus callback claiming another control rejects continuation",
    "Reentrant real arrangement with invalid ancestor rejects unsettled bring",
]
ORIGINAL_REQUIRED_FAILURE = CASES[1]


def sha(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for body in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(body)
    return value.hexdigest()


def write(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def contained_file(root, relative):
    path = root / relative
    if not path.is_file() or path.is_symlink() or not path.resolve().is_relative_to(root):
        raise RuntimeError("Expected contained regular source: " + relative)
    for parent in path.parents:
        if parent == root:
            break
        if parent.is_symlink():
            raise RuntimeError("Source ancestor alias refused: " + relative)
    return path


def source_check(root):
    rows = []
    for relative, (size, digest) in PINNED.items():
        path = contained_file(root, relative)
        if path.stat().st_size != size or sha(path) != digest:
            raise RuntimeError("Immutable native focus source changed: " + relative)
        rows.append({"path": relative, "bytes": size, "sha256": digest})
    return rows


def normal(record, expected_exit):
    return (record.get("error") is None and record.get("exit") == expected_exit
            and record.get("exitAfterDrain") == expected_exit
            and record.get("normalEOF") is True and record.get("familyClosed") is True
            and record.get("finalECHILD") is True and not record.get("signals")
            and bool(record.get("births"))
            and all(item.get("gone") is True for item in record["births"]))


def native_outcomes(text):
    lines = re.findall(r"^(PASS|FAIL): (.+)$", text, re.MULTILINE)
    samples = re.findall(r"^NATIVE_FOCUS: (.+)$", text, re.MULTILINE)
    if any(len(line.encode()) > 4096 for line in samples):
        raise RuntimeError("Actual native focus sample is unbounded")
    matches = re.findall(
        r"^(\d+) real native focus/scroll checks; (\d+) passed; (\d+) failed\. Controlled callback boundary regression, not actual browser case acceptance\.$",
        text, re.MULTILINE)
    actual = {"discovered": len({name for _, name in lines}),
              "executed": len(lines), "passed": sum(state == "PASS" for state, _ in lines),
              "failed": sum(state == "FAIL" for state, _ in lines),
              "notRun": max(0, 9 - len(lines)),
              "outcomes": [{"name": name, "state": state} for state, name in lines],
              "nativeFocusSamples": samples}
    if (len(lines) != 9 or len({name for _, name in lines}) != 9
            or [name for _, name in lines] != CASES
            or len(matches) != 1
            or tuple(map(int, matches[0])) != (9, actual["passed"], actual["failed"])
            or len(samples) != 3
            or [line.split(";", 1)[0] for line in samples] !=
               ["new-focus", "already-focused", "automatic-bring-disabled"]):
        raise RuntimeError("Actual unchanged native nine discovery/completion/sample differs: " + json.dumps(actual))
    return actual



def original_new_focus_sample(actual):
    # These are literal invariant-culture fields from the unchanged fixture's
    # Sample("new-focus", ...) call, not inferred from its FAIL label.
    samples = [line for line in actual["nativeFocusSamples"] if line.startswith("new-focus; ")]
    if len(samples) != 1:
        raise RuntimeError("Exact original new-focus sample absent/duplicated")
    match = re.fullmatch(
        r"new-focus; return=(True|False); focused=(True|False); offset=([^;]+); viewport=([^;]+); target=([^;]+); client=([^;]+); requests=(0|[1-9]\d*)",
        samples[0])
    if match is None:
        raise RuntimeError("Actual original new-focus fields have unsupported shape")

    def vector(text, count):
        pieces = [piece.strip() for piece in text.split(",")]
        if (len(pieces) != count or any(not re.fullmatch(
                r"[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[Ee][+-]?\d+)?", piece) for piece in pieces)):
            raise RuntimeError("Actual original native geometry format differs")
        values = [float(piece) for piece in pieces]
        if not all(math.isfinite(value) for value in values):
            raise RuntimeError("Actual original native geometry is not finite")
        return values

    offset = vector(match[3], 2)
    viewport = vector(match[4], 2)
    target = vector(match[5], 4)
    client = vector(match[6], 2)
    requests = int(match[7])
    returned = match[1] == "True"
    focused = match[2] == "True"
    fully_inside_client = target[1] >= -0.1 and target[1] + target[3] <= client[1] + 0.1
    if (not returned or not focused or requests < 2
            or offset[1] < 0
            or viewport[0] <= 0 or viewport[1] <= 0
            or client[0] <= 0 or client[1] <= 0
            or target[2] <= 0 or target[3] <= 0
            or fully_inside_client):
        raise RuntimeError("Original negative is not actual focused double-bring outside the native client")
    return {"returned": returned, "focused": focused, "requests": requests,
            "offset": offset, "viewport": viewport, "target": target, "client": client,
            "fullyInsideClientAtOriginalTolerance": fully_inside_client,
            "tolerance": 0.1,
            "qualification": "Literal unchanged new-focus sample plus separately PASS initial-offscreen fixture case; no other original failure count assumed."}


def validate_native(variant, actual, actual_exit):
    states = {row["name"]: row["state"] for row in actual["outcomes"]}
    if variant == "proposed":
        if actual_exit != 0 or (actual["passed"], actual["failed"], actual["notRun"]) != (9, 0, 0):
            raise RuntimeError("Proposed source did not pass all unchanged native nine")
        return "PASS_PROPOSED_NATIVE_NINE"
    if (actual_exit != 1 or states[CASES[0]] != "PASS"
            or states[ORIGINAL_REQUIRED_FAILURE] != "FAIL" or actual["failed"] < 1
            or actual["notRun"] != 0):
        raise RuntimeError("Original source did not complete the real new-focus viewport negative")
    actual["originalNewFocusQualification"] = original_new_focus_sample(actual)
    # Other original failures are retained literally, never predeclared or waived.
    return "ORIGINAL_NATIVE_NINE_NEGATIVE_OBSERVED"


def resource_sample(output, diagnostics, phase):
    # Command-boundary logical samples, not a continuous/systemwide quota.
    total = diagnostic_bytes = 0
    categories = {}
    for path in output.rglob("*"):
        if path.is_symlink():
            raise RuntimeError("Unexpected symlink in task-owned output")
        if path.is_file():
            size = path.stat().st_size
            total += size
            category = path.relative_to(output).parts[0]
            categories[category] = categories.get(category, 0) + size
            if path.is_relative_to(diagnostics):
                diagnostic_bytes += size
    free = os.statvfs(output).f_bavail * os.statvfs(output).f_frsize
    sample = {"phase": phase, "logicalOwnedBytes": total,
              "diagnosticBytes": diagnostic_bytes, "freeBytes": free,
              "logicalCategoryBytes": dict(sorted(categories.items())),
              "ownedSampleCapBytes": 3 * 1024**3,
              "diagnosticSampleCapBytes": 32 * 1024**2,
              "freeFloorBytes": 256 * 1024**2,
              "projectedRemainingReserveBytes": max(0, 3 * 1024**3 - total)}
    if total + 65536 > 3 * 1024**3 or diagnostic_bytes + 65536 > 32 * 1024**2 or free < 256 * 1024**2 + sample["projectedRemainingReserveBytes"] + 65536:
        sample["violations"] = {
            "ownedSampleCap": total + 65536 > 3 * 1024**3,
            "diagnosticSampleCap": diagnostic_bytes + 65536 > 32 * 1024**2,
            "remainingReserveFloor": free < 256 * 1024**2 + sample["projectedRemainingReserveBytes"] + 65536,
        }
        raise RuntimeError("Ordinary CI resource sample outside declared bounds: " + json.dumps(sample, sort_keys=True, separators=(",", ":")))
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



def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--variant", choices=("original", "proposed"), required=True)
    parser.add_argument("--expected-commit", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root = Path.cwd().resolve()
    output = args.output
    if (output.is_symlink() or output.absolute() != output.resolve()
            or output.exists() or output.resolve().is_relative_to(root)):
        raise RuntimeError("Fresh canonical external task-owned output required")
    output.mkdir(parents=True)
    diagnostics = output / "diagnostics"
    diagnostics.mkdir()
    result = {"status": "NOT_RUN", "accepted": False, "variant": args.variant,
              "expected": 9, "counts": {"discovered": 0, "executed": 0, "passed": 0, "failed": 0, "notRun": 9},
              "compiler": "NOT_RUN", "native": "NOT_RUN",
              "scope": "Unchanged nine actual native Avalonia/ScrollViewer/Button/Bridge cases only; not Wave browser zoom, user focus, DOM, provider, hardware or full accessibility acceptance.",
              "history": "All earlier Wave/Picture/AX native and browser failures remain distinct and unchanged."}
    commands = None
    before = None
    runtime = None
    target = None
    task = None
    binary_before = None
    samples = []
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
    script = Path(__file__).resolve()
    script_before = script.read_bytes()
    try:
        before = source_check(root)
        write(diagnostics / "source-before.json", before)
        common = contained_file(root, COMMON)
        spec = importlib.util.spec_from_file_location("maintained_focus9_commands", common)
        module = importlib.util.module_from_spec(spec)
        sys.dont_write_bytecode = True
        spec.loader.exec_module(module)
        commands = module.Commands(diagnostics, env, root)
        head = commands.run("git-head", ["git", "rev-parse", "HEAD"], 15).strip()
        if not re.fullmatch(r"[0-9a-f]{40}", args.expected_commit) or head != args.expected_commit:
            raise RuntimeError("Actual checkout differs from github.sha")
        result["sourceCommit"] = head
        commands.run("git-clean-before", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
        if commands.run("untracked-before", ["git", "ls-files", "--others", "--exclude-standard"], 15).strip():
            raise RuntimeError("Untracked source input refused")
        tree_before = commands.run("source-tree-before", ["git", "ls-tree", "-r", "HEAD"], 15)
        commands.run("native-submodule-pins", ["git", "submodule", "status", "--recursive", "--",
                     "framework/CUI/vendor/Avalonia/external/XamlX",
                     "framework/CUI/vendor/Avalonia/external/Avalonia.DBus"], 15)
        samples.append(resource_sample(output, diagnostics, "before-sdk"))
        if commands.run("dotnet-version", ["dotnet", "--version"], 30).strip() != "10.0.401":
            raise RuntimeError("Expected maintained SDK10.0.401")
        commands.run("dotnet-info", ["dotnet", "--info"], 30)
        artifacts = output / "artifacts"
        task = artifacts / "bin/Avalonia.Build.Tasks/release/Avalonia.Build.Tasks.dll"
        props = ["-p:SelfContained=false", "-p:UseSharedCompilation=false",
                 "-p:AvaloniaBuildTasksLocation=" + str(task),
                 "-p:CreateHardLinksForCopyAdditionalFilesIfPossible=false",
                 "-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=false",
                 "-p:CreateHardLinksForCopyLocalIfPossible=false",
                 "-p:CreateHardLinksForPublishFilesIfPossible=false"]
        if args.variant == "proposed":
            props.append("-p:BrowserFocusBridgeSource=" + str(contained_file(root, PROPOSED)))
        # Original omits the override: immutable project uses the real unchanged
        # canonical ../BrowserAccessibilityBridge.cs. No compatibility source edit.
        commands.run("restore", ["dotnet", "restore", PROJECT, "--artifacts-path", str(artifacts),
                     "-r", "linux-x64", "--configfile", str(root / "NuGet.Config"),
                     "--disable-build-servers", "-p:Configuration=Release", "-m:1", "-nodeReuse:false"] + props, 600)
        samples.append(resource_sample(output, diagnostics, "after-restore"))
        result["compiler"] = "STARTED"
        commands.run("build", ["dotnet", "build", PROJECT, "--no-restore", "-c", "Release",
                     "--artifacts-path", str(artifacts), "-r", "linux-x64",
                     "--disable-build-servers", "-m:1", "-nodeReuse:false"] + props, 1200)
        result["compiler"] = "EXIT0"
        samples.append(resource_sample(output, diagnostics, "after-build"))
        evaluated = module.parse_sdk(commands.run("target-properties", ["dotnet", "msbuild", PROJECT,
                     "-p:Configuration=Release", "-p:RuntimeIdentifier=linux-x64",
                     "-p:UseArtifactsOutput=true", "-p:ArtifactsPath=" + str(artifacts),
                     "-nodeReuse:false",
                     "-getProperty:TargetPath,TargetDir,TargetFramework,RuntimeIdentifier,UseAppHost,AvaloniaBuildTasksLocation,BrowserFocusBridgeSource"] + props, 30))
        write(diagnostics / "target-properties.json", evaluated)
        expected_bridge = (root / PROPOSED if args.variant == "proposed"
                           else root / "apps/Web/BrowserAccessibilityBridge.cs")
        evaluated_bridge = Path(evaluated["BrowserFocusBridgeSource"])
        if not evaluated_bridge.is_absolute():
            evaluated_bridge = (root / PROJECT).parent / evaluated_bridge
        target = Path(evaluated["TargetPath"]).resolve()
        apphost = target.with_suffix("")
        if (evaluated["TargetFramework"] != "net10.0" or evaluated["RuntimeIdentifier"] != "linux-x64"
                or evaluated["UseAppHost"].lower() != "true"
                or evaluated_bridge.resolve() != expected_bridge.resolve()
                or not target.is_relative_to(artifacts) or not target.is_file() or target.is_symlink()
                or not apphost.is_file() or apphost.is_symlink() or not os.access(apphost, os.X_OK)
                or Path(evaluated["TargetDir"]).resolve() != target.parent
                or Path(evaluated["AvaloniaBuildTasksLocation"]).resolve() != task.resolve()
                or not task.is_file() or task.is_symlink()):
            raise RuntimeError("Actual target/RID/entry/apphost/source-built owner task/bridge binding differs")
        with target.open("rb") as stream:
            if stream.read(2) != b"MZ":
                raise RuntimeError("Actual managed entry is not PE")
        with apphost.open("rb") as stream:
            if stream.read(4) != b"\x7fELF":
                raise RuntimeError("Actual linux-x64 apphost is not ELF")
        runtime = runtime_pins(target.parent)
        binary_before = {"runtime": runtime, "ownerTask": {"bytes": task.stat().st_size, "sha256": sha(task)}}
        write(diagnostics / "runtime-before.json", binary_before)
        result["actualSelectedBridge"] = {"path": str(expected_bridge.relative_to(root)),
                                         "bytes": expected_bridge.stat().st_size, "sha256": sha(expected_bridge)}
        result["native"] = "STARTED"
        native_error = None
        try:
            commands.run("native-nine", [str(apphost)], 120)
        except Exception as error:
            native_error = error
            result["actualNativeCommandException"] = repr(error)
        record = commands.records[-1]
        result["actualNativeCommandRecord"] = record
        log_path = diagnostics / "native-nine.log"
        if not log_path.is_file() or log_path.is_symlink() or log_path.stat().st_size > 8 * 1024**2:
            raise RuntimeError("Native literal command log absent or unbounded")
        text = log_path.read_text(errors="strict")
        result["nativeLog"] = {"bytes": log_path.stat().st_size, "sha256": sha(log_path)}
        # Capture full real case outputs before expected-exit/negative gates.
        actual = native_outcomes(text)
        result["actualNative"] = actual
        result["counts"] = {key: actual[key] for key in ("discovered", "executed", "passed", "failed", "notRun")}
        write(diagnostics / "native-nine-results.json", actual)
        expected_exit = 0 if args.variant == "proposed" else 1
        if not normal(record, expected_exit):
            if native_error is not None:
                raise native_error
            raise RuntimeError("Native exit/family differs; setup/compiler/custody failure is not expected negative")
        result["status"] = validate_native(args.variant, actual, record["exit"])
        result["native"] = "COMPLETED"
    except Exception as error:
        result.update(status="FAIL", error=repr(error))
    finally:
        try:
            after = source_check(root)
            write(diagnostics / "source-after.json", after)
            if before is not None and before != after:
                raise RuntimeError("Immutable selected source changed")
            if script.read_bytes() != script_before:
                raise RuntimeError("Operational source changed")
            if commands:
                tree_after = commands.run("source-tree-after", ["git", "ls-tree", "-r", "HEAD"], 15)
                if "tree_before" in locals() and tree_after != tree_before:
                    raise RuntimeError("Whole tracked tree identities changed")
                commands.run("git-clean-after", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
                if commands.run("git-head-after", ["git", "rev-parse", "HEAD"], 15).strip() != args.expected_commit:
                    raise RuntimeError("Source commit changed")
                if commands.run("untracked-after", ["git", "ls-files", "--others", "--exclude-standard"], 15).strip():
                    raise RuntimeError("Unexpected untracked source after execution")
            if runtime is not None:
                binary_after = {"runtime": runtime_pins(target.parent),
                                "ownerTask": {"bytes": task.stat().st_size, "sha256": sha(task)}}
                write(diagnostics / "runtime-after.json", binary_after)
                if binary_before != binary_after:
                    raise RuntimeError("Actual runtime/source-built task bytes changed")
                result["runtimeAndOwnerTaskUnchanged"] = True
        except Exception as error:
            result.update(status="FAIL", sourceOrBinaryCustodyError=repr(error))
        records = [] if commands is None else commands.records
        closed = bool(records) and all(normal(item, 1 if args.variant == "original"
                     and item["name"] == "native-nine" else 0) for item in records)
        result["allRecordedFamiliesNormalClosed"] = closed
        if not closed:
            result["status"] = "FAIL"
        result["accepted"] = False
        result["negativeIsAcceptance"] = False
        result["operationalSourceSha256"] = hashlib.sha256(script_before).hexdigest()
        result["acceptanceAuthority"] = "Complete final stdout and natural driver exit only; disk receipt is not an early positive marker"
        result["sourceQualification"] = "Eight full selected immutable bodies plus all tracked Git tree identities and clean/untracked status. Not an exhaustive ignored/generated compiler-input closure."
        result["resourceQualification"] = "3GiB owned outputs/caches, 32MiB diagnostics and 256MiB floor are command-boundary/final logical samples; not a hard peak/deleted-byte/systemwide quota. Original HOME/SDK-global writes remain outside this scoped sample, HOME is not overridden."
        try:
            samples.append(resource_sample(output, diagnostics, "before-final-receipt"))
            result["resourceSamples"] = samples
            payload = (json.dumps(result, indent=2) + "\n").encode()
            if len(payload) > 65536:
                raise RuntimeError("Final receipt exceeds 64KiB")
            (diagnostics / "result.json").write_bytes(payload)
            result["finalResourceSample"] = resource_sample(output, diagnostics, "after-final-receipt")
            result["accepted"] = (result["status"] in
                ("PASS_PROPOSED_NATIVE_NINE", "ORIGINAL_NATIVE_NINE_NEGATIVE_OBSERVED") and closed)
        except Exception as error:
            result.update(status="FAIL", accepted=False, finalResourceError=repr(error))
    print(json.dumps(result, indent=2))
    return 0 if result["accepted"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
