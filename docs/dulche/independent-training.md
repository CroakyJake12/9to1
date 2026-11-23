# Independent local training

Dulche supervises an independently authored worker. The worker owns the model-specific training decisions, input, updates and checkpoint format. The user service owns process admission, resource limits, checkpoint custody and monitoring. Closing 9to1 or suspending an inference model does not stop an admitted training job.

## Registration and operation

Run these commands from the installed service directory or `eng/dulche/service` in the checkout. The manifest path and digest come from the independent workflow owner.

```bash
python3 -B training_jobs.py status
python3 -B training_jobs.py register "$WORKFLOW_MANIFEST" --sha256 "$WORKFLOW_SHA256"
python3 -B training_jobs.py launch "$WORKFLOW_ID"
python3 -B training_jobs.py pause "$ATTEMPT_ID"
python3 -B training_jobs.py check "$ATTEMPT_ID"
python3 -B training_jobs.py resume "$ATTEMPT_ID"
```

The broker exposes the same read-only inventory at `GET /v1/training/status`. Attempts, validated checkpoints, measurements and reports live under `$DULCHE_STATE_DIR/training` or the default `~/.local/state/9to1/dulche/training`. A resume operation creates a new attempt and verifies the prior checkpoint digest and immutable workflow identity.

The independent owner's JSON registration supplies these fields.

| Field | Required value or meaning |
|---|---|
| `schema`, `purpose`, `worker_protocol` | `dulche-independent-workflow-v1`, `real_training`, `checkpoint-yield-v1` |
| `id`, `model`, `device` | Stable lowercase workflow identifier, specific base-model identity, and either `cpu` or an approved locally visible physical GPU UUID |
| `executable`, `arguments`, `cwd` | Executable object with absolute `path` and `sha256`, argument array and explicit working directory |
| `inputs`, optional `read_only_roots` | Pinned file arguments, including independent scripts and configuration, and any additional directories the worker needs to read |
| `dataset_manifest`, `dataset_root` | Independently supplied manifest path and SHA-256, plus the root of its verified local files. The existing training custody validator checks split separation, provenance and actual bytes. |
| `limits` | Integer `ram_mib`, `min_free_ram_mib`, `vram_mib`, `checkpoint_reserve_bytes`, `max_checkpoint_bytes`, `wall_seconds`, `progress_timeout_seconds`, `checkpoint_grace_seconds`. GPU workflows also declare `max_gpu_temperature_c`. |

The worker can import `TrainingControl` from `training_worker_client.py` or implement the same file contract. It calls `ready` when its real workflow is prepared. It waits for an unexpired permit before submitting bounded compute operations and publishes monotonically increasing completed steps. On a checkpoint request it fences outstanding accelerator work, publishes a complete checkpoint, marks the terminal step and exits. The supervisor verifies the SHA-256 and preserves a separate content-addressed checkpoint outside the worker's writable directory. The current transport accepts one regular checkpoint file per publication. Library-specific checkpoint directories need their own independent worker packaging.

## Resource and lifecycle controls

The supervisor and worker run in separate, dedicated user-systemd units. The worker stops when its supervisor exits. Landlock confines writes to the attempt output and restricts compute-device access to the selected device. Both units run independently of the GUI. An imported repository cannot select its own executable by presenting a model file.

CPU jobs receive a cgroup quota of 10% of the host's total logical CPU capacity. The supervisor measures actual CPU consumption from the worker cgroup. GPU jobs receive short, expiring compute permits and per-process SM/VRAM measurements. An unsupported per-process activity measurement is labelled with a conservative upper bound from measured device activity. A numerical GPU utilization target can only be validated during a real run. Busy inference, thermal or memory pressure, significant swapping, stale measurements and a sustained compute overrun trigger a cooperative checkpoint and exit. The supervisor stops the owned worker after its grace period if needed. Disk admission retains 50 GiB free plus the declared checkpoint allowance.

After a registered worker confirms a real training start, Dulche installs a one-shot user calendar timer at the measured process start time plus 3,600 seconds. The timer persists across user-manager restarts. The health check records its actual execution time, progress, checkpoint verification, resource samples, worker status, storage and log errors. A missed check runs when its persistent timer is next activated. Failed jobs require an explicit resume from a verified checkpoint.

## Verification on 23 November 2026

The OS transport fixture verified a real supervised process and cgroup, blocked GPU access in CPU mode, and blocked writes to supervisor state. A separate, explicitly named timer fixture exercised the actual user scheduler and health reporter. A synthetic launch epoch moved its deadline about seven seconds into the future. The timer fired 40.8 milliseconds after that deadline, reported `Unknown` for its absent worker, and was removed. These tests performed no model updates.

The 27B base at `~/Qwen3.8-27B` contains all 18 indexed Safetensors shards. The index declares 55,562,855,904 tensor bytes. The local Qwen session returned `UNAVAILABLE` when asked only for a pre-existing, separately authorized workflow reference. The checked registries had no qualifying workflow. No production training job or one-hour production timer was created.

The published Unsloth 27B workflow documents 24 GB for local QLoRA. Its notebook uses a mixed-source dataset whose licence and row-level provenance were not verified for this acceptance. Both local GPUs hold 16 GB and use separate driver environments. The Flash-Next Axolotl recipe separately calls for about 100 GB of free host RAM just for its offloaded table. LlamaFactory's exact-model CUDA guide requires the owner to supply a dataset and qualify the actual runtime. The evidence supports the model's presence and these framework options. A configuration meeting this machine's capacity, independent input provenance and measured compute envelope is still required. [Unsloth 27B training guide](https://unsloth.ai/docs/models/qwen3.8/train) · [Unsloth notebook](https://github.com/unslothai/notebooks/blob/main/nb/Kaggle-Qwen3.8_%2827B%29-Conversational.ipynb) · [Axolotl Flash-Next recipe](https://github.com/axolotl-ai-cloud/axolotl/blob/main/examples/qwen3.8-flash-next/qlora.yaml) · [LlamaFactory 27B workflow](https://github.com/hiyouga/LlamaFactory/blob/main/.agents/skills/llamafactory-qwen38-sft/references/training.md)

```bash
python3 -B -m unittest discover -s eng/dulche/service -p test_training_jobs.py -v
python3 -B eng/dulche/service/test_training_timer_live.py --fixture-state /path/to/isolated/fixture-state
```

## Additional source and runtime checks

| Independently published path | What the publisher demonstrated | Local decision |
|---|---|---|
| Exact 27B ChartQA vision LoRA | 438 training steps on a GB10 with 121 GB unified memory. The reported peak was 60.3 GB. The code carries Apache-2.0 and the Hugging Face ChartQA card declares GPL-3.0. | The measured working set exceeds either discrete 16 GB GPU. The published run does not establish equivalent CPU offload on this workstation. [Training report](https://github.com/ryangu00/training-vl-lora-qwen3.8-27b/tree/1a49bd7ae059eceef55421e75221d8638b2a9ee1) · [Dataset metadata](https://huggingface.co/datasets/HuggingFaceM4/ChartQA) |
| Qwench 27B | Its documented single-GPU QLoRA path uses a 24 GB card. The repository includes sample data and separate CPU merge support. | The CPU merge option applies after training. No measured under-16 GB training configuration was established. [Qwench](https://github.com/raiyanyahya/qwench/tree/7c9e472274a4a26319e79c9839531d5108d16fd6) |
| 27B educational QLoRA preset | MIT project with a checked-in exact-base preset and a deterministic local demo-data generator. The publisher explicitly reports offline validation and leaves actual GPU training acceptance open. | Capacity and independently verified training progress on the selected device remain unknown. The demo records were neither downloaded nor used. [Preset](https://github.com/HamidGadirov/llm-vlm-finetuning-lab/blob/bd0f6716cbe8dcbd2b81969e1416d8ec009db41f/configs/train/qwen3.8-27b-qlora.yaml) · [Model register](https://github.com/HamidGadirov/llm-vlm-finetuning-lab/blob/bd0f6716cbe8dcbd2b81969e1416d8ec009db41f/models.json) |
| Yokota lab full SFT | A one-step acceptance run across 24 GB200 GPUs with 192 GiB each. The run owner supplies the immutable input manifest and licence. | This distributed full-parameter configuration exceeds the local hardware. [Lab report](https://github.com/rioyokotalab/qwen38-full-finetuning/tree/fa20c22e68046bc96138c2de11e5fa4660f32a9c) |
| TTF converted-model recovery | Qwen3.8-27B recovery training reached a reported 19.7 GB peak on a 24 GB GPU. The project uses PolyForm Noncommercial. | The measured peak exceeds 16 GB even before sharing the device with inference. The publisher describes CPU execution for tests. [TTF project](https://github.com/oguzeldereli/ttf-ternary) |
| KTopt output correction | The publisher describes a fine-tuning-free low-rank correction produced by weighted singular-value decomposition. | Loading its existing GGUF adapter provides a serving capability and supplies no evidence of an independent optimizer update. [Model card](https://huggingface.co/wiklif/Qwen3.8-27B-KTopt-GGUF) |

The local Unsloth Studio database contained zero training runs, training metrics, data recipes and data-recipe executions. Its runs and outputs directories were empty. The local Studio runtime reported PyTorch 2.13.0+cu130, Transformers 5.20.1 and PEFT 0.18.1. Its environment did not contain bitsandbytes.

The local 9to1 Hugging Face adapter checks for `model_type == llama` and `LlamaForCausalLM`. The installed full 27B checkpoint declares `qwen3_5` and `Qwen3_5ForConditionalGeneration`. In the locally pinned llama.cpp source, `src/models/qwen35.cpp:391` and `src/models/qwen4exp.cpp:920` build `ggml_ssm_conv`. The differentiated-operator switch in `ggml/src/ggml.c:6751` has no case for either `SSM_CONV` or `GATED_DELTA_NET`; its default at line 7227 aborts unsupported backward operators. The example documents limited FP32 training, and the installed CUDA RPC binary directory contained no `llama-finetune`. These findings rule out using that installation as a verified full-model gradient path for the two GGUF architectures. A different external adapter needs its own proof.

## Adaptive training cycles

`training_cycle.py` keeps private cycle state, a dated event history, mentor-context and evidence snapshots, the accepted checkpoint and the active resource reservation under `$DULCHE_STATE_DIR/training/cycles/<id>`. `training_release.py` freezes the training CLI, supervisor and custody dependencies independently of running inference services. Use the absolute CLI paths from its release receipt.

A cycle plan has schema `dulche-training-cycle-v1` and supplies `id`, `objective`, `mentor`, `workflow`, exact conflicting `models`, a timezone-qualified `deadline`, `target_cycles`, `max_cycles`, `required_checks`, `validation_id` and `heldout_id`. Optional `validation_benchmark`, `final_benchmark` and `gate_executable` references contain an absolute path and SHA-256. Set `heldout_pending: true` when independent final custody is outstanding. This permits training and blocks final certification. `first_window_owner` binds an explicitly coordinated initial broker reservation.

```sh
python3 -B training_cycle.py create plan.json --sha256 "$PLAN_SHA256"
python3 -B training_cycle.py status "$CYCLE_ID"
python3 -B training_cycle.py start "$CYCLE_ID"
python3 -B training_cycle.py pause "$CYCLE_ID"
python3 -B training_cycle.py resume "$CYCLE_ID"
```

Starting installs a per-cycle user service and separate absolute-cutoff timer. The first round calibrates with the first independently confirmed optimizer update. Subsequent rounds use observed update throughput, startup/checkpoint/restore costs, review duration, quality trends and the remaining time. The workload’s registered memory, disk, thermal, compute and inference-priority limits still govern admission. The training supervisor requests a checkpoint before its absolute cutoff and bounds its worker unit’s runtime independently of mentor progress.

An independent gate executable receives two arguments: the stage request path and the output receipt path. Write the receipt atomically. Alternatively an independent operator can use `submit CYCLE_ID receipt.json`. Each receipt echoes the request’s `cycle`, `request_id`, `stage`, `benchmark_id`, `benchmark_sha256` and `checkpoint_sha256` (null for the base). Supply a finite higher-is-better `score`, named boolean `checks`, and an `evidence` artifact reference. Baselines may mark unmeasured checks null. Baselines and reviews supply a pinned `mentor_context` and optionally `training_directives`; the worker may read the latter from `DULCHE_CYCLE_DIRECTIVES`. Its independently authored implementation owns which directives it supports.

A review additionally supplies `decision` (`continue`, `finish` or `rollback`) and `optimizer`. Bind `updates` to the exact supervised step delta, attest the inspected optimizer state with `optimizer_state_sha256`, and provide differing `parameter_before_sha256`/`parameter_after_sha256` or an actual worker-generated `output/optimizer-evidence.jsonl` with finite positive changes, parameter names and every reported step. Only reviewed checkpoints that pass all checks and match or improve the previous validation score are promoted. Restore precedes review and requires observable retirement of this attempt’s worker/supervisor and verified GPU release when applicable. A failed or missing gate cannot certify learning.

`import-baseline` can bind an existing independent development report to an exact pinned validation suite, preserving its original evidence and score. `reserve-final CYCLE_ID custody.json --sha256 DIGEST` seals a separately attested benchmark once; the custody document provides `cycle`, `benchmark_id`, `artifact`, `independent_source` and `previously_unseen:true`. The final receipt must independently attest previously unseen evaluation. Hashes establish bytes and identity; the evaluator remains accountable for behavioral checks and sample-level independence. An older result previously viewed as “final” is development evidence once used for baseline or tuning.

For an already admitted first window, `adopt CYCLE_ID ATTEMPT_ID --window-owner OWNER` joins the exact existing workflow digest, GPU reservation and absolute cutoff without launching another process. Use this before starting the cycle director when a separate authorized watcher already owns launch. A manually started first attempt can specify `training_jobs.py launch WORKFLOW --deadline ISO_TIME`. Resume and pause operate on that cycle’s exact attempt. `retry-gate` retains an invalid receipt and resets only a terminal independent gate dispatch. Jobs remain blocked when trainer resource release, held-out custody or evaluator evidence cannot be proven.
