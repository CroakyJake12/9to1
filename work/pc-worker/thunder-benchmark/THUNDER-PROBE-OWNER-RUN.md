# Thunder local PC probe — owner-run usage note

Dispatch: `parallel16-thunder6-fresh-probe-20261020-prompt.md` (lane 6) + handoff
`parallel16-thunder-pass6-fresh-handoff-local-probe.prompt.md` (SHA256
`0a9b8a94dca730878305b74a94415299abddfd7835715e2a12f381d64f802e05`).

## Artifacts
- Probe: `work/pc-worker/thunder-benchmark/thunder_local_pc_probe.py`
- Offline tests: `work/pc-worker/thunder-benchmark/test_thunder_local_pc_probe.py`

Probe SHA256: see the `sha256sum` line in the checkpoint below (computed on the exact file at
delivery).

## What it does
Single-file, standard-library Python 3 probe. It runs locally **on the PC** inside the hard
observation window `2026-10-20 20:00:00Z–21:00:00Z` and never talks to the public endpoint.

- Bounded read-only native observations: OS/release/architecture, CPU model/count (whitelisted
  `/proc/cpuinfo` fields or Windows `PROCESSOR_IDENTIFIER`), physical RAM (`MemTotal` or Windows
  `GlobalMemoryStatusEx`), and optional `nvidia-smi` query if the binary is already present.
- Fixed loopback allowlist only (`127.0.0.1:8081`) with read-only `GET /health`, `/version`,
  `/props`, and authenticated `GET /v1/models` pre/post.
- Reads only the existing `~/.config/homepc-ai/api-key` (bounded); never printed/copied/logged.
- Six ordered interleaved runs `short,long,short,long,short,long` on the current loopback service,
  `stream=true`, `max_tokens=32`, deterministic synthetic prompts (long context ≤16KiB UTF-8).
  The sample target is **five successfully completed runs** (target incomplete otherwise).
- Monotonic client timings only (request start, time-to-first-text, total) + output char/hash and
  API usage tokens only when actually reported. No fabricated tokens or gains.
- Private result written atomically: `0600` file, `0700` parent (POSIX), standard schema/version,
  UTC window, native sources, model pre/post, ordered runs, failures and limitations. No secrets /
  personal paths / hostname / raw payloads.

Honest limitations always recorded: current-service/client timings only; candidate/upstream
comparison NOT RUN; candidate deployment/loading NOT VERIFIED; GPU workload binding/attributed RAM
NOT VERIFIED; no speed-gain conclusion / full-engine / Windows / app acceptance.

## Exact OWNER command (one command — owner executes only after independent review)
```sh
cd /workspace/9to1-mercury-thunder/work/pc-worker/thunder-benchmark && python3 thunder_local_pc_probe.py
```
Run exactly this on the PC during the window. Reviewer/worker must NOT run it. The script exits
immediately with `window_status=not_started|window_closed` outside the window; it performs no
hardware/service requests then.

## Offline tests (worker-run; no live PC/key/8081/model/network)
```sh
cd /workspace/9to1-mercury-thunder/work/pc-worker/thunder-benchmark && python3 test_thunder_local_pc_probe.py
```
Result: **12/12 pass** (mocked clocks + transport; window before/after, trickling deadline,
malformed metadata, missing-key no-leak, unsafe model-ID redaction, exclusive 0600 output, pre/post
model mismatch, synthetic short/long order and incomplete counts).

Checkpoint (delivered for root review): code `thunder_local_pc_probe.py`,
`test_thunder_local_pc_probe.py`, this note; SHA256 sums recorded in the checkpoint below.
