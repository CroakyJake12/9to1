#!/usr/bin/env python3
"""Check the real disposable ARM runner before acquisition/build/native testing."""
from pathlib import Path
import argparse
import datetime
import hashlib
import json
import os
import platform
import shutil
import subprocess

COMMANDS = ("dotnet", "dpkg", "dpkg-deb", "readelf", "file", "ldd", "gpgv",
            "curl", "git", "wmctrl", "xdotool", "xprop", "xwininfo", "import",
            "tesseract", "dbus-run-session", "xvfb-run", "openbox")


def run(arguments):
    result = subprocess.run(arguments, capture_output=True, text=True, timeout=20)
    if result.returncode:
        raise RuntimeError("Command failed: " + arguments[0])
    return result.stdout.strip()


def preflight(root, temporary, expected_head, expected_sdk, which=shutil.which, execute=run, machine=platform.machine):
    checks, failures = [], []
    def check(name, operation):
        try:
            value = operation()
            checks.append({"check": name, "status": "PASS", "observed": value})
        except Exception as error:
            failures.append({"check": name, "kind": "INFRASTRUCTURE", "error": str(error)})
    def equal(name, actual, expected):
        if actual != expected:
            raise ValueError(name + " mismatch: " + repr(actual) + "; expected " + repr(expected))
        return actual
    check("native architecture", lambda: equal("machine", machine(), "aarch64"))
    check("Debian architecture", lambda: equal("dpkg architecture", execute(["dpkg", "--print-architecture"]), "arm64"))
    check("pinned SDK", lambda: equal("SDK", execute(["dotnet", "--version"]), expected_sdk))
    check("immutable source", lambda: equal("source", execute(["git", "-C", str(root), "rev-parse", "HEAD"]), expected_head))
    def commands():
        missing = [name for name in COMMANDS if which(name) is None]
        if missing:
            raise ValueError("Missing required native tools: " + ", ".join(missing))
        return list(COMMANDS)
    check("native tools", commands)
    check("production accessibility observer and workflow-test interpreter", lambda: execute([
        "dbus-run-session", "--", "xvfb-run", "-a", "/usr/bin/python3", "-c",
        "import pyatspi, yaml; print('pyatspi and yaml importable')"]))
    def capacity():
        available = shutil.disk_usage(temporary).free
        if available < 6 * 1024**3:
            raise ValueError("Need at least6GiB free for pinned dependencies, normal build and retained diagnostics")
        return {"free_bytes": available, "minimum_bytes": 6 * 1024**3}
    check("output capacity", capacity)
    def identity():
        manifest = json.loads((root / ".github/scripts/native16-arm64/native16-source-identity.json").read_text())
        for name, expected in manifest["changed_files_sha256"].items():
            path = root / name
            if path.is_symlink() or not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != expected:
                raise ValueError("Frozen input identity mismatch: " + name)
        return {"frozen_inputs": len(manifest["changed_files_sha256"])}
    check("frozen source inputs", identity)
    return {"schema": "9to1.arm-pipeline-preflight/v1",
            "at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
            "source_head": expected_head, "checks": checks, "failures": failures,
            "status": "BLOCKED" if failures else "CHECKPOINT",
            "native_application_acceptance": "NOT RUN", "release_ready": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path("evidence/pipeline-preflight.json"))
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[3]
    receipt = preflight(root, Path(os.environ["RUNNER_TEMP"]), os.environ["GITHUB_SHA"], os.environ["NATIVE_DOTNET_VERSION"])
    packages = Path("evidence/runner-package-versions.txt")
    fingerprint_inputs = {"sdk": os.environ["NATIVE_DOTNET_VERSION"],
                          "runner_image_os": os.environ.get("ImageOS"),
                          "runner_image_version": os.environ.get("ImageVersion"),
                          "package_versions_sha256": hashlib.sha256(packages.read_bytes()).hexdigest()}
    receipt["package_cache_inputs"] = fingerprint_inputs
    receipt["package_cache_cross_run_reuse"] = all(fingerprint_inputs.values())
    if not receipt["package_cache_cross_run_reuse"]:
        # Unknown image identity does not block product work, but must never
        # permit cross-run artifact reuse on an unverified environment.
        fingerprint_inputs["unknown_image_run_nonce"] = os.environ["GITHUB_RUN_ID"] + "." + os.environ["GITHUB_RUN_ATTEMPT"]
    fingerprint = hashlib.sha256(json.dumps(fingerprint_inputs, sort_keys=True).encode()).hexdigest()
    receipt["package_cache_environment_sha256"] = fingerprint
    with Path(os.environ["GITHUB_ENV"]).open("a") as stream:
        stream.write("PIPELINE_ENVIRONMENT_SHA=" + fingerprint + "\n")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(receipt, indent=2) + "\n")
    for failure in receipt["failures"]:
        print("INFRASTRUCTURE:", failure["check"], failure["error"])
    return 0 if receipt["status"] == "CHECKPOINT" else 78


if __name__ == "__main__":
    raise SystemExit(main())
