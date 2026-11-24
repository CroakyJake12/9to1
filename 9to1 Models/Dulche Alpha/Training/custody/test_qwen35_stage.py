from dataclasses import replace
from importlib.util import find_spec
from io import BytesIO
import multiprocessing
import socket
import unittest
from unittest.mock import patch
from uuid import uuid4

from custody.artifacts import CustodyError
from custody.qwen35_range import Qwen35TextRange
from custody.qwen35_stage import Qwen35RankStage
from custody.rank_autograd import AutogradBoundary
from custody.rank_stream import BoundaryFrame, send_frame
from custody.test_qwen35_range import make_tiny_text


def _fixture():
    import torch
    torch.set_num_threads(1)
    torch.manual_seed(41)
    text = make_tiny_text()
    head = torch.nn.Linear(32, 32, bias=False)
    ids = torch.tensor([[1, 5, 9, 7, 2], [4, 3, 10, 6, 2]])
    return text, head, ids


def _loss(head, hidden, ids):
    import torch
    return 2 * torch.nn.functional.cross_entropy(head(hidden[:, :-1]).reshape(-1, 32), ids[:, 1:].reshape(-1))


def _tail(peer, boundary, results):
    text, head, ids = _fixture()
    stage = Qwen35RankStage(Qwen35TextRange(text, split=2, rank=1), boundary)
    def backward_loss(hidden):
        loss = _loss(head, hidden, ids)
        loss.backward()
        return loss.item(), hidden.detach().tolist()
    with peer, peer.makefile("rb") as inbound, peer.makefile("wb") as outbound:
        loss, hidden = stage.tail(inbound, outbound, step=1, microbatch=0, device="cpu",
                                  backward_loss=backward_loss)
    grads = {n: p.grad.tolist() if p.grad is not None else None for n, p in text.named_parameters()}
    results.put((loss, hidden, grads, head.weight.grad.tolist()))


@unittest.skipUnless(find_spec("torch") and find_spec("transformers"), "requires installed native PyTorch/Qwen")
class Qwen35StageTests(unittest.TestCase):
    def make_boundary(self):
        return AutogradBoundary(BoundaryFrame(str(uuid4()), 1, 0, 0, "activation", "float32", (2, 5, 32)), 1280)

    def test_actual_two_process_stage_matches_unsplit_qwen(self):
        import torch
        ref, ref_head, ids = _fixture()
        early, _, _ = _fixture()
        boundary = self.make_boundary()
        stage = Qwen35RankStage(Qwen35TextRange(early, split=2, rank=0), boundary)
        ctx = multiprocessing.get_context("spawn")
        local, peer = socket.socketpair()
        local.settimeout(20)
        peer.settimeout(20)
        results = ctx.Queue()
        child = ctx.Process(target=_tail, args=(peer, boundary, results))
        child.start()
        try:
            peer.close()
            with local, local.makefile("rb") as inbound, local.makefile("wb") as outbound:
                stage.early(ids, inbound, outbound, step=1, microbatch=0)
            loss, hidden, grads, head_grad = results.get(timeout=15)
            child.join(15)
            self.assertEqual(child.exitcode, 0)

            native = ref(input_ids=ids, use_cache=False).last_hidden_state
            ref_loss = _loss(ref_head, native, ids)
            ref_loss.backward()
            torch.testing.assert_close(torch.tensor(hidden), native, rtol=0, atol=0)
            self.assertEqual(loss, ref_loss.item())
            torch.testing.assert_close(torch.tensor(head_grad), ref_head.weight.grad, rtol=0, atol=0)
            for name, parameter in early.named_parameters():
                with self.subTest(name=name):
                    expected = ref.get_parameter(name).grad
                    tail_owned = name.startswith(("layers.2.", "layers.3.", "norm."))
                    if tail_owned:
                        self.assertIsNone(parameter.grad)
                        actual = torch.tensor(grads[name]) if grads[name] is not None else None
                    else:
                        self.assertIsNone(grads[name])
                        actual = parameter.grad
                    if expected is None:
                        self.assertIsNone(actual)
                    else:
                        torch.testing.assert_close(actual, expected, rtol=0, atol=0)
        finally:
            if child.is_alive():
                child.terminate()
                child.join(5)
            results.close()

    def test_role_coordinate_and_wire_mismatch_rejected_before_model_forward(self):
        text, _, ids = _fixture()
        boundary = self.make_boundary()
        early, tail = (Qwen35RankStage(Qwen35TextRange(text, split=2, rank=i), boundary) for i in (0, 1))
        inbound, outbound = BytesIO(), BytesIO()
        with patch.object(Qwen35TextRange, "forward", side_effect=AssertionError("unexpected forward")):
            for stage, step, microbatch in ((tail, 1, 0), (early, 2, 0), (early, 1, 1)):
                with self.assertRaisesRegex(CustodyError, "rank or microbatch"):
                    stage.early(ids, inbound, outbound, step=step, microbatch=microbatch)
            with self.assertRaisesRegex(CustodyError, "token batch"):
                early.early(ids[:, :-1], inbound, outbound, step=1, microbatch=0)
            send_frame(inbound, replace(boundary.activation, microbatch=1), b"\0" * 1280, max_bytes=1280)
            inbound.seek(0)
            with self.assertRaisesRegex(CustodyError, "header differs"):
                tail.tail(inbound, outbound, step=1, microbatch=0, device="cpu", backward_loss=lambda h: None)
        self.assertEqual(outbound.getvalue(), b"")
