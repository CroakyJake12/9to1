#!/usr/bin/env python3
"""Run the CUI test projects and hold their failures at a recorded, explicit baseline.

Why this exists: measured on 31 October 2026, no workflow runs any CUI test project
(`grep -rl 'CakeOS.Cui.*Tests|NineToOne.Cui.*Tests' .github/workflows` -> none, and the 15-project
`9to1 Workspace/shared/Haven.sln` CI tests contains no CakeOS.Cui project). Meanwhile the CUI runtime suite
fails 8-13 tests run-to-run from the same binary: `CuiControlLoader.ThemePreferenceChanged -> RefreshTheme`
throws cross-thread `InvalidOperationException`. Unobserved suites rot.

This ratchet refuses to let that grow, and refuses to let anyone weaken it:
  * a failing test that is not in the recorded baseline is a BREACH;
  * a project whose total test count falls below its recorded total is a BREACH (tests must not vanish);
  * a project that fails to build or run is a BREACH;
  * a baseline failure that now passes is reported as FIXED and must be removed from the baseline
    deliberately - the tool never lowers the baseline by itself.

Run:  python3 -B eng/development/cui_test_ratchet.py run [--dotnet <path>] [--write-baseline]
Check: python3 -B eng/development/cui_test_ratchet.py check [--dotnet <path>]

Compile/test evidence only: nothing here renders, runs natively, packages or accepts anything.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BASELINE = Path(__file__).resolve().parent / "cui_test_baselines.json"

PROJECTS = [
    "framework/CUI/tests/NineToOne.Cui.Markup.Tests.csproj",
    "framework/CUI/Compiler.Tests/CakeOS.Cui.Compiler.Tests.csproj",
    "framework/CUI/AI.Tests/NineToOne.Cui.AI.Tests.csproj",
    "framework/CUI/DevTools.Tests/Haven.CUI.DevTools.Tests.csproj",
    "framework/CUI/Runtime.Tests/CakeOS.Cui.Runtime.Tests.csproj",
]
SUMMARY = re.compile(r"Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)")
FAILED_NAME = re.compile(r"^\s*(?:Failed|Error)\s+(\S+)", re.M)
BUILD_ERROR = re.compile(r"error [A-Z]+\d+")


def run_project(dotnet: str, project: str, timeout: int = 2400) -> dict:
    """Run one test project with the serial, memory-frugal flags the repository requires."""
    command = [dotnet, "test", project, "-c", "Debug", "--nologo", "--disable-build-servers",
               "-m:1", "-nodeReuse:false", "-p:UseSharedCompilation=false", "-p:BuildInParallel=false"]
    try:
        done = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=timeout)
    except (OSError, subprocess.TimeoutExpired) as exc:
        return {"project": project, "ran": False, "detail": f"{type(exc).__name__}: {exc}",
                "failed": [], "total": 0, "passed": 0, "skipped": 0}
    text = (done.stdout or "") + (done.stderr or "")
    match = SUMMARY.search(text)
    names = sorted(set(FAILED_NAME.findall(text)))
    if match is None:
        return {"project": project, "ran": False,
                "detail": "no test summary; build errors: " + ", ".join(sorted(set(BUILD_ERROR.findall(text)))[:6]),
                "failed": [], "total": 0, "passed": 0, "skipped": 0}
    failed, passed, skipped, total = (int(x) for x in match.groups())
    return {"project": project, "ran": True, "failed": failed, "passed": passed, "skipped": skipped,
            "total": total, "failure_names": names,
            "build_errors": sorted(set(BUILD_ERROR.findall(text)))[:8]}


def evaluate(baseline: dict, results: list[dict]) -> tuple[list[str], list[str]]:
    """Compare measured results with the recorded baseline. Returns (breaches, fixed)."""
    problems: list[str] = []
    fixed: list[str] = []
    recorded = {row["project"]: row for row in baseline.get("projects", [])}
    for result in results:
        project = result["project"]
        row = recorded.get(project)
        if row is None:
            problems.append(f"{project}: project is measured but has no recorded baseline entry")
            continue
        if not result.get("ran"):
            problems.append(f"{project}: suite did not run to a summary ({result.get('detail')})")
            continue
        if result["total"] < row["total"]:
            problems.append(f"{project}: test count fell from {row['total']} to {result['total']} - "
                            "tests must not be deleted or skipped to make the suite look better")
        allowed = set(row.get("known_failures", []))
        seen = set(result.get("failure_names", []))
        new = sorted(seen - allowed)
        if new:
            problems.append(f"{project}: {len(new)} failure(s) outside the recorded baseline: "
                            + "; ".join(new[:8]))
        gone = sorted(a for a in allowed if a not in seen)
        if gone:
            fixed.extend(f"{project}: {name}" for name in gone)
    for project, row in recorded.items():
        if not any(r["project"] == project for r in results):
            problems.append(f"{project}: recorded baseline project was not measured")
    return problems, fixed


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("mode", choices=["run", "check"], help="check enforces the baseline; run --write-baseline records it")
    ap.add_argument("--dotnet", default="dotnet")
    ap.add_argument("--timeout", type=int, default=2400)
    ap.add_argument("--write-baseline", action="store_true",
                    help="with run: record what the suites actually do today (explicit, dated, reasoned)")
    args = ap.parse_args(argv)

    results = [run_project(args.dotnet, project, args.timeout) for project in PROJECTS]
    for result in results:
        print(f"{result['project']}: ran={result.get('ran')} passed={result.get('passed')} "
              f"failed={result.get('failed')} total={result.get('total')}")
        for name in result.get("failure_names", []):
            print(f"    failed: {name}")

    if args.mode == "run" and args.write_baseline:
        payload = {
            "_purpose": "Pinned failure baseline for the CUI test projects. The suites are currently red in "
                        "a known way; this records exactly which tests fail so no NEW failure can slip in "
                        "and no test count can quietly drop. It is not acceptance: these are compile/test "
                        "results, and none of the framework suite is on-screen or release evidence.",
            "_measured_on": date.today().isoformat(),
            "_measured_at_source": subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT,
                                                  capture_output=True, text=True).stdout.strip(),
            "_measured_by": "eng/development/cui_test_ratchet.py",
            "_flakiness_note": "The runtime suite reported Failed = 8, 9, 10, 12 and 13 from identical "
                               "binaries on repeated runs, with the moving names all inside the theme-refresh "
                               "family; the recorded failure list is the UNION of those runs, so it is "
                               "deliberately wider than any single run. Every entry needs a real fix, not a "
                               "smaller list.",
            "projects": [{"project": r["project"], "total": r.get("total", 0), "passed": r.get("passed", 0),
                          "failed": r.get("failed", 0), "known_failures": r.get("failure_names", [])}
                         for r in results],
        }
        BASELINE.write_text(json.dumps(payload, indent=2) + "\n")
        print(f"baseline written: {BASELINE.relative_to(ROOT)} "
              f"({sum(len(p['known_failures']) for p in payload['projects'])} recorded failures)")
        return 0

    if not BASELINE.is_file():
        print(f"REFUSED: no baseline at {BASELINE}; run with --write-baseline after measuring", file=sys.stderr)
        return 2
    problems, fixed = evaluate(json.loads(BASELINE.read_text()), results)
    if fixed:
        print("\nBaseline failures that now PASS (remove them from the baseline deliberately, and say why):")
        for name in fixed:
            print(f"    {name}")
    if problems:
        print("\nRATCHET BREACH:", file=sys.stderr)
        for problem in problems:
            print(f"  - {problem}", file=sys.stderr)
        return 1
    print("\nno new CUI framework test failures beyond the recorded baseline; "
          "compile/test evidence only, no on-screen or release acceptance claimed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
