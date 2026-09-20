#!/usr/bin/env python3
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[2]
target = root / "HUI" / "vendor" / "Haven.UI" / "Input" / "HavenInputRouter.cs"

if not target.is_file():
    raise SystemExit(f"Missing staged HUI input router: {target}")

source = target.read_text(encoding="utf-8")
old = """    public bool KeyUp(HavenKey key)\n    {\n        if (_focused is IHavenKeyboardInputTarget custom && custom.KeyUp(new HavenKeyInput(key, HavenKeyModifiers.None))) return true;\n        if (key == HavenKey.Tab) return true;\n"""
new = """    public bool KeyUp(HavenKey key)\n    {\n        if (_focused is IHavenKeyboardInputTarget custom)\n        {\n            var focusedBeforeKeyUp = _focused;\n            var wasPressed = focusedBeforeKeyUp.State.HasFlag(HavenElementState.Pressed);\n            if (custom.KeyUp(new HavenKeyInput(key, HavenKeyModifiers.None)))\n            {\n                // Canonical Button handles its own Enter/Space pressed state and Invoke(),\n                // so the router's generic Activate() path is not reached. Preserve pointer\n                // parity by executing the same declarative ClickActions after a successful\n                // keyboard activation, without invoking the button a second time.\n                if (focusedBeforeKeyUp is Button button\n                    && key is HavenKey.Enter or HavenKey.Space\n                    && wasPressed\n                    && !button.State.HasFlag(HavenElementState.Disabled))\n                    _actions.ExecuteClick(root, button);\n                return true;\n            }\n        }\n        if (key == HavenKey.Tab) return true;\n"""

if old not in source:
    if new in source:
        print("CakeOS keyboard click-action parity patch already applied.")
        sys.exit(0)
    raise SystemExit("Pinned HUI input-router context changed; refusing to apply migration patch.")

target.write_text(source.replace(old, new, 1), encoding="utf-8")
print("Applied CakeOS HUI keyboard click-action parity patch.")