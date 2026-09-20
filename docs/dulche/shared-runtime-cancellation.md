# Dulche Shared Runtime Cancellation

## Decision

Dulche runs broker-owned `llama-server` workers for the shared local runtime, one per explicitly loaded model slot. It does not start a server per application. Each worker starts with `--parallel 1`, has its own private Unix socket, and serves CUI/provider traffic through the broker. `GET /health` reports the `single-broker-owned-slot-workers` topology and loaded slot diagnostics under `runtime`.

For a chat request, the broker sends the validated Haven `request_id` to the pinned worker as `X-Conversation-Id`. Cancellation first calls the worker's `DELETE /v1/stream?conv_id=<request_id>` control route. This replaces the previous duplicated-socket shutdown used solely to unblock the proxy's blocking stream read.

The broker retains a transport-close fallback only when the worker has not returned response headers yet or the control request fails. A `202` cancellation response reports the applied mode and whether that fallback was used. It never claims that generation has stopped: `workerCompletionConfirmed` remains `false`.

## Root-Cause Evidence

The pinned upstream commit is `ggml-org/llama.cpp` `5266f24da75dc449bd56cbed7addb9c8e4a6a73e` (v0.4.0), recorded in `upstream.lock.json`.

- `tools/server/server-stream.cpp` makes resumable cancellation opt-in through `X-Conversation-Id`; without a stream pipe, `server_res_spipe::should_stop()` falls back to peer connection liveness.
- The same file registers `DELETE /v1/stream?conv_id=...`, where `evict_and_cancel()` sets the session cancellation flag and finalizes blocked readers.
- `tools/server/README-dev.md`, "Resumable streaming", states that the DELETE route stops the work through the response-reader cancellation path.

This explains the broker's old transport workaround and demonstrates that the pinned worker has a request-scoped control API. The broker integration test now uses a threaded fake worker that records the conversation header and only unblocks its blocked stream when it receives the worker DELETE request.

## Evidence Status And Limits

The control path is source-inspected and model-free integration-tested. It is not yet proven against a real GGUF, the pinned `llama-server` binary, systemd, or the approved VM. In particular, no measured bound exists for cancellation-to-compute-stop latency. Keep the fallback and `workerCompletionConfirmed: false` until a real-runtime cancellation proof records request acceptance, stream termination, worker availability for a later request, and timing on the target runtime.
