# Thunder local PC probe — owner-run usage note (pass-11 corrected dispatch)

Dispatch: `parallel16-thunder11-actual-route-stream-20261021-prompt.md` (lane 6). PROBE ONLY.
Root independently verifies and runs; a worker must NOT run a live probe/inference.

## Artifacts
- Probe: `work/pc-worker/thunder-benchmark/thunder_local_pc_probe.py`
  SHA256 `0b5dc2fd37ef4f8dcb02a88add4c6e065e0bfa462652cc62059b73b49c03722e`
- Offline tests: `work/pc-worker/thunder-benchmark/test_thunder_local_pc_probe.py`
  SHA256 `62e94f69bc1c6d3224ff6360d48a2d9157bf3dc61056b7a2e5969dea8a89c304` — **37/37 pass** (incl. real loopback transport/SSE fixtures)
- Regression (compile NOT RUN, root native slot): `Tests/NativeStrataDescendantAfterReapFixture.cpp`
- This note (read by the owner before running).

## What it does
Single-file, standard-library Python 3 probe that runs locally **on the PC** inside the hard
observation window `2026-10-21 15:00:00Z–20:00:00Z`.

- **Verified fixed public inference** `https://ai.9to1.uk/v1` (TLS verification ON via the system
  CA; public route uses a bounded urllib opener that honours inherited HTTP(S)_PROXY/NO_PROXY;
  loopback is always direct). Authenticated only with an API key read from the configured local
  path `~/.config/homepc-ai/api-key` **or** an explicit `--key-file PATH`. The key path is used
  only if it actually exists; missing/auth failure **fails closed**. The key **value** is never
  logged/copied/CLI/env/output.
- **Routing fix (pass-11):** the URL is built as `scheme://host:port` + the allowlisted request
  path (the path already includes the `/v1` prefix), so the real `GET /v1/models` /
  `POST /v1/chat/completions` routes are used — the pass-10 double-`/v1/v1` defect (404) is
  gone. Redirects are **refused** (custom `HTTPRedirectHandler`): a 3xx surfaces as a readable
  response and is never followed, so `Authorization` is never forwarded to a redirected origin.
- **Timely bounded reads:** each blocking read uses `read1` with a per-read socket timeout bounded
  to `min(remaining UTC+monotonic budget, READ_STALL_MAX_S=0.5s)`. A stalled stream loops until
  the absolute deadline stops it (no whole-4096-byte read delaying the first token).
- **SSE stream:** standard CRLF (`\r\n`) delimiters are normalized. A server `error` event fails
  that request with a bounded, sanitized category (`engine_crash` / `auth_error` / `rate_error` /
  `server_error`). `engine_crash` / `auth_error` / `rate_error` STOP the run loop; the probe still
  captures post-inventory once (no retrying/reloading). Empty/changed/unknown inventory never
  launches inference.
- **Model selection:** exact `/models` ids `3-8-flash--q3-pro-` and `strata-dynamic`; **default
  chat model is `strata-dynamic`** when present, else the flash id. Only observed ids are used,
  pre and post.
- **Optional local loopback 8081** is separate, enabled only via
  `THUNDER_PROBE_LOCAL_8081=1`; records only the IDs that endpoint actually returns and never
  assumes the public id/alias.
- **Two run modes:**
  - default **quick-smoke**: `2+2` short / light long, `max_tokens=32`, 45s ceiling.
  - `--baseline`: documented **bounded baseline** — fixed synthetic prompts, `max_tokens=256`,
    90s ceiling, 5 interleaved SHORT/LONG (steady-decode sample). Cached/uncached are never
    mixed as the same prefill.
- Bounded read-only native observations (OS/CPU/RAM, optional `nvidia-smi` preflight — labelled,
  never a workload-peak sample).
- Client totals (`first_text_delta_s`, `total_s`) are kept **separate** from server-provided
  `usage`/`server_timings` (bounded numeric scalars only). No fabricated GPU data.
- Private result: fresh, exclusively created private 0700 output directory (unique
  `thunder_probe_private_<token>/` under the base), unique exclusive 0600 JSON, atomic and
  non-overwriting. A pre-existing dir is never chmod'd; a symlink base is refused. No secrets /
  personal paths / hostname / raw payloads.

Honest limitations always recorded: current-service/client timings only (includes public network
overhead); candidate/upstream comparison NOT RUN; candidate deployment/loading NOT VERIFIED; GPU
workload binding/attributed RAM NOT VERIFIED; physical RTX 5080/model GGUF/source/VRAM not
verified from the public API; no speed-gain conclusion / full-engine / Windows / app acceptance.

## Exact OWNER command (one command — owner executes only after root approval)
```sh
cd /workspace/9to1-mercury-thunder/work/pc-worker/thunder-benchmark && python3 thunder_local_pc_probe.py
```
If you want the documented bounded baseline (fixed synthetic prompts, 256-token cap, 90s, 5
interleaved each), add `--baseline`:
```sh
cd /workspace/9to1-mercury-thunder/work/pc-worker/thunder-benchmark && python3 thunder_local_pc_probe.py --baseline
```
If the key is not at the default local path on this host, pass the explicit path (never the value):
```sh
cd /workspace/9to1-mercury-thunder/work/pc-worker/thunder-benchmark && python3 thunder_local_pc_probe.py --key-file /path/to/owner/keyfile
```
Run exactly this on the PC **during the window**. Reviewer/worker must NOT run it. The script
exits immediately with `window_status=not_started|window_closed` outside the window (no
hardware/service requests then). Optional local loopback is NOT attempted unless
`THUNDER_PROBE_LOCAL_8081=1` is set explicitly.

## Offline tests (worker-run; no live PC/key/8081/public HTTPS/model/network)
```sh
cd /workspace/9to1-mercury-thunder/work/pc-worker/thunder-benchmark && python3 test_thunder_local_pc_probe.py
```
Result: **37/37 pass** — mocked clocks + transport, plus **real** loopback HTTP fixtures:
exact route (no double `/v1`), two-server no-redirect credential safety, delayed local SSE stream
(first content observed before later body released), real SSE error detection, and deadline+stall
bounding; CRLF normalization, `_classify_sse_error` categories, `--baseline` prompts/cap/plan,
fatal-stop-while-still-capturing-post-inventory, and empty-inventory-never-launches.

Checkpoint (delivered for root review): code `thunder_local_pc_probe.py`,
`test_thunder_local_pc_probe.py`, this note; SHA256 sums above.
