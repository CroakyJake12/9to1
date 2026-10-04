# Parity-test workflow (laptop integrator)

## 1. Detect

Fetch and list remote branches. A desktop delivery is a branch (never direct
main work) with reviewable commits. Record it in DONOR-PARITY-STATUS.md as
RECEIVED. Never assume a pushed branch is ready.

## 2. Inspect

- `git log` the branch: one coherent donor-parity milestone per merge.
- `git diff` against the integration base: affected paths, donor-parity
  intent, no unrelated redesign. Reject redesign masquerading as parity.
- Shared HUI additions get extra scrutiny: reusable, not app-specific hacks.

## 3. Build (Windows ARM64, this laptop)

- Rust donor/bridge code: `cargo build --release` + `cargo test` with the
  vcpkg ARM64 stack (`C:\CakeOS-work\vcpkg`, release-only triplets).
- Managed: `dotnet build` / `dotnet test` for affected projects.
- Full app: `tests/windows/publish-windows.ps1` (Canvas) or equivalent.

## 4. Functional verify

Run the app's real workflows on this machine (mouse/pen; touch where the
session delivers it). Required evidence per matrix row: PASS / PARTIAL /
MISSING / BLOCKED BY HUI / BLOCKED BY BRIDGE / BLOCKED BY UPSTREAM.

## 5. Visual verify

- Reference donor screenshots (Rnote: `crates/rnote-ui/data/screenshots`
  in the cargo checkout; controller overnight dir keeps copies).
- CakeOS screenshots at equivalent states (same docs, same tools).
- Compare: information architecture, control placement, feature set,
  toolbar/menu hierarchy, contextual surfaces, workspace proportions,
  workflow discoverability. Native HUI rendering differences are fine;
  a different product design is a REJECT during parity phase.

## 6. Verdict

ACCEPT (merge one milestone) or REJECT (reasons recorded in the status
file). Update the parity matrix. Never merge semantic conflicts silently;
determine the owning worker.

## 7. Regression

After each merge: affected unit tests, smoke scripts, and (for ISO-touching
changes) static ISO validation + generic QEMU smoke. Candidate 2 stays
preserved; hardware and parity are independent axes.
