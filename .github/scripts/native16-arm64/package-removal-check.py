#!/usr/bin/env python3
"""Remove only this candidate package on its disposable native CI runner.

Never used on the owner laptop. Refuses an unrelated installed source or active
candidate process, and verifies that existing test-profile data remains unchanged.
"""
from pathlib import Path
import datetime
import hashlib
import json
import os
import platform
import subprocess


def profile_snapshot(temporary, evidence=Path("evidence")):
    # Consume the actual owned contexts recorded by the journeys. Duplicating
    # their directory-name constants here would silently omit renamed profiles.
    roots, files = set(), []
    for name in ("native-startup-receipt.json", "package-ui-journey-receipt.json", "package-browser-journey-receipt.json"):
        path = evidence / name
        if not path.is_file():
            continue
        receipt = json.loads(path.read_text())
        events = receipt.get("events", []) + receipt.get("stages", [])
        for event in events:
            if event.get("kind") == "ISOLATED_XDG_CONTEXT":
                root = Path(event["profile"])
                if not root.is_absolute() or ".." in root.parts or not root.is_relative_to(temporary.absolute()):
                    raise ValueError("Recorded test profile is outside the disposable runner context")
                roots.add(root)
    for root in sorted(roots):
        if root.is_symlink() or any(parent.is_symlink() for parent in root.parents):
            raise ValueError("Profile evidence path must not follow symlinks")
        # Include journals/WALs and other persisted records, not just the main
        # database. Uninstall must not silently discard the only recent writes.
        for path in sorted(root.rglob("*")):
            if path.is_symlink():
                raise ValueError("Profile evidence path must not follow symlinks")
            if path.is_dir():
                continue
            if not path.is_file():
                raise ValueError("Unsupported special file in persisted test profile")
            with path.open("rb") as stream:
                sha = hashlib.file_digest(stream, "sha256").hexdigest()
            files.append({"path": str(path), "bytes": path.stat().st_size, "sha256": sha})
    if not files:
        raise ValueError("No real persisted test-profile data available for removal check")
    return files


def unchanged(snapshot):
    for record in snapshot:
        path = Path(record["path"])
        if path.is_symlink() or not path.is_file() or path.stat().st_size != record["bytes"]:
            raise ValueError("Removal altered or deleted persisted profile data")
        with path.open("rb") as stream:
            if hashlib.file_digest(stream, "sha256").hexdigest() != record["sha256"]:
                raise ValueError("Removal altered or deleted persisted profile data")


def active_candidate_processes():
    found = []
    targets = {b"/opt/9to1-preview/Haven", b"/usr/lib/haven/llama.cpp/llama-server"}
    for item in Path("/proc").iterdir():
        if not item.name.isdecimal():
            continue
        try:
            argv = (item / "cmdline").read_bytes().split(b"\0")
            if argv and argv[0] in targets:
                found.append(int(item.name))
        except (FileNotFoundError, ProcessLookupError, PermissionError):
            pass
    return found


def run_removal(evidence, command=None, timeout=60):
    # Redirect before starting the command, so partial output survives timeout,
    # decode errors, interruption and ordinary nonzero exits byte-for-byte.
    evidence.mkdir(parents=True, exist_ok=True)
    command = command or ["sudo", "dpkg", "--remove", "9to1-preview"]
    with (evidence / "package-removal.stdout.log").open("wb") as output, \
            (evidence / "package-removal.stderr.log").open("wb") as errors:
        return subprocess.run(command, stdout=output, stderr=errors, timeout=timeout)


def main():
    output = Path("evidence/package-removal-receipt.json")
    receipt = {"schema": "9to1.arm-package-removal/v1", "status": "FAIL",
               "source_head": os.environ.get("GITHUB_SHA"),
               "acceptance_tools_head": os.environ.get("GITHUB_SHA"),
               "owner_laptop": "NOT RUN", "release_ready": False}
    try:
        if os.environ.get("GITHUB_ACTIONS") != "true" or os.environ.get("GITHUB_REPOSITORY") != "CroakyJake12/9to1" \
                or platform.machine() != "aarch64" or os.geteuid() == 0:
            raise ValueError("Only the disposable native same-repository CI runner is supported")
        installed = json.loads(Path("/usr/share/doc/9to1-preview/source-and-build.json").read_text())
        receipt["installed_build_provenance"] = installed
        if installed.get("source_head") != os.environ["GITHUB_SHA"]:
            raise ValueError("Installed package is from a different source revision")
        if active_candidate_processes():
            raise RuntimeError("Candidate processes are still active; no removal or process interruption attempted")
        snapshot = profile_snapshot(Path(os.environ["RUNNER_TEMP"]))
        receipt["persisted_profiles_before"] = snapshot
        result = run_removal(Path("evidence"))
        receipt["removal_exit_code"] = result.returncode
        if result.returncode:
            raise RuntimeError("Package removal failed")
        query = subprocess.run(["dpkg-query", "-W", "-f=${db:Status-Status}", "9to1-preview"], capture_output=True, text=True, timeout=10)
        receipt["dpkg_status_after"] = query.stdout.strip()
        if query.stdout.strip() == "installed" or any(Path(p).exists() for p in ["/opt/9to1-preview/Haven", "/usr/bin/9to1-preview"]):
            raise RuntimeError("Installed package runtime or registration remains after removal")
        unchanged(snapshot)
        receipt["persisted_profiles_unchanged"] = True
        receipt["status"] = "PASS_PACKAGE_REMOVAL_AND_PROFILE_PRESERVATION"
    except Exception as error:
        receipt["error"] = repr(error)
    receipt["at"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(receipt, indent=2) + "\n")
    return 0 if receipt["status"] == "PASS_PACKAGE_REMOVAL_AND_PROFILE_PRESERVATION" else 1


if __name__ == "__main__":
    raise SystemExit(main())
