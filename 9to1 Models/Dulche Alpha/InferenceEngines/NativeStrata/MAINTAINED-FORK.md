# Dulche's pinned Strata fork

Origin: https://github.com/ro99/strata

Commit: `015b075079c51a7aec670ee24924f920f5e7bb2b`.
Git commit tree: `491a4a74e538bd469f83811c8a907cc7d6118db9`.
Archive: 1,476,955 bytes, SHA256 `5b89b7b61a21cb6bee50f3a15b0f904a3c9324fdacc05f105128db69ebb3a62e`.
`strata-source-lock.json` binds all 303 original blobs (7,805,688 source bytes).

The maintained fork is the exact upstream source plus these additive native bridge files.
There are no upstream body edits, separate installed Strata application, HTTP server,
new Task/Run owner, or provider fallback loop. The C ABI and bounded dedicated-worker
protocol are version 1. Upstream's C++ headers are internal; upgrading the commit
requires re-verifying the source lock and rebuilding/reviewing this adapter.

The original Apache 2.0 license is in `LICENSE.strata`. The additive bridge is also
licensed under Apache 2.0. Upstream's `vendor/marlin` license has the same exact body;
the installed package preserves both original paths and `MARLIN-UPSTREAM-README.md`.
The latter identifies vLLM 2.3.8 distribution provenance. That import's original
vLLM commit was not recorded; the Strata import and local modifications remain
trackable through this exact immutable donor commit. No stronger provenance is claimed.

Fetch the exact public archive through a permitted network route, or recover the
owner-only task archive at https://drive.google.com/file/d/1vNbuH_SK0-Wqd89HMJqeedjl3W1-I35i/view.
The saved object size is verified; its remote checksum/roundtrip was not returned.
Every recovery must verify the local SHA above. The historical donor receipt's
`treeSha` field is GitHub's tree-API commit projection; the actual Git commit tree
listed above is independently verified.

Preparation (no compilation or execution):

```sh
python build-package.py --archive /path/strata.exact.tar.gz --work /new/source-work
```

Build/install the bundled runtime into Dulche's package, with no weights or tests:

```sh
python build-package.py --archive /path/strata.exact.tar.gz --work /new/build-work --build --install /new/dulche-package
```

The actual donor requires Linux x64, a C++20 compiler, CMake 3.20+, and CUDA 12.8+.
Its CUDA targets are 8.6 and 12.0. The build script refuses absent/older CUDA and
does not promote a CPU stub. The current worker admits centralized Safetensors
registrations only; rank-local TP2 remains an explicit unsupported adapter path.
No research, sampling, precision, or topology switches are enabled for rankings.
The default model-specific flash-attention choice is preserved. Placement-cache
writes are disabled; no unreviewed cache path is created by the child process.

The package contains `runtimes/linux-x64/native/dulche-strata-worker`, original
licenses, a source lock, and an actual binary-digest build receipt. Availability
requires that installed protected binary plus an actual held model/artifact owner,
observed hardware and successful native initialization. The managed provider
supports text and streaming. Structured tools additionally require the explicit
version-1 tool command/terminal, independent worker-bound Tools inventory and a
real correlated proposal probe from the same approved loaded model. Images remain
unsupported. The native
C ABI preserves actual multimodal parts for a future supported managed adapter.
Load/prefill cancellation terminates this isolated worker and invalidates its KV;
reinitialization is required. Shutdown requires its terminal protocol record.

Runtime Auto filters every declared hard requirement, then orders compatible
engines by manifest preference and the broad llama.cpp default. This checkpoint
does not claim local timing measurements or calibration. Manual overrides never
substitute an engine unless the caller grants fallback after the same observed
initialization failure. All original failed tasks/causes remain in custody even
when a later compatible initialization succeeds; strict drain does not erase them.

The actual same-owner managed factory, model-bearing startup bridge, protected
installed-binary/model observation producers and Dulche Compile inclusion must be
paired before runtime use. Existing llama startup is preserved. Engine switching
waits for the previous actual lease before a fresh session, preserving the same
canonical model/request/session/context/permission owners; a pending request
prevents a switch. Native CUDA/model execution, calibration, packaged shipping
acceptance and full same-Task takeover remain unverified in this source checkpoint.

## Structured tool request path

Command 4 carries version 1 and the SAME model-facing bounded tool request; terminal
7 carries version 1 and the actual complete generated envelope. Operations 1/2/3,
ABI/source hello, ordinary text/stream records and all protected load/CUDA requirements
remain unchanged. An older worker refuses command 4; its unread/failed pipe is drained
and retired instead of being relabelled tool-capable.

The maintained managed codec freezes the exact tool definitions/schemas, original
assistant call identifiers and ordered matching tool results. It supplies the model
with an explicit `dulche_strata_tools_json_v1` JSON envelope contract. The actual
model must generate that envelope: plain text, fences, ambiguous/foreign fields,
foreign tool names, duplicate/reused IDs and non-object arguments are refused. Empty
calls are valid ordinary replies. The bridge does not constrain a model into a canned
call, create a tool execution, grant permission, or parse ordinary text requests as
calls. Generic JSON prompting is not a capability claim for a model family.

Support inventory expresses engine eligibility, not effective model readiness.
After genuine protected initialization, a Tools-bearing original Task admission is
re-read around a harmless real-model nonce proposal probe. Tools appears in the raw
catalogue only after its correlated result and the independent original build support
both validate. The probe never executes its proposed function. The same native model
lease, retained driver/raw pipe tasks, UTF-8 terminal decoder, failed-session exit/stderr
join and final close own these operations. Central ManagedProviderDulcheAdapter and
Task/Run tool ownership still validate every real request/proposal and execute tools.

This isolated source proposal has not run a compiler, native build, CUDA/model probe
or protected execution. Synthetic pipe/codec tests distinguish source behavior from
real CUDA acceptance. A truthful genuine native binary, support inventory, Safetensors
checkpoint, hardware and physical protection remain required for positive execution.


The structured terminal additionally carries the exact bounded `open`, `close` and
`opened_by_prompt` values from the SAME native registration retained only after
successful initialization of the current session. Failed loads and shutdown clear
that retained registration. The host first proves equality of the entire decoded
token stream and terminal (including reasoning); it then separates only the
actual declared preamble/termination and parses the unchanged strict JSON tool
envelope. Prompt-opened models start inside their declared block. Missing/foreign/
orphan delimiters refuse. No model-name heuristic, generic thinking-budget mapping,
capability boolean or arbitrary search for JSON is used. Whole raw reasoning plus
final content stays under the original finite byte limit. The existing ordinary
text terminal is unchanged. These source controls do not prove an actual CUDA
model, independent signed support inventory or end-to-end tool acceptance.
