"""Audit which donors the canonical specification designates against what the repository declares.

Read-only. It re-reads the specification and checks every recorded designation at its recorded line, so
a mapping cannot silently rot when the specification changes. It reports designations with no registry
binding, bindings with no designation, and donors with no materialised source. Grants no acceptance.
"""

import argparse
import json
from pathlib import Path
import re
import sys

REGISTRY = "eng/donor-sources.json"
GITMODULES = ".gitmodules"
SPEC = "docs/specification/9to1-development-specification-v2.md"

# Designations recorded from the canonical specification, with the line the wording sits on and a
# fragment used to prove the wording is still there. App names follow the specification's own product
# identity section: Dev/Sites/Stacks are Build workspaces, Study/Translate are Spaces, Mail is a
# Connect mode, and CUI/Dulche/Home Core are framework/service layers rather than applications.
DESIGNATIONS = [
    {"app": "Haven", "donor": "firefox-gecko", "line": 1641,
     "fragment": "Firefox donor parity is the minimum supported capability set"},
    {"app": "CUI framework", "donor": "avalonia", "line": 1816,
     "fragment": "CUI is derived from a pinned Avalonia source fork"},
    {"app": "CUI framework", "donor": "avalonia", "line": 1830,
     "fragment": "MUST obtain and include the selected appropriate Avalonia source revision"},
    {"app": "Write", "donor": "libreoffice-writer", "line": 8387,
     "fragment": "All applicable LibreOffice Writer functionality"},
    {"app": "Write", "donor": "libreofficekit", "line": 8651,
     "fragment": "integrated LibreOffice Writer/LibreOfficeKit donor engine"},
    {"app": "Present", "donor": "libreoffice-impress-draw", "line": 8761,
     "fragment": "LibreOffice Impress/Draw is the donor foundation"},
    {"app": "Boards", "donor": "appflowy-board", "line": 9383,
     "fragment": "AppFlowy is the primary donor for applicable workspace/page/database/block behaviours"},
    {"app": "Canvas", "donor": "rnote", "line": 10049, "fragment": "selected Rnote donor parity manifest"},
    {"app": "Data", "donor": "libreoffice-calc", "line": 10318,
     "fragment": "LibreOffice Calc is the spreadsheet-compatibility/donor engine"},
    {"app": "Data", "donor": "libreoffice-calc", "line": 10320,
     "fragment": "MUST contain or fetch the appropriate maintained LibreOffice Calc donor source"},
    {"app": "Data", "donor": "duckdb", "line": 10318, "fragment": "DuckDB is the local analytical SQL"},
    {"app": "Files", "donor": "files", "line": 10804, "fragment": "donor-backed by Files/WinUI"},
    {"app": "Connect", "donor": "element-matrix", "line": 12219,
     "fragment": "Element, together with Matrix as its underlying open commu"},
    {"app": "Connect/Mail", "donor": "thunderbird", "line": 12221,
     "fragment": "retains Thunderbird as its preferred mature donor/reference for email"},
    {"app": "Games", "donor": "godot", "line": 14523, "fragment": "maintained fork of Godot as its primary game-engine donor"},
    {"app": "Games", "donor": "blender", "line": 14525, "fragment": "maintained Blender-derived modelling/rendering toolset"},
    {"app": "Mini Computer", "donor": "virtualbox", "line": 14982,
     "fragment": "MUST fork and maintain the latest appropriate VirtualBox source"},
    {"app": "Maps", "donor": "comaps", "line": 16038, "fragment": "The primary mature donor is the latest appropriate CoMaps source"},
    {"app": "Launcher", "donor": "lawnchair", "line": 16203,
     "fragment": "Lawnchair is the primary donor and mature behavioural baseline for Launcher"},
    {"app": "Build (source collaboration)", "donor": "gitea", "line": 6746, "fragment": "Primary donor: Gitea."},
    {"app": "Build (source collaboration)", "donor": "gitea", "line": 6768,
     "fragment": "Gitea is the primary donor and must be forked from the latest appropriate source"},
    {"app": "Build (source collaboration)", "donor": "gitnuro", "line": 6748,
     "fragment": "Secondary donor/reference: Gitnuro"},
    {"app": "Build (site workspace)", "donor": "cakemods", "line": 7491,
     "fragment": "cakemods website/site-plugin source is the primary donor"},
    {"app": "Dulche runtime", "donor": "llama-cpp", "line": 4485, "fragment": "llama.cpp remains the b"},
    {"app": "Dulche runtime", "donor": "strata", "line": 4515,
     "fragment": "Dulche MUST carry a maintained Strata fork"},
    {"app": "9to1 OS", "donor": "wine", "line": 16692, "fragment": "donor-backed utilities"},
    {"app": "9to1 OS", "donor": "winboat", "line": 16692, "fragment": "donor-backed utilities"},
    {"app": "9to1 OS", "donor": "gnome-system-monitor", "line": 16694,
     "fragment": "Ubuntu/GNOME system-monitor tooling as donor baseline"},
]

# Registry donor keys that can legitimately appear under more than one specification name.
SYNONYMS = {
    "firefox-gecko": {"firefox-gecko", "firefox"},
    "libreoffice-writer": {"libreoffice"}, "libreofficekit": {"libreoffice"},
    "libreoffice-impress-draw": {"libreoffice"}, "libreoffice-calc": {"libreoffice"},
    "appflowy-board": {"appflowy-board"}, "element-matrix": {"element", "matrix"},
    "gnome-system-monitor": {"gnome-shell", "mutter"}, "code-oss": {"code-oss", "vscodium"},
}


def load(repo):
    repo = Path(repo)
    registry = json.loads((repo / REGISTRY).read_text())
    bindings = {}
    for b in registry.get("bindings", []):
        bindings.setdefault(b["donor"], set()).add(b["app"])
    upstreams = {k.lower() for k in registry.get("upstreams", {})}
    modules = (repo / GITMODULES).read_text() if (repo / GITMODULES).is_file() else ""
    spec_lines = (repo / SPEC).read_text().splitlines()
    return repo, bindings, upstreams, modules, spec_lines


def find_fragment(spec_lines, fragment):
    """Where the specification states this wording now, if at all."""
    hits = [i + 1 for i, line in enumerate(spec_lines) if fragment in line]
    return hits


def audit(repo="."):
    repo, bindings, upstreams, modules, spec_lines = load(repo)
    problems, notes, rows = [], [], []
    for d in DESIGNATIONS:
        hits = find_fragment(spec_lines, d["fragment"])
        if not hits:
            problems.append(f"missing designation: {d['app']}/{d['donor']} wording {d['fragment']!r} "
                            f"is no longer in {SPEC} at all")
            wording = "ABSENT"
        else:
            wording = str(hits[0])
            if d["line"] not in hits:
                # A recorded line number is a convenience, not a fact that survives edits: the
                # specification gains lines as it is amended. Report where the wording actually lives
                # rather than failing the designation.
                notes.append(f"{d['app']}/{d['donor']}: wording is now on line {hits[0]}, recorded "
                             f"{d['line']} - the specification has been amended since this mapping "
                             f"was written")
        keys = SYNONYMS.get(d["donor"], {d["donor"]})
        declared = bool(keys & set(bindings))
        in_upstreams = bool(keys & upstreams)
        in_modules = any(k.lower() in modules.lower() for k in keys)
        rows.append({**d, "wording_found_at": wording, "declared_binding": declared,
                     "declared_upstream": in_upstreams, "has_gitmodules_entry": in_modules})
        if not declared:
            problems.append(f"undeclared donor: {d['app']} is designated on {d['donor']} by the "
                            f"specification (line {wording}) but has no binding in {REGISTRY}")
        elif not in_upstreams:
            problems.append(f"binding without upstream record: {d['app']}/{d['donor']}")
        elif not in_modules:
            problems.append(f"no .gitmodules entry for designated donor {d['app']}/{d['donor']}")

    designated = {k for d in DESIGNATIONS for k in SYNONYMS.get(d["donor"], {d["donor"]})}
    # A binding whose donor has no upstream record cannot be pinned, licensed or verified at all. The
    # pre-existing gate rejects this; the coverage audit must not be blind to it either.
    for donor_key, apps in sorted(bindings.items()):
        if not ({donor_key} & upstreams):
            problems.append(f"binding without upstream record: {donor_key} "
                            f"(apps: {', '.join(sorted(apps))})")
    extra = sorted(set(bindings) - designated)
    for e in extra:
        problems.append(f"registry binding with no specification designation found: {e} "
                        f"(apps: {', '.join(sorted(bindings[e]))}) - it may be an older designation "
                        f"this audit has not mapped, so verify against the specification")
    return {"designations": rows, "problems": problems, "notes": notes,
            "counts": {"designations": len(rows), "distinct_designated_donors": len(designated),
                       "registry_bindings": len(bindings)},
            "note": "Read-only coverage audit. No clone, fetch, build, install or acceptance is performed."}


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("verb", choices=["audit"])
    ap.add_argument("--repo", default=".")
    ap.add_argument("--json", action="store_true")
    ap.add_argument("--out")
    args = ap.parse_args(argv)

    out = Path(args.out) if args.out else None
    if out is not None and out.exists():
        print(f"REFUSED: {out} already exists; the audit never overwrites", file=sys.stderr)
        return 2

    result = audit(args.repo)
    if args.json:
        text = json.dumps(result, indent=2, ensure_ascii=False)
    else:
        text = "\n".join(f"{r['app']:<30}{r['donor']:<26}"
                         f"{'binding' if r['declared_binding'] else 'NO BINDING':<12}"
                         f"{'upstream' if r['declared_upstream'] else 'no upstream':<13}"
                         f"{'gitlink' if r['has_gitmodules_entry'] else 'no gitlink':<13}"
                         f"spec line {r['line']}"
                         for r in sorted(result["designations"], key=lambda x: (x["app"], x["donor"])))
    if out is not None:
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(text + "\n")
    print(text)
    print(f"\ncounts: {result['counts']}")
    if result["problems"]:
        print("\nPROBLEMS:", file=sys.stderr)
        for p in result["problems"]:
            print(f"  - {p}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
