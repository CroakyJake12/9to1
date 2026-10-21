# THUNDER PASS-5 handoff — 20 October 2026 (lane 6)

Resumes `ses_ea553125dffe0sx2MyJhLF3q7w` in `/workspace/9to1-mercury-thunder`, branch
`codex/mercury-thunder-upgrade-20261020`. Pass-4 frozen checkpoint
`ccb697763e3133f0d5cbbe45d9964189ec32bef1` and receipt
`parallel6-thunder-pass4-frozen/identity.json` preserved untouched. Worktree HEAD remains
`873058283de8efaf90e70d7c7398686b3b1cafb4` (uncommitted source-only work, as before). No
commit/push/deploy, no .NET/MSBuild/restore, no CMake/CUDA/engine build, no model/GPU/PC/network/
provider/config/cap changes, no installs/downloads, no model subagents, no unassigned worktrees.

AGENTS + authority reviewed: canonical v2 spec (SHA-256 `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`) and owner-correction
`2026-10-20-single-app-implementation/owner-correction.json` (SHA-256 `a34114994d23eeb7e2696aa2292c61cbbe429ef17df4fcb15275e1f51225cd06`) reviewed. They govern app-surface single-implementation; this dispatch is a Thunder native candidate *backend* (not a user-facing app/UA) and introduces no duplicate app surface, so no conflict applies.

## What changed (all source/tests only)

Priority 1 — managed token-frame decode corrected + causal fixtures.
- `StrataNativeWorker.cs` candidate `StreamAsync` token branch: removed the erroneous `_=input.ReadUInt32()` discard. `Bytes(input)` now consumes the actual producer form `[u32 byte_len][raw utf-8 bytes]` and `End(input)` rejects trailing bytes. The V1 legacy worker token branch is untouched (byte-for-byte preserved).
- `NativeStrataCandidateManagedWireTests.py`: added `CandidateTokenFrameDecodeTests` (source-scan custody) and `CandidateTokenFrameNetFixtureRegistry` with the labelled .NET NOT RUN cases: short_ascii, empty, multibyte, split_scalar_first_half/second_half, truncated_body, trailing_byte.

Priority 2 — native fixture command framing/deadlines repaired.
- `NativeStrataNativeFixtureTests.py`: replaced the input builder `_frame` (which emitted `[type,len,body]`, mismatching the real parser) with `_command(op, body)` = `[u32 size][u32 op][body]`, matching worker `read_command` (`[u32 size]` then op as first body u32). Updated load/unknown-ops/stop call sites.
- Truncated test now declares a body length larger than the bytes sent **and closes stdin**, so the worker's `fread` hits EOF instead of blocking forever; `_read_frame` is now select-based with a real read deadline; full stderr + bounded status (`wait(timeout=15)`) collected.
- The 5 real-binary cases remain explicitly NOT RUN (skip label) pending root build allocation.

Priority 3 — POSIX interpreter binding + exception-safe bounded ownership.
- `dulche_child_process.h` rewritten:
  - StderrDrain: poll-based with a private wake pipe; `stop_and_join()` deterministically unblocks the blocked read and joins with a real bound; the reader thread owns and closes its fds (no cross-thread close). Added `entered_blocked()` synchronization.
  - ChildProcess spawn: per-pipe acquisition cleanup, `setpgid` (child + parent) for group kill, and an exec-failure handshake over a CLOEXEC status pipe (child writes errno on failure, else EOF on exec success) with a bounded poll. `kill()` kills the process group. `shutdown()` order = group-kill → close stdin write end → join stderr drain → bounded exact-pid reap. `_try_reap` distinguishes real reap from ECHILD.
  - Windows compile boundaries: `_spawn_windows` is now actually called (`return _spawn_windows(spec);`), POSIX-only methods guarded, `ToWide` fixed with checked `MB_ERR_INVALID_CHARS` conversion and exact capacity (undersized-buffer bug removed).
- Authored synchronized fixture `NativeStrataChildOwnershipFixture.cpp` (exec failure reported; drain blocked before exceptional destruction; long-lived descendant group-kill + exact reap; broken pipe EPIPE; exact reap) + `NativeStrataChildOwnershipFixtureTests.py` (header custody runs; the C++ compile/run is NOT RUN).

Priority 4 — worker Windows source body + actual bounds.
- `dulche_strata_candidate_worker.cpp`:
  - Added `os_write`/`os_read`/`os_poll` wrappers (POSIX real; Windows fail-closed/compile-valid, truthful unsupported-platform admission) and replaced unguarded `::write`/`::read`/`::poll`.
  - `bind_helper` now binds an approved resolved interpreter via `resolve_python()` (env override `STRATA_PYTHON`, then constrained PATH search for `python3`); no bare `python3` execv (execv does not search PATH). ChildProcess handshake confirms exec success.
  - `wait_ready` uses a real wall-clock deadline + poll-based readability + alive() checks (replaces the unbounded iteration loop).
  - `helper_recv` bounded with a real 30s external read deadline (`wait_readable`) so a silent helper cannot stall generation forever.
  - Preserved SIGPIPE/EINTR/EOF handling; no global signal changes.

## Verification (direct commands, real unittest status capture)

Commands (from `9to1 Models/Dulche Alpha/Tests`):

| Command | Result |
|---|---|
| `python3 -m unittest NativeStrataCandidateManagedWireTests -v` | 15 tests, 0 skipped, OK |
| `python3 -m unittest NativeStrataChildOwnershipFixtureTests -v` | 8 tests, 1 skipped (C++ compile/run NOT RUN), OK |
| `python3 -m unittest NativeStrataNativeFixtureTests -v` | 7 tests, 5 skipped (binary cases NOT RUN), OK |
| `python3 -m unittest NativeStrataTokenizerHelperTests NativeStrataCandidatePlanTests NativeStrataCandidateTransportTests NativeStrataPackageTests NativeStrataUpdateToolTests -v` | 62 tests, 0 skipped, OK |

Total this pass: 92 executed + 6 labelled NOT RUN (5 native binary + 1 C++ ownership fixture). All previously-green Planck preserve green. No `| head`/grep/tr/echo masking was used; unittest output is the source of status truth.

## Source identity (exact hashes)

- `NativeStrata/dulche_child_process.h` — SHA-256 `535f970c44b322ce589d8c537554841190a19da7aac64c66f7435fe1bf694e8d`
- `NativeStrata/dulche_strata_candidate_worker.cpp` — `f623f57bc64f5f878f519a7173f1d5dab1c3b867bae9966e3a5dd4495ce978fc`
- `InferenceEngines/StrataNativeWorker.cs` — `a4f8c8e59f2b84446cf167a5be97c5e4ecd67da1255dab4458e64e3a4cf092de`
- `Tests/NativeStrataNativeFixtureTests.py` — `b1680b5ecf810d0758b40fe4bd008fb72a83b1c2b85583181037035bec50ee76`
- `Tests/NativeStrataCandidateManagedWireTests.py` — `f95ff8b326f193d3b28d53da6d9043a803bae0bb1c1f0160bdf096c9626e552c`
- `Tests/NativeStrataChildOwnershipFixture.cpp` — `d88caa2b33411d078d5c8ee139fae14121cf31a4349242d399c5aacf13a6022a`
- `Tests/NativeStrataChildOwnershipFixtureTests.py` — `f2739be3242f321b327f3bbb5250ddffe0a21d1255b4db0b8683604952f8e706`
- Worktree HEAD unchanged: `873058283de8efaf90e70d7c7398686b3b1cafb4`

## Current blocker matrix

| Blocker | Scope | Status |
|---|---|---|
| No .NET allocation | Managed wire/token-decode cases (short/empty/multibyte/split-scalar/truncated/trailing) | NOT RUN (source-scan custody green) |
| No C++ compile/run allocation | `dulche_child_process.h` + `NativeStrataChildOwnershipFixture.cpp` real compile/run; NativeFixture 5 binary cases | NOT RUN (header custody green) |
| No engine/model build | Native binary fixture cases; generation acceptance | NOT RUN |
| Missing donor `regex`/`jinja2` | Real helper text generation (fail-closed by design) | blocked in cloud env |

## Next root gate commands

```bash
# 1) One serial bounded portable-helper C++ compile (worktree-local output, short timeout):
cd "/workspace/9to1-mercury-thunder/9to1 Models/Dulche Alpha/Tests"
g++ -std=c++20 -O1 -pthread -o /tmp/opencode/child-ownership-fixture NativeStrataChildOwnershipFixture.cpp
/tmp/opencode/child-ownership-fixture        # expect: checks=… failures=0, exit 0

# 2) When a .NET slot exists (guarded dotnet, -m:1), add the managed token-fixture cases from
#     CandidateTokenFrameNetFixtureRegistry against StrataNativeWorker.cs.

# 3) When a root build slot exists, build the candidate worker and run NativeFixtureTests.py
#     (now exercising the real [u32 size][u32 op] command framing with stdin-close + deadlines).
```

Do not select/promote the candidate in production; V1 rollback and factory default remain intact.
This is engineering feedback, not engine acceptance.
