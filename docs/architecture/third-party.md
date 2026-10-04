# Third-Party And Donor Provenance

## Policy

Active migration work preserves original copyright notices and licences. A donor is not promoted to an active implementation until its source revision, licence, modifications, and runtime integration are recorded.

## Current Evidence

- Existing Avalonia dependencies are NuGet package references in legacy hosts. No source fork is vendored yet, so the required upstream revision and local modification record are MISSING.
- `9to1 OS/HUI/vendor/.donor-revision` records the Haven donor revision used by the retained legacy HUI material.
- Dulche records its pinned llama.cpp source revision in `9to1 Workspace/Home/Source/Dulche/llamacpp/upstream.lock.json`; the cancellation control-path evidence is in `docs/dulche/shared-runtime-cancellation.md`.
- Files remains an unmodified Windows donor at `9to1 Workspace/Files/Source/Files/`; it is not a portable active app.
- Canvas/Rnote, Boards/AppFlowy, LibreOffice-backed app slices, and media donors require a per-app provenance and licence audit before a verified CUI port can be claimed.

## Required Before Release

- Centralise a machine-readable third-party inventory with licence file locations and immutable revisions.
- Vendor or maintain the CUI Avalonia-derived fork with its MIT licence and rebase policy.
- Record modifications for every promoted donor component.
- Validate that each distributed `.deb` and Windows package carries the required notices.
