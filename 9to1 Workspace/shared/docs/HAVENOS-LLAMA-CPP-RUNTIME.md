# Local inference provider integration

Model inference is a runtime capability owned by the configured provider and broker. The native desktop obtains availability and model identity through those boundaries. A UI setting, model listing or compiled provider is not an acknowledgement of a generated response.

For a provider handoff, record the actual runtime, model, resource location and observed limits. Local development must respect existing services, device ownership, thermal limits and resident workloads. Keep activation separate from discovery.

## Configuration

Configuration is process-local and can be supplied directly through `LlamaCppRuntimeOptions` or through these environment variables:

- `HAVEN_LLAMA_CPP_ENABLED`
- `HAVEN_LLAMA_CPP_EXECUTABLE`
- `HAVEN_LLAMA_CPP_MODEL`
- `HAVEN_LLAMA_CPP_UNIFIED_CLI`
- `HAVEN_LLAMA_CPP_PORT`
- `HAVEN_LLAMA_CPP_CONTEXT_SIZE`
- `HAVEN_LLAMA_CPP_PARALLEL`
- `HAVEN_LLAMA_CPP_ALWAYS_LOADED`

Malformed numeric environment values remain invalid and do not silently fall back to runnable defaults.

## Always-loaded mode

`AlwaysLoaded=true` does not add a boot service. A non-critical Haven composition root may explicitly register the seam with `AddHavenLlamaCppRuntime` and call `StartIfAlwaysLoadedAsync` after normal application startup. If the executable, model, or configuration is unavailable, the call returns a stopped status rather than making HavenOS startup fail.

The model remains loaded only for the lifetime of the Haven-owned llama.cpp process. `StopAsync`/disposal targets only that owned process handle.

[Local provider](../../Home/Source/Dulche/llamacpp/README.md) · [Runtime packaging](../../../9to1%20OS/packaging/llamacpp/PACKAGING.md)
