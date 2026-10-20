# Bundled Dulche runtime/model-asset packaging and integrity

`bundle/` is a bounded, reproducible packager/validator for a bundled Dulche
runtime and its model assets. It is authored by the isolated Mercury extra15
bundle lane and publishes **new packaging/asset-admission files only**. It does
not modify `broker.py`, `HomeDulcheRuntime*`, `services/managedrouting`,
`StrataNativeWorker`/`NativeStrata/**`, or any engine pin owned by Thunder.

## Purpose

The product goal is a usable bundled local-AI path without requiring an external
provider. Before any bundled runtime/model can be *admitted*, the integrator
must be able to:

- prove exactly which files were supplied and that they are immutable
  (SHA-256, byte size, runtime format, platform/architecture, licence, source,
  canonical install path),
- reject malformed, substituted, truncated, symlink-escaped, or placeholder
  (all-zero hash) inputs,
- emit an honest, versioned package manifest plus deterministic receipts,
- verify an installed package closure before runtime admission, and
- report missing real assets as actionable `degraded`/`BLOCKED`, never as a
  fake "ready" or as "external provider required".

## Honesty rules

- Files are only ever supplied explicitly by the caller. Nothing is downloaded,
  fetched, or network-installed by this code.
- Every admitted file is actually hashed; a declared expected hash/size is
  cross-checked, never trusted in place of hashing.
- The all-zero placeholder SHA-256 (the value in `model-manifest.example.json`)
  is rejected: no invented pins.
- A bundle whose required assets are missing or rejected is `invalid`; a bundle
  with only optional assets missing is `degraded`; only a fully verified set is
  `ready`.
- Test fixtures are generated at test time and are explicitly `TESTONLY`; they
  are not real models or real runtimes, and this repository never claims real
  generation. Missing real assets therefore stay BLOCKED until the integrator
  supplies authentic files.

## Modules

| Module | Responsibility |
| --- | --- |
| `identity.py` | safe regular-file access, SHA-256 hashing, path containment/symlink-escape rejection, atomic durable writes |
| `formats.py` | runtime-format sniffers (GGUF, safetensors, ELF64, opaque) and architecture matching |
| `licence.py` | SPDX-ish licence id, source reference and redistribution-policy validation |
| `spec.py` | the explicit input spec (which assets, engines, platforms, pins) |
| `manifest.py` | the versioned manifest model: render, canonical dump, parse/validate |
| `receipt.py` | deterministic, timestamp-free receipts |
| `packager.py` | orchestrates admission and emits manifest + receipt |
| `verifier.py` | verifies an installed package closure against a manifest |
| `cli.py` | `bundle-ctl pack` / `bundle-ctl verify` |

## Usage

```bash
# Pack: spec + a directory containing the real supplied files.
python3 -m bundle.cli pack --spec my-spec.json --assets-root ./supplied --out ./out

# Verify an installed tree against the emitted manifest before runtime admission.
python3 -m bundle.cli verify --install-root ./installed --manifest ./out/bundle-manifest.json
```

A spec (JSON):

```json
{
  "bundleId": "dulche-bundle",
  "bundleVersion": "0.1.0",
  "host": {"os": "linux", "arch": "x86_64"},
  "assets": [
    {
      "logical": "smollm2-q4",
      "kind": "model.gguf",
      "engine": "llamacpp",
      "platform": {"os": "linux", "arch": "x86_64"},
      "runtimeFormat": "gguf",
      "source": "https://huggingface.co/unsloth/SmolLM2-135M-Instruct-GGUF",
      "license": "Apache-2.0",
      "path": "models/SmolLM2-135M-Instruct-Q4_K_M.gguf",
      "installPath": "assets/models/smollm2-q4.gguf",
      "capabilities": ["chat"],
      "redistribution": "review-required",
      "required": true
    }
  ]
}
```

## State semantics

- `ready` — every required asset present and verified; bundle can be admitted.
- `degraded` — required assets verified but optional assets missing; usable with
  that engine capability missing (actionable, not a provider requirement).
- `invalid` — a required asset is missing/rejected, or a verified closure check
  failed; do not admit.

## Multi-engine

Canonical Dulche is multiengine (`llamacpp` broker plus embedded `strata`/Thunder).
This packager is engine-agnostic and records per-engine state in `engineState`;
it preserves real engine-specific capabilities and does not pretend a missing
real asset is a supported engine.

## Integration (patch-only)

`integration-patch.diff` is an illustrative, NOT-applied consumer sketch.
Integration is **BLOCKED**: the shared Home runtime surface
(`ManagedDulcheRuntimeService.cs` and siblings) and an installer staging
entrypoint are owned by other workers and are not present/owned in this sparse
worktree, so no exact gate is claimed. Root/lane5 must integrate the verifier as
a pre-admission gate on their owned surface. The sketch names the concrete
discovered targets and the missing dependency; it is not applied and adds no
hidden services.
