# Parity integration status (gatekeeper-owned)

Integration branch: `repair/donor-parity-integration`.
Base: `emergency/windows-apps-book4edge` @ `5431052` (native ARM64 engine,
ABI v3, shared HuiRenderer, HUI-native CanvasRoot, Boards Route-A host).

## Accepted delivery 1 — donor audit/docs

- SOURCE BRANCH: `origin/repair/donor-parity-audit`
- SOURCE SHA: `51e02085fb83a88f2877f9e103ab802e674c50d4`
- INTEGRATED SHA: `3151f98` (cherry-pick, clean)
- TESTS: docs-only; no code touched
- FUNCTIONAL PARITY: n/a (ground-truth record)
- VISUAL PARITY: n/a
- BLOCKERS: none. Note: matrices describe the pre-ABI-v3 bridge and the old
  `HUI/*/Canvas/` pill UI; `docs/parity/CANVAS-RNOTE-PARITY.md` carries the
  newer CakeOS-side state until the next matrix refresh. HUI donor pin
  internally consistent, not remote-confirmed (audit's own caveat, kept).
- SCREENSHOT EVIDENCE: n/a

## Accepted delivery 2 — shared HUI component kit

- SOURCE BRANCH: `origin/repair/hui-donor-parity-components`
- SOURCE SHA: `b7aee9853fa6c6b65b201bea3890245beffed772`
- INTEGRATED SHA: `e30f666` (cherry-pick; `HUI/*/HuiPreviewSurface.cs`
  surface-hex hunks dropped as deleted-by-us — superseded by the shared
  `CakeOS.Hui.Renderer` token pipeline, which resolves the full vocabulary
  plus `#hex`/`solid()` instead of one hex special-case)
- TESTS: `HUI/shared.Tests` 22/22 PASS (verified on laptop), Boards
  hui-tests 28/28 PASS (verified on laptop, pre-Boards-delivery tree)
- FUNCTIONAL PARITY: kit is generic infrastructure, no product claims
- VISUAL PARITY: n/a (components render through the shared renderer;
  icon support verified via existing icon commands)
- BLOCKERS: none. Residual notes: kit predates the shared renderer and does
  not reference it (fine — renderer-neutral by design); ColourPicker `Input`
  caret/selection commands are intentionally unsupported by the Avalonia
  backend (fail-fast, documented).
- SCREENSHOT EVIDENCE: pending (kit has no app surface of its own)

## Accepted delivery 3 — Boards AppFlowy CRUD parity

- SOURCE BRANCH: `origin/repair/boards-appflowy-parity`
- SOURCE SHA: `1a98a88c93e8495094eeebad47e0bf43d99c9d14`
- INTEGRATED SHA: `c6e769c` (cherry-pick, clean)
- TESTS: contract 44/44 PASS (30 existing + 14 new CRUD, verified),
  hui-tests 34/34 PASS (28 + 6 new CRUD, verified),
  `verify-boards-foundation.ps1` PASS (verified)
- FUNCTIONAL PARITY: PARTIAL per audit matrix (structural gaps closed:
  CreateGroup/indexed, RemoveGroup, RenameCard, RemoveCard with stable-ID
  validation, orphan-safe hierarchy, freeform cleanup, adapter mappings,
  allowlists; HUI inline rename/delete/add-group/card-counts through the
  durable session path)
- VISUAL PARITY: MISSING (no donor screenshot comparison yet; runtime
  launch pending below)
- BLOCKERS: none for integration. AppFlowy PoC (Flutter) not run on this
  laptop (no Flutter SDK); donor-drag proof rests on existing PoC evidence.
- SCREENSHOT EVIDENCE: `boards-hui1.png` captured at 1200x800 (windowed)
  showing Boards application with groups/lanes, header, toolbar, cards,
  status bar — **runtime verified on Windows ARM64 laptop**

## Laptop-side Boards inventory task

CANCELLED AS DUPLICATE — the audit matrix covers donor scope, CRUD
behaviours, menus/context, filtering/sorting (vacuous: donor package has
none), properties (donor-opaque; CakeOS extensions documented), and
persistence workflow with more precision than a fresh inventory would add.
