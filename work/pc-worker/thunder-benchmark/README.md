# Thunder benchmark harness — preparation (NOT RUN)

Status: **NOT RUN / BLOCKED**. No authorised RTX endpoint is connected and the owner window
(21:00–22:00 Europe/London = 20:00–21:00 UTC on 2026-10-20) has not opened. This directory is
offline preparation only. Nothing here has executed on any owner PC.

## Purpose

Produce repeatable, objective measurements for the embedded Strata engine (Thunder) after the
upstream upgrade, on the owner's actual hardware:
**Windows desktop, RTX 5080 16 GB, i9-14900K, 64 GB DDR4**. Primary model: installed
Qwen3.8-Flash-Next **subject to actual installed-identifier verification**.

Historical Q2 ~90 tok/s and Q3 ~50 tok/s are uncontrolled observations, **not** baselines.

## What this directory now contains (pass 2)

- `thunder_bench.py` — source-only offline harness: fail-closed guard contract, interleaved
  existing/upstream/optimised scheduling (≥5 valid runs/config), bounded process lifetime (kills the
  whole process tree on timeout), candidate `strata --serve` protocol parser (GEN/T/DONE/ERR), raw
  result recording, median/MAD aggregation. No measurement equals a result.
- `test_thunder_bench.py` — 14 deterministic build-free tests (guards, parser, scheduling,
  aggregation, subprocess lifetime). Pass: `python3 -m unittest test_thunder_bench`.
- `COMPATIBILITY-MAP.md` — ABI v1 → candidate v0.2.1 entry-point map (honest deltas).
- `prep/strata-v0.2.1-lock.candidate.json` — **corrected** candidate lock for v0.2.1: commit
  `90767252f420e25223c739d604145ad15911d48b`, tree `f7adcc9073dc4bc12d5b001f1c85b58f2c2e9ead`
  (distinct; provenance resolved from the GitHub tag→commit→tree chain).
- `prep/strata-v0.2.1-tree-blobs.json` — authenticated tree blob map (path→git blob SHA) with the 16
  `eol=crlf` export-normalized exemptions; cross-checked by `prepare-upstream-update.py verify-tree`.
- `prep/provenance-v0.2.1.json` — the resolved chain evidence.

## Fail-closed guard contract (implemented in `thunder_bench.py`)

The runner MUST NOT begin (or continue) unless ALL of the following are true at execution time:

1. **Owner window verified** — current UTC time is within [20:00, 21:00) on 2026-10-20
   (21:00–22:00 Europe/London), checked from an authoritative clock at start **and** continuously;
   stop before 21:00 UTC. Late-arriving endpoint: start only if still in-window.
2. **Endpoint positive verification** — the authorised integration is actually connected
   (health/hello probe succeeds against the real endpoint), not assumed from old bridge statuses.
3. **Device verification** — `nvidia-smi` reports RTX 5080 (or the authorised configured device)
   with expected VRAM; fail if a different GPU is present.
4. **Model verification** — the loaded model identity/registration matches the planned model
   (Qwen3.8-Flash-Next) from actual runtime state, not from config string.
5. **No destructive changes** — never change drivers/clocks/voltage/power-limit/BIOS/OS-security;
   never kill unrelated processes; never expose credentials or personal files.
6. **Rollback retained** — the known-good pre-upgrade engine build and original baseline artifacts
   remain intact before any measurement (the current legacy pin is retained).

On any revocation or at window end the runner stops immediately; it never extends by elapsed-time
assumption. If any guard fails, record BLOCKED and do not emit results as passes.

## Candidate build plan (NOT executed)

The candidate profile is source-ready: `NativeStrata/CMakeLists.txt` (`DULCHE_STRATA_PROFILE=candidate`),
`build-package.py` (`--profile candidate --lock <candidate-lock> --target windows-x64|linux-x64`),
the relay worker `dulche_strata_candidate_worker.cpp`, and the llama.cpp dependency lock
`NativeStrata/llama.cpp-dependency-lock.json` (pinned `3cf03257f219afbe7334045ff7c6a06ac68c627d`).

Required before any candidate build:

1. Verify the candidate lock + provenance:
   ```sh
   python3 "9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/prepare-upstream-update.py" \
     verify --archive <v0.2.1.tar.gz> --lock prep/strata-v0.2.1-lock.candidate.json
   python3 "9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/prepare-upstream-update.py" \
     verify-tree --lock prep/strata-v0.2.1-lock.candidate.json \
                  --tree-blobs prep/strata-v0.2.1-tree-blobs.json
   ```
2. Provide a verified local llama.cpp checkout (`--ggml-dir` / `STRATA_GGML_DIR`) at the pinned
   commit; do not fetch mutable/unrecorded dependencies during an offline build.
3. Allocate the heavy-build slot (Linux x64 or Windows x64 toolchain, CUDA 12.8+, sm_120/RTX 50).
   Then:
   ```sh
   python3 "9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/build-package.py" \
     --archive <verified-v0.2.1.tar.gz> --work /new/build-work \
     --profile candidate --lock prep/strata-v0.2.1-lock.candidate.json \
     --target linux-x64 --ggml-dir <verified-llama-checkout> \
     --build --install /new/dulche-package
   ```
4. Bind a verified tokenizer provider (donor `tools/strata_tokenizer.py` or future C++ BPE); until
   then the candidate relay fails closed on text generation.
5. Only after proof of build + tokenizer + mapping: benchmark per the guard contract (interleaved
   ≥5 runs/config; separate load/prefill/decode/TTFT/memory/correctness/stability).
