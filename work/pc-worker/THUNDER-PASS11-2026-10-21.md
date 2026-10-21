# THUNDER PASS-11 checkpoint — actual route/stream fixes + SSE stop + baseline

- Date: 2026-10-21 (~17:55 UTC). Lane 6 (continuation of `ses_ea553125dffe0sx2MyJhLF3q7w`).
- Scope: **SOURCE ONLY**. No compiler/native/CMake/.NET/SDK/GPU/inference execution. Lightweight Python
  functional tests only. No commits/push/deploy/global config/credential edits. Frozen reviewer copies
  (`/tmp/9to1-thunder-priority-acceptance-20261021/`) not edited.
- The `dulche_child_process.h` ownership fix from pass-10 is **unchanged** this turn (no actual review
  failure; truthfully documented as Linux-only, not a mathematical proof against all recycled-pgid
  cases).

## 1. Real routing fix (double `/v1` prefix) + no redirects

Independent actual evidence (`probe10-independent/actual-double-prefix-failure.json`): `GET /v1/models`
was sent as `/v1/v1/models` → 404 (`Endpoint.base_url` includes `/v1` and `endpoint.path` adds it again;
`_request` concatenated both).

- **Fix:** `_origin_url()` builds `scheme://host:port` + the request path (the path already carries the
  allowlisted `/v1` prefix). One correct route for GET/POST/local/public. The fixed allowlisted endpoint
  is not changed.
- **Redirects refused:** new `_NoRedirectHandler` returns `None` from `redirect_request`, so any 3xx
  surfaces as a readable `HTTPError` and is never followed — `Authorization` is never sent to a
  redirected origin. No arbitrary URLs, no TLS-disable, no proxy bypass.

## 2. Timely bounded reads (read1 + per-read timeout)

- `_chunks` (`resp.read(4096)`) replaced by `_bounded_chunks` → `_read_some` using **`read1`** with a
  per-read socket timeout `min(remaining UTC+monotonic budget, READ_STALL_MAX_S=0.5s)`. A stalled stream
  loops until the absolute deadline aborts; the first SSE token is no longer delayed until a full 4096
  bytes arrive.
- Normalize standard CRLF (`\r\n`) SSE delimiters.

## 3. SSE error detection → fail request + STOP on fatal

- `parse_sse_chunks` now raises `SSEError(category)` when the server sends an SSE `error` object
  (bounded, sanitized category, never a raw/secrets-bearing payload).
- `_classify_sse_error` → `engine_crash` / `auth_error` / `rate_error` / `server_error`.
- `run_one` records `error_category` and `fatal=True` for engine crash/auth/rate; `main` STOPS the run
  loop on fatal but still captures post-inventory once (no retries/reloads).
- Empty/changed/unknown inventory never launches inference.

## 4. Baseline mode

`--baseline` flag: fixed synthetic prompts, `max_tokens=256`, 90s per-run ceiling, 5 interleaved
SHORT/LONG (steady-decode sample). Default remains the quick-smoke (2+2 / max 32). Client totals vs
server `usage`/`server_timings` kept separate; no fabricated GPU data.

## 5. Test results (all green, stable across repeated runs)

| Suite | Result |
|---|---|
| `test_thunder_local_pc_probe.py` | **37/37 OK** (incl. REAL loopback fixtures) |
| `NativeStrata*.py` (full) | **114 ran / 7 skipped / OK** |
| `NativeStrataTokenizerHelperTests` + `NativeStrataUpdateToolTests` | **34 OK** |

Real loopback fixtures (not source-string mirrors): exact route no-double-prefix (GET+POST exact
path/body), two-server no-redirect credential safety (redirected host never contacted), delayed local SSE
stream (first content observed before later body: `first_text_delta_s < total_s - 0.3`), real SSE error
detection via transport, and stall+deadline abort (`Deadline.from_timeout(1.5)` vs 3s server stall).

## 6. Artifact hashes (pass-11)

| Artifact | SHA-256 |
|---|---|
| `work/pc-worker/thunder-benchmark/thunder_local_pc_probe.py` | `0b5dc2fd37ef4f8dcb02a88add4c6e065e0bfa462652cc62059b73b49c03722e` |
| `work/pc-worker/thunder-benchmark/test_thunder_local_pc_probe.py` | `3c3ac5ef22993030daea75f400c79f689f01a2ac6265731a995dade0e0a723b5` |
| `work/pc-worker/thunder-benchmark/THUNDER-PROBE-OWNER-RUN.md` | `433878fe7704d10ca8237a2f9b5ce7cb347b414163925530067ec309ec89ebb7` |
| `work/pc-worker/THUNDER-PASS11-2026-10-21.md` (this checkpoint) | `e4a1eb0411622200add09f57d3dbdede3dcbb5ba5293b2f24c3c62ac0cfd6b1f` |

## 7. NOT RUN / blocked (recorded, not waived)

- Native fixtures compile+run (descendant-after-reap, child-ownership, candidate worker, NativeFixture):
  root native slot allocated elsewhere. Header fix unchanged from pass-10; real Linux acceptance still
  needed; static review notes `_posix_own_group_alive` matches on pgrp+same session only.
- Live probe/inference: owner/reviewer-run on the PC inside 21 Oct 15–20 UTC; a worker must not run it.
- v0.2.2 archive acquisition still BLOCKED by the ≥256MiB reserve guard (separ fabricated SHA/tree;
  candidate remains v0.2.1; #3465 exists upstream but cannot be claimed to fix owner exit-6 without the
  actual installed version + engine crash logs).

## 8. Owner-run handoff

- Exact owner commands (quick-smoke and `--baseline`) in `THUNDER-PROBE-OWNER-RUN.md`; portable, no
  assumed old PC repo/key path; `--key-file PATH` explicit.
- Build commands for the two C++ fixtures unchanged from pass-10 checkpoint (section 7).
