#!/usr/bin/env python3
"""G01 native interactive battery (ATLAS-20261120).

Run AFTER the desktop session restart (input was server-dead on 21 Nov morning - see GitHub
issue #40). Requires: the owned dev copy running on :1 (live-preview), the x11 venv, and DISPLAY
writable. Every step captures evidence; prints a PASS/FAIL table; NEVER kills anything.

usage: ~/.venvs/x11/bin/python eng/development/native_battery_g01.py
"""
import os
import sys
import time
import hashlib

os.environ.setdefault("DISPLAY", ":1")
from Xlib import display as xdisplay
from Xlib import X, XK
from Xlib.ext import xtest
from PIL import Image

EVID = sys.argv[1] if len(sys.argv) > 1 else "docs/development/evidence/atlas-20261120"
d = xdisplay.Display()
root = d.screen().root

def find_window(name="9to1"):
    cl = root.get_full_property(d.intern_atom("_NET_CLIENT_LIST"), X.AnyPropertyType).value or []
    for w in cl:
        win = d.create_resource_object("window", w)
        try:
            if str(win.get_wm_name() or "") == name:
                return win
        except Exception:
            pass
    return None

def origin(win):
    # mutter pins this window's frame at screen top-left; the client renders at (0, ~74).
    # translate_coords is unreliable under this display's scaling, so trust the visible frame.
    return int(os.environ.get("BATTERY_ORIGIN_X", "0")), int(os.environ.get("BATTERY_ORIGIN_Y", "74"))

def capture(win, tag):
    g = win.get_geometry()
    img = win.get_image(0, 0, g.width, g.height, X.ZPixmap, 0xFFFFFFFF)
    path = os.path.join(EVID, f"g01b-{tag}.png")
    os.makedirs(EVID, exist_ok=True)
    Image.frombytes("RGB", (g.width, g.height), bytes(img.data), "raw", "BGRX").save(path)
    return hashlib.sha256(bytes(img.data)).hexdigest()

def key(name, *mods):
    codes = [d.keysym_to_keycode(XK.string_to_keysym(m)) for m in mods] + [d.keysym_to_keycode(XK.string_to_keysym(name))]
    for c in codes:
        xtest.fake_input(d, X.KeyPress, c)
    d.flush(); time.sleep(0.08)
    for c in reversed(codes):
        xtest.fake_input(d, X.KeyRelease, c)
    d.flush(); time.sleep(1.4)

def click(win, x, y):
    ox, oy = origin(win)
    root.warp_pointer(ox + x, oy + y)
    d.sync(); time.sleep(0.25)
    xtest.fake_input(d, X.ButtonPress, 1); d.flush(); time.sleep(0.12)
    xtest.fake_input(d, X.ButtonRelease, 1); d.flush(); time.sleep(2.6)

def sanity(win):
    """Input is only trustworthy if a keystroke visibly changes the app."""
    h0 = capture(win, "sanity-a")
    key("Tab")
    h1 = capture(win, "sanity-b")
    return h0 != h1

results = []
def record(step, ok, note=""):
    results.append((step, "PASS" if ok else "FAIL", note))
    print(f"{'PASS' if ok else 'FAIL':4} {step} {note}")

win = find_window()
if win is None:
    print("FAIL no 9to1 window - start the dev copy first (live_preview start)"); sys.exit(2)
ox, oy = origin(win)
if ox < 0 or oy < 0:
    print(f"FAIL window stranded at ({ox},{oy}) - session still broken or pre-fix build"); sys.exit(3)
win.set_input_focus(X.RevertToParent, X.CurrentTime); d.sync(); time.sleep(0.4)

record("input-reaches-app", sanity(win), "(Tab moved focus visibly)")
capture(win, "00-initial")

# 1) tab strip: click each tab label, expect content change (client y of strip ~ top 40px; measure
#    from TopRail height 74 frame + row; coordinates are client-relative physical px)
strip_y = 46
h_before = capture(win, "10-tab-a")
click(win, 200, strip_y)
h_after = capture(win, "11-tab-b")
record("tab-click-changes", h_before != h_after, "clicked first tab area")

# 2) plus button New tab: TopRail NewTabButton is at the right end of the tab row; find its x by
#    scanning right-to-left is overkill - click the standard + at strip right edge (Atlas: after last)
w = win.get_geometry().width
h_before = capture(win, "12-before-plus")
click(win, w - 60, strip_y)
h_after = capture(win, "13-after-plus")
record("plus-newtab", h_before != h_after, "clicked plus area")

# 3) keyboard Ctrl+T new tab
count_before = len([1 for wl in (root.get_full_property(d.intern_atom('_NET_CLIENT_LIST'), X.AnyPropertyType).value or [])])
h_before = capture(win, "14-before-ctrlt")
key("t", "Control_L")
h_after = capture(win, "15-after-ctrlt")
record("ctrl-t-newtab", h_before != h_after, "(visual change; cross-check tab count in capture)")

# 4) Home surface: click the restored 'Home' tab then check widgets/clock/search render
h_before = capture(win, "16-before-home")
click(win, 420, strip_y)
h_after = capture(win, "17-after-home")
record("home-tab-click", h_before != h_after, "verify visually in 17-after-home.png")

# 5) tab-centring repro: capture a zoomed crop of the tab strip for the alignment audit
g = win.get_geometry()
img = win.get_image(0, 0, g.width, 90, X.ZPixmap, 0xFFFFFFFF)
Image.frombytes("RGB", (g.width, 90), bytes(img.data), "raw", "BGRX").save(os.path.join(EVID, "g01b-20-tabstrip-crop.png"))
record("tabstrip-crop-captured", True, "audit alignment vs HAV-TAB-002 (left icon/title, trailing X)")

print("\n== summary ==")
for step, status, note in results:
    print(f"{status:4} {step} {note}")
fails = [r for r in results if r[1] == "FAIL"]
sys.exit(1 if fails else 0)
