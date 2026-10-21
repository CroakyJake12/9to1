# Laptop preview asset profile — Bookworm same-pin ARM64 (real assets)

Status: **staged/preview, NOT Ready**. This is a *preview* profile (staging-time)
over real, hash-verified supplied assets. It is **not** an inference/runtime
claim: `claimedReady` is always `false`. Native raw engine/model checks PASS
independently; application/laptop/canonical-managed-broker acceptance is NOT RUN.

## Summary

| Axis | State | Reason |
| --- | --- | --- |
| Target platform | **confirmed** | Linux aarch64, Debian Architecture=arm64. Engine ELF64 e_machine 183. |
| Engine bytes | **present** | Bookworm same-pin rebuild of llama.cpp source `5266f24…` (b10809 / 0.4.0), NOT official release asset. sha256 `edcd91e1…`, 14234480 B, DT_NEEDED libm.so.6/libc.so.6 only, GLIBC floor 2.36, static GCC runtime. |
| Notices | **present** | engine MIT notice + gcc-12-base copyright notice (GPL-3.0-or-later/GCC Runtime Library Exception) kept separate. |
| Provenance | **present** | BUILD-INFO.json, SOURCE-COMMIT, engine-closure.json (source/build/ABI pins). |
| Preview model | **present** | Qwen3-0.6B-Q6_K.gguf (canonical id `qwen3-06b-q6k`, sha256 `1c15a882…`, 495,107,776 B, Apache-2.0, redistribution allowed). Structural sniff PASS (GGUF v3, 310 tensors, alignment 32). |
| Deployment admission | 4/7 present admitted | Engine MIT notice + gcc-12-base notice + engine + model admitted (confirmed OS/arch + known SPDX licence + redistribution allowed). Provenance assets (BUILD-INFO/SOURCE-COMMIT/engine-closure) are **BLOCKED**: unknown licence + review-required redistribution cannot confer deployment admission. |
| Native raw engine/model | **PASS independently** | CI run 39562111673: prompt, SSE stream, socket cancellation processing→idle, recovery (bounded engine/model only). |
| Application/laptop broker | **NOT RUN** | Canonical managed broker/provider path not exercised here; stays BLOCKED until evidenced. |
| Streaming/cancellation via broker | **NOT RUN** | Requires the canonical broker/provider path with evidence (native raw proof does not substitute). |

## Assets (7 present; 4 deployment-admitted, 3 blocked)

- Admitted: engine `bookworm-llama-server` (ELF64/AArch64 e_machine 183); notices
  `llama.cpp-MIT-notice`, `gcc-12-base-copyright-notice`; model
  `qwen3-06b-q6k`.
- BLOCKED (deployment): provenance `BUILD-INFO`, `SOURCE-COMMIT`,
  `engine-closure` — unknown licence + review-required redistribution. Structural
  identity is retained; blocker is deployment admission only.

## Q4_K_M reconciliation with `formats.py`

- `Q4_K_M` is a model-wide k-quant method, not one ggml type; `formats.py`
  structurally supports only **Q6_K (tag 14)** among quantized types.
- The selected preview model is **Q6_K** and passes the structural gate.
- Authentic, matching pinned upstream ggml.h block-size definitions for other
  k-quant tags are **not available in this offline worktree** (no network; the
  local plain-type tag table is the frozen authority — no guessed IDs). Therefore
  **Q4_K_M containers remain unknown-format/rejected** until that evidence and
  real bytes are supplied. Fail-closed, not a bypass/relabelling.

## Preview profile artifacts

- `preview_profile.py` — builder; `python -m bundle.preview_profile <request.json>
  [--out profile.json]`. Rejects blank/invalid target, wrong ELF machine,
  TESTONLY assets, absent licence/source/evidence. Structural vs deployment
  admission separated; `claimedReady` always `false`.
- `preview-asset-profile.json` — real Bookworm asset profile (7/7 admitted).
- Tests: `tests/test_preview_profile.py` (8 tests) + `tests/test_verifier.py`
  (strict-closure scoped to owned llama.cpp subtree regression).

## Licence notices

- **llama.cpp** (pinned source, rebuilt Bookworm engine): MIT. Verified engine
  MIT notice (`llama.cpp-MIT.txt`) ships in the closure.
- **GCC runtime** (`static linkage): **gcc-12-base copyright notice** must ship
  (GPL-3.0-or-later with GCC Runtime Library Exception), **not** relabelled MIT.
- **Preview model** (Qwen3-0.6B-Q6_K, id `qwen3-06b-q6k`): Apache-2.0,
  redistribution **allowed** per canonical `qwen3-06b-q6k.json`.
