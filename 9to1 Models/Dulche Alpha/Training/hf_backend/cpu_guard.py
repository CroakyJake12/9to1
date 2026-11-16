"""Fail-fast guard for dedicated CPU software-test processes only.

This is deliberate refusal, not a CUDA emulation or a GPU security boundary.
The supervisor must still apply resource admission and process ownership.
"""
import os


def install():
    if os.environ.get("CUDA_VISIBLE_DEVICES") != "-1":
        raise RuntimeError("CPU worker requires explicit CUDA_VISIBLE_DEVICES=-1 before imports")
    import torch
    if torch.cuda.is_initialized():
        raise RuntimeError("CPU worker already initialized CUDA")
    if hasattr(torch._C._cuda_init, "dulche_guard_calls"):
        return torch._C._cuda_init

    def refuse_cuda_init(*args, **kwargs):
        refuse_cuda_init.dulche_guard_calls += 1
        raise RuntimeError("CPU-only worker refused attempted CUDA initialization")

    refuse_cuda_init.dulche_guard_calls = 0
    torch._C._cuda_init = refuse_cuda_init
    torch.set_default_device("cpu")
    return refuse_cuda_init


def assert_cpu(model, batch):
    import torch
    if os.environ.get("CUDA_VISIBLE_DEVICES") != "-1" or torch.cuda.is_initialized():
        raise RuntimeError("CPU worker device policy changed")
    if any(value.device.type != "cpu" for value in (*model.parameters(), *model.buffers(), *batch.values())):
        raise RuntimeError("CPU fixture contains a non-CPU parameter, buffer or input")
