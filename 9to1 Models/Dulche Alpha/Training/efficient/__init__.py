"""CPU correctness boundary; no automatic device selection or filesystem writes."""

from .core import Config, CpuTrainer, LoRALinear, StepRecord

__all__ = ["Config", "CpuTrainer", "LoRALinear", "StepRecord"]
