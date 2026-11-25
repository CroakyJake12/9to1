import hashlib
from io import BytesIO
import unittest
from unittest.mock import Mock
from uuid import uuid4

from .artifacts import CustodyError
from .rank_stream import BoundaryFrame
from .v8_frozen_receipt import FrozenV100Receipt
from .v8_guest_main_channel import FrozenGuestMainChannel


class MainChannelTests(unittest.TestCase):
    def setUp(self):
        self.attempt, self.cuda, self.cpu = str(uuid4()), b"synthetic CUDA", b"synthetic CPU"
        self.receipt = FrozenV100Receipt(self.attempt, 1, "guest", 2, "a" * 64,
                                         hashlib.sha256(self.cuda).hexdigest(),
                                         hashlib.sha256(self.cpu).hexdigest())

    def endpoint(self, rank, *, inbound=None):
        channel = FrozenGuestMainChannel(rank=rank, attempt_id=self.attempt, step=1,
            owner="guest", grad_accum=2, seq_cap=16, vocab_size=32,
            inbound=inbound if inbound is not None else BytesIO(), outbound=BytesIO(),
            sampler=Mock() if rank == 1 else None)
        # Batch format and autograd framing have focused tests of their own.
        channel._batch = Mock(return_value="original-side batch")
        return channel

    def finish_frames(self, rank, channel):
        for micro in range(2):
            self.assertEqual(channel.read_batch(rank, 1, micro), "original-side batch")
            frame = BoundaryFrame(self.attempt, 1, micro, 0, "activation", "float16", (1, 4, 4))
            with channel.frames(rank, frame) as (inbound, outbound):
                self.assertIs(inbound, channel._in)
                self.assertIs(outbound, channel._out)

    def test_guest_completion_round_trip_then_no_replay(self):
        guest = self.endpoint(0)
        self.finish_frames(0, guest)
        guest.send_done(self.receipt, self.cuda, self.cpu)
        host = self.endpoint(1, inbound=BytesIO(guest._out.getvalue()))
        self.finish_frames(1, host)
        self.assertEqual(host.receive_done(), (self.receipt, self.cuda, self.cpu))
        with self.assertRaisesRegex(CustodyError, "out of order"):
            host.receive_done()

    def test_wrong_coordinate_and_truncated_or_corrupt_rng_refuse(self):
        host = self.endpoint(1)
        with self.assertRaisesRegex(CustodyError, "out of order"):
            host.receive_done()
        self.assertEqual(host._in.tell(), 0)
        with self.assertRaisesRegex(CustodyError, "out of order"):
            host.read_batch(1, 1, 0)
        guest = self.endpoint(0)
        self.finish_frames(0, guest)
        guest.send_done(self.receipt, self.cuda, self.cpu)
        raw = guest._out.getvalue()
        for bad in (raw[:-1], raw[:-1] + b"x"):
            host = self.endpoint(1, inbound=BytesIO(bad))
            self.finish_frames(1, host)
            with self.assertRaises(CustodyError):
                host.receive_done()
