from importlib.util import find_spec
import io
import unittest

from custody.artifacts import CustodyError
from custody.rank_stream import BoundaryFrame, receive_frame, send_frame
from custody.v8_batch_port import GuestBatchPort, HostBatchPort, _DTYPE_U32, _HEADER, _MAGIC


ARGS = dict(attempt_id="15c60bc0-d3e4-4fe7-8b09-3d49958fcc25", first_step=7,
            grad_accum=2, seq_cap=4, vocab_size=32)


class BatchPortTests(unittest.TestCase):
    def test_bad_route_attempt_dtype_length_and_truncation_fail_closed(self):
        guest = GuestBatchPort(io.BytesIO(), **ARGS)
        valid = guest._header(7, 0, 2)
        unpacked = list(_HEADER.unpack(valid))
        for index, value in [(0, b"DRF1"), (1, b"x"*16), (2, 8), (3, 1),
                             (4, 1), (5, 2), (6, 5), (6, 1)]:
            bad = unpacked.copy()
            bad[index] = value
            stream = io.BytesIO(_HEADER.pack(*bad) + b"x"*8)
            g = GuestBatchPort(stream, **ARGS)
            with self.assertRaises(CustodyError):
                g(0, 7, 0)
            self.assertEqual(stream.tell(), _HEADER.size)
            with self.assertRaises(CustodyError):
                g(0, 7, 0)
        with self.assertRaises(CustodyError):
            GuestBatchPort(io.BytesIO(valid + b"x"*7), **ARGS)(0, 7, 0)
        with self.assertRaises(CustodyError):
            GuestBatchPort(io.BytesIO(valid + b"\xff"*8), **ARGS)(0, 7, 0)

    @unittest.skipUnless(find_spec("torch"), "native CPU Torch not installed")
    def test_shared_stream_only_ids_and_exact_return_gradient_frame(self):
        import torch

        class Sampler:
            def __init__(self):
                self.calls = []
                self.labels = torch.tensor([[-100, 19, 20]], dtype=torch.long)

            def microbatch(self, step, micro):
                self.calls.append((step,micro))
                return torch.tensor([[step,micro,3]], dtype=torch.long), self.labels

        s = Sampler()
        wire = io.BytesIO()
        host = HostBatchPort(s, wire, **ARGS)
        self.assertIs(host(1, 7, 0), s.labels)
        self.assertEqual(wire.getvalue(), _HEADER.pack(_MAGIC, host._identity, 7, 0, 0, _DTYPE_U32, 3)
                         + b"\x07\0\0\0\0\0\0\0\x03\0\0\0")
        frame = BoundaryFrame(ARGS["attempt_id"],7,0,1,"gradient","float16",(1,3,1))
        send_frame(wire, frame, b"a"*6, max_bytes=6)
        self.assertIs(host(1, 7, 1), s.labels)
        self.assertIs(host(1, 8, 0), s.labels)
        self.assertEqual(s.calls,[(7,0),(7,1),(8,0)])
        wire.seek(0)
        guest = GuestBatchPort(wire, **ARGS)
        self.assertTrue(torch.equal(guest(0,7,0), torch.tensor([[7,0,3]])))
        self.assertEqual(receive_frame(wire,frame,max_bytes=6),b"a"*6)
        self.assertTrue(torch.equal(guest(0,7,1), torch.tensor([[7,1,3]])))
        self.assertTrue(torch.equal(guest(0,8,0), torch.tensor([[8,0,3]])))
        with self.assertRaises(CustodyError):
            guest(0,8,0)

    @unittest.skipUnless(find_spec("torch"), "native CPU Torch not installed")
    def test_invalid_labels_or_ids_never_emits_prefix(self):
        import torch
        class Invalid:
            def microbatch(self, step, micro):
                return torch.tensor([[1,32]]), torch.tensor([[-100,1]])
        wire=io.BytesIO()
        host=HostBatchPort(Invalid(),wire,**ARGS)
        with self.assertRaises(CustodyError):
            host(1,7,0)
        self.assertEqual(wire.getvalue(),b"")
        class InvalidLabels:
            def microbatch(self, step, micro):
                return torch.tensor([[1, 2]]), torch.tensor([[1.0, 2.0]])
        with self.assertRaises(CustodyError):
            HostBatchPort(InvalidLabels(), wire, **ARGS)(1, 7, 0)
        self.assertEqual(wire.getvalue(), b"")
