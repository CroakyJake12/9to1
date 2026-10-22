# Authenticated ARM installer delivery

Delivery is separate from packaging, acceptance and installation. Run
`eng/release-delivery/installer_delivery.py` on the actual receiving laptop. It
uses the owner's existing authenticated GitHub CLI session, or verifies archives
already downloaded through GitHub's authenticated UI. It never installs, launches,
changes services/credentials, dispatches a worker or makes a package release-ready.
Mercury is revoked; no model is called.

The inspected laptop bridge supports only health/read_evidence/status and131072-byte
bounded reads. It has no registered transfer/exec operation. Do not replace it,
expand its roots, invent an operation, expose public packages or evade a cloud
download tool's size limit. Native laptop downloads are the intended operation.
Until a supported laptop invocation channel exists, the owner must run the helper
locally. A link, cloud upload or queued journal entry is not laptop delivery.

## Prepare exact accepted inputs

Root must independently accept the runnable candidate against the canonical spec,
eight mandatory gates and seven quality criteria. The ARM journey aggregate
`9to1.arm-package-journey-aggregate/v1` deliberately has `release_ready:false`
even for its passing checkpoint. The builder's `ASSEMBLED_NOT_NATIVE_ACCEPTED`
receipt and green build cannot authorise transfer. If mandatory physical GNOME
acceptance is missing, keep the blocker; this helper provides no waiver.

Root prepares `delivery-manifest.json` with schema `9to1.installer-delivery/v1`:

| Field | Required actual value |
|---|---|
| repository | CroakyJake12/9to1, existing private repo ID1378313608 |
| source_revision / source_patch_sha256 | Exact tested40-character revision and64-character patch digest |
| package | file_name, safe ZIP member, exact inner bytes/SHA256, dpkg name/version and architecture=arm64 |
| acceptance | status=READY, coordinator, independent reviewer, implementer, matching source_revision/package_sha256, mandatory_gates, quality, evidence identity |
| artifacts | Pinned IDs, run_id, workflow_head_sha, name, compressed ZIP bytes/SHA256 and roles |
| target | distribution=ubuntu, version=26.04, architecture=arm64, gnome_acceptance=NOT RUN |

No current package is authorised by this document. Do not populate READY from a
failed or unaccepted package, historical report, label or fixture. The eight
`mandatory_gates` must actually PASS: cui-conformance, donor-licences,
visual-category, packaged-workflows, privacy-security-accessibility,
platform-performance, cross-app, independent-acceptance. The seven `quality`
values must PASS: Usability, Product fit, Functionality, Integration, Separation
and authenticity, User fit, Refinement. Reviewer and implementer must differ.

`acceptance.evidence` pins a retained verdict's exact bytes/SHA. That verdict repeats
status, source_revision, package_sha256, reviewer, implementer, mandatory_gates and
quality. Use either safe relative `acceptance.evidence_file` beside the manifest,
or `acceptance.evidence_member` in the corresponding source/evidence artifact,
never both. Adjacent post-build approval avoids rebuilding/repacking immutable
packages merely to add independent review. Keep reviewed inputs in the existing
private authenticated repository/checkpoint custody. The helper checks structure,
applicability and bytes; it cannot establish semantic truth of arbitrary review
claims. Root must perform and independently review the required acceptance.

Preserve all four existing artifact roles: package,
corresponding-source-and-evidence, browser-sidecar, upstream-engine-source. Exactly
one artifact supplies the package and one supplies corresponding source/evidence.
Normal package ZIP contents are DEB + SHA256SUMS + package-receipt.json. ZIP digest
and inner DEB digest differ. Retention workflow revision may differ from tested
source: pin both and verify the embedded package source. No tokens, signed download
URLs, personal data or binary packages belong in Git. This helper publishes nothing.

## Receive on the laptop

Root supplies the reviewed helper and genuinely accepted inputs through the
existing authenticated private path. The owner's `gh` must already have private
repository access; no login or token read is performed. The owner may inspect
`gh auth status` using their normal account workflow. Use a new private directory:

```sh
mkdir -m 700 "$HOME/Downloads/9to1-accepted-candidate"
python3 installer_delivery.py preflight --manifest delivery-manifest.json \
  --destination "$HOME/Downloads/9to1-accepted-candidate" \
  --baseline "$HOME/Downloads/9to1-preview-2026-10-21/9to1-preview_0.1.0~internal.native16.20261021.39571247743.1+icu78.1_arm64.deb"
python3 installer_delivery.py receive --manifest delivery-manifest.json \
  --destination "$HOME/Downloads/9to1-accepted-candidate" \
  --baseline "$HOME/Downloads/9to1-preview-2026-10-21/9to1-preview_0.1.0~internal.native16.20261021.39571247743.1+icu78.1_arm64.deb"
```

Preflight checks actual Ubuntu26.04/aarch64, the old baseline, private repository,
unexpired exact artifact metadata and free space for all ZIPs + DEB +256MiB reserve.
It downloads no package. Missing access, expiry, capacity or baseline mismatch
blocks transfer. For authenticated browser downloads, save each ZIP as
`artifact-ID.zip` in a separate directory. Use `verify-local --archives /actual/dir`
with the same arguments; it honestly records authentication NOT CHECKED by that
command while verifying every pinned archive/package/provenance/acceptance byte.

Receive stages privately, verifies actual dpkg metadata, retains source/licence
archives and publishes without overwriting existing bytes or symlinks. Identical
files may be reused. The historical startup baseline is62,010,524 bytes, SHA256
`91f302640f912d0ef86da2ce9d07809ca8b3f082c72a668dae9b9b1b56399fcf`.
The helper rehashes it locally before and after; a historical report alone is not
proof. Preserve its directory and never substitute the distinct69MB draft asset.

Only a successful target receipt says DELIVERED_BYTES_VERIFIED with actual path,
bytes, SHA, timestamp, retained archives and unchanged baseline. Blocked attempts
retain private failure receipts where safe. Installation, GNOME, local inference,
browser and shutdown remain NOT RUN by delivery. Root must inspect the actual
receipt through a supported path; prepared commands do not prove execution.

## Focused checks

```sh
python3 -B -m unittest discover -s eng/release-delivery -p test_installer_delivery.py -v
```

Tests use real tiny ARM64/AMD64 DEBs and filesystem/ZIP operations, fixture review
decisions and fake provider transport. They claim no real application acceptance,
GitHub download, laptop delivery or GNOME success. No SDK is needed. When global
/tmp is full, set NINETOONE_DELIVERY_TEST_TMP to an existing private scratch directory.
Fixtures use explicit0755 control directories and a private dpkg temp directory,
retaining failures. Register this focused command in the existing infrastructure
workflow during integration; do not create a parallel CI/packaging system.
