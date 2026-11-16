"""Explicit local model/data admission; no automatic downloads or device choice."""

__all__ = ["LocalBundle", "LocalSession", "attach_lora", "make_software_model"]


def __getattr__(name):
    # Permit a registered CPU worker to install its refusal guard before any
    # transformers/PEFT import, including `python -m ...` package resolution.
    if name in __all__:
        from . import adapter
        return getattr(adapter, name)
    raise AttributeError(name)
