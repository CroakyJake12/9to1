#!/usr/bin/env python3
"""Assess each specification requirement against the source that exists, with a resolvable pointer.

Owner request, 31 October 2026: per-app percentages must be MEASURED, not estimated. This is stage 2 of that
instrument (stage 1 is `spec_register.py`, which fixes the denominator). Per app it reports:

    TEST-ASSERTED   a test file carries the requirement's distinctive vocabulary
    CODE-PRESENT    first-party source (app-local, shared or framework) carries that vocabulary
    MIRROR-ONLY     only the Stage A donor mirror carries it: structure without specification behaviour
    WEAK-SINGLE-TERM one distinctive term matched, which is not enough to claim the requirement is addressed
    NO-EVIDENCE     the requirement's distinctive vocabulary appears nowhere in the app's recorded roots
    NO-PROBE-TERMS  the requirement is pure prose with no distinctive vocabulary to search (rare; recorded,
                    never folded into another tier)

Boundaries, stated in the output as well as here:

  * CODE-PRESENT means the requirement's own distinctive vocabulary is addressed by first-party source. It is
    NOT proof of correct or complete behaviour, and it never counts as accepted or on-screen;
  * NO-EVIDENCE is the strong, falsifiable claim: that vocabulary does not occur in the app's recorded roots at
    all. One counterexample disproves it, which is why the pointer for it is empty and the terms are kept;
  * every non-empty pointer is re-checked by `verify-pointers` against the files as they stand; a row whose
    pointer no longer resolves is STALE and reported, not carried forward;
  * MIRROR-ONLY can never lift an app into CODE-PRESENT: a Stage A donor mirror proves structure exists, not
    that the specification behaviour was built.

Matching discipline: words like "app", "user", "data" or "UI" would credit every file in the repository, so a
term counts only when it is distinctive — a backticked or quoted identifier, a CamelCase or dotted name, or a
long-enough domain word outside the stop-list — and a tier needs MIN_TERMS distinct terms co-occurring in the
same file.

Overrides: `assessments/<App>.json` holds hand-made states with an evidence string and a reason, and always
win over the probe, so real reading of a running surface replaces mechanical matching instead of competing
with it. An override without an evidence string is refused.

Usage:
    python3 -B eng/spec-coverage/assess.py score [--app Boards]
    python3 -B eng/spec-coverage/assess.py row <requirement-id-substring>
    python3 -B eng/spec-coverage/assess.py verify-pointers
Static evidence only: no compilation, no rendering, no packaging, no acceptance.
"""
from __future__ import annotations

import argparse
import json
import os
import random
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
REGISTER = HERE / "register.json"
OUT = HERE / "assessment.json"
SUMMARY_OUT = HERE / "coverage-summary.json"
APP_ROOTS = HERE / "app-roots.json"
OVERRIDE_DIR = HERE / "assessments"

MIN_TERMS = 2                 # distinct terms required in one file before a tier is credited
MAX_TERMS_PER_ROW = 10        # probe breadth per requirement
MAX_FILE_BYTES = 1_500_000

TIERS = ("TEST-ASSERTED", "CODE-PRESENT", "MIRROR-ONLY", "WEAK-SINGLE-TERM", "NO-EVIDENCE", "NO-PROBE-TERMS",
         "NO-DISTINCTIVE-TERM", "PARTIAL", "IMPLEMENTED", "NOT-IMPLEMENTED")

# Markdown is deliberately NOT an evidence class. Measured in the first hand audit of 12 credited rows
# (31 Oct 2026): 6 were false, and four of those six pointed at documentation - two at an app's own
# DonorParityManifest.md, which records what is MISSING, so a gap record was being credited as proof of
# implementation. Requirement text also echoes specification prose, which is authority, not evidence.
# .json is also excluded: measured, eng/donor-sources.json credited a 9to1 OS requirement by matching
# "compatibility" and "installed" in my own registry metadata. Tooling metadata is not product evidence.
FILE_SUFFIXES = {".cui", ".cs", ".axaml", ".xaml", ".ts", ".tsx", ".js", ".rs", ".cpp", ".h", ".hpp",
                 ".py", ".xhtml", ".html", ".dart", ".ui"}
SKIP_FRAGMENTS = ("/bin/", "/obj/", "/vendor/", "/node_modules/", "/.git/", "/_fixtures/",
                  "/source/libreoffice/",
                  "/source/thunderbird/", "/source/codeoss/", "/source/gitea/", "/source/appflowy/",
                  "/source/rnote/", "/source/firefox", "/source/strata/")
MIRROR_HINTS = ("donormirror",)
# Test fixtures are not product evidence. Measured cause of a suite flake on 31 Oct 2026: ClassifyTests writes
# synthetic files under eng/spec-coverage/_fixtures, and eng/ is a recorded root for the packaging bucket, so a
# leftover fixture changed the packaging tier counts between runs - the committed coverage summary and an
# in-process re-score disagreed, and the mismatch appeared and disappeared with fixture lifetime rather than with
# any real change to the product.
FIXTURE_FRAGMENT = "/_fixtures/"
ASSERT_RE = re.compile(r"\b(Assert|assert\.|Assert\.|Should\.|EXPECT_|expect\(|DidNot|IsTrue|IsFalse|AreEqual)\b")

# Words so common in this specification that matching them would credit every file in the repository.
STOP = set("""a an the and or but if then than that this these those there here of to in on for with without
from by as at into onto over under again also not no nor only just very more most other others some any each
every both same such can cannot could should would may might must shall will is are was were be been being do
does did done have has had having it its they them their you your yours we our us he she his her which who
whom whose what when where why how all none one two three four five six seven eight nine ten first second
third next last new old existing user users app apps application applications surface surfaces ui screen
screens view views window windows element elements control controls item items list lists page pages tab tabs
bar bars menu menus button buttons field fields text values value data file files folder folders state states
action actions service services system systems component components feature features behaviour behaviors
requirement requirements spec specified specification implementation implementations implemented implement
implementing code source sources work works working workspace level levels mode modes normal standard default
custom explicit explicitly required require requires requiring mandatory optional supports support supported
unsupported supporting using use uses used user-facing cross platform platforms windows linux android web
browser native desktop mobile device devices session sessions save saved saving load loaded loading open
opened opening opening close closed closing create created creating delete deleted removing remove removes
edit edited editing add added move moved copy copied paste typed type types named name names identifier
identifiers ids id stable uniquely unique valid invalid acceptable error errors failure failures successfully
success failed fallback retry retries ensure ensures ensuring prevent prevents preventing allow allows
allowing allowed disallowed provide provides providing provided expose exposes exposing exposed display
displays displayed displaying show shows shown showing hide hides hidden hiding keep keeps preserved preserve
preserving remain remains unchanged apply applies applied applying respect respects respecting consistent
consistently inconsistent empty full complete partially partial available unavailable accessible access
permission permissions authorised authorized unauthorised account accounts signed sign signing login logout
profile profiles identity identities canonical object objects reference referenced embedding embed embedded
owner owns owning ownership shared separately distinct different same similar single multiple many few
current active inactive enabled disabled visible invisibly presentation visual interactive keyboard mouse
pointer touch tap drag drop click clicked clicking hover focused focus unfocused selection selected
selecting order ordered reordering grouped grouping labels label caption captions tooltip tooltips icon icons
image images content contents message messages prompt prompts chat chats conversation conversations model
models settings setting preference preferences resource resources limit limits budget budgets usage consume
consumption should""".split())

CANDIDATES: list[str] = []

IDENT_RE = re.compile(r"[A-Za-z_][A-Za-z0-9_]*(?:[.\-_/][A-Za-z_][A-Za-z0-9_]*)*")
BACKTICK_RE = re.compile(r"`([^`]{2,60})`")
QUOTED_RE = re.compile(r"[\"“‘'<]([A-Za-z][A-Za-z0-9 _.,\-']{1,60})[\"”’'>]")
TYPEABLE_RE = re.compile(r"^(?:[A-Za-z][A-Za-z0-9]*[.\-_])+[A-Za-z0-9]+$")


def probe_terms(text: str) -> list[str]:
    """Distinctive terms a file must address for this row to be credited."""
    terms: list[str] = []
    seen: set[str] = set()

    def add(term: str, distinctive: bool) -> None:
        term = term.strip().strip(".,;:()[]'\"“”")
        if len(term) < 3:
            return
        key = term.lower()
        if key in seen or key in STOP:
            return
        if not distinctive and (len(key) < 8 or not re.fullmatch(r"[a-z][a-z\-]+", key)):
            return
        seen.add(key)
        terms.append(term)

    for chunk in BACKTICK_RE.findall(text):
        for token in IDENT_RE.findall(chunk):
            add(token, True)
    for chunk in QUOTED_RE.findall(text):
        add(chunk, True)
    for token in IDENT_RE.findall(text):
        # A capital letter alone is NOT a code identifier: measured, treating capitalized sentence words as
        # identifiers made "Board", "Context" and "Space" credit arbitrary files, which is how the first run
        # reached 89% evidence-credited. A term is identifier-like only if a developer would have typed it as
        # a name: CamelCase, dotted/underscored/slashedin, ALL CAPS, or a quoted/backticked literal.
        if (re.search(r"[a-z][A-Z]", token) or TYPEABLE_RE.match(token)
                or re.fullmatch(r"[A-Z][A-Za-z0-9]{1,}", token) and re.fullmatch(r"[A-Z0-9]{2,}", token)):
            add(token, True)
    for word in re.findall(r"\b[A-Za-z][A-Za-z\-]{7,}\b", text):
        add(word, False)
    return terms[:MAX_TERMS_PER_ROW]


def is_mirror(rel: str) -> bool:
    return any(hint in rel.lower() for hint in MIRROR_HINTS)


def is_test(rel: str) -> bool:
    return any(hint in rel.lower() for hint in ("/tests/", ".tests", "test_"))


def donor_prefixes() -> list[str]:
    """Donor material is excluded by the donor registry, not by a guessed directory name.

    The repository already records every pinned donor checkout in eng/donor-sources.json, so that is the
    authority for what is upstream material rather than first-party source. Counting donor code as evidence
    that a 9to1 requirement is implemented would be the single most misleading thing this instrument could do.
    """
    registry = json.loads((ROOT / "eng/donor-sources.json").read_text())
    return sorted({str(row["path"]) for row in registry.get("bindings", []) if row.get("path")})


def candidates() -> list[str]:
    """Every file that could be first-party evidence, walked once with the donor trees pruned."""
    pruned = donor_prefixes()
    found: list[str] = []
    for top in ("9to1 Workspace", "framework", "eng", "9to1 Models"):
        base = ROOT / top
        if not base.is_dir():
            continue
        for dirpath, dirnames, filenames in os.walk(base):
            rel = Path(dirpath).relative_to(ROOT).as_posix()
            if any(fragment in "/" + rel.lower() for fragment in SKIP_FRAGMENTS):
                dirnames[:] = []
                continue
            if any(rel == prefix or rel.startswith(prefix + "/") for prefix in pruned):
                dirnames[:] = []
                continue
            dirnames[:] = [d for d in dirnames if d not in ("bin", "obj", "vendor", "node_modules", ".git")]
            for name in filenames:
                if os.path.splitext(name)[1].lower() not in FILE_SUFFIXES:
                    continue
                full = os.path.join(dirpath, name)
                try:
                    if os.path.getsize(full) > MAX_FILE_BYTES:
                        continue
                except OSError:
                    continue
                found.append((Path(full).relative_to(ROOT)).as_posix())
    return sorted(set(found))


class Corpus:
    """Index each candidate file at most once, whatever number of apps consult it."""

    def __init__(self) -> None:
        self.id_by_path: dict[str, int] = {}
        self.paths: list[str] = []
        self.postings: dict[str, list[int]] = defaultdict(list)
        self.indexed: set[int] = set()

    def ensure(self, rel: str) -> int:
        if rel in self.id_by_path:
            return self.id_by_path[rel]
        index = len(self.paths)
        self.id_by_path[rel] = index
        self.paths.append(rel)
        return index

    def index(self, rel: str) -> int:
        index = self.ensure(rel)
        if index in self.indexed:
            return index
        self.indexed.add(index)
        try:
            text = (ROOT / rel).read_text(encoding="utf-8", errors="ignore").lower()
        except OSError:
            return index
        for token in set(re.findall(r"[a-z0-9][a-z0-9_\-]{2,}", text)):
            self.postings[token].append(index)
            # Test method names are the most behaviour-descriptive vocabulary in this repository, and the base
            # token regex swallows `Matching_challenge_commits_site_bound_ownership` into ONE token, so the
            # words inside it were invisible. Measured consequence: Boards reported only 2 TEST-ASSERTED of 138
            # while Haven.Desktop.Tests carries a large Boards suite. Split the parts as well.
            if "_" in token or "-" in token:
                for part in re.split(r"[_\-]", token):
                    if len(part) >= 4 and part not in STOP:
                        self.postings[part].append(index)
        for phrase in re.findall(r"[\"\u201c]([a-z0-9][a-z0-9 _\-]{3,40})[\"\u201d]", text):
            self.postings[phrase.strip()].append(index)
        return index




def app_owned(app: str) -> dict[int, str]:
    """file id -> root class, for the roots recorded for this app. First matching root wins; donor mirrors are
    classified as 'mirror' wherever they sit, so a Stage A mirror can never pass as app-local source."""
    roots = json.loads(APP_ROOTS.read_text())["apps"].get(app)
    if not roots:
        return {}
    owned: dict[int, str] = {}
    for rel in CANDIDATES:
        for root_class, prefix in roots:
            if rel == prefix or rel.startswith(prefix.rstrip("/") + "/"):
                owned.setdefault(corpus.ensure(rel), "mirror" if is_mirror(rel) else root_class)
                break
    return owned


def classify_row(row: dict[str, object], terms: list[str], owned: dict[int, str],
                 postings: dict[str, list[int]]) -> dict[str, object]:
    """Credit a tier only when distinctive vocabulary, not common prose, co-occurs in one file.

    Two guards exist because the first measured run was far too generous: Games reported 72% test-asserted and
    Play 70% although neither app exists here. (1) A term appearing in a large share of the app's files is
    generic prose for that app and cannot credit a requirement (document-frequency guard). (2) At least one
    matched term must be identifier-like — a name a developer would actually have typed — because "organisation
    management workflow" appearing in a file is not evidence that a specific requirement is addressed.
    """
    if not terms:
        return {"tier": "NO-PROBE-TERMS", "pointer": None, "terms": []}
    # Frequency guard, chosen from the measured distribution rather than intuition. Measured over Boards
    # (4,240 files in its roots, 785 probe terms): 16.4% of terms do not occur in the app at all, 20% occur in
    # at most one file, 33% in at most ten, but 94% occur in up to 500 files. A term sitting in half the
    # repository says nothing about one requirement, so only terms rarer than half a percent of the app's files
    # may credit a tier - measured as df <= max(10, 0.5% of files), which retains the informative 39%.
    owned_count = max(1, len(owned))
    df_cap = max(10, int(round(0.005 * owned_count)))

    def doc_freq(term: str) -> int:
        return sum(1 for fid in postings.get(term.lower(), []) if fid in owned)

    usable = [term for term in terms if doc_freq(term) <= df_cap] or []

    per_file: dict[int, set[str]] = {}
    for term in usable:
        for fid in postings.get(term.lower(), []):
            if fid in owned:
                per_file.setdefault(fid, set()).add(term.lower())

    def rank(fid: int) -> tuple[int, int]:
        order = {"app-local": 5, "shared": 4, "framework": 3, "tests": 3, "models": 3, "mirror": 1}
        # Test files rank first: a matching test is stronger evidence than a matching source file, and the
        # tier decision below reads the winner. Measured defect of the previous order: a shared-src hit
        # outranked the real test, so a genuinely tested requirement reported CODE-PRESENT.
        return (1 if is_test(corpus.paths[fid]) else 0, order.get(owned[fid], 0))

    if not usable:
        return {"tier": "NO-DISTINCTIVE-TERM", "pointer": None, "terms": terms}
    strong = {fid: found for fid, found in per_file.items() if len(found) >= MIN_TERMS}
    if strong:
        fid = max(strong, key=rank)
        rel = corpus.paths[fid]
        if owned[fid] == "mirror":
            tier = "MIRROR-ONLY"
        elif is_test(rel) and file_has_assertions(fid):
            tier = "TEST-ASSERTED"
        elif is_test(rel):
            tier = "CODE-PRESENT"          # matched in a test file without an assertion: not proof
        else:
            tier = "CODE-PRESENT"
        return {"tier": tier, "pointer": {"root": owned[fid], "path": rel, "terms": sorted(strong[fid]),
                                          "co_occurring_files": len(strong)},
                "terms": terms}
    partial = {fid: found for fid, found in per_file.items() if found}
    if partial:
        fid = max(partial, key=rank)
        if owned[fid] == "mirror":
            return {"tier": "MIRROR-ONLY", "pointer": {"root": "mirror", "path": corpus.paths[fid],
                                                       "terms": sorted(partial[fid])}, "terms": terms}
        return {"tier": "WEAK-SINGLE-TERM", "pointer": {"root": owned[fid], "path": corpus.paths[fid],
                                                        "terms": sorted(partial[fid])}, "terms": terms}
    return {"tier": "NO-EVIDENCE", "pointer": None, "terms": terms}


def is_identifier_like(term: str) -> bool:
    """A term a developer would have typed as a name, not a sentence word."""
    return bool(re.search(r"[A-Z]", term)) or bool(re.search(r"[_.\-/]", term))


def file_has_assertions(fid: int) -> bool:
    rel = corpus.paths[fid]
    try:
        text = (ROOT / rel).read_text(encoding="utf-8", errors="ignore")
    except OSError:
        return False
    return bool(ASSERT_RE.search(text))


corpus = Corpus()          # single shared index for a scoring run


def sample_rows(payload: dict[str, object], count: int, seed: int, app_filter: list[str],
                tiers: tuple[str, ...]) -> list[dict[str, object]]:
    """A reproducible random sample for hand-auditing, so each tier gets a MEASURED precision.

    The probe cannot be trusted to hand down a verdict on its own, and this session measured why: with no
    frequency guard it credited Games with 72% test-asserted although no Games source exists; with the strictest
    guard it fell to 10% presence across the board. Both are wrong. So the tiers report how many requirements the
    probe places in each tier, and a hand audit of a fixed random sample measures how often each tier is right.
    """
    rng = random.Random(seed)
    rows = [r for r in payload["assessments"]
            if (not app_filter or r["app"] in app_filter) and r["tier"] in tiers]
    return rng.sample(rows, min(count, len(rows)))


def load_overrides(app: str) -> dict[str, dict[str, object]]:
    path = OVERRIDE_DIR / f"{app}.json"
    if not path.is_file():
        return {}
    usable: dict[str, dict[str, object]] = {}
    for row in json.loads(path.read_text()).get("assessments", []):
        if not row.get("evidence"):
            raise SystemExit(f"REFUSED: override {row.get('id')} in {path.relative_to(ROOT)} has no evidence. "
                             "A hand-made state must state what was observed.")
        usable[str(row["id"])] = row
    return usable


def score(app_filter: list[str] | None = None, write_artifacts: bool = True) -> dict[str, object]:
    """Measure the evidence tiers.

    `write_artifacts=False` is for tests. A scoring run with no indexed corpus is a setup failure, not a
    result: measured on 31 October 2026, a test that re-scored without populating CANDIDATES overwrote
    coverage-summary.json with every app reporting NO-EVIDENCE, and then compared that degenerate output
    against the file it had just corrupted - a green test proving nothing. The refusal below makes that
    shape of failure impossible to publish.
    """
    global CANDIDATES
    if not CANDIDATES:
        CANDIDATES = candidates()
    register = json.loads(REGISTER.read_text())
    rows = [r for r in register["requirements"] if r["level"] == "MUST"]
    apps = sorted({str(r["app"]) for r in rows})
    results: list[dict[str, object]] = []
    for app in apps:
        if app_filter and app not in app_filter:
            continue
        owned = app_owned(app)
        for fid in owned:
            corpus.index(corpus.paths[fid])
        overrides = load_overrides(app)
        for row in [r for r in rows if r["app"] == app]:
            override = overrides.get(str(row["id"]))
            if override:
                assessment = {"tier": str(override["state"]).upper(),
                              "pointer": {"root": "owner-review", "path": str(override["evidence"]),
                                          "terms": [], "co_occurring_files": 0},
                              "terms": [], "reason": str(override.get("reason", ""))}
            else:
                assessment = classify_row(row, probe_terms(str(row["text"])), owned, corpus.postings)
            assessment.update({"id": row["id"], "app": app, "scope": row["scope"],
                               "group": row.get("group", ""), "line": row["line"],
                               "sources_considered": len(owned)})
            results.append(assessment)
    per_app: dict[str, Counter] = defaultdict(Counter)
    for result in results:
        per_app[str(result["app"])][str(result["tier"])] += 1
    summary = {}
    for app, counts in per_app.items():
        total = sum(counts.values()) or 1
        strong = counts["TEST-ASSERTED"] + counts["CODE-PRESENT"]
        summary[app] = {"must_rows": total, **{tier: counts[tier] for tier in
                                               ("TEST-ASSERTED", "CODE-PRESENT", "MIRROR-ONLY",
                                                "WEAK-SINGLE-TERM", "NO-EVIDENCE", "NO-PROBE-TERMS",
                                                "NO-DISTINCTIVE-TERM")},
                        "override_states": {tier: counts[tier] for tier in
                                            ("PARTIAL", "IMPLEMENTED", "NOT-IMPLEMENTED") if counts[tier]},
                        "code_presence_pct": round(100 * strong / total, 1),
                        "test_asserted_pct": round(100 * counts["TEST-ASSERTED"] / total, 1),
                        "no_evidence_pct": round(100 * counts["NO-EVIDENCE"] / total, 1),
                        "mirror_only_pct": round(100 * counts["MIRROR-ONLY"] / total, 1)}
    if not corpus.paths:
        raise SystemExit("REFUSED: no files were indexed, so no evidence tier can be published. Check "
                         "app-roots.json and that the workspace is present.")
    payload = {"_meta": {
        "evidence_level": "static text-evidence tiers over first-party source: not a rendering, not a run, "
                          "not packaging, not acceptance, and never a claim that behaviour is correct",
        "meaning": ("Static evidence tiers over the pinned specification register. CODE-PRESENT means the "
                    "requirement's distinctive vocabulary appears in first-party source; it does NOT prove "
                    "behaviour, and nothing here is on-screen, packaged or accepted. NO-EVIDENCE means that "
                    "vocabulary appears in none of the app's recorded roots, which one counterexample disproves."),
        "registerSpecSha256": register["_meta"]["specSha256"],
        "tiers": list(TIERS), "min_terms_for_tier": MIN_TERMS,
        "files_indexed": len(corpus.paths),
    }, "summary": summary, "assessments": results}
    if not write_artifacts:
        # Tests must never move the published evidence. Measured defect of the first version of this guard: the
        # summary write stayed unconditional while only the row dump was gated, so an in-test re-score overwrote
        # coverage-summary.json - the committed record - with whatever synthetic state the test happened to have
        # on disk. The comparison test then passed by comparing a file it had just written.
        return payload

    OUT.write_text(json.dumps(payload, indent=2) + "\n")
    # The compact summary is the committed record; the full row set is a working file. The summary carries the
    # same provenance so a number in a report can always be traced back to the specification pin that made it.
    SUMMARY_OUT.write_text(json.dumps({
        "_meta": payload["_meta"],
        "specification": json.loads(REGISTER.read_text())["_meta"]["specification"],
        "summary": summary,
        "honesty_note": (
            "The band between the strict and loose probe settings is itself the measured finding: static text "
            "matching cannot decide implementation state. NO-EVIDENCE rows are the reliable claim (the "
            "requirement's distinctive vocabulary is absent from the app's recorded roots); CODE-PRESENT rows "
            "are candidate evidence awaiting a hand verdict; audited precision of the credited tiers is in "
            "audit.json and docs/development/per-app-measured-coverage-2026-10-31.md. Nothing here is on-screen "
            "behaviour, packaging or acceptance."),
    }, indent=2) + "\n")
    return payload


def verify_pointers() -> list[str]:
    """Re-check that every stored pointer still resolves in the repository as it stands."""
    data = json.loads(OUT.read_text())
    problems: list[str] = []
    for row in data["assessments"]:
        pointer = row.get("pointer")
        if not pointer or pointer.get("root") in (None, "owner-review"):
            continue
        path = ROOT / pointer.get("path", "")
        if not path.is_file():
            problems.append(f"{row['id']}: pointer path absent: {pointer.get('path')}")
            continue
        text = path.read_text(encoding="utf-8", errors="ignore").lower()
        missing = [term for term in pointer.get("terms", []) if term.lower() not in text]
        if missing:
            problems.append(f"{row['id']}: pointer terms not found in {pointer['path']}: {missing}")
    return problems


def main(argv=None) -> int:
    global CANDIDATES
    if not CANDIDATES:
        CANDIDATES = candidates()
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("mode", nargs="?", default="score",
                    choices=["score", "row", "verify-pointers", "terms", "sample", "precision"])
    ap.add_argument("target", nargs="?", default=None)
    ap.add_argument("--app", action="append", default=[])
    ap.add_argument("--seed", type=int, default=20261031)
    ap.add_argument("--tier", default="TEST-ASSERTED,CODE-PRESENT,WEAK-SINGLE-TERM,MIRROR-ONLY")
    ap.add_argument("--json", action="store_true")
    args = ap.parse_args(argv)

    if args.mode == "verify-pointers":
        if not OUT.is_file():
            print("REFUSED: no assessment.json yet; run `score` first", file=sys.stderr)
            return 2
        problems = verify_pointers()
        for problem in problems[:40]:
            print("STALE:", problem)
        print(f"pointer verification: {len(problems)} stale row(s)")
        return 1 if problems else 0

    if args.mode == "sample":
        if not OUT.is_file():
            print("REFUSED: run `score` first", file=sys.stderr)
            return 2
        payload = json.loads(OUT.read_text())
        chosen = sample_rows(payload, int(args.target or 20), args.seed, args.app,
                             tuple(args.tier.split(",")))
        register = {r["id"]: r for r in json.loads(REGISTER.read_text())["requirements"]}
        print(f"sampled {len(chosen)} row(s), seed={args.seed}, tiers={args.tier}")
        for row in chosen:
            spec_row = register.get(row["id"], {})
            pointer = row.get("pointer") or {}
            print(f"\n[{row['tier']}] {row['id']}  ({row['app']}, spec L{row['line']})")
            print(f"    REQUIREMENT: {spec_row.get('text', '')[:220]}")
            print(f"    POINTER    : {pointer.get('root')} {pointer.get('path')}")
            print(f"    TERMS      : {pointer.get('terms')}")
            path = ROOT / str(pointer.get("path", ""))
            if path.is_file():
                text = path.read_text(encoding="utf-8", errors="ignore").splitlines()
                hits = [(number, line) for number, line in enumerate(text, 1)
                        if any(term.lower() in line.lower() for term in (pointer.get("terms") or []))]
                for number, line in hits[:3]:
                    print(f"    L{number:>5}: {line.strip()[:170]}")
        return 0

    if args.mode == "precision":
        audit = HERE / "audit.json"
        if not audit.is_file():
            print("REFUSED: no audit.json; hand-verdicts must be recorded before precision can be measured",
                  file=sys.stderr)
            return 2
        data = json.loads(audit.read_text())
        per_tier: dict[str, list[int]] = defaultdict(lambda: [0, 0, 0])   # judged-true, judged-false, verdictless
        for row in data["verdicts"]:
            bucket = per_tier[str(row["tier"])]
            if row.get("verdict") is True:
                bucket[0] += 1
            elif row.get("verdict") is False:
                bucket[1] += 1
            else:
                bucket[2] += 1
        print("measured tier precision (hand-audited, this audit only):")
        for tier, (t, f, u) in sorted(per_tier.items()):
            judged = t + f
            precision = (t / judged) if judged else 0.0
            print(f"  {tier:<18} audited {judged:>3}  true {t:>3}  false {f:>3}  precision {precision:.2f}"
                  + (f"   unresolved {u}" if u else ""))
            data.setdefault("precision", {})[tier] = {"audited": judged, "true": t, "false": f,
                                                      "precision": round(precision, 3)}
        audit.write_text(json.dumps(data, indent=2) + "\n")
        summary = json.loads(OUT.read_text())["summary"] if OUT.is_file() else {}
        precision_map = data.get("precision", {})
        print("\nper app: measured tier counts scaled by the audited precision of each tier")
        print(f"  {'app':<26} {'MUST':>5} {'probe-credited':>15} {'band (audited precision)':>26}")
        for app, row in sorted(summary.items(), key=lambda kv: -kv[1]["must_rows"]):
            credited = row["TEST-ASSERTED"] + row["CODE-PRESENT"] + row["MIRROR-ONLY"] + row["WEAK-SINGLE-TERM"]
            band_lo = sum(row[tier] * precision_map.get(tier, {}).get("precision", 0.0)
                          for tier in ("TEST-ASSERTED", "CODE-PRESENT"))
            band_hi = sum(row[tier] * precision_map.get(tier, {}).get("precision", 1.0)
                          for tier in ("TEST-ASSERTED", "CODE-PRESENT"))
            total = row["must_rows"] or 1
            print(f"  {app:<26} {total:>5} {credited:>15} "
                  f"{100*band_lo/total:>11.1f}% .. {100*band_hi/total:<11.1f}%")
        return 0

    if args.mode == "terms":
        if not args.target:
            print("usage: assess.py terms \"<requirement text>\"", file=sys.stderr)
            return 2
        print(json.dumps(probe_terms(args.target), indent=2))
        return 0

    if args.mode == "row":
        if not args.target or not OUT.is_file():
            print("usage: assess.py row <requirement-id-substring>   (after `score`)", file=sys.stderr)
            return 2
        data = json.loads(OUT.read_text())
        found = next((r for r in data["assessments"] if args.target in str(r["id"])), None)
        if not found:
            print(f"no assessment row matching {args.target}", file=sys.stderr)
            return 1
        print(json.dumps(found, indent=2))
        register = json.loads(REGISTER.read_text())
        row = next((r for r in register["requirements"] if r["id"] == found["id"]), None)
        if row:
            print(f"SPEC L{row['line']} [{row['app']}] {row['text']}")
        return 0

    payload = score(args.app or None)
    summary = payload["summary"]
    if args.json:
        print(json.dumps(summary, indent=2))
        return 0
    print(f"MUST requirements assessed: {sum(v['must_rows'] for v in summary.values())} across "
          f"{len(summary)} app buckets; {payload['_meta']['files_indexed']} files indexed")
    print(f"\n  {'app':<26} {'rows':>5} {'test':>5} {'code':>5} {'weak':>5} {'none':>5} {'unsearch':>8} "
          f"{'credited%':>9} {'test%':>6}")
    for app, row in sorted(summary.items(), key=lambda kv: -kv[1]["must_rows"]):
        print(f"  {app:<26} {row['must_rows']:>5} {row['TEST-ASSERTED']:>5} {row['CODE-PRESENT']:>5} "
              f"{row['WEAK-SINGLE-TERM']:>5} {row['NO-EVIDENCE']:>5} {row['NO-PROBE-TERMS'] + row['NO-DISTINCTIVE-TERM']:>8} "
              f"{row['code_presence_pct']:>9} {row['test_asserted_pct']:>6}")
    print("\nStatic evidence tiers only. CODE-PRESENT is vocabulary present in first-party source, never "
          "verified behaviour; nothing here is rendered, packaged or accepted; acceptance stays NOT RUN.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
