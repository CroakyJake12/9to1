#!/usr/bin/env python3
"""Measure which surfaces are authored in CUI and which are still foreign rendering.

The owner's rule, restated on 31 October 2026: "the main distinction is replace any foreign ui rendering.
main ui structure is rendered in cui", and `.xaml`, `.axaml` and `.hui` are all foreign rendering. Dynamic and
transient surfaces may stay in host code for now; the main structure may not.

This tool turns that rule into a count that can be watched rather than argued about:

    own .cui          first-party CUI documents (donor mirror corpora counted separately)
    mirror .cui       Stage A donor mirrors: CUI structure that has not been adapted to the specification yet
    foreign           .xaml/.axaml/.hui first-party UI files, excluding donor checkouts and test fixtures
    shell files       foreign files that are the app shell itself (window, main view, sidebar, top rail, pages)

Donor trees are excluded through `eng/donor-sources.json`, so upstream Avalonia/WPF markup is never counted as
our violation, and our own markup is never excused as donor material.

Baseline behaviour: foreign files may only fall and CUI documents may only rise. Both directions are checked, so
the tool cannot be satisfied by deleting a CUI document, and a new `.axaml` view is a breach rather than a
silent addition. A lowered baseline must state the reason.

Usage:
    python3 -B eng/spec-coverage/surface_conformance.py check
    python3 -B eng/spec-coverage/surface_conformance.py record --reason "why the numbers moved"
Static file measurement only: nothing here renders a surface or claims any of it works.
"""
from __future__ import annotations

import argparse
import json
import sys
from collections import Counter
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BASELINE = Path(__file__).resolve().parent / "surface_baselines.json"

# Machine-readable kind of this record. Tests and gates match on this, never on prose in _reason.
NATURE = "one-way ratchet baseline: foreign-rendering UI files may only fall, own .cui may only rise"
DONOR_REGISTRY = ROOT / "eng/donor-sources.json"
SHELL_MARKERS = ("/shell/", "mainview", "mainwindow", "app.axaml", "sidebar", "toprail", "/views/pages/")


def donor_prefixes() -> list[str]:
    registry = json.loads(DONOR_REGISTRY.read_text())
    return sorted({str(row["path"]) for row in registry.get("bindings", []) if row.get("path")})


def app_owner_index() -> list[tuple[str, str]]:
    """Longest-prefix map from a repository path to the specification app that owns it.

    Measured defect of the first version: it attributed app by workspace directory name alone, so Build scored
    zero first-party CUI documents when its real surfaces live in 9to1 Workspace/Dev (6 .cui), Sites (13) and
    Stacks (1) - the specification's one-app rule makes those directories Build. The app-local roots recorded in
    app-roots/assess config are the single source of truth for that grouping, so the two tools cannot drift.
    """
    try:
        config = json.loads((Path(__file__).resolve().parent / "app-roots.json").read_text())["apps"]
    except (OSError, KeyError, json.JSONDecodeError):
        return []
    pairs = [(str(prefix), app) for app, roots in config.items() for root_class, prefix in roots
             if root_class == "app-local"]
    return sorted(pairs, key=lambda pair: -len(pair[0]))


def app_owner(rel: str, index: list[tuple[str, str]]) -> str:
    for prefix, app in index:
        if rel == prefix or rel.startswith(prefix.rstrip("/") + "/"):
            return app
    return rel.split("/")[1] if rel.startswith("9to1 Workspace/") else rel.split("/")[0]


def measure() -> dict[str, object]:
    pruned = donor_prefixes()
    workspace = ROOT / "9to1 Workspace"

    def excluded(rel: str) -> bool:
        if any(rel == prefix or rel.startswith(prefix + "/") for prefix in pruned):
            return True
        return any(fragment in rel.lower() for fragment in ("/bin/", "/obj/", "/vendor/", "/node_modules/"))

    own_cui: Counter[str] = Counter()
    mirror_cui: Counter[str] = Counter()
    foreign_files: list[str] = []
    app_of_path = app_owner_index()
    for path in workspace.rglob("*.cui"):
        rel = path.relative_to(ROOT).as_posix()
        if excluded(rel):
            continue
        app = app_owner(rel, app_of_path)
        (mirror_cui if "/CUI/DonorMirror/" in rel else own_cui)[app] += 1
    for extension in (".xaml", ".axaml", ".hui"):
        for path in workspace.rglob(f"*{extension}"):
            rel = path.relative_to(ROOT).as_posix()
            if excluded(rel) or "/donormirror/" in rel.lower():
                continue
            lowered = rel.lower()
            if "/tests/" in lowered or "/tests." in lowered:
                continue                                  # a test fixture is not a shipping surface
            foreign_files.append(rel)
    shell = [rel for rel in foreign_files
             if any(marker in rel.lower() for marker in SHELL_MARKERS)]
    per_app_foreign: Counter[str] = Counter(app_owner(rel, app_of_path) for rel in foreign_files)
    return {
        "own_cui_total": sum(own_cui.values()),
        "mirror_cui_total": sum(mirror_cui.values()),
        "foreign_total": len(foreign_files),
        "foreign_shell_total": len(shell),
        "own_cui_per_app": dict(own_cui.most_common()),
        "mirror_cui_per_app": dict(mirror_cui.most_common()),
        "foreign_per_app": dict(per_app_foreign.most_common()),
        "foreign_files": sorted(foreign_files),
        "foreign_shell_files": sorted(shell),
    }


def compare(baseline: dict[str, object], now: dict[str, object]) -> list[str]:
    problems: list[str] = []
    if now["foreign_total"] > baseline["foreign_total"]:
        added = sorted(set(now["foreign_files"]) - set(baseline.get("foreign_files", [])))
        problems.append(f"foreign rendering grew {baseline['foreign_total']} -> {now['foreign_total']}; "
                        f"new: {', '.join(added[:6])}"
                        + (f" (+{len(added) - 6} more)" if len(added) > 6 else ""))
    if now["own_cui_total"] < baseline["own_cui_total"]:
        problems.append(f"first-party CUI documents fell {baseline['own_cui_total']} -> "
                        f"{now['own_cui_total']}: CUI surfaces must not be deleted to improve a ratio")
    if now["mirror_cui_total"] < baseline["mirror_cui_total"]:
        problems.append(f"Stage A mirror documents fell {baseline['mirror_cui_total']} -> "
                        f"{now['mirror_cui_total']}: a lost mirror must be explained, not absorbed")
    return problems


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("mode", choices=["check", "record"])
    ap.add_argument("--reason", default=None)
    ap.add_argument("--json", action="store_true")
    args = ap.parse_args(argv)

    now = measure()
    if args.json:
        print(json.dumps(now, indent=2))
        return 0
    if args.mode == "record":
        if not args.reason:
            print("REFUSED: recording a new surface baseline needs a reason", file=sys.stderr)
            return 2
        payload = {"_meaning": "CUI versus foreign-rendering surface counts. A baseline may only fall in foreign "
                               "files and rise in CUI documents; both directions are checked.",
                   "_evidence_level": "static file measurement: no rendering, no run, no acceptance",
                   "_nature": NATURE,
                   "_recorded_on": date.today().isoformat(), "_reason": args.reason,
                   **now}
        BASELINE.write_text(json.dumps(payload, indent=2) + "\n")
        print(f"recorded: own .cui {now['own_cui_total']}, mirror .cui {now['mirror_cui_total']}, "
              f"foreign {now['foreign_total']} (shell {now['foreign_shell_total']})")
        return 0

    if not BASELINE.is_file():
        print("REFUSED: no surface baseline; run `record --reason ...` after measuring", file=sys.stderr)
        return 2
    baseline = json.loads(BASELINE.read_text())
    problems = compare(baseline, now)
    print(f"own .cui {now['own_cui_total']} (was {baseline['own_cui_total']}) | "
          f"mirror .cui {now['mirror_cui_total']} (was {baseline['mirror_cui_total']}) | "
          f"foreign {now['foreign_total']} (was {baseline['foreign_total']}, "
          f"shell {now['foreign_shell_total']})")
    if problems:
        print("SURFACE BREACH:", file=sys.stderr)
        for problem in problems:
            print(f"  - {problem}", file=sys.stderr)
        return 1
    print("no new foreign rendering; no CUI surface lost. Static file measurement only - no surface here is "
          "rendered, run or accepted.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
