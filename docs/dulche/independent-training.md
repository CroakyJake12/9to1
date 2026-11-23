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
