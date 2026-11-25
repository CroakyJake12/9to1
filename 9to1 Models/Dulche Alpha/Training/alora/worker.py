"""Independent ALORA worker bridge for the existing checkpoint-yield-v1 control.

The caller supplies an admitted model/session, dataset, publisher and actual
TrainingControl. This module does not load models, select devices, launch a
service or register a workflow. Returned publications remain review candidates.
"""
from __future__ import annotations

import hashlib
import json
import math
import os
from pathlib import Path
import time
from uuid import uuid4

from custody.artifacts import _fsync_directory, _open_regular, checked_path
from .checkpoints import CheckpointPublisher
from .data import TrainingDataset
from .curriculum import CATEGORY_PERCENTAGES, validate_counts
from .session import ComputeInterrupted, TrainingSession


def _admit_curriculum(dataset):
    counts = {category: 0 for category in CATEGORY_PERCENTAGES}
    for row in dataset.rows():
        counts[row["category"]] += 1
    return validate_counts(counts)


def _exclusive(path):
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0)
    return os.fdopen(os.open(path, flags, 0o600), "wb")


def _optimizer_evidence(stream, session, record, prior_updates):
    if (type(record) is not dict or record.get("status") != "completed"
            or type(record.get("updates")) is not int
            or record["updates"] != prior_updates + 1
            or record["updates"] != session.updates or session.cursor != session.updates
            or record.get("parameter_before_sha256") == record.get("parameter_after_sha256")):
        raise ValueError("Optimizer evidence differs from the completed update")
    changes = record.get("changed_parameters")
    if type(changes) is not list or not changes:
        raise ValueError("A completed update needs measured adapter changes")
    names, lines = set(), []
    for change in changes:
        name = change.get("name") if type(change) is dict else None
        value = change.get("max_abs_update") if type(change) is dict else None
        if (type(name) is not str or name not in session.parameters or name in names
                or type(value) not in (int, float) or not math.isfinite(value) or value <= 0):
            raise ValueError("Optimizer measurements must name actual changed adapters")
        names.add(name)
        lines.append(json.dumps({"step": record["updates"], "parameter": name,
                                 "max_abs_update": value}, allow_nan=False) + "\n")
    payload = "".join(lines).encode("utf-8")
    if stream.write(payload) != len(payload):
        raise OSError("Incomplete optimizer-evidence write")
    stream.flush()
    os.fsync(stream.fileno())


def _deliver_checkpoint(session, publisher, publication, control, phase):
    if session.device.type == "cuda":
        import torch
        torch.cuda.synchronize(session.device)
    source = publisher.checkpoint_path(publication)
    expected = publication.checkpoint
    directory = Path(control.output) / "checkpoints"
    directory.mkdir(mode=0o700, exist_ok=True)
    checked_path(directory, directory=True)
    destination = directory / f"alora-{session.updates}-{uuid4().hex}.snapshot"
    digest, count = hashlib.sha256(), 0
    # Exclusive publication preserves all earlier/partial copies on failure.
    with _open_regular(source) as incoming, _exclusive(destination) as outgoing:
        while chunk := incoming.read(1024 * 1024):
            count += len(chunk)
            if count > expected.size_bytes:
                raise ValueError("Checkpoint exceeds its immutable source size")
            digest.update(chunk)
            if outgoing.write(chunk) != len(chunk):
                raise OSError("Incomplete checkpoint copy")
        outgoing.flush()
        os.fsync(outgoing.fileno())
    _fsync_directory(directory)
    if count != expected.size_bytes or digest.hexdigest() != expected.sha256:
        raise ValueError("Delivered checkpoint differs from its immutable source")
    with _open_regular(destination) as incoming:
        if hashlib.file_digest(incoming, "sha256").hexdigest() != expected.sha256:
            raise ValueError("Delivered checkpoint failed readback verification")
    control.publish_checkpoint(destination.name)
    control.finish(phase)
    return {"phase": phase, "updates": session.updates,
            "checkpoint_sha256": expected.sha256, "checkpoint_path": str(destination),
            "publication": publication}


def _failure_marker(output, session, initial_updates, error):
    destination = output / f"worker-failure-{uuid4().hex}.json"
    payload = (json.dumps({
        "schema": 1, "kind": "alora-worker-failure", "phase": "failed",
        "purpose": session.purpose, "initial_updates": initial_updates,
        "observed_updates": session.updates, "cursor": session.cursor,
        "error_type": type(error).__name__, "retry_current_session": False,
    }, allow_nan=False, sort_keys=True) + "\n").encode("utf-8")
    with _exclusive(destination) as stream:
        if stream.write(payload) != len(payload):
            raise OSError("Incomplete worker failure marker")
        stream.flush()
        os.fsync(stream.fileno())
    _fsync_directory(output)


def run_training(session, dataset, publisher, control, *, target_updates: int,
                 checkpoint_exception: type[Exception]):
    """Run to an absolute update target, or publish a cooperative pause.

    Resume starts at cursor % train-sample-count in stable manifest/row order.
    A new supervised attempt needs its own output directory; existing optimizer
    logs are never appended/replaced. The returned publication is suitable for
    publisher.load() before restoring a separate admitted session.
    """
    if (type(session) is not TrainingSession or type(dataset) is not TrainingDataset
            or type(publisher) is not CheckpointPublisher):
        raise ValueError("An admitted ALORA session, dataset and publisher are required")
    if (type(target_updates) is not int or not 1 <= target_updates <= 1_000_000
            or type(session.updates) is not int or session.updates < 0
            or type(session.cursor) is not int or session.cursor != session.updates
            or target_updates <= session.updates or session.failed):
        raise ValueError("Target must advance an unfailed session with a matching cursor")
    if (dataset.purpose != session.purpose or publisher.purpose != session.purpose
            or dataset.manifest_sha256 != session.identity["dataset_manifest_sha256"]):
        raise ValueError("Worker inputs differ from the session's admitted identity")
    if (not isinstance(checkpoint_exception, type)
            or not issubclass(checkpoint_exception, Exception)
            or checkpoint_exception is Exception):
        raise ValueError("Supply the actual control checkpoint exception type")
    output = Path(control.output)
    checked_path(output, directory=True)
    count = sum(len(entry.samples) for entry in dataset.manifest.files if entry.split == "train")
    if count <= 0:
        raise ValueError("No admitted training samples")
    if session.purpose == "real_training":
        _admit_curriculum(dataset)
    initial_updates = session.updates

    def check_permit():
        try:
            deadline = control.wait_for_compute()
        except checkpoint_exception as error:
            raise ComputeInterrupted("Supervisor requested a checkpoint") from error
        if type(deadline) not in (int, float) or not math.isfinite(deadline):
            raise ValueError("Invalid compute-permit deadline")
        if deadline <= time.monotonic():
            raise ComputeInterrupted("Compute permit expired before the next operation")

    try:
        publication, phase = None, "completed"
        with _exclusive(output / "optimizer-evidence.jsonl") as evidence:
            _fsync_directory(output)
            control.ready(completed_steps=initial_updates)
            rows = iter(dataset.rows())
            offset = session.cursor % count
            for _ in range(offset):
                next(rows)
            remaining = count - offset
            while session.updates < target_updates:
                before = session.updates
                try:
                    check_permit()
                    if remaining == 0:
                        rows, remaining = iter(dataset.rows()), count
                    row = next(rows)
                    remaining -= 1
                    record, publication = session.step_row(
                        dataset, row, publisher, before_compute=check_permit)
                except ComputeInterrupted:
                    if session.failed or session.updates != before:
                        raise RuntimeError("An interruption after a failed or committed update cannot pause")
                    phase = "paused"
                    break
                # Storage/control failures after commit must never become a clean pause.
                publisher.checkpoint_path(publication)
                _optimizer_evidence(evidence, session, record, before)
                control.progress(session.updates)
        if phase == "paused" or publication is None:
            publication = session.publish_current(publisher)
        return _deliver_checkpoint(session, publisher, publication, control, phase)
    except BaseException as error:
        # An update or external publication may already have committed. Never retry.
        session.failed = True
        try:
            _failure_marker(output, session, initial_updates, error)
        except BaseException as marker_error:
            raise BaseExceptionGroup("Worker failed and its failure marker could not be retained",
                                     [error, marker_error]) from None
        raise
