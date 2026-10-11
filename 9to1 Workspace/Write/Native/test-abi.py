"""Exercise the actual compiled bridge without pretending a missing donor is present."""
import ctypes
import pathlib
import sys

bridge_path = pathlib.Path(sys.argv[1]).resolve(strict=True)
bridge = ctypes.CDLL(str(bridge_path))
bridge.nto_lok_abi_version.restype = ctypes.c_uint
bridge.nto_lok_donor_commit.restype = ctypes.c_char_p
bridge.nto_lok_last_error.restype = ctypes.c_char_p
bridge.nto_lok_create.argtypes = [ctypes.c_char_p, ctypes.c_char_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.POINTER(ctypes.c_void_p)]
bridge.nto_lok_create.restype = ctypes.c_int
bridge.nto_lok_destroy.argtypes = [ctypes.c_void_p]
bridge.nto_lok_destroy.restype = ctypes.c_int
assert bridge.nto_lok_abi_version() == 1
assert bridge.nto_lok_donor_commit() == b"d3fca73f81dfb19a72a0da41598b7390eaf5845b"
assert bridge.nto_lok_create(None, None, None, None, None) == 1
session = ctypes.c_void_p(123)
assert bridge.nto_lok_create(b"unavailable", b"https://example.invalid/profile", None, None, ctypes.byref(session)) == 1
assert session.value is None
assert bridge.nto_lok_destroy(None) == 1
missing = bridge_path.parent / "intentionally-absent-lok-runtime"
assert not missing.exists(), "Use a fresh missing runtime fixture path"
assert bridge.nto_lok_create(str(missing).encode(), (bridge_path.parent / "isolated-profile").as_uri().encode(), None, None, ctypes.byref(session)) == 3
assert session.value is None
assert b"could not initialise" in bridge.nto_lok_last_error()
print("PASS: 9 native ABI assertions; actual bridge loaded, invalid inputs and missing donor fail explicitly.")
print("NOT RUN: donor rendering, editing, IME, clipboard, undo, save/reopen, packaged CUI integration.")
