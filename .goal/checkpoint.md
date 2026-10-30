# Checkpoint 2 · 30 October 2026

Status: ACTIVE, BF-35 menu/lifetime/drag repair compiled and checked; foreground retains source ownership.

- Full normal source test at 00:34 UTC: **233 total, 231 passed, 2 failed, 0 skipped**.
  Baseline was **223 total, 215 passed, 8 failed**. All 25 block-action cases now pass.
- Menu opens its actual CUI rows; outside dismissal and one-click reopening, keyboard submenu/Escape,
  keyboard Duplicate, undo and stale-ticket refusal are checked. Live queued rebuild keeps focus and
  selection on duplicated paragraph/list editors. Drag checks retain order/undo and now exercise real
  dispatcher timers, release geometry and complete visual cleanup.
- Pre-existing full-suite failures remain: embedded custom/high-contrast document paint after theme
  change (White expected, Black observed), and BoardsEditor declared-surface assertion at line 710.
- Design conversion is **NOT fixed yet**: old TurnInto path replaces with blank content/new ID. Next
  unit must use original revision ticket and preserve identity/content/history. Text/heading is clear;
  projected list-row splitting and incompatible object mappings require explicit lossless semantics.
- Precise scope/results: `docs/development/boards-menu-repair-2026-10-30.md` and its evidence JSON.
  Logs/receipts: `/workspace/scratch/9to1-overnight-20261030/`. Private runner `run-layer1.py` takes a
  fresh label plus exact dotnet argv, serial locks, resource guards and immutable output receipts.
  Evidence is ordinary Layer1 PARTIAL / NOT REUSABLE; native PC and ARM remain NOT RUN.
- Next safe step: commit/push this checked unit to main, then continue Design and the remaining
  reproduced failures. Record verified pushed commit in the next checkpoint. No branch/worktree made.

- Fetched origin/main and switched the clean existing checkout from `work` to canonical `main`.
  Starting main revision: `7199daab` (overnight handoff), containing Boards WIP `8aa8e906` and Browse
  WIP `733f35c3`. Original work ref retained; no source discarded or new implementation branch made.
- Read AGENTS.md, reviewer feedback, overnight handoff, workflow, canonical authority/CUI/Boards
  requirements and applicable expanded/owner menu requirements.
- Cloud has .NET SDK 10.0.401, allowed by global.json rollForward; approximately 30 GiB free disk and
  16 GiB available memory at setup. No live SDK/model processes observed here.
- Existing private cloud workflow projection is absent. Its required recovery reports
  INFRASTRUCTURE/BLOCKED; no replacement ledger or heavy/ARM allocation has been created.
  Overnight handoff distinguishes ordinary source compilation from the pinned root-live/ARM lane.
- Desktop coordinator read: offline, last observation 29 October 23:56:39 UTC,
  `native_contract_changed`, prompt unavailable. No native dispatch or process action attempted.
- Baseline fixture/control-type errors were corrected without dropping genuine popup visibility,
  pointer, stale-revision, order or undo checks. The reproduction and successor evidence are retained.
- Authoritative usage percentage: UNKNOWN; no usage/allowance tool discovered. Stop on an actual 5%
  reading or explicit owner stop, never a context estimate.
- One hourly continuation task is enabled through 2026-11-01 00:00 UTC. ACTIVE means read-only to
  scheduled cycles; only a CHECKPOINT_READY foreground handoff allows a scheduled writer to proceed.
