"""Measure the donor audits and compare them against the recorded ratchet baselines.

check  - runs the three donor audits read-only and fails if any problem count exceeds its baseline.
record - re-measures and writes the baseline file (requires --write; the diff is printed first).

Nothing here clones, fetches, builds, installs or accepts anything.
"""

import argparse
import json
import subprocess
import sys
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BASELINE = ROOT / "eng/development/donor_audit_baselines.json"


def run(*args):
    return subprocess.run([sys.executable, "-B", *args], cwd=ROOT, capture_output=True, text=True)


def measure():
    gate = run("eng/check-donor-sources.py", "--local-only")
    gate_issues = sum(1 for l in (gate.stdout + gate.stderr).splitlines() if l.startswith("FAIL:"))

    census = json.loads(run("eng/development/donor_policy.py", "census", "--repo", ".",
                            "--json").stdout)
    coverage = json.loads(run("eng/development/donor_coverage.py", "audit", "--repo", ".",
                              "--json").stdout)

    return {
        "donor_gate_issues": gate_issues,
        "census_violations": len(census["violations"]),
        "census_findings": len(census["findings"]),
        "coverage_problems": len(coverage["problems"]),
        "counts": {"census_rows": len(census["donors"]),
                   "coverage_designations": coverage["counts"]["designations"],
                   "distinct_designated_donors": coverage["counts"]["distinct_designated_donors"],
                   "registry_bindings": coverage["counts"]["registry_bindings"]},
        "findings": census["findings"],
        "coverage_problems_list": coverage["problems"],
    }


def gitsha():
    return subprocess.run(["git", "-C", str(ROOT), "rev-parse", "--short", "HEAD"],
                          capture_output=True, text=True).stdout.strip()


def breaches(baseline, measured):
    """Where a measured run is worse than the recorded ratchet."""
    out = []
    for key in ("donor_gate_issues", "census_violations", "coverage_problems"):
        limit, actual = baseline.get(key), measured[key]
        if limit is None:
            out.append(f"{key} has no recorded baseline")
        elif actual > limit:
            out.append(f"{key} grew from {limit} to {actual}")
    return out


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("verb", choices=["check", "record"])
    ap.add_argument("--write", action="store_true",
                    help="required with record; without it the new baselines are only printed")
    args = ap.parse_args(argv)

    if not BASELINE.is_file():
        print(f"REFUSED: no baseline file at {BASELINE}", file=sys.stderr)
        return 2
    baseline = json.loads(BASELINE.read_text())
    now = measure()

    print(f"measured at {gitsha()}: gate={now['donor_gate_issues']} "
          f"census_violations={now['census_violations']} census_findings={now['census_findings']} "
          f"coverage_problems={now['coverage_problems']}")
    print(f"counts: {now['counts']}")
    for label, items in (("census findings", now["findings"]),
                         ("coverage problems", now["coverage_problems_list"])):
        if items:
            print(f"\n{label}:")
            for i in items:
                print(f"  - {i}")

    if args.verb == "record":
        if not args.write:
            print("\nREFUSED: record needs --write; nothing has been changed", file=sys.stderr)
            return 2
        before = {k: baseline.get(k) for k in
                  ("donor_gate_issues", "census_violations", "coverage_problems")}
        baseline.update({"donor_gate_issues": now["donor_gate_issues"],
                         "census_violations": now["census_violations"],
                         "coverage_problems": now["coverage_problems"],
                         "_measured_on": date.today().isoformat(),
                         "_measured_at_source": gitsha()})
        BASELINE.write_text(json.dumps(baseline, indent=2, ensure_ascii=False) + "\n")
        print(f"\nbaseline updated {before} -> "
              f"{{'donor_gate_issues': {now['donor_gate_issues']}, "
              f"'census_violations': {now['census_violations']}, "
              f"'coverage_problems': {now['coverage_problems']}}}")
        return 0

    regressions = breaches(baseline, now)
    if regressions:
        print("\nRATCHET BREACH (new donor drift, not pre-existing debt):", file=sys.stderr)
        for r in regressions:
            print(f"  - {r}", file=sys.stderr)
        print("  Fix the new drift, or lower the baseline in the same change only when the previous "
              "number was itself wrong - and say which.", file=sys.stderr)
        return 1
    print("\nno ratchet breach: every audit is at or below its recorded baseline")
    return 0


if __name__ == "__main__":
    sys.exit(main())
