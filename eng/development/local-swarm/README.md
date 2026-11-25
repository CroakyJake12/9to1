# Local priority swarm and native Inkling handoff

The local dispatcher runs through the installed Dulche API and follows the mission order: Spaces and Assistants, Dulche, Build, Files, then P1. The reviewed manifest contains exact source/test ownership. Blocked tasks require a concrete repaired cause before resumption. Existing user changes and earlier candidates remain isolated.

## Runtime

`harness.py` provides scoped source actions, actual token-budget checks, bounded model requests, independent review, guarded SDK checks, and reviewed candidate commits. `priority_harness.py` chooses only loaded qualified models and applies the ordered manifest. The V100 admits one generation at a time while retaining its actual cache slots. A qualified native Inkling model may run in parallel on the RTX 5080.

`context_receipts.py` creates a bounded working context without replacing canonical history. Exact observed reads are cached with source fingerprints; evicted evidence can be recovered. Earlier immutable generation archives and checkpoint backups remain private runtime evidence.

The 22:00 publisher integrates only reviewed, test-passing owned commits on `swarm/2026-11-24-local`, checks changed paths, runs both Python verification suites and the combined owning SDK tests, then verifies the pushed Git revision. New candidates are not full native UI acceptance or full specification completion.

## Native Inkling

`inkling-runtime/` records the deployed runtime patch, ownership/thermal checks, installation and baseline qualification receipts. The native model uses the existing verified Inkling GGUF, dedicated signed Strata Inkling engine and verified DSpark drafter. It has its own Dulche identity, owned systemd unit, authenticated loopback endpoint, tokenization and cancellation.

The inherited RTX guard previously targeted the V100 worker. The reviewed replacement points only at identity-proven native RTX units, retains an 80°C ceiling and 8 GiB host-memory floor, removes the obsolete RTX jump/rise checks, and binds the native process to the independent guard watchdog. The V100 rule remains unchanged. Original guard source and rollback configuration are preserved on the PC. Twenty guard/adapter checks passed, including actual isolated process termination while the real V100 PID remained unchanged.

The first native 1024-output-token benchmark measured 58.7 engine tokens/s and 56.83 end-to-end tokens/s with five executable code-smoke cases passing. These are measured baseline figures, not the public Basalt or 5090 aggregate headline rates. See the separate current qualification receipt for later selected settings. No model weights, credentials, private conversation histories or user profiles are included here.

## Checks

Run `python3 -B test_harness.py` and `python3 -B test_priority_harness.py` from this directory. The runtime patch applies to the recorded installed Dulche baseline; it must not overwrite unrelated changes in the canonical service source. Deploy through the normal immutable installer, retain the guard, and qualify actual output before admitting the model to development.

Patch files are stored losslessly as `.patch.json`; extract the `patch` string before applying to the recorded baseline. This preserves significant unified-diff whitespace while keeping ordinary source whitespace checks enabled.

The bounded FP8 KV comparison measured 57.7 engine t/s and 55.98 end-to-end t/s on the same 1,024-token request, with the same five smoke cases passing. The original FP16 configuration was restored because FP8 provided no measured gain in this trial. Current verification includes the selected source and engine digests and fresh restore health. The Python suites now contain 26 checks.

## Known qualification gap: Q57

Q57's owned `TaskRunOriginalRunControlTests.cs` is excluded from the portable
`net10.0` target of `Haven.Desktop.Tests.csproj`; the Windows
`net10.0-windows10.0.19041.0` target includes it. This was confirmed by evaluating
the actual MSBuild `Compile` items against project files matching the task base.

Other included partial classes match the configured
`ChatCloudPermissionCallerTests|ChatCanonicalContextProducerTests` filter.
Consequently, a portable test pass alone cannot validate changes to this owned
file, and a nonzero test count does not resolve the omission. Q57 remains blocked
for verification until the actual owned partial and its dependency closure are
included and successfully tested in the portable target, or a qualified Windows
runner executes the owning tests. No Q57 implementation or acceptance is claimed.

This qualification note is documentation only. The existing 26-check harness
verification receipt and test log are retained; the suites were not rerun for
this documentation amendment.

## Publication status diagnostics

Canonical ownership checks now read Git porcelain separately from diagnostic stderr. A warning about a not-yet-existing owned directory does not imply dirty user work; actual changes and failed status commands still block publication. This repair has two focused regressions. The current harness suites pass 28 checks (18 core, 10 priority); earlier runtime qualification receipts remain separately dated.
