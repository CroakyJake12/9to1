# THUNDER PASS-10 checkpoint — ownership leak fix + probe output/transport corrections

- Date: 2026-10-21 (~17:10 UTC). Lane 6 (continuation of `ses_ea553125dffe0sx2MyJhLF3q7w`).
- Branch `codex/mercury-thunder-upgrade-20261020`; worktree HEAD `873058283de8efaf90e70d7c7398686b3b1cafb4`.
- Scope: **SOURCE ONLY**. No compiler/native/CMake/.NET/SDK/GPU/inference execution. Lightweight Python
  functional tests only. No commits/push/deploy/global config/credential edits. Frozen reviewer copies
  (`/tmp/9to1-thunder-priority-acceptance-20261021/`) not edited.

## 1. Priority 1 — owned process-group leak after direct reap (FIXED in source)

**Bug:** `ChildProcess::shutdown()` gated the group kill on `!_reaped`. When the direct child (group
leader) had already exited and been reaped (e.g. `wait_exit()` then `shutdown()`), a still-alive
descendant in the owned process group was left running. Independent reviewer challenge (`descendant-challenge`,
exit 1) reproduced: `root=1318182 descendant=1318184 direct_reaped=1 descendant_live_before=1
descendant_live_after_shutdown=1`.

**Fix (`dulche_child_process.h`):**
- `kill()` now attempts the group kill even when `_reaped` (retires the still-owned group after direct
  reaping) and preserves PID/group identity custody through reaping.
- New `_posix_own_group_alive()` ownership guard: while the leader is alive it is trivially ours; once
  reaped, it scans `/proc` (bounded, read-only) for a live member with `pgrp==_pgid && session==_sid`.
  This closes the **pgid-reuse hazard** — a recycled pgid naming an unrelated group is never killed, nor
  are arbitrary/outside descendants.
- `shutdown()` calls `kill()` unconditionally; idempotent; never throws; safe in destructor/unwinding.
- Windows safe owned-job behavior preserved (source-only; truthful untested status). Reviewed
  cancellation/error/EOF/destructor paths (exec-failure and drain-failure paths unchanged and still group
  kill via the same guard).

**Regression fixture:** `Tests/NativeStrataDescendantAfterReapFixture.cpp` mirrors the reviewer challenge:
direct parent (`sh`) exits while its own `sleep` descendant holds pipes/group; asserts `wait_exit` reaps
the direct child, the descendant is alive before shutdown, and retired **after** shutdown (`failures=0`).
**COMPILE+RUN NOT RUN** (root native slot). Build command recorded below.

## 2. Priority 2 — private output directory symlink/reuse mutation (FIXED)

**Bug:** `_write_private_json` used `os.makedirs(...exist_ok=True)` then `os.chmod(d,0700)`, which follows an
existing symlink and changes the target's permissions (QA confirmed `0750→0700` and JSON written through a
symlink destroyed**Fix (`thunder_local_pc_probe.py`):**
- New `_fresh_private_dir()` **exclusively creates a fresh private 0700 directory** via `os.mkdir`
  (fails if the name exists; never follows a symlink; never chmods/reuses an existing path).
- New `_resolve_output_base()` refuses a symlink base and never chmods an existing base; `_write_private_json`
  now writes the unique exclusive 0600 file inside the fresh dir. Existing target dirs and symlink targets
  are left untouched.
- Functional tests added: existing-dir not chmod'd, symlink base refused, fresh unique output dirs, no-target
  mutation (all green).

## 3. Corrected endpoint/auth/transport context (probe updated)

- **Key-file selection:** CLI `--key-file PATH` (path only, never the value). Default local owner-PC key
  path used only if it actually exists; missing/auth fails closed. No invented repo/shared paths on the PC.
- **Transport:** public route now uses a bounded **urllib opener** that honours inherited HTTP(S)_PROXY/
  NO_PROXY and the system CA (`HTTPSHandler(context=ssl.create_default_context())`); loopback is always
  direct (keeps offline framing hermetic). Deadline enforced before every connect/read.
- **Model selection:** matches exact `/models` ids `3-8-flash--q3-pro-` and `strata-dynamic`; **default chat
  model is `strata-dynamic`** when present, else the flash id. Only observed ids used, pre + post.
- **SSE events:** final `usage` plus optional `server_timings` (bounded numeric scalars) captured and kept
  **separate** from client `first_text_delta_s`/`total_s`.
- Strict 2026-10-21 15:00–20:00 UTC window/revocation unchanged.

## 4. Test results (all green)

| Suite | Result |
|---|---|
| `NativeStrata*.py` (full) | **114 ran / 7 skipped / OK** |
| `NativeStrataTokenizerHelperTests` + `NativeStrataUpdateToolTests` | **34 OK** |
| `test_thunder_local_pc_probe.py` (offline, mocked clock/transport) | **26/26 OK** |

## 5. Artifact hashes

| Artifact | SHA-256 |
|---|---|
| `InferenceEngines/NativeStrata/dulche_child_process.h` | `418603b9e64bb2b048ab26350f03cdc05f2268bf4c1029865c1dcd05eba6ad3c` |
| `Tests/NativeStrataDescendantAfterReapFixture.cpp` (new) | `2a2f9150bff0abd7b55daf895528270882d737e297be6cc5d6d325d1c7b2152e` |
| `Tests/NativeStrataChildOwnershipFixtureTests.py` | `072aad7ce105f2e92a569ebda50939358789681f8a74e33766c4d4ba65953326` |
| `thunder_local_pc_probe.py` | `e686ed23c761b1a7fa734028824653997f454e95eabe8db51ae7e96d083c1531` |
| `test_thunder_local_pc_probe.py` | `16c9729705c83a53ff26e68bfad5ee19774df2824004ee27b5d4c523a9a01858` |
| `THUNDER-PROBE-OWNER-RUN.md` (corrected) | `2f7a682abe8e1c94b67804a502e2ce24ad0d905c4095d42e6e0f9ffa783f10d3` |

## 6. NOT RUN / blocked (recorded, not waived)

- **Native fixtures compile+run** (descendant-after-reap, child-ownership, candidate worker, NativeFixture
  binary cases): root native slot allocated elsewhere this turn.
- Full engine build (no CUDA 12.8+/cmake), .NET fixtures, GPU/VRAM measurement, **live probe/inference**
  (owner/reviewer run on PC inside 15–20 UTC window; a worker must not execute it).
- v0.2.2 archive acquisition still BLOCKED by disk reserve guard (no fabricated SHA/tree; candidate remains
  v0.2.1).

## 7. Root build commands (when slot allocated)

```
cd '/workspace/9to1-mercury-thunder/9to1 Models/Dulche Alpha/Tests'
# descendant-after-reap regression fixture (pass-10, POSIX):
g++ -std=c++20 -O1 -pthread -I '../InferenceEngines/NativeStrata' -o /tmp/opencode/descendant-after-reap-fixture NativeStrataDescendantAfterReapFixture.cpp
/tmp/opencode/descendant-after-reap-fixture      # expect failures=0, exit 0
# child-ownership fixture (existing):
g++ -std=c++20 -O1 -pthread -I '../InferenceEngines/NativeStrata' -o /tmp/opencode/child-ownership-fixture NativeStrataChildOwnershipFixture.cpp
/tmp/opencode/child-ownership-fixture            # expect failures=0
# (candidate worker + NativeFixture binary cases unchanged from pass-9 closure)
```

## 8. Owner-run probe handoff
- Packet: `work/pc-worker/thunder-benchmark/thunder_local_pc_probe.py` (SHA above) + offline tests (26/26).
- Run **BY THE OWNER/REVIEWER on the PC**, inside **2026-10-21 15:00–20:00 UTC**, with the local key
  (or `--key-file PATH`). Command in `THUNDER-PROBE-OWNER-RUN.md`. I do not execute live inference.

## 9. Next move
1. Root builds/runs the two C++ fixtures (section 7) — expect failures=0.
2. Root/reviewer runs the PC probe inside the window.
3. Re-verify v0.2.2 archive disk reserve; if cleared, acquire + update receipt/lock (data change).
