#!/usr/bin/env python3
"""Normal source builds of unchanged Write fixtures, using B1's Commands engine.

No fixture DLL import, owner compile replacement, synthetic document or browser
acceptance. Existing raw-key fixture logic remains native/source admission scope.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import sys

COMMANDS_PATH = "apps/Web/Tests/ci/run-ordinary-native.py"
COMMANDS_SHA = "a57aa33f71714c2add7a7ad7999e238d52177da2a483fe49e1c0405319f4be38"
SUITES = {
    "space15": ("apps/Web/Tests/BrowserWriteSpace.Tests.csproj",
                "apps/Web/Tests/BrowserWriteSpaceRegression.cs",
                "3f72a2b4310db50ca7e70a5b622837367d0c40de2d379a0ab8e0fa478bbd15ee"),
    "durable65": ("apps/Web/Write/Tests/WriteBrowser.Persistence.Tests.csproj",
                  "apps/Web/Write/Tests/Program.cs",
                  "44b11fa3bb3b508bf249bfb52892144f999d754c56d333d0ab325c0e0f7217b8"),
}


def require(value, message):
    if not value:
        raise RuntimeError(message)


def digest(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for part in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(part)
    return result.hexdigest()


def write_json(path, data):
    text = json.dumps(data, indent=2) + "\n"
    require(len(text.encode()) <= 1024 * 1024, "Finite diagnostic metadata cap exceeded")
    path.write_text(text)


def import_commands(root):
    path = root / COMMANDS_PATH
    require(path.resolve(strict=True) == path and not path.is_symlink(), "Canonical maintained command source required")
    require(digest(path) == COMMANDS_SHA, "Reviewed B1 command source differs")
    spec = importlib.util.spec_from_file_location("existing_write_ordinary_commands", path)
    require(spec is not None and spec.loader is not None, "Maintained source loader absent")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    require(digest(path) == COMMANDS_SHA, "Maintained command source changed during import")
    return module


def validate_space(text, fixture):
    # The exact unchanged fixture contains fifteen literal Check names. Refuse
    # duplicates, partial summaries or a changed fixture/count/three-word oracle.
    expected = re.findall(r'Check\("([^"]+)"', fixture.read_text())
    actual = re.findall(r"^PASS: (.+)$", text, re.MULTILINE)
    require(len(expected) == len(set(expected)) == 15 and actual == expected,
            "Exact original fifteen raw-key/authoring/render/persistence assertions required")
    require("15 actual native raw-key/space ownership checks PASS;" in text,
            "Complete awaited original fixture summary absent")
    return {"assertions": 15, "actualNames": actual,
            "scope": "Real native raw event/scene/editor/repository; browser TextInput admission is source-reviewed fixture condition"}


def validate_durable(text, mode, expected):
    records = []
    for line in text.splitlines():
        if line.startswith("{"):
            try:
                item = json.loads(line)
            except json.JSONDecodeError:
                continue
            if item.get("mode") == mode and "assertions" in item:
                records.append(item)
    actual = re.findall(r"^ASSERT (.+)$", text, re.MULTILINE)
    require(len(records) == 1 and records[0]["assertions"] == expected and len(actual) == expected,
            "Actual unchanged " + mode + " assertion count/complete receipt mismatch")
    return {"mode": mode, "assertions": expected, "actualNames": actual, "originalReceipt": records[0]}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--suite", choices=SUITES, required=True)
    parser.add_argument("--expected-commit", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root = Path.cwd().resolve(strict=True)
    require(re.fullmatch(r"[0-9a-f]{40}", args.expected_commit), "Exact adopted checkout SHA required")
    require(args.output.is_absolute() and not args.output.is_symlink(), "Fresh absolute output required")
    output = args.output.resolve()
    require(not output.exists() and not output.is_relative_to(root), "Fresh disposable output outside checkout required")
    output.mkdir(parents=True)
    diagnostics = output / "diagnostics"
    diagnostics.mkdir()
    project, fixture_name, fixture_sha = SUITES[args.suite]
    fixture = root / fixture_name
    result = {"suite": args.suite, "status": "NOT_RUN", "sourceCommit": None,
              "executed": None, "scope": "Existing native authoring/render/local durable tests only; no WASM/browser/provider/ACL/full parity",
              "history": "Historical f8de canonical Space10PASS1FAIL4NOTRUN and proposal15PASS unchanged; earlier local durable65 evidence remains scoped historical."}
    commands = None
    before = None
    try:
        sys.dont_write_bytecode = True
        ordinary = import_commands(root)
        require(digest(fixture) == fixture_sha, "Existing fixture/oracle changed; new review required")
        before = {name: digest(root / name) for name in [project, fixture_name, COMMANDS_PATH,
                  str(Path(__file__).resolve().relative_to(root)), "NuGet.Config", "global.json"]}
        env = os.environ.copy()
        for variable, directory in {
            "DOTNET_CLI_HOME": "cli", "NUGET_PACKAGES": "nuget", "NUGET_HTTP_CACHE_PATH": "http",
            "NUGET_PLUGINS_CACHE_PATH": "plugins", "XDG_CACHE_HOME": "font-cache",
            "TMPDIR": "tmp", "TMP": "tmp", "TEMP": "tmp",
        }.items():
            path = output / directory
            path.mkdir(exist_ok=True)
            env[variable] = str(path)
        env.update(CI="true", DOTNET_GENERATE_ASPNET_CERTIFICATE="false", DOTNET_CLI_TELEMETRY_OPTOUT="1",
                   DOTNET_NOLOGO="1", MSBUILDDISABLENODEREUSE="1")
        commands = ordinary.Commands(diagnostics, env, root)
        require(commands.run("git-head", ["git", "rev-parse", "HEAD"], 15).strip() == args.expected_commit,
                "Actual new checkout differs from dispatched github.sha")
        result["sourceCommit"] = args.expected_commit
        commands.run("git-clean-before", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
        commands.run("source-tree-pins", ["git", "ls-tree", "-r", "HEAD"], 15)
        tracked = commands.run("fixture-tracked", ["git", "ls-files", "--", project, fixture_name,
                                COMMANDS_PATH, str(Path(__file__).resolve().relative_to(root))], 15)
        require(len(set(tracked.splitlines())) == 4, "Root must adopt both unchanged fixture files and maintained drivers")
        write_json(diagnostics / "source-pins-before.json", before)
        require(commands.run("dotnet-version", ["dotnet", "--version"], 30).strip() == "10.0.401", "SDK10.0.401 required")
        commands.run("dotnet-info", ["dotnet", "--info"], 30)
        artifacts = output / "artifacts"
        task = artifacts / "bin/Avalonia.Build.Tasks/release/Avalonia.Build.Tasks.dll"
        properties = ["-p:SelfContained=false", "-p:UseSharedCompilation=false",
                      "-p:AvaloniaBuildTasksLocation=" + str(task)]
        common = ["--artifacts-path", str(artifacts), "-r", "linux-x64",
                  "--disable-build-servers", "-m:1", "-nodeReuse:false"] + properties
        commands.run("restore", ["dotnet", "restore", project, "--configfile", str(root / "NuGet.Config"),
                     "-p:Configuration=Release"] + common, 600)
        commands.run("build", ["dotnet", "build", project, "--no-restore", "-c", "Release"] + common, 1200)
        actual = ordinary.parse_sdk(commands.run("target-properties", ["dotnet", "msbuild", project,
            "-p:Configuration=Release", "-p:RuntimeIdentifier=linux-x64", "-p:UseArtifactsOutput=true",
            "-p:ArtifactsPath=" + str(artifacts), "-nodeReuse:false",
            "-getProperty:TargetPath,TargetFramework,RuntimeIdentifier,UseAppHost"] + properties, 30))
        write_json(diagnostics / "target-properties.json", actual)
        target = Path(actual["TargetPath"]).resolve(strict=True)
        apphost = target.with_suffix("")
        require(actual["TargetFramework"] == "net10.0" and actual["RuntimeIdentifier"] == "linux-x64" and
                actual["UseAppHost"].lower() == "true" and target.is_relative_to(artifacts) and
                apphost.is_file() and os.access(apphost, os.X_OK), "Actual isolated source-built entry DLL/apphost absent")
        write_json(diagnostics / "entry-identity.json", {"entrySHA": digest(target), "apphostSHA": digest(apphost)})
        outcomes = []
        if args.suite == "space15":
            require(task.is_file(), "Normal source-built owner Avalonia task required")
            data = output / "space-fixture"
            data.mkdir()
            commands.env["HAVEN_DATA_DIR"] = str(data)
            outcomes.append(validate_space(commands.run("native-space", [str(apphost)], 120), fixture))
            result.update(executed=15, passed=15, failed=0)
        else:
            data = output / "durable-fixture"
            data.mkdir()
            commands.env["HAVEN_DATA_DIR"] = str(data)
            # Separate real apphost subprocesses; preserve identical fixture directory
            # across seed/restart-verify, then isolated subscriber-error fixture.
            for mode, count in [("seed", 23), ("verify", 40), ("subscriber", 2)]:
                if mode == "subscriber":
                    separate = output / "subscriber-fixture"
                    separate.mkdir()
                    commands.env["HAVEN_DATA_DIR"] = str(separate)
                outcomes.append(validate_durable(commands.run("native-" + mode, [str(apphost), mode], 120), mode, count))
            result.update(executed=65, passed=65, failed=0)
        write_json(diagnostics / "actual-original-assertions.json", outcomes)
        require(all(digest(root / name) == value for name, value in before.items()), "Current source custody mismatch")
        result["status"] = "PASS"
    except Exception as error:
        result.update(status="FAIL", error=repr(error))
    finally:
        # A failed/partial original process can still have observed assertions.
        # Preserve those labels without turning a prefix into a complete PASS
        # or inventing executed/failure counts for checks not reached.
        observed = []
        for name in ("native-space", "native-seed", "native-verify", "native-subscriber"):
            log = diagnostics / (name + ".log")
            if log.exists():
                text = log.read_text(errors="replace")
                observed.append({"stage": name, "logSHA": digest(log),
                    "observedPassLabels": re.findall(r"^PASS: (.+)$", text, re.MULTILINE),
                    "observedAssertLabels": re.findall(r"^ASSERT (.+)$", text, re.MULTILINE)})
        result["observedNativeLogPrefixes"] = observed
        if commands is not None:
            try:
                commands.run("git-clean-after", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
                require(before is None or all(digest(root / name) == value for name, value in before.items()),
                        "Final original fixture/driver/project custody mismatch")
            except Exception as error:
                result.update(status="FAIL", sourceCustodyError=repr(error))
        write_json(diagnostics / "result.json", result)
    print(json.dumps(result, indent=2))
    return 0 if result["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
