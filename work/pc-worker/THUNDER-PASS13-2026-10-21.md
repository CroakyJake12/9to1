# Thunder Pass-13 — managed production compile fix, truthful GGUF admission, bounded probe fix (lane 6)

Date: 2026-10-21. Session continued from pass-12 (frozen, SHA256 d856d2797823d32974777f1a45d3add239f63a16d8ed412800bcee083439d918).
Scope: pass-13 exclusive objectives. SOURCE/PYTHON ONLY, no SDK/native/.NET/CMake/CUDA execution, no PC
inference/model download/credentials/commit/push/deploy/global-config/cap changes. Root D36 owns normal
Dulche production work; edits here limited to `StrataNativeWorker.cs` (allowed) and the NativeStrata / probe lane.

## 1. Actual normal managed production compile failure (CS0411) — fixed
Root normal `Dulche.Runtime.csproj` build failed at 16:16:08 on two errors in the frozen candidate class:
```
StrataNativeWorker.cs(804,97) CS0411 typearguments for StrataCandidateWorker.Publish<T>(Func<Task<T>>) cannot be inferred.
StrataNativeWorker.cs(810,12) same CS0411.
```
Both are the throw-only async unsupported-structured-tools lambdas (`ProbeOriginalStructuredToolsAsync` -> `Task<bool>`,
`ToolsOriginalAsync` -> `Task<OllamaToolResponse>`). Because each body only throws and never returns a value, C# cannot
infer `T` for `Publish<T>(Func<Task<T>>)`.

Fix (minimal, production, faithful): give explicit generic result types on the `Publish` calls:
- `ProbeOriginalStructuredToolsAsync` -> `Publish<bool>(async () => { ... throw new NotSupportedException(...); })`
- `ToolsOriginalAsync` -> `Publish<OllamaToolResponse>(async () => { ... throw new NotSupportedException(...); })`

No canned `false`/success, no tool advertisement, and the original task / cancellation / cleanup / custody flow is
unchanged (the faulted task still carries the precise `NotSupportedException` and the same model-lease/termination
custody). No other candidate `Publish` call site needed changes (all others return inferable values, including
`StreamAsync` which returns `0`).

## 2. Truthful candidate GGUF format admission (V2) — fixed in `StrataNativeWorker.cs`
Independent 7-case packet expects:
- Safetensors metadata admission reaches the genuine ABI/current-origin handshake, then truthful `STRATA_HAS_CUDA=0`
  refusal ("No actual CUDA build exists in this candidate worker.") and the model lease closed exactly once. (Already passed.)
- A GGUF registration must ALSO reach the candidate handshake (same non-CUDA refusal) instead of being refused by the
  legacy Safetensors-only format guard.

Change: in candidate `StrataCandidateWorker.InitializeAsync` (V2), the format gate now admits both `"Safetensors"`
and `"GGUF"` (the engine-native weight format of the confirmed `qwen35moe` family), keeping centralized topology and a
non-empty registration/context. This:
- does NOT claim a model is loaded, does NOT rewrite the held artifact format label, and does NOT invent a conversion;
- preserves the legacy V1 `StrataNativeWorker.InitializeAsync` Safetensors-only guard byte-for-byte;
- keeps unknown formats/topologies fail-closed (message: "admits only the original Safetensors or engine-native GGUF
  (qwen35moe) registration with centralized topology; other formats/topologies are refused closed.").

### Documented coordination need (NOT edited here — parallel Dulche-owned files)
For GGUF to be PRODUCTION-selectable beyond the direct `StrataCandidateWorker.CreateCandidateAfterPublicationAsync`
path, a coordinator must resolve these EXISTING shared-extension/config points (out of lane 6's allowed edits):
1. `InferenceEngineContracts.cs` line ~144 stratum validator: `Require(model.WeightFormat == "Safetensors", ...)` —
   currently rejects GGUF weight-format for strata eligibility. Needs to admit `"GGUF"` for the V2 candidate without
   relabelling the held artifact.
2. `ManagedInferenceEngineFactories.cs`: the production factory currently creates the V1 worker only; V2
   (`StrataCandidateWorker`) is not production-selected. Wiring V2 selection (and its non-CUDA refusal path) is a
   factory/eligibility decision, not a NativeStrata source edit.
3. No config/registry change is made here; `StrataCapabilityRegistry.Resolve` already accepts the confirmed
   `qwen35moe` family spellings (unchanged).

## 3. Small known probe follow-up — bounded socket timeout fixed (thunder-benchmark)
Evidence: `/tmp/9to1-thunder-priority-acceptance-20261021/probe11-independent/actual-read-deadline-failure.json`
bounded 0.7s, elapsed 1.010s to `DeadlineExceeded`; cause: `_set_read_timeout` called `resp.fp.raw.settimeout`,
but for a plain HTTP response `fp.raw` is a `socket.SocketIO` (no `settimeout`; the AttributeError was ignored), so
the real socket stayed unbound and a blocking read could exceed the absolute budget.

Fix in `thunder_local_pc_probe.py`:
- `_set_read_timeout` now reaches the real stdlib socket WITHOUT bypassing TLS:
  * HTTPS -> `fp.raw` is the `SSLSocket`, which owns `settimeout` -> use it (no TLS layer bypass);
  * HTTP -> `fp.raw` is `socket.SocketIO`, which has no `settimeout` -> descend to its `_sock` (real socket) and bind.
  * If neither is present, it leaves the absolute deadline loop as the backstop (never silently claims a hard bound).
- `_read_some` now treats the timed-out-socket `OSError("cannot read from timed out object")` (which surfaces through
  http.client's buffered reader instead of `socket.timeout`) as a stall (return None), so the deadline loop enforces
  the real bound; genuine OSError/ValueError still fail as transport errors.

New fixture test `Pass11RealRouteStreamTests.test_real_deadline_bound_applies_between_headers_and_body`: a loopback
server flushes headers then withholds the body for 4s; budget consumes part of the budget between headers and body read;
asserts `DeadlineExceeded` at ~the bound (elapsed < 3.0s), which only holds if the per-read socket read is actually
bounded. Existing `test_real_stall_deadline_aborts` still passes.

Full probe suite: 38 tests pass (real route, redirect refusal, delayed SSE, SSE error detection, stall/deadline, and
the new header/body-bound test). All existing probe behaviors preserved.

## Validation performed (functional, source-level)
- `python3 -m unittest test_thunder_local_pc_probe` -> 38/38 OK.
- `NativeStrataCapabilityDispatchTests` (34) and `NativeStrataCandidateManagedWireTests` (15) OK; `py_compile` clean
  on edited Python test files.
- StrataNativeWorker.cs reviewed region-by-region; the two previously-failing Publish sites now carry explicit type
  arguments; legacy V1 guard unchanged.

## Explicit NOT RUN / NOT performed
- NO SDK/native/.NET/CMake/CUDA compile or run (Root D36 normal managed rerun pending serial SDK allocation; this pass
  is source fix only).
- NO GGUF/Safetensors model loading, no weights download, no GPU/engine/model acceptance, no tool/vision/MTP claim.
- NO claim that upstream v0.2.2 fixes owner engine exit-6 (issue #3465 is whole-prompt batched retry after non-finite
  logits; no actual version/logs).
- NO PC/live inference by worker; NO credentials/global config/cap changes; NO git commit/push/deploy; no edits to
  other worktrees or frozen reviewer copies; no edits to parallel Dulche-owned `InferenceEngineContracts.cs` /
  `ManagedInferenceEngineFactories.cs` / `LlamaCppRuntime` (coordination documented in §2).
- Verified v0.2.2 pin, complete 4625 lock, licences, and legacy rollback left unchanged this pass.
