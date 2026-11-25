# ALORA: activation-profiled, selected-expert LoRA

This package implements the first training stage discussed on 25 November 2026:
profile which routed experts the software-development curriculum uses, allocate
LoRA parameters only for selected experts, keep the base/router/shared experts
frozen, and retain actual optimizer and checkpoint evidence.

ALORA here is the project's selected-expert mechanism. It is distinct from PEFT
**activated LoRA (aLoRA)**, which is controlled by an invocation token sequence.

## Implemented flow

1. **Admit inputs.** `TrainingDataset` reuses the existing custody manifest and
   byte validators. It verifies actual sample hashes and separation across train,
   validation and final-held-out files, then exposes only train rows.
2. **Measure dispatch.** `capture.profile_row` hooks the installed
   `Qwen4ExpTextExperts` forward arguments during a real native-model forward.
   Padding and the masked expert sentinel cannot influence selection. A failed
   forward leaves the previous profile unchanged and removes its hooks.
3. **Select experts.** `ActivationProfiler` normalizes routing mass per token,
   sample and curriculum category. Deterministic ranking follows the agreed
   category weights; large samples cannot silently dominate small ones.
   Profiles bind the base-model fingerprint, dataset identity and execution
   purpose and can be retained and restored as checked JSON.
4. **Attach sparse adapters.** `attach_selected_experts` inserts low-rank updates
   separately into gate/up and down projections, preserving the intervening
   activation and the original routing weights. Only the selected expert IDs
   allocate trainable parameters or optimizer slots. Other experts remain
   differentiable with respect to their inputs.
5. **Train and checkpoint.** `TrainingSession.step_row` verifies the selected
   train row again, executes forward/backward/AdamW, measures actual parameter
   changes, and publishes immutable adapter/optimizer/RNG/cursor state with
   bound evidence. Cancellation before the optimizer restores RNG and clears
   gradients. An uncertain optimizer or publication failure stops the session.
6. **Cooperate with the existing supervisor.** `worker.run_training` uses an
   injected Dulche `TrainingControl`. It honours expiring permits, reports
   completed updates only after durable evidence, and publishes a single-file
   checkpoint before normal completion or a cooperative pause.
7. **Prepare merge output.** `SelectedExperts.merged_slices` yields the selected
   merged weight slices for a separate immutable export. It never writes into
   the active base model or changes the serving model.

The initial backend is the installed Transformers eager packed-expert interface:

- `gate_up_proj[expert, 2 * intermediate, hidden]`
- `down_proj[expert, hidden, intermediate]`
- `forward(hidden_states, top_k_index, top_k_weights)`

Unknown expert classes, quantized packed weights, meta tensors, inference
dispatch/offload hooks, aliased expert weights and repeated attachment refuse.
The session supports a single already-materialized device. It does not claim a
qualified low-memory Flash loader or a training-safe CPU/SSD offload engine.

## Curriculum and evaluation

The requested corpus contains **5,000–10,000 independently sourced examples**:

| Category | Share | At 5,000 | At 10,000 |
|---|---:|---:|---:|
| Autonomous recovery | 25% | 1,250 | 2,500 |
| UI implementation and interaction | 25% | 1,250 | 2,500 |
| UX | 15% | 750 | 1,500 |
| Proactive reasoning | 15% | 750 | 1,500 |
| Coding | 20% | 1,000 | 2,000 |

The existing lineage rules apply to examples, labels, rewards, corrections and
filtering signals. Numeric software fixtures in the test modules are
`software_test_only`; they are not a training curriculum or a quality benchmark.
The package generates no training examples or rewards.

Preserve the requested **54-case computer-use evaluation** as independent
evaluation. Its actual pinned artifact was not located in the bounded audit.
The older local training owner directory has a 2,000-row corpus and a 105-case
development benchmark; those are different inputs. Neither is silently
substituted for the requested corpus/evaluation, and an evaluation already used
for tuning cannot be relabelled as previously unseen.

Quality and promotion remain with `eng/dulche/service/training_cycle.py` and its
independent review/final gates. Adapter-update tests do not establish improved
coding, visual quality, recovery, tasks per minute or retained general capability.
Preference optimization, visual RL, dynamic expert reselection and automatic rank
allocation are later stages, not implemented training modes in this package.

## Offline preparation commands

Run from the existing `9to1 Models/Dulche Alpha/Training` directory with the
independently selected Python environment:

```sh
python -B -m alora curriculum --examples 5000

python -B -m alora check-dataset \
  --manifest /approved/dataset_manifest.json \
  --manifest-sha256 EXACT_MANIFEST_FILE_SHA256 \
  --root /approved/dataset \
  --vocab-size EXACT_MODEL_VOCAB_SIZE --max-tokens ADMITTED_CONTEXT_BOUND

python -B -m alora plan \
  --profile /retained/profile.json \
  --profile-sha256 CANONICAL_SHA256_FROM_PROFILE_RECORD \
  --experts-per-layer 8 --rank 8 --alpha 16 \
  --output /fresh/selected-experts.json
```

These commands inspect local inputs or write a new plan. They do not obtain
models, register workflows, acquire GPUs or start a service. Existing plan files
are preserved. The profile digest is the canonical `sha256` field inside the
profile; the dataset digest is the exact manifest file digest.

## Input format

Each JSONL row contains exactly:

```text
id, category, input_ids, attention_mask, labels
```

Arrays contain ordinary integers; labels may use `-100` for ignored targets.
Lengths must match, padding cannot supervise loss, and at least one shifted
next-token target is required. The sample hash is SHA-256 of compact,
key-sorted ASCII JSON containing only the three token arrays. The file hash
binds IDs and categories. Declare the actual model vocabulary/context limits.

Every file in all three splits requires examples and labels lineage in the
existing custody manifest. Validation of declarations does not prove their
honesty or licence rights; independent input admission remains necessary.

## Library integration

The runtime owner supplies the already admitted model, immutable input
fingerprints, device/resource admission, custody attempt and control client.
The package deliberately does not use inference `device_map="auto"` as a
training loader.

```python
from alora.adapters import attach_selected_experts
from alora.capture import profile_row
from alora.profile import ActivationProfiler
from alora.session import TrainingSession
from alora.worker import run_training

profile = ActivationProfiler(topology, base_model_sha256=base_sha256)
for row in calibration_rows:  # Bound train rows from the admitted dataset.
    profile = profile_row(model, dataset, row, profile,
                          before_compute=check_admitted_profile_permit)

selected = profile.select(experts_per_layer=budget)
attach_selected_experts(model, selected, rank=rank, alpha=alpha)
session = TrainingSession(
    model,
    identity={
        "base_model_sha256": base_sha256,
        "dataset_manifest_sha256": dataset.manifest_sha256,
        "profile_sha256": profile.to_record()["sha256"],
    },
    purpose="real_training",
    profile=profile,
    expert_budget_per_layer=budget,
)
# publisher is an ALORA CheckpointPublisher on the admitted custody attempt.
# TrainingControl / CheckpointRequired come from the existing service release.
run_training(session, dataset, publisher, control,
             target_updates=target_updates,
             checkpoint_exception=CheckpointRequired)
```

Retain the calibration profile with `to_record()`. Load it in another process
with `ActivationProfiler.from_record(record, expected_sha256=expected_digest)`.
It preserves purpose and source binding. Reproduce the same selected plan and
base in a fresh session, then call
`session.restore(source_publisher.load(publication))` before handing it to a new
control attempt. Restoration validates exact adapter IDs/shapes, optimizer
settings and slot coverage, progress, and RNG. Source checkpoint custody remains
read-only. The worker cursor is the number of completed rows in the fixed
manifest order; epochs repeat that order, without an implicit shuffle.

## Verification and scope

The numerical tests use tiny random instances of the **actual installed**
Qwen4Exp expert and sparse-MoE classes, not a replacement expert implementation.
They test native forward/input-gradient/routing-gradient equivalence, sparse
optimizer allocation, frozen-weight preservation, gradients through subsequent
frozen blocks, nonzero merge equivalence, exact resumed updates, bad-state
refusal, publication failure, permit interruption and the real file-based
TrainingControl contract. Dataset and profiler tests cover provenance, split
separation, category balance, padding and retained-profile identity.

Use the existing SDK lock and guarded runner, with a fresh evidence directory.
CPU tests require `CUDA_VISIBLE_DEVICES=-1` and the existing
`hf_backend.cpu_guard` before Transformers imports. No CUDA initialization is
permitted in their entry/exit checks. The actual command, counts, source revision
and guard receipts are recorded in the ALORA01 validation handoff.

Validated numerical CPU behavior is separate from GPU/backward support for the
complete Flash network, practical memory capacity, multimodal training, a real
dataset optimizer run, actual computer-use evaluation, model export/quantization
and inference acceptance. This change starts none of those operations.

## Source references

- [Installed interface upstream](https://github.com/huggingface/transformers/blob/main/src/transformers/models/qwen4_exp/modeling_qwen4_exp.py)
- [Official Flash model and configuration](https://huggingface.co/Qwen/Qwen3.8-Flash-Next)
- [PEFT LoRA and activated LoRA documentation](https://huggingface.co/docs/peft/main/package_reference/lora)
- [PyTorch autograd mechanics](https://docs.pytorch.org/docs/stable/notes/autograd.html)
- [Accelerate inference-only offloading scope](https://huggingface.co/docs/accelerate/en/concept_guides/big_model_inference)
- [Axolotl Flash training guide](https://docs.axolotl.ai/docs/models/qwen3.8-flash-next.html)
- Existing repository contracts: `docs/dulche/independent-training.md`,
  `Training/custody/`, `eng/dulche/service/training_worker_client.py`.
