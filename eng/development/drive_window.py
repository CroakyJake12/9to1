#!/usr/bin/env python3
"""Drive a running 9to1 desktop window with synthetic X11 input, and record exactly what was sent.

This exists so on-screen behaviour can be exercised and captured rather than inferred from source reading. It
sends real button/key events to the client window and records each one with a timestamp, so a resulting frame can
be tied to a specific interaction.

It refuses to act when the target window cannot be found, and never guesses coordinates: every action names the
window id it was sent to.
"""
from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path

from Xlib import X, XK, error as Xerror
from Xlib.display import Display
from Xlib.ext import xtest
from Xlib import Xatom
from Xlib.protocol import event as xevent


def find_by_id(dpy: Display, window_id: str):
    """Address one window by its exact X id.

    Measured reason this exists: name matching returns the FIRST viewable window in depth-first order whose
    WM_NAME contains the substring. The Boards host has more than one such window, and capture_window.py and
    drive_window.py then disagree about which one is being looked at - clicks were reported as sent
    (window 0x420000f) while the capture read a different one (0x480000f), so the frame never changed and I
    nearly concluded the app ignored input. Addressing the exact id that the capture reported removes the
    ambiguity and makes the input receipt verifiable against the frame it is supposed to change.
    """
    wanted = int(window_id, 16)
    stack = [dpy.screen().root]
    while stack:
        window = stack.pop()
        if window.id == wanted:
            try:
                name = window.get_wm_name() or "".strip()
            except Exception:
                name = ""
            return window, f"{name or '(no name)'} [id {window_id}]"
        try:
            stack.extend(window.query_tree().children)
        except Xerror.XError:
            continue
    return None, None


def list_viewable(dpy: Display, wanted: str):
    """Print every viewable window whose name matches, with id and geometry, so the operator can choose."""
    found = []
    stack = [dpy.screen().root]
    while stack:
        window = stack.pop()
        try:
            name = window.get_wm_name() or ""
            attributes = window.get_attributes()
        except (Xerror.XError, ValueError):
            name, attributes = "", None
        if name and wanted.lower() in str(name).lower() and attributes and attributes.map_state == X.IsViewable:
            try:
                g = window.get_geometry()
                geom = (g.x, g.y, g.width, g.height)
            except Xerror.XError:
                geom = None
            found.append((f"0x{window.id:08x}", str(name), geom))
        try:
            stack.extend(window.query_tree().children)
        except Xerror.XError:
            continue
    for entry in found:
        print("MATCH", entry[0], entry[2], entry[1])
    return found


def find(dpy: Display, wanted: str, exclude: str | None = None):
    stack = [dpy.screen().root]
    while stack:
        window = stack.pop()
        try:
            name = window.get_wm_name()
            attributes = window.get_attributes()
        except (Xerror.XError, ValueError):
            name, attributes = None, None
        if exclude and exclude.lower() in str(name or "").lower():
            try:
                stack.extend(window.query_tree().children)
            except Xerror.XError:
                pass
            continue
        if name and wanted.lower() in str(name).lower() and attributes and attributes.map_state == X.IsViewable:
            try:
                klass = " ".join(str(part) for part in (window.get_wm_class() or ()))
            except (Xerror.XError, ValueError):
                klass = ""
            if any(marker in klass for marker in ("CakeOS", "Haven")) or "dotnet" in klass.lower():
                return window, f"{name} [{klass}]"
        try:
            stack.extend(window.query_tree().children)
        except Xerror.XError:
            continue
    return None, None


def keycode_for(dpy: Display, symbol: str) -> int:
    code = dpy.keysym_to_keycode(XK.string_to_keysym(symbol))
    if not code:
        raise SystemExit(f"REFUSED: no keycode for keysym {symbol!r} on this keyboard")
    return code


def absolute_origin(window, dpy: Display) -> tuple[int, int]:
    """Walk the window's parent chain summing geometry offsets.

    Measured defect of the first version: python-xlib's TranslateCoords reply gave the negated offset here, so
    clicks landed outside the client area and the frame came back unchanged. The manual walk is verifiable
    against `xwininfo -root -tree` output, which is how this tool's author checked it.
    """
    abs_x = abs_y = 0
    current = window
    root = dpy.screen().root
    guard = 0
    while current.id != root.id and guard < 12:
        info = current.get_geometry()
        abs_x += info.x
        abs_y += info.y
        parent = current.query_tree().parent
        if parent is None:
            break
        current = parent
        guard += 1
    return abs_x, abs_y


def click(dpy: Display, window, x: int, y: int, button: int = 1) -> tuple[int, int]:
    """XTEST moves the real pointer, so window-relative coordinates must be translated to root coordinates
    first. Passing them as-is would click somewhere else on the screen (measured: the target window sits at
    a negative absolute X when maximised on this display)."""
    abs_x, abs_y = absolute_origin(window, dpy)
    root_x = abs_x + x
    root_y = abs_y + y
    root_id = dpy.screen().root.id
    # Avalonia hit-tests from the pointer, so move it first: a button event at a position the pointer has
    # never visited can be delivered without the visual state the control needs.
    xtest.fake_input(dpy, X.MotionNotify, x=root_x, y=root_y, root=root_id)
    dpy.sync()
    time.sleep(0.2)
    xtest.fake_input(dpy, X.ButtonPress, button, x=root_x, y=root_y, root=root_id)
    dpy.sync()
    time.sleep(0.08)
    xtest.fake_input(dpy, X.ButtonRelease, button, x=root_x, y=root_y, root=root_id)
    dpy.sync()
    return root_x, root_y, abs_x, abs_y


def key(dpy: Display, window, combo: str) -> None:
    parts = [p.strip() for p in combo.split("+")]
    modifiers = {"ctrl": "Control_L", "shift": "Shift_L", "alt": "Alt_L"}
    held = [keycode_for(dpy, modifiers[m.lower()]) for m in parts[:-1]]
    keysym = parts[-1]
    press = keycode_for(dpy, keysym.lower() if len(keysym) == 1 else keysym)
    for code in held:
        xtest.fake_input(dpy, X.KeyPress, code)
    xtest.fake_input(dpy, X.KeyPress, press)
    dpy.sync()
    time.sleep(0.05)
    xtest.fake_input(dpy, X.KeyRelease, press)
    for code in reversed(held):
        xtest.fake_input(dpy, X.KeyRelease, code)
    dpy.sync()


def wm_close(dpy: Display, window) -> None:
    """Ask the window to close the way a window manager does, so the app runs its own save/drain path."""
    protocols = dpy.intern_atom("WM_PROTOCOLS")
    delete = dpy.intern_atom("WM_DELETE_WINDOW")
    window.send_event(xevent.ClientMessage(window=window.id, client_type=delete, data=(32, [delete, 0, 0, 0, 0])))
    dpy.sync()


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--window", help="substring of the WM_NAME to match")
    ap.add_argument("--window-id", help="exact X window id, e.g. 0x480000f; the id that capture_window reported")
    ap.add_argument("--list", action="store_true", help="list every viewable window matching --window and exit")
    ap.add_argument("--exclude", default=None, help="skip windows whose title contains this substring")
    ap.add_argument("--click", action="append", default=[], help="x,y in window pixels")
    ap.add_argument("--key", action="append", default=[], help='e.g. "ctrl+n"')
    ap.add_argument("--pause", type=float, default=1.2)
    ap.add_argument("--wm-close", action="store_true", help="send WM_DELETE_WINDOW instead of clicking")
    ap.add_argument("--place", default=None, help="x,y,width,height to move/resize the window to first")
    args = ap.parse_args(argv)

    dpy = Display(None)
    if not args.window and not args.window_id:
        raise SystemExit("REFUSED: pass --window or --window-id")
    if args.list:
        if not args.window:
            raise SystemExit("REFUSED: --list needs --window")
        found = list_viewable(dpy, args.window)
        raise SystemExit(0 if found else 3)
    if args.window_id:
        window, title = find_by_id(dpy, args.window_id)
    else:
        window, title = find(dpy, args.window, args.exclude)
    if window is None:
        print(f"NOT SENT: no mapped window matching {args.window!r}", file=sys.stderr)
        return 2
    if args.place:
        px, py, pw, ph = (int(part) for part in args.place.split(","))
        window.configure(x=px, y=py, width=pw, height=ph)
        dpy.sync()
        time.sleep(0.6)
    if args.wm_close:
        wm_close(dpy, window)
        print(json.dumps({"window_title": title, "sent": [{"action": "wm_delete_window",
              "window_id": hex(window.id)}], "evidence_level": "window-manager close request delivered"}, indent=2))
        return 0
    log = []
    for spec in args.click:
        x, y = (int(part) for part in spec.split(","))
        root_x, root_y, abs_x, abs_y = click(dpy, window, x, y)
        log.append({"action": "click", "window_id": hex(window.id), "window_xy": [x, y],
                    "root_xy": [root_x, root_y], "window_origin": [abs_x, abs_y]})
        time.sleep(args.pause)
    for combo in args.key:
        key(dpy, window, combo)
        log.append({"action": "key", "window_id": hex(window.id), "combo": combo})
        time.sleep(args.pause)
    print(json.dumps({"window_title": title, "sent": log,
                      "evidence_level": "synthetic input delivered to the running window; the resulting frame is "
                                        "what proves the effect"}, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
