#!/usr/bin/env python3
"""Install an immutable development candidate, or restore the previous Dulche release."""

import argparse
import fcntl
import hashlib
import http.client
import time
from dulchectl import LocalHTTP
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def manager(*args):
    subprocess.run(["systemctl", "--user", *args], check=True)


def exchange(path, target):
    link = path.with_name(path.name + ".next")
    link.unlink(missing_ok=True)
    link.symlink_to(target)
    os.replace(link, path)


def api(method, route):
    runtime = Path(os.environ.get("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}"))
    conn = LocalHTTP(str(runtime / "9to1-dulche/api.sock"), 10)
    try:
        conn.request(
            method,
            route,
            "{}" if method == "POST" else None,
            {"Content-Type": "application/json"},
        )
        reply = conn.getresponse()
        return reply.status, json.load(reply)
    finally:
        conn.close()


def verify_serving():
    end = time.monotonic() + 30
    last = "no response"
    while time.monotonic() < end:
        try:
            code, info = api("GET", "/v1/status")
            if (
                code == 200
                and info.get("version") == 1
                and info.get("phase") == "ready"
            ):
                return
            last = str(info.get("phase", code))
        except (OSError, http.client.HTTPException, ValueError) as exc:
            last = str(exc)
        time.sleep(0.25)
    raise RuntimeError("Updated service did not pass its readiness check: " + last)


def main():
    a = argparse.ArgumentParser(description=__doc__)
    a.add_argument("--rollback", action="store_true")
    a.add_argument(
        "--activate",
        action="store_true",
        help="Start the local development socket in the current user session",
    )
    args = a.parse_args()
    if os.geteuid() == 0:
        raise SystemExit("Install as the desktop user")
    src = Path(__file__).resolve().parent
    base = Path.home() / ".local/share/9to1/dulche-service"
    units = Path.home() / ".config/systemd/user"
    base.mkdir(mode=0o700, parents=True, exist_ok=True)
    units.mkdir(mode=0o700, parents=True, exist_ok=True)
    releases = base / "releases"
    releases.mkdir(mode=0o700, exist_ok=True)
    with (base / "install.lock").open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        current = base / "current"
        previous = base / "previous"
        old = current.resolve(strict=True) if current.is_symlink() else None
        if args.rollback:
            revision = previous.resolve(strict=True)
            if revision.parent != releases:
                raise SystemExit("Unknown rollback target")
            manifest = json.loads((revision / "manifest.json").read_text())
            tag = hashlib.sha256(
                json.dumps(manifest, sort_keys=True).encode()
            ).hexdigest()
            if revision.name != tag:
                raise SystemExit(
                    "Rollback manifest digest differs from the installed release"
                )
            for name, digest in manifest.items():
                target = revision / name
                if (
                    not target.resolve().is_relative_to(revision)
                    or target.is_symlink()
                    or hashlib.sha256(target.read_bytes()).hexdigest() != digest
                ):
                    raise SystemExit("Rollback content differs from reviewed release")
        else:
            files = sorted(
                [
                    p
                    for p in src.iterdir()
                    if p.is_file()
                    and p.suffix in (".py", ".service", ".socket", ".json")
                    and not p.name.startswith("test_")
                ]
            )
            sources = {p.name: p for p in files}
            donor = src.parents[2] / "9to1 Workspace/Home/Source/Dulche"
            for relative in (
                "bundle/formats.py",
                "bundle/identity.py",
                "llamacpp/gguf.py",
            ):
                sources[relative] = donor / relative
            training = src.parents[2] / "9to1 Models/Dulche Alpha/Training/custody"
            for name in ("__init__.py", "artifacts.py", "provenance.py"):
                sources["custody/" + name] = training / name
            manifest = {
                name: hashlib.sha256(p.read_bytes()).hexdigest()
                for name, p in sources.items()
            }
            tag = hashlib.sha256(
                json.dumps(manifest, sort_keys=True).encode()
            ).hexdigest()
            revision = releases / tag
            if not revision.exists():
                stage = Path(tempfile.mkdtemp(dir=releases))
                try:
                    for name, p in sources.items():
                        (stage / name).parent.mkdir(parents=True, exist_ok=True)
                        if p.suffix == ".py":
                            compile(p.read_bytes(), str(p), "exec")
                        shutil.copyfile(p, stage / name)
                        (stage / name).chmod(0o400)
                    (stage / "manifest.json").write_text(json.dumps(manifest, indent=2))
                    # Exercise the frozen import graph from a separate process before switching the link.
                    subprocess.run(
                        ["/usr/bin/python3", "-B", "-c", "import daemon"],
                        cwd=stage,
                        check=True,
                    )
                    for item in stage.rglob("*"):
                        if item.is_dir():
                            item.chmod(0o500)
                        elif item.name == "manifest.json":
                            item.chmod(0o400)
                    stage.chmod(0o500)
                    stage.rename(revision)
                finally:
                    if stage.exists():
                        shutil.rmtree(stage)
            for name, digest in manifest.items():
                if hashlib.sha256((revision / name).read_bytes()).hexdigest() != digest:
                    raise SystemExit("Release hash mismatch")
        for name in (
            "dulche-backend.service",
            "dulche-backend.socket",
            "dulche-openai.service",
            "dulche-openai.socket",
        ):
            dest = units / name
            if dest.exists() and (
                not dest.is_symlink() or os.readlink(dest) != str(current / name)
            ):
                raise SystemExit(f"Existing unrelated unit: {dest}")
        active = (
            subprocess.run(
                [
                    "systemctl",
                    "--user",
                    "is-active",
                    "--quiet",
                    "dulche-backend.service",
                ]
            ).returncode
            == 0
        )
        if active and not args.activate and old != revision:
            raise SystemExit(
                "Candidate prepared; select --activate to replace the running service"
            )
        if active and old != revision:
            code, info = api("POST", "/v1/service/prepare-upgrade")
            if code == 404:
                # Migration from the original v1 release which has no drain endpoint.
                code, info = api("GET", "/v1/status")
                if (
                    code != 200
                    or any(j["state"] in ("queued", "running") for j in info["agents"])
                    or any(
                        m["state"] in ("loading", "suspending", "resuming", "unloading")
                        for m in info["models"]
                    )
                ):
                    raise SystemExit(
                        "Wait for existing work to finish before upgrading this release"
                    )
            elif code != 200:
                raise SystemExit("Update deferred: " + str(info))
        try:
            exchange(current, revision)
            for name in (
                "dulche-backend.service",
                "dulche-backend.socket",
                "dulche-openai.service",
                "dulche-openai.socket",
            ):
                exchange(units / name, current / name)
            manager("daemon-reload")
            if args.activate:
                manager(
                    "reset-failed", "dulche-backend.service", "dulche-backend.socket"
                )
                manager("start", "dulche-backend.socket")
                if old != revision:
                    manager("restart", "dulche-backend.service")
                verify_serving()
        except Exception:
            if old and old != revision:
                exchange(current, old)
                manager("daemon-reload")
                if args.activate:
                    manager(
                        "reset-failed",
                        "dulche-backend.service",
                        "dulche-backend.socket",
                    )
                    manager("start", "dulche-backend.socket")
                    manager("restart", "dulche-backend.service")
                    verify_serving()
            raise
        if old and old != revision:
            exchange(previous, old)
        print(revision)


if __name__ == "__main__":
    main()
