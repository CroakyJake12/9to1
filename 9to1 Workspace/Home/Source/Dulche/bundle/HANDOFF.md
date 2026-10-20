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

---

# Pass 2 (frozen 5c7bcc712332771830f1cea1435a95392e9fcd6e)

## Independent acceptance reproduced two FAILs; both fixed

Probe: `/workspace/shared/9to1-test-evidence/extra15-bundle-independent-20261020/admission-probe.json`
(40 bundle + 4 llamacpp tests still PASS independent; two additional probes FAILed).

### Fix 1 — required missing asset became READY at installed verification

Cause: `verify_installed` parsed the manifest (`allow_invalid=True`) but derived
success only from statuses of assets that happened to be admitted, ignoring
`manifest.state` / `missingAssets` / required-engine closure.

Fix (`bundle/verifier.py`):
- `_validate_missing_records` validates every `missingAssets` record (logical/
  kind/engine/required-bool/reason); malformed records => hard invalid.
- `_recompute_state` derives readiness from the complete required closure and
  never trusts a rewritten `state` field:
  - any required missingAssets => invalid,
  - any installed-asset status != present => invalid,
  - strict-closure unexpected file => invalid,
  - optional-only missing when all present verified => degraded,
  - otherwise ready (provided at least one admitted asset exists).
- New tests: `test_required_missing_asset_pack_install_verify_not_ready`
  (exact probe reproduction), `test_optional_missing_asset_verifies_degraded`,
  `test_malformed_missing_record_verifies_invalid`,
  `test_contradictory_advertised_ready_with_required_missing_is_invalid`.

### Fix 2 — truncated headers production-admitted

Cause: `sniff_gguf` read only 8 bytes; `sniff_elf` read only 20 bytes;
`safetensors` checked only header keys without confirming declared data offsets
fit the real file.

Fix (`bundle/formats.py`) — bounded structural validation:
- GGUF: read the full 24-byte primary header and walk the declared metadata KV
  table and tensor-info table from the actual bytes; any read past EOF fails;
  each tensor's declared data offset must lie within the file.
- ELF: require the full 64-byte ELF64 header, validate magic/class/data/version
  and `e_ehsize == 64`.
- safetensors: validate `data_offsets` are in-bounds against the real file size
  and that at least one tensor exists.
- Format sniffers now return `structurallyComplete: true`; `opaque` explicitly
  returns `structurallyComplete: false` (not evidence of usability).
- New tests: truncated GGUF / truncated ELF / safetensors out-of-bounds /
  zero-tensor rejection, plus pack-level truncated-GGUF-not-admitted and
  structurally-valid-positive tests.

Structural integrity is kept distinct from usable inference readiness: no model
is loaded, no weight contents are decoded, and real licence-reviewed backend/
weights remain missing => BLOCKED.

## Pass-2 test results (all run, all passing)
- `bundle/`: `python3 -m unittest discover -s tests -p "test_*.py"` => **50
  tests OK** (was 40; +10 causal).
- `llamacpp/`: `python3 -m unittest tests.test_manifest_validation` => 4 OK.
- `python3 -m py_compile` on all changed modules => OK.
- CLI smoke: pack with required-missing => `invalid`; install-only-present then
  verify => `invalid` (NOT ready); truncated GGUF pack => `invalid` with
  structured `runtime-format` rejection.

## Pass-2 changed-file SHA-256
- `bundle/formats.py` `8f7660250444bf2c6c38d693e68a59486a8caf7464f1738b6b8004cbad11927d`
- `bundle/verifier.py` `2f80ed7277a4ec6efe69edf9477fe01f14c7235d659753daa7ff160eb79b6e70`
- `bundle/tests/helpers.py` `075a3dc0c240bfb881c5bc727588c0750753f00cbbaa34a0e4073ab842a61bf1`
- `bundle/tests/test_formats.py` `8d38d3a9443da48b673415e8fe79056c110f6c2bf3b3501f4f24216897f491ef`
- `bundle/tests/test_packager.py` `1ec846e8b9b160fd313fd1d53a7e6f35da182824258ec9b178ec91af8f6f5627`
- `bundle/tests/test_verifier.py` `109ce1250c40ed93c49b6d9b145d40aa2b12cda319ec25e1e5ff945b7ff074de`
- `bundle/README.md` `65f39444d259b18f67a87343c7958ee0da379a7d6861f41972ad655d5275f194`
- `bundle/integration-patch.diff` `79a880c15385c249d39e3fc9fc0cc3620eaa7f0156d1309aee5428cd5a988b01`

## Integration status
`bundle/integration-patch.diff` is a NOT-applied sketch only. Integration is
**BLOCKED**: the shared Home runtime surface and installer staging entrypoint
are owned by other workers and are not present/owned in this sparse worktree. No
exact startup/installer gate is claimed and no hidden process/service launches
were added.

---

# Pass 3 (frozen 1f6517357d14c2efad2431b267a5a08f36cfbb2c)

## Independent probe (pass2) reproduced three malformed-structure FAILs; all fixed

Probe: `/workspace/shared/9to1-test-evidence/extra15-bundle-pass2-independent-20261020/admission-probe.json`.
The 50/50 bundle + 4/4 llamacpp suites still PASS independent; three additional
malformed-structure probes FAILed (all returned `structurallyComplete:true`).
The probe was copied unchanged into my work area and re-run: all three FAIL rows
now produce `IdentityError` (PASS), and the two retained required-missing /
contradictory-ready rows remain PASS.

### Fix A — GGUF tensor dimension width (uint64) and data-region bounds
Cause: `formats.py` skipped `4*n_dimensions`, but GGUF v2/v3 tensor dimensions
are uint64 (8 bytes each). This misaligned the type/offset reads, so a 57-byte
rank-1 dim=1 F32 offset=4GiB file read offset 0 (misaligned) and was marked
complete.

Fix:
- Walk each dimension via `reader.u64()` (uint64). Element count accumulates over
  the real dims; rank is capped at 64.
- Only plain (non-quantized) ggml tensor types with known byte widths are
  supported; any other ggml type fails closed with a precise `unsupported`
  diagnostic rather than claiming completeness.
- The tensor data section start is computed as the 8-byte-aligned position after
  the tensor-info table; each tensor's offset is relative to that aligned data
  region, and `data_section_start + offset + data_size <= file_size` must hold.
- Reader was refactored to bounded seek/read (never `read()` the whole file)
  with file-size arithmetic; all counts/lengths/offsets are bounds-checked
  before any loop/allocation.
- New fixtures/tests: valid one-tensor GGUF with real uint64 dimensions +
  payload (positive); high-offset (probe) and unknown-ggml-type negatives.

### Fix B — ELF64 declared program/section table bounds
Cause: only `e_ehsize` was validated, so a 64-byte header declaring a 56-byte
program header at offset 64 (with no table present) passed.

Fix:
- Validate `e_phoff + e_phentsize*e_phnum <= file_size` (when phnum>0) and
  `e_shoff + e_shentsize*e_shnum <= file_size` (when shnum>0).
- Require entry sizes >= 56 (Phdr) / >= 64 (Shdr) when a table is present.
- Reject extended-count forms (PN_XNUM/SHN_XNUM) as unsupported.
- New tests: missing-table negative (probe) and valid bounded non-empty program
  table positive.

### Fix C — safetensors dtype/shape extent semantics
Cause: only header keys were checked, so F32 shape[1024] with offsets [0,1] and
a 1-byte payload passed.

Fix:
- Map supported dtypes to byte widths; unknown dtype fails closed (no
  `structurallyComplete:true`).
- `element_count = product(shape)` with integer semantics; empty shape `[]` is a
  legitimate scalar (extent = dtype bytes), `[0]` is a legitimate empty tensor
  (extent 0) — these are NOT rejected.
- Require `end - start >= extent` (truncation/contradiction rejected; padding
  permitted) and `payload_begin + end <= file_size`.
- New tests: truncated-extent negative (probe), valid scalar empty-shape
  positive, zero-size tensor positive, unknown-dtype negative.

Structural integrity stays distinct from usable inference readiness: no model is
loaded, no weight contents decoded, no execution. Real licence-reviewed backend/
weights remain missing => BLOCKED.

## Pass-3 test results (all run, all passing)
- `bundle/`: `python3 -m unittest discover -s tests -p "test_*.py"` => **59
  tests OK** (was 50; +9 causal in test_formats).
- `llamacpp/`: `python3 -m unittest tests.test_manifest_validation` => 4 OK.
- `python3 -m py_compile` on all changed modules => OK.
- Unchanged independent probe copied into my work area
  (`/tmp/opencode/pass3-probe.py`): all 5 rows PASS (3 malformed rejects + 2
  closure invalid), log saved; reviewer evidence file not overwritten.

## Pass-3 changed-file SHA-256
- `bundle/formats.py` `bba1106e36c0228cca00535d2fc7ca936ca372df81aff7f24ae33940f3fa0148`
- `bundle/tests/helpers.py` `e9db2bafe706b2fbf34b1c2845d17a109da803530d8db2475209db30d369a4fd`
- `bundle/tests/test_formats.py` `e85a82053ddee8ec6a4699ba1c309e7b39e0adba4b734b3f76079504dc93cfc9`
- All prior pass1/pass2 files unchanged (hashes as recorded in earlier handoff
  sections).

## Integration status
Unchanged: integration remains **BLOCKED**; the sketch diff is not applied and
no model/runtime readiness is claimed. No .NET/app/model/subagent/provider/config/
credentials/cap/commit/push/external actions performed.
