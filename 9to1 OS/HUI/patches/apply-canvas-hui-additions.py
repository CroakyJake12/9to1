#!/usr/bin/env python3
"""CakeOS HUI additions for the Canvas application (donor-safe patch).

Appends Canvas-needed line icons to HavenIconCatalog and completes the
HavenKey alphabet, using the exact geometry/primitives style of the donor.
Idempotent and fail-closed: refuses to run when the pinned donor context
changed, so a donor bump surfaces as a build error instead of silent drift.

Runs automatically from HUI/build-linux-host.sh and
tests/windows/publish-windows.ps1 after donor staging.
"""
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[2]
catalog = root / "HUI" / "vendor" / "Haven.UI" / "Drawing" / "HavenIconCatalog.cs"
router = root / "HUI" / "vendor" / "Haven.UI" / "Input" / "HavenInputRouter.cs"

for target in (catalog, router):
    if not target.is_file():
        raise SystemExit(f"Missing staged HUI file: {target}")

ICON_ANCHOR = """        _ => Geometry(new HavenPathFigure(new HavenPoint(4, 4),"""

ICON_ENTRIES = """        "pen" => Geometry(new HavenPathFigure(new HavenPoint(4, 20), [new HavenLineSegment(new HavenPoint(8, 12)), new HavenLineSegment(new HavenPoint(12, 16))], true), Line((10, 14), (19, 5)), Line((6.5, 17.5), (9, 15))),        "highlighter" => Geometry(new HavenPathFigure(new HavenPoint(3, 17), [new HavenLineSegment(new HavenPoint(5, 15)), new HavenLineSegment(new HavenPoint(13, 7)), new HavenLineSegment(new HavenPoint(17, 11)), new HavenLineSegment(new HavenPoint(9, 19))], true), Line((14, 4), (20, 10)), Line((3, 21), (8, 21))),
        "eraser" => Geometry(new HavenPathFigure(new HavenPoint(6, 13), [new HavenLineSegment(new HavenPoint(11, 8)), new HavenLineSegment(new HavenPoint(18, 15)), new HavenLineSegment(new HavenPoint(13, 20)), new HavenLineSegment(new HavenPoint(8, 20))], true), Line((4, 21), (10, 21))),
        "select" => Geometry(new HavenPathFigure(new HavenPoint(7, 3), [new HavenLineSegment(new HavenPoint(17, 12)), new HavenLineSegment(new HavenPoint(12, 12)), new HavenLineSegment(new HavenPoint(14, 19)), new HavenLineSegment(new HavenPoint(11, 20)), new HavenLineSegment(new HavenPoint(9, 13)), new HavenLineSegment(new HavenPoint(5, 16))], true)),
        "shape" => Geometry(new HavenPathFigure(new HavenPoint(3, 6), [new HavenLineSegment(new HavenPoint(12, 6)), new HavenLineSegment(new HavenPoint(12, 15)), new HavenLineSegment(new HavenPoint(3, 15))], true), Circle(16.5, 15.5, 4)),
        "rect" or "rectangle" => Geometry(new HavenPathFigure(new HavenPoint(5, 7), [new HavenLineSegment(new HavenPoint(19, 7)), new HavenLineSegment(new HavenPoint(19, 17)), new HavenLineSegment(new HavenPoint(5, 17))], true)),
        "ellipse" => Geometry(Circle(12, 12, 7)),
        "line" => Geometry(Line((5, 19), (19, 5))),
        "arrow" => Geometry(Line((5, 19), (19, 5)), Line((12, 5), (19, 5), (19, 12))),
        "undo" => Geometry(Line((9, 5), (4, 10), (9, 15)), Line((4, 10), (18, 10))),
        "redo" => Geometry(Line((15, 5), (20, 10), (15, 15)), Line((20, 10), (6, 10))),
        "fit" => Geometry(Line((4, 9), (4, 4), (9, 4)), Line((15, 4), (20, 4), (20, 9)), Line((20, 15), (20, 20), (15, 20)), Line((9, 20), (4, 20), (4, 15))),
        "save" => Geometry(new HavenPathFigure(new HavenPoint(5, 4), [new HavenLineSegment(new HavenPoint(19, 4)), new HavenLineSegment(new HavenPoint(19, 20)), new HavenLineSegment(new HavenPoint(5, 20))], true), Line((9, 4), (9, 9), (15, 9), (15, 4)), Line((8, 20), (8, 15), (16, 15), (16, 20))),
        "folder-open" => Geometry(new HavenPathFigure(new HavenPoint(3, 9), [new HavenLineSegment(new HavenPoint(21, 9)), new HavenLineSegment(new HavenPoint(21, 19)), new HavenLineSegment(new HavenPoint(3, 19))], true), Line((3, 9), (9, 9), (12, 5), (21, 5))),
"""

ZOOM_ENTRIES = """        "zoom-in" => Geometry(Circle(11, 11, 7), Line((11, 8), (11, 14)), Line((8, 11), (14, 11)), Line((16, 16), (21, 21))),
        "zoom-out" => Geometry(Circle(11, 11, 7), Line((8, 11), (14, 11)), Line((16, 16), (21, 21))),
"""

KEY_OLD = "public enum HavenKey { Unknown, Enter, Space, Escape, Tab, Left, Right, Up, Down, Home, End, Backspace, Delete, A, C, D, F, V, X, Y, Z }"
KEY_NEW = "public enum HavenKey { Unknown, Enter, Space, Escape, Tab, Left, Right, Up, Down, Home, End, Backspace, Delete, A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z }"


def patch_icons() -> None:
    source = catalog.read_text(encoding="utf-8")
    if '"zoom-in" => Geometry(' in source:
        print("Canvas HUI icon additions already applied.")
        return
    if '"pen" => Geometry(' not in source:
        if ICON_ANCHOR not in source:
            raise SystemExit("Pinned HUI icon-catalog context changed; refusing to apply icon patch.")
        source = source.replace(ICON_ANCHOR, ICON_ENTRIES + ICON_ANCHOR, 1)
        catalog.write_text(source, encoding="utf-8")
    source = catalog.read_text(encoding="utf-8")
    if ICON_ANCHOR not in source:
        raise SystemExit("Pinned HUI icon-catalog context changed; refusing to apply zoom icon patch.")
    catalog.write_text(source.replace(ICON_ANCHOR, ZOOM_ENTRIES + ICON_ANCHOR, 1), encoding="utf-8")
    print("Applied Canvas HUI icon additions.")


def patch_keys() -> None:
    source = router.read_text(encoding="utf-8")
    if KEY_NEW in source:
        print("Canvas HUI key-alphabet addition already applied.")
        return
    if KEY_OLD not in source:
        raise SystemExit("Pinned HUI input-router context changed; refusing to apply key patch.")
    router.write_text(source.replace(KEY_OLD, KEY_NEW, 1), encoding="utf-8")
    print("Applied Canvas HUI key-alphabet addition.")


patch_icons()
patch_keys()
