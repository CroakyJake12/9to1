# Dulche llama.cpp donor and broker boundary

Upstream https://github.com/ggml-org/llama.cpp (`master`) and controlled fork https://github.com/CroakyJake12/llama.cpp are both pinned to `53ed051ce5e8193652e449f43216ca3859454f49` (tag `b11157`; selected 2026-09-24 UTC). The actual unmodified donor source is at `Source/llama.cpp/`, MIT notice `Source/llama.cpp/LICENSE`; no patches. `Dulche/llamacpp/` continues to hold 9-1 broker/model integration code and its older runtime build lock (`5266f24`/v0.4.0). The broker was not replaced, and compatibility with newest upstream source has **not** been established.

## llama.cpp consumption pin corrected 2026-10-31 (measured, not inferred)

The registry named `53ed051ce5e8193652e449f43216ca3859454f49` (tag b11157, upstream date 2026-09-24) as the pinned
revision, but nothing in this repository builds from it. The revision actually consumed is
`3cf03257f219afbe7334045ff7c6a06ac68c627d` (upstream date 2026-09-20), named by `llama.cpp-dependency-lock.json` (tree
`d255198f04f9b8349f1dff24501d513f83cbfeda`, 3982 entries), by the host cache `ggml-VERSION.txt`, by
`.github/workflows/haven-chat-model-ci.yml`, by `.github/scripts/thunder-artifact-audit.py` and by
`NativeStrataCandidatePlanTests.py`. The registry now records the consumed revision, with
`53ed051ce5e8193652e449f43216ca3859454f49` held as a **newer declared revision whose upgrade is NOT RUN** - it needs a
rebuild and a native run of the resident inference host, which this session must not disturb. The
owner's rule is latest-unless-known-regression, so the upgrade is owed, not waived.

Measured today: `3cf03257f219afbe7334045ff7c6a06ac68c627d` is reachable in the controlled fork
https://github.com/CroakyJake12/llama.cpp (GitHub API). Reachability is not a submodule pin and no gitlink was added.
