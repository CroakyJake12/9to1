# Hardware, backend and benchmark evidence

This slice adds **read-only planning and evidence tooling**. It does not inspect the approved VM automatically, install a driver/toolkit, enable a GPU backend, download a model, or claim model runtime performance.

## Approved VM

The preserved VM identity remains `vm/havenos-dev.json`: UUID `1c55da8e-b74d-43ae-894b-521f7a64c11e`, approved snapshot UUID `f25b7456-c562-4ba4-9798-5814ee17fe25`.

No hardware probe has yet been run inside that VM. Therefore effective RAM, vCPU features, DRM/GPU exposure and accelerator toolchains are **unknown** for runtime-evidence purposes. CPU remains the only safe first target because it is the only backend already built by Haven; even CPU is not yet model-runtime-proven in the VM.

## Backend evidence matrix

`backend-matrix.json` records each backend separately. Evidence is monotonic:

`inspected -> built -> tested -> runtimeProven -> benchmarked`

`backend_matrix.py` rejects impossible promotions such as `runtimeProven=true` while `tested=false`. Hardware detection only returns **candidates**; it never changes evidence.

Current candidates recorded from llama.cpp v0.4.0 are CPU, CUDA, HIP, Vulkan, SYCL, OpenCL and OpenVINO. RPC stays disabled in the Haven baseline because it expands the network/trust boundary and is not required for local inference.

## Read-only hardware probe

Run later, inside the exact approved VM or target native host:

```sh
python3 hardware_probe.py --output hardware.json
```

The probe reads procfs/sysfs, DMI metadata and device-node permissions. It checks whether common accelerator tools are present with `PATH` lookup but **does not execute them**. It does not install packages, load modules or alter the machine.

Use the probe with the matrix:

```sh
python3 backend_matrix.py backend-matrix.json --probe hardware.json
```

The result says only which backends have matching observed hardware. A detected NVIDIA GPU, for example, makes CUDA a candidate; it does not make CUDA built/tested/runtime-proven.

## RAM/VRAM budget planning

`budget.py` deliberately requires explicit assumptions. For a standard transformer, the mathematical K/V payload estimate is:

`layers * context * kv_heads * head_dim * (K scalar bytes + V scalar bytes)`

The tool supports f32/f16/bf16 scalar KV estimates. Quantized KV cache formats are intentionally not approximated as one-byte/fractional scalars because GGML block metadata would make that misleading.

The resulting RAM/VRAM values are **planning budgets**, not observed resident memory. The default runtime-overhead margin is 15% and the additional safety margin is 10%; both are visible parameters, not claims about llama.cpp internals.

Example only:

```sh
python3 budget.py --model-file /path/model.gguf \
  --layers 32 --context 8192 --kv-heads 8 --head-dim 128 \
  --k-type f16 --v-type f16 --gpu-offload-fraction 0 --kv-location cpu
```

## Benchmark harness

`benchmark.py` can later run against an **already-installed and approved** model via the Haven Unix socket. It records:

- model load latency;
- time to first streamed content;
- total generation time;
- completion token count and tokens/sec only when the server returns usage;
- optional aggregate RSS sampling for a supplied broker PID and its descendants;
- a SHA-256 fingerprint and byte length of the prompt, never the prompt text.

It does not download anything. Benchmark results apply only to the exact model, quantization, backend, build, context, hardware and settings used in that run.

For service-process memory sampling, pass the broker service `MainPID` obtained separately from the target system. `--unload-after` is explicit; otherwise the model remains loaded after the run.

## Next runtime gate

A future approved-VM run should collect, in order:

1. `hardware_probe.py` output;
2. backend candidate evaluation;
3. an explicit memory budget for the chosen approved GGUF;
4. service/model-load smoke evidence;
5. benchmark output including TTFT, token throughput and RSS;
6. failure cases: invalid GGUF, insufficient memory, worker crash, cancellation and restart.

Until steps 4–5 happen on the preserved VM, the project must continue to say **not runtime-proven / not benchmarked**.
