# Haven llama.cpp runtime — staged implementation

This directory implements the production boundary for local GGUF inference. It does **not** bundle a model, download a model, install packages, modify the approved VM, or expose llama.cpp directly to HUI.

## Evidence state

Evidence is attached to the exact commit that produced it; one stage never inherits another stage's label.

- Upstream `ggml-org/llama.cpp` v0.4.0 provenance: **inspected and pinned**.
- Haven broker/model admission code: **implemented and CI-gated**.
- Explicit local model manager: **implemented on the lifecycle slice; tested only when that slice's CI succeeds**.
- Pinned CPU `llama-server`: **built/smoke-tested only when the relevant current-head `build-pinned-llamacpp-cpu` CI job succeeds**.
- Pinned llama.cpp source: **not copied into this repository**.
- Model inference: **not runtime-proven until an approved GGUF is actually loaded and prompted**.
- Approved Ubuntu VM: **unchanged until a separately recorded staging/runtime gate**.
- GPU backends: **not built, tested, benchmarked, or runtime-proven**.

A successful binary build is not evidence that a model loaded, generated tokens, met a latency target, or ran inside the approved VM.

## Boundary

```text
HUI / provider registry
  |
  | Haven API over $XDG_RUNTIME_DIR/haven/inference.sock (0600)
  v
broker.py  [model store read-only]
  |
  | private Unix socket
  v
llama-server (single model, no UI/logs/slots, parallel 1)
  |
  v
verified content-addressed GGUF

explicit user action
  |
  v
haven-modelctl  [local import / replace / delete only]
  |
  v
model store
```

The systemd user service restricts the broker and its worker to `AF_UNIX`, denies network sockets, makes the home directory read-only, gives write access only to the runtime directory, and grants read-only access to the Haven model store. Model-store mutation therefore belongs to `haven-modelctl`, not the inference daemon.

## HUI-facing API

- `GET /health`
- `GET /v1/provider`
- `GET /v1/models`
- `POST /v1/models/{id}/load`
- `POST /v1/models/{id}/unload`
- `POST /v1/chat/completions` (streaming; requires a safe `request_id`)
- `POST /v1/requests/{request_id}/cancel`

The API is Haven-owned. Upstream llama-server endpoints are an internal implementation detail. `provider-contract.json` is the machine-readable coexistence contract for HUI/provider work.

## Ollama coexistence and migration

CakeAI's existing provider router already treats provider-qualified keys as first-class and preserves unqualified names as Ollama compatibility names. Haven therefore uses:

- existing legacy name: `<model>` → Ollama;
- explicit existing key: `ollama:<model>` → Ollama;
- new local key: `llamacpp:<model-id>` → this provider.

The llama.cpp broker never proxies or manages Ollama. Retiring Ollama remains deferred until feature compatibility and performance evidence justify it.

## Model store and lifecycle

Default root: `$XDG_DATA_HOME/haven/models` (or `~/.local/share/haven/models`).

```text
models/
  blobs/sha256/<first-two-digest-chars>/<full-sha256>.gguf
  manifests/<model-id>.json
  .staging/
```

Before a model is started, the broker checks:

1. the manifest ID and filename are safe and identical;
2. the blob path exactly matches its SHA-256 content address;
3. the blob exists and has the declared byte size;
4. its header has GGUF magic;
5. its GGUF version is 2 or 3 and is allowed by the manifest;
6. its SHA-256 matches the manifest.

`haven-modelctl` is deliberately local-only. Import copies an already-local regular file into same-filesystem staging, hashes and validates it, adopts the content-addressed blob, then atomically publishes the manifest. Replace/update is explicit and retains the previous blob for rollback. Delete removes the manifest first and only purges a blob when explicitly requested and no other manifest references it.

Mutations are serialized with a store `flock`. The broker holds a shared per-model `flock` for the entire llama-server worker lifetime; the manager requires an exclusive per-model lock for import/replace/delete. This closes the check-then-delete race around a live model.

See `MODEL-LIFECYCLE.md` for crash consistency, permissions, and CLI semantics. GGUF compatibility is never treated as redistribution permission.

## Runtime policy

The first runtime is intentionally conservative:

- one loaded model and one parallel generation slot;
- default context limit 8192 tokens, bounded to 512–131072 by the broker;
- server Web UI and slots endpoint disabled;
- upstream runtime logging disabled to avoid retaining prompts and to remove stderr-pipe backpressure;
- server-side cache-RAM pool disabled for the first slice;
- worker environment strips llama.cpp argument overrides, GGML overrides, proxy/token variables, and dynamic-loader injection variables;
- cancellation actively shuts down the private worker connection so a blocked read cannot ignore a HUI cancel request.

These defaults are safety/resource baselines, not benchmark-derived optimal settings.

## Upstream build

`upstream.lock.json` pins v0.4.0 to commit `5266f24da75dc449bd56cbed7addb9c8e4a6a73e`. `build-cpu.sh` refuses any other checkout and builds only `llama-server` with llama.cpp/ggml shared-library output disabled, native-host tuning disabled for reproducibility, subprocess/HTTPS support disabled, and RPC/CUDA/HIP/Vulkan/SYCL/OpenCL/OpenVINO disabled.

The resulting Linux executable still dynamically depends on normal system runtime libraries such as libc/libstdc++/libgomp; `BUILD_SHARED_LIBS=OFF` does not mean a fully static Linux binary.

The build script performs no clone or package installation. Set `LLAMA_SOURCE` to an already approved checkout and `LLAMA_BUILD_DIR` to an out-of-tree directory before running it.

llama.cpp is MIT licensed. Distribution must preserve the upstream copyright and permission notice; model licences are separate and must be reviewed per model.

## Packaging target

Expected installed files:

```text
/usr/lib/haven/inference/broker.py
/usr/lib/haven/inference/gguf.py
/usr/lib/haven/inference/model_lease.py
/usr/lib/haven/inference/modelctl.py
/usr/lib/haven/llama.cpp/llama-server
/usr/lib/systemd/user/haven-inference-broker.service
/usr/bin/haven-modelctl
```

The model store remains user-owned and outside the OS image.

## Next acceptance gates

1. Lifecycle-slice CI passes, including atomic import/delete and lease-concurrency tests.
2. With a separately approved model licence and GGUF, run model admission and inference smoke tests and record time-to-first-token, generation throughput, peak RSS, and failure behavior.
3. Only after that, stage the package into the preserved approved Ubuntu VM and gather service/runtime evidence.
4. Add accelerator backends independently; no backend inherits a `runtime-proven` label from CPU.
