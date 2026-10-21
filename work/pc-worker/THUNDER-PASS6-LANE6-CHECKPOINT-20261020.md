# Thunder pass6 — lane 6 checkpoint (2026-10-20)

Dispatch: `parallel16-thunder6-fresh-probe-20261020-prompt.md`; handoff
`parallel16-thunder-pass6-fresh-handoff-local-probe.prompt.md` (SHA256
`0a9b8a94dca730878305b74a94415299abddfd7835715e2a12f381d64f802e05`).

## 1. Probe checkpoint (delivered FIRST for root review; owner-only execution)
- Probe: `work/pc-worker/thunder-benchmark/thunder_local_pc_probe.py`
  SHA256 `4c7d3f44a7d6fe2abbc130d5fa31e2d1de869dc98447f8d8a3408063f0baf8b2`
- Offline tests: `work/pc-worker/thunder-benchmark/test_thunder_local_pc_probe.py`
  SHA256 `d847b2d279253bfe1311df0447883c15185559d0ae11ddff4b3fda8cea5c48cb` — **12/12 pass**
- Owner-run note: `work/pc-worker/thunder-benchmark/THUNDER-PROBE-OWNER-RUN.md`
  SHA256 `93fac4fa6296b8e0d2820b7d564158e8e7372547098318721112c9848423d714`
- Exact owner command (one command, owner executes only after review):
  `cd /workspace/9to1-mercury-thunder/work/pc-worker/thunder-benchmark && python3 thunder_local_pc_probe.py`
- Worker has NOT run the probe, read a real key, or touched 8081/model/network. Hard window
  20:00–21:00 UTC is enforced inside the probe; outside it records `window_closed/not_started`.

## 2. Four helper/managed fixes (hashes/status)
| Fix | File | SHA256 | Status |
|---|---|---|---|
| 1 wake-pipe deadlock (bounded one-byte wake) | `9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/dulche_child_process.h` | `79078059906dc91a9b2d2b28f7dc171bb536bc200a2481b94ae4dfc5e53d2b68` | source fixed; g++ build NOT RUN (root allocation only) |
| 2 managed helper binding (approved STRATA_PYTHON, precise refusal) | `9to1 Models/Dulche Alpha/InferenceEngines/StrataNativeWorker.cs` | `0258226d7b01e857c5b56d98a63e25ff2c104ef4e71ae5478d9dbbbe662e444b` | source fixed; .NET build NOT RUN |
| 3a fixture wrapper: `-I NATIVE` + compiler timeout | `9to1 Models/Dulche Alpha/Tests/NativeStrataChildOwnershipFixtureTests.py` | `4d8f06ff8543ce4705a0deee26ecc875ac32d23e4349bd9597ec0c899868742f` | tests pass (8 OK, 1 run test NOT RUN) |
| 3b fixture header compile note updated | `9to1 Models/Dulche Alpha/Tests/NativeStrataChildOwnershipFixture.cpp` | `9dd20302302ed3b1d4ca6511a617678e75a9690d2ca22116937c8a6bef40fa2a` | source check |
| 3c Native fixture absolute-deadline reads + guaranteed cleanup + pinned-binary guard | `9to1 Models/Dulche Alpha/Tests/NativeStrataNativeFixtureTests.py` | `7c2dcf5e382acdc86e2dee5cf6b13834f86b0749e82d83fd912c1d5dbd5ac283` | tests pass (7 OK, 5 NOT RUN) |
| 4 Windows platform boundary + no fabricated reap success | `dulche_child_process.h` (same file as fix 1) | see above | source fixed; Windows compile NOT RUN |

## Blockers / NOT RUN
- No `.NET`/CUDA/CMake/heavy builds, no g++ native compile, no Windows build, no live PC/model/
  network/SSH/Tailscale/GPU/service action performed (source-only checkpoint per AGENTS allocation).
- Built candidate worker absent → native fixture run cases and C++ fixture run remain NOT RUN until a
  root allocation builds and pins the artifact (`DULCHE_NATIVE_CANDIDATE_SHA256` /
  `DULCHE_CHILD_FIXTURE_ALLOWED`).
- Original-owner dependency (separate patch): `StrataNativeArtifactSource` does not yet supply an
  approved interpreter (`PythonInterpreterPath`); the candidate work now binds `STRATA_PYTHON` when
  supplied and refuses precisely otherwise.

## 3. Probe correction dispatch (Thunder7 probe-only; 2026-10-20)
Resolved by `parallel16-thunder7-fresh-probe-only-20261020-prompt.md` + handoff
`parallel16-thunder-local-probe-exact-defects-fresh-successor.prompt.md`. PROBE ONLY; four
helper/managed fixes above remain untouched. All eight exact defects fixed against the c753
source (verified public HTTPS endpoint + public model id; 5 valid SHORT + 5 valid LONG; real
monotonic clock paired with UTC cutoff; metadata whitelist; exclusive 0600 non-overwrite output in
a newly owned 0700 dir; stdlib `http.client` framing; deadline before every phase).

New SHA256 (replace the pass-6 hashes above):
- `thunder_local_pc_probe.py` → `86cb9b8ad7ebecb481ae7cba7f39596f13ea0fc9a856ec27a7f3e1b068aa8b45`
- `test_thunder_local_pc_probe.py` → `918aa56651309151b67a55cc165db1648f5111efab4ca95cec58b179001b7f6c` — 22/22 pass
- Owner-run note `THUNDER-PROBE-OWNER-RUN.md` updated.
- Exact owner command unchanged:
  `cd /workspace/9to1-mercury-thunder/work/pc-worker/thunder-benchmark && python3 thunder_local_pc_probe.py`
- Worker has NOT run the probe, read a real key, or contacted the PC/8081/public HTTPS/network.
