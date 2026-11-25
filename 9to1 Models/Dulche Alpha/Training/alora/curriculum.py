"""Owner-requested curriculum proportions; this module contains no training data."""

from __future__ import annotations

from collections.abc import Mapping
from types import MappingProxyType

CATEGORY_PERCENTAGES = MappingProxyType({
    "autonomous_recovery": 25,
    "ui": 25,
    "ux": 15,
    "proactive_reasoning": 15,
    "coding": 20,
})
CATEGORY_WEIGHTS = MappingProxyType({
    category: percentage / 100 for category, percentage in CATEGORY_PERCENTAGES.items()
})


def target_counts(total: int) -> dict[str, int]:
    """Allocate 5,000–10,000 examples exactly, with deterministic largest remainders."""
    if type(total) is not int or not 5_000 <= total <= 10_000:
        raise ValueError("The ALORA curriculum requires 5,000–10,000 examples.")
    counts = {category: total * percentage // 100
              for category, percentage in CATEGORY_PERCENTAGES.items()}
    order = sorted(CATEGORY_PERCENTAGES,
                   key=lambda category: -(total * CATEGORY_PERCENTAGES[category] % 100))
    for category in order[:total - sum(counts.values())]:
        counts[category] += 1
    return counts


def validate_counts(counts: Mapping[str, int]) -> dict[str, int]:
    """Check independently supplied category counts; never create or select examples."""
    if not isinstance(counts, Mapping) or set(counts) != set(CATEGORY_PERCENTAGES):
        raise ValueError("Curriculum counts must identify all five owner-requested categories.")
    if any(type(value) is not int or value < 0 for value in counts.values()):
        raise ValueError("Curriculum counts must be non-negative integers.")
    expected = target_counts(sum(counts.values()))
    if dict(counts) != expected:
        raise ValueError(f"Curriculum category counts differ from the required allocation: {expected}")
    return expected
