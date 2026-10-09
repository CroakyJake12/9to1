#!/usr/bin/env python3
"""Verify Chromium's src repository; DEPS/gclient dependency sync is a separate step."""
import argparse
import json
from pathlib import Path
import subprocess
import sys

from verify_firefox_source import verify

SOURCE_PATH = "9to1 Workspace/Browse/Source/Chromium/src"
SOURCE_URL = "https://chromium.googlesource.com/chromium/src.git"
REQUIRED_FILES = (
    "LICENSE", "DEPS", "BUILD.gn", "chrome/app/chrome_main.cc",
    "components/bookmarks/browser/bookmark_model.cc",
    "components/download/internal/common/download_item_impl.cc",
    "third_party/blink/renderer/core/BUILD.gn",
)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", type=Path)
    args = parser.parse_args()
    try:
        location = Path(__file__).resolve()
        lock = json.loads(location.with_name("chromium-source.lock.json").read_text(encoding="utf-8"))
        evidence = verify(location.parents[3], lock, expected_path=SOURCE_PATH,
                          expected_url=SOURCE_URL, required_files=REQUIRED_FILES, top_level_only=True)
        text = json.dumps(evidence, indent=2) + "\n"
        if args.evidence:
            args.evidence.parent.mkdir(parents=True, exist_ok=True)
            args.evidence.write_text(text, encoding="utf-8")
        print(text, end="")
        return 0
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        print(f"Chromium source verification FAILED: {error}", file=sys.stderr)
        if isinstance(error, subprocess.CalledProcessError) and error.stderr:
            print(error.stderr, file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
