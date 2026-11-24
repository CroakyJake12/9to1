"""One native tiny Qwen microbatch over the existing operator-owned two-GPU pipes."""

import argparse
from copy import deepcopy
import hashlib
import json
import math
import re
import sys

from .artifacts import CustodyError
from .dual_gpu_smoke import _CAPABILITY, _cuda_device, _identity, _pipe_pair
from .qwen35_range import Qwen35TextRange
from .qwen35_stage import Qwen35RankStage
from .rank_autograd import AutogradBoundary
from .rank_stream import BoundaryFrame

_MARKER = b"DULCHE_DUAL_QWEN_SMOKE "
_RTOL = 0.003
_ATOL = 0.00003
_SHA256 = re.compile(r"[0-9a-f]{64}")


def _fixture(torch):
    import transformers
    from transformers import Qwen3_5TextConfig
    from transformers.models.qwen3_5.modeling_qwen3_5 import Qwen3_5TextModel

    if transformers.__version__ != "5.20.1":
        raise CustodyError("Native Qwen smoke requires reviewed Transformers 5.20.1.")
    torch.manual_seed(41)
    config = Qwen3_5TextConfig(
        vocab_size=32, hidden_size=32, intermediate_size=64, num_hidden_layers=4,
        num_attention_heads=2, num_key_value_heads=1, head_dim=16, linear_conv_kernel_dim=2,
        linear_key_head_dim=8, linear_value_head_dim=8, linear_num_key_heads=1, linear_num_value_heads=2,
        layer_types=["linear_attention", "full_attention"] * 2, max_position_embeddings=64,
        pad_token_id=0, use_cache=False, attention_dropout=0,
        rope_parameters={"rope_type": "default", "rope_theta": 10000.,
                         "partial_rotary_factor": 0.5, "mrope_section": [2, 1, 1]},
    )
    config._attn_implementation = "eager"
    with torch.device("cpu"):
        return Qwen3_5TextModel(config), torch.nn.Linear(32, 32, bias=False), torch.tensor([[1, 5, 9, 7, 2]])


def _loss(torch, head, hidden, ids):
    return torch.nn.functional.cross_entropy(head(hidden[:, :-1]).reshape(-1, 32), ids[:, 1:].reshape(-1))


def _rank(attempt, rank, expected_uuid):
    _identity(attempt, rank, expected_uuid)
    import torch

    torch.set_num_threads(1)
    device = _cuda_device(torch, rank, expected_uuid)
    # Common Volta/Blackwell route, no BF16 or TF32 precision policy change to v8.
    torch.set_float32_matmul_precision("highest")
    torch.backends.cuda.matmul.allow_tf32 = False
    torch.backends.cudnn.allow_tf32 = False
    reference, head, ids = _fixture(torch)
    digest = hashlib.sha256()
    for module in (reference, head):
        for name, parameter in module.named_parameters():
            digest.update(name.encode("ascii") + b"\0")
            digest.update(bytes(parameter.detach().contiguous().reshape(-1).view(torch.uint8).tolist()))
    ranked = deepcopy(reference)
    local_head = deepcopy(head).to(device) if rank == 1 else None
    native_hidden = reference(input_ids=ids, use_cache=False).last_hidden_state
    native_loss = _loss(torch, head, native_hidden, ids)
    native_loss.backward()

    text = Qwen35TextRange(ranked, split=2, rank=rank)
    for module in (text._edge, text._rotary, *(layer for _, layer in text._layers)):
        module.to(device)
    boundary = AutogradBoundary(BoundaryFrame(attempt, 1, 0, 0, "activation", "float32", (1, 5, 32)), 640)
    stage = Qwen35RankStage(text, boundary)
    device_ids = ids.to(device)
    loss_value = None
    if rank == 0:
        stage.early(device_ids, sys.stdin.buffer, sys.stdout.buffer, step=1, microbatch=0)
    else:
        def backward_loss(hidden):
            torch.testing.assert_close(hidden.detach().cpu(), native_hidden.detach(), rtol=_RTOL, atol=_ATOL)
            loss = _loss(torch, local_head, hidden, device_ids)
            torch.testing.assert_close(loss.detach().cpu(), native_loss.detach(), rtol=_RTOL, atol=_ATOL)
            loss.backward()
            return loss.item()

        loss_value = stage.tail(sys.stdin.buffer, sys.stdout.buffer, step=1, microbatch=0,
                                device=device, backward_loss=backward_loss)
    count, max_error = 0, 0.0
    for name, parameter in ranked.named_parameters():
        owned_by_tail = name.startswith(("layers.2.", "layers.3.", "norm."))
        if owned_by_tail != (rank == 1):
            if parameter.grad is not None:
                raise CustodyError("A foreign Qwen smoke parameter unexpectedly received a gradient.")
            continue
        expected = reference.get_parameter(name).grad
        if expected is None or parameter.grad is None:
            if expected is not None or parameter.grad is not None:
                raise CustodyError("Missing Qwen smoke gradient differs from the CPU reference.")
            continue
        actual = parameter.grad.detach().cpu()
        torch.testing.assert_close(actual, expected, rtol=_RTOL, atol=_ATOL)
        count += 1
        max_error = max(max_error, (actual - expected).abs().max().item())
    if rank == 1:
        actual = local_head.weight.grad.detach().cpu()
        torch.testing.assert_close(actual, head.weight.grad, rtol=_RTOL, atol=_ATOL)
        count += 1
        max_error = max(max_error, (actual - head.weight.grad).abs().max().item())
    if not count:
        raise CustodyError("No native Qwen stage gradients were observed.")
    torch.cuda.synchronize(device)
    receipt = {"attempt": attempt, "rank": rank, "gpu_uuid": expected_uuid,
               "compute_capability": list(_CAPABILITY[rank]), "dtype": "float32",
               "fixture": "tiny-native-qwen35-v1", "fixture_sha256": digest.hexdigest(), "torch": str(torch.__version__),
               "owned_gradients": count, "max_abs_gradient_error": max_error, "loss": loss_value}
    sys.stderr.buffer.write(_MARKER + json.dumps(receipt, separators=(",", ":"), allow_nan=False).encode("ascii") + b"\n")
    sys.stderr.buffer.flush()


def _receipts(attempt, uuids, outcomes):
    accepted = []
    for rank, (uuid, (code, log)) in enumerate(zip(uuids, outcomes, strict=True)):
        lines = [line[len(_MARKER):] for line in log.splitlines() if line.startswith(_MARKER)]
        if code != 0 or len(lines) != 1:
            raise CustodyError(f"Native Qwen rank {rank} did not complete (exit {code}): {log[-4096:]!r}")
        try:
            receipt = json.loads(lines[0])
            error, loss = receipt["max_abs_gradient_error"], receipt["loss"]
            if (receipt["attempt"] != attempt or type(receipt["rank"]) is not int or receipt["rank"] != rank
                    or receipt["gpu_uuid"] != uuid or receipt["compute_capability"] != list(_CAPABILITY[rank])
                    or receipt["fixture"] != "tiny-native-qwen35-v1" or receipt["dtype"] != "float32"
                    or type(receipt["fixture_sha256"]) is not str or not _SHA256.fullmatch(receipt["fixture_sha256"])
                    or type(receipt["torch"]) is not str or not receipt["torch"]
                    or type(receipt["owned_gradients"]) is not int or not 1 <= receipt["owned_gradients"] <= 256
                    or type(error) not in (int, float) or not math.isfinite(error) or error < 0
                    or (rank == 0 and loss is not None)
                    or (rank == 1 and (type(loss) not in (int, float) or not math.isfinite(loss) or loss <= 0))):
                raise ValueError
        except (KeyError, TypeError, ValueError) as error:
            raise CustodyError("A native Qwen receipt does not match its admitted CUDA rank.") from error
        accepted.append(receipt)
    if accepted[0]["fixture_sha256"] != accepted[1]["fixture_sha256"]:
        raise CustodyError("The native Qwen ranks did not use the same synthetic fixture state.")
    return accepted


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    rank = sub.add_parser("rank")
    rank.add_argument("--attempt", required=True)
    rank.add_argument("--rank", type=int, choices=(0, 1), required=True)
    rank.add_argument("--gpu-uuid", required=True)
    pair = sub.add_parser("pair")
    pair.add_argument("--attempt", required=True)
    pair.add_argument("--early-uuid", required=True)
    pair.add_argument("--tail-uuid", required=True)
    pair.add_argument("--early-argv-json", required=True)
    pair.add_argument("--tail-argv-json", required=True)
    pair.add_argument("--timeout", type=int, default=60)
    args = parser.parse_args(argv)
    if args.command == "rank":
        _rank(args.attempt, args.rank, args.gpu_uuid)
    else:
        uuids = (args.early_uuid, args.tail_uuid)
        for rank, uuid in enumerate(uuids):
            _identity(args.attempt, rank, uuid)
        if uuids[0] == uuids[1]:
            raise CustodyError("Native Qwen smoke requires distinct GPU UUIDs.")
        results = _pipe_pair(json.loads(args.early_argv_json), json.loads(args.tail_argv_json), args.timeout)
        print(json.dumps(_receipts(args.attempt, uuids, results), separators=(",", ":"), allow_nan=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
