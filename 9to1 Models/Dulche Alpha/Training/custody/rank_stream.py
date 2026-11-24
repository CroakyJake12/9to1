"""Strict tensor frames for already-owned binary streams; no launcher or network I/O."""

from dataclasses import dataclass
import struct
from typing import BinaryIO
from uuid import UUID

from .artifacts import CustodyError, canonical_id

# Fixed header, big-endian metadata, raw little-endian IEEE/BF16 payload.
_HEADER = struct.Struct("!4s16sQIBBBIIII")
_MAGIC = b"DRF1"
_DTYPES = {"float32": (1, 4), "float16": (2, 2), "bfloat16": (3, 2)}
_ROUTES = {"activation": (0, 1), "gradient": (1, 2)}  # direction: (source rank, wire code)
_HARD_CAP = 64 * 1024 * 1024
_CHUNK = 64 * 1024


@dataclass(frozen=True, slots=True)
class BoundaryFrame:
    attempt_id: str
    step: int
    microbatch: int
    source_rank: int
    direction: str
    dtype: str
    shape: tuple[int, int, int]


def _wire(frame: BoundaryFrame, max_bytes: int) -> tuple[bytes, int]:
    if type(max_bytes) is not int or not 0 < max_bytes <= _HARD_CAP:
        raise CustodyError("Invalid rank frame byte limit.")
    if (type(frame) is not BoundaryFrame or type(frame.step) is not int or not 0 < frame.step < 2**64
            or type(frame.microbatch) is not int or not 0 <= frame.microbatch < 2**32
            or type(frame.direction) is not str or frame.direction not in _ROUTES
            or type(frame.source_rank) is not int or frame.source_rank != _ROUTES[frame.direction][0]
            or type(frame.dtype) is not str or frame.dtype not in _DTYPES
            or type(frame.shape) is not tuple or len(frame.shape) != 3
            or any(type(dim) is not int or not 0 < dim < 2**32 for dim in frame.shape)):
        raise CustodyError("Invalid rank frame expectation.")
    attempt = UUID(canonical_id(frame.attempt_id))
    if attempt.int == 0:
        raise CustodyError("Invalid rank frame attempt.")
    dtype, item_bytes = _DTYPES[frame.dtype]
    batch, tokens, width = frame.shape
    length = batch * tokens * width * item_bytes
    if length > max_bytes:
        raise CustodyError("Rank frame exceeds the byte limit.")
    return _HEADER.pack(_MAGIC, attempt.bytes, frame.step, frame.microbatch, frame.source_rank,
                        _ROUTES[frame.direction][1], dtype, batch, tokens, width, length), length


def _read_exact(stream: BinaryIO, length: int) -> bytes:
    result = bytearray()
    while len(result) < length:
        remaining = min(_CHUNK, length - len(result))
        chunk = stream.read(remaining)
        if type(chunk) is not bytes or not 0 < len(chunk) <= remaining:
            raise CustodyError("Truncated or invalid rank stream.")
        result.extend(chunk)
    return bytes(result)


def _write_all(stream: BinaryIO, data: bytes) -> None:
    pending = memoryview(data)
    while pending:
        count = stream.write(pending[:_CHUNK])
        if type(count) is not int or not 0 < count <= min(_CHUNK, len(pending)):
            raise CustodyError("Short or invalid rank stream write.")
        pending = pending[count:]


def send_frame(stream: BinaryIO, frame: BoundaryFrame, payload: bytes, *, max_bytes: int) -> None:
    """Send an exact frame. After any I/O failure, retire the stream out of band."""
    header, length = _wire(frame, max_bytes)
    if type(payload) is not bytes or len(payload) != length:
        raise CustodyError("Rank payload does not match the expected tensor byte size.")
    _write_all(stream, header)
    _write_all(stream, payload)
    stream.flush()


def receive_frame(stream: BinaryIO, expected: BoundaryFrame, *, max_bytes: int) -> bytes:
    """Read only the exact caller-scheduled frame; no resync after rejection."""
    header, length = _wire(expected, max_bytes)
    if _read_exact(stream, _HEADER.size) != header:
        raise CustodyError("Rank frame header differs from the expected activation or gradient.")
    return _read_exact(stream, length)
