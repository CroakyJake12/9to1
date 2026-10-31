#!/usr/bin/env python3
"""Stage A donor-UI -> CUI mirror runner.

  python3 -B eng/cui-mirror/mirror.py --app Canvas --donor rnote \
      --src "9to1 Workspace/Canvas/Source/Rnote" --out "9to1 Workspace/Canvas/CUI/DonorMirror/rnote"

Writes one .cui per translated donor UI file, plus gaps.md / gaps.json / inventory.json beside them.
Idempotent: existing outputs are rewritten only when the donor source changed (hash compared), and a
donor file that cannot be translated at all is recorded, never silently skipped.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cui_mirror
import dialect_dart
import dialect_gtk
import dialect_xaml
import dialect_xul

DIALECT_BY_SUFFIX = {".ui": ("gtk", dialect_gtk), ".xaml": ("xaml", dialect_xaml),
                     ".xul": ("xul", dialect_xul), ".xhtml": ("xul", dialect_xul),
                     ".html": ("xul", dialect_xul)}
# Gitea's web UI is Go html/template over plain HTML markup, and VS Code / Firefox ship their chrome as
# HTML too, so the html branch of the XUL dialect carries them.
DIALECT_BY_SUFFIX[".tmpl"] = ("html", dialect_xul)
DIALECT_BY_SUFFIX[".templ"] = ("html", dialect_xul)
DIALECT_BY_SUFFIX[".cshtml"] = ("html", dialect_xul)
DIALECT_BY_SUFFIX[".inc"] = ("xul", dialect_xul)
DIALECT_BY_SUFFIX[".svg"] = ("vector", None)
DIALECT_BY_SUFFIX[".dart"] = ("dart", dialect_dart)
DIALECT_BY_SUFFIX[".arb"] = ("localisation", None)
DIALECT_BY_SUFFIX[".g.dart"] = ("generated", None)

NOT_YET = {".jsx": "React/TSX component", ".tsx": "React/TSX component",
           ".ts": "code-built workbench UI (TypeScript views)", ".js": "custom elements in JS",
           ".qrc": "Qt resource/QML", ".ui.qml": "QML", ".vala": "Vala/GTK code-built UI",
           ".py": "code-built UI (Tk/GTK python)", ".cs": "code-built UI (see C# constructions list)"}


def tool_version() -> str:
    """Digest of the emitter and dialect sources, so mirrors refresh when the tool changes."""
    here = Path(__file__).resolve().parent
    digest = hashlib.sha256()
    for name in sorted(("cui_mirror.py", "dialect_dart.py", "dialect_gtk.py", "dialect_xaml.py",
                        "dialect_xul.py")):
        path = here / name
        if path.is_file():
            digest.update(path.read_bytes())
    return digest.hexdigest()


def _excluded(rel: str, patterns: list[str]) -> bool:
    """Exclude donor build/test paths by NAME SEGMENT, not by substring.

    Measured defect of the substring form: `--exclude test` silently dropped genuine product UI -
    swriter/ui/testmailsettings.ui and opttestpage.ui (test pages, fair enough) but also
    footnotesendnotestabpage.ui, chisquaretestdialog.ui, ttestdialog.ui and ztestdialog.ui, none of which
    is a test fixture at all. A pattern with a slash still matches a path fragment so a caller can name a
    subtree explicitly.
    """
    parts = [segment.lower() for segment in rel.replace("\\", "/").split("/")]
    for raw in patterns:
        needle = raw.lower()
        if "/" in needle:
            if needle in rel.lower().replace("\\", "/"):
                return True
        elif any(part == needle or part.startswith(needle + ".") or part.startswith(needle + "-")
                 for part in parts):
            return True
    return False


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--app", required=True, help="owning 9to1 app, for the output path and report")
    ap.add_argument("--donor", required=True, help="donor name used in the mirror id and report")
    ap.add_argument("--src", required=True, help="donor source directory to scan")
    ap.add_argument("--out", required=True, help="directory for the mirrored .cui corpus")
    ap.add_argument("--dialect", default="auto", choices=["auto", "dart", "gtk", "xaml", "xul"])
    ap.add_argument("--limit", type=int, default=0, help="cap files (smoke runs)")
    ap.add_argument("--exclude", action="append", default=[],
                    help="substring exclusion (repeat), e.g. test, help, print")
    args = ap.parse_args(argv)

    src = Path(args.src)
    out = Path(args.out)
    if not src.is_dir():
        print(f"REFUSED: no donor source at {src}", file=sys.stderr)
        return 2
    out.mkdir(parents=True, exist_ok=True)

    # Read back the previous inventory: it is the record of which donor revision each mirror came from,
    # so an unchanged donor file is not rewritten and a tool change refreshes everything.
    prior_digests: dict[str, str] = {}
    prior_inventory = out / "inventory.json"
    if prior_inventory.is_file():
        try:
            for rec in json.loads(prior_inventory.read_text()).get("files", []):
                if rec.get("cui"):
                    prior_digests[rec["cui"]] = rec.get("donor_sha256", "")
        except (json.JSONDecodeError, OSError):
            pass

    files, unsupported = [], {}
    for path in sorted(src.rglob("*")):
        if not path.is_file():
            continue
        rel = str(path.relative_to(src))
        if _excluded(rel, args.exclude):
            continue
        suffix = path.suffix.lower()
        if args.dialect != "auto":
            if suffix not in [k for k, (n, _) in DIALECT_BY_SUFFIX.items() if n == args.dialect]:
                continue
        if suffix == ".svg" or DIALECT_BY_SUFFIX.get(suffix, (None, None))[1] is None:
            continue
        if suffix in DIALECT_BY_SUFFIX:
            files.append(path)
        elif suffix in NOT_YET or suffix == ".qml" or suffix == ".dart":
            unsupported[suffix] = unsupported.get(suffix, 0) + 1
    if args.limit:
        files = files[:args.limit]

    results, records = [], []
    for path in files:
        rel = str(path.relative_to(src))
        name, module = DIALECT_BY_SUFFIX[path.suffix.lower()]
        try:
            source = path.read_text(encoding="utf-8", errors="replace")
        except OSError as exc:
            results.append(cui_mirror.Result(cui_id=path.stem, root=None,
                                             gaps=[{"donor": "unreadable", "construct": str(exc),
                                                   "file": rel, "line": 0, "why": "skipped"}]))
            continue
        try:
            result = module.translate(path, source)
        except Exception as exc:                              # one unreadable donor file cannot abort a run
            result = cui_mirror.Result(cui_id=path.stem, root=None,
                                       gaps=[{"donor": type(exc).__name__, "construct": str(exc)[:120],
                                             "file": rel, "line": 0, "why": "translator raised; recorded"}])
        results.append(result)
        if result.root is None:
            records.append({"donor_file": rel, "cui": None, "status": "not translated",
                            "dialect": name})
            continue
        document = cui_mirror.emit(result)
        target = out / (rel[: -len(path.suffix)] + ".cui")
        target.parent.mkdir(parents=True, exist_ok=True)
        digest = hashlib.sha256((source + "\n" + tool_version()).encode("utf-8", "replace")).hexdigest()
        # The inventory already records each donor digest, so it is the staleness record: no separate
        # stamp files to keep in sync, and no second source of truth about what a mirror came from.
        changed = prior_digests.get(str(target.relative_to(out))) != digest
        if changed:
            target.write_text(document)
        records.append({"donor_file": rel, "cui": str(target.relative_to(out)), "status": "translated",
                        "dialect": name, "nodes": result.translated, "actions": len(result.actions),
                        "donor_sha256": digest, "refreshed": changed})

    # Stale output is worse than no output: an old mirror of a file the donor no longer has, or one the
    # current tool refuses to translate, would keep compiling and quietly misreport coverage. Prune every
    # .cui under this donor's corpus that this run did not produce, and say how many went.
    expected = {str(Path(r["cui"])) for r in records if r["status"] == "translated"}
    pruned = []
    for existing in out.rglob("*.cui"):
        rel = str(existing.relative_to(out))
        if rel in expected:
            continue
        pruned.append(rel)
        existing.unlink()
        for stale in list(existing.parent.glob(existing.name + ".source-sha256")):
            stale.unlink()      # earlier runs kept a stamp per file; the inventory replaces them

    summary = cui_mirror.summarise(results)
    summary["pruned_stale"] = len(pruned)
    summary["donor"] = args.donor
    summary["app"] = args.app
    summary["source"] = str(src)
    summary["translated_files"] = sum(1 for r in records if r["status"] == "translated")
    summary["untranslated_files"] = sum(1 for r in records if r["status"] != "translated")
    summary["unsupported_suffixes"] = unsupported
    cui_mirror.write_gap_report(out / "gaps.md", summary, args.donor)
    (out / "inventory.json").write_text(json.dumps(
        {"summary": {k: v for k, v in summary.items() if k != "gaps"}, "files": records},
        indent=2) + "\n")
    print(f"{args.donor}: files={len(files)} translated={summary['translated_files']} "
          f"nodes={summary['translated']} dropped={summary['dropped_nodes']} "
          f"gaps={summary['gap_count']} not_translated={summary['untranslated_files']}")
    for construct, count in list(summary["gap_constructs"].items())[:12]:
        print(f"   gap {construct:<44}{count}")
    if unsupported:
        print("   donor UI still needing a dialect pass: "
              + ", ".join(f"{k} x{v}" for k, v in sorted(unsupported.items())))
    return 0


if __name__ == "__main__":
    sys.exit(main())
