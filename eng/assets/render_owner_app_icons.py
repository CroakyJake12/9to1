#!/usr/bin/env python3
"""Render newly authored SVG masters; never edit or resample owner-provided PNG assets.

Requires Inkscape 1.4 (e7c3feb100, 2024-10-09) (the recorded export toolchain). SVGs use only bounded,
self-contained geometric shapes/gradients. Output is transparent 256px RGBA PNG.
"""
import argparse
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

STEMS = ("canvas-whiteboard", "build", "terminal", "voice-vision", "write", "wave")

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--check", action="store_true", help="Compare fresh renders without replacing packaged assets")
    args = parser.parse_args()
    tool = shutil.which("inkscape")
    if not tool:
        parser.error("Inkscape is unavailable; do not replace missing exports with placeholder artwork")
    version = subprocess.check_output([tool, "--version"], text=True, timeout=10).splitlines()[0]
    if version != "Inkscape 1.4 (e7c3feb100, 2024-10-09)":
        parser.error("Use the recorded Inkscape 1.4 export toolchain; review changed output before updating")
    assets = args.repo.resolve() / "9to1 Workspace/shared/src/Haven.Desktop/Assets/OwnerAppIcons"
    with tempfile.TemporaryDirectory(prefix="9to1-icon-render-") as tmp:
        env = dict(os.environ, INKSCAPE_PROFILE_DIR=str(Path(tmp) / "profile"),
                   XDG_CACHE_HOME=str(Path(tmp) / "cache"), XDG_CONFIG_HOME=str(Path(tmp) / "config"),
                   XDG_DATA_HOME=str(Path(tmp) / "data"))
        for folder in ("light", "dark"):
            for stem in STEMS:
                master = assets / "masters" / folder / (stem + ".svg")
                target = assets / folder / (stem + ".png")
                output = Path(tmp) / (folder + "-" + stem + ".png")
                # Rasterisation of our SVG source, not image editing of the owner's existing assets.
                subprocess.run([tool, "--export-type=png", "--export-area-page", "--export-width=256",
                                "--export-height=256", "--export-background-opacity=0",
                                "--export-filename=" + str(output), str(master)], env=env,
                               check=True, timeout=20, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
                content = output.read_bytes()
                if len(content) > 24 * 1024:
                    raise RuntimeError("Unexpected icon export size: " + str(output))
                if args.check:
                    if target.read_bytes() != content:
                        raise RuntimeError("Export differs from the pinned master/toolchain: " + str(target))
                else:
                    target.parent.mkdir(parents=True, exist_ok=True)
                    with target.open("xb") as stream:
                        stream.write(content)
                        stream.flush()
                        os.fsync(stream.fileno())
                    target.chmod(0o600)
                print(folder + "/" + stem + ": " + str(len(content)) + "B sha256=" + hashlib.sha256(content).hexdigest())

if __name__ == "__main__":
    main()
