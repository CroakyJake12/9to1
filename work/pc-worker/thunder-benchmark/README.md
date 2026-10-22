# Dulche Thunder benchmark and owner-PC observation tools

Thunder is Dulche's embedded Strata integration. These tools neither create another inference engine nor promote an untested release.

## Current owner-authorised observation window

22 October 2026, 17:10–21:00 UTC (18:10–22:00 Europe/London). The owner made the desktop available until 22:00 BST. The fixed absolute cutoff is checked throughout; the next day requires renewed owner authorisation. There is no automatic date rollover.

The owner PC is Linux according to the latest owner clarification. Actual GPU, RAM, model and engine identities must come from observations. Expected RTX5080/16GB, i9-14900K and64GB are not measurements.

## Owner-run probe

`thunder_local_pc_probe.py` is a standalone Python3 standard-library tool. Its API key comes from the existing `~/.config/homepc-ai/api-key` or an explicit `--key-file PATH`; no key value is accepted on the command line or written to evidence. It contacts only the fixed authenticated HTTPS inference endpoint `https://ai.9to1.uk/v1`. Optional local metadata reads use only `127.0.0.1:8081` and are enabled explicitly:

```sh
THUNDER_PROBE_LOCAL_8081=1 python3 thunder_local_pc_probe.py --inventory-only
```

Inventory mode collects bounded native hardware observations and allowlisted local/public service metadata. It performs no inference POSTs, deploys no engine, changes no model/configuration and does not interrupt the running service. This is the first command to use while Root's single-request public baseline is in progress. Its output does not prove an upgraded engine is running, authenticate a model-weight checksum or establish benchmark comparability.

The separate `--baseline` mode uses the existing fixed short/long prompts, five interleaved runs per size,256-token output cap and90-second per-run limit. Run it only when the coordinator has allocated the single shared inference request lane. An engine/auth/rate error stops further inference. A public API stream's client timings do not establish pure engine decode or upgrade speed gains; queueing, prefill and prompt-cache observations remain separate.

Evidence is written to a fresh private0700 child directory under `thunder_probe_private` beside the downloaded script, with unique0600 JSON files. The launcher should print the download/output directory so the owner can locate it. Existing evidence is never overwritten.

## Offline regression checks

```sh
python3 -B -m unittest test_thunder_bench test_thunder_local_pc_probe
```

The tests use synthetic inputs, owned short-lived subprocesses and ephemeral loopback test servers. They do not use live owner credentials, GPU or inference. Fixtures use their owning temporary directory rather than requiring the unrelated `/tmp/opencode` directory. Tests enforce exact start/end/date handling, native observation deadlines, no inference in inventory mode, public-model identity checks, bounded streaming, credential-origin protection and failure evidence retention.

## Upstream and comparison workflow

The latest officially verified stable release on22October remains Strata v0.2.2/engine0.1.47, commit011c9e600a93bbc9fd451c563501b5831c32bbca. The existing CUDA13/SM120 candidate was compiled in CI before this checkpoint. Preserve its pinned source/ggml/CPU runtime dependencies, licences and known-good rollback. Native GPU/model execution remains an independent gate.

`thunder_bench.py --offline-compare --baseline baseline.json --candidate candidate.json` validates at least five successful runs per configuration, exact model/workload/provenance comparability, finite phase metrics and consistent engine identity within each group. Reports are descriptive: raw values, median/MAD/range and descriptive delta. `gainReliable` remains false; matching metadata and five runs alone do not prove a reliable gain.

`NativeStrata/check-upstream-release.py` detects official newer stable releases without replacing pins, downloading model weights or automatically promoting an engine. Upgrades remain isolated, archive/tree/licence verified, compatibility tested, benchmarked on authorised actual hardware and independently accepted before integration. Retain rollback versions and reconcile patches explicitly.
