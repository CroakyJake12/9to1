#!/usr/bin/env python3
"""Source-built unchanged Motion CLI/store workflow, using maintained B1 Commands."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re

COMMANDS_SHA256 = "a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38"
LEDGER_SHA256 = "8a9c70cf3b95392a083c71d5656fae99897c9b96be115f4612e3862142f68659"
PROJECT = "apps/Web/Tests/MotionProject/MotionProjectWorkflow.Tests.csproj"
LEDGER = "apps/Web/Tests/MotionProject/source-pins.json"
CASE_NAME = "original-motion-project-cli-real-file-workflow"


def digest(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for part in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(part)
    return h.hexdigest()


def write_json(path, value):
    raw = (json.dumps(value, indent=2) + "\n").encode()
    if len(raw) > 8 * 1024 * 1024:
        raise RuntimeError("Bounded diagnostic metadata overflow")
    path.write_bytes(raw)


def sources(root, ledger):
    for rel, expected in ledger["files"].items():
        file = root / rel
        if file.stat().st_size != expected["bytes"] or digest(file) != expected["sha256"]:
            raise RuntimeError("Actual reviewed source differs: " + rel)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--expected-commit", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root, output = Path.cwd().resolve(), args.output.resolve()
    if output.exists() or output.is_relative_to(root):
        raise RuntimeError("Require fresh owned output outside the checkout")
    output.mkdir(parents=True)
    diagnostics = output / "diagnostics"
    diagnostics.mkdir()
    result = {"status": "NOT_RUN", "expected": 1, "discovered": 1, "executed": 0,
              "passed": 0, "failed": 0, "notRun": 1,
              "scope": "Original Motion local file/model/CLI only; browser, Files identity resolution, media decoding/rendering, provider, crash injection and full Motion NOT_RUN"}
    commands, ledger, ledger_sha = None, None, None
    try:
        custodian = root / "apps/Web/Tests/ci/run-ordinary-native.py"
        if digest(custodian) != COMMANDS_SHA256:
            raise RuntimeError("Maintained B1 Commands changed; review before execution")
        spec = importlib.util.spec_from_file_location("maintained_b1_motion_commands", custodian)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        env = os.environ.copy()
        for variable, directory in {"DOTNET_CLI_HOME": "cli", "NUGET_PACKAGES": "nuget",
                                    "NUGET_HTTP_CACHE_PATH": "http", "NUGET_PLUGINS_CACHE_PATH": "plugins",
                                    "XDG_CACHE_HOME": "cache", "TMPDIR": "tmp", "TMP": "tmp", "TEMP": "tmp"}.items():
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
        if commands.run("dotnet-version", ["dotnet", "--version"], 30).strip() != "10.0.401":
            raise RuntimeError("Requires maintained SDK10.0.401")
        commands.run("dotnet-info", ["dotnet", "--info"], 30)
        ledger_path = root / LEDGER
        ledger_sha = digest(ledger_path)
        if ledger_sha != LEDGER_SHA256:
            raise RuntimeError("Reviewed original source ledger changed")
        ledger = json.loads(ledger_path.read_text())
        sources(root, ledger)
        artifacts = output / "artifacts"
        props = ["-p:SelfContained=false", "-p:UseSharedCompilation=false", "-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=false",
                 "-p:CreateHardLinksForCopyLocalIfPossible=false", "-p:CreateHardLinksForPublishFilesIfPossible=false"]
        commands.run("restore", ["dotnet", "restore", PROJECT, "--artifacts-path", str(artifacts), "-r", "linux-x64",
                     "--configfile", str(root / "NuGet.Config"), "--disable-build-servers", "-p:Configuration=Release",
                     "-m:1", "-nodeReuse:false"] + props, 600)
        commands.run("build", ["dotnet", "build", PROJECT, "--no-restore", "-c", "Release", "--artifacts-path", str(artifacts),
                     "-r", "linux-x64", "--disable-build-servers", "-m:1", "-nodeReuse:false"] + props, 600)
        evaluated = module.parse_sdk(commands.run("target-properties", ["dotnet", "msbuild", PROJECT,
                     "-p:Configuration=Release", "-p:RuntimeIdentifier=linux-x64", "-p:UseArtifactsOutput=true",
                     "-p:ArtifactsPath=" + str(artifacts), "-nodeReuse:false",
                     "-getProperty:TargetPath,TargetFramework,RuntimeIdentifier,UseAppHost"] + props, 30))
        target = Path(evaluated["TargetPath"]).resolve()
        apphost = target.with_suffix("")
        if (evaluated["TargetFramework"] != "net10.0" or evaluated["RuntimeIdentifier"] != "linux-x64"
                or evaluated["UseAppHost"].lower() != "true" or not target.is_relative_to(artifacts)
                or not target.is_file() or not apphost.is_file() or not os.access(apphost, os.X_OK)):
            raise RuntimeError("Actual isolated managed entry/apphost closure missing")
        with target.open("rb") as stream:
            if stream.read(2) != b"MZ":
                raise RuntimeError("Actual managed entry header absent")
        with apphost.open("rb") as stream:
            if stream.read(4) != b"\x7fELF":
                raise RuntimeError("Actual Linux apphost header absent")
        write_json(diagnostics / "target-properties.json", evaluated)
        write_json(diagnostics / "binary-pins.json", [{"path": str(p.relative_to(artifacts)), "bytes": p.stat().st_size, "sha256": digest(p)}
                   for p in sorted(artifacts.rglob("*")) if p.is_file() and p.suffix in (".dll", ".pdb")])
        result.update(nativeStarted=True, entryDLLSHA256=digest(target), apphostSHA256=digest(apphost), sourceLedgerSHA256=ledger_sha)
        native_report = diagnostics / "native-results.json"
        deferred = None
        try:
            commands.run("native", [str(apphost), str(native_report)], 120)
        except Exception as error:
            deferred = error
        # Preserve actual structured failure BEFORE rejecting unexpected native exit or custody.
        if native_report.is_file() and native_report.stat().st_size <= 65536:
            native = json.loads(native_report.read_text())
            result["nativeResultSHA256"] = digest(native_report)
            result["actualNativeResult"] = native
            for key in ("discovered", "executed", "passed", "failed", "notRun"):
                result[key] = native[key]
        else:
            raise RuntimeError("Native result absent or unbounded; no result inferred")
        if deferred is not None:
            raise deferred
        if (native["name"] != CASE_NAME or native["originalReturnCode"] != 0 or native["failure"] is not None
                or tuple(native[k] for k in ("discovered", "executed", "passed", "failed", "notRun")) != (1, 1, 1, 0, 0)):
            raise RuntimeError("Original Motion workflow did not genuinely pass")
        if (diagnostics / "native.log").read_text().splitlines() != ["START " + CASE_NAME, "PASS " + CASE_NAME]:
            raise RuntimeError("Original entry case completion log differs")
        result["status"] = "ORIGINAL_MOTION_LOCAL_PROJECT_WORKFLOW_PASS"
    except Exception as error:
        result.update(status="FAIL", error=repr(error))
    finally:
        if ledger is not None:
            try:
                sources(root, ledger)
                if digest(root / LEDGER) != ledger_sha:
                    raise RuntimeError("Ledger changed during execution")
                result["sourceAfterUnchanged"] = True
            except Exception as error:
                result.update(status="FAIL", sourceError=repr(error))
        if commands is not None:
            try:
                commands.run("git-clean-after", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
            except Exception as error:
                result.update(status="FAIL", sourceCustodyError=repr(error))
        write_json(diagnostics / "result.json", result)
    print(json.dumps(result, indent=2))
    return 0 if result["status"] == "ORIGINAL_MOTION_LOCAL_PROJECT_WORKFLOW_PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
