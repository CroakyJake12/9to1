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
| `bundle/README.md` | `1c4a3e57c947f7d3d4449f3cc670e092056ae1d6a057fe38ad5296a3fb054097` |
| `bundle/integration-patch.diff` | `b4d5d76726dc969227bd3f2defa5865a735c11626b476957fcf518c4f8568f9c` |
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

---

# Pass 4 (extra15 pass4)

## Independent probe (pass3) reproduced three structural FAILs; all fixed

Probe: `/workspace/shared/9to1-test-evidence/extra15-bundle-pass3-independent-20261020/additional-structural-probe.py`
(three `structurallyComplete:true` FAIL rows). Copied unchanged into my work
area and re-run with a fresh output file; all three now raise `IdentityError`
(PASS), and the two retained closure rows stay PASS.

### Fix 1 — GGUF alignment (default 32, `general.alignment` override)
Cause: `formats.py` used a hardcoded 8-byte alignment for the tensor data section
and discarded all metadata values, so a 65-byte tensor table padded to 72/76
bytes passed despite the GGUF default 32-byte alignment requiring data start 96.
Fix (`bundle/formats.py`):
- Walk the real metadata KV table; decode scalar/string/array values.
- Validate `general.alignment`: integer scalar only, positive; default 32 when
  absent. Non-integer/bool/zero/negative fail closed. No power-of-two/multiple
  constraint invented (GGUF admits any positive alignment); alignment arithmetic
  is exact for any positive integer.
- `data_section_start = ceil_align(reader.pos, alignment)`; tensor offsets/extents
  validated against it.
- New tests: default32 positive (table65+pad31+data4), exact truncated negative
  (pad7+data4, the reproduced FAIL), explicit `general.alignment=64` positive,
  zero/wrong-type alignment negatives, unknown metadata-type negative. ggml type
  widths unchanged/exact (plain types only; quantized tags still fail closed).

### Fix 2 — safetensors exact extent + non-overlapping complete coverage
Cause: `end - start >= extent` and per-tensor checks let two F32[1] tensors both
claim `[0,4]` in a 4-byte buffer, and one F32[1] tensor claim `[0,8]` (extent 4)
pass.
Fix (`bundle/formats.py`):
- Require `end - start == extent` (exact; contradictions/truncation rejected).
- Non-empty slices must be non-overlapping and exactly tile the whole data buffer
  (no holes / trailing uncovered bytes).
- Bool offsets rejected; scalar `[]` and zero-size tensors retained; unknown dtype
  still fail-closed.
- New tests: overlapping-regions negative (reproduced FAIL), extent-mismatch
  negative (reproduced FAIL), valid adjacent two-tensor positive, gap negative,
  trailing-uncovered negative, bool-offset negative, scalar/zero-size retained.

## Pass-4 test results (all run, all passing)
- `bundle/`: `python3 -m unittest discover -s tests -p "test_*.py"` => **71
  tests OK** (was 59; +12 causal in test_formats).
- `llamacpp/`: `python3 -m unittest tests.test_manifest_validation` => 4 OK
  (files unchanged).
- `python3 -m py_compile` on all changed modules => OK.
- Unchanged additional probe copied into my work area: 3/3 FAIL rows now PASS
  (exit 0), receipt saved; reviewer evidence file not overwritten.
- Unchanged prior probe: 5/5 PASS (exit 0), saved.

## Pass-4 changed-file SHA-256
- `bundle/formats.py` `bc6772a32a5f0998422ed13a6c78b558c7e56ca32386ff6cdd2c545c6cac7788`
- `bundle/tests/helpers.py` `f5ee5e727c09c27651291afca326079732971e7d516f7349a0d9954fc1288c85`
- `bundle/tests/test_formats.py` `263c168f2c02155435950740a55930572aaabdd8e3861afc2d7b0ec93451cb0f`
- `bundle/README.md` `1a2a27378d74d1b305d338a9657ddbd0d86a6270f19039042c2731d2a2bd7d33`
- llamacpp manifest-validation files unchanged.

## Integration status
Unchanged: integration remains **BLOCKED**; no model/runtime readiness claimed.
No .NET/app/model/subagent/provider/config/credentials/cap/commit/push/external
actions performed.

---

# Pass 5 (bundle15 type-bounds + actual consumer patch)

Resumed same session after the execution environment reconnected (old process was
terminated/was; preserved commit bf8e903b, custody receipt recorded). The
interrupted run had not applied these edits; this turn completed them.

## Independent probe (bundle15 frozen) reproduced three FAILs; all fixed

Probe evidence: `/workspace/shared/9to1-test-evidence/bundle15-pass4-safe-frozen-20261020/additional-type-metadata-probe.json`
(three FAIL rows). Re-run unchanged with fresh private outputs; all three now
raise `IdentityError` (PASS).

### Fix 1 — GGUF ggml type discriminants + quantized block bounds + alignment type
Cause: `GGUF_PLAIN_TYPE_BYTES` wrongly mapped tags 14–18 to I8/I16/I32/I64/F64.
Official ggml.h (SHA 475beaa7e959be8c383279d9ce9d5191f37426e6b338270cfb12b26a798129d3)
says 14=Q6_K, 15=Q8_K, 16=IQ2_XXS, 17=IQ2_XS, 18=IQ3_XXS; scalar I8/I16/I32/
I64/F64 are 24–28 (BF16=30). Type 14 shape[1] with 1 payload byte was accepted
as complete.
Fix (`bundle/formats.py`):
- Plain scalar widths corrected to 0 F32, 1 F16, 24 I8, 25 I16, 26 I32, 27 I64,
  28 F64, 30 BF16.
- Quantized types handled only via `GGUF_QUANTIZED_TYPES`; currently Q6_K (14)
  with exact block 256 elements / 210 bytes (verified from official structs).
  Element count must be divisible by block elements; otherwise explicitly rejected.
  All other quantized/unknown types stay fail-closed; never reinterpreted as
  plain scalars.
- `general.alignment` must be an exact UINT32 scalar (type 4); any other type tag
  (int64/bool/float/string/array) or non-positive value fails closed. Default 32.
- New tests: Q6_K valid block (shape[256]/210B), truncated 209-byte block,
  invalid dim-1 tiny file, int64-alignment negative, plus prior alignment tests.

### Fix 2 — safetensors `__metadata__` + typed dtype guarding
Cause: `__metadata__` was skipped without validation; malformed dtype/container
types could leak a `TypeError`.
Fix (`bundle/formats.py`):
- `__metadata__` must be a string→string object; array/scalar/nested-object
  impostors rejected before any structural claim.
- dtype must be a plain string before dictionary lookup; unhashable/non-string
  dtype raises a typed `IdentityError`.
- Kept scalar `[]`/zero-size tensors, exact sizes, non-overlap, full coverage,
  bool-offset negatives.
- New tests: nested-metadata impostor, scalar-metadata impostor, valid string
  metadata, unhashable (list/dict) dtype.

### Consumer patch (root-owned; not applied by this lane)
`bundle/integration-patch.diff` is now a real, `git apply --check`-clean unified
diff against current root source in `/workspace/9to1-chat-cui`. It adds a
fail-closed `IHomeDulcheModelStructuralGate` and wires it into
`HomeDulchePackagedModelEvidenceSource.VerifyBlobAsync` so a model is eligible
only after real size+hash verification AND structural admission. It never turns
structural completeness into installed/verified/running/Ready. The deep validator
is `bundle/structural_validate.py` (new entrypoint; validated here); root must
ship it at `/usr/lib/haven/inference/bundle/structural_validate.py`. Until then
the gate fails CLOSED (models stay BLOCKED). This is the exact causal dependency.

## Pass-5 test results (all run, all passing)
- `bundle/`: `python3 -m unittest discover -s tests -p "test_*.py"` => **79
  tests OK** (was 71; +8 causal in test_formats).
- `llamacpp/`: `python3 -m unittest tests.test_manifest_validation` => 4 OK
  (files unchanged).
- `python3 -m py_compile` on all changed modules (incl. structural_validate.py)
  => OK.
- Unchanged additional structural probe (fresh output): 3/3 FAIL rows now PASS
  (exit 0).
- Unchanged prior probe: 5/5 PASS (exit 0).
- type-metadata probe (fresh): 3/3 FAIL rows PASS (exit 0).
- `structural_validate.py` smoke: good Q6_K → exit 0; bad dim-1 → exit 1;
  valid safetensors → exit 0.
- `git apply --check` of `integration-patch.diff` against root source: OK.

## Pass-5 changed-file SHA-256
- `bundle/formats.py` `aa85fa2d31906b671b8fabffd744368727768b12da65b61c8b43584450463231`
- `bundle/tests/test_formats.py` `ae4e00a63660184b36cfe5e80c5f993977e071f2a4a7003712a4ea5392f08e3f`
- `bundle/tests/helpers.py` `f5ee5e727c09c27651291afca326079732971e7d516f7349a0d9954fc1288c85` (unchanged)
- `bundle/structural_validate.py` `1b57db3adc029aa71e752b4661bfc379c345965dfc997dded3e386038479d3d2` (new)
- `bundle/integration-patch.diff` `f1b04b6a7a8a602bd840cf64c0a144152c0b2e0252f790eacb510c53eda82bc4`
- `bundle/README.md` `1a2a27378d74d1b305d338a9657ddbd0d86a6270f19039042c2731d2a2bd7d33` (updated)
- llamacpp manifest-validation files unchanged.

## Remaining unverified real-asset/runtime work (NOT RUN here)
- No real model weights / canonical runtime executables present; bundled runtime
  and generation remain BLOCKED. The structural validator entrypoint ships for
  root to install at the canonical sibling path; the C# gate is a patch
  (application + SDK build/acceptance are root-owned and NOT RUN here).
- No inference/hardware/startup acceptance claimed; no downloads performed.


---

# Pass 6 (same-session managed consumer correction)

Reviewer rejection of the pass-5 `integration-patch.diff` was addressed. The
Python3-subprocess structural gate (which had a compile error `_ = await proc
WaitForExitAsync(token)`, unused `_timeout`, undrained stdout/stderr, no child
termination/join on cancellation, and an unshipped Python3 prerequisite) is
REPLACED by a managed typed verification seam.

## Q6_K row-dimension fix (parser, bundle)
- `bundle/formats.py` now tracks the first dimension (`ne[0]`) and requires it to
  be divisible by the block element count (256) for Q6_K, not just the total
  element count. `[128,2]` (total 256) is now rejected; `[256,1]` passes.
- New tests: `test_gguf_q6k_invalid_row_128_2_rejected`,
  `test_gguf_q6k_valid_row_256_1_passes`.
- Independent reviewer probe `additional-q6-row-probe.py` re-run in a private area:
  invalid `[128,2]` now raises `IdentityError` (PASS), valid `[256,1]` passes;
  exit 0.

## Managed typed consumer seam (root-owned patch; not applied by this lane)
`bundle/integration-patch.diff` is a new, `git apply --check`-clean unified diff
against current root source in `/workspace/9to1-chat-cui`:
- New `HomeDulcheModelStructuralGate.cs` (`Source/Home/Core`, auto-included by the
  existing `Source\Home\Core\*.cs` compile glob): `IHomeDulcheModelStructuralGate`
  + `HomeDulchePackagedModelStructuralGate` performing a bounded in-process GGUF
  scan (magic, version, alignment, tensor-table bounds, Q6_K row divisibility).
  It fails CLOSED on provable defect/absence/timeout/cancellation and passes
  through tensor types outside the supported subset (never bricked, never claimed
  complete). No external process / interpreter / network; no Python prerequisite
  at native eligibility/startup.
- `HomeDulchePackagedModelEvidenceSource.VerifyBlobAsync` now requires BOTH real
  size+hash verification AND structural admission. Cancellation propagates
  (`OperationCanceledException`) and fails closed.
- `Home/Tests/HomeDulcheRuntimeServiceTests.cs` adds focused owning regressions:
  structurally malformed blob (valid hash, bad magic), missing artifact,
  cancellation, canonical caller/no-external-provider, rollback/previous-known-
  good, and real source identity. Existing evidence fixtures now write a genuine
  minimal GGUF so the positive path exercises the gate.

## Correction round (reviewer FAIL findings → fixed)
- `declaredExtents` now uses consistent unsigned extent math (`tensorOffset +
  knownSize`, both `ulong`); the reviewer-flagged `ulong + long` mix is gone.
- `ReadString`'s nullable result is retained (`string? key`) with explicit null
  rejection before use (Nullable=enable/TreatWarningsAsErrors).
- Cancellation test uses `.AsTask()` on the returned `ValueTask<...>` for
  `Assert.ThrowsAnyAsync<OperationCanceledException>`.
- `relative.Replace('\', '/')` char literals corrected to valid C# `'\\'`.
- `dataStart` is now aligned from the reader position AFTER all tensor-info
  records, not the pre-table start. Exact causal case covered by tests: a GGUFv3
  whose tensor-info ends at byte 57 with no aligned payload is REJECTED (aligned
  data start 64 > EOF), while the padded 68-byte valid counterpart (64 + 4-byte
  payload) is admitted.
- `general.alignment` must be a UINT32 scalar with a positive power-of-two bounded
  value; wrong scalar/string type or value 3 is rejected (not silently accepted).
- Per-tensor offsets must be aligned; impossible zero dimensions reject; metadata
  values running beyond EOF reject.
- Cancellation token is polled during metadata/tensor scans and before admission;
  a cancelled gate propagates `OperationCanceledException` and never partially
  admits. No timeout is claimed (the verifier is a bounded in-process scan that
  honours caller cancellation).
- The canonical caller test is labelled EVIDENCE-ONLY: it exercises
  `FindEligibleAsync`, not `OverlayChatSessionFactory`/provider/broker/model
  adapter; real caller/inference/asset acceptance stays BLOCKED.

## Pass-6 test results (all run, all passing)
- `bundle/`: `python3 -m unittest discover -s tests -p "test_*.py"` => **81
  tests OK** (was 79; +2 Q6_K row tests).
- `llamacpp/`: `python3 -m unittest tests.test_manifest_validation` => 4 OK
  (files unchanged).
- `python3 -m py_compile` on all changed modules => OK.
- Reviewer Q6_K row probe: invalid row rejected / valid row passes (exit 0).
- `git apply --check` of `integration-patch.diff` against root source: OK.

## Remaining unverified / NOT RUN
- The C# consumer patch is provided as source; .NET build/compile and native
  acceptance are root-owned and NOT RUN here (no SDK slot allocated). C# compile
  is therefore NOT claimed PASS.
- Real model weights / canonical runtime executables remain absent; bundled
  runtime and generation stay BLOCKED. No inference/hardware/startup acceptance
  claimed; no downloads performed.

# Pass 7 (laptop preview asset profile; source only)

Turn scope: prepare a concrete preview asset profile + reproducible OFFLINE
staging recipe over existing pinned llama.cpp/bundle contracts. Source/tests only;
no downloads, no SDK/build/restore, no runtime/model execution, no commits.

## New (added-only; no frozen source file changed)
- `bundle/preview_profile.py` — `PreviewProfileBuilder` + CLI
  (`python -m bundle.preview_profile request.json [--out profile.json]`).
  Re-hashes supplied files, structurally sniffs them with the SAME bundle tooling
  (`formats.sniff_format`), records real identities, and never claims platform
  compatibility or redistribution without evidence. `claimedReady` is always
  `false`.
- `bundle/tests/test_preview_profile.py` — 4 tests: missing asset → blocked/no
  hash; supplied valid GGUF → admitted with real identity; PENDING target not
  claimed; unknown k-quant ggml type → rejected (fail-closed), not relabelled.
- `bundle/preview-asset-profile.json` / `.md` — prospective profile reflecting
  honest current state (target PENDING; engine/model bytes absent).
- `bundle/offline-staging-recipe.md` — supplied-assets-only staging recipe,
  expected installed-path closure, root build/generation smoke commands (NOT
  RUN), and the missing-prerequisite list.
- `bundle/PREVIEW-NOTICE.md` — licence/redistribution notice (llama.cpp MIT +
  CI SmolLM2 lock is not redistributable and unchanged).

## Honest reconciliation: Q4_K_M vs `formats.py`
- `Q4_K_M` is a model-wide k-quant method, not one ggml type; such a container
  holds several block types (Q2_K/Q3_K/Q4_K/Q5_K/Q6_K, …). `formats.py`
  structurally supports only **Q6_K (tag 14)** among quantized types.
- Authentic, matching pinned upstream ggml.h block-size definitions for the other
  k-quant tags are NOT available in this offline worktree (no network; local
  plain-type tag table is frozen authority — no guessed IDs). Therefore Q4_K_M
  containers stay unknown-format/rejected. This is an intentional fail-closed
  gap; no checker bypass/format relabelling.

## Test results (run in this turn)
- `bundle/`: `python3 -m unittest discover -s tests -p "test_*.py"` => **85
  tests OK** (was 81; +4 preview-profile).
- `python3 -m py_compile` on `preview_profile.py` => OK.
- `preview_profile` CLI smoke against a real built GGUF fixture => exit 0,
  correct real sha256/size + ADMITTED.
- `integration-patch.diff` `git apply --check` against root => still 0
  (untouched this turn).
- HEAD unchanged `ddb2a8b8`; no commits/pushes.

## Remaining unverified / NOT RUN
- Target laptop OS/architecture is PENDING (root answer). No platform support is
  claimed.
- Real engine/model bytes absent => preview staging and generation NOT RUN.
- Q4_K_M structural closure BLOCKED until authentic pinned upstream block
  definitions + real bytes are supplied.
- Streaming/cancellation acceptance NOT RUN.
- Managed C# consumer patch compile remains NOT RUN (no SDK slot; unchanged from
  pass 6).

---

# Pass 8 (real ARM64 assets; corrected staging)

Turn scope: complete the preview profile/staging against **real** hash-verified
ARM64 assets supplied by root (`/workspace/shared/9to1-preview-delivery/
actual-arm64-assets-20261021.json`). Source/tests only; no downloads, SDK/build/
restore, model/native execution, OS changes, commits/pushes.

## Real facts verified in this turn
- Target confirmed: Linux aarch64 / Debian arm64. Runtime `llama.cpp` v0.4.0 /
  `b10809` / commit `5266f24da75dc449bd56cbed7addb9c8e4a6a73e`, ELF machine 183.
- Runtime closure: 16 files (15 ELF64 e_machine 183 + MIT `LICENSE`) — every
  file hash/size matches the pinned `runtime-input-spec.json`; every ELF is
  e_machine 183.
- Model: Qwen3-0.6B-Q6_K.gguf — hash re-verified
  (`1c15a88244c9e852516e5ba0e394fc97f2182f1f2757413fb99fe6a9214c033b`,
  495,107,776 B); structural sniff PASS (GGUF v3, 310 tensors, alignment 32,
  structurallyComplete).

## Changes (bundle/ area only; no frozen source file altered)
- `bundle/preview_profile.py` — opaque (notice) assets now admit on presence +
  evidence (no binary structure to valid), keeping ELF machine gate and
  structural/deployment separation; TESTONLY/wrong-machine/absent-evidence/
  blank-target still rejected. `claimedReady` always false.
- `bundle/tests/test_preview_profile.py` — 8 tests, all passing.
- `bundle/arm64-runtime-bundle-spec.json` — real 16-file runtime closure spec
  (real pins, no placeholders), consumed by `bundle.cli pack/verify`.
- `bundle/offline-staging-recipe.md` — corrected to use `bundle.cli pack/verify`
  + `modelctl` offline staging (no nonexistent `stage-manifest`); canonical
  paths/fields/sockets/providerId; broker/model load then SSE prompt with
  cancellation/owned drain; honest NOT RUN/BLOCKED limits.
- `bundle/preview-asset-profile.json` / `.md` — real-asset profile (17/17
  present admitted, 0 blocked, claimedReady false).
- `bundle/PREVIEW-NOTICE.md` — corrected CI-policy-versus-licence wording,
  llama.cpp MIT + Qwen3 Apache-2.0 notices referencing verified receipts; no
  future timestamps.

## Commands validated (real assets)
- `python3 -m bundle.cli pack --spec bundle/arm64-runtime-bundle-spec.json
  --assets-root <supplied> --out <out>` → rc 0, ready, 16 assets, 0 missing.
- `python3 -m bundle.cli verify --install-root <installed> --manifest
  <out>/bundle-manifest.json` → rc 0, ready, all present, no unexpectedPaths.
- `python3 modelctl.py --model-root … import <q6k> --id qwen3-0.6b-q6-k
  --name "Qwen3-0.6B-Q6_K" --license Apache-2.0 --origin … --capability chat
  --redistribution review-required` → imported; `modelctl.py list --verify` →
  verified, key `llamacpp:qwen3-0.6b-q6-k`.
- `python3 -m bundle.preview_profile b15-arm64-request.json --out
  b15-arm64-profile.json` → rc 0, 17 present, 17 admitted, 0 blocked, claimedReady false.

## Test results
- `bundle/`: `python3 -m unittest discover -s tests -p "test_*.py"` → **89
  tests OK** (81 + 8 preview-profile). `py_compile` on `preview_profile.py` OK.
- `llamacpp/`: `python3 -m unittest tests.test_manifest_validation` → 4 OK.

## Remaining unverified / NOT RUN
- Native ARM execution / startup / capability probes: NOT RUN and BLOCKED (x64
  build host proves no ARM execution; root allocates cross/qemu/native/laptop).
- Distro ABI pending (`GLIBC2.38/GLIBCXX3.4.32/CXXABI1.3.13/OpenSSL3`); may
  require an older-glibc rebuild of the SAME source pin, not an OS upgrade.
- Actual tensor inventory beyond structural sniff: PENDING (filename is not
  proof; structural sniff passes).
- Streaming/cancellation acceptance: NOT RUN (needs real ARM engine/model + the
  canonical broker/provider path with evidence).
- Managed C# consumer patch compile: NOT RUN (no SDK slot; unchanged).
- HEAD unchanged `ddb2a8b8`; no commits/pushes.

---

# Pass 9 (actual Bookworm same-pin rebuild; verifier strict-closure scope)

Turn scope: adapt the preview/spec/recipe to the **real Bookworm same-pin
rebuild** supplied by root (native ARM CI run 39562111673). Source/tests only;
no downloads, SDK/build/restore, model/native execution, OS changes, commits/
pushes, model subagents, or credential/provider/cap/global-config changes.

## Real facts used (verified in this turn)
- Engine `bookworm-llama-server`: sha256 `edcd91e1a50804fbf2f82245534a5bc60161c7d9a5356c498b07f6157d5ecdf5`, 14,234,480 B, ELF64/AArch64 e_machine 183; DT_NEEDED only libm.so.6/libc.so.6; GLIBC floor 2.36; static GCC runtime. Rebuild of SAME pinned llama.cpp source `5266f24…` (b10809 / 0.4.0) on Debian Bookworm arm64 — NOT the official release asset.
- Notices: `llama.cpp-MIT.txt` (MIT, sha256 `94f29bbe…`), `gcc-12-base-copyright.txt` (GPL-3.0-or-later/GCC Runtime Library Exception, sha256 `da8191658b…`).
- Provenance: `BUILD-INFO.json` (`04256fbb…`), `SOURCE-COMMIT` (`48ebfb0a…`), `engine-closure.json` (`2bc79f48…`).
- Model canonical id **`qwen3-06b-q6k`** (not older `qwen3-0.6b-q6-k`), redistribution **allowed** per canonical JSON, Apache-2.0.
- Native raw engine/model prompt/SSE/socket-cancel PASS independently (run 39562111673); application/laptop/canonical-managed-broker acceptance NOT RUN.

## Changes (bundle/ area only; no frozen source file altered)
- `arm64-runtime-bundle-spec.json` — rewritten to the Bookworm same-pin rebuild closure: 6 assets (1 engine ELF + MIT notice + gcc-12-base notice + BUILD-INFO/SOURCE-COMMIT/engine-closure provenance). Real pins, no placeholders, truthful source references. Old 15-ELF official spec preserved as `arm64-runtime-bundle-spec.official-16elf.historical.json` (historical evidence, unchanged).
- `verifier.py` — strict-closure unexpected-scan now scoped to the **owned top-level subtree(s)** containing declared install paths (e.g. `llama.cpp/`), not just `assets/` and not whole `/usr`. Added targeted regression in `tests/test_verifier.py` (in-subtree smuggled file flagged; unrelated top-level dir not scanned).
- `offline-staging-recipe.md`, `preview-asset-profile.{json,md}`, `PREVIEW-NOTICE.md` — updated to Bookworm rebuild identity, canonical model id `qwen3-06b-q6k`, model redistribution allowed, GCC runtime notice, native-PASS-INDEPENDENT/NOT-RUN language, no future timestamps.
- `tests/test_verifier.py` — 1 new regression (strict-closure scope).

## Commands validated (real assets)
- `bundle.cli pack --spec arm64-runtime-bundle-spec.json --assets-root <supplied> --out <out>` → rc 0, ready, 6 assets, 0 missing.
- `bundle.cli verify --install-root <installed> --manifest <out>/bundle-manifest.json` → rc 0, ready, all 6 present, no unexpectedPaths.
- `python3 -m bundle.preview_profile b17-request.json --out b17-profile.json` → rc 0, 7 present, 7 admitted, 0 blocked, claimedReady false (model id `qwen3-06b-q6k`).
- Worker hardlinked immutable model bytes (no 500MB copy) at `/tmp/9to1-bundle17-worker-20261021/model/Qwen3-0.6B-Q6_K.gguf`.

## Test results
- `bundle/`: `python3 -m unittest discover -s tests -p "test_*.py"` → **90 tests OK** (89 + 1 verifier regression). `py_compile` on `verifier.py`/all OK.
- `llamacpp/`: `python3 -m unittest tests.test_manifest_validation` → 4 OK.

## Remaining unverified / NOT RUN
- Application/laptop/canonical managed-broker acceptance: NOT RUN (native raw engine/model PASS is bounded engine/model only).
- Distro ABI: rebuilt engine needs GLIBC >= 2.36; .deb must declare `Depends: libc6 (>= 2.36), python3`; never upgrade owner libc.
- Actual tensor inventory beyond structural sniff: PENDING.
- Broker streaming/cancellation acceptance: NOT RUN (needs canonical broker/provider path with evidence).
- Managed C# consumer patch compile: NOT RUN (no SDK slot; unchanged).
- Root strict GGUF rank-4 guard/fixtures preserved verbatim in root acceptance-context (`root-stricter-formats.py`/`root-stricter-test_formats.py`); not overwritten or weakened by this lane.
- HEAD unchanged `ddb2a8b8`; no commits/pushes.

---

# Pass 10/11 (Bundle18: honest admission + symlink scan + executable data-stage)

Turn scope: repair the four independent Bundle17 acceptance failures without
touching frozen source/Root strict QA/canonical locks. Source/tests/static
checks only; no SDK/app/native/model execution, no downloads, no OS changes, no
commits/pushes, no credential/provider/cap/global-config changes. Python tests
ran only in `/tmp/9to1-bundle18-worker-20261021` (`-B`/`PYTHONDONTWRITEBYTECODE`).

## Failure 1 — `preview_profile.py` deployment admission
- Added `_KNOWN_OS`; target is confirmed only when OS is a real known OS AND
  arch is known (garbage OS no longer confers admission).
- `_evidence_present` now requires a **known SPDX licence** + **redistribution
  == "allowed"**. review-required/unknown/prohibited stay BLOCKED while the
  independent structural identity is retained; blank/unknown licence cannot
  confer deployment admission.
- Negative regressions added in `tests/test_preview_profile.py`: garbage OS,
  prohibited/unknown/review-required redistribution, blank/unknown licence.
  Existing positive test updated to `redistribution="allowed"`.
- Regenerated profile: 7 present → **4 deployment-admitted** (engine, MIT
  notice, gcc-12-base notice, model), **3 BLOCKED** (BUILD-INFO/SOURCE-COMMIT/
  engine-closure provenance: unknown licence + review-required).

## Failure 2 — `verifier.py` symlink scan
- Strict-closure owned-subtree scan now flags **undeclared symlinks**
  (including directories and broken targets) as unexpected; declared assets are
  always regular files and keep their containment/regular-file checks.
- Regression `test_strict_closure_flags_undeclared_symlinks` added
  (`tests/test_verifier.py`); unrelated top-level dirs still not scanned.

## Failure 3 — honest profile timestamp
- The manually-invented `at: 08:05Z` was replaced with the actual recorded
  observation timestamp **`2026-10-21T08:42:43Z`** (this lane's Bundle17
  activation), and the profile now shows the honest deployment-blocked states
  (`claimedReady` false; native CI PASS vs app/laptop NOT RUN clarified).

## Failure 4 — executable data-only staging helper
- New `bundle/data_stage.py` (typed `argparse` CLI `python3 -m bundle.data_stage`):
  consumes local Bookworm payload/evidence root + canonical seed JSON/model/notice
  + canonical modelctl/gguf/model_lease source root + `--stage-root`; validates
  hashes/sizes/ELF 183/source/receipt/model-id/licence; reuses existing
  `pack_bundle` + `verify_installed(strict)`; stages engine at
  `stage/usr/lib/haven/llama.cpp/llama-server` + all 6 manifest files, manifest+
  receipt at `inference/bundle`, python closure at `inference`, seed JSON+weights
  + notices under `usr/share/haven/default-models/`; HARDLINKS the model (same
  filesystem, recorded identity, no 500MB copy); refuses overwrite/symlinks/
  unrelated writes; no maintainer scripts/user-HOME import.
- Unit tests in worker dir `test_data_stage.py` (6): actual-input smoke, wrong
  hash, wrong arch, bad seed id, extra-asset-in-subtree rejection, overwrite
  refusal. All pass.

## Validated
- `bundle/`: `python3 -B -m unittest discover -s tests -p "test_*.py"` →
  **94 tests OK** (preview_profile 11 incl. new negative; verifier 13 incl.
  symlink regression). `py_compile` OK on all changed modules.
- `llamacpp/`: 4/4 OK.
- `bundle.data_stage` smoke: `state ready`, 6 assets staged, model hardlinked,
  strict verify `ready`/no unexpectedPaths, 0 symlinks.
- HEAD unchanged `ddb2a8b8`; no commits/pushes.

## Remaining (NOT RUN / unchanged)
- App/laptop/canonical managed-broker acceptance and final .deb assembly: Root
  owns; native raw engine/model PASS is bounded engine/model only.
- Distro ABI: engine needs GLIBC >= 2.36 → .deb `Depends: libc6 (>= 2.36),
  python3`; never upgrade owner libc.
- Root strict GGUF rank-4 QA and strict C# receipt verifier preserved verbatim.
- Managed C# consumer patch compile: NOT RUN (no SDK slot).

---

# Pass 11/12 (Bundle19: final package-stage repair)

Turn scope: fix the four actual independent staging-helper failures from the
Bundle18 acceptance, plus the recipe continuation bug. Source/tests/static
checks only. Python tests ran only in `/tmp/9to1-bundle19-worker-20261021`
(`-B`/`PYTHONDONTWRITEBYTECODE`). No SDK/app/native/model execution, no
downloads, no OS changes, no commits/pushes.

## Fix 1 — staged engine not executable
- `_stage_asset` now sets staged destination mode explicitly: **runtime.binary
  ELF → 0755**, notices/provenance/Python/seed metadata → **0644**. Source
  inputs are never chmod'ed. Real fresh stage independently stats engine
  `0755`, size `14234480`, hash
  `edcd91e1a50804fbf2f82245534a5bc60161c7d9a5356c498b07f6157d5ecdf5`.

## Fix 2 — unpinned fake seed accepted
- `_validate_seed` now enforces the **EXACT canonical pins**: id
  `qwen3-06b-q6k`, SHA-256 `1c15a882…`, size `495107776`, blob
  `1c/1c15a882….gguf`, license Apache-2.0, redistribution allowed, capabilities
  include `chat`, schemaVersion 1, and the exact immutable resolve URL. The
  supplied model bytes must equal the pinned hash/size AND pass the existing
  canonical `sniff_format` GGUF structural gate (no invented parser). A fake
  17-byte non-GGUF model is rejected **before any stage writes** (no stage dir
  created).

## Fix 3 — symlink stage-root write
- `_validate_stage_root` now rejects, BEFORE any write: a symlink stage root,
  any existing symlink ancestor, a non-directory stage root, and a non-empty
  existing stage root. The `--force` bypass was removed. Focused test asserts
  rejection with the link target left completely untouched.

## Fix 4 — foreign-OS ELF deployment-admitted
- `preview_profile.py` adds `_SUPPORTED_RUNTIME_OS = {"linux"}`; a
  `runtime.binary` ELF is deployment-admitted only for a Linux target. A Linux
  ELF under `windows`/aarch64 is BLOCKED for deployment while its independent
  structural identity is retained; linux/aarch64 stays admitted when policy is
  allowed. Focused regression added.

## Recipe command fix
- `offline-staging-recipe.md` `--python-source-root` line was missing a trailing
  continuation (inline comment), so the command failed with "missing required
  --stage-root". Moved the comment to its own line, added the trailing backslash,
  and updated the refusal wording (no `--force`).

## Validated
- Worker `test_data_stage.py` (5 focused tests) pass: smoke (engine 0755 /
  others 0644 / hardlinked weights / ready), fake-seed-before-writes, symlink
  stage-root target untouched, ancestor symlink, non-empty stage root.
- `preview_profile` suite: **12 tests OK** (incl. new foreign-OS ELF); verifier
  suite 13 OK; `py_compile` OK on `preview_profile.py`/`verifier.py`/
  `data_stage.py`.
- Real `bundle.data_stage` CLI invocation (exact recipe form) → state ready, 6
  assets, engine 0755 + exact hash, 0 symlinks, model hardlinked.
- Input custody: shared engine/model/worker inputs rehashed unchanged (model
  `1c15a882…`, engines `edcd91e1…`); model source inode/mode untouched. All
  mutating fixtures copy to independent inodes before any change.

## NOT RUN / unchanged
- App/laptop/canonical managed-broker acceptance and final .deb assembly: Root
  owns (native raw engine/model PASS is bounded engine/model only). Root strict
  GGUF rank-4 QA and strict C# receipt verifier preserved verbatim. Managed C#
  consumer patch compile NOT run (no SDK slot). HEAD unchanged
  `ddb2a8b8`; no commits/pushes.
