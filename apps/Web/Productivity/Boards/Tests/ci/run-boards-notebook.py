#!/usr/bin/env python3
"""Source-built canonical notebook service/editor/repository; separate processes.

Uses the maintained ordinary Commands engine. No copied fixture binary, custom
process custodian, renderer, browser acceptance, provider or permission grant.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re

COMMON = "apps/Web/Tests/ci/run-ordinary-native.py"
COMMON_SHA = "a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38"
PROJECT = "apps/Web/Productivity/Boards/Tests/BoardsNotebook.RichText.Tests.csproj"
LEDGER = "apps/Web/Productivity/Boards/Tests/ci/boards-notebook-source-pins.json"
LEDGER_SHA = "d446aece95b73f9bf109a58d7d768b3963c575d79c8af7f2feac21cc18f8086a"
FORMAT = [
    "Exact three-word content, including both spaces",
    "Only selected beta forms the middle owner run",
    "Exact owner rich formatting on beta and unaffected neighbours",
]
SEED = [
    "Fresh seed never overwrites another fixture",
    "Actual Boards owner creates a durable notebook",
    "Actual Boards service creates a canonical paragraph on the owner page",
    "Editor selects the exact owner block ID",
    "Actual public owner editor mutation marks the same notebook dirty",
    "Retained editor and core expose the same owning document identity/content",
    "Owner beta range6..10 selected",
] + FORMAT + [
    "Owner undo removes only selected bold format",
    "Undo does not invent durable revision",
    "Owner redo restores selected rich format",
] + FORMAT + [
    "Actual durable owner receipt acknowledges save",
    "Save preserves the actual editor and its undo history",
    "Actual owner persists create and rich edit revision history",
    "Latest real historical snapshot preserves canonical identity/revision",
] + FORMAT + ["Saved close releases editor and notebook"]
VERIFY = [
    "Fresh process opens the same actual notebook ID",
    "Canonical revision/section/page identities survive fresh process",
    "Whole sections/pages/blocks/runs/attributes survive restart exactly",
] + FORMAT + [
    "Fresh process preserves exact owner history revision IDs/digests/metadata",
    "Fresh process loads canonical rich history snapshot",
] + FORMAT + [
    "Independent real repository object opens the same base",
    "Winning owner save publishes next revision",
    "Stale owner CAS failure preserves the rich draft",
    "Conflict cannot change winner durable bytes",
    "Conflict retains exact unsaved text and original IDs",
    "Dirty conflict denies close and retains the active notebook",
    "Real saved notebook opens in read-only presentation",
    "Typed read-only authoring and retained input admission denied",
    "Owning View mode freezes rich authoring",
    "View mode denies typed rich formatting",
    "Owning Edit mode restores authoring permission",
    "Known clean owner document saves and closes",
]
FIXTURE_SCOPE = "real owner notebook editor/local durable fixture; native/browser NOT_RUN"


def sha(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def write(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def source_check(root):
    ledger = root / LEDGER
    if ledger.is_symlink() or sha(ledger) != LEDGER_SHA:
        raise RuntimeError("Reviewed notebook source ledger changed; review/rebind required")
    rows = json.loads(ledger.read_text())["sources"]
    seen = set()
    for row in rows:
        relative = Path(row["path"])
        if relative.is_absolute() or ".." in relative.parts or row["path"] in seen:
            raise RuntimeError("Source ledger must contain unique checkout-relative paths")
        seen.add(row["path"])
        path = root / relative
        if path.is_symlink() or not path.is_file() or not path.resolve().is_relative_to(root):
            raise RuntimeError("Pinned source is not a contained regular file: " + row["path"])
        if path.stat().st_size != row["bytes"] or sha(path) != row["sha256"]:
            raise RuntimeError("Source body changed: " + row["path"])
    return rows


def validate(mode, text):
    expected = SEED if mode == "seed" else VERIFY
    actual = re.findall(r"^ASSERT (.*)$", text, re.MULTILINE)
    if actual != expected:
        raise RuntimeError(mode + " unchanged assertion sequence incomplete/different: " + repr(actual))
    summaries = []
    for line in text.splitlines():
        if line.startswith("{"):
            summaries.append(json.loads(line))
    required = {"mode": mode, "assertions": len(expected), "scope": FIXTURE_SCOPE}
    if summaries != [required]:
        raise RuntimeError(mode + " exact completed fixture receipt absent/different")
    return {"mode": mode, "assertions": len(actual), "assertionNames": actual,
            "receipt": required, "logSha256": hashlib.sha256(text.encode()).hexdigest()}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--expected-commit", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root = Path.cwd().resolve()
    output = args.output
    if output.is_symlink() or output.absolute() != output.resolve() or output.exists() or output.resolve().is_relative_to(root):
        raise RuntimeError("Require a fresh canonical isolated output outside checkout")
    output.mkdir(parents=True)
    diagnostics = output / "diagnostics"
    diagnostics.mkdir()
    result = {"status": "NOT_RUN", "project": PROJECT, "sourceCommit": None,
              "expectedAssertions": {"seed": len(SEED), "verify": len(VERIFY)},
              "scope": "Actual Notes-backed Boards notebook service/editor/local filesystem fixture; no rendered/native-input/browser/ACL/full-rich-Boards acceptance",
              "productSurface": "HELD for original owner seamless workspace renderer and public guarded close contract",
              "preservedHistory": "Original shared Space-key failure and BoardsPage detached-save source finding remain open"}
    commands = None
    before = None
    env = os.environ.copy()
    for variable, directory in {
        "DOTNET_CLI_HOME": "cli", "NUGET_PACKAGES": "nuget",
        "NUGET_HTTP_CACHE_PATH": "http", "NUGET_PLUGINS_CACHE_PATH": "plugins",
        "XDG_CACHE_HOME": "font-cache", "TMPDIR": "tmp", "TMP": "tmp", "TEMP": "tmp",
        "HAVEN_DATA_DIR": "fixture-data",
    }.items():
        path = output / directory
        path.mkdir(exist_ok=True)
        env[variable] = str(path)
    env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE="false", DOTNET_CLI_TELEMETRY_OPTOUT="1",
               DOTNET_NOLOGO="1", MSBUILDDISABLENODEREUSE="1")
    try:
        common = root / COMMON
        if common.is_symlink() or sha(common) != COMMON_SHA:
            raise RuntimeError("Maintained ordinary Commands source changed; review/rebind required")
        spec = importlib.util.spec_from_file_location("boards_ordinary_commands", common)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        commands = module.Commands(diagnostics, env, root)
        head = commands.run("git-head", ["git", "rev-parse", "HEAD"], 15).strip()
        if not re.fullmatch(r"[0-9a-f]{40}", args.expected_commit) or head != args.expected_commit:
            raise RuntimeError("Checkout differs from github.sha")
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
        properties = ["-p:SelfContained=false", "-p:UseSharedCompilation=false",
                      "-p:AvaloniaBuildTasksLocation=" + str(task),
                      "-p:CreateHardLinksForCopyAdditionalFilesIfPossible=false",
                      "-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=false",
                      "-p:CreateHardLinksForCopyLocalIfPossible=false",
                      "-p:CreateHardLinksForPublishFilesIfPossible=false"]
        commands.run("restore", ["dotnet", "restore", PROJECT, "--artifacts-path", str(artifacts),
                     "-r", "linux-x64", "--configfile", str(root / "NuGet.Config"),
                     "--disable-build-servers", "-p:Configuration=Release", "-m:1", "-nodeReuse:false"] + properties, 600)
        commands.run("build", ["dotnet", "build", PROJECT, "--no-restore", "-c", "Release",
                     "--artifacts-path", str(artifacts), "-r", "linux-x64", "--disable-build-servers",
                     "-m:1", "-nodeReuse:false"] + properties, 1200)
        evaluated = module.parse_sdk(commands.run("target-properties", ["dotnet", "msbuild", PROJECT,
                     "-p:Configuration=Release", "-p:RuntimeIdentifier=linux-x64", "-p:UseArtifactsOutput=true",
                     "-p:ArtifactsPath=" + str(artifacts), "-nodeReuse:false",
                     "-getProperty:TargetPath,TargetDir,TargetFramework,RuntimeIdentifier,UseAppHost,AvaloniaBuildTasksLocation"] + properties, 30))
        write(diagnostics / "target-properties.json", evaluated)
        target = Path(evaluated["TargetPath"]).resolve()
        apphost = target.with_suffix("")
        if evaluated["TargetFramework"] != "net10.0" or evaluated["RuntimeIdentifier"] != "linux-x64" or evaluated["UseAppHost"].lower() != "true":
            raise RuntimeError("Actual framework/RID/apphost differs")
        if not target.is_relative_to(artifacts) or not target.is_file() or not apphost.is_file() or not os.access(apphost, os.X_OK) or not task.is_file():
            raise RuntimeError("Actual isolated source-built entry/task/apphost absent")
        runtime = [{"path": str(path.relative_to(target.parent)), "bytes": path.stat().st_size, "sha256": sha(path)}
                   for path in sorted(target.parent.rglob("*")) if path.is_file()]
        write(diagnostics / "reported-emitted-runtime.json", runtime)
        result["seedStarted"] = True
        seed_text = commands.run("seed", [str(apphost), "seed"], 120)
        result["seed"] = validate("seed", seed_text)
        state = output / "fixture-data/boards-rich-text-state.json"
        if not state.is_file() or state.stat().st_size == 0:
            raise RuntimeError("Actual persisted seed oracle absent")
        result["persistedOracle"] = {"bytes": state.stat().st_size, "sha256": sha(state)}
        result["verifyStarted"] = True
        verify_text = commands.run("verify", [str(apphost), "verify"], 120)
        result["verify"] = validate("verify", verify_text)
        after_runtime = [{"path": str(path.relative_to(target.parent)), "bytes": path.stat().st_size, "sha256": sha(path)}
                         for path in sorted(target.parent.rglob("*")) if path.is_file()]
        if after_runtime != runtime:
            raise RuntimeError("Source-built entry runtime changed during seed/verify")
        write(diagnostics / "reported-emitted-runtime-after.json", after_runtime)
        result.update(status="PASS", executedAssertions=len(SEED) + len(VERIFY), failedAssertions=0)
    except Exception as error:
        result.update(status="FAIL", error=repr(error))
    finally:
        try:
            after = source_check(root)
            if before is not None and after != before:
                raise RuntimeError("Selected original source bodies changed")
            write(diagnostics / "source-after.json", after)
            if commands is not None:
                commands.run("git-clean-after", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
        except Exception as error:
            result.update(status="FAIL", sourceCustodyError=repr(error))
        result["binaryCustodyQualification"] = "Generated body hashes only; native binaries/packages and fixture stores are not uploaded"
        write(diagnostics / "result.json", result)
    print(json.dumps(result, indent=2))
    return 0 if result["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
