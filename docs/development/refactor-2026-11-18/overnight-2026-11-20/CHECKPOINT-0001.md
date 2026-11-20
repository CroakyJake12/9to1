# CHECKPOINT-0001 — G00 reconciliation complete (ATLAS-20261120)

2026-11-20 20:55Z · coordinator: OpenCode desktop session (model `homepc/3.8-pro`) · repo `main` @ `e8a07c9f8` (== handoff remote pin; no newer local work, 2 untracked `cloud/files-drive/dev/live-*.mjs` WIP preserved untouched).

## What was reconciled

* **Package identity:** repo copies of SPECIFICATION.md (`b6cd0617…`) and Atlas v3.3 (`75c0abad…`) match the package hashes byte-exactly. Adoption + S1/S3 commits (`64b48baa7`, inventories to `e8a07c9f8`) verified; first S3 increment is **code+compile green, native retest NOT RUN** — the published record matches the live tree.
* **Goal state:** `.goal/` canonical; the 31-Oct Boards sprint (`PAUSED_OWNER_WRAP`, Astra workers stopped) is preserved verbatim (`revisions/2026-11-20-atlas-start/CURRENT.before`, contract/checkpoint/queue untouched). `CURRENT` now links this bounded run — an extension, not a rival coordinator. `RUN.json` binds parent `ATLAS-20261120`; `TASKS.json` carries all 41 packets with runtime fields.
* **Workers:** coordinator live. **V100 parallel worker BLOCKED with receipts**: no pre-existing V100 agent session existed; registering the existing lane (`:18640`, `qwen2.5-coder-14b-q4_k_m`) through the supported OpenCode provider interface produced two failed tool-use probes (fabricated output; then echoed tool JSON with no side effect even after `--jinja`). No silent substitution; boards-owned packets held, coordinator proceeds ("a specific blocked operation must not hold the entire refactor").
* **Services preserved:** official engine (`:18500`/`:47831`) healthy; V100 lane up (now `-np 2`, ctx 16384, `--jinja`, 165 W cap, guard `CHECK_OK`). Neither was reloaded to test UI; models stay resident. Config change reversible: `~/.config/opencode/opencode.json.bak-20261120-atlas`.
* **Environment:** SDK `~/.dotnet/dotnet` 10.0.301; guarded serial builds via `eng/development/guarded_lowmem_build.py`; MemAvailable ≈7 GiB with both services up — build baseline of 18 Nov succeeded at less, guard floor 1100 MiB will be honoured, no relaxations. Native dev copy: profile + live-preview state exist; no Haven process running.
* **Authority notes:** open-decisions.json has 46 records, DEC-001…013 still open → no invented policy for Study weights, reload handshakes, etc. Full independent acceptance stays paused. Sunday's shared single-model multi-GPU engine work explicitly **not started**; shared inference stays out of this queue.

## Next executable task (claimed ready)

**G01-T01** — verify S1 tabs + S3 Home route in the **current dev copy**: guarded `Haven.Desktop` build at HEAD → focused `Haven.UI.Tests` → `live_preview.py` normal lifecycle launch → observe New-tab / restored-home-tab on the canonical surface, tab-strip behaviours, and the `MainView:883` legacy reference left deliberately by `64b48baa7`; fix only observed gaps; native evidence or honest NOT RUN with reason. Then freeze the narrow shell/command contracts before any Boards-owned work is handed out.

## Recovery

Reload order per handoff §8: this checkpoint → `RUN.json` → `TASKS.json` → git state → services → workers. Commit range: G00 ledger commits below; nothing else staged (blanket staging prohibited).
