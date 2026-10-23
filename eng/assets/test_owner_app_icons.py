#!/usr/bin/env python3
"""Check icon-source integrity, not native UI acceptance.

Run after integration: python3 -B eng/assets/test_owner_app_icons.py
Private proposal review may supply --baseline for unchanged owner assets. This never
replaces production Bitmap decoding, real appearance checks or visual acceptance.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys
import unittest
import xml.etree.ElementTree as ET
import zlib

REPO = Path(__file__).resolve().parents[2]
BASELINE = None


def png_size(data):
    if not data.startswith(b"\x89PNG\r\n\x1a\n"):
        raise ValueError("PNG signature missing")
    position, chunks, size = 8, [], None
    while position < len(data):
        if position + 12 > len(data):
            raise ValueError("truncated PNG chunk")
        length = struct.unpack_from(">I", data, position)[0]
        end = position + 12 + length
        if end > len(data):
            raise ValueError("truncated PNG payload")
        kind = data[position + 4:position + 8]
        content = data[position + 8:position + 8 + length]
        crc = struct.unpack_from(">I", data, end - 4)[0]
        if zlib.crc32(kind + content) & 0xffffffff != crc:
            raise ValueError("PNG CRC mismatch")
        if not chunks:
            if kind != b"IHDR" or length != 13:
                raise ValueError("PNG IHDR missing")
            width, height, depth, colour, compression, filtering, interlace = struct.unpack(">IIBBBBB", content)
            if not (1 <= width <= 1254 and 1 <= height <= 1254 and depth == 8 and colour == 6
                    and compression == filtering == interlace == 0):
                raise ValueError("unexpected owner-icon PNG format")
            size = (width, height)
        if kind == b"IEND" and (length or end != len(data)):
            raise ValueError("unexpected trailing PNG data")
        chunks.append(kind)
        position = end
    if chunks[-1:] != [b"IEND"] or b"IDAT" not in chunks:
        raise ValueError("incomplete PNG")
    return size


def inspect_master(data):
    if b"<!DOCTYPE" in data or b"<!ENTITY" in data:
        raise ValueError("external SVG declaration")
    root = ET.fromstring(data)
    if root.tag != "{http://www.w3.org/2000/svg}svg" or root.attrib.get("viewBox") != "0 0 128 128":
        raise ValueError("unexpected SVG root")
    allowed = {"svg", "defs", "linearGradient", "stop", "rect", "path", "circle"}
    for element in root.iter():
        if element.tag.removeprefix("{http://www.w3.org/2000/svg}") not in allowed:
            raise ValueError("unsupported artwork element")
        for name, value in element.attrib.items():
            if name.lower().endswith("href") or "http:" in value or "https:" in value or "file:" in value:
                raise ValueError("external SVG reference")
    return root


class OwnerIconIntegrityTests(unittest.TestCase):
    def test_all_packaged_artwork_matches_provenance_and_png_integrity(self):
        manifest = json.loads((REPO / "docs/assets/owner-app-icon-completion-20261023.json").read_text())
        self.assertEqual(42, len(manifest["packaged_pngs"]))
        for asset in manifest["packaged_pngs"]:
            with self.subTest(path=asset["path"]):
                path = REPO / asset["path"]
                if not path.is_file() and BASELINE is not None and asset["origin"] == "owner-original":
                    path = BASELINE / asset["path"]
                data = path.read_bytes()
                self.assertEqual(asset["bytes"], len(data))
                self.assertEqual(asset["sha256"], hashlib.sha256(data).hexdigest())
                self.assertEqual(tuple(asset["pixels"]), png_size(data))

    def test_authored_masters_are_self_contained_and_pinned(self):
        manifest = json.loads((REPO / "docs/assets/owner-app-icon-completion-20261023.json").read_text())
        self.assertEqual(12, len(manifest["svg_masters"]))
        for asset in manifest["svg_masters"]:
            with self.subTest(path=asset["path"]):
                data = (REPO / asset["path"]).read_bytes()
                self.assertEqual(asset["sha256"], hashlib.sha256(data).hexdigest())
                inspect_master(data)

    def test_truncated_and_corrupted_pngs_are_rejected(self):
        path = REPO / "9to1 Workspace/shared/src/Haven.Desktop/Assets/OwnerAppIcons/dark/terminal.png"
        data = path.read_bytes()
        with self.assertRaises(ValueError):
            png_size(data[:-1])
        corrupt = bytearray(data)
        corrupt[-5] ^= 1
        with self.assertRaises(ValueError):
            png_size(corrupt)

    def test_external_artwork_references_are_rejected(self):
        with self.assertRaises(ValueError):
            inspect_master(b'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128"><image href="https://invalid.example/a.png"/></svg>')


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=REPO)
    parser.add_argument("--baseline", type=Path, help="Read unchanged owner assets from this explicit private-review baseline")
    args, remaining = parser.parse_known_args()
    REPO, BASELINE = args.repo.resolve(), args.baseline.resolve() if args.baseline else None
    unittest.main(argv=[sys.argv[0], *remaining])
