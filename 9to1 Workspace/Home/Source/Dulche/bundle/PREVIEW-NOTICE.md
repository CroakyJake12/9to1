# Preview asset notice (bundle15/17 lane)

This notice documents the licencing status of assets in the laptop preview
profile. It is **not** permission to ship a model, and it does not claim that any
licence policy forbids redistribution; it states the actual licence terms of the
pinned upstream assets.

## llama.cpp engine (pinned source, Bookworm rebuild)

- Source: `https://github.com/ggml-org/llama.cpp.git` commit
  `5266f24da75dc449bd56cbed7addb9c8e4a6a73e` (release b10809 / project 0.4.0).
- Licence: **MIT**. Copies or substantial portions must include the upstream
  copyright and MIT permission notice — the verified `llama.cpp-MIT.txt` (sha256
  `94f29bbed6a22c35…`) is part of the runtime closure.
- Build: rebuilt from the pinned source on Debian Bookworm arm64 (native CI run
  39562111673); **NOT** the original official release asset. Immutable engine
  identity `bookworm-llama-server` (sha256 `edcd91e1…`, e_machine 183).
- Static GCC runtime linkage requires shipping the **gcc-12-base copyright notice**
  (GPL-3.0-or-later with GCC Runtime Library Exception), which is kept separate
  and **not** relabelled MIT.

## Preview model (Qwen3-0.6B-Q6_K)

- Canonical id: **`qwen3-06b-q6k`**. Source:
  `https://huggingface.co/unsloth/Qwen3-0.6B-GGUF/resolve/50968a4468ef4233ed78cd7c3de230dd1d61a56b/Qwen3-0.6B-Q6_K.gguf`.
- Licence: **Apache-2.0** (source model). Authentic model-card/licence receipt
  and canonical `qwen3-06b-q6k.json` are referenced.
- Redistribution: **allowed** per the canonical JSON (not the CI-lock policy).
- State: real file present (495,107,776 B), hash re-verified
  (`1c15a88244c9e852516e5ba0e394fc97f2182f1f2757413fb99fe6a9214c033b`), structural
  sniff PASS. No application/laptop execution claimed.

## CI-only lock (unchanged, not a preview asset)

- `test-model.lock.json` (`unsloth/SmolLM2-135M-Instruct-GGUF` revision
  `8637628433dc3df4a2d363adc89e4c3917f299dc`, `SmolLM2-135M-Instruct-Q4_K_M.gguf`)
  is a **CI-test-only** artifact. It is not redistributable under the CI policy
  (`distributionPolicy` all false, `runnerRetention=delete-after-proof`) and is
  **not** a preview/deployment asset. That is a fact about the CI workflow — not
  a claim that Apache-2.0 itself forbids redistribution. The lock is unchanged
  and must not be packaged or uploaded.

## CI-policy-versus-licence wording

The CI-only exclusion above is a policy/process restriction relative to the CI
test workflow, distinct from the actual upstream licences. The llama.cpp MIT,
GCC runtime notice (GPL-3.0-or-later/GCC Runtime Library Exception), and Qwen3
Apache-2.0 terms are stated separately from policy limits.
