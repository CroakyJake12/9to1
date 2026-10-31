#!/usr/bin/env python3
"""Capture a real on-screen X11 window to a PNG, with the evidence metadata the record needs.

This exists because "it compiled" and "a headless test passed" are not on-screen evidence. The capture reads the
mapped window's pixels off the running X server, so the frame proves the surface was actually displayed.

Nothing is invented: if the window is missing, unmapped or zero-sized, the tool exits with that as the result and
writes nothing, rather than producing a plausible image.
"""
from __future__ import annotations

import argparse
import hashlib
import os
import json
import re
import subprocess
import sys
import time
from pathlib import Path

from PIL import Image
from Xlib import X
from Xlib import error as Xerror
from Xlib.display import Display


def find_window(dpy, wanted: str):
    """Locate a mapped client window whose WM_NAME contains `wanted` (case-insensitive)."""
    stack, results = [dpy.screen().root], []
    while stack:
        window = stack.pop()
        try:
            name = window.get_wm_name()
        except (Xerror.XError, ValueError):
            name = None
        try:
            attributes = window.get_attributes()
        except Xerror.XError:
            attributes = None
        if name and wanted.lower() in str(name).lower():
            geometry = window.get_geometry()
            if attributes is not None and attributes.map_state == X.IsViewable and geometry.width > 50:
                # Measured defect: this WM reparents and both the frame and the client carry the same WM_NAME,
                # so an unfiltered match captured the decoration frame (a different pixel size, which throws off
                # every coordinate derived from the frame). The client window is preferred when it can be told.
                try:
                    klass = " ".join(str(part) for part in (window.get_wm_class() or ()))
                except (Xerror.XError, ValueError):
                    klass = ""
                results.append((window, str(name), geometry, klass))
        try:
            stack.extend(window.query_tree().children)
        except Xerror.XError:
            continue
    return results


def capture(name_filter: str, out: Path, surface: str = "") -> dict[str, object]:
    dpy = Display(None)                   # DISPLAY from the environment, exactly as the session sees it
    found = find_window(dpy, name_filter)
    if not found:
        raise SystemExit(f"NOT CAPTURED: no mapped viewable window matching {name_filter!r} on this display")
    clients = [hit for hit in found if hit[3] and "mutter" not in hit[3].lower()]
    window, title, geometry, klass = (clients or found)[0]
    window_id = window.id
    width, height = geometry.width, geometry.height
    # Measured defect of the first version: it decoded every capture as 3-byte RGB with a zero stride. X pads
    # pixmap rows to a 4-byte boundary and Avalonia's X11 surface is 32-bit BGRX, so the decode was off by a
    # whole pixel column - the published frame came out as tiled repeats of itself. The depth and row stride now
    # come from the reply that X actually sent, and a mismatch is an error rather than a plausible picture.
    raw = window.get_image(0, 0, width, height, X.ZPixmap, 0xFFFFFFFF)
    depth = raw.depth
    row_stride = ((width * depth + 31) // 32) * 4
    data = bytes(raw.data)
    if depth == 32:
        image = Image.frombytes("RGB", (width, height), data, "raw", "BGRX", row_stride, 1)
    elif depth == 24:
        image = Image.frombytes("RGB", (width, height), data, "raw", "BGR", row_stride, 1)
    elif depth == 16:
        image = Image.frombytes("RGB", (width, height), data, "raw", "BGR;16", row_stride, 1)
    else:
        raise SystemExit(f"NOT CAPTURED: unsupported pixmap depth {depth} on this display")
    if len(data) < row_stride * (height - 1) + width * (depth // 8):
        raise SystemExit(f"NOT CAPTURED: X returned {len(data)} bytes, the frame needs at least "
                         f"{row_stride * (height - 1) + width * (depth // 8)}")
    out.parent.mkdir(parents=True, exist_ok=True)
    image.save(out)
    dpy.sync()
    pid = subprocess.run(["xprop", "-id", hex(window_id), "_NET_WM_PID"], capture_output=True,
                         text=True).stdout.strip()
    return {
        "png": str(out),
        "surface": surface,
        "window_id": hex(window_id),
        "window_title": title,
        "window_class": klass,
        "pixels": [width, height],
        "pixmap_depth": depth, "row_stride_bytes": row_stride, "reply_bytes": len(data),
        "sha256": hashlib.sha256(out.read_bytes()).hexdigest(),
        "captured_at": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        "display_env": os.environ.get("DISPLAY", "unset"),
        "pid": pid,
        "evidence_level": "on-screen capture from the running X server: the surface was displayed; this does NOT "
                          "assert the behaviour is correct, complete or accepted",
    }


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--window", required=True, help="substring of the WM_NAME to capture")
    ap.add_argument("--out", required=True)
    ap.add_argument("--surface", default="", help="label recorded with the capture, e.g. Boards (new CUI)")
    args = ap.parse_args(argv)
    record = capture(args.window, Path(args.out), args.surface)
    print(json.dumps(record, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
