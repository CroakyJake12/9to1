#!/usr/bin/env python3
"""Audit original worker source identities without changing their schemas/states."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
paragraphs = json.loads((ROOT / "source/paragraphs.json").read_text())
canonical = {(row["index"], row["end"]): row for row in paragraphs}
manifest = json.loads((ROOT / "source/manifest.json").read_text())
combined = set()
report = {"source_revision": manifest["revisionId"], "worker_maps": [],
          "inventory_complete": False, "product_parity_verified": False,
          "classification": "Source paragraph and candidate sentence records; counts are not independently reviewed atomic requirement totals."}

for path in sorted(ROOT.glob("b*/requirements.json")):
    if path.parent.name == "b6":
        continue
    document = json.loads(path.read_text())
    rows = document.get("requirements", document.get("rows", [])) if isinstance(document, dict) else document
    defects, keys, states, outcomes = [], set(), {}, {}
    exact_quotes = 0
    for row in rows:
        start = row.get("sourceStart", row.get("source_start", row.get("source_index")))
        end = row.get("sourceEnd", row.get("source_end"))
        key = (start, end)
        original = canonical.get(key)
        text = row.get("requirement", row.get("canonical_text", row.get("canonical_clause", row.get("paragraph", row.get("text", "")))))
        rid = row.get("id", row.get("requirement_id"))
        if original is None or text.strip() != original["text"].strip():
            defects.append({"id": rid, "source": key, "defect": "Canonical quote/range mismatch"})
        else:
            exact_quotes += 1
            keys.add(key)
        revision = row.get("source_revision", document.get("sourceRevision") if isinstance(document, dict) else None)
        if revision != manifest["revisionId"]:
            defects.append({"id": rid, "defect": "Source revision mismatch"})
        state = row.get("state", row.get("requirement_state", "UNSPECIFIED"))
        outcome = row.get("testOutcome", row.get("test_outcome", "UNSPECIFIED"))
        states[state] = states.get(state, 0) + 1
        outcomes[outcome] = outcomes.get(outcome, 0) + 1
    combined |= keys
    report["worker_maps"].append({"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                                  "records_supplied": len(rows), "exact_source_quotes": exact_quotes,
                                  "unique_source_ranges": len(keys), "states": states, "test_outcomes": outcomes,
                                  "integrity_defects": defects,
                                  "acceptance": "NOT VERIFIED: a source/procedure map is not a deployed runtime result."})

report["combined_unique_source_ranges"] = len(combined)
report["nonempty_canonical_paragraphs"] = sum(bool(row["text"].strip()) for row in paragraphs)
report["unrepresented_source_paragraphs"] = [row for row in paragraphs if row["text"].strip() and (row["index"], row["end"]) not in combined]
(ROOT / "b6/worker-map-review.json").write_text(json.dumps(report, indent=2) + "\n")
for worker in report["worker_maps"]:
    print(Path(worker["path"]).parent.name, "records=", worker["records_supplied"], "source_ranges=", worker["unique_source_ranges"],
          "quote/revision defects=", len(worker["integrity_defects"]), "outcomes=", worker["test_outcomes"])
print("Combined source ranges:", len(combined), "; full atomic coverage and product parity remain unestablished.")
