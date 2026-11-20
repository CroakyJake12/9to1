#!/usr/bin/env python3
"""Native evidence capture for the owned 9to1 dev window (Xlib+XTEST, user-space only).
usage documented in docs/development/live-preview.md evidence sections; added by the ATLAS window 2026-11-20.
usage: capture_window.py OUT.png [key CTRL+t] [click X Y]   (DISPLAY=:1)"""
import os, sys, time
os.environ["DISPLAY"] = ":1"
from Xlib import display, X, XK
from Xlib.ext import xtest
from PIL import Image

OUT = sys.argv[1]
d = display.Display()
root = d.screen().root
cl = root.get_full_property(d.intern_atom('_NET_CLIENT_LIST'), X.AnyPropertyType).value
target = None
for w in cl:
    win = d.create_resource_object('window', w)
    if '9to1' in str(win.get_wm_name() or ''):
        target = win
if target is None:
    print("NO 9to1 WINDOW"); sys.exit(1)
target.set_input_focus(X.RevertToParent, X.CurrentTime)
target.configure(stack_mode=X.Above)
d.sync()
time.sleep(0.4)

if len(sys.argv) > 2 and sys.argv[2] == "--click":
    ix, iy = float(sys.argv[3]), float(sys.argv[4])
    geo0 = target.get_geometry()
    print("window geometry:", geo0.width, "x", geo0.height, "| image px: same coordinate space (X11 physical)")
    origin = target.translate_coords(root, 0, 0)
    rx, ry = origin.x + int(ix), origin.y + int(iy)
    root.warp_pointer(rx, ry)
    d.sync()
    xtest.fake_input(d, X.MotionNotify, x=rx, y=ry)
    d.sync()
    time.sleep(0.2)
    xtest.fake_input(d, X.ButtonPress, 1)
    d.sync()
    xtest.fake_input(d, X.ButtonRelease, 1)
    d.sync()
    time.sleep(1.4)

if len(sys.argv) > 2 and sys.argv[2] == "--key":
    combo = sys.argv[3]
    parts = combo.lower().split('+')
    keycode_map = {"ctrl": XK.string_to_keysym("Control_L"), "alt": XK.string_to_keysym("Alt_L"),
                   "shift": XK.string_to_keysym("Shift_L")}
    keys = []
    for p in parts[:-1]:
        keys.append(keycode_map[p])
    keys.append(XK.string_to_keysym(parts[-1]))
    codes = [d.keysym_to_keycode(k) for k in keys]
    for c in codes:
        xtest.fake_input(d, X.KeyPress, c)
    d.sync()
    for c in reversed(codes):
        xtest.fake_input(d, X.KeyRelease, c)
    d.sync()
    time.sleep(1.2)

geo = target.get_geometry()
img_raw = target.get_image(0, 0, geo.width, geo.height, X.ZPixmap, 0xFFFFFFFF)
img = Image.frombytes("RGB", (geo.width, geo.height), bytes(img_raw.data), "raw", "BGRX")
img.save(OUT)
print("captured", OUT, geo.width, "x", geo.height)
