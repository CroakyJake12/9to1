# ALORA: activation-profiled, selected-expert LoRA

ALORA profiles actual routed-expert use, allocates LoRA parameters to the selected
experts, and trains those adapters while keeping the base, router and shared
experts frozen. The package retains source-bound profiles, measured optimizer
changes and recoverable checkpoints. ALORA here is the project's selected-expert
mechanism; PEFT's invocation-token **activated LoRA (aLoRA)** is a separate feature.

The [operator runbook](RUNBOOK.md) covers exact request schemas, guarded CPU
calibration, partial-profile continuation, workflow preparation and the existing
Dulche supervisor commands.

## Execution scope and evidence

| Path | Implemented scope | Qualification boundary |
|---|---|---|
| Native CPU library | In-memory experts and the mapped Qwen4Exp GGUF text graph; float32/float64 frozen paging; selected adapters; streamed causal loss; retained calibration and checkpoint continuation. | CPU numerical, native-fixture and real-GGUF proof receipts identify the exact source and executed path. |
| Guarded CPU calibration | A pinned child under the existing SDK lock and memory guard, with a retained profile or explicit partial result. | Calibration observes training rows and makes no optimizer update. |
| Prepared CPU training workflow | A pinned executable for the existing isolated Dulche worker, its real control client and verified service checkpoint import. | Real execution also requires an independently admitted corpus, profile and supervisor job. |
| CUDA library candidate | Logical `cuda:0`, FP32, verified current Dulche worker/device/permit state and pinned native reference-kernel policy. | No CUDA arithmetic qualification or production GPU launcher is claimed. |
| Export and serving | Selected merged weight slices can be produced for a separate immutable export. | Full-model export, quantization, serving activation and quality promotion are separate work. |

Exact test totals, revisions, guard results and measured resource use belong in
the retained validation handoff. A proof from one source revision does not
establish execution of a later revision. The package has no demonstrated real
curriculum training result or computer-use quality gain.

## Implemented training flow

1. **Admit inputs.** `TrainingDataset` reuses the existing custody validators.
   It verifies actual sample and file hashes, provenance declarations and
   separation of train, validation and final-held-out files. Its public row
   iterator and snapshot method expose only training rows.
2. **Retain a calibration.** `calibration.calibrate` accepts an already admitted,
   entirely frozen native or paged model. It selects deterministic training rows
   from every category, runs actual dispatch capture and publishes the profile
   with an independent receipt. The default is four samples per category:
   20 total. The guarded CPU CLI supplies the model and real memory/time guard.
3. **Select experts.** `ActivationProfiler` normalizes positive real routing mass
   per token, sample and category. Padding and the masked expert sentinel do not
   influence ranking. Category weighting prevents long samples or heavily
   represented categories from silently dominating selection. Ties are
   deterministic; unused experts do not fill spare budget.
4. **Attach adapters.** The in-memory and paged attachment paths stage
   gate/up and down adapters for the selected expert IDs, preserving the
   intervening activation and original routing weights. Only those adapters
   allocate trainable parameters and optimizer slots.
5. **Train and checkpoint.** `TrainingSession.step_row` revalidates the row,
   computes causal loss, performs backward/AdamW, measures actual adapter changes
   and publishes adapter, optimizer, RNG and cursor state with bound evidence.
6. **Cooperate with Dulche.** `worker.run_training` owns the single readiness
   handshake, honours compute permits and reports completed optimizer updates
   after durable evidence. A clean pause delivers the most recent completed
   checkpoint through the existing single-file checkpoint contract.

Both expert backends preserve the installed native eager dispatch interface:

- `gate_up_proj[expert, 2 * intermediate, hidden]`
- `down_proj[expert, hidden, intermediate]`
- `forward(hidden_states, top_k_index, top_k_weights)`

The in-memory attachment validates native expert classes, materialized floating
weights, unique ownership and absence of inference offload hooks. Quantized
packed weights use the explicit GGUF mapping below.

## Native GGUF text backend

`GGUFWeightStore` owns read-only descriptors and memory maps for the exact shard
set. It verifies sizes, path/descriptor identity and structural metadata, then
pins a SHA-256 of each actual encoded slice. Whole-file shard hashes are
**external attestations**; the store does not claim to have freshly rehashed
every full model file. Quantization comes from each tensor's actual GGUF type:
F32, F16, BF16, IQ3_S, IQ4_XS, IQ4_NL, Q8_0 or Q6_K.

Encoded-read and float32-decode limits apply before slice allocation. Embedding
lookups gather requested rows and preserve duplicates and order. The fingerprint
table defaults to 262,144 entries, with an explicit bounded configuration.
Gathering preflights new entries before partial reads. Capacity exhaustion raises
`FingerprintCapacityExceeded` and retains existing pins.

`frozen_linear` keeps the immutable store/reference, input device/dtype and
compute callback needed for backward. It retains neither a frozen weight tensor
nor an input activation solely for that linear operation. Backward rereads and
verifies the same frozen matrix to compute the input gradient.
`PagedLinear` streams output-row ranges; `PagedExperts` materializes only
actually routed expert slices. Gradients remain connected through frozen
experts and native routing weights.

`load_text_model` constructs the installed `Qwen4ExpForCausalLM` under meta
and maps every expected parameter, buffer and GGUF tensor. Mapping covers
DeltaNet head permutations, SSM logarithms, zero-centred norms, QSA projection
splits, depthwise convolution shapes and exact int64 PLE constants. Native
decoder, attention, router and shared-expert logic remain in use. Small frozen
routers, norms, convolution weights and SSM vectors stay resident; large
projections and embeddings use paging.

The supported graph is text only. Unknown tensor families, unexplained meta
state, incompatible configuration, mutable caches, embedding reversal and full
base `state_dict` export are refused. CPU accepts float32/float64. The explicit
CUDA candidate accepts FP32 only after current supervised admission and native
reference-kernel validation. The CPU calibration and training launchers do not
expose a GPU execution mode.

### Streamed supervised loss

`paged_loss.native_causal_loss` runs the native decoder and computes full-
vocabulary cross-entropy in token and vocabulary tiles over the frozen paged
head. It applies the causal shift, ignores `-100` targets and preserves the
native optional router auxiliary loss. The normalization is over the complete
vocabulary; no sampled-softmax approximation is introduced.

The implementation retains supervised hidden states, target IDs and per-token
normalization statistics. Backward rematerializes the same checked head slices
and propagates the input gradient. It does not retain or return a complete
`[tokens, vocabulary]` training-logits tensor. `TrainingSession.step_row`
selects this boundary for a mapped native graph. Direct calls to the native
causal-LM forward retain that model's normal logits-returning behavior.

`loss_token_rows` defaults to 128; `chunk_rows` bounds vocabulary rows.
Their score tile is limited to 16 MiB, accounting for the compute element size.
Projection scores follow the native loss's FP32 upcast. Per-token normalization
sums use FP64 to preserve small probability masses across a wide vocabulary;
large score tiles remain FP32, and the returned loss is FP32. Qualification uses
FP64 CE on the same FP32 scores, since a native FP32 whole-vocabulary reduction
can itself lose precision. Higher-order derivatives are explicitly refused.

Slice and score-tile bounds do not bound total process RAM. Decoder activations,
attention state, retained hidden states, small resident parameters, optimizer
state, temporary arrays and mapped pages still need an admitted memory/context
budget.

## Permits, pauses and exact continuation

`compute_scope` propagates the existing permit callback through paging,
expert dispatch and loss operations. Backward functions capture and re-enter
that callback rather than assuming Python context variables propagate to
autograd threads. Checks occur before bounded reads, transfers and arithmetic,
including loss tiles and the optimizer boundary.

A cooperative interruption before optimizer mutation restores the previous RNG
state and clears partial gradients without advancing the cursor. Fingerprint
capacity exhaustion follows the same clean-pause path. The worker seals the
completed state; a new supervised attempt opens a fresh store and resumes the
same next row. Existing pins are never silently cleared during a live
forward/backward operation. If a single row cannot fit its admitted pin budget,
restarting with the same bounds cannot make that row fit.

An interruption after optimizer mutation, uncertain optimizer state or failed
publication is a failure requiring verified recovery. It is not reported as a
clean pause and the same session is not retried.

Session identity binds the base, dataset and retained profile, exact adapter
plan, optimizer settings and execution device. The native loader report also
binds canonical input/execution configuration, installed native source hashes,
runtime versions and paging settings. Its backend manifest pins production
ALORA Python modules, GGUF reader/dequantizer/constants and relevant versions.
A different loader identity or changed execution configuration cannot silently
continue an existing checkpoint.

The prepared CPU executable verifies the real worker PID/cgroup, isolated
output, workflow digest, exact launch, GPU-disabled environment and pinned
virtual environment before loading the model. It uses the existing
`TrainingControl` client. Service resume bytes are verified against their
content-addressed path, digest, size and completed-step receipt, imported into
the new custody attempt without pickle, then passed through strict session
restore. The original service checkpoint remains unchanged.

## Calibration publication and resume

`calibrate` requires every category budget before the first forward. It rejects
trainable parameters, already-adapted graphs and aliased expert paths.
`capture.profile_row` hooks actual native dispatch, uses evaluation/no-grad
mode, restores every module's original training flag and removes its hooks on
all exits. A failed in-progress row contributes no observations.

A complete run publishes the exact profile JSON at the requested path. An
interrupted run publishes `<path>.partial.json`. Each gets a separate
`<actual-profile-path>.receipt.json` containing raw-file and canonical-profile
digests, source identities, deterministic selected rows and completed category
counts. Publication uses exclusive sibling staging, fsync and non-replacing
hard-link publication.

Resume pins both original files. It validates the receipt, canonical profile,
base/dataset/loader/topology, selected source records and the completed whole-row
prefix before compute. A fresh output directory receives the continuation.
Original partial-profile and receipt paths are preserved because the receipt
binds those locators. Calibration samples are never reported as optimizer
updates or `TrainingControl.progress`.

## Curriculum, input format and evaluation

The requested training curriculum contains **5,000–10,000 independently sourced
training examples**. The current real-request validators enforce the training
split's total and exact category quotas; validation and final-held-out files
are separate required splits.

| Category | Share | At 5,000 | At 10,000 |
|---|---:|---:|---:|
| Autonomous recovery | 25% | 1,250 | 2,500 |
| UI implementation and interaction | 25% | 1,250 | 2,500 |
| UX | 15% | 750 | 1,500 |
| Proactive reasoning | 15% | 750 | 1,500 |
| Coding | 20% | 1,000 | 2,000 |

Each pretokenized JSONL row contains exactly `id`, `category`, `input_ids`,
`attention_mask` and `labels`. Arrays contain ordinary integers, matching
lengths and explicit padding masks; labels may use `-100`. Padding cannot
supervise loss and at least one shifted next-token target is required.
The sample hash covers compact, key-sorted ASCII JSON of the three token arrays.
File hashes additionally bind IDs and categories. Declare the actual model
vocabulary and admitted context bound.

All splits require examples and labels lineage in the existing custody manifest.
The same independent sourcing rules apply to rewards, corrections and filtering
signals. Software fixtures are explicitly `software_test_only`; they are not a
curriculum or a quality benchmark. This package generates no training examples
or rewards. Validation of declarations does not itself establish source rights.

The bounded 25 November audit did not locate the requested admitted ALORA corpus
or pinned **54-case computer-use evaluation**. The older owner's 2,000-item Atlas
corpus and consumed 105-case development benchmark are different inputs.
They are not silently substituted. A consumed evaluation is not newly held out.

Quality review and promotion remain with the existing training-cycle review
and final gates. Numeric adapter updates do not demonstrate improved coding,
visual quality, recovery, throughput or retained general capability. Preference
optimization, visual RL, dynamic expert reselection and automatic rank allocation
are not implemented training modes here.

## Operator entrypoints

Run `python -B -m alora` from `9to1 Models/Dulche Alpha/Training` using the
independently selected environment.

| Command | Effect |
|---|---|
| `curriculum --examples 5000` | Reports category quotas; generates no examples. |
| `check-dataset` | Verifies pinned local data; real mode also enforces the requested curriculum. |
| `plan` | Writes a fresh expert plan from a retained canonical profile. |
| `calibrate` | Validates the real request and runs a bounded CPU child under the actual SDK lock and memory guard. |
| `prepare-workflow` | Writes a pinned deployment and supervisor manifest without registering or launching it. |
| `run` | Restricted worker entrypoint inside the actual admitted CPU job and copied deployment. |

See [RUNBOOK.md](RUNBOOK.md) for complete arguments, request fields, continuation
and evidence interpretation. The generated regular executable keeps the pinned
virtual-environment interpreter in its shebang; its bootstrap verifies copied
sources and environment before imports.

## Verification contracts and sources

The tests use tiny numeric instances of the actual installed Qwen4Exp classes.
They cover native forward/input/routing gradients, frozen paging, mixed
quantization, PLE behavior, streamed loss, sparse optimizer changes, exact
continuation, provenance tampering, capacity pauses, permit boundaries,
calibration publication and the existing file-based supervisor contract.

Retained real-GGUF proofs use numeric software inputs. Their receipts identify
which tensor-slice, native graph, forward/backward or checkpoint operations
actually ran. Source revision and evidence must travel together. Use the
existing SDK lock, guarded runner and fresh evidence directory for qualification;
CPU tests install `hf_backend.cpu_guard` before Transformers imports and check
that CUDA remains uninitialized.

Implementation references:

- [Calibration and resume](calibration.py), [guarded CPU calibration](calibration_cli.py)
- [Source paging](gguf_store.py), [autograd paging](paging.py), [selected experts](paged_experts.py)
- [Native mapping](gguf_model.py), [streamed causal loss](paged_loss.py), [compute admission](compute.py)
- [Workflow preparation](workflow.py), [CPU runner](runner.py), [worker bridge](worker.py)
- [Session state](session.py), [checkpoint custody](checkpoints.py), [input custody](data.py)
- Existing repository contracts: `docs/dulche/independent-training.md`,
  `docs/dulche/cyclic-resource-windows.md`, `Training/custody/` and
  `eng/dulche/service/training_worker_client.py`.
