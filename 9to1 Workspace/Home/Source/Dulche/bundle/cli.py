#!/usr/bin/env python3
"""Command-line entrypoint for the bundled Dulche packager/verifier.

Usage:
    bundle-ctl pack --spec spec.json --assets-root DIR --out DIR [--timestamp]
    bundle-ctl verify --install-root DIR --manifest MANIFEST.json [--no-strict]
"""
from __future__ import annotations

import argparse
import json
import pathlib
import sys

from . import VERSION
from .identity import IdentityError
from .manifest import parse_manifest
from .packager import pack_bundle
from .spec import load_spec
from .verifier import verify_installed


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="bundle-ctl", description="Bundled Dulche runtime/model-asset packager")
    parser.add_argument("--version", action="version", version=VERSION)
    sub = parser.add_subparsers(dest="command", required=True)

    pack = sub.add_parser("pack", help="Build a versioned manifest + receipt from supplied files")
    pack.add_argument("--spec", required=True, type=pathlib.Path)
    pack.add_argument("--assets-root", required=True, type=pathlib.Path)
    pack.add_argument("--out", required=True, type=pathlib.Path)
    pack.add_argument("--prefix", default="bundle")
    pack.add_argument("--timestamp", action="store_true", help="Include a generatedAtUtc audit field (non-deterministic)")

    verify = sub.add_parser("verify", help="Verify an installed package closure against a manifest")
    verify.add_argument("--install-root", required=True, type=pathlib.Path)
    verify.add_argument("--manifest", required=True, type=pathlib.Path)
    verify.add_argument("--no-strict", action="store_true", help="Skip unexpected-file closure check")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = _build_parser().parse_args(argv)
    try:
        if args.command == "pack":
            spec = load_spec(args.spec)
            result = pack_bundle(spec, args.assets_root, include_timestamp=args.timestamp)
            written = result.write(args.out, prefix=args.prefix)
            payload = {"status": "packed", "state": result.state, "outputs": written}
            payload["rejected"] = result.rejected
            print(json.dumps(payload, indent=2, sort_keys=True))
            return 0 if result.state in ("ready", "degraded") else 2
        if args.command == "verify":
            manifest = parse_manifest(json.loads(args.manifest.read_text(encoding="utf-8")), allow_invalid=True)
            result = verify_installed(args.install_root, manifest, strict_closure=not args.no_strict)
            statuses = [{"logical": s.logical, "state": s.state, "detail": s.detail} for s in result.asset_statuses]
            payload = {
                "status": "verified" if result.ok() else "not-verified",
                "state": result.state,
                "assets": statuses,
                "unexpectedPaths": result.unexpected_paths,
            }
            print(json.dumps(payload, indent=2, sort_keys=True))
            return 0 if result.ok() else 2
        return 2
    except (IdentityError, OSError, ValueError, json.JSONDecodeError) as exc:
        print(json.dumps({"status": "error", "detail": str(exc)}, separators=(",", ":")), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
