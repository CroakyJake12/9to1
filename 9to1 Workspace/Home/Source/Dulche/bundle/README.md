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
| `structural_validate.py` | bounded single-blob structural-admission entrypoint (exit 0 = structurally complete; runnable by the root-owned packaged consumer) |

## Structural format conformance (formats.py)

- GGUF tensor data begins right after the tensor-info table, aligned to the
  file's declared `general.alignment` metadata value (uint32, default **32**
  bytes). Only an integer-scalar alignment is supported; non-integer / bool /
  zero / negative alignment declarations fail closed. Tensor offsets/extents are
  validated against the aligned data start.
- Only plain (non-quantized) ggml tensor types with known byte widths are
  admitted; any other ggml type fails closed as `unsupported`.
- Safetensors requires each tensor's declared byte slice to **exactly** equal its
  dtype/shape extent (no truncation or contradictory padding), and the non-empty
  tensor slices must be **non-overlapping** and **completely cover** the data
  buffer (no holes). Empty-shape scalars and zero-size tensors remain valid;
  Python bool offsets are rejected.
- These are bounded structural checks only: no weights are decoded, no model is
  loaded, and passing them is not evidence of usable inference readiness.
- GGUF ggml type tags are verified against upstream ggml.h: scalar widths only
  for F32=0, F16=1, I8=24, I16=25, I32=26, I64=27, F64=28, BF16=30. Quantized
  types are handled separately; only Q6_K (14) is supported with exact block
  parameters (256 elements / 210 bytes) and dimension divisibility enforced. All
  other quantized/unknown types fail closed and are never reinterpreted as plain
  scalars. `general.alignment` must be an exact UINT32 scalar (default 32).
- Safetensors `__metadata__` must be a string→string object; impostors are
  rejected before any structural claim. Malformed/container-typed dtypes raise a
  typed `IdentityError`, never a `TypeError`.

## Structural validator entrypoint (`structural_validate.py`)

A bounded CLI that runs `sniff_format` on one blob: exit 0 = structurally
complete, exit 1 = rejected (unsupported type / malformed bounds / truncation),
exit 2 = usage/file error. It never decodes weights, allocates tensors, loads a
model or executes it. The root-owned packaged consumer may ship this at a
canonical sibling path (e.g. `/usr/lib/haven/inference/bundle/structural_validate.py`)
and run it as a fail-closed structural gate; see `integration-patch.diff`.
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

## Integration (patch-only, pass 6 corrected managed seam)

`integration-patch.diff` is a root-preserving, NOT-applied consumer patch that
replaces the earlier Python3-subprocess gate with a **managed typed verification
seam**:

- Adds `IHomeDulcheModelStructuralGate` + `HomeDulchePackagedModelStructuralGate`
  (`HomeDulcheModelStructuralGate.cs` under `Source/Home/Core`, auto-included by
  the existing `Source\Home\Core\*.cs` compile glob). It performs a bounded,
  in-process GGUF scan — magic, version, alignment (declared UINT32, positive
  power-of-two, bounded), scalar-type widths, tensor-table bounds, per-tensor
  offset alignment, and supported quantized-block row divisibility (Q6_K
  ne[0] % 256 == 0). No external process, no interpreter, no network. It fails
  CLOSED on provable defect/absence/cancellation, and passes through tensor types
  outside the supported subset (never bricked, never claimed complete). It
  exposes no timeout and claims no deadline; it only honours caller cancellation by
  polling the token during the scan.
- Wires the gate into `HomeDulchePackagedModelEvidenceSource.VerifyBlobAsync`:
  a model is eligible only after real size+hash verification AND structural
  admission. Cancellation propagates (`OperationCanceledException`) and fails
  closed.
- Adds focused owning test regressions (malformed blob, missing artifact,
  cancellation, truncated GGUFv3 dataStart rejection vs valid padded 68-byte
  admission, alignment type/power-of-two negatives, misaligned tensor offset,
  zero-dimension, skip-beyond-EOF metadata, rollback/previous-known-good, real
  source identity) in `Home/Tests/HomeDulcheRuntimeServiceTests.cs`. The canonical
  caller test is labelled EVIDENCE-ONLY (it exercises `FindEligibleAsync`, not
  `OverlayChatSessionFactory`/provider/broker/model adapter); real caller/
  inference/asset acceptance stays BLOCKED.

No Python artifact is installed or invoked at native runtime; the Python
`structural_validate.py` remains QA/pack tooling only. The managed gate ships as
compiled assembly code, so there is no unshipped interpreter prerequisite.
Integration remains not-applied by this lane; root owns application + .NET
build/acceptance (NOT RUN here).
