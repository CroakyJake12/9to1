from dataclasses import replace
from io import BytesIO
import socket
from threading import Thread
import unittest
from uuid import uuid4

from .artifacts import CustodyError
from .qwen35_shard_reader import RankTensorReader
from .qwen35_tensor_fetch import RankTensorFetcher, serve_rank_tensor_fetch, _REQUEST
from .qwen35_tensor_stream import _Contract
from . import test_qwen35_tensor_stream as eager_fixture


class OnDemandFetchTests(unittest.TestCase):
    def setUp(self):
        eager_fixture.RankTensorByteStreamTests.setUp(self)
        self.contract = _Contract(self.options["attempt_id"], self.plan, self.manifest, 4, 8)
        self.config = self.options | {"max_requests": 3, "max_fetch_bytes": 12}

    def test_duplex_repeats_only_the_requested_pinned_tensor(self):
        host, guest = socket.socketpair()
        host.settimeout(2)
        guest.settimeout(2)
        errors = []

        def server():
            try:
                with host, host.makefile("rb") as source, host.makefile("wb") as sink, \
                        RankTensorReader(self.root, self.plan, self.manifest, max_tensor_bytes=4) as reader:
                    serve_rank_tensor_fetch(source, sink, reader=reader, **self.config)
            except Exception as exc:
                errors.append(exc)

        worker = Thread(target=server, daemon=True)
        worker.start()
        try:
            with guest, guest.makefile("rb") as source, guest.makefile("wb") as sink:
                receiver = RankTensorFetcher(source, sink, **self.config)
                for ordinal in (1, 1, 0):
                    got = receiver._fetch(self.placements[ordinal])
                    self.assertEqual(got, self.data[ordinal])
                    self.assertIs(type(got), bytearray)
                    del got
                receiver.finish()
                with self.assertRaisesRegex(CustodyError, "closed"):
                    receiver._fetch(self.placements[0])
        finally:
            worker.join(3)
        self.assertFalse(worker.is_alive())
        self.assertEqual(errors, [])

    def _header(self):
        return b"DQF1" + self.contract.prefix[4:]

    def test_reply_attempt_integrity_and_foreign_requests_fail_closed(self):
        sink = BytesIO()
        with self.assertRaisesRegex(CustodyError, "wrong attempt"):
            RankTensorFetcher(BytesIO(self._header()), sink,
                              **(self.config | {"attempt_id": str(uuid4())}))

        receiver = RankTensorFetcher(BytesIO(self._header()), sink, **self.config)
        with self.assertRaisesRegex(CustodyError, "Unadmitted"):
            receiver._fetch(replace(self.placements[0], key="model.language_model.layers.32.q_proj.weight"))
        self.assertEqual(sink.getvalue(), b"")

        response = self._header() + _REQUEST.pack(b"DQA1", 0, 0) + b"fake"
        receiver = RankTensorFetcher(BytesIO(response), BytesIO(), **self.config)
        with self.assertRaisesRegex(CustodyError, "digest"):
            receiver._fetch(self.placements[0])
        with self.assertRaisesRegex(CustodyError, "closed"):
            receiver._fetch(self.placements[0])

    def test_host_refuses_replay_or_excess_before_returning_extra_payload(self):
        for requests, error in ([(0, 0), (0, 0)], "replayed"), ([(0, 0), (1, 1)], "byte budget"):
            source = BytesIO(b"".join(_REQUEST.pack(b"DQR1", n, ordinal) for n, ordinal in requests))
            sink = BytesIO()
            with RankTensorReader(self.root, self.plan, self.manifest, max_tensor_bytes=4) as reader, \
                    self.assertRaisesRegex(CustodyError, error):
                serve_rank_tensor_fetch(source, sink, reader=reader, **(self.config | {"max_fetch_bytes": 4}))
            self.assertEqual(sink.getvalue(), self._header() + _REQUEST.pack(b"DQA1", 0, 0) + self.data[0])

    def test_chunk_sink_only_publishes_after_original_digest_and_discards_failures(self):
        from unittest.mock import patch

        class Fragmented(BytesIO):
            def readinto(self, view):
                return super().readinto(view[:1])  # split a BF16 value on the byte stream

        cases = ((self.data[0], None, [("chunk", 0, 2), ("chunk", 2, 2), "publish"]),
                 (self.data[0][:-1] + b"!", "digest", [("chunk", 0, 2), ("chunk", 2, 2), "discard"]),
                 (self.data[0][:-1], "Truncated", [("chunk", 0, 2), "discard"]))
        for payload, error, expected in cases:
            with self.subTest(error=error):
                staged = []
                receiver = RankTensorFetcher(Fragmented(self._header() + _REQUEST.pack(b"DQA1", 0, 0) + payload),
                                             BytesIO(), **self.config)

                def make(length):
                    self.assertEqual(length, 4)
                    return (lambda offset, chunk: staged.append(("chunk", offset, len(chunk))),
                            lambda: staged.append("publish"),
                            lambda: staged.append("discard"))

                with patch("custody.qwen35_tensor_fetch._CHUNK", 2):
                    if error:
                        with self.assertRaisesRegex(CustodyError, error):
                            receiver._consume(self.placements[0], make)
                    else:
                        receiver._consume(self.placements[0], make)
                self.assertEqual(staged, expected)
