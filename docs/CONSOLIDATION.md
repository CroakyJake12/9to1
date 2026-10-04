# Consolidation Record

## Source Policy

`C:\Github\9to1` on `main` is the consolidation target. The inspected CakeOS and CakeAI repositories remain read-only donor sources. Valuable code is retained until an equivalent active replacement has been verified.

## Current Selections

- CakeOS Canvas/HUI material was selected from `repair/donor-parity-forwardport` at `13f38f6`; Boards integration material was selected from `repair/donor-parity-integration` at `7c42fce`.
- CakeAI material was selected from `migration/write-libreoffice-poc-20260907` at `44541b79`.
- The older CakeAI Data application surface is preserved under `reference/cakeai/data-app-surface/`. The newer CakeOS Calc/DuckDB engine is the active Data implementation.
- llama.cpp-derived source is shared at `9to1 Workspace/Home/Source/Dulche/llamacpp/`. `9to1 Models/` is reserved for 9to1-developed model artifacts.
- Files is retained as a Windows donor at `9to1 Workspace/Files/Source/Files/`; it has no nested Git repository and is not yet an active portable app.

## Classification

| Classification | Locations | Meaning |
| --- | --- | --- |
| ACTIVE | `framework/CUI/`, current Workspace app/domain projects, `9to1 OS/release/` metadata | Source currently used by a build or current migration slice. |
| DONOR | `9to1 Workspace/Files/Source/Files/` and retained upstream-source areas | Valuable source to port; not proof of product parity. |
| REFERENCE | `reference/cakeos/`, `reference/cakeai/`, historic migration evidence | Retained alternatives and superseded work. |
| LEGACY | Existing HUI/Haven.UI hosts and active AXAML/HUI surfaces | Migration-era implementation, not valid CUI authoring. |
| GENERATED | `bin/`, `obj/`, `obj-hui/`, `obj-apk/`, `obj-apk3/`, `__pycache__/` | Rebuildable outputs; ignored and removed from version control in this pass. |
| OBSOLETE_CANDIDATE | Legacy source after donor-parity and CUI/platform acceptance | Must not be removed before a verified replacement exists. |

## Non-Negotiable Gaps

The consolidation is not complete. There is no maintained Avalonia-derived CUI fork, CUI renderer/compiler/DevTools, complete HUI/AXAML migration, standalone app package set, Linux runtime smoke, Windows package/runtime smoke, or complete per-app donor parity. See `docs/MASTER-MIGRATION-STATUS.md` for the audited status matrix.
