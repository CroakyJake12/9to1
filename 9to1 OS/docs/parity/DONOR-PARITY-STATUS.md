# Donor parity status (integrator-owned)

Branch: `repair/donor-parity-integration`. Updated by the laptop integrator
on every accepted or rejected desktop delivery. No vague labels.

## Canvas / Rnote

- Donor version: Rnote 0.14.2 (rev `8cac558fa73ee63d958cec05124ea2fdbd7d608d`)
- Inventory: `docs/parity/CANVAS-RNOTE-PARITY.md` (engine APIs verified from source)
- Bridge: ABI v3 (tools, styles, eraser, viewport, save/reopen) — PASS for covered set
- HUI: shared renderer + real `tokens.json` pipeline; icon catalog extended via
  `HUI/patches/apply-canvas-hui-additions.py` — PASS for covered set
- Windows ARM64: native AA64 engine + publish + smoke READY (pre-parity shell)
- Visual parity: MISSING (no faithful reconstruction delivered yet)
- Functional parity: PARTIAL (matrix rows 1-6, 9-10, 15-18 PASS; rest MISSING)
- Remaining mismatches: see matrix rows 7-8, 11-14, 19-30

## Boards / AppFlowy

- Donor version: AppFlowy board experience (pinned `appflowy-board` ref
  `804d7898ac0becabf73e45527baf5d5c573cd6bb` in `apps/Boards/appflowy_poc`)
- Inventory: NOT STARTED (awaiting desktop reconstruction track)
- Bridge: `CakeOS.Apps.Boards.Contract` + HUI projection, 58/58 tests native
- HUI: mounts in shared host; no donor-style board chrome yet
- Windows ARM64: engine PASS, runtime NOT TESTED
- Visual parity: MISSING
- Functional parity: MISSING (structured mutations proven at engine level only)
- Remaining mismatches: full donor interaction model (drag/reorder UX, menus,
  cards/editors, filtering/sorting, properties)

## Other donor apps

No reconstruction deliveries received. Tracks from repo history
(Browse/Write/Data/Present/Plan/Terminal/Dev/WinBoat/Wine/Wave/Images) remain
at pre-parity architecture stage; each needs donor version + inventory before
any parity claim.

## Incoming desktop deliveries

| Branch | App | Milestone | State | Verdict |
|---|---|---|---|---|
| (none yet) | — | — | — | — |

State: RECEIVED / UNDER REVIEW / ACCEPTED / REJECTED.
A delivery is merged only with: diff inspected, donor-parity intent verified,
tests run on this laptop (ARM64 where applicable), screenshots compared.
