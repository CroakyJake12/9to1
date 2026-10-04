#!/usr/bin/env bash
set -euo pipefail

# This is a copy-only gate. It never builds, downloads, installs, or executes a package.
root="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
manifest="${EMERGENCY_RELEASE_MANIFEST:-$root/release/emergency-preview-0.1.json}"
preload_manifest="${EMERGENCY_PACKAGE_PRELOAD_MANIFEST:-$root/release/package-preload-manifest.json}"
input_dir="${EMERGENCY_PACKAGE_INPUT_DIR:-$root/artifacts/packages}"
output_dir="${EMERGENCY_COHORT_OUTPUT_DIR:-$root/artifacts/emergency-preview-0.1-cohort}"

die() {
  printf '%s\n' "$*" >&2
  exit 2
}

command -v python3 >/dev/null 2>&1 || die 'python3 is required for deterministic cohort assembly.'
[[ -f "$manifest" ]] || die "Emergency manifest is missing: $manifest"
[[ -f "$preload_manifest" ]] || die "Package preload manifest is missing: $preload_manifest"
[[ -d "$input_dir" ]] || die "Candidate package directory is missing: $input_dir"
[[ -d "$(dirname -- "$output_dir")" ]] || die "Cohort output parent is missing: $(dirname -- "$output_dir")"
[[ ! -e "$output_dir" ]] || die "Refusing to replace existing cohort output: $output_dir"

python3 - "$manifest" "$preload_manifest" "$input_dir" "$output_dir" <<'PY'
import hashlib
import json
import shutil
import sys
from pathlib import Path

manifest_path = Path(sys.argv[1])
preload_manifest_path = Path(sys.argv[2])
input_dir = Path(sys.argv[3])
output_dir = Path(sys.argv[4])

with manifest_path.open(encoding="utf-8") as stream:
    manifest = json.load(stream)
with preload_manifest_path.open(encoding="utf-8") as stream:
    preload_manifest = json.load(stream)

if manifest.get("releaseId") != "cakeos-emergency-preview-0.1":
    raise SystemExit("unexpected emergency release manifest")
if manifest.get("packagePreloadManifest") != "release/package-preload-manifest.json":
    raise SystemExit("emergency manifest does not identify the package preload manifest")
if (
    preload_manifest.get("schemaVersion") != 1
    or preload_manifest.get("releaseId") != manifest["releaseId"]
    or preload_manifest.get("version") != manifest.get("version")
):
    raise SystemExit("package preload manifest does not match the emergency release")
assembly = manifest.get("cohortAssembly", {})
if any(assembly.get(key) for key in ("networkAllowed", "buildAllowed", "installAllowed", "vmMutationAllowed")):
    raise SystemExit("emergency cohort assembly policy is not copy-only")

release_candidates = [item for item in manifest.get("candidatePackageGraph", []) if item.get("state") == "CANDIDATE"]
if not release_candidates:
    raise SystemExit("emergency manifest contains no candidate packages")
preload = preload_manifest.get("preload", {})
if (
    preload.get("mode") != "copy-only"
    or preload.get("sourceDirectory") != assembly.get("inputDirectory")
    or preload.get("outputDirectory") != assembly.get("outputDirectory")
    or any(preload.get(key) for key in ("networkAllowed", "buildAllowed", "installAllowed", "vmMutationAllowed"))
):
    raise SystemExit("package preload policy does not match the copy-only cohort assembly")

release_by_id = {item.get("id"): item for item in release_candidates}
candidates = preload_manifest.get("packages", [])
if set(item.get("id") for item in candidates) != set(release_by_id):
    raise SystemExit("package preload entries do not match the emergency candidate graph")
for item in candidates:
    candidate = release_by_id[item["id"]]
    if item.get("sourceType") != "standalone-debian-artifact":
        raise SystemExit(f"preload candidate is not a standalone package artifact: {item['id']}")
    for field in ("assemblyOrder", "packageName", "fileName", "sha256", "architecture", "entrypoint", "dependencies"):
        if item.get(field) != candidate.get(field):
            raise SystemExit(f"preload candidate does not match release metadata: {item['id']} {field}")
    for field in ("repository", "ref", "revision", "workflowArtifactId"):
        if str(item.get("source", {}).get(field)) != str(candidate.get("source", {}).get(field)):
            raise SystemExit(f"preload candidate does not match release provenance: {item['id']} {field}")
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
