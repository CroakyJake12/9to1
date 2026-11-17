"""Restore only tracked Avalonia/Bun-generated maps after real SDK publication.

The ordinary build deliberately uses the newly generated maps until the SDK
copies its public assets. This helper restores the original *tracked* source
bytes afterwards, without touching the independently published copy or hiding
any other tracked change. It never runs Git cleanup commands.
"""

import os
from pathlib import Path
import stat
import tempfile


TRACKED_MAPS = (
    "framework/CUI/vendor/Avalonia/src/Browser/Avalonia.Browser/staticwebassets/avalonia.js.map",
    "framework/CUI/vendor/Avalonia/src/Browser/Avalonia.Browser/staticwebassets/storage.js.map",
    "framework/CUI/vendor/Avalonia/src/Browser/Avalonia.Browser/staticwebassets/sw.js.map",
)


def _regular(path):
    details = path.lstat()  # Missing paths and symlinks are never repaired silently.
    if not stat.S_ISREG(details.st_mode):
        raise RuntimeError(f"Tracked Bun map is not a regular file: {path}")
    return details


def snapshot_generated_maps(root):
    """Capture the exact three original regular-file bytes and permission bits."""
    root = Path(root)
    originals = {}
    for relative in TRACKED_MAPS:
        path = root / relative
        details = _regular(path)  # Verify *before* reading; never follow a link.
        originals[relative] = (path.read_bytes(), stat.S_IMODE(details.st_mode))
    return originals


def restore_generated_maps(root, originals):
    """Restore only whitelisted maps, rejecting replaced/missing files first.

    The caller must independently verify its full Git tree after this succeeds.
    Public SDK assets must already have been copied before calling this method.
    """
    if type(originals) is not dict or set(originals) != set(TRACKED_MAPS):
        raise RuntimeError("Expected precisely the three tracked generated maps")
    root = Path(root)
    # Preflight every target before the first write, so one missing/symlink map
    # never causes a partial cleanup of the other two.
    for relative, original in originals.items():
        if type(original) is not tuple or len(original) != 2 or type(original[0]) is not bytes or type(original[1]) is not int:
            raise RuntimeError("Invalid tracked map snapshot")
        _regular(root / relative)
    for relative, (body, original_mode) in originals.items():
        path = root / relative
        if path.read_bytes() == body and stat.S_IMODE(_regular(path).st_mode) == original_mode:
            continue
        descriptor, temporary = tempfile.mkstemp(prefix=f".{path.name}.9to1-restore-", dir=path.parent)
        try:
            with os.fdopen(descriptor, "wb") as writer:
                writer.write(body)
                writer.flush()
                os.fchmod(writer.fileno(), original_mode)
                os.fsync(writer.fileno())
            # Atomic in-directory replacement. No git checkout/reset; unrelated
            # changes remain visible to the caller's mandatory clean check.
            _regular(path)
            os.replace(temporary, path)
        finally:
            if os.path.lexists(temporary):
                os.unlink(temporary)
    for relative, (body, original_mode) in originals.items():
        path = root / relative
        if path.read_bytes() != body or stat.S_IMODE(_regular(path).st_mode) != original_mode:
            raise RuntimeError(f"Tracked Bun map was not restored: {relative}")
