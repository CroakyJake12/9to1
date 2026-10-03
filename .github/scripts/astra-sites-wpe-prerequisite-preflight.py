"""Hosted Noble package/file observations only. No library or UI is loaded.

The exact inherited session helper observes/drains every original metadata and
apt child. Its legacy expectedManagedLaunch field is a drain handshake here;
this job builds/runs no managed application and grants no native acceptance.
"""
import argparse, hashlib, json, os, pathlib, re, signal, stat, subprocess, sys, time

sys.dont_write_bytecode = True
PACKAGES = (
    "libwpewebkit-2.0-dev", "libwpewebkit-2.0-1",
    "libwpebackend-fdo-1.0-dev", "libwpebackend-fdo-1.0-1",
    "libwpe-1.0-dev", "libwpe-1.0-1",
)
LINKS = ("libWPEWebKit-2.0.so", "libWPEBackend-fdo-1.0.so", "libwpe-1.0.so")
MAX_TOTAL = 2 * 1024 * 1024
MAX_LOG = 256 * 1024
EXEC_GUARD = "import os,sys; fd=int(sys.argv[1]); token=os.read(fd,1); os.close(fd); assert token==b'G'; os.execv(sys.argv[2],sys.argv[2:])"


def digest(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""): h.update(block)
    return h.hexdigest()


def add(errors, error):
    if not any(error is prior for prior in errors): errors.append(error)


def collect(errors, operation):
    try: return operation()
    except BaseException as error: add(errors, error); return None


def fail(primary, cleanup):
    errors = []
    for error in ([primary] if primary is not None else []) + cleanup: add(errors, error)
    if len(errors) > 1: raise BaseExceptionGroup("Prerequisite original and cleanup failures", errors)
    if errors: raise errors[0]


def run(root, out, normal, helper_sha, self_sha):
    if sys.platform != "linux" or os.geteuid() == 0: raise RuntimeError("Ordinary Linux runner required")
    if os.environ.get("GITHUB_REF") != "refs/heads/validation/astra-sites-wpe-prerequisites01":
        raise RuntimeError("Exact isolated prerequisite branch required")
    if out.exists() or out.is_symlink(): raise RuntimeError("Fresh output required")
    out.mkdir(mode=0o700, parents=False)
    helper = root / ".github/scripts/astra_original_native_session_drain.py"
    script = pathlib.Path(__file__).resolve()
    if helper.is_symlink() or script.is_symlink() or digest(helper) != helper_sha or digest(script) != self_sha:
        raise RuntimeError("Exact source helper/script required")
    guard = {}; exec(compile(helper.read_bytes(), str(helper), "exec"), guard)
    OriginalSession = guard["OriginalSession"]
    if not callable(getattr(os, "pidfd_open", None)) or not callable(getattr(signal, "pidfd_send_signal", None)):
        raise RuntimeError("Original creator pidfd required")
    observations = {"classification": "PACKAGE_AND_LINKER_FILE_PREREQUISITES_ONLY", "commands": [],
                    "nativeLibraryLoaded": False, "managedProductBuiltOrRun": False,
                    "sitesSuitesRequired": True, "nativeDomAndProcessCustodyRequired": True}

    def budget():
        size = 0
        for path in out.rglob("*"):
            info = path.lstat()
            if stat.S_ISLNK(info.st_mode): raise RuntimeError("Output symlink refused")
            if stat.S_ISREG(info.st_mode): size += info.st_size
        if size > MAX_TOTAL: raise RuntimeError("Prerequisite output budget exceeded")
        return size

    def save(path, body):
        data = (json.dumps(body, sort_keys=True, indent=2) + "\n").encode()
        if path.is_symlink(): raise RuntimeError("Receipt symlink refused")
        old = path.stat().st_size if path.exists() else 0
        if budget() - old + len(data) > MAX_TOTAL: raise RuntimeError("Receipt budget refused")
        with path.open("wb") as handle:
            handle.write(data); handle.flush(); os.fsync(handle.fileno())

    def protect():
        if digest(helper) != helper_sha or digest(script) != self_sha: raise RuntimeError("Prerequisite source changed")

    def command(name, argv, deadline=60):
        protect(); budget()
        records = out / (name + "-sessions"); records.mkdir(mode=0o700)
        save(records / "expected-managed-launch.json", {"expectedManagedLaunch": True, "drained": False,
             "classification": "legacy generic drain handshake; prerequisite process only"})
        log = out / (name + ".log")
        process = session = output = creator = read_fd = write_fd = None
        primary = None; cleanup = []; started = time.monotonic()
        try:
            output = log.open("xb")
            read_fd, write_fd = os.pipe()
            process = subprocess.Popen([sys.executable, "-I", "-c", EXEC_GUARD, str(read_fd), *argv],
                cwd=root, env={**os.environ, "LC_ALL": "C", "DEBIAN_FRONTEND": "noninteractive"},
                stdout=output, stderr=subprocess.STDOUT, start_new_session=True, pass_fds=(read_fd,))
            creator = os.pidfd_open(process.pid, 0)
            session = OriginalSession(process, records)
            os.close(read_fd); read_fd = None
            if os.write(write_fd, b"G") != 1: raise RuntimeError("Original exec handshake refused")
            os.close(write_fd); write_fd = None
            expires = time.monotonic() + deadline
            while process.poll() is None:
                session.observe(); budget()
                if log.stat().st_size > MAX_LOG: raise RuntimeError("Bounded prerequisite log exceeded")
                if time.monotonic() >= expires: raise TimeoutError("Original prerequisite deadline exceeded")
                time.sleep(.04)
            if process.wait() != 0: raise RuntimeError("Original prerequisite command failed: " + name)
        except BaseException as error: primary = error
        finally:
            # Body failure is captured before independent original close/drains.
            if output is not None: collect(cleanup, output.close)
            for fd in (read_fd, write_fd):
                if fd is not None: collect(cleanup, lambda fd=fd: os.close(fd))
            if session is not None: collect(cleanup, session.drain)
            elif process is not None: add(cleanup, RuntimeError("Original session identity unavailable; no true seal"))
            if creator is not None:
                def kill_original():
                    try: signal.pidfd_send_signal(creator, signal.SIGKILL)
                    except ProcessLookupError: pass
                collect(cleanup, kill_original)
            elif process is not None: collect(cleanup, process.kill)
            if process is not None: collect(cleanup, lambda: process.wait(timeout=10))
            if creator is not None: collect(cleanup, lambda: os.close(creator))
            row = {"name": name, "argv": argv, "exitCode": None if process is None else process.returncode,
                   "creatorPidfdCaptured": creator is not None, "originalSessionCaptured": session is not None,
                   "seconds": time.monotonic() - started, "primaryType": None if primary is None else type(primary).__name__}
            row["logBytes"] = collect(cleanup, lambda: log.stat().st_size)
            row["logSha256"] = collect(cleanup, lambda: digest(log))
            row["drains"] = []
            for path in collect(cleanup, lambda: sorted(records.glob("*.json"))) or []:
                body = collect(cleanup, lambda path=path: json.loads(path.read_text()))
                if body is not None: row["drains"].append({"path": str(path.relative_to(out)), "body": body})
            row["cleanupTypes"] = [type(error).__name__ for error in cleanup]
            observations["commands"].append(row)
            collect(cleanup, lambda: save(out / "commands.json", observations["commands"]))
            collect(cleanup, protect); collect(cleanup, budget)
        fail(primary, cleanup)
        if log.stat().st_size > MAX_LOG: raise RuntimeError("Final prerequisite log exceeded")
        return log.read_text()

    primary = None; cleanup = []
    try:
        # No shell evaluation of release data or metadata values.
        release_file = pathlib.Path("/etc/os-release")
        release = release_file.read_text(); observations["osRelease"] = release
        fields = dict(line.split("=", 1) for line in release.splitlines() if "=" in line)
        if fields.get("ID", "").strip('"') != "ubuntu" or fields.get("VERSION_ID", "").strip('"') != "24.04" or fields.get("VERSION_CODENAME", "").strip('"') != "noble":
            raise RuntimeError("Actual selected Noble release required")
        if command("architecture", ["/usr/bin/dpkg", "--print-architecture"]).strip() != "amd64":
            raise RuntimeError("Actual selected amd64 required")
        if command("head", ["/usr/bin/git", "rev-parse", "HEAD"]).strip() != normal:
            raise RuntimeError("Actual exact prerequisite source head required")
        command("apt-update", ["/usr/bin/sudo", "-n", "/usr/bin/apt-get", "-o", "Acquire::AllowInsecureRepositories=false", "-o", "APT::Get::AllowUnauthenticated=false", "update"], 300)
        candidates = {}; policies = {}
        for package in PACKAGES:
            policy = command("policy-" + package, ["/usr/bin/apt-cache", "policy", package]); policies[package] = policy
            found = re.findall(r"^\s*Candidate:\s*(\S+)\s*$", policy, re.M)
            if len(found) != 1 or not re.fullmatch(r"[A-Za-z0-9.+:~_-]+", found[0]) or found[0] == "(none)":
                raise RuntimeError("Selected distro package candidate unavailable: " + package)
            version = found[0]; candidates[package] = version
            # Require an actual selected-version official Noble origin. Unknown
            # mirrors/origins refuse; no guessed upstream version is substituted.
            lines = policy.splitlines(); origins = []; in_version = False
            for line in lines:
                match = re.match(r"\s*(?:\*\*\*\s+)?(\S+)\s+\d+\s*$", line)
                if match: in_version = match[1] == version; continue
                if in_version and "http" in line:
                    origin = re.search(r"https?://([^/\s]+)\S*\s+(noble(?:-updates|-security|-backports)?)/", line)
                    if origin is None or not (origin[1] == "ubuntu.com" or origin[1].endswith(".ubuntu.com")):
                        raise RuntimeError("Selected candidate origin not verified as official Noble")
                    origins.append(line.strip())
            if not origins: raise RuntimeError("Selected candidate has no observed official Noble origin")
        observations["candidateVersions"] = candidates; observations["aptPolicies"] = policies
        save(out / "candidate-provenance.json", {"versions": candidates, "policies": policies})
        command("install-selected-dev-roots", ["/usr/bin/sudo", "-n", "/usr/bin/apt-get", "-o", "Acquire::AllowInsecureRepositories=false", "-o", "APT::Get::AllowUnauthenticated=false", "install", "--yes", "--no-install-recommends", "--", *[p + "=" + candidates[p] for p in PACKAGES if p.endswith("-dev")]], 600)
        installed = {}; ownership = {}
        for package in PACKAGES:
            actual = command("installed-" + package, ["/usr/bin/dpkg-query", "-W", "-f=${Package}\t${Architecture}\t${Version}\t${Status}\n", package]).strip().split("\t")
            if actual != [package, "amd64", candidates[package], "install ok installed"]:
                raise RuntimeError("Installed candidate identity mismatch: " + package)
            listing = command("files-" + package, ["/usr/bin/dpkg-query", "-L", package]).splitlines()
            if not listing or len(listing) != len(set(listing)) or any(not path.startswith("/") for path in listing):
                raise RuntimeError("Complete package file list refused")
            installed[package] = {"version": actual[2], "architecture": actual[1], "allFiles": listing}
            for path in listing: ownership.setdefault(path, []).append(package)
        observations["installedPackages"] = installed

        def regular_elf(path):
            info = path.lstat()
            if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022:
                raise RuntimeError("Ordinary immutable package ELF required")
            with path.open("rb") as handle: header = handle.read(64)
            if len(header) != 64 or header[:6] != b"\x7fELF\x02\x01" or int.from_bytes(header[18:20], "little") != 62:
                raise RuntimeError("Actual x86_64 little-endian ELF required")
            return {"path": str(path), "bytes": info.st_size, "sha256": digest(path), "mode": oct(stat.S_IMODE(info.st_mode)),
                    "device": info.st_dev, "inode": info.st_ino, "packageOwners": ownership.get(str(path), [])}

        libraries = []
        for name in LINKS:
            matches = [path for path in ownership if pathlib.PurePosixPath(path).name == name]
            if matches != ["/usr/lib/x86_64-linux-gnu/" + name]: raise RuntimeError("Exact distro unversioned linker path required")
            path = pathlib.Path(matches[0]); seen = set(); chain = []
            while path.is_symlink():
                if str(path) in seen or len(seen) >= 8 or not ownership.get(str(path)): raise RuntimeError("Package link ownership/cycle refused")
                seen.add(str(path)); target = os.readlink(path)
                chain.append({"path": str(path), "target": target, "packageOwners": ownership[str(path)]})
                path = pathlib.Path(os.path.normpath(str(path.parent / target)))
                if path.parent != pathlib.Path("/usr/lib/x86_64-linux-gnu"): raise RuntimeError("Package linker leaves selected library directory")
            if not ownership.get(str(path)) or not any(candidate.endswith(name + ".1") for candidate in [item["path"] for item in chain] + [str(path)]):
                raise RuntimeError("Exact versioned PInvoke path/owner absent")
            libraries.append({"availabilityName": name, "chain": chain, "actualElf": regular_elf(path)})
        observations["libraryFiles"] = libraries
        observations["packagedEngineExecutables"] = [regular_elf(pathlib.Path(path)) for path in ownership
            if pathlib.PurePosixPath(path).name in {"WPEWebProcess", "WPENetworkProcess", "WPEGPUProcess"}]
        observations["qualification"] = "Package metadata and owned ELF/linker bytes only. No PInvoke/export/load, DOM, engine launch, sandbox, escaped-descendant, profile or product authority is proven. Full Sites suites and actual original consumer/tasks/process drains remain required."
    except BaseException as error: primary = error
    finally:
        collect(cleanup, protect)
        observations["outputBytesBeforeReceipt"] = collect(cleanup, budget)
        for attempt in range(2):
            observations["status"] = "PREREQUISITE_FILE_OBSERVATIONS_PASS" if primary is None and not cleanup else "PREREQUISITE_REFUSED_ORIGINAL_FAILURES_RETAINED"
            observations["primaryType"] = None if primary is None else type(primary).__name__
            observations["cleanupTypes"] = [type(error).__name__ for error in cleanup]
            previous = len(cleanup); collect(cleanup, lambda: save(out / "receipt.json", observations))
            if len(cleanup) == previous: break
        if primary is not None or cleanup:
            collect(cleanup, lambda: print(json.dumps({"status": "PREREQUISITE_REFUSAL", "primaryType": observations["primaryType"], "cleanupTypes": [type(e).__name__ for e in cleanup]}), file=sys.stderr))
    fail(primary, cleanup)
    print(json.dumps({"status": observations["status"], "receipt": str(out / "receipt.json"), "nativeAcceptance": False}))


if __name__ == "__main__":
    args = argparse.ArgumentParser()
    for name in ("root", "out", "normal", "helper-sha", "self-sha"): args.add_argument("--" + name, required=True)
    parsed = args.parse_args()
    run(pathlib.Path(parsed.root).resolve(), pathlib.Path(parsed.out), parsed.normal, parsed.helper_sha, parsed.self_sha)
