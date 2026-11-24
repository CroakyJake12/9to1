from importlib.util import find_spec
from io import BytesIO
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
from uuid import uuid4

from .artifacts import CustodyError
from .qwen35_range import Qwen35TextRange
from .qwen35_stage import Qwen35RankStage
from .rank_autograd import AutogradBoundary
from .rank_grad_clip import ClipPlan, GradientSlot
from .rank_stream import BoundaryFrame, receive_frame, send_frame
from .v8_joint_step import JointV8Ports, PreparedV8Rank, RankV8Update, _AdmittedControl, commit_v8_sft_step, prepare_v8_rank, run_v8_rank_sft_step


class JointV8WiringTests(unittest.TestCase):
    def test_unverified_admission_or_mentor_never_reads_model(self):
        reader = Mock()
        for approval, mentor in ((False, True), (True, False), (True, None), (1, True)):
            ports = JointV8Ports(lambda *a: approval, lambda: mentor, Mock(), Mock(), Mock(), Mock())
            with self.assertRaisesRegex(CustodyError, "not verified"):
                prepare_v8_rank(config=None, plan=None, rank=0, verified_tensor_reader=reader,
                                approved_segment="upper", approved_precision="float16",
                                lora_state={}, ports=ports, first_step=1, local_control=None)
        reader.assert_not_called()

    def test_streamed_v100_port_defers_only_exact_owned_placements(self):
        ports = JointV8Ports(lambda *_: True, lambda: True, Mock(), Mock(), Mock(), Mock())
        place = SimpleNamespace(key="test.owned.weight", header=SimpleNamespace(shape=(3, 4)))
        assemble = SimpleNamespace(text=object(), deferred=(place,))
        fetch = Mock(return_value="authenticated device tensor")
        reader, local, sync = Mock(), Mock(), Mock()
        kwargs = dict(config=None, plan=object(), verified_tensor_reader=reader,
                      approved_segment="upper", lora_state={}, ports=ports,
                      first_step=1, local_control=local, v100_fetch_weight=fetch)
        with patch("custody.v8_joint_step.assemble_qwen35_rank", return_value=assemble) as factory, \
                patch("custody.v8_joint_step.place_frozen_early_v8") as pager, \
                patch.dict(sys.modules, torch=SimpleNamespace(cuda=SimpleNamespace(synchronize=sync))):
            with self.assertRaisesRegex(CustodyError, "Unsupported"):
                prepare_v8_rank(rank=1, approved_precision="bfloat16", **kwargs)
            factory.assert_not_called()
            prepared = prepare_v8_rank(rank=0, approved_precision="float16", **kwargs)
            self.assertIs(prepared.text, assemble.text)
            self.assertIs(factory.call_args.kwargs["defer_v100_weights"], True)
            materialize = pager.call_args.kwargs["materialize_weight"]
            with self.assertRaisesRegex(CustodyError, "admitted deferred"):
                materialize("foreign.weight", (3, 4))
            with self.assertRaisesRegex(CustodyError, "admitted deferred"):
                materialize(place.key, (4, 3))
            fetch.assert_not_called()
            with prepared.control.running(1):
                self.assertEqual(materialize(place.key, (3, 4)), "authenticated device tensor")
                request, duty = fetch.call_args.args[0], fetch.call_args.kwargs["duty"]
                self.assertIs(request, place)
                duty()
            sync.assert_called_once_with("cuda:0")
            local.wait_for_compute.assert_called_once_with()

    def test_pager_tracks_actual_step_and_disallows_replay_after_post_wait_revocation(self):
        events = []
        mentor = Mock(return_value=True)

        def admitted(rank, step):
            events.append((rank, step))
            return True

        ports = JointV8Ports(admitted, mentor, Mock(), Mock(), Mock(), Mock())
        local = Mock()
        control = _AdmittedControl(ports, local, 0, 17)
        with self.assertRaisesRegex(CustodyError, "no active"):
            control.wait_for_compute()
        with control.running(17):
            control.wait_for_compute()
            with self.assertRaisesRegex(CustodyError, "already running"):
                with control.running(18):
                    self.fail("overlapping rank step")
        with control.running(18):
            control.wait_for_compute()
        self.assertEqual(events, [(0, 17)] * 3 + [(0, 18)] * 3)
        local.wait_for_compute.side_effect = lambda: setattr(mentor, "return_value", False)
        with self.assertRaisesRegex(CustodyError, "not verified"):
            with control.running(19):
                control.wait_for_compute()
        mentor.return_value = True
        with self.assertRaisesRegex(CustodyError, "out-of-order"):
            with control.running(19):
                self.fail("failed attempt replayed")

    def test_new_tail_step_never_uses_previous_native_duty_expiry(self):
        ports = JointV8Ports(lambda *_: True, lambda: True, Mock(), Mock(), Mock(), Mock())
        control = _AdmittedControl(ports, Mock(), 1, 18)
        tail = Mock()
        prepared = PreparedV8Rank(Mock(), tail, control)
        plan = ClipPlan(str(uuid4()), 18, ("early", "tail"), (GradientSlot(1, "y"),))
        tail.duty.expires = 1e12

        def run_owned(*args, **kwargs):
            self.assertEqual(tail.duty.expires, 0.0)
            return "staged"

        with patch("custody.v8_joint_step._run_owned_step", side_effect=run_owned):
            self.assertEqual(run_v8_rank_sft_step(None, plan, prepared=prepared, grad_accum=2,
                                                 original_seq_len=16, frame_budget=1024), "staged")

    def test_commit_stops_before_storage_on_divergent_rank_results(self):
        with patch("custody.v8_joint_step.commit_sft_update") as actual:
            with self.assertRaisesRegex(CustodyError, "divergent"):
                commit_v8_sft_step(None, None, early=RankV8Update((0,), None, None),
                                   tail=RankV8Update((1,), None, 2.0))
            actual.assert_not_called()
            early, tail = RankV8Update((1,), "rank 0", None), RankV8Update((1,), "rank 1", 2.0)
            commit_v8_sft_step("checkpoint", "plan", early=early, tail=tail)
            actual.assert_called_once_with("checkpoint", "plan", (1,), ("rank 0", "rank 1"))

    @unittest.skipUnless(find_spec("torch"), "requires installed PyTorch")
    def test_explicit_bf16_transition_returns_gradient_to_fp16_wire(self):
        import torch
        from types import SimpleNamespace

        text = object.__new__(Qwen35TextRange)
        text._torch, text._rank = torch, 1
        text._config = SimpleNamespace(hidden_size=3, max_position_embeddings=32)
        shape = (1, 2, 3)
        frame = BoundaryFrame(str(uuid4()), 1, 0, 0, "activation", "float16", shape)
        boundary = AutogradBoundary(frame, 12)
        wire_in, wire_out = BytesIO(), BytesIO()
        send_frame(wire_in, frame, boundary._pack(torch, torch.ones(shape, dtype=torch.float16)), max_bytes=12)
        wire_in.seek(0)

        def native_tail(*, hidden):
            self.assertEqual(hidden.dtype, torch.bfloat16)
            return hidden * 2

        with patch.object(Qwen35TextRange, "forward", side_effect=native_tail):
            stage = Qwen35RankStage(text, boundary)
            with self.assertRaisesRegex(CustodyError, "boundary cast"):
                stage.tail(wire_in, wire_out, step=1, microbatch=0, device="cpu",
                           backward_loss=lambda h: h.sum().backward(), hidden_dtype=torch.float32)
            stage.tail(wire_in, wire_out, step=1, microbatch=0, device="cpu",
                       backward_loss=lambda h: h.sum().backward(), hidden_dtype=torch.bfloat16)
        wire_out.seek(0)
        raw = receive_frame(wire_out, boundary.gradient, max_bytes=12)
        result = boundary._unpack(torch, raw, "cpu")
        torch.testing.assert_close(result, torch.full(shape, 2, dtype=torch.float16), atol=0, rtol=0)
