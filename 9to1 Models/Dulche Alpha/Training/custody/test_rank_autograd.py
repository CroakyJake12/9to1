from dataclasses import replace
from importlib.util import find_spec
from io import BytesIO
import multiprocessing
import socket
from struct import pack
import unittest
from uuid import uuid4

from custody.artifacts import CustodyError
from custody.rank_autograd import AutogradBoundary
from custody.rank_stream import BoundaryFrame, receive_frame, send_frame


def _tail(peer, boundary, outcomes):
    import torch

    with peer, peer.makefile("rb") as inbound, peer.makefile("wb") as outbound:
        leaf = boundary.tail_forward(inbound, device="cpu")
        weight = torch.tensor(3.0, requires_grad=True)
        loss = (leaf.mul(weight).sub(1).square().sum() / 2) * 2
        loss.backward()
        boundary.tail_backward(leaf, outbound)
        outcomes.put((loss.item(), weight.grad.item(), leaf.is_leaf))


class RankAutogradTests(unittest.TestCase):
    def setUp(self):
        self.spec = BoundaryFrame(str(uuid4()), 1, 0, 0, "activation", "float32", (1, 2, 2))
        self.boundary = AutogradBoundary(self.spec, 16)

    def test_boundary_shares_exact_activation_coordinates_and_is_protocol_compatible(self):
        payload = pack("<4f", 1, 2, 3, 4)
        gradient = replace(self.spec, source_rank=1, direction="gradient")
        self.assertEqual(self.boundary.gradient, gradient)
        wire = BytesIO()
        send_frame(wire, gradient, payload, max_bytes=self.boundary.max_bytes)
        wire.seek(0)
        self.assertEqual(receive_frame(wire, self.boundary.gradient, max_bytes=16), payload)
        with self.assertRaisesRegex(CustodyError, "begin with an activation"):
            AutogradBoundary(gradient, 16)
        with self.assertRaisesRegex(CustodyError, "exceeds"):
            AutogradBoundary(self.spec, 15)

    @unittest.skipUnless(find_spec("torch") is not None, "real CPU tensors require installed PyTorch")
    def test_wire_handles_noncontiguous_low_precision_and_rejects_nonfinite_values(self):
        import torch

        for name in ("float32", "float16", "bfloat16"):
            boundary = AutogradBoundary(replace(self.spec, dtype=name), 16)
            tensor = torch.tensor([[[1, -2], [3, 4]]], dtype=getattr(torch, name)).transpose(1, 2)
            self.assertFalse(tensor.is_contiguous())
            wire = BytesIO()
            send_frame(wire, boundary.activation, boundary._pack(torch, tensor), max_bytes=16)
            wire.seek(0)
            leaf = boundary.tail_forward(wire, device="cpu")
            self.assertTrue(leaf.is_leaf and leaf.requires_grad)
            torch.testing.assert_close(leaf, tensor, rtol=0, atol=0)
            with self.assertRaisesRegex(CustodyError, "missing its boundary gradient"):
                boundary.tail_backward(leaf, BytesIO())
        with self.assertRaisesRegex(CustodyError, "nonfinite"):
            self.boundary._pack(torch, torch.full((1, 2, 2), float("nan")))

    @unittest.skipUnless(find_spec("torch") is not None, "real CPU graph requires installed PyTorch")
    def test_real_two_process_cpu_graph_matches_unsplit_autograd(self):
        import torch

        ctx = multiprocessing.get_context("spawn")
        local, peer = socket.socketpair()
        local.settimeout(10)
        peer.settimeout(10)
        outcomes = ctx.Queue()
        proc = ctx.Process(target=_tail, args=(peer, self.boundary, outcomes))
        proc.start()
        try:
            peer.close()
            x = torch.tensor([[[1.25, -2.5], [0.75, 0.5]]], requires_grad=True)
            w = torch.tensor(2.0, requires_grad=True)
            with local, local.makefile("wb") as outbound, local.makefile("rb") as inbound:
                self.boundary.early_backward(x * w, outbound, inbound)
            proc.join(15)
            self.assertEqual(proc.exitcode, 0)
            tail_loss, tail_grad, leaf = outcomes.get(timeout=2)
            self.assertTrue(leaf)

            ref_x = x.detach().clone().requires_grad_(True)
            ref_w = w.detach().clone().requires_grad_(True)
            ref_tail = torch.tensor(3.0, requires_grad=True)
            reference = ((ref_x * ref_w * ref_tail - 1).square().sum() / 2) * 2
            reference.backward()
            torch.testing.assert_close(x.grad, ref_x.grad, rtol=0, atol=0)
            torch.testing.assert_close(w.grad, ref_w.grad, rtol=0, atol=0)
            self.assertEqual(tail_grad, ref_tail.grad.item())
            self.assertEqual(tail_loss, reference.item())
        finally:
            if proc.is_alive():
                proc.terminate()
                proc.join(5)
            outcomes.close()
