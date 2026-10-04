#!/usr/bin/env python3
"""Ordinary maintained native projects; uploads contain no fixture stores/binaries.

Each CI matrix job gets fresh artifacts/CLI/cache/data directories. No imported
test DLL, compiler/task substitution, source patch, provider config or deployment.
"""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import re
import selectors
import signal
import subprocess
import time

SUITES = {
    "navigation5": ("apps/Web/Tests/BrowserNavigationCancellation.Tests.csproj", 5),
    "navigation37": ("apps/Web/Tests/BrowserNavigation.Tests.csproj", 37),
    "bindings15": ("apps/Web/Accounts/Tests/BrowserAccounts.Tests.csproj", 15),
    "factory6": ("apps/Web/Accounts/Tests/AccountSettings.Factory.Tests.csproj", 6),
    "files17": ("apps/Web/Files/Tests/FilesBrowser.Tests.csproj", 17),
    "sites18": ("apps/Web/Sites/Tests/SitesBrowser.Tests.csproj", 18),
}
LOG_CAP = 8 * 1024 * 1024


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for part in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(part)
    return value.hexdigest()


def processes():
    result = {}
    for directory in Path("/proc").iterdir():
        if not directory.name.isdecimal():
            continue
        try:
            text = (directory / "stat").read_text()
            fields = text[text.rfind(")") + 2:].split()
            result[int(directory.name)] = (int(fields[1]), int(fields[19]))
        except (FileNotFoundError, ProcessLookupError, PermissionError):
            continue
    return result


class Commands:
    """Retain actual child identities; never signal numeric groups or strangers."""
    def __init__(self, diagnostics, env, cwd):
        self.diagnostics, self.env, self.cwd = diagnostics, env, cwd
        self.deadline = time.monotonic() + 26 * 60
        self.records = []
        # Adopt orphaned descendants so final ECHILD is a kernel observation.
        if ctypes.CDLL(None, use_errno=True).prctl(36, 1, 0, 0, 0) != 0:
            raise OSError(ctypes.get_errno(), "PR_SET_CHILD_SUBREAPER failed")

    def run(self, name, argv, timeout):
        start = time.monotonic()
        end = min(self.deadline, start + timeout)
        if end <= start:
            raise RuntimeError("CI driver deadline reached before " + name)
        before = processes()
        held = {}
        record = {"name": name, "argv": argv, "exit": None,
                  "normalEOF": False, "signals": [], "error": None}
        self.records.append(record)
        process = subprocess.Popen(argv, cwd=self.cwd, env=self.env,
                                   stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                                   stderr=subprocess.STDOUT, start_new_session=True)
        root_identity = processes().get(process.pid)
        selector = selectors.DefaultSelector()
        os.set_blocking(process.stdout.fileno(), False)
        selector.register(process.stdout, selectors.EVENT_READ)

        def refresh():
            current = processes()
            # New direct children of this subreaper and descendants of exact
            # retained births belong to this one serial command family.
            pending = set(current)
            again = True
            while again:
                again = False
                for pid in list(pending):
                    parent, birth = current[pid]
                    own_parent = parent in held and current.get(parent, (None, None))[1] == held[parent][0]
                    direct = parent == os.getpid() and before.get(pid) != current[pid]
                    actual_root = pid == process.pid and current[pid] == root_identity and parent == os.getpid()
                    if actual_root or own_parent or direct:
                        if pid not in held:
                            try:
                                fd = os.pidfd_open(pid)
                                if processes().get(pid) != (parent, birth):
                                    os.close(fd)
                                    continue
                                held[pid] = (birth, fd)
                            except ProcessLookupError:
                                continue
                        pending.remove(pid)
                        again = True
            return current

        def signal_owned(sig):
            current = refresh()
            for pid, (birth, fd) in held.items():
                if current.get(pid, (None, None))[1] != birth:
                    continue
                try:
                    signal.pidfd_send_signal(fd, sig)
                    record["signals"].append({"pid": pid, "birth": birth, "signal": sig})
                except ProcessLookupError:
                    pass

        eof, total = False, 0
        try:
            refresh()
            with (self.diagnostics / (name + ".log")).open("wb") as log:
                while not eof or process.poll() is None:
                    refresh()
                    if time.monotonic() >= end:
                        raise TimeoutError(name + " owned command deadline")
                    for key, _ in selector.select(0.1):
                        data = os.read(key.fd, 65536)
                        if not data:
                            selector.unregister(key.fileobj)
                            eof = True
                        else:
                            if total + len(data) > LOG_CAP:
                                raise RuntimeError(name + " bounded diagnostic log overflow")
                            log.write(data)
                            total += len(data)
                record["exit"] = process.wait()
                record["normalEOF"] = eof
        except Exception as error:
            record["error"] = repr(error)
            signal_owned(signal.SIGTERM)
        finally:
            # Allow ordinary descendants to finish, then force only retained
            # identities on failure. Any forced cleanup prevents stage PASS.
            for round_number in range(100):
                current = refresh()
                process.poll()
                for pid, (birth, _) in held.items():
                    if pid == process.pid:
                        continue
                    if current.get(pid, (None, None))[1] != birth:
                        continue
                    try:
                        os.waitpid(pid, os.WNOHANG)
                    except ChildProcessError:
                        pass
                current = processes()
                alive = [pid for pid, (birth, _) in held.items()
                         if current.get(pid, (None, None))[1] == birth]
                if not alive and process.poll() is not None:
                    break
                if round_number == 20:
                    signal_owned(signal.SIGTERM)
                if round_number == 50:
                    signal_owned(signal.SIGKILL)
                time.sleep(0.05)
            current = refresh()
            record["births"] = [{"pid": pid, "birth": birth,
                                 "gone": current.get(pid, (None, None))[1] != birth}
                                for pid, (birth, _) in held.items()]
            try:
                found, _ = os.waitpid(-1, os.WNOHANG)
                record["finalECHILD"] = False
                record["unexpectedWaitableChild"] = found
            except ChildProcessError:
                record["finalECHILD"] = True
            record["exitAfterDrain"] = process.poll()
            record["seconds"] = round(time.monotonic() - start, 3)
            record["bytes"] = total
            record["familyClosed"] = bool(held) and all(x["gone"] for x in record["births"]) and record["finalECHILD"]
            selector.close()
            process.stdout.close()
            for _, fd in held.values():
                os.close(fd)
            write_json(self.diagnostics / "commands.json", self.records)
        if record["error"] or record["signals"] or not record["familyClosed"] or not record["normalEOF"]:
            raise RuntimeError(name + " failed owned command custody: " + json.dumps(record))
        if record["exit"] != 0:
            raise RuntimeError(name + " exit " + str(record["exit"]))
        return (self.diagnostics / (name + ".log")).read_text(errors="replace")


def parse_sdk(text):
    offset = text.find("{")
    if offset < 0:
        raise ValueError("SDK property JSON absent")
    value, end = json.JSONDecoder().raw_decode(text[offset:])
    if text[offset:][end:].strip():
        raise ValueError("Unexpected trailing SDK property output")
    return value["Properties"]


def validate_result(suite, expected, text, report):
    if len(re.findall(r"^PASS(?:[: ]|$)", text, re.MULTILINE)) != expected:
        raise RuntimeError("Per-case PASS count does not match the unchanged suite")
    if suite == "navigation5":
        data = json.loads(report.read_text())
        actual = (data["discovered"], data["passed"], data["failed"], data["notRun"])
        if actual != (expected, expected, 0, 0) or len(data["checks"]) != expected or not all(x["passed"] for x in data["checks"]):
            raise RuntimeError("Same native5 checks did not all pass: " + str(actual))
    elif suite == "navigation37":
        if f"Discovered: {expected}; executed: {expected}; passed: {expected}; failed: 0." not in text:
            raise RuntimeError("Navigation37 complete summary absent")
    elif suite in ("bindings15", "factory6"):
        pattern = rf"Discovered={expected} Executed={expected} Passed={expected} Failed=0 Skipped=0(?: |$)"
        if not re.search(pattern, text, re.MULTILINE):
            raise RuntimeError("Account complete summary absent")
    else:
        pattern = rf"^RESULT expected={expected} executed={expected} passed={expected} failed=0(?: |$)"
        if not re.search(pattern, text, re.MULTILINE):
            raise RuntimeError("Files/Sites complete summary absent; do not relabel old counts")



def sites_children(data, diagnostics):
    # These receipts are written by the unchanged C# IndependentProcesses case
    # after actual child apphost exit. C# seed/verify retain all original native
    # identity/schema/index/renderer/artifact oracles; Python does not replace them.
    directory = data / "sites/independent-native-seed-and-verify-processes"
    evidence = {}
    for mode, assertions in (("seed", 17), ("verify", 6)):
        log = directory / (mode + ".log")
        exit_file = directory / (mode + ".exit")
        if not log.is_file() or log.stat().st_size > LOG_CAP or not exit_file.is_file() or exit_file.stat().st_size > 16:
            raise RuntimeError("Actual Sites child log/exit receipt absent or unbounded: " + mode)
        if exit_file.read_text().strip() != "0":
            raise RuntimeError("Actual Sites child did not exit0: " + mode)
        matches = re.findall(rf"^RESULT mode={mode} expected=1 executed=1 passed=1 failed=0 assertions=(\d+)$", log.read_text(), re.MULTILINE)
        if matches != [str(assertions)]:
            raise RuntimeError("Actual Sites child original assertion count differs: " + mode)
        evidence[mode] = {"exit": 0, "assertions": assertions, "logSha256": digest(log), "exitSha256": digest(exit_file)}
    oracle = directory / "canonical-oracle.json"
    if not oracle.is_file() or oracle.stat().st_size == 0:
        raise RuntimeError("Actual Sites seed persisted oracle absent")
    # Hash/size only: never upload the oracle, actual profile/source identifiers,
    # fixture stores or project/artifact JSON. The native verify process checks it.
    evidence["persistedOracle"] = {"bytes": oracle.stat().st_size, "sha256": digest(oracle)}
    evidence["scope"] = "Actual source-created child receipts and original native seed17/verify6 assertions; no browser/provider/deployment proof"
    write_json(diagnostics / "sites-child-receipts.json", evidence)
    return evidence


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--suite", required=True, choices=SUITES)
    parser.add_argument("--expected-commit", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root = Path.cwd().resolve()
    output = args.output.resolve()
    if output.exists() or output.is_relative_to(root):
        raise RuntimeError("Require a fresh isolated output outside the checkout")
    output.mkdir(parents=True)
    diagnostics = output / "diagnostics"
    diagnostics.mkdir()
    project, expected = SUITES[args.suite]
    result = {"suite": args.suite, "project": project, "expected": expected,
              "status": "NOT_RUN", "sourceCommit": None,
              "scope": "Native application regression only; browser/provider/deployment/full parity NOT_RUN",
              "preservedHistory": "Original03 RESOURCE_HOLD and17 RESOURCE_HOLD are not replaced by CI"}
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
    commands = Commands(diagnostics, env, root)
    try:
        head = commands.run("git-head", ["git", "rev-parse", "HEAD"], 15).strip()
        if not re.fullmatch(r"[0-9a-f]{40}", args.expected_commit) or head != args.expected_commit:
            raise RuntimeError("Checkout differs from github.sha")
        result["sourceCommit"] = head
        commands.run("git-clean-before", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
        # Git blob/gitlink IDs pin the complete tracked tree; no source contents
        # or private/generated fixture profiles are uploaded.
        commands.run("source-tree-pins", ["git", "ls-tree", "-r", "HEAD"], 15)
        commands.run("native-submodule-pins", ["git", "submodule", "status", "--recursive", "--",
                     "framework/CUI/vendor/Avalonia/external/XamlX",
                     "framework/CUI/vendor/Avalonia/external/Avalonia.DBus"], 15)
        version = commands.run("dotnet-version", ["dotnet", "--version"], 30).strip()
        if version != "10.0.401":
            raise RuntimeError("Expected SDK10.0.401, got " + version)
        commands.run("dotnet-info", ["dotnet", "--info"], 30)
        artifacts = output / "artifacts"
        task = artifacts / "bin/Avalonia.Build.Tasks/release/Avalonia.Build.Tasks.dll"
        properties = ["-p:SelfContained=false", "-p:UseSharedCompilation=false",
                      "-p:AvaloniaBuildTasksLocation=" + str(task),
                      "-p:CreateHardLinksForCopyAdditionalFilesIfPossible=false",
                      "-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=false",
                      "-p:CreateHardLinksForCopyLocalIfPossible=false",
                      "-p:CreateHardLinksForPublishFilesIfPossible=false"]
        # One maintained normal restore per entry and all referenced projects.
        # No copied assets, precompiled fixture or skipped vendor task build.
        commands.run("restore", ["dotnet", "restore", project, "--artifacts-path", str(artifacts),
                     "-r", "linux-x64", "--configfile", str(root / "NuGet.Config"),
                     "--disable-build-servers", "-p:Configuration=Release", "-m:1",
                     "-nodeReuse:false"] + properties, 600)
        commands.run("build", ["dotnet", "build", project, "--no-restore", "-c", "Release",
                     "--artifacts-path", str(artifacts), "-r", "linux-x64",
                     "--disable-build-servers", "-m:1", "-nodeReuse:false"] + properties, 1200)
        evaluated = parse_sdk(commands.run("target-properties", ["dotnet", "msbuild", project,
                     "-p:Configuration=Release", "-p:RuntimeIdentifier=linux-x64",
                     "-p:UseArtifactsOutput=true", "-p:ArtifactsPath=" + str(artifacts),
                     "-nodeReuse:false", "-getProperty:TargetPath,TargetDir,TargetFramework,RuntimeIdentifier,UseAppHost,AvaloniaBuildTasksLocation"] + properties, 30))
        write_json(diagnostics / "target-properties.json", evaluated)
        target = Path(evaluated["TargetPath"]).resolve()
        apphost = target.with_suffix("")
        if evaluated["TargetFramework"] != "net10.0" or evaluated["RuntimeIdentifier"] != "linux-x64" or evaluated["UseAppHost"].lower() != "true":
            raise RuntimeError("Actual framework/RID/apphost disagrees with required native execution")
        if not target.is_relative_to(artifacts) or not target.is_file() or not apphost.is_file() or not os.access(apphost, os.X_OK):
            raise RuntimeError("Real isolated entry DLL/apphost missing")
        inventory = {"peCount": 0, "portablePdbCount": 0, "pdbCount": 0,
                     "entryDLL": {"path": str(target.relative_to(artifacts)), "sha256": digest(target)},
                     "apphost": {"path": str(apphost.relative_to(artifacts)), "sha256": digest(apphost)}}
        for path in artifacts.rglob("*"):
            if path.is_file() and path.suffix in (".dll", ".pdb"):
                with path.open("rb") as stream:
                    magic = stream.read(4)
                if path.suffix == ".dll" and magic[:2] == b"MZ":
                    inventory["peCount"] += 1
                elif path.suffix == ".pdb":
                    inventory["pdbCount"] += 1
                    inventory["portablePdbCount"] += int(magic == b"BSJB")
        if args.suite != "bindings15":
            if not task.is_file():
                raise RuntimeError("Normal owner Avalonia task output absent")
            inventory["ownerBuildTask"] = {"sha256": digest(task), "bytes": task.stat().st_size}
        write_json(diagnostics / "pe-pdb-counts.json", inventory)
        report = output / "native5-result.json"
        data = output / "fixture-data"
        if args.suite == "navigation5":
            native_args = [str(report)]
        elif args.suite == "files17":
            native_args = [str(data / "files")]
        elif args.suite == "sites18":
            native_args = ["suite", str(data / "sites")]
        else:
            native_args = []
        result["nativeStarted"] = True
        text = commands.run("native", [str(apphost)] + native_args, 120)
        validate_result(args.suite, expected, text, report)
        if args.suite == "sites18":
            result["sitesChildren"] = sites_children(data, diagnostics)
        result.update(status="PASS", executed=expected, passed=expected, failed=0)
    except Exception as error:
        result.update(status="FAIL", error=repr(error))
    finally:
        try:
            commands.run("git-clean-after", ["git", "diff", "--exit-code", "HEAD", "--"], 15)
        except Exception as error:
            result.update(status="FAIL", sourceCustodyError=repr(error))
        write_json(diagnostics / "result.json", result)
    print(json.dumps(result, indent=2))
    return 0 if result["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
