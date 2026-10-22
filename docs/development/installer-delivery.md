# ARM installer delivery: private custody required

**Actual delivery is BLOCKED.** Read-only GitHub inspection on22October2026
confirmed that `CroakyJake12/9to1` is **PUBLIC**, repository ID1378313608.
Authentication to its Actions artifacts does not make them private: ordinary
GitHub users with public-repository access can retrieve those artifacts.
Do not upload private installers there or treat a draft upload, signed URL,
cloud reference or logged-in download as proof of private custody.

The current helper at `eng/release-delivery/installer_delivery.py` pins this
source repository as its artifact store and requires `private:true`. It therefore
correctly refuses the actual repository. Earlier documentation calling it the
existing private repository was incorrect. Keep the privacy gate; do not change
repository visibility, repurpose unrelated repositories, create a new store or
publish packages merely to make the helper pass.

## What has actually been verified

The `fc4e1215` protocol/source review passed29 fixture tests and17 independent
repair/positive observations. The final documentation correction changes neither
Python file. Tests use real tiny ARM64/AMD64 DEBs and filesystem/ZIP operations,
with fake provider transport, host policy and READY decisions. They establish
protocol invariants, **not** an operational private route, real GitHub download,
laptop delivery, installation, GNOME or application acceptance.

The protocol binds tested source revision, source patch SHA and inner DEB SHA to
an independently reviewed retained READY verdict. All eight mandatory release
gates and seven quality criteria remain required. The package builder's
`ASSEMBLED_NOT_NATIVE_ACCEPTED` receipt, green build, or ARM journey aggregate
with `release_ready:false` cannot authorise delivery. Metadata validation cannot
establish the semantic truth of arbitrary review claims. No current package is
made READY by these checks or by this document.

The helper checks private repository/artifact/run/digest identity, bounded regular
metadata, actual dpkg architecture/name/version, package receipt and checksums,
private receiving capacity, source/licence sidecars and no-overwrite publication.
It requires the historical startup baseline to match locally before and after.
That baseline is62,010,524 bytes, SHA256
`91f302640f912d0ef86da2ce9d07809ca8b3f082c72a668dae9b9b1b56399fcf`.
Its present laptop existence is NOT RUN here. Never substitute the distinct69MB
historical draft asset. A target-side receipt must state actual path, bytes, SHA,
time and preserved baseline. Native use/installation remain separate.

## Read-only custody audit and smallest next step

Existing private GitHub repositories were inspected; none was identified as
project-related, selected or modified. The connected personal OneDrive contains
an existing 9to1 project folder. Root rechecked the exact folder and effective
permissions: one matching owner grant, no additional sharing grant. Normalised
owner-link display metadata has null type/scope; Root reviewed it rather than
assuming privacy from the URL. Private IDs and full permission evidence stay
outside Git in the current Root custody audit.

After review, Root uploaded one harmless61-byte nonce file with conflict=fail.
Actual item/parent/size/owner permission readback matched, and a successful raw
fetch/download returned the same61 bytes, SHA256 and nonce. No upload retry,
second probe, sharing change or deletion occurred. This proves a small private
custody roundtrip only, not large-file support, package readiness or laptop
transfer. The browser-URL fetch first failed403/serviceReadOnly; the documented
Graph item URL using actual returned IDs succeeded without changing access.
Use returned Graph IDs for that bounded read path; retain the original failure.

The smallest route is Root-approved use of that existing owner-only
project custody, starting from the demonstrated small-file channel. It avoids creating a repository or changing source visibility.
Before use, verify the selected target's actual permissions again, prove the
supported upload/input-size path without bypassing tool caps, retain exact
provider IDs/bytes/digests, and verify authenticated receiving-side download.
A callable upload API alone is not proof that a large package can be supplied or
that the laptop received it. Retain all source/licence artifacts with the package.

A reviewed successor must separate the **public source identity** from the
**private artifact-store identity** in its configuration, approval and receipts.
The existing helper is GitHub-specific; it does not yet implement OneDrive
custody/permission verification. Its `verify-local` mode reports authentication
NOT CHECKED and must not be used to infer private origin or approve public-hosted
packages. No owner command is requested until a genuine READY package and the
approved private route exist. Keep earlier accepted code/evidence immutable;
new backend/custody behaviour requires appropriate focused independent review.

The registered laptop coordinator currently exposes only health/read_evidence/
status and131072-byte bounded reads. It has no registered invocation/transfer
operation. The desktop's separate Git task channel does not change that fact.
Do not replace the bridge or invent an operation. Missing laptop invocation and
actual receipt readback remain separate blockers even after private custody is
resolved. Root records installation, GNOME, local inference/browser and ordinary
close as NOT RUN until those actual journeys execute.

## Focused protocol checks

```sh
python3 -B -m unittest discover -s eng/release-delivery -p test_installer_delivery.py -v
```

No SDK is needed. Set NINETOONE_DELIVERY_TEST_TMP to an existing private scratch
directory when global/tmp is full; fixtures use explicit control permissions and
private dpkg scratch. Register this focused command in the existing infrastructure
workflow during integration. Do not create a parallel packaging/CI system.
