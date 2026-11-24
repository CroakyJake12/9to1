"""Operator-owned two-rank CUDA smoke, with no model, optimizer or network listener."""

import argparse
import ctypes
import json
import os
import re
import subprocess
import sys
import threading
import time
from uuid import UUID

from .artifacts import CustodyError, canonical_id
from .rank_autograd import AutogradBoundary
from .rank_stream import BoundaryFrame

_GPU = re.compile(r"GPU-[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}")
_CAPABILITY = ((12, 0), (7, 0))
_MARKER = b"DULCHE_DUAL_GPU_SMOKE "
_LOG_LIMIT = 64 * 1024


def _identity(attempt, rank, expected_uuid):
    if (UUID(canonical_id(attempt)).int == 0 or type(rank) is not int or rank not in (0, 1)
            or type(expected_uuid) is not str or _GPU.fullmatch(expected_uuid) is None):
        raise CustodyError("The smoke requires a fresh attempt, rank and exact GPU UUID.")


def _cuda_driver_identity():
    """Read the real UUID and CC of visible ordinal zero without trusting its mask."""
    try:
        driver = ctypes.CDLL("libcuda.so.1")
        uuid_symbol = "cuDeviceGetUuid_v2" if hasattr(driver, "cuDeviceGetUuid_v2") else "cuDeviceGetUuid"
        number = ctypes.c_int
        pnum = ctypes.POINTER(number)
        uuid_type = ctypes.c_ubyte * 16

        def checked(name, types, *args):
            function = getattr(driver, name)
            function.argtypes = types
            function.restype = number
            result = function(*args)
            if result != 0:
                raise CustodyError(f"CUDA driver {name} failed with result {result}.")

        count, device, major, minor = (number() for _ in range(4))
        binary_uuid = uuid_type()
        checked("cuInit", [ctypes.c_uint], 0)
        checked("cuDeviceGetCount", [pnum], ctypes.byref(count))
        if count.value != 1:
            raise CustodyError("The CUDA driver must see exactly one admitted device.")
        checked("cuDeviceGet", [pnum, number], ctypes.byref(device), 0)
        checked(uuid_symbol, [ctypes.POINTER(uuid_type), number], ctypes.byref(binary_uuid), device.value)
        checked("cuDeviceComputeCapability", [pnum, pnum, number],
                ctypes.byref(major), ctypes.byref(minor), device.value)
    except (AttributeError, OSError) as error:
        raise CustodyError("The CUDA driver cannot verify the admitted device UUID.") from error
    return "GPU-" + str(UUID(bytes=bytes(binary_uuid))), (major.value, minor.value)


def _cuda_device(torch, rank, expected_uuid):
    if os.environ.get("CUDA_VISIBLE_DEVICES") != expected_uuid:
        raise CustodyError("CUDA visibility must be exactly the approved GPU UUID.")
    if not torch.cuda.is_available() or torch.cuda.device_count() != 1:
        raise CustodyError("The rank runtime must see exactly one CUDA device.")
    props = torch.cuda.get_device_properties(0)
    identifier = getattr(props, "uuid", None)
    # PyTorch releases expose a canonical string, uuid.UUID, or raw 16 bytes.
    if isinstance(identifier, UUID):
        observed = "GPU-" + str(identifier)
    elif isinstance(identifier, bytes) and len(identifier) == 16:
        observed = "GPU-" + str(UUID(bytes=identifier))
    elif type(identifier) is str:
        value = identifier.lower().removeprefix("gpu-")
        observed = "GPU-" + value
    else:
        observed = None
    if observed is None or _GPU.fullmatch(observed) is None:
        observed, driver_capability = _cuda_driver_identity()
        if driver_capability != _CAPABILITY[rank]:
            raise CustodyError("The CUDA driver reports a different compute capability for this rank.")
    if observed != expected_uuid or (props.major, props.minor) != _CAPABILITY[rank]:
        raise CustodyError("Observed CUDA identity or compute capability differs from the admitted rank.")
    return torch.device("cuda:0")


def _tiny_step(torch, rank, attempt, device, inbound, outbound):
    exchange = AutogradBoundary(BoundaryFrame(attempt, 1, 0, 0, "activation", "float16", (1, 2, 2)), 8)
    weight = torch.tensor(2.0 if rank == 0 else 0.5, device=device, dtype=torch.float16, requires_grad=True)
    if rank == 0:
        x = torch.tensor([[[1.0, 2.0], [3.0, 4.0]]], device=device, dtype=torch.float16, requires_grad=True)
        exchange.early_backward(x * weight, outbound, inbound)
        gradients = x.grad.detach().reshape(-1).tolist()
        weight_grad = weight.grad.detach().item()
        if gradients != [1.0, 2.0, 3.0, 4.0] or weight_grad != 15.0:
            raise CustodyError("Early synthetic GPU backward result differs from reference.")
        return {"input_grad": gradients, "weight_grad": weight_grad}
    leaf = exchange.tail_forward(inbound, device=device)
    loss = (leaf * weight).square().sum() / 2
    loss.backward()
    loss_value, weight_grad = loss.detach().item(), weight.grad.detach().item()
    if loss_value != 15.0 or weight_grad != 60.0:
        raise CustodyError("Tail synthetic GPU forward/backward result differs from reference.")
    exchange.tail_backward(leaf, outbound)
    return {"loss": loss_value, "weight_grad": weight_grad}


def _run_rank(attempt, rank, expected_uuid):
    _identity(attempt, rank, expected_uuid)
    import torch

    device = _cuda_device(torch, rank, expected_uuid)
    evidence = _tiny_step(torch, rank, attempt, device, sys.stdin.buffer, sys.stdout.buffer)
    torch.cuda.synchronize(device)
    receipt = {"attempt": attempt, "rank": rank, "gpu_uuid": expected_uuid,
               "compute_capability": list(_CAPABILITY[rank]), "torch": str(torch.__version__),
               "dtype": "float16", "evidence": evidence}
    sys.stderr.buffer.write(_MARKER + json.dumps(receipt, separators=(",", ":")).encode("ascii") + b"\n")
    sys.stderr.buffer.flush()


def _collect(stream, result):
    with stream:
        while data := stream.read(4096):
            available = _LOG_LIMIT + 1 - len(result)
            if available > 0:
                result.extend(data[:available])


def _pipe_pair(early_argv, tail_argv, timeout):
    """No shell interpolation; each binary stdout is directly the other stdin."""
    if (type(timeout) is not int or not 1 <= timeout <= 120
            or any(type(argv) not in (tuple, list) or not argv
                   or any(type(arg) is not str or not arg or "\0" in arg for arg in argv)
                   for argv in (early_argv, tail_argv))):
        raise CustodyError("Invalid owned rank launch parameters.")
    read_early, write_tail = os.pipe()
    read_tail, write_early = os.pipe()
    children, readers, logs = [], [], [bytearray(), bytearray()]
    try:
        for rank, (argv, rx, tx) in enumerate(((early_argv, read_early, write_early),
                                               (tail_argv, read_tail, write_tail))):
            worker = subprocess.Popen(argv, stdin=rx, stdout=tx, stderr=subprocess.PIPE, shell=False)
            children.append(worker)
            reader = threading.Thread(target=_collect, args=(worker.stderr, logs[rank]), daemon=True)
            reader.start()
            readers.append(reader)
    except BaseException:
        for child in children:
            child.kill()
            child.wait()
        raise
    finally:
        for fd in (read_early, write_tail, read_tail, write_early):
            os.close(fd)
    deadline = time.monotonic() + timeout
    try:
        while any(child.poll() is None for child in children):
            if any(child.returncode not in (None, 0) for child in children):
                break
            if time.monotonic() >= deadline:
                raise CustodyError("The owned two-rank smoke exceeded its wall budget.")
            time.sleep(0.02)
    finally:
        for child in children:
            if child.poll() is None:
                child.kill()
            child.wait()
        for reader in readers:
            reader.join(1)
    if any(reader.is_alive() for reader in readers) or any(len(log) > _LOG_LIMIT for log in logs):
        raise CustodyError("Rank diagnostics were incomplete or exceeded their byte budget.")
    return tuple((child.returncode, bytes(log)) for child, log in zip(children, logs))


def _verified_receipts(attempt, uuids, outcomes):
    receipts = []
    for rank, (uuid, (code, log)) in enumerate(zip(uuids, outcomes, strict=True)):
        accepted = [line[len(_MARKER):] for line in log.splitlines() if line.startswith(_MARKER)]
        if code != 0 or len(accepted) != 1:
            # Keep bounded human-readable local diagnostics, never treat them as instructions.
            raise CustodyError(f"Rank {rank} did not complete its smoke (exit {code}): {log[-2048:]!r}")
        try:
            receipt = json.loads(accepted[0])
            expected = ({"input_grad": [1.0, 2.0, 3.0, 4.0], "weight_grad": 15.0}
                        if rank == 0 else {"loss": 15.0, "weight_grad": 60.0})
            if (receipt["attempt"] != attempt or receipt["rank"] != rank or receipt["gpu_uuid"] != uuid
                    or receipt["compute_capability"] != list(_CAPABILITY[rank])
                    or receipt["dtype"] != "float16" or receipt["evidence"] != expected
                    or type(receipt["torch"]) is not str or not receipt["torch"]):
                raise ValueError
        except (TypeError, KeyError, ValueError) as error:
            raise CustodyError("A smoke receipt did not match its admitted CUDA rank.") from error
        receipts.append(receipt)
    return receipts


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    rank = commands.add_parser("rank", help="Binary rank worker; diagnostics to stderr")
    rank.add_argument("--attempt", required=True)
    rank.add_argument("--rank", type=int, choices=(0, 1), required=True)
    rank.add_argument("--gpu-uuid", required=True)
    pair = commands.add_parser("pair", help="Run two operator-supplied argv arrays over directly owned pipes")
    pair.add_argument("--attempt", required=True)
    pair.add_argument("--early-uuid", required=True)
    pair.add_argument("--tail-uuid", required=True)
    pair.add_argument("--early-argv-json", required=True)
    pair.add_argument("--tail-argv-json", required=True)
    pair.add_argument("--timeout", type=int, default=45)
    opts = parser.parse_args(argv)
    if opts.command == "rank":
        _run_rank(opts.attempt, opts.rank, opts.gpu_uuid)
    else:
        uuids = (opts.early_uuid, opts.tail_uuid)
        for i, uuid in enumerate(uuids):
            _identity(opts.attempt, i, uuid)
        if uuids[0] == uuids[1]:
            raise CustodyError("Two distinct CUDA UUIDs are required.")
        outcomes = _pipe_pair(json.loads(opts.early_argv_json), json.loads(opts.tail_argv_json), opts.timeout)
        print(json.dumps(_verified_receipts(opts.attempt, uuids, outcomes), separators=(",", ":")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
