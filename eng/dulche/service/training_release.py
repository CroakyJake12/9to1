#!/usr/bin/python3
"""Freeze the training supervisor and cycle CLI without restarting inference or changing an active job."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile


def prepare(source, destination):
    modules = (
        "training_jobs.py",
        "training_cycle.py",
        "training_budget.py",
        "training_worker_exec.py",
        "training_worker_client.py",
        "dulchectl.py",
        "engine_profiles.py",
    )
    custody = source / "custody"
    if not custody.is_dir():
        custody = source.parents[2] / "9to1 Models/Dulche Alpha/Training/custody"
    files = {name: source / name for name in modules}
    files.update(
        {
            "custody/" + name: custody / name
            for name in ("__init__.py", "artifacts.py", "provenance.py")
        }
    )
    blobs = {rel: p.read_bytes() for rel, p in files.items()}
    digests = {rel: hashlib.sha256(data).hexdigest() for rel, data in blobs.items()}
    raw = json.dumps(digests, sort_keys=True, indent=2).encode()
    name = hashlib.sha256(raw).hexdigest()
    destination.mkdir(mode=0o700, parents=True, exist_ok=True)
    final = destination / name
    if not final.exists():
        staging = Path(tempfile.mkdtemp(prefix=".stage-", dir=destination))
        for rel, data in blobs.items():
            compile(data, rel, "exec")
            path = staging / rel
            path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
            path.write_bytes(data)
            path.chmod(0o400)
        (staging / "manifest.json").write_bytes(raw)
        (staging / "manifest.json").chmod(0o400)
        subprocess.run(
            [
                sys.executable,
                "-B",
                "-c",
                "import training_cycle, training_jobs; training_jobs.custody_path(); import custody.artifacts, custody.provenance",
            ],
            cwd=staging,
            check=True,
        )
        (staging / "custody").chmod(0o500)
        staging.chmod(0o500)
        try:
            staging.rename(final)
        except OSError:
            if not final.exists():
                raise
            staging.chmod(0o700)
            (staging / "custody").chmod(0o700)
            shutil.rmtree(staging)
    for rel, expected in digests.items():
        p = final / rel
        if (
            p.is_symlink()
            or hashlib.sha256(p.read_bytes()).hexdigest() != expected
            or p.stat().st_mode & 0o222
        ):
            raise RuntimeError("Training release failed verification: " + rel)
    if (final / "manifest.json").read_bytes() != raw:
        raise RuntimeError("Training release manifest mismatch")
    return {
        "release": name,
        "path": str(final),
        "supervisor": str(final / "training_jobs.py"),
        "cycle": str(final / "training_cycle.py"),
        "restarted_services": [],
    }


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument(
        "--destination",
        type=Path,
        default=Path(os.environ.get("XDG_DATA_HOME", Path.home() / ".local/share"))
        / "9to1/dulche-training/releases",
    )
    args = ap.parse_args()
    print(
        json.dumps(prepare(Path(__file__).resolve().parent, args.destination), indent=2)
    )
