# THUNDER PASS-7 — 21 October 2026 (lane 6)

Resumes the exact ended session in `/workspace/9to1-mercury-thunder`, branch
`codex/mercury-thunder-upgrade-20261020`. Worktree HEAD remains `873058283de8efaf90e70d7c7398686b3b1cafb4`
(uncommitted source-only work preserved). Pass-4 frozen checkpoint `ccb697763e3133f0d5cbbe45d9964189ec32bef1`
and receipt preserved. No commit/push/deploy, no .NET/MSBuild/restore, no CMake/CUDA/engine build, no model/
GPU/PC/network/SSH/Tailscale/provider/config/cap changes, no installs/downloads, no model subagents, no
unassigned worktrees. Source-only until Root allocates a build/SDK/hardware slot.

Authority reviewed: AGENTS.md, canonical spec v2, owner-priority
`/workspace/shared/9to1-private-task-prompts/thunder-priority-20261021/owner-priority.json`
(window 15–20 UTC; additional models orinth1.5 + "earlier new model" identifiers pending confirmation; cap $5
unchanged). The local `2026-10-20-dulche-thunder/owner-correction.json` referenced in the dispatch is **not
present** in this worktree (listed owner-references contain no thunder file); noted, not blocking.

## 1. Existing upgrade / state

- Candidate is the *authored, untested* embedded upstream candidate: `NativeStrata/dulche_strata_candidate_worker.cpp`
  + `dulche_child_process.h` (poll-wake StderrDrain, group-kill, exec-failure handshake), bundled tokenizer helper
  (`strata_tokenizer_helper.py` + donor runtime closure), managed `StrataNativeWorker.cs` V2 wire, and source-custody
  tests. All source-uncompiled (no build allocation).
- Known-good V1 rollback (ro99/strata pin `015b075…`, Apache-2.0, in-process ABI worker) preserved; candidate is not
  promoted in production. No speculative performance claims are made.
- Pass 5/6 fixes are intact (verified line-by-line this session): managed token-frame decode corrected; native fixture
  framing `[u32 size][u32 op][body]` + stdin-close/deadlines; child ownership rewritten; POSIX interpreter binding
  (`resolve_python` env override + PATH); Windows compile boundaries + checked `ToWide`; bounded `wait_ready` and
  `helper_recv` deadlines.

## 2. Official latest upstream verification (today's objective)

Verified against the official `https://github.com/Niko1221/Strata` (web, no download):

| Item | Value |
|---|---|
| **Latest stable tag** | **v0.2.2** (released 21 Oct 2026 11:10 UTC) |
| Tag commit | `011c9e600a93bbc9fd451c563501b5831c32bbca` |
| Commit tree | `539755e6d6b799bd75a47225befc786c436f6d8b` |
| Engine package version | **0.1.47** (`project(strata VERSION 0.1.47 …)` in-tree; v0.2.1 had 0.1.46) |
| License | **MIT** (© 2026 Niko1221 and Strata contributors) — unchanged from v0.2.1; differs from the pinned Apache-2.0 |
| This turn's candidate pin | still **v0.2.1** (commit `90767252…`) — NOT bumped (no blind bump; v0.2.2 source not acquired) |

**Serve wire contract unchanged v0.2.1 → v0.2.2** (verified by direct string comparison between the local v0.2.1
`generate.cpp` and the fetched v0.2.2 `generate.cpp`): `READY <max_context> stop`, `GEN <max_new> [keys] <id,id,…>`,
`STOP`, `QUIT`, `T <id>`, `DONE <…>`, `ERR <msg>`, flags `--serve --pack --max-context`. The candidate worker's
spawn argv and parser therefore remain valid for a v0.2.2 bump; a bump changes only the origin constant, engine-version
receipt, and source lock.

**New capability/dependency-relevant facts in v0.2.2**: GPU Main/Helper/Off roles (`--gpu-roles`), Gyro on Intel
Arc/Mac + faster NVIDIA Gyro, RX 6000 engine packages, optional API key, own-GGUF loading, `strata bench run` real
runs, and the engine's registered model families now include **`qwen4exp`** alongside `qwen35moe` (from upstream
`tests/core/model_family_test.cpp`). No arch/quant/model alias is invented from this.

## 3. Additional-model request (orinth1.5 and "earlier new model")

No existing explicit context, installed-model manifest, or repo name for these was found anywhere in the worktree
(grep of `9to1 Models` and the repo root for `orinth|qwen3.8|flashnext|qwen4exp|manifest` → no local matches).
Recorded **MODEL ID: OPEN**. The only concrete upstream signal is the `qwen4exp` family in v0.2.2's own model-family
test; this is not assumed to be either requested model. No fake compatibility aliases or weight downloads. Once the
official identifier is confirmed, capability-based compatibility will be added via the existing Dulche engine
abstraction + real engine-format/tokenizer/tool/structured-output capability tests.

## 4. Concrete advancement this turn (source-only)

1. `Tests/NativeStrataCandidateTransportTests.py` — added `UpstreamServeContractCustodyTests` (+ `VERIFIED_UPSTREAM`
   dict): pins the candidate worker's serve-wire expectations to the **verified official** upstream contract (v0.2.2
   READY/GEN/STOP/QUIT forms) and asserts the candidate is still pinned to v0.2.1 (honesty guard, no bump). 5 new
   tests, all green.
2. `work/pc-worker/thunder-benchmark/minimal-build-closure.json` — concrete minimal reproducible build closure + exact
   root build commands + acquisition-size estimates (see §5/§6). Valid JSON.

## 5. Minimal native build closure (what root can build with a small g++ slot)

No CUDA/cmake needed for the candidate worker itself; `g++ 14.2.0` is present. Exact commands:

```bash
cd "/workspace/9to1-mercury-thunder/9to1 Models/Dulche Alpha/Tests"
mkdir -p /tmp/opencode
# (1) minimal candidate worker (STRATA_HAS_CUDA=0 fail-closes hardware probe; no engine/GPU symbols):
g++ -std=c++20 -O2 -DSTRATA_HAS_CUDA=0 -DSTRATA_NATIVE_EXT="" \
    -I '../InferenceEngines/NativeStrata' \
    '../InferenceEngines/NativeStrata/dulche_strata_candidate_worker.cpp' \
    -o /tmp/opencode/dulche-strata-candidate-worker
# (2) child-ownership fixture:
g++ -std=c++20 -O1 -pthread -I '../InferenceEngines/NativeStrata' \
    -o /tmp/opencode/child-ownership-fixture NativeStrataChildOwnershipFixture.cpp
/tmp/opencode/child-ownership-fixture        # expect "failures=0", exit 0
# (3) native fixture against the built worker (the 5 NOT-RUN binary cases):
mkdir -p work/pc-worker/thunder-benchmark/native-child-test/build
cp /tmp/opencode/dulche-strata-candidate-worker \
   work/pc-worker/thunder-benchmark/native-child-test/build/dulche-strata-candidate-worker
DULCHE_NATIVE_CANDIDATE_SHA256=$(sha256sum /tmp/opencode/dulche-strata-candidate-worker | cut -d' ' -f1) \
    python3 NativeStrataNativeFixtureTests.py -v
```

## 6. Blockers / NOT RUN / acquisition sizes

| Blocker | Scope | Status |
|---|---|---|
| No g++ build allocation | candidate worker compile, child-ownership fixture, NativeFixture 5 binary cases | NOT RUN (source custody green) |
| No .NET allocation | managed token-fixture cases + managed build | NOT RUN |
| Full engine build | needs upstream source + CUDA 12.8+ (sm_120/RTX 50) toolkit + cmake (none present here) | BLOCKED |
| v0.2.2 source acquisition | ~28 MB tarball (v0.2.1 = 27,912,843 B; not fetched) | requires Root resource coord |
| Runtime closure pins | regex 2026.9.29 (~0.5 MB) + jinja2 3.1.6 (~0.3 MB) | requires Root install coord |
| GPU measurement | real RTX 5080 baseline/profile vs latest upstream/Thunder | BLOCKED until the approved endpoint connects (no PC actions here) |
| `orinth1.5` / earlier model ID | identifier pending async clarification | OPEN |

## 7. Verification this turn

`python3 -m unittest discover -p 'NativeStrata*.py'` → **97 ran, 6 skipped (listed C++/native binary NOT-RUN
cases), OK**. No output-masking pipelines; real unittest status. Existing 92 + 5 new upstream-contract custody tests
green.

## 8. Source identity (untracked/unchanged HEAD)

- Worktree HEAD: `873058283de8efaf90e70d7c7398686b3b1cafb4`
- `Tests/NativeStrataCandidateTransportTests.py` SHA256: `2ae3cdb26ffa854ec1a651ed0eb7c4f20c724570dbaf02ec70a73bf18a6fa32a`
- New `work/pc-worker/thunder-benchmark/minimal-build-closure.json` SHA256: `d3ea6f7063537d9226aab270a9a60ff2180a9ece353f944bc5af6a31dd6c27ec` (valid JSON)

## 9. Next root gate

1. Authorize the **small g++ slot** (§5 commands) to compile the candidate worker + child-ownership fixture and run the
   NativeFixture binary cases. 2. Coordinate **v0.2.2 source tarball acquisition** (~28 MB) if the v0.2.2 bump is
   prioritized; reuse `prep/` by hash. 3. .NET slot for the managed token-fixture cases. 4. On real hardware, run the
   offline benchmark harness (`thunder-benchmark/thunder_bench.py`) against baseline latest-upstream vs Thunder, strictly
   evidence-based.
