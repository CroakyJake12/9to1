#!/usr/bin/env python3
"""Run the unchanged full normal Mail72 inside the original trusted caller session."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

sys.dont_write_bytecode = True
from team_c_mail72_source_guard import digest, owned_output, require_disposable_runner, verify_current_cut

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
FIXTURE = ("HAVEN_MAIL_FIXTURE_HOST", "HAVEN_MAIL_FIXTURE_IMAP_TLS_PORT", "HAVEN_MAIL_FIXTURE_SMTP_TLS_PORT", "HAVEN_MAIL_FIXTURE_ADDRESS", "HAVEN_MAIL_FIXTURE_PASSWORD")


def write(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def file_catalog(root):
    rows = []
    for path in sorted(root.rglob("*")):
        if path.is_symlink():
            raise RuntimeError("Linked runtime body refused")
        if path.is_file():
            data = path.read_bytes()
            rows.append({"path": path.relative_to(root).as_posix(), "bytes": len(data), "sha256": digest(data)})
    return rows


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--expected-commit", required=True)
    p.add_argument("--manifest", required=True)
    p.add_argument("--manifest-sha", required=True)
    args = p.parse_args()
    require_disposable_runner()
    root = Path.cwd().resolve()
    cut, before = verify_current_cut(root, args.manifest, args.manifest_sha, args.expected_commit)
    output = owned_output(root)
    diagnostics = output / "diagnostics"
    diagnostics.mkdir(parents=True, exist_ok=True)
    for key in FIXTURE:
        if not os.environ.get(key):
            raise RuntimeError("Approved real loopback fixture environment is incomplete")
    if os.environ[FIXTURE[0]] != "localhost":
        raise RuntimeError("Exact owned localhost fixture required")
    environment = os.environ.copy()
    for key, name in {"DOTNET_CLI_HOME": "cli", "NUGET_PACKAGES": "nuget", "NUGET_HTTP_CACHE_PATH": "http", "NUGET_PLUGINS_CACHE_PATH": "plugins", "XDG_CACHE_HOME": "cache", "XDG_CONFIG_HOME": "config", "TMPDIR": "tmp", "TMP": "tmp", "TEMP": "tmp"}.items():
        directory = output / name
        directory.mkdir(exist_ok=True)
        environment[key] = str(directory)
    environment.update(DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_GENERATE_ASPNET_CERTIFICATE="false", MSBUILDDISABLENODEREUSE="1", PYTHONDONTWRITEBYTECODE="1")
    commands = []
    result = {"status": "NOT_RUN", "sourceCommit": args.expected_commit, "sourceBasis": cut["sourceBasis"], "project": cut["owningProject"], "requiredTests": 72, "filter": None, "assertionCount": None, "qualification": "Provisional full managed Mail component and synthetic maintained loopback real TLS only; no native GUI/Home/provider credentials/package/Windows/full release acceptance."}
    write(diagnostics / "source-before.json", before)

    def command(label, argv, seconds):
        log = diagnostics / (label + ".log")
        record = {"name": label, "argv": argv, "startedUtc": time.time(), "timeoutSeconds": seconds, "sameOriginalCallerSession": True}
        commands.append(record)
        with log.open("wb") as stream:
            # No detached session: the unchanged original MailCallerSession owns
            # and samples the real child family, then drains it before CA removal.
            process = subprocess.Popen(argv, stdout=stream, stderr=subprocess.STDOUT, env=environment, cwd=root)
            record["pid"] = process.pid
            try:
                record["exitCode"] = process.wait(timeout=seconds)
            except subprocess.TimeoutExpired:
                record["timeout"] = True
                raise RuntimeError("Original command deadline exceeded; outer original family owns cleanup")
            finally:
                record["endedUtc"] = time.time()
                write(diagnostics / "commands.json", commands)
        if log.stat().st_size > 8 * 1024 * 1024:
            raise RuntimeError("Declared post-completion command log bound exceeded")
        text = log.read_text(errors="replace")
        if record["exitCode"]:
            raise RuntimeError("Original normal command failed; retained exit and raw log")
        return text

    artifacts = output / "artifacts"
    flags = ["--artifacts-path", str(artifacts), "-r", "linux-x64", "--disable-build-servers", "-m:1", "-nodeReuse:false", "-p:SelfContained=false", "-p:UseSharedCompilation=false", "-p:CreateHardLinksForCopyAdditionalFilesIfPossible=false", "-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=false", "-p:CreateHardLinksForCopyLocalIfPossible=false", "-p:CreateHardLinksForPublishFilesIfPossible=false"]
    dotnet = shutil.which("dotnet")
    target = runtime_before = None
    failure = None
    try:
        if shutil.disk_usage(output).free < 4 * 1024**3:
            raise RuntimeError("Declared disposable output capacity insufficient")
        if not dotnet:
            raise RuntimeError("Actual reviewed SDK tool absent")
        tool = Path(dotnet).resolve()
        result["dotnetTool"] = {"path": str(tool), "bytes": tool.stat().st_size, "sha256": digest(tool.read_bytes())}
        if command("sdk-version", [dotnet, "--version"], 30).strip() != "10.0.401":
            raise RuntimeError("Reviewed SDK10.0.401 required")
        project = cut["owningProject"]
        command("restore", [dotnet, "restore", project, "--configfile", str(root / "NuGet.Config"), "--disable-parallel", "-p:Configuration=Release"] + flags, 600)
        restore_rows = []
        mail_library_present = False
        for assets in sorted(artifacts.rglob("project.assets.json")):
            data = assets.read_bytes()
            value = json.loads(data)
            libraries = sorted(value["libraries"])
            mail_library_present |= "MailKit/4.18.0" in libraries
            restore_rows.append({"path": assets.relative_to(output).as_posix(), "bytes": len(data), "sha256": digest(data), "libraries": libraries, "packageFolders": list(value["packageFolders"])})
        if len(restore_rows) != 5 or not mail_library_present:
            raise RuntimeError("Actual five-project restore or original MailKit4.18.0 closure differs")
        write(diagnostics / "restore-assets.json", restore_rows)
        command("build", [dotnet, "build", project, "--no-restore", "-c", "Release", "-f", "net10.0"] + flags, 600)
        inputs = []
        for i, project_path in enumerate(cut["ordinaryProjects"]):
            text = command("evaluated-project-" + str(i), [dotnet, "msbuild", project_path, "-p:Configuration=Release", "-p:RuntimeIdentifier=linux-x64", "-p:UseArtifactsOutput=true", "-p:ArtifactsPath=" + str(artifacts), "-p:SelfContained=false", "-p:UseSharedCompilation=false", "-nodeReuse:false", "-getProperty:TargetPath,TargetFramework", "-getItem:Compile,EmbeddedResource,Content,AdditionalFiles"], 30)
            offset = text.find("{")
            value, length = json.JSONDecoder().raw_decode(text[offset:])
            if offset < 0 or text[offset + length:].strip():
                raise RuntimeError("Exact ordinary evaluated project result absent")
            write(diagnostics / ("evaluated-project-" + str(i) + ".json"), value)
            for kind, items in value.get("Items", {}).items():
                for item in items:
                    path = Path(item["FullPath"])
                    if path.is_symlink() or not path.is_file() or not (path.resolve().is_relative_to(root) or path.resolve().is_relative_to(artifacts)):
                        raise RuntimeError("Ordinary evaluated source/input is absent or foreign")
                    data = path.read_bytes()
                    inputs.append({"project": project_path, "kind": kind, "path": str(path), "bytes": len(data), "sha256": digest(data), "generatedOwned": path.resolve().is_relative_to(artifacts)})
            if project_path == project:
                target = Path(value["Properties"]["TargetPath"])
                if value["Properties"]["TargetFramework"] != "net10.0" or not target.is_file() or not target.resolve().is_relative_to(artifacts):
                    raise RuntimeError("Actual source-built ordinary Mail assembly is absent")
        write(diagnostics / "evaluated-inputs.json", inputs)
        runtime_before = file_catalog(target.parent)
        write(diagnostics / "runtime-before.json", runtime_before)
        discovery = command("whole-discovery", [dotnet, "test", project, "--no-build", "--no-restore", "-c", "Release", "-f", "net10.0", "--list-tests"] + flags, 120)
        names = [line.strip() for line in discovery.splitlines() if line.strip().startswith("HavenOS.Mail.Tests.")]
        if len(names) != 72 or len(set(names)) != 72 or sorted(names) != cut["expectedTestNames"]:
            raise RuntimeError("Original full72 discovery identities differ")
        write(diagnostics / "whole-discovery.json", {"names": names, "discovered": len(names), "filter": None})
        tests = diagnostics / "test-results"
        tests.mkdir()
        trx = tests / "mail72.trx"
        started = time.time_ns()
        test_failure = None
        try:
            command("whole-unfiltered-mail72", [dotnet, "test", project, "--no-build", "--no-restore", "-c", "Release", "-f", "net10.0", "--logger", "trx;LogFileName=mail72.trx", "--results-directory", str(tests)] + flags, 600)
        except RuntimeError as error:
            test_failure = error
        if not trx.is_file() or trx.stat().st_mtime_ns < started:
            raise RuntimeError("Fresh original full72 TRX absent")
        document = ET.parse(trx).getroot()
        cases = document.findall(".//t:UnitTestResult", NS)
        counts = {key: int(value) for key, value in document.find(".//t:Counters", NS).attrib.items()}
        actual_names = [row.attrib["testName"] for row in cases]
        if len(cases) != 72 or len(set(actual_names)) != 72 or sorted(actual_names) != cut["expectedTestNames"] or counts["total"] != 72 or counts["executed"] != 72 or counts["notExecuted"]:
            raise RuntimeError("Full72 execution identities/counters differ or a test is skipped")
        tls = [row for row in cases if row.attrib["testName"] in cut["originalTLS3TestNames"]]
        write(diagnostics / "whole-trx-readback.json", {"counters": counts, "cases": [{"name": x.attrib["testName"], "outcome": x.attrib["outcome"]} for x in cases], "originalTLS3": [{"name": x.attrib["testName"], "outcome": x.attrib["outcome"]} for x in tls], "sha256": digest(trx.read_bytes()), "filter": None})
        result.update(counters=counts, trxSha256=digest(trx.read_bytes()), runtimeFiles=len(runtime_before), evaluatedInputRows=len(inputs), originalTLS3Executed=len(tls))
        if test_failure or counts["passed"] != 72 or counts["failed"] or len(tls) != 3 or any(x.attrib["outcome"] != "Passed" for x in cases):
            raise RuntimeError("Original full72 contains a genuine failure; raw TRX/log preserved")
        result["status"] = "PASS_FULL_NORMAL_MAIL72_WITH_REAL_LOOPBACK_TRUSTED_TLS_UNACCEPTED"
    except Exception as error:
        failure = error
        result.update(status="FAIL_OR_INCOMPLETE", failureType=type(error).__name__)
    finally:
        try:
            _, after = verify_current_cut(root, args.manifest, args.manifest_sha, args.expected_commit)
            write(diagnostics / "source-after.json", after)
            if after != before:
                raise RuntimeError("Current whole source changed during ordinary tests")
            if target is not None and runtime_before is not None:
                after_runtime = file_catalog(target.parent)
                write(diagnostics / "runtime-after.json", after_runtime)
                if after_runtime != runtime_before:
                    raise RuntimeError("Actual source-built ordinary runtime changed")
        except Exception as error:
            result.update(status="FAIL_OR_INCOMPLETE", finalCustodyFailureType=type(error).__name__)
            failure = failure or error
        result["commands"] = commands
        write(diagnostics / "normal-mail72-result.json", result)
        # Upload gating is performed only by the outer fixture after its genuine
        # Java, CRL and caller drains and runner trust cleanup have completed.
    if failure:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
