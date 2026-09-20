#!/usr/bin/env bash
set -euo pipefail

# This is a copy-only gate. It never builds, downloads, installs, or executes a package.
root="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
manifest="${EMERGENCY_RELEASE_MANIFEST:-$root/release/emergency-preview-0.1.json}"
input_dir="${EMERGENCY_PACKAGE_INPUT_DIR:-$root/artifacts/packages}"
output_dir="${EMERGENCY_COHORT_OUTPUT_DIR:-$root/artifacts/emergency-preview-0.1-cohort}"

die() {
  printf '%s\n' "$*" >&2
  exit 2
}

command -v python3 >/dev/null 2>&1 || die 'python3 is required for deterministic cohort assembly.'
[[ -f "$manifest" ]] || die "Emergency manifest is missing: $manifest"
[[ -d "$input_dir" ]] || die "Candidate package directory is missing: $input_dir"
[[ -d "$(dirname -- "$output_dir")" ]] || die "Cohort output parent is missing: $(dirname -- "$output_dir")"
[[ ! -e "$output_dir" ]] || die "Refusing to replace existing cohort output: $output_dir"

python3 - "$manifest" "$input_dir" "$output_dir" <<'PY'
import hashlib
import json
import shutil
import sys
from pathlib import Path

manifest_path = Path(sys.argv[1])
input_dir = Path(sys.argv[2])
output_dir = Path(sys.argv[3])

with manifest_path.open(encoding="utf-8") as stream:
    manifest = json.load(stream)

if manifest.get("releaseId") != "cakeos-emergency-preview-0.1":
    raise SystemExit("unexpected emergency release manifest")
assembly = manifest.get("cohortAssembly", {})
if any(assembly.get(key) for key in ("networkAllowed", "buildAllowed", "installAllowed", "vmMutationAllowed")):
    raise SystemExit("emergency cohort assembly policy is not copy-only")

candidates = [item for item in manifest.get("candidatePackageGraph", []) if item.get("state") == "CANDIDATE"]
if not candidates:
    raise SystemExit("emergency manifest contains no candidate packages")
candidates.sort(key=lambda item: (item.get("assemblyOrder"), item.get("packageName")))

verified = []
for item in candidates:
    filename = item.get("fileName")
    expected_hash = item.get("sha256")
    if not filename or Path(filename).name != filename:
        raise SystemExit(f"invalid candidate filename: {filename!r}")
    if not expected_hash or len(expected_hash) != 64:
        raise SystemExit(f"invalid candidate hash: {filename}")
    source = input_dir / filename
    if not source.is_file():
        raise SystemExit(f"candidate package is missing: {filename}")
    actual_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    if actual_hash != expected_hash:
        raise SystemExit(f"candidate package hash mismatch: {filename}")
    verified.append((item, source, actual_hash))

output_dir.mkdir()
for item, source, actual_hash in verified:
    shutil.copy2(source, output_dir / source.name)

with (output_dir / assembly["checksums"]).open("w", encoding="ascii", newline="\n") as stream:
    for item, source, actual_hash in verified:
        stream.write(f"{actual_hash}  {source.name}\n")

cohort = {
    "releaseId": manifest["releaseId"],
    "preparedFromBaseRevision": manifest["preparedFromBaseRevision"],
    "packages": [
        {
            "assemblyOrder": item["assemblyOrder"],
            "packageName": item["packageName"],
            "fileName": source.name,
            "sha256": actual_hash,
            "architecture": item["architecture"],
            "entrypoint": item["entrypoint"],
            "dependencies": item["dependencies"],
        }
        for item, source, actual_hash in verified
    ],
}
with (output_dir / assembly["cohortManifest"]).open("w", encoding="ascii", newline="\n") as stream:
    json.dump(cohort, stream, sort_keys=True, separators=(",", ":"))
    stream.write("\n")
PY

printf 'Verified copy-only emergency cohort created at %s\n' "$output_dir"
