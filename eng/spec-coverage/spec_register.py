#!/usr/bin/env python3
"""Build the exact per-app requirement denominator from the canonical specification.

Why this exists: the owner asked on 31 October 2026 for "per app measured percentages" rather than the rough
ranges I had been quoting. A percentage needs three separate things, and conflating any two of them makes the
number meaningless:

  1. a DENOMINATOR: how many normative requirements does this app actually own;
  2. a STATE per requirement, with an evidence pointer that resolves;
  3. a separate ACCEPTANCE state, which stays almost entirely NOT RUN because independent acceptance is paused.

This tool produces (1) only. It asserts nothing about implementation.

Parsing rules, stated so they can be argued with rather than guessed at:
  * fenced code blocks are not requirement text;
  * a requirement unit is one logical item: a list item plus its wrapped continuation lines, or a paragraph. A
    stem ending in ":" keeps its following bullets inside the same unit, because those bullets carry no
    independent normative keyword and would otherwise inflate the count;
  * units are split into SENTENCES and a sentence counts once. A sentence containing MUST and MAY is one
    MUST-level requirement, not two;
  * NORMATIVE LEVELS follow the specification's own "Normative requirement language" section: MUST/MUST NOT/
    SHALL/SHALL NOT/REQUIRED are mandatory ("MUST"); SHOULD/SHOULD NOT/RECOMMENDED are required-by-default
    ("SHOULD"); MAY/OPTIONAL are intentionally optional ("MAY"). That section states that lowercase forms carry
    the same meaning, so lowercase is matched too;
  * the implementation denominator is the MUST set. SHOULD and MAY are reported separately and never merged in;
  * GROUP: the specification also uses id-prefixed markers ("ADM-01 — Product role…", "TF-L01 — Link shell…")
    as group titles. Some are markdown headings, some are plain paragraph lines. Both are tracked, so a
    requirement carries its group id where one exists;
  * APP attribution comes from the enclosing section, with recorded overrides for the id-prefixed families,
    because three families sit under headings that do not name the product (measured: ADM-01..ADM-12 sit
    directly under "Other independent apps and shared capabilities" with no heading at all, which made 56
    requirements look ownerless);
  * SCOPE has three values, not two: in-scope, out-of-scope, and UNRESOLVED where the owner's scoping answer
    does not name the app. UNRESOLVED is reported as a question; it is never folded into either side.

Run:  python3 -B eng/spec-coverage/spec_register.py build
      python3 -B eng/spec-coverage/spec_register.py summary [--json]
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SPEC = ROOT / "docs/specification/9to1-development-specification-v2.md"
PIN = Path(__file__).resolve().parent / "spec-pin.json"
DEFAULT_OUT = Path(__file__).resolve().parent / "register.json"

# Pinned authority. AGENTS.md names this exact digest; the register is only valid while the file still hashes
# to it, and a drift must be re-pinned deliberately rather than absorbed.
EXPECTED_SHA256 = "e1779d226848da492e926ab70e8d4722ad444476cfc91312bf5c2c067654fe81"

FENCE = re.compile(r"^\s*(```|~~~)")
HEADING = re.compile(r"^(#{1,6})\s+(.*)$")
LIST_ITEM = re.compile(r"^\s*(?:[*+-]|\d+[.)])\s+(.*)$")
STEMLINE = re.compile(r"^[^\n]*[:：]\s*$")
# id-prefixed group marker, heading or plain paragraph: TF-L01, SPUI-133, ADM-01, WRITE-UX-101
GROUP_MARKER = re.compile(r"^(?:#{1,6}\s+)?\s*((?:[A-Z][A-Z0-9]*-){1,3}[A-Z]?\d{1,4})\s*[—–:\u2014-]\s*(\S.*)$")

MUST_WORDS = r"MUST NOT|MUST|SHALL NOT|SHALL|REQUIRED TO|REQUIRED|MANDATORY"
SHOULD_WORDS = r"SHOULD NOT|SHOULD|RECOMMENDED"
MAY_WORDS = r"MAY NOT|MAY|OPTIONAL"
ABBREVIATIONS = {"e.g.", "i.e.", "etc.", "vs.", "approx.", "no.", "St.", "Dr.", "Mr.", "Inc.", "Ltd.", "U.S."}


def level_of(sentence: str) -> str | None:
    """Normative level of one sentence, per the specification's own key-words section."""
    if re.search(rf"\b({MUST_WORDS})\b", sentence) or re.search(r"\b(must not|must|required to|required)\b", sentence):
        return "MUST"
    if re.search(rf"\b({SHOULD_WORDS})\b", sentence) or re.search(r"\b(should not|should|recommended)\b", sentence):
        return "SHOULD"
    if re.search(rf"\b({MAY_WORDS})\b", sentence) or re.search(r"\b(may not|may|optional|optionally)\b", sentence):
        return "MAY"
    return None


def sentences(text: str) -> list[str]:
    """Split requirement prose into sentences, avoiding the measured traps: decimals, ellipses, single-letter
    abbreviations, and list markers that look like "a." or "1."."""
    out: list[str] = []
    buf: list[str] = []
    tokens = re.split(r"(\s+)", text)
    for index, token in enumerate(tokens):
        buf.append(token)
        if not token.endswith("."):
            continue
        stripped = token.strip()
        if stripped in ABBREVIATIONS or re.fullmatch(r"[A-Za-z]\.", stripped):
            continue   # "a." / "b." enumerations and single-letter initials are not sentence ends
        if stripped.endswith(".."):
            continue
        if re.search(r"\d\.$", stripped) and not re.fullmatch(r"\d+\.", stripped):
            continue
        following = next((t for t in tokens[index + 1:] if t.strip()), "")
        if following[:1].isupper() or following[:1] in "\"'(*-" or not following:
            out.append("".join(buf).strip())
            buf = []
    if "".join(buf).strip():
        out.append("".join(buf).strip())
    return [re.sub(r"\s+", " ", s).strip() for s in out if s.strip()]


# --- app and scope maps ---------------------------------------------------------------------------
# Section -> app. Keys are exact `## ` section titles. Recorded, reviewable, not a keyword guess.
SECTION_APPS: dict[str, tuple[str, str]] = {
    "Authority and scope": ("cross-cutting", "in"),
    "Product identities and ownership": ("cross-cutting", "in"),
    "Development priority and dependency order": ("cross-cutting", "in"),
    "CUI application-authoring mandate": ("cross-cutting", "in"),
    "Completion and acceptance": ("cross-cutting", "in"),
    "Design references": ("cross-cutting", "in"),
    "General engineering, privacy and acceptance contracts": ("cross-cutting", "in"),
    "Haven and shared platform services": ("Haven", "in"),
    "CUI language, presentation and shared editor framework": ("CUI", "in"),
    "Shared application lifecycle, launch and modular packaging": ("packaging", "UNRESOLVED"),
    "Shared models, settings and resource controls": ("shared-settings", "UNRESOLVED"),
    "Dulche, nodes and agentic runtime": ("Dulche", "UNRESOLVED"),
    "Spaces and Space Builder": ("Spaces", "in"),
    "Study Space": ("Study Space", "UNRESOLVED"),
    "Voice & Vision": ("Voice & Vision", "in"),
    "Build": ("Build", "in"),
    "Automations": ("Automations", "in"),
    "Write": ("Write", "in"),
    "Present": ("Present", "in"),
    "Boards": ("Boards", "in"),
    "Canvas": ("Canvas", "in"),
    "Data": ("Data", "in"),
    "Files": ("Files", "in"),
    "Accounts, web services, cloud storage and integrations": ("Accounts", "in"),
    "Planner": ("Planner", "provisional"),
    "Connect and Mail": ("Connect", "in"),
    "Cards": ("Cards", "provisional"),
    "Forms": ("Forms", "UNRESOLVED"),
    "AI Studio": ("AI Studio", "in"),
    "Picture": ("Picture", "out"),
    "Wave": ("Wave", "out"),
    "Motion": ("Motion", "out"),
    "Games": ("Games", "out"),
    "Play": ("Play", "out"),
    "Mini Computer": ("Mini Computer", "out"),
    "Other independent apps and shared capabilities": ("other apps", "UNRESOLVED"),
    "9to1 OS distribution": ("9to1 OS", "out"),
    "Explicitly deferred scope": ("deferred", "out"),
    "Open product and release decisions": ("open decisions", "out"),
}

# id-prefixed family -> (app, scope, why the override exists).
#
# RULE, corrected after measurement: the enclosing section OWNS a requirement; the id family is metadata. The
# first version of this table also overrode LE, SH and SPUI, and that stole requirements from the app that
# holds them - measured: the Cards section's own LE-10 lifecycle requirements moved to "packaging" (Cards MUST
# fell 9 -> 3), and 136 SPUI requirements were lifted out of Spaces, Dulche and shared-settings into a bucket
# no app owns. Only two families genuinely need an override, and only because the section they sit in names no
# product at all:
FAMILY_OVERRIDES: dict[str, tuple[str, str, str]] = {
    "ADM": ("Admin", "UNRESOLVED",
            "measured: ADM-01...ADM-12 (L15512-L15590) sit directly under '## Other independent apps and shared "
            "capabilities' with no heading between them and that heading, so 56 requirements had no owner"),
    "TF": ("TF (spec names no product)", "UNRESOLVED",
           "measured: '## Other independent apps and shared capabilities' > '### Link/Prototype/Paths/Replay/"
           "Variants' all carry TF-* markers, and L1037 'TF-X05 - Open naming decision and implementation "
           "defaults' keeps the product name OPEN while L947 calls it one mandatory shipping product, so "
           "inventing a name here would contradict the specification"),
}

# Families whose requirements stay with their section's app, and are reported as a cross-cutting GROUP view in
# the summary instead of being re-parented.
GROUP_FAMILIES_KEPT_WITH_SECTION = ("LE", "SH", "SPUI", "GOV", "APP-VIS")

# Which apps the owner's Shipmas scoping names, in the owner's words. Kept so the scope column can be
# re-derived and audited, and so UNRESOLVED stays a question rather than a silent choice.
SCOPE_PROVENANCE = {
    "in": "owner scoping 30/31 October 2026: Haven and the frameworks under it, Spaces (all six built-in "
          "Spaces), Voice & Vision, Automations, Accounts/Subscriptions, Connect as email only, Build, Write, "
          "Present, Boards, Canvas, Data, Files, AI Studio",
    "provisional": "owner: Cards then Planner as provisional",
    "out": "owner scoping 30/31 October 2026: Games, Play, Picture, Wave, Motion, Mini Computer and 9to1 OS "
           "distribution are out for this delivery",
    "UNRESOLVED": "the owner's scoping list does not name this app, and the specification treats it as "
                  "mandatory; asked rather than decided - see the open-decisions register",
}


def top_section_of(path: str) -> str:
    """The enclosing `## ` section. parts[0] is the `# ` book title, so parts[1] is the section."""
    parts = [part for part in path.split(" > ") if part]
    return parts[1] if len(parts) > 1 else (parts[0] if parts else "")


# Sections that name a bucket, not a product: only these may be re-attributed by id family.
PRODUCTLESS_SECTIONS = {"Other independent apps and shared capabilities"}


def app_and_scope(path: str, group: str | None) -> tuple[str, str]:
    """App ownership comes from the section; the id family only fills a gap where the section names no product."""
    top = top_section_of(path)
    entry = SECTION_APPS.get(top)
    if entry is None:
        return "UNATTRIBUTED", "UNRESOLVED"
    if group and top in PRODUCTLESS_SECTIONS:
        override = FAMILY_OVERRIDES.get(group.split("-")[0])
        if override:
            return override[0], override[1]
    return entry


def requirement_id(number: int, sentence: str, group: str | None) -> str:
    digest = hashlib.sha256(re.sub(r"\s+", " ", sentence).strip().lower().encode()).hexdigest()[:10]
    return f"{group or 'G0'}:L{number}-{digest}"


def parse(text: str) -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    stack: dict[int, str] = {}
    in_fence = False
    pending: tuple[int, list[str]] | None = None
    pending_is_stem = False
    group: str | None = None
    group_title = ""

    def path() -> str:
        return " > ".join(stack[d] for d in sorted(stack))

    def emit_group_title(number: int) -> None:
        """A group title is usually not a requirement, but some of them carry a normative keyword.

        Measured shapes: "SPUI-133 — MODEL REASONING EFFORT DISPLAY" is a label, while "GOV-002 — Mandatory
        release gates. No RCVP or Full PASS unless (1) CUI static and runtime conformance ... MUST ..." states
        a requirement. Skipping the whole line would lose that MUST, so the title is scanned and only its
        normative sentences are recorded, flagged from_group_title.
        """
        if not group_title:
            return
        for sentence in sentences(group_title):
            level = level_of(sentence)
            if level is None:
                continue
            app, scope = app_and_scope(path(), group)
            rows.append({"id": requirement_id(number, sentence, group), "group": group or "",
                         "group_title": group_title, "line": number, "section": path(),
                         "top_section": top_section_of(path()), "app": app, "scope": scope,
                         "level": level, "text": sentence, "from_group_title": True})

    def flush() -> None:
        nonlocal pending, pending_is_stem
        if pending is None:
            return
        body = " ".join(pending[1]).strip()
        for sentence in sentences(body):
            level = level_of(sentence)
            if level is None:
                continue
            app, scope = app_and_scope(path(), group)
            rows.append({"id": requirement_id(pending[0], sentence, group), "group": group or "",
                         "group_title": group_title, "line": pending[0], "section": path(),
                         "top_section": top_section_of(path()), "app": app, "scope": scope,
                         "level": level, "text": sentence})
        pending, pending_is_stem = None, False

    for number, raw in enumerate(text.splitlines(), start=1):
        if FENCE.match(raw):
            in_fence = not in_fence
            flush()
            continue
        if in_fence:
            continue
        heading = HEADING.match(raw)
        if heading:
            flush()
            depth, title = len(heading.group(1)), heading.group(2).strip()
            stack[depth] = title
            for deeper in [d for d in stack if d > depth]:
                del stack[deeper]
            marker = GROUP_MARKER.match(raw)
            if marker:
                group, group_title = marker.group(1), marker.group(2)
                emit_group_title(number)
            elif depth <= 3:
                group, group_title = None, ""
            continue
        if not raw.strip():
            flush()
            continue
        marker = GROUP_MARKER.match(raw)
        if marker and len(marker.group(0)) <= 150 and not LIST_ITEM.match(raw):
            flush()
            group, group_title = marker.group(1), marker.group(2)
            emit_group_title(number)
            continue
        item = LIST_ITEM.match(raw)
        if item:
            if pending_is_stem:
                pending[1].append(item.group(1))
                continue
            flush()
            pending = (number, [item.group(1)])
            pending_is_stem = bool(STEMLINE.match(item.group(1)))
            continue
        if pending is not None and not raw.startswith(("|", ">")):
            pending[1].append(raw.strip())
            if STEMLINE.match(raw.strip()):
                pending_is_stem = True
            continue
        if raw.startswith(("|", ">")):
            flush()
            continue
        flush()
        pending = (number, [raw.strip()])
        pending_is_stem = bool(STEMLINE.match(raw.strip()))
    flush()

    seen: dict[str, int] = {}
    for row in rows:
        if row["id"] in seen:
            seen[row["id"]] += 1
            row["id"] = f"{row['id']}#{seen[row['id']]}"
        else:
            seen[row["id"]] = 0
    return rows


def measure() -> tuple[list[dict[str, object]], dict[str, int]]:
    text = SPEC.read_text(encoding="utf-8")
    measured_sha = hashlib.sha256(SPEC.read_bytes()).hexdigest()
    if measured_sha != EXPECTED_SHA256:
        raise SystemExit(f"REFUSED: specification digest {measured_sha[:12]}… does not match the pinned "
                         f"authority {EXPECTED_SHA256[:12]}… Re-pin deliberately; do not silently rebase.")
    in_fence, lines_with_keyword, must_lines = False, 0, 0
    for raw in text.splitlines():
        if FENCE.match(raw):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        level = level_of(raw)
        if level is None:
            continue
        lines_with_keyword += 1
        if level == "MUST":
            must_lines += 1
    return parse(text), {"lines_with_keyword": lines_with_keyword, "must_lines": must_lines,
                         "spec_sha256": measured_sha}


def summarise(rows: list[dict[str, object]], line_counts: dict[str, int]) -> dict[str, object]:
    by_level = Counter(str(r["level"]) for r in rows)
    per_app: dict[str, dict[str, int]] = defaultdict(lambda: {"MUST": 0, "SHOULD": 0, "MAY": 0, "must_scope": {}})
    for r in rows:
        bucket = per_app[str(r["app"])]
        bucket[str(r["level"])] += 1
        if r["level"] == "MUST":
            bucket["must_scope"][str(r["scope"])] = bucket["must_scope"].get(str(r["scope"]), 0) + 1
    scopes = ("in", "provisional", "out", "UNRESOLVED")
    rollup = {s: sum(b["must_scope"].get(s, 0) for b in per_app.values()) for s in scopes}
    return {
        "statements": len(rows), "by_level": dict(by_level),
        "line_level": {"lines_containing_a_normative_keyword": line_counts["lines_with_keyword"],
                       "of_which_must_level": line_counts["must_lines"]},
        "reconciliation": (f"lines vs statements: one line can hold more than one requirement sentence. "
                           f"{line_counts['lines_with_keyword']} keyword lines ({line_counts['must_lines']} "
                           f"MUST-level) resolve to {len(rows)} statements ({by_level.get('MUST', 0)} MUST). "
                           "Quote the statement figure, and say so."),
        "must_by_scope": rollup,
        "scope_provenance": SCOPE_PROVENANCE,
        "per_app": {app: {"MUST": b["MUST"], "SHOULD": b["SHOULD"], "MAY": b["MAY"], "must_by_scope": b["must_scope"]}
                    for app, b in sorted(per_app.items(), key=lambda kv: -kv[1]["MUST"])},
        "unattributed": sorted({str(r["top_section"]) for r in rows if r["app"] == "UNATTRIBUTED"}),
        "group_families": dict(Counter(str(r["group"]).split("-")[0] for r in rows
                                       if r["group"]).most_common()),
        "group_families_note": ("families are metadata, not ownership: LE/SH/SPUI/GOV/APP-VIS requirements "
                                "belong to the app whose section holds them, and are listed here so a shared "
                                "family can still be inspected across apps"),
        "groups_tracked": len({str(r["group"]) for r in rows if r["group"]}),
    }


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("mode", nargs="?", default="build", choices=["build", "summary"])
    ap.add_argument("--out", default=str(DEFAULT_OUT))
    ap.add_argument("--json", action="store_true")
    args = ap.parse_args(argv)

    rows, line_counts = measure()
    summary = summarise(rows, line_counts)
    if args.mode == "build":
        Path(args.out).write_text(json.dumps({
            "_meta": {"specification": str(SPEC.relative_to(ROOT)), "specSha256": line_counts["spec_sha256"],
                      "pinnedAuthoritySha256": EXPECTED_SHA256,
                      "tool": "eng/spec-coverage/spec_register.py",
                      "meaning": "DENOMINATOR ONLY. No row here asserts that anything is implemented, tested, "
                                 "rendered, packaged or accepted. The specification remains the only product "
                                 "authority; this file is derived from it."},
            "summary": summary, "requirements": rows}, indent=2) + "\n")
        PIN.write_text(json.dumps({"specification": str(SPEC.relative_to(ROOT)),
                                   "sha256": line_counts["spec_sha256"],
                                   "pinnedBy": "AGENTS.md 'Sole product authority'",
                                   "matchesPinnedDigest": True,
                                   "note": "if the digest changes this tool refuses until the pin is changed "
                                           "deliberately"}, indent=2) + "\n")
        print(f"register written: {args.out} ({len(rows)} normative statements, "
              f"{summary['groups_tracked']} id-marked groups tracked)")
    if args.json:
        print(json.dumps(summary, indent=2))
        return 0
    print(f"line level : {summary['line_level']['lines_containing_a_normative_keyword']} keyword lines, "
          f"{summary['line_level']['of_which_must_level']} MUST-level")
    print(f"statement  : {summary['statements']} total | MUST {summary['by_level'].get('MUST', 0)} | "
          f"SHOULD {summary['by_level'].get('SHOULD', 0)} | MAY {summary['by_level'].get('MAY', 0)}")
    print("MUST by scope:", json.dumps(summary["must_by_scope"]))
    if summary["unattributed"]:
        print("UNATTRIBUTED sections (mapping gap to resolve, not absorb):", summary["unattributed"])
    print("\nper app (MUST / SHOULD / MAY, and MUST split by scope):")
    for app, counts in summary["per_app"].items():
        scope = " ".join(f"{k}={v}" for k, v in sorted(counts["must_by_scope"].items()))
        print(f"  {app:<24} {counts['MUST']:>5} {counts['SHOULD']:>4} {counts['MAY']:>4}   {scope}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
