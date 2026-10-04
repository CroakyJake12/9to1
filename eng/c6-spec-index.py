#!/usr/bin/env python3
"""Lossless canonical-source coverage inventory. Extraction is not semantic approval."""
import argparse, hashlib, json, re
from pathlib import Path
p = argparse.ArgumentParser()
p.add_argument("--spec", required=True)
p.add_argument("--revision", required=True)
p.add_argument("--output", required=True)
a = p.parse_args()
source = Path(a.spec).read_bytes()
revision = json.loads(Path(a.revision).read_text())
out = Path(a.output); out.mkdir(parents=True, exist_ok=True)
rows = []
section = "General Rules"
for n, text in enumerate(source.decode().splitlines(), 1):
    if not text.strip(): continue
    heading = bool(re.match(r"^(?:SH-\d+|TF-[A-Z0-9]+|Pass \d+:|[0-9]+\.[0-9]* ?[A-Z])", text))
    if heading: section = text
    levels = sorted(set(x.upper() for x in re.findall(r"\b(?:must(?: not)?|shall(?: not)?|required|should(?: not)?|recommended|may|optional)\b", text, re.I)))
    tags = [tag for tag, pattern in {
        "acceptance": r"acceptance|test|verify|validation|evidence|journey",
        "donor": r"donor|upstream|parity",
        "platform": r"Windows|Android|Go\b|browser|web|native|9to1-OS|Linux|desktop|mobile",
        "exception-or-open": r"OPEN|OUT OF SCOPE|exempt|exception|deferred|fallback",
    }.items() if re.search(pattern, text, re.I)]
    # Hosted groups have explicit C ownership; shared/product clauses require A/B/C reconciliation.
    owner = "C" if section.startswith("SH-") and not section.startswith("SH-06") else "A/B/C"
    rows.append(dict(id=f"SRC-L{n:05}", source_line=n, source_text=text,
        source_sha256=hashlib.sha256(source).hexdigest(), source_revision=revision["revisionId"],
        section_context=section, normative_levels=levels, tags=tags,
        requirement_state=None, coverage_state="UNMAPPED", blocker="Unmapped source clause; semantic decomposition and behavioural evidence pending",
        owner=owner, ownership_confirmed=False, platforms=[], platform_mapping="UNMAPPED",
        tests=[], test_mapping="UNMAPPED", applicability="UNRESOLVED-INCLUDED",
        exception_basis=None, semantic_review="PENDING"))
with (out/"source-coverage.jsonl").open("w") as f:
    for row in rows: f.write(json.dumps(row, ensure_ascii=False)+"\n")
summary=dict(source_revision=revision["revisionId"], source_sha256=hashlib.sha256(source).hexdigest(),
    total_source_lines=len(source.decode().splitlines()), nonblank_source_clauses=len(rows),
    normative_keyword_clauses=sum(bool(x["normative_levels"]) for x in rows),
    nonkeyword_clauses=sum(not x["normative_levels"] for x in rows),
    acceptance_tagged_clauses=sum("acceptance" in x["tags"] for x in rows),
    states={"VERIFIED":0,"IMPLEMENTED-UNVERIFIED":0,"MISSING":0,"BLOCKED":0},
    classified_requirement_gates=0, unresolved_source_clauses=len(rows),
    unmapped_clauses=len(rows), semantic_review_complete=False,
    warning="Source-clause superset, not a fully decomposed requirement/platform denominator. All list items, examples and table cells retained; no exclusions inferred.")
(out/"coverage-summary.json").write_text(json.dumps(summary, indent=2)+"\n")
print(json.dumps(summary, indent=2))
