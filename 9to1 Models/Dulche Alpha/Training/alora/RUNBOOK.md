# ALORA operator runbook

This runbook covers the implemented CPU calibration and independently supervised
CPU training paths. It uses the existing SDK lock, memory guard, custody format
and Dulche worker protocol. CUDA remains an unexecuted library candidate;
these production commands have no GPU launch mode.

The bounded 25 November audit did not locate an admitted ALORA 5,000–10,000-row
training corpus or the requested pinned 54-case computer-use evaluation.
The commands below become executable when those independent inputs are
available. Software fixtures and the older Atlas corpus are not substitutes.
No real training result or quality improvement is implied by the examples.

## 1. Inputs, environment and digest meanings

Run preparation commands from the package's parent directory:

```bash
cd "/absolute/checkout/9to1 Models/Dulche Alpha/Training"
ALORA_PYTHON="/home/jacob/.unsloth/native-venv/bin/python"
```

The interpreter shown is the existing local environment. An independent owner
must select and pin the actual environment for a run. Preparation retains its
original venv path, resolved executable digest and `pyvenv.cfg` digest. A
regular generated launcher preserves that venv path in its shebang and verifies
the deployment before importing copied code.

| Value | Exact meaning |
|---|---|
| `--request-sha256` | SHA-256 of the request file's actual bytes. |
| A normal `{"path", "sha256"}` input | Absolute existing file and its raw-byte SHA-256. |
| `dataset.manifest.sha256` | Raw manifest-file SHA; separate from the custody manifest's canonical dataset digest. |
| `profile.sha256` | Raw retained profile-file SHA. |
| `profile_record_sha256` / plan's `--profile-sha256` | Canonical `sha256` field inside `ActivationProfiler.to_record()`. |
| Calibration `receipt_sha256` | Raw SHA of the separate receipt file. |
| A shard's `sha256` and `size_bytes` | Existing external full-file attestation and exact integer size. Source slices are freshly verified when read; whole shards are not freshly rehashed by this loader. |
| Base identity in the profile | `GGUFWeightStore.source_identity`, derived from the admitted store. It is not the hash of one arbitrary shard or the human-readable model name. |

For small request/configuration files, obtain raw-byte pins with the standard
hash utility:

```bash
sha256sum /absolute/existing/request.json
sha256sum /absolute/existing/config.json
```

All request paths are absolute and checked. Each output deployment directory
must be new, and its parent must already exist. Input files and previously
published outputs remain unchanged.

The full real corpus must use `purpose: "real_training"`. Its training split
must contain 5,000–10,000 rows with the required 25/25/15/15/20 category shares.
The custody manifest also includes separate validation and final-held-out
files, actual file/sample hashes and independently admitted examples/labels
lineage. The package does not generate examples, labels, rewards or corrections.

```bash
"$ALORA_PYTHON" -B -m alora curriculum --examples 5000

"$ALORA_PYTHON" -B -m alora check-dataset \
  --manifest /absolute/admitted/dataset_manifest.json \
  --manifest-sha256 REPLACE_WITH_RAW_MANIFEST_SHA256 \
  --root /absolute/admitted/dataset \
  --vocab-size 248320 \
  --max-tokens 1024
```

The vocabulary and context values must agree with the actual pinned model and
admitted bounds. `check-dataset --software-test-only` is available for software
fixtures; the public calibration and training preparation requests require
`real_training`.

## 2. Guarded CPU calibration request

The following is a **schema template**, not an admitted run. Replace example
paths, digest placeholders, shard size and all numeric resource/recipe choices
with the independent owner's actual values. Every real shard belongs in the
`shards` array; the single example entry only shows its shape.

```json
{
  "schema": "alora-calibration-request-v1",
  "purpose": "real_training",
  "id": "alora-calibration-001",
  "model": "Qwen/Qwen3.8-Flash-Next",
  "python": {
    "path": "/home/jacob/.unsloth/native-venv/bin/python",
    "sha256": "REPLACE_WITH_RESOLVED_INTERPRETER_SHA256"
  },
  "dataset": {
    "manifest": {
      "path": "/absolute/admitted/dataset_manifest.json",
      "sha256": "REPLACE_WITH_RAW_MANIFEST_SHA256"
    },
    "root": "/absolute/admitted/dataset",
    "vocab_size": 248320,
    "max_tokens": 1024
  },
  "config": {
    "path": "/absolute/admitted/config.json",
    "sha256": "REPLACE_WITH_RAW_CONFIG_SHA256"
  },
  "shards": [
    {
      "path": "/absolute/admitted/model-shard.gguf",
      "sha256": "REPLACE_WITH_EXTERNAL_SHARD_SHA256",
      "size_bytes": 123456789
    }
  ],
  "settings": {
    "seed": 1234,
    "threads": 2,
    "chunk_rows": 256,
    "max_decoded_bytes": 67108864,
    "max_read_bytes": 67108864,
    "max_fingerprints": 262144,
    "samples_per_category": 4
  },
  "guard": {
    "script": {
      "path": "/home/jacob/Documents/GitHub/9to1/eng/development/guarded_lowmem_build.py",
      "sha256": "2c6998d4beea8b77218556c88bcddc78b1cd71a4058d44f7bc279e4bdfe6dd64"
    },
    "sdk_lock": "/home/jacob/.local/state/9to1-sdk.lock",
    "floor_mib": 8192,
    "timeout_seconds": 3600
  },
  "resume": null
}
```

The guard digest above is the approved existing implementation pinned by
`workflow.APPROVED_MEMORY_GUARD_SHA256`. A changed guard requires a reviewed
source change; supplying a different digest does not admit a replacement guard.
The SDK lock must be the existing current user's
`~/.local/state/9to1-sdk.lock`.

Calibration defaults to four samples per category, accepts 1–64, and requires
every category budget before forward computation. Three per category gives 15;
four gives 20. Selection uses ascending sample ID within each category, then
category round-robin execution. Only verified training rows are eligible.

Run the exact public command:

```bash
"$ALORA_PYTHON" -B -m alora calibrate \
  --request /absolute/requests/calibration-001.json \
  --request-sha256 REPLACE_WITH_RAW_CALIBRATION_REQUEST_SHA256 \
  --output /absolute/evidence/calibration-001
```

This command acquires the existing SDK flock itself and retains it while the
pinned memory guard runs its CPU child. Run it directly: wrapping this public
command in another holder of the same flock causes the intended busy-lock
refusal. The child verifies actual parent process lineage, guard command and
kernel lock ownership; a caller-created environment variable is not an
admission token.

The guard floor is at least 8,192 MiB and timeout is 60–86,400 seconds. The child
uses its explicit CPU thread cap, disables CUDA visibility and hub downloads,
and checks remaining time/free memory cooperatively. Full corpus validation,
model preparation and calibration occur inside the guarded child. Calibration
does not register a training workflow or publish optimizer progress.

### Calibration outputs

| Artifact | Meaning |
|---|---|
| `deployment.json`, `worker`, copied `source/`, `inputs/` | Exact pinned child and inputs. |
| `guard.log`, `guard.log.guard.json` | Existing memory/time guard output and execution receipt. |
| `calibration-invocation.json` | Parent's binding of request, deployment, launcher, guard and SDK lock. |
| `calibration-result.json` | Child completion state, retained profile locators and continuation metadata. |
| `profile.json` + `profile.json.receipt.json` | Complete retained profile and separate receipt. |
| `profile.json.partial.json` + `profile.json.partial.json.receipt.json` | Cooperatively interrupted profile containing only completed whole rows. |
| `calibration-failure.json`, when produced | A failure; not a successful calibration or optimizer update. |

Inspect `phase` and `complete`, then verify the raw file pins and canonical
profile pin. Only `phase: "completed"` with `complete: true` is the completed
calibration result. A guard failure can leave useful evidence without returning
success. A preparation interruption before a model/profile exists reports
`profile_path: null` and `resume: null`; start a fresh invocation when
admitted. Unfinished staging files are not verified continuation receipts.

## 3. Resume a partial calibration

Copy the returned `resume` descriptor verbatim into a new calibration request:

```json
{
  "profile": {
    "path": "/absolute/evidence/calibration-001/profile.json.partial.json",
    "sha256": "REPLACE_WITH_RETAINED_RAW_PROFILE_SHA256"
  },
  "receipt": {
    "path": "/absolute/evidence/calibration-001/profile.json.partial.json.receipt.json",
    "sha256": "REPLACE_WITH_RETAINED_RAW_RECEIPT_SHA256"
  }
}
```

This object replaces the new request's `resume: null`. Keep the original
profile and receipt at their original paths: the immutable receipt binds those
locators. The deployment rechecks their pins and does not relocate or rewrite
them. Hash the new request and use a new output directory:

```bash
"$ALORA_PYTHON" -B -m alora calibrate \
  --request /absolute/requests/calibration-002.json \
  --request-sha256 REPLACE_WITH_NEW_REQUEST_SHA256 \
  --output /absolute/evidence/calibration-002
```

Resume requires the same dataset, base, loader/runtime/source identity,
topology, sample budget and deterministic selected rows. It verifies that
every retained completed row has all expected expert-layer observations and
that counts match the completed prefix. A fresh store starts with fresh runtime
pins while preserving accumulated profile statistics. Completed rows are
skipped, and the interrupted row contributes no earlier partial observation.

If fingerprint capacity is exhausted before any row can complete, the same
bounds will reproduce that refusal. The owner must admit a viable row/read/pin
budget; deleting live pins or rewriting an existing receipt is not recovery.
Changes to the native mapping/backend/configuration may intentionally prevent
continuation; keep the original evidence and create a new calibration when
the admitted execution identity changes.

## 4. Inspect the selected plan

The plan command accepts the **canonical profile digest**, not the raw-file pin:

```bash
"$ALORA_PYTHON" -B -m alora plan \
  --profile /absolute/evidence/calibration-002/profile.json \
  --profile-sha256 REPLACE_WITH_CANONICAL_PROFILE_SHA256 \
  --experts-per-layer 8 --rank 8 --alpha 16 \
  --output /absolute/evidence/selected-experts.json
```

The output path must be fresh. The plan is inspectable metadata; it neither
attaches adapters nor starts training. The workflow preparer rederives selection
from the same retained profile and explicit settings.

## 5. Prepare the CPU training workflow

The request has exactly these top-level fields. As above, this is a schema
template with illustrative numeric settings, not measured throughput or an
approved resource allocation.

```json
{
  "schema": "alora-training-request-v1",
  "purpose": "real_training",
  "id": "alora-training-001",
  "model": "Qwen/Qwen3.8-Flash-Next",
  "python": {
    "path": "/home/jacob/.unsloth/native-venv/bin/python",
    "sha256": "REPLACE_WITH_RESOLVED_INTERPRETER_SHA256"
  },
  "control_client": {
    "path": "/absolute/checkout/eng/dulche/service/training_worker_client.py",
    "sha256": "REPLACE_WITH_ACTUAL_CONTROL_CLIENT_SHA256"
  },
  "dataset": {
    "manifest": {
      "path": "/absolute/admitted/dataset_manifest.json",
      "sha256": "REPLACE_WITH_RAW_MANIFEST_SHA256"
    },
    "root": "/absolute/admitted/dataset",
    "vocab_size": 248320,
    "max_tokens": 1024
  },
  "config": {
    "path": "/absolute/admitted/config.json",
    "sha256": "REPLACE_WITH_RAW_CONFIG_SHA256"
  },
  "profile": {
    "path": "/absolute/evidence/calibration-002/profile.json",
    "sha256": "REPLACE_WITH_RETAINED_RAW_PROFILE_SHA256"
  },
  "profile_record_sha256": "REPLACE_WITH_CANONICAL_PROFILE_SHA256",
  "shards": [
    {
      "path": "/absolute/admitted/model-shard.gguf",
      "sha256": "REPLACE_WITH_EXTERNAL_SHARD_SHA256",
      "size_bytes": 123456789
    }
  ],
  "settings": {
    "target_updates": 100,
    "experts_per_layer": 8,
    "rank": 8,
    "alpha": 16,
    "learning_rate": 0.0001,
    "max_grad_norm": 1,
    "seed": 1234,
    "threads": 2,
    "chunk_rows": 256,
    "max_decoded_bytes": 67108864,
    "max_read_bytes": 67108864,
    "max_fingerprints": 262144,
    "loss_token_rows": 128
  },
  "limits": {
    "ram_mib": 16384,
    "min_free_ram_mib": 8192,
    "checkpoint_reserve_bytes": 1073741824,
    "max_checkpoint_bytes": 268435456,
    "wall_seconds": 3600,
    "progress_timeout_seconds": 300,
    "checkpoint_grace_seconds": 120,
    "startup_timeout_seconds": 600,
    "vram_mib": 0
  }
}
```

The control client must match the actual service release. Preparation copies and
pins it with the execution code. The request supplies the actual source config
and every shard attestation; the worker checks the retained profile's base
identity against the opened store.

Key supported bounds are:

| Field | Bound or meaning |
|---|---|
| `target_updates` | 1–1,000,000; absolute completed-update target across continuation. |
| `experts_per_layer` | 1–65,536; actual selection also respects the observed native topology. |
| `rank` | 1–1,024; attachment also requires that rank fit the actual expert dimensions. |
| `seed` | Integer 0 through 2^63−1. |
| `threads` | 1–256 and no more than the host's logical processors. |
| `chunk_rows` | 1–65,536. |
| `max_read_bytes`, `max_decoded_bytes` | Positive, at most 1 GiB each and within the declared process RAM limit. |
| `max_fingerprints` | 1–1,048,576; 262,144 is the store default. |
| `loss_token_rows` | 1–4,096, default 128; each compute/reduction tile at most 16 MiB, budgeting eight bytes per element. |
| `dataset.max_tokens` | 2–32,768 and no greater than the pinned model's context bound. |
| `alpha`, `learning_rate`, `max_grad_norm` | Finite positive numbers. |
| All listed limits | Explicit positive integers except `vram_mib`, which must be integer zero. Startup and checkpoint grace cannot exceed wall time. |

The streamed loss bounds head score tiles, not decoder/attention activation RAM.
Numeric field validity is separate from the supervisor's current physical
resource admission and measured feasibility.

```bash
"$ALORA_PYTHON" -B -m alora prepare-workflow \
  --request /absolute/requests/training-001.json \
  --request-sha256 REPLACE_WITH_RAW_TRAINING_REQUEST_SHA256 \
  --output /absolute/deployments/alora-training-001
```

Preparation validates the real corpus, canonical retained profile and full
native expert topology. It creates a new pinned deployment, regular executable
`worker`, copied sources/control client, input copies and `workflow.json`.
The returned `workflow` object supplies the path and raw SHA for registration.
The result explicitly reports `registered: false`, `launched: false` and
`fresh_full_file_hash_verified: false`.

The generated manifest uses `dulche-independent-workflow-v1`,
`checkpoint-yield-v1` and `device: "cpu"`. Its exact registered arguments are
`--spec <deployment.json> --spec-sha256 <raw deployment SHA>`.
The `alora run` command is an internal admitted-worker entrypoint; invoke the
generated workflow through the supervisor.

## 6. Register, launch, monitor and resume through Dulche

These command forms are copied from the repository's
`docs/dulche/independent-training.md`. Run them from the installed service
directory or the matching checkout's `eng/dulche/service`. Set the variables
from the actual preparation result and supervisor response.

```bash
python3 -B training_jobs.py status
python3 -B training_jobs.py register "$ALORA_WORKFLOW_MANIFEST" --sha256 "$ALORA_WORKFLOW_SHA256"
python3 -B training_jobs.py launch "$ALORA_WORKFLOW_ID"
python3 -B training_jobs.py pause "$ALORA_ATTEMPT_ID"
python3 -B training_jobs.py check "$ALORA_ATTEMPT_ID"
python3 -B training_jobs.py resume "$ALORA_ATTEMPT_ID"
```

`ALORA_WORKFLOW_MANIFEST` and `ALORA_WORKFLOW_SHA256` are the returned
`workflow.path` and `workflow.sha256`. The ID comes from the request;
the attempt ID comes from the actual supervisor. The read-only broker inventory
is `GET /v1/training/status`.

The existing service owns process isolation, CPU limits, physical admission,
inference priority, permit deadlines and checkpoint grace. It supplies the real
job environment/PID/cgroup. The ALORA runner verifies that context, exact
workflow and launch, disabled GPU visibility and pinned venv before model use.
The worker bridge emits one readiness handshake when genuinely prepared.

A clean pause delivers the last completed update. Resume creates a new attempt,
verifies the immutable workflow and content-addressed checkpoint receipt, and
imports that checkpoint into the new attempt's custody before restoring session
state. The CAS source's path, digest, byte size and step count must agree.
Import uses the existing data-only snapshot decoder; the source is not rewritten.

The cursor equals completed optimizer updates. Continuation starts at
`cursor % training_sample_count` in fixed manifest/row order; epochs repeat
that order without an implicit shuffle. `target_updates` is absolute, not an
additional count. A capacity pause opens a clean store on the next attempt and
retries the same uncompleted row exactly once per attempt.

Keep the prepared deployment immutable. An existing resume binds the original
workflow and checkpoint identity; a changed recipe/source/runtime needs its
own independently prepared and admitted workflow rather than edits to those
files.

## 7. Evidence and failure interpretation

Attempts normally live under
`~/.local/state/9to1/dulche/training/jobs/<attempt>/`, or the configured
`DULCHE_STATE_DIR/training` root. Use the supervisor's actual returned paths.

| Evidence | What it establishes |
|---|---|
| `run-identity.json` | Dataset/profile/base/session/loader identities, corpus counts and any verified service-resume import. |
| `optimizer-evidence.jsonl` | Measured changes to named selected adapters for completed updates. |
| Custody checkpoint and bound evidence | Immutable adapter/Adam/RNG/cursor state and corresponding completion status. |
| Delivered checkpoint + service CAS receipt | The exact bytes and step count accepted for continuation. |
| `runner-result.json` | Explicit completed/paused state, update count, checkpoint locator and store statistics. |
| Failure markers | A failed attempt; they do not establish a clean retry or successful update. |
| Calibration profile/receipt | Actual routed-expert observations and their complete/partial status, with no optimizer progress claim. |

A permit or fingerprint-capacity interruption before optimizer mutation restores
RNG, clears partial gradients and leaves the completed cursor unchanged.
A storage failure or interruption after optimizer mutation cannot be retried in
the same session. Continue only from a verified checkpoint through a new
admitted attempt.

A checkpoint/profile that names a different dataset, base, topology, adapter
plan, optimizer, native configuration or pinned loader/runtime is refused.
Source/receipt tampering is a failure, not a capacity pause.

For the final handoff, retain the exact revision and source hashes, request and
deployment pins, guard/supervisor receipts, profile and receipt pins, actual
test/native execution evidence, update/checkpoint evidence and remaining
defects. Record any missing corpus/evaluation admission explicitly. Keep test
counts and measured timings in that evidence rather than guessing them here.

The 54-case evaluation stays independently held out. Adapter-update and
software-fixture tests do not establish quality, capability retention or
promotion to a serving model. The existing independent training-cycle gates
remain responsible for those decisions.
