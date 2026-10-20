# Handoff — Mercury extra15 bundle lane

## Assignment (direct owner, 20 October 2026)

Owner authorized 10 additional Mercury workers (up to 16 total including Thunder),
superseding earlier six/single-worker notes. Spending cap remains **$5** and was
NOT altered. This lane is **lane 15**, worktree
`/workspace/9to1-mercury-extra15-bundle`, source-only base
`ddb2a8b813c8189b31556ef2ee491ac4f6f2b4eb`.

## Deadline

Establish a reproducible, truthful **bundled Dulche runtime/model-asset packaging
and integrity path** — a prerequisite to the owner's default usable local AI
without a provider connection.

## What was delivered (all new files; nothing existing edited)

Location: `9to1 Workspace/Home/Source/Dulche/bundle/` plus a new llamacpp
manifest-validation utility. `git status` shows only these three new paths
(untracked); all prior work is preserved.

New package (`bundle/`):

| File | SHA-256 |
| --- | --- |
| `bundle/__init__.py` | `2fa932c94772e75daeb3c129d896e12e512af1743d777d2c077fbe0003fc0709` |
| `bundle/identity.py` | `297d4aae14266a30da762bcd156cae4a274982aa310f8c8ad744176d41cfada4` |
| `bundle/formats.py` | `86e7d2e8e4405acd7d9ed880d6e9c85382750e112f035569b74970c32f0306f4` |
| `bundle/licence.py` | `8af3e1561978ea09b49c6179c44a83afc2d8f32cd4f1bef95c935a0915309b6d` |
| `bundle/spec.py` | `962243bb4c35a15043ad4e4cb91300e3cc1e3a01bbf1d972e7560f684918e81f` |
| `bundle/manifest.py` | `e396148f28941b4ab15d20e9d8c9d35a572f413a262b3ce112ae8f806e246130` |
| `bundle/receipt.py` | `e445127616fbc851f37a604a139e32c10785d17480cebdcdfb427c5cf1c4cad0` |
| `bundle/packager.py` | `5c2d8168f925d8858c1fe8b9a04d4961deb6985dcbd67da0802d5cc2e9d94567` |
| `bundle/verifier.py` | `216c4f7dd5056947acbf3040b76b477f44ff66f5f973b86da63d3dc0ef96ad2f` |
| `bundle/cli.py` | `9ceea3420adc99d2a4c137baa0fe1ac17e638d17155f6b2200160e272127a804` |
| `bundle/README.md` | `cd6c1c124020f3f1cf0346b422c0bd2ac96a4c366e6cc8b655632631df893065` |
| `bundle/integration-patch.diff` | `e57af0199481017b3a3f14a8f5014918a58bc44ccd9a8e25a2baa076ef3bbac7` |
| `bundle/tests/__init__.py` | `9fe763e9fb9fb204d20bc25ae64d247c029926907cc2cdf0bf1010e5a1e40308` |
| `bundle/tests/helpers.py` | `1c05d306aa2ebe352f1319e2a7527de6723c8989261b0670d0727fba9a6983d0` |
| `bundle/tests/test_packager.py` | `d2b7e5d4386c72d6c2288355e86cb22c99f2e6de7d96bd6609486d03aeda2ca2` |
| `bundle/tests/test_verifier.py` | `0efa353c162deab45d13cb3829208efcfacf7f266908c2ce02a41443a5b164a2` |
| `bundle/tests/test_formats.py` | `8f4f6f4209c7a4e03f6f4525be2dd742148efe4ce1cf2a905e4c9ba0ac92dad0` |
| `bundle/tests/test_path_containment.py` | `e67cc498a801b816885eebdc77e53abb6c4128519a444833f1d1a34311f2d227` |
| `bundle/tests/test_reproducibility.py` | `dc7ac0169b7243ab083a7c5439dd2661092bf6af7f67fef10056d2fd8fc697eb` |
| `bundle/tests/test_manifest.py` | `c67915aa3885e5ff2f38fb28172ee65a2b1d53ded7695764176aab86f35a043d` |

New llamacpp manifest-validation utility (llamacpp area, **new file only**):

| File | SHA-256 |
| --- | --- |
| `llamacpp/manifest_validation.py` | `4a145df04d6d61a99ab60573729b0b61eaf9fe1d41f4650e3d12c993cdf0ae24` |
| `llamacpp/tests/test_manifest_validation.py` | `889bf6a8fe9c174f3e0cf7a3db26c73bef0e19a7b8eed15a7d702fcde45f551a` |

## Feature coverage

- **Explicit-supplied admission**: assets are only admitted from files the
  integrator actually supplies at a root; nothing is downloaded (no network).
- **Immutable identity**: SHA-256 + byte size computed from the real file; any
  declared expected hash/size is cross-checked, never trusted in place of hashing.
- **Runtime format**: GGUF (v2/v3 magic), safetensors header, ELF64 class/endian/
  machine, or explicit opaque; declared-format mismatches are rejected.
- **Platform/architecture**: native binary ELF machine is matched against the
  bundle host architecture; mismatch rejected.
- **Path containment / symlink escape**: absolute, `..`, and symlink-escape
  paths rejected; installed closure rejects any symlink component.
- **Licence/source/redistribution**: SPDX-ish id, non-empty source and
  redistribution policy validated; format compatibility is never treated as
  redistribution permission.
- **Honest versioned manifest + deterministic receipts**: state `ready` /
  `degraded` / `invalid`, identical inputs produce identical manifest+receipt
  bytes; receipts stay deterministic even when an optional audit timestamp is
  added to the manifest. No invented pins; all-zero placeholder hashes rejected.
- **Installed-closure verification**: before runtime admission, re-verifies
  every installed asset (exists, regular file, no symlink, hash/size/format/
  arch) and, strictly, rejects unexpected files in the owned assets subtree.
- **Multi-engine**: engine-agnostic; per-engine state recorded; missing real
  assets are actionable `degraded`/BLOCKED, not "external provider required".

## Test results (all run, all passing)

- `python3 -m unittest discover -s tests -p "test_*.py"` in `bundle/` → **40
  tests OK** (pack, verify, formats, path containment, reproducibility,
  manifest/licence).
- `python3 -m unittest tests.test_manifest_validation` in `llamacpp/` → **4
  tests OK**.
- `python3 -m py_compile` over every new `.py` file → OK.
- CLI smoke (pack → ready; verify staged install → ready; tamper → invalid/
  hash-mismatch) → OK.

All fixtures are generated at test time and are explicitly `TESTONLY`; no real
model/weight generation is claimed.

## Missing real assets → BLOCKED

No authentic model weights or backend binaries are present in this sparse worktree
and none were downloaded (disk/runtime budget). Therefore an actual bundled
Dulche runtime/model is still **BLOCKED** pending the owner/integrator supplying
real, licence-reviewed assets. The tooling is implemented and verified against
authentic-structured fixtures and will admit real files when supplied.

## Integration for root/lane5 (patch-only, NOT applied)

`bundle/integration-patch.diff` is an optional, illustrative consumer sketch
(verifier as a pre-admission gate) referencing the shared Home runtime
(`ManagedDulcheRuntimeService.cs`) and installer staging. It is not applied
because those files are owned by other workers; root/lane5 may integrate it.

## Next executable checks

1. Supply a real, licence-reviewed GGUF (+ optional native backend binary) and
   re-run `bundle-ctl pack` then `bundle-ctl verify` against a staged install.
2. Wire the verifier gate per `integration-patch.diff` (root/lane5) and confirm
   a failed closure keeps the previous known-good bundle usable.
3. Run the full llama.cpp lifecycle/installed-package acceptance when the real
   assets are present (these remain NOT RUN here).
