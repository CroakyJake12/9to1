# Thunder candidate compatibility map — Dulche ABI v1 → Niko1221/Strata v0.2.1

Author: independent Mercury Thunder lane (pass 2). Status: **SOURCE-ONLY / NOT RUN**. This map is
engineering evidence grounded in the actual v0.2.1 source (verified tree `f7adcc…`), not an
implementation claim. Nothing here has been compiled or executed.

## The real candidate engine surface

The v0.2.1 engine is **not a library**. `include/strata/qwen35/engine.hpp` exposes only
`int run(int argc, char** argv)` (a CLI entry point); `include/strata/core/session.hpp` is low-level
GPU session state (one token through the 48-layer graph), not a chat runtime. The resident engine is
`src/program/generate.cpp`, whose `main` runs a persistent `--serve` loop: weights load once, then
line-oriented requests on stdin/stdout:

| Direction | Line | Meaning |
|---|---|---|
| in | `GEN <max_new> [keys] <id,id,...>` | text request as token ids |
| in | `STOP` | cancel the current request |
| in | `QUIT` | end the process |
| out | `T <id>` | one generated token (per token) |
| out | `PP <pos> <pt> <ms> <tok/s>` | prompt-chunk telemetry |
| out | `RESUME <n>` / `REUSED <n>` | cache-reuse telemetry |
| out | `ERR <message>` | request could not run |
| out | `DONE <g> <pt> <pt_ms> <dec_ms> <finish> <a> <o> <reused> ...` | terminal + metrics |

Key facts: **no C++ tokenizer** in the generate program (prompt arrives as token ids; the reference
tokenizer is the donor's Python `tools/strata_tokenizer.py` / a pack tokenizer); decode is greedy by
default; model family is Qwen (GGUF, `qwen35moe`), not legacy Safetensors/centralized topology.

## ABI operation → candidate mapping

| ABI v1 operation | Candidate entry point | Status |
|---|---|---|
| `dulche_strata_abi_version` | envelope ABI v1; transport ABI **v2** (explicit incompatible) | implemented (source) |
| `dulche_strata_origin_commit` | candidate commit `90767252f420e25223c739d604145ad15911d48b` | implemented (source) |
| `dulche_strata_has_cuda` | CUDA runtime probe (`cudaRuntimeGetVersion`/`cudaGetDeviceCount`) | implemented (source) |
| `dulche_strata_has_nccl` | not claimed by candidate relay (reports 0) | implemented (source) |
| `dulche_strata_registered_models` | candidate family `qwen35moe` (full enumeration pending real build) | partial / BLOCKED |
| `dulche_strata_create` / `destroy` | spawn / terminate the `strata --serve` sub-process (lifecycle) | implemented (source) |
| `dulche_strata_load` | engine spawn argv with model/context; **not validated in a real build** | BLOCKED (fail-closed) |
| `dulche_strata_generate` | → `GEN` line; parse `T`/`DONE`/`ERR`; relay `T` as token frames | parser implemented; **run BLOCKED** |
| `result_text` | concatenated generated token text (from `T` ids, requires tokenizer decode) | BLOCKED (tokenizer) |
| `reused_tokens` | `DONE` `<reused>` field | available (pending run) |
| `incremental_kv` | not exposed by `DONE` | unavailable (returns 0) |
| `stopped` | `DONE` `<finish>` ∈ {stop,length,cancel,nonfinite} | available (pending run) |
| prompt/prefill/decode tokens & seconds | `DONE` fields | available (pending run) |
| structured-tool terminal (wire v1) | legacy registration reasoning metadata does not exist in candidate | BLOCKED |

## Honest capability deltas (never silently converted)

- **Model format/topology**: legacy admits Safetensors + centralized topology only; candidate is GGUF /
  Qwen family. The candidate does **not** support the legacy Safetensors registration contract.
- **Tokenizer**: text→token-id boundary is a **required blocker**. Until a verified tokenizer provider
  is bound (donor Python tokenizer or a future C++ BPE) in a real build, text generation refuses with a
  native error; no token ids are fabricated.
- **Load/generate execution**: the candidate relay fail-closes until the engine spawn argv is validated
  against a real `strata` build (build slot) and the tokenizer provider is bound.
- **Structured tools / reasoning terminal**: no equivalent registration metadata is exposed by the
  candidate engine; the legacy structured-tool terminal contract cannot be truthfully reproduced yet.

## What is preserved

The legacy profile (`DULCHE_STRATA_PROFILE=legacy`, ro99/strata pin) remains the known-good rollback
with its original in-process ABI worker, CMake, packaging, managed provider and tests. The candidate is
an explicit, separately selected profile inside the same backend — not a second public engine.
