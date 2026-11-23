"""Cooperative control contract for a separately authored, approved training worker."""

from __future__ import annotations

import hashlib
import json
import os
import tempfile
import time
from pathlib import Path


class CheckpointRequired(RuntimeError):
    """The independent worker must finish or cancel its current bounded operation, checkpoint and exit."""


class TrainingControl:
    def __init__(self):
        self.job = os.environ["DULCHE_TRAIN_JOB"]
        self.output = Path(os.environ["DULCHE_TRAIN_OUTPUT"]).resolve(strict=True)
        self.control = Path(os.environ["DULCHE_TRAIN_CONTROL"])
        self.resume = os.environ.get("DULCHE_RESUME_CHECKPOINT")
        self.record = {
            "job": self.job,
            "pid": os.getpid(),
            "training_started": False,
            "phase": "preparing",
            "steps": 0,
        }

    def _publish(self):
        dest = self.output / "progress.json"
        fd, tmp = tempfile.mkstemp(dir=self.output, prefix=".progress-")
        try:
            with os.fdopen(fd, "w") as f:
                json.dump(self.record, f)
                f.flush()
                os.fsync(f.fileno())
            os.replace(tmp, dest)
        finally:
            Path(tmp).unlink(missing_ok=True)

    def ready(self, completed_steps=0):
        """Call only when the real registered workflow is prepared to execute."""
        if (
            self.record["training_started"]
            or type(completed_steps) is not int
            or completed_steps < 0
        ):
            raise ValueError("Invalid training-start acknowledgement")
        self.record.update(
            training_started=True, phase="running", steps=completed_steps
        )
        self._publish()

    def wait_for_compute(self):
        """Return an absolute monotonic deadline. The worker controls its own bounded operations."""
        while True:
            with self.control.open() as f:
                cmd = json.load(f)
            if cmd.get("job") != self.job:
                raise CheckpointRequired("Control identity changed")
            if cmd.get("action") in ("checkpoint-and-exit", "stop"):
                raise CheckpointRequired(cmd.get("reason", "stop"))
            until = cmd.get("permit_until_monotonic", 0)
            if cmd.get("action") == "run" and time.monotonic() < until:
                return until
            time.sleep(0.05)

    def progress(self, completed_steps):
        if (
            not self.record["training_started"]
            or type(completed_steps) is not int
            or completed_steps <= self.record["steps"]
        ):
            raise ValueError(
                "Progress must identify an additional completed independent update"
            )
        self.record["steps"] = completed_steps
        self._publish()

    def publish_checkpoint(self, relative):
        """Register a completed immutable worker-authored checkpoint. No model-specific serialization."""
        base = (self.output / "checkpoints").resolve(strict=True)
        p = (base / relative).resolve(strict=True)
        if base not in p.parents or not p.is_file() or not p.stat().st_size:
            raise ValueError("Checkpoint is outside this attempt or empty")
        with p.open("rb") as f:
            checksum = hashlib.file_digest(f, "sha256").hexdigest()
        self.record["checkpoint"] = {
            "path": str(p.relative_to(base)),
            "sha256": checksum,
            "steps": self.record["steps"],
        }
        self._publish()

    def finish(self, phase):
        if phase not in ("paused", "completed"):
            raise ValueError("Unknown terminal phase")
        if (self.record.get("checkpoint") or {}).get("steps") != self.record["steps"]:
            raise ValueError("The terminal step needs its own durable checkpoint")
        self.record["phase"] = phase
        self._publish()
