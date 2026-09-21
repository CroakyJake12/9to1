# Dulche Shared Runtime Cancellation

## Decision

Dulche runs one broker-owned `llama-server` worker for the shared local runtime. It does not start a server per application. The broker starts the worker with `--parallel 1`, routes all HUI traffic through its private Unix socket, and reports this topology in `GET /health` under `runtime`.

For a chat request, the broker sends the validated Haven `request_id` to the pinned worker as `X-Conversation-Id`. Cancellation first calls the worker's `DELETE /v1/stream?conv_id=<request_id>` control route. This replaces the previous duplicated-socket shutdown used solely to unblock the proxy's blocking stream read.

The broker retains a transport-close fallback only when the worker has not returned response headers yet or the control request fails. A `202` cancellation response reports the applied mode and whether that fallback was used. It never claims that generation has stopped: `workerCompletionConfirmed` remains `false`.

## Why Haven keeps a proxy

Repository history introduced the broker boundary in `8129731` and replaced its duplicated-socket cancellation workaround with the pinned upstream control route in `e57db0d`. The proxy remains necessary for reasons independent of that workaround:

- it exposes a stable Haven-owned API and `llamacpp:` model namespace while keeping Ollama compatibility names unambiguous;
- it admits only manifests and content-addressed GGUF blobs that pass Haven's path, size, version, and digest checks;
- it owns exactly one `--parallel 1` worker instead of allowing per-application servers and duplicated model memory;
- it keeps model-store mutation and downloads out of the sandboxed inference service;
- it hides the upstream API/socket and applies fixed offline, logging, UI, slot, context, cache, and environment policy;
- it maps Haven request IDs onto the pinned worker's request-scoped cancellation mechanism.

The proxy is therefore a policy, admission, compatibility, and lifecycle boundary—not merely a workaround for a blocking read.

## Single-instance arbitration and crash recovery

The broker now acquires an exclusive, non-blocking `flock` on `$XDG_RUNTIME_DIR/haven/inference-broker.lease` before inspecting or removing the public socket. The lease is held for the complete broker lifetime and released only after worker/socket cleanup. A second broker fails without touching the active owner's socket.

Historically, startup relied only on probing `inference.sock`. That distinguished a normally live socket from a stale one, but the probe/unlink/bind sequence was not atomic: concurrent starters could both observe a stale socket and race to remove or replace it. The root cause was using a discoverability endpoint as the ownership primitive. The persistent lease file is now the ownership primitive; the socket probe remains only a compatibility-safe stale-socket cleanup after ownership is established.

Linux releases `flock` when the broker process exits, including `SIGKILL`, so a replacement broker can acquire ownership and remove the crashed broker's stale socket. The lock file itself intentionally remains and is not a PID file. `O_CLOEXEC` plus the worker's `close_fds=True` prevents `llama-server` from retaining broker ownership. Existing API/socket paths and the pinned llama.cpp worker protocol are unchanged.

## Root-Cause Evidence

The pinned upstream commit is `ggml-org/llama.cpp` `5266f24da75dc449bd56cbed7addb9c8e4a6a73e` (v0.4.0), recorded in `upstream.lock.json`.

- `tools/server/server-stream.cpp` makes resumable cancellation opt-in through `X-Conversation-Id`; without a stream pipe, `server_res_spipe::should_stop()` falls back to peer connection liveness.
- The same file registers `DELETE /v1/stream?conv_id=...`, where `evict_and_cancel()` sets the session cancellation flag and finalizes blocked readers.
- `tools/server/README-dev.md`, "Resumable streaming", states that the DELETE route stops the work through the response-reader cancellation path.

This explains the broker's old transport workaround and demonstrates that the pinned worker has a request-scoped control API. The broker integration test now uses a threaded fake worker that records the conversation header and only unblocks its blocked stream when it receives the worker DELETE request.

## Evidence Status And Limits

The control path is source-inspected and model-free integration-tested. It is not yet proven against a real GGUF, the pinned `llama-server` binary, systemd, or the approved VM. In particular, no measured bound exists for cancellation-to-compute-stop latency. Keep the fallback and `workerCompletionConfirmed: false` until a real-runtime cancellation proof records request acceptance, stream termination, worker availability for a later request, and timing on the target runtime.

Single-instance contention and post-`SIGKILL` lease recovery are covered by a Linux subprocess test using the production broker entry point and real Unix sockets. This proves broker arbitration and stale public-socket recovery without a model. It does not prove orphan-worker handling outside the systemd cgroup, real inference, or target-VM behavior.
