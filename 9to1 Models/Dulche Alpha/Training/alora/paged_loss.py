"""Causal training loss with bounded vocabulary/token tiles over a frozen head.

The native decoder remains unchanged. Only the frozen LM-head loss boundary is
streamed; no complete [tokens, vocabulary] logits tensor is retained or returned.
"""
from __future__ import annotations

import torch
from torch.nn import functional as F
from torch.autograd.function import once_differentiable

from .paging import PagedLinear, _materialize
from .compute import compute_scope, check_compute, current_compute_callback


def _chunks(size, rows):
    for start in range(0, size, rows):
        yield start, min(start + rows, size)


class _StreamingCrossEntropy(torch.autograd.Function):
    @staticmethod
    def forward(ctx, hidden, targets, store, refs, token_rows):
        callback = current_compute_callback()
        device, count = hidden.device, hidden.shape[0]
        check_compute(device=device)
        maxima = torch.full((count,), -torch.inf, dtype=torch.float32, device=device)
        totals = torch.zeros_like(maxima)
        selected = torch.empty_like(maxima)
        for ref in refs:
            check_compute(device=device)
            weight = _materialize(store, ref, device=device, dtype=hidden.dtype)
            lo, hi = ref.row_start, ref.row_start + ref.shape[0]
            for start, end in _chunks(count, token_rows):
                check_compute(device=device)
                scores = F.linear(hidden[start:end], weight).float()
                if not bool(torch.isfinite(scores).all()):
                    raise ValueError("Frozen output-head logits are nonfinite")
                chunk_max = scores.amax(dim=-1)
                old_max = maxima[start:end]
                new_max = torch.maximum(old_max, chunk_max)
                totals[start:end] = (totals[start:end] * torch.exp(old_max - new_max)
                                     + torch.exp(scores - new_max[:, None]).sum(dim=-1))
                maxima[start:end] = new_max
                labels = targets[start:end]
                matches = (labels >= lo) & (labels < hi)
                local_rows = torch.where(matches)[0]
                selected[start + local_rows] = scores[local_rows, labels[local_rows] - lo]
            del weight
        log_totals = totals.log()
        # Keep the shift separate: adding a small log-total to a large FP32
        # maximum loses probability precision even when the loss is finite.
        loss = ((maxima - selected) + log_totals).mean()
        if not bool(torch.isfinite(loss)):
            raise ValueError("Streaming output-head loss is nonfinite")
        ctx.store, ctx.refs, ctx.token_rows = store, refs, token_rows
        ctx.callback = callback
        ctx.save_for_backward(hidden, targets, maxima, log_totals)
        return loss

    @staticmethod
    @once_differentiable
    def backward(ctx, grad_loss):
        hidden, targets, maxima, log_totals = ctx.saved_tensors
        count, device = hidden.shape[0], hidden.device
        # Autograd engine threads need an explicit captured callback; Python
        # ContextVars are not assumed to propagate to those threads.
        with compute_scope(ctx.callback):
            check_compute(device=device)
            gradient = torch.zeros_like(hidden)
            for ref in ctx.refs:
                check_compute(device=device)
                weight = _materialize(ctx.store, ref, device=device, dtype=hidden.dtype)
                lo, hi = ref.row_start, ref.row_start + ref.shape[0]
                for start, end in _chunks(count, ctx.token_rows):
                    check_compute(device=device)
                    scores = F.linear(hidden[start:end], weight).float()
                    probabilities = torch.exp((scores - maxima[start:end, None])
                                              - log_totals[start:end, None])
                    labels = targets[start:end]
                    matches = (labels >= lo) & (labels < hi)
                    local_rows = torch.where(matches)[0]
                    probabilities[local_rows, labels[local_rows] - lo] -= 1
                    probabilities.mul_(grad_loss.float() / count)
                    gradient[start:end].add_(probabilities.to(hidden.dtype) @ weight)
                del weight
            check_compute(device=device)
        return gradient, None, None, None, None


def streaming_cross_entropy(hidden, head, targets, *, token_rows=128):
    """Mean CE over nonignored targets with gradients only into hidden states.

    The float32 loss follows the installed Transformers CausalLM loss's upcast.
    Supported compute is FP32/FP64; higher-order derivatives are refused.
    """
    if (type(head) is not PagedLinear or not isinstance(hidden, torch.Tensor)
            or hidden.ndim < 2 or hidden.layout != torch.strided
            or hidden.dtype not in (torch.float32, torch.float64)
            or hidden.shape[-1] != head.in_features
            or hidden.device != head.device or hidden.dtype != head.dtype
            or not isinstance(targets, torch.Tensor) or targets.dtype != torch.long
            or targets.shape != hidden.shape[:-1] or targets.device != hidden.device):
        raise ValueError("Streaming loss requires matching hidden/target tensors and a frozen paged head")
    if type(token_rows) is not int or not 1 <= token_rows <= 4096:
        raise ValueError("Loss token rows must be a bounded positive integer")
    if head.chunk_rows * token_rows * max(4, hidden.element_size()) > 16 * 1024 * 1024:
        raise ValueError("The output-head score tile exceeds its 16 MiB bound")
    check_compute(device=hidden.device)
    flat_targets = targets.reshape(-1)
    valid = flat_targets != -100
    if not bool(valid.any()):
        raise ValueError("At least one supervised next-token target is required")
    if bool(((flat_targets[valid] < 0) | (flat_targets[valid] >= head.out_features)).any()):
        raise ValueError("Supervised target is outside the admitted output vocabulary")
    values = hidden.reshape(-1, hidden.shape[-1])[valid]
    labels = flat_targets[valid]
    # PagedLinear constructs contiguous, ordered row ranges covering its matrix.
    refs = head._refs
    if (not refs or refs[0].row_start != 0
            or sum(ref.shape[0] for ref in refs) != head.out_features
            or any(a.row_start + a.shape[0] != b.row_start for a, b in zip(refs, refs[1:]))):
        raise ValueError("Paged output-head references do not cover the vocabulary exactly")
    return _StreamingCrossEntropy.apply(values, labels, head.store, refs, token_rows)


def native_causal_loss(model, *, input_ids, attention_mask, labels, token_rows=128):
    """Use the unchanged native decoder and its optional router auxiliary loss."""
    from transformers.models.qwen4_exp.modeling_qwen4_exp import (
        Qwen4ExpForCausalLM, load_balancing_loss_func,
    )
    if type(model) is not Qwen4ExpForCausalLM or type(model.lm_head) is not PagedLinear:
        raise ValueError("Streamed training loss requires the mapped native Qwen4Exp text model")
    if (not isinstance(labels, torch.Tensor) or labels.ndim != 2
            or labels.shape != input_ids.shape or labels.shape[1] < 2):
        raise ValueError("Causal training requires matching token sequences of length at least 2")
    check_compute(device=model.lm_head.device)
    router_loss = bool(model.config.output_router_logits)
    outputs = model.model(input_ids=input_ids, attention_mask=attention_mask,
                          use_cache=False, output_router_logits=router_loss, return_dict=True)
    loss = streaming_cross_entropy(outputs.last_hidden_state[:, :-1], model.lm_head,
                                   labels[:, 1:], token_rows=token_rows)
    if router_loss:
        auxiliary = load_balancing_loss_func(outputs.router_logits, model.num_experts,
                                            model.num_experts_per_tok, attention_mask)
        loss = loss + model.router_aux_loss_coef * auxiliary.to(loss.device)
    return loss
