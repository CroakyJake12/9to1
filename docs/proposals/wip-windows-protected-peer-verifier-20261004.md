# Unselected Windows protected-peer verifier recovery snapshot

Status: UNSELECTED / UNREVIEWED / UNCOMPILED / UNRUN. This branch is a recoverable code checkpoint, not an accepted implementation, installed authority or a review-ready pull request. It adds no default registration or approved identity. Its parent is the package proposal commit `78de8166d9d73773f5b533a12ec6dc46465f82c0`.

The two complete draft source bodies implement a candidate read-only adapter over the existing canonical Home package database and original profile/session lease. The native helper opens the observed process, reads the kernel SID, process birth and image path, opens original protected files without reparse traversal, checks owner/DACL policy, hashes complete file contents and calls WinVerifyTrust on the original image handle with a supplied publisher-certificate pin. The adapter checks bounded signed descriptor/installation-receipt inputs, current canonical package entry revision, exact payload inventory and the existing controlled-launch authority before returning an installed-peer observation. It creates no listener or package registry and performs no installation or privilege elevation.

No approved CAKE/Home publisher, signing key, protected installation receipt or genuine controlled-launch/runtime attestation was supplied. These are required machine/owner inputs. Missing controlled-launch evidence remains unavailable even for a valid signed image. No unrelated project certificate is an acceptable substitute. The proposed protected receipt codec still needs an authoritative installer format/issuer contract; these source records do not declare an installed release.

Known source work that MUST close before selection:

- Replace early unavailable returns inside the adapter's owning try/finally so independent original descriptor-close failures cannot bypass the final error collector. Retain refusal and cleanup causes independently.
- Preserve the original ACL-body failure when LocalFree fails; the current finally can replace it. Review every partial native acquisition and close path with the same original-error requirement.
- Validate all WinTrust/security/process ABI layouts against maintained Windows headers. Check each provider/signer pointer before the next native helper call, and guard native structure initialization before cleanup.
- Bound and deduplicate retained file/ancestor handles, enforce the intended hard-link and local-path policy, and preserve the original identity on every independent reopen. Sequential observations cannot claim atomic approval or protection against unrelated privileged mutation.
- Complete exact descriptor classification/version/service/role and canonical receipt-generation binding against the accepted owner format; reject unsupported declarations without inventing a release identity or granting from metadata.
- Obtain complete independent source/API/ABI/task/cleanup reviews and owning compilation before activating this adapter.

Required meaningful native validation remains unimplemented and UNRUN: actual accepted Windows pipe PID/SID with held original Home lease; original process exit/birth mismatch and foreign SID refusal; signed image with approved versus foreign publisher; unsigned/replaced payload and missing/undeclared file refusal; reparse and foreign-writable ACL refusal; stale protected receipt/package revision; retired original actor/lease; missing or copied controlled-launch evidence; held verification and independent body/close errors. A valid image signature alone must never produce installed admission. Retain original handles, tasks, complete diagnostics and independent cleanup before accepting any result.

The package proposal's separately reviewed 23 methods / 45 cases are unchanged by this recovery snapshot. This branch adds no executed validation or native acceptance.
