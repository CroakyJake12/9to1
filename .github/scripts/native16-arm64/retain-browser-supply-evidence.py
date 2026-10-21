#!/usr/bin/env python3
"""Copy ONLY bounded raw browser-supply diagnostics into a durable evidence dir.

The workbook runs this on ``always()`` so acquisition/verification failure logs
survive the runner even when the success-only sidecar export never happens. It
copies only small top-level ``.json``/``.log`` receipts from the known supplier
temp evidence dir — never the ``source/``, ``providers/``, ``deb/`` extract or
``archive/`` trees. The original nonzero failure outcome is preserved (this helper
never fabricates success).
"""
import argparse
import datetime
import json
import shutil
import sys
from pathlib import Path


def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def retain(source: Path, dest: Path) -> list:
    dest.mkdir(parents=True, exist_ok=True)
    copied = []
    if not source.is_dir():
        copied = []
    else:
        for pattern in ("*.json", "*.log"):
            for f in sorted(source.glob(pattern)):
                if f.is_file():
                    shutil.copy2(f, dest / f.name)
                    copied.append(f.name)
    (dest / "browser-supply-retained.json").write_text(
        json.dumps({"schema": "9to1.chromium-native.supply-ci.retained/v1",
                    "at": now(), "copied": copied}, indent=2) + "\n")
    return copied


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--source", type=Path, required=True,
                    help="RUNNER_TEMP/9to1-browser-evidence")
    ap.add_argument("--dest", type=Path, required=True,
                    help="$GITHUB_WORKSPACE/evidence/browser-supply")
    args = ap.parse_args(argv)
    retain(args.source, args.dest)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
