"""Read budgets that can pause an update without weakening source verification."""


class FingerprintCapacityExceeded(ValueError):
    """A new frozen-weight slice cannot fit in the current store's pin budget.

    Existing pins remain valid and are never evicted. A caller may checkpoint
    completed updates and resume with a freshly admitted store; this exception
    does not authorise retrying an interrupted optimizer mutation.
    """
