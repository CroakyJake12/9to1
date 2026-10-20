# Thunder benchmark harness — preparation (NOT RUN)

Status: **NOT RUN / BLOCKED**. No authorised RTX endpoint is connected and the owner window
(21:00–22:00 Europe/London = 20:00–21:00 UTC on 2026-10-20) has not opened. This directory is
offline preparation only. Nothing here has executed on any owner PC.

## Purpose

Produce repeatable, objective measurements for the embedded Strata engine (Thunder) after the
upstream upgrade, on the owner's actual hardware:
**Windows desktop, RTX 5080 16 GB, i9-14900K, 64 GB DDR4**. Primary model: installed
Qwen3.8-Flash-Next **subject to actual installed-identifier verification**.

Historical Q2 ~90 tok/s and Q3 ~50 tok/s are uncontrolled observations, **not** baselines.

## Fail-closed guard contract (for the future runner)

The runner MUST NOT begin (or continue) unless ALL of the following are true at execution time:

1. **Owner window verified** — current UTC time is within [20:00, 21:00) on 2026-10-20
   (21:00–22:00 Europe/London), checked from an authoritative clock at start **and** continuously;
   stop before 21:00 UTC. Late-arriving endpoint: start only if still in-window.
2. **Endpoint positive verification** — the authorised integration is actually connected
   (health/hello probe succeeds against the real endpoint), not assumed from old bridge statuses.
3. **Device verification** — `nvidia-smi` reports RTX 5080 (or the authorised configured device)
   with expected VRAM; fail if a different GPU is present.
4. **Model verification** — the loaded model identity/registration matches the planned model
   (Qwen3.8-Flash-Next) from actual runtime state, not from config string.
5. **No destructive changes** — never change drivers/clocks/voltage/power-limit/BIOS/OS-security;
   never kill unrelated processes; never expose credentials or personal files.
6. **Rollback retained** — the known-good pre-upgrade engine build and original baseline artifacts
   remain intact before any measurement (the current maintained pin is retained).

On any revocation or at window end the runner stops immediately; it never extends by elapsed-time
assumption. If any guard fails, record BLOCKED and do not emit results as passes.

## Planned methodology

- Compare: existing embedded engine (current pin), latest unmodified upstream (candidate v0.2.1/
  engine v0.1.46), and (optionally) optimised Thunder under identical model/quantisation/context/
  prompts/decoding/configuration.
- Metrics: prefill speed, decode tok/s, TTFT, peak VRAM/RAM, utilisation, short/long context,
  correctness, stability.
- Interleaved repeated measurements; medians and variability; **≥5 valid runs per configuration**;
  separate load/prefill/steady-decode; distinguish gains from noise.
- Priority if the window is short: baseline first, then the most promising optimisation.

## Exact build/resource needs (for the coordinator's separate build slot)

The native Strata package build and CUDA compile are NOT permitted in this bounded source turn.
When the root allocates the serialized heavy-build/native slot, the next steps are:

1. Verify the candidate lock (generated today, in /tmp and reproducible via the tool):
   ```sh
   python3 "9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/prepare-upstream-update.py" \
     verify --archive <v0.2.1.tar.gz> --lock <candidate-lock.json>
   ```
2. **Adapter rework is required first** (see THUNDER-DISCOVERY-2026-10-20.md §3): the current
   bridge symbols do not exist in v0.2.1's tree. Map ABI v1 functions to the new engine API, then
   build:
   ```sh
   python3 "9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/build-package.py" \
     --archive <verified-v0.2.1.tar.gz> --work /new/build-work \
     --build --install /new/dulche-package
   ```
   Requires Linux x64, C++20, CMake ≥3.24, CUDA ≥12.8 (engine sm_120/RTX 50 targets).
3. Run the runtime/test suites in a build-enabled environment; retain known-good rollback.
```
. Continue only after the API mapping and build are proven; then benchmark per the guard contract.
