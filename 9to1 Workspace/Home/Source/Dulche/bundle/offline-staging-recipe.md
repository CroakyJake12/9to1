# Offline staging recipe — Bookworm same-pin ARM64 (.deb arm64) preview

This recipe stages a laptop preview **entirely from files the integrator
supplies** (real, hash-verified Bookworm-rebuilt ARM64 runtime + model bytes). It
performs **no downloads and no network retrieval**. It uses the existing
`bundle.cli` pack/verify commands and the existing `modelctl` canonical per-user
store — **not** any nonexistent `bundle.packager stage-manifest`.

> Target is **confirmed**: Linux aarch64, Debian Architecture=arm64. The engine
> is a **rebuild from the SAME pinned llama.cpp source**
> `5266f24da75dc449bd56cbed7addb9c8e4a6a73e` (b10809 / project 0.4.0) on
> Debian Bookworm arm64 — it is **NOT** the original official release asset. Its
> immutable build identity is `bookworm-llama-server` (sha256
> `edcd91e1a50804fbf2f82245534a5bc60161c7d9a5356c498b07f6157d5ecdf5`,
> 14234480 B, ELF64/AArch64 e_machine 183, DT_NEEDED only libm.so.6/libc.so.6,
> GLIBC floor 2.36, static GCC runtime).

## 0. Real asset state (source of truth)

- Native ARM CI run **39562111673**
  (`https://github.com/CroakyJake12/9to1/actions/runs/39562111673`), executed
  infra commit `e3841dd89fc9be9d69882020d5ec8eb198606823`.
  `verified-engine-model-checkpoint.json` proves **PASS_BOUNDED_NATIVE_ARM_ENGINE_MODEL_ONLY**
  for both official-baseline and bookworm-source variants (real prompt, SSE
  stream, active socket cancellation processing→idle + follow-up, recovery).
  Application/laptop/canonical-managed-broker acceptance remains **NOT RUN**.
- Engine binary: `/tmp/9to1-native-arm-run-39562111673-review-20261021/
  bookworm-engine-ready/bin/llama-server` (sha256 `edcd91e1…`, 14234480 B,
  e_machine 183). Inner archive `bookworm-source/
  llamacpp-b10809-linux-arm64-bookworm.tar.gz` sha256
  `f34911ff90f7805dfbed221e0a8cabc7a8d71ea0ed4ab69b534657b204434256`;
  source tar + SHA256SUMS + build/compile/package provenance alongside it.
- Notices (truthful, kept separate):
  - `payload/licenses/llama.cpp-MIT.txt` — MIT (sha256 `94f29bbed6a22c35…`).
  - `payload/licenses/gcc-12-base-copyright.txt` — GCC runtime copyright notice
    (GPL-3.0-or-later with GCC Runtime Library Exception; sha256
    `da8191658b3452ce9caf31638ba61dab31a38c619fa39df119812e050f592fd3`). It is
    **not** relabelled MIT.
  - `payload/licenses/Qwen3-Apache-2.0.txt` + model-card (Apache-2.0).
- Model: `Qwen3-0.6B-Q6_K.gguf` (canonical id **`qwen3-06b-q6k`**; unsloth
  revision `50968a4468ef4233ed78cd7c3de230dd1d61a56b`), 495,107,776 B, sha256
  `1c15a88244c9e852516e5ba0e394fc97f2182f1f2757413fb99fe6a9214c033b` (re-verified
  in this turn). Canonical JSON `qwen3-06b-q6k.json`; immutable resolve URL
  `https://huggingface.co/unsloth/Qwen3-0.6B-GGUF/resolve/50968a4468ef4233ed78cd7c3de230dd1d61a56b/Qwen3-0.6B-Q6_K.gguf`.
  Redistribution is **allowed** per the canonical JSON (not the CI-lock policy).
- The 15-ELF official-runtime closure from the prior turn is preserved as
  **historical evidence** (`arm64-runtime-bundle-spec.official-16elf.historical.json`);
  it is not the shipped closure.

## 1. Stage directory layout (expected installed-path closure)

```
<stage>/supplied/                 # real files as named in the spec (flat tree)
  bin/llama-server                # bookworm rebuild, e_machine 183
  licenses/llama.cpp-MIT.txt
  licenses/gcc-12-base-copyright.txt
  BUILD-INFO.json
  SOURCE-COMMIT
  evidence/engine-closure.json
<stage>/out/                      # pack output
  bundle-manifest.json
  bundle-receipt.json
```

Canonical install target (matches existing broker): `/usr/lib/haven`
(`installPath=llama.cpp/llama-server`, notices under `llama.cpp/licenses/`,
provenance under `llama.cpp/provenance/`). Emitted metadata installs at
`/usr/lib/haven/inference/bundle/{bundle-manifest.json,bundle-receipt.json}`.

## 2. Stage the whole data-only package tree (`bundle.data_stage`)

The executable staging helper assembles the full tree from the supplied local
roots in one step — it validates every hash/size + ELF machine against the spec
pins, runs the existing pack, stages the engine closure + manifest/receipt +
python closure + seed (hardlinked model), and re-runs the strict verifier on the
staged install root:

```bash
cd <bundle-lane>   # contains bundle/ package
# <canonical>/ is the root containing modelctl.py, gguf.py, model_lease.py
python3 -m bundle.data_stage \
  --spec bundle/arm64-runtime-bundle-spec.json \
  --payload-root <stage>/supplied \
  --seed-model   <immutable-model>.gguf \
  --seed-json    <canonical>/qwen3-06b-q6k.json \
  --seed-notice  <canonical>/Qwen3-Apache-2.0-LICENSE.txt \
  --python-source-root <canonical>/ \
  --stage-root   <stage>/debian
# → state "ready"; tree installed at <stage>/debian/usr/...; model hardlinked.
```

The helper refuses: a non-empty existing stage root (there is no `--force`
bypass), a symlink stage root or any symlink ancestor, wrong hash/size, wrong
ELF arch, a non-canonical seed id/hash/blob/licence/source, any symlink under
the stage, and writes only inside the fixed stage subdirectories. It never
executes an engine/model, downloads anything, or imports into a user HOME/model
store.

The equivalent manual low-level check (optional) uses the existing `bundle.cli`:

```bash
python3 -m bundle.cli pack --spec bundle/arm64-runtime-bundle-spec.json \
  --assets-root <stage>/supplied --out <stage>/out
python3 -m bundle.cli verify --install-root <stage>/debian/usr/lib/haven \
  --manifest <stage>/debian/usr/lib/haven/inference/bundle/bundle-manifest.json
# → state "ready", all 6 assets "present", no unexpectedPaths. The strict closure
#   scan is scoped to the owned llama.cpp/ subtree, not the whole /usr.
```

## 3. Model seed (existing canonical per-user modelctl store)

The model is staged into the canonical per-user store, **never** `/root` HOME or
a guessed HOME. Seed metadata is installed by the package at
`/usr/share/haven/default-models/` (weights + `<id>.json` + notices under
`notices/Qwen3-Apache-2.0-LICENSE.txt`). No root postinst import. Actual-user
first launch uses `modelctl` as the actual user (Dulche10 owns that flow) and
**preserves** the existing selected model (no `--replace`, no new private store).

No 500MB duplicate: use a **hardlink** to the immutable local model bytes for any
private recipe/test input (`ln -f <immutable.gguf> <worker>/model/…`); record the
hardlink; do not import/copy the model bytes by the worker.

Manual/verification import command (typed argument vectors, no shell):

```bash
python3 /usr/lib/haven/inference/modelctl.py \
  --model-root "${XDG_DATA_HOME:-$HOME/.local/share}/haven/models" \
  --runtime-dir "$XDG_RUNTIME_DIR/haven" \
  import /usr/share/haven/default-models/1c15a88244c9e852516e5ba0e394fc97f2182f1f2757413fb99fe6a9214c033b.gguf \
  --id qwen3-06b-q6k --name "Qwen3 0.6B Q6_K" \
  --license Apache-2.0 \
  --origin https://huggingface.co/unsloth/Qwen3-0.6B-GGUF/resolve/50968a4468ef4233ed78cd7c3de230dd1d61a56b/Qwen3-0.6B-Q6_K.gguf \
  --capability chat --redistribution allowed
```

Verified with `modelctl list --verify`.

## 4. Broker/model-load then prompt, SSE stream, cancellation/owned drain

Use the existing Dulche-owned broker — do **not** start a raw `llama-server` on
broker port 9477, and never treat a metadata `GET /v1/models` listing as
generation.

- Broker CONTROL socket: `$XDG_RUNTIME_DIR/haven/inference.sock`.
- Owned worker socket: `$XDG_RUNTIME_DIR/haven/llamacpp-worker.sock`.
- Canonical engine: `/usr/lib/haven/llama.cpp/llama-server` (the Bookworm
  rebuild), invoked by the broker ( `--model <blob> --alias <id>
  --host <workerUnixSocket> --no-ui --no-slots --log-disable --offline
  --parallel 1 --ctx-size <limit> --cache-ram 0`).
- ProviderId mapping (retain, do not rename): Python broker contract providerId/
  model-key prefix = `llamacpp` / `llamacpp:<id>`; C# `LlamaCppModelProvider.Id`
  is exactly `llama-cpp`. The model key `qwen3-06b-q6k` maps to
  `llamacpp:qwen3-06b-q6k` in the Python store.

The reproducible acceptance flow is a **generation** request through the broker
(after the verified model is loaded and its capability probe is positive):

1. Broker loads the model (real `llama-server` under the worker socket) and
   becomes healthy (startup deadline; failure retains the lifecycle lease).
2. Send a genuine prompt; consume the **SSE stream** of tokens.
3. Exercise **request cancellation** and verify the owned worker drain: on
   cancel, the in-flight request stops, the owned `llamacpp-worker.sock` worker
   is drained/joined, and no work is left running.
4. A metadata `GET /v1/models` listing is never counted as generation.

Native raw engine/model prompt/SSE/socket-cancel PASS **independently** (CI run
39562111673). The canonical broker/provider path and laptop acceptance remain
**NOT RUN** here and stay BLOCKED until exercised with evidence.

## 5. Missing prerequisites / NOT RUN

- Application/laptop/canonical managed-broker acceptance — **NOT RUN** (only
  bounded native ARM engine/model evidence exists).
- Distro ABI: the rebuilt engine needs **GLIBC >= 2.36**. The .deb must declare
  `Depends: libc6 (>= 2.36), python3`; never upgrade the owner's libc.
- Actual tensor inventory beyond structural sniff — PENDING (filename is not
  proof; structural sniff passes).
- Managed C# consumer patch compile — NOT RUN (no SDK slot; unchanged).
- No downloads/network/SDK/build/native-model execution performed in this lane.
