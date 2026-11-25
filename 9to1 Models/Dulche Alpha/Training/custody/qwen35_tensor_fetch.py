"""Bounded on-demand fetch of pins from the host's admitted BF16 rank.

Use a fresh authenticated duplex transport, separate from activation traffic.
This protocol does not authenticate a peer or enforce a socket timeout by
itself; the launcher must own both. No late resync after a failed request.
"""
import hashlib
import struct
import sys

from .artifacts import CustodyError
from .qwen35_shard_reader import RankTensorReader
from .qwen35_tensor_stream import _CHUNK, _Contract, _read_exact, _write_all

_REQUEST = struct.Struct("!4sQI")  # op, exact monotonically increasing request index, plan ordinal
_STOP = 2**32 - 1
_MAX_REQUESTS = 65536


class _Budget:
    def __init__(self, contract, requests, total_bytes):
        if (type(requests) is not int or not 0 < requests <= _MAX_REQUESTS
                or type(total_bytes) is not int or not 0 < total_bytes <= requests * 2**34):
            raise CustodyError("Invalid admitted on-demand tensor quota.")
        self.contract, self.requests, self.bytes = contract, requests, total_bytes
        self.sequence = 0
        self.ordinals = {p.key: n for n, p in enumerate(contract.order)}

    def debit(self, ordinal):
        if self.sequence >= self.requests or not 0 <= ordinal < len(self.contract.order):
            raise CustodyError("Unadmitted tensor ordinal or exhausted request budget.")
        _, length, digest = self.contract.item(ordinal)
        if length > self.bytes:
            raise CustodyError("On-demand tensor fetch exceeded the admitted byte budget.")
        self.bytes -= length
        self.sequence += 1
        return length, digest


def _prefix(outbound, contract):
    # This response binds the attempt, rank, manifest and plan before the
    # host waits for tensor requests. Domain separation from eager streaming.
    marker = b"DQF1" + contract.prefix[4:]
    _write_all(outbound, marker)
    outbound.flush()


def serve_rank_tensor_fetch(inbound, outbound, *, attempt_id, plan, manifest, reader,
                            max_tensor_bytes, max_backing_bytes, max_requests, max_fetch_bytes):
    """Serve already-owned immutable host data; never serve paths or offsets."""
    contract = _Contract(attempt_id, plan, manifest, max_tensor_bytes, max_backing_bytes)
    budget = _Budget(contract, max_requests, max_fetch_bytes)
    if (type(reader) is not RankTensorReader or reader._stack is None or reader._index != manifest.index_sha256
            or reader._pins != contract.shards or reader._digests != contract.digests
            or reader._placements != contract.by_key):
        raise CustodyError("The host reader does not match the admitted BF16 fetch manifest.")
    _prefix(outbound, contract)
    while True:
        request = _read_exact(inbound, _REQUEST.size)
        magic, seq, ordinal = _REQUEST.unpack(request)
        if magic != b"DQR1" or seq != budget.sequence:
            raise CustodyError("Invalid or replayed on-demand BF16 tensor request.")
        if ordinal == _STOP:
            _write_all(outbound, _REQUEST.pack(b"DQD1", seq, _STOP))
            outbound.flush()
            return
        length, _ = budget.debit(ordinal)
        p = contract.order[ordinal]
        # _read_bytes validates original pins from the already-open host
        # descriptors; never forward unauthenticated contents.
        payload = reader._read_bytes(p)
        if len(payload) != length:
            raise CustodyError("Host tensor size differs from its admitted request.")
        _write_all(outbound, _REQUEST.pack(b"DQA1", seq, ordinal))
        _write_all(outbound, payload)
        outbound.flush()
        del payload


class RankTensorFetcher:
    """One private host tensor at a time; caller must not keep returned storage."""

    def __init__(self, inbound, outbound, *, attempt_id, plan, manifest, max_tensor_bytes,
                 max_backing_bytes, max_requests, max_fetch_bytes):
        contract = _Contract(attempt_id, plan, manifest, max_tensor_bytes, max_backing_bytes)
        self._budget = _Budget(contract, max_requests, max_fetch_bytes)
        self._in, self._out = inbound, outbound
        self._closed = False
        if _read_exact(inbound, len(contract.prefix)) != b"DQF1" + contract.prefix[4:]:
            raise CustodyError("On-demand BF16 stream has the wrong attempt or rank manifest.")

    def _consume(self, placement, make_private_sink):
        contract = self._budget.contract
        discard = None
        try:
            if self._closed or contract.by_key.get(placement.key) != placement:
                raise CustodyError("Unadmitted or closed on-demand BF16 request.")
            seq, ordinal = self._budget.sequence, self._budget.ordinals[placement.key]
            length, expected_digest = self._budget.debit(ordinal)
            _write_all(self._out, _REQUEST.pack(b"DQR1", seq, ordinal))
            self._out.flush()
            if _read_exact(self._in, _REQUEST.size) != _REQUEST.pack(b"DQA1", seq, ordinal):
                raise CustodyError("On-demand BF16 response does not match its original request.")
            # Sink stays private and its publication callback runs only after
            # authenticating the original BF16 bytes across every chunk.
            accept, publish, discard = make_private_sink(length)
            raw, digest, offset = bytearray(min(_CHUNK, length)), hashlib.sha256(), 0
            with memoryview(raw) as view:
                while offset < length:
                    with view[:min(_CHUNK, length - offset)] as chunk:
                        fill = 0
                        while fill < len(chunk):
                            count = self._in.readinto(chunk[fill:])
                            if type(count) is not int or not 0 < count <= len(chunk) - fill:
                                raise CustodyError("Truncated on-demand BF16 tensor payload.")
                            fill += count
                        digest.update(chunk)
                        accept(offset, chunk)
                        offset += len(chunk)
            if digest.digest() != expected_digest:
                raise CustodyError("On-demand BF16 payload digest does not match its original pin.")
            return publish()
        except BaseException:
            self._closed = True
            if discard is not None:
                discard()
            raise

    def _fetch(self, placement):
        """Tiny test/diagnostic helper only; product weights use the GPU sink."""
        def make(length):
            if length > _CHUNK:
                raise CustodyError("CPU diagnostic fetch would retain an overlarge tensor.")
            result = bytearray(length)

            def accept(offset, chunk):
                result[offset:offset + len(chunk)] = chunk

            return accept, lambda: result, result.clear

        return self._consume(placement, make)

    def materialize_v100(self, placement, *, duty):
        """Private GPU destination; no full guest CPU tensor allocation."""
        import torch

        device = torch.device("cuda:0")
        if (not callable(duty) or sys.byteorder != "little" or torch.cuda.device_count() != 1
                or torch.cuda.get_device_capability(device) != (7, 0)):
            self._closed = True
            raise CustodyError("Chunked materialization needs the admitted V100 FP16 lease.")
        try:
            # Recheck before requesting any work, allocation, each copy, and
            # handing off the verified destination. Existing Duty owns pacing.
            duty()

            def make(length):
                duty()
                private = [torch.empty(length // 2, device=device, dtype=torch.float16)]

                def accept(offset, chunk):
                    duty()
                    with torch.no_grad():
                        source = torch.frombuffer(chunk, dtype=torch.bfloat16)
                        converted = source.to(dtype=torch.float16)
                        start = offset // 2
                        private[0][start:start + converted.numel()].copy_(converted, non_blocking=False)

                def publish():
                    duty()
                    return private.pop().view(placement.header.shape)

                return accept, publish, private.clear

            return self._consume(placement, make)
        except BaseException:
            self._closed = True
            raise

    def finish(self):
        if self._closed:
            raise CustodyError("On-demand tensor fetch was already retired.")
        self._closed = True
        seq = self._budget.sequence
        _write_all(self._out, _REQUEST.pack(b"DQR1", seq, _STOP))
        self._out.flush()
        if _read_exact(self._in, _REQUEST.size) != _REQUEST.pack(b"DQD1", seq, _STOP):
            raise CustodyError("On-demand BF16 stream did not acknowledge completion.")
