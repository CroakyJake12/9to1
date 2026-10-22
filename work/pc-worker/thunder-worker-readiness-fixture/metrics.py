"""Synthetic benchmark-summary readiness fixture; deliberately defective baseline."""

def median_measurement(values):
    ordered = sorted(value for value in values if value)
    return ordered[len(ordered) // 2]
