#!/usr/bin/env python3
"""Compile-check every committed Stage A mirror corpus and hold it at its recorded ratchet.

The corpora are committed; the donor sources they were mirrored from are not (they are pinned by exact
revision in eng/donor-sources.json). So this check rebuilds the CUI compiler probe and runs the real
compiler over every mirrored .cui, then compares against eng/cui-mirror/mirror_baselines.json.

Exit codes: 0 no compile errors and no lost mirrors; 1 a ratchet breach; 2 the check itself could not run.
It reports compile evidence only - nothing here renders, runs natively, packages or accepts anything.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BASELINE = Path(__file__).resolve().parent / "mirror_baselines.json"
PROBE_PROJECT = Path(__file__).resolve().parent / "probe" / "CuiMirrorProbe.csproj"
SUMMARY = re.compile(r"documents=(\d+) without-errors=(\d+) with-errors=(\d+)")


def corpora() -> list[str]:
    found = []
    for path in sorted((ROOT / "9to1 Workspace").glob("*/CUI/DonorMirror/*")):
        if path.is_dir() and any(path.rglob("*.cui")):
            found.append(str(path.relative_to(ROOT)))
    return found


def build_probe(dotnet: str) -> str | None:
    build = subprocess.run([dotnet, "build", str(PROBE_PROJECT), "-c", "Release", "--nologo", "-v", "q",
                            "--disable-build-servers", "-m:1", "-nodeReuse:false",
                            "-p:UseSharedCompilation=false", "-p:BuildInParallel=false"],
                           cwd=ROOT, capture_output=True, text=True)
    if build.returncode:
        print(build.stdout[-3000:])
        print(build.stderr[-1500:], file=sys.stderr)
        return None
    for candidate in (PROBE_PROJECT.parent / "bin" / "Release" / "net10.0" / "CuiMirrorProbe.dll",):
        if candidate.is_file():
            return str(candidate)
    return None


def evaluate(baseline: dict, documents: int, clean: int, errors: int) -> list[str]:
    problems = []
    if errors:
        problems.append(f"{errors} mirrored CUI document(s) fail to compile (baseline allows "
                        f"{baseline.get('compile_errors_allowed', 0)})")
    if baseline.get("documents") and documents < baseline["documents"]:
        problems.append(f"mirrored documents fell from {baseline['documents']} to {documents}: "
                        "mirrors were lost, which must be explained rather than absorbed")
    if clean != documents:
        problems.append(f"{documents - clean} document(s) produced diagnostics")
    return problems


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--dotnet", default="dotnet", help="dotnet entry point to use")
    ap.add_argument("--probe", default=None, help="prebuilt CuiMirrorProbe.dll (built if omitted)")
    args = ap.parse_args(argv)

    if not BASELINE.is_file():
        print(f"REFUSED: no baseline at {BASELINE}", file=sys.stderr)
        return 2
    baseline = json.loads(BASELINE.read_text())
    paths = corpora()
    if not paths:
        print("REFUSED: no committed mirror corpora found", file=sys.stderr)
        return 2

    probe = args.probe or build_probe(args.dotnet)
    if probe is None:
        print("FAILED: probe did not build", file=sys.stderr)
        return 2

    out = subprocess.run([args.dotnet, probe, "--strict", *paths], cwd=ROOT, capture_output=True,
                         text=True)
    print(out.stdout.strip())
    if out.stderr.strip():
        print(out.stderr.strip(), file=sys.stderr)
    match = SUMMARY.search(out.stdout)
    if not match:
        print("REFUSED: probe output did not report a summary", file=sys.stderr)
        return 2
    documents, clean, errors = (int(x) for x in match.groups())
    problems = evaluate(baseline, documents, clean, errors)
    print(f"\ncorpora={len(paths)} documents={documents} clean={clean} errors={errors} "
          f"baseline documents={baseline.get('documents')} allowed-errors="
          f"{baseline.get('compile_errors_allowed')}")
    if problems:
        print("RATCHET BREACH:", file=sys.stderr)
        for problem in problems:
            print(f"  - {problem}", file=sys.stderr)
        return 1
    print("mirror corpora compile clean against the real CUI compiler; "
          "compile evidence only, no rendering or acceptance claimed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
