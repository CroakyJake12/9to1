# Opt-in q36 host-cache preparation

This is a small preparation profile for the existing embedded Strata q36 engine.
It makes the original bounded-host-cache patch reproducible from a normal 9to1
checkout. It does not select an engine, update the active source lock, apply a
patch to upstream, compile anything or enable a production setting.

From `NativeStrata/`, using an explicitly supplied already-acquired source root:

```sh
python3 -B prepare-upstream-update.py prepare-host-cache \
  --source-root /owner-held/Strata-source \
  --out /isolated-parent/new-host-cache-packet
```

The output must be new and outside the provided upstream directory. Existing
entries, including dangling links, refuse. The updater validates every selected
input and payload before creating output, uses exclusive/no-follow file creation,
and emits only the unchanged helper, integration patch, CPU smoke, notices and a
deterministic preparation receipt. It computes the patched engine in memory and
checks its exact expected digest; no full postimage is written. Apply/build is a
separate Root-owned operation after source, dependency and resource admission.

`profile.json` is selected-file metadata, not a complete archive source lock. Its
ten exact file identities reference official stable Strata `v0.2.3.1`, annotated
tag `9bc5216413cefc7d1e4dd5f3f9d2f31aef6228f1`, commit
`7ddc17a38fe7f31637e895ecfba58c2cd43d65ec`, tree
`ead28543fa3f3d02fb595754c3c0603a10b3580c`. Each row retains its official source URL.
Source CMake says `0.1.48.1`; release notes retain distributed engine packages
`0.1.48` and change Control to `0.1.48.1`; upstream AGENTS identifies engine
`0.1.48`. These distinct version observations do not change Dulche's candidate.

The selected q36 engine is byte-identical to the old `011c9e6...` prototype base:
213557 bytes, Git blob `03660de40aefcbab150cd8f0e50a92d973e3ab3f`, SHA-256
`d0401ecb4b52d4a4a9abac1112d4fef9164b692b1bd808dea51a9e07baa23e05`.
Expected patched digest:
`991caec2b613372ed4a7c34a16b808250982c691c5f066c7d4702edbf565b820`.
The original helper, patch and smoke retain their exact CPU-proof bytes. No new
cache algorithm, CUDA kernel, scoring, GPU-cache, packing or MTP change is added.

The source reader opens with `O_NOFOLLOW|O_NONBLOCK`, requires a regular file via
`fstat`, bounds reads, and refuses observable descriptor metadata/size changes.
Platforms unable to enforce these flags refuse this preparation mode. Parent
components are not confined; this is not a filesystem snapshot. Matching selected
bytes never proves the rest of a checkout, its Git HEAD or a complete build and
dependency closure. The separate main-core ExpertSource/ExpertCache API is not
modified or wrapped.

The source/MIT notice is `LICENSE.strata`; exact official GGML MIT notice and
version pin are `LICENSE.ggml` and `ggml-VERSION.txt`. Preserve these in future
fork/package integration. GGML's pin remains
`3cf03257f219afbe7334045ff7c6a06ac68c627d`; other build dependencies and model
licenses still require their full separate custody. Existing legacy Apache and
candidate notices/pins remain untouched.

Run the build-free update tests using the normal `Tests/NativeStrataUpdateToolTests.py`.
The small synthetic tests verify preparation mechanics and refusal cases. Actual
upstream applicability and relocation use a genuine external selected-source
fixture supplied explicitly:

```sh
DULCHE_STRATA_HOST_CACHE_SOURCE_ROOT=/owner-held/frozen-source \
  python3 -B -m unittest -v NativeStrataUpdateToolTests.ExternalHostCachePreparationTests
```

Run that command from `Dulche Alpha/Tests`. No network or upstream source is
checked in for tests. Missing external fixtures report NOT RUN/skip; an explicitly
supplied invalid fixture fails. Relocation copies only the small updater/profile
assets and proves identical emitted bytes while retaining the external source.

The unchanged helper previously passed a GCC14 CPU-only smoke. These preparation
checks do not build the patched engine or prove CUDA/HIP/SYCL, MTP/streaming/
cancellation behavior, total RAM fit, output quality or performance. The owner's
later desktop source plus Sage/resource patches remain absent, and hardware
testing requires a new authorised window. No current-desktop compatibility,
speed, acceptance or production-readiness claim follows from this profile.
