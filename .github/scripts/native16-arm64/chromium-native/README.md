# Chromium-native ARM64 pinned runtime pipeline (Lane 8)

This directory implements a reproducible, fully **source/static-only** acquisition,
staging, and verification pipeline for a pinned native **ARM64** Chromium runtime,
built from official signed Debian packages. It never installs system packages, never
executes ANY ARM64 binary (the host is x86_64), and never launches a browser. All
proof here is static ELF + signed-archive metadata; live native/GNOME acceptance
remains **NOTRUN** and owned by the root packager's CI.

## Selected version (exact pin)

| Item | Value |
|------|-------|
| Binary package version | `155.0.8059.79-1~deb13u1` (Debian `trixie-security`) |
| Source package version | `155.0.8059.79-1~deb13u1` (Debian `trixie-security`) |
| Architecture | `arm64` (AArch64) |
| Cloud-validated upstream | `151.0.7922.173-1~deb13u1` (**different**; no 151 proof is reused for 155) |
| Private compat library | `libjpeg62-turbo` `1:2.1.5-4` (Debian trixie main) for SONAME `libjpeg.so.62` |

## Trust and signature verification (fail-closed)

- The official `debian-archive-keyring_2025.1_all.deb` is verified against the pinned
  manifest SHA256 (`9ea7778e…c7fac`) **before** any archive key is used; a custom
  `--keyring` cannot bypass the pinned trust anchor.
- Main archive key pinned `B8E5F13176D2A7A75220028078DBA3BC47EF2265`; security archive
  key pinned `5E04A1E3223A19A20706E20F9904613D4CCE68C6`.
- `gpg` machine-readable **STATUS** records are parsed (`parse_gpg_status`). A signature
  is accepted only when a **VALIDSIG** for the exact pinned fingerprint (signing or
  primary field) is present, no revoked/expired/bad status appears, and the tool exit
  code is taken into account. Human-readable "Good signature" text is never trusted.
  Unknown co-signing `NO_PUBKEY` is permitted only alongside a valid pinned VALIDSIG.

## Package set (verified against signed index AND pinned manifest)

| Debian package | Version | SHA256 (index-verified + pinned) |
|----------------|---------|--------------------------|
| `chromium` | 155.0.8059.79-1~deb13u1 | `bb4334a065801d7482df4783b535ab16654fb71e6eac8a89bd49eb2e7082be27` |
| `chromium-common` | 155.0.8059.79-1~deb13u1 | `2f8d97e652adc2afa8cc96f79141ffdff0ca4f11c3a35671e7b61f60d10eb2b5` |
| `chromium-sandbox` | 155.0.8059.79-1~deb13u1 | `37670b391e0a5cb00cefbb62b92476d65f44887aca14fd8ef5348bdfa14cd128` |
| `libjpeg62-turbo` | 1:2.1.5-4 | `e4989073bb0bac8a6ec043c7adb80e1dfe601d8552233da48bb24ab45d1a1d4a` |

Every binary is independently re-verified by the staging step **against the pinned
manifest** (exact version, arm64 architecture, byte size, SHA256) before any extraction
occurs; an unverified/mismatched input aborts before any runtime output exists.

## Source gate (mandatory, not optional)

`acquire-chromium-debian.py` authenticates the **corresponding signed source inputs**
against the PGP-signed `Sources` index and records a build-graph ledger in
`chromium-acquire-receipt.json`:

- Chromium source (`trixie-security`, exact pinned source version `155.0.8059.79-1~deb13u1`):
  `.dsc`, `.debian.tar.xz`, `.orig-pre-gen.tar.xz`, `.orig.tar.xz` — each size/SHA256
  verified against both the signed Sources index and `source-pins.json`. The Chromium
  download URL is generated from the **signed Sources `Directory`/`Filename`**, never a
  hard-coded duplicate of the archive base.
- Private JPEG source: the `libjpeg-turbo` source stanza for the built `2.1.5-4` binary
  is located in the signed `trixie` Sources index; **all** `Checksums-Sha256` entries are
  iterated (no extension guessing), so the `.dsc` (2508 B), `.orig.tar.gz`
  (2 264 471 B) and `.debian.tar.xz` (108 128 B) are **downloaded, size/SHA256-verified,
  and physically stored** with their actual local paths recorded in the receipt. No
  source input is left as metadata-only.

The **executable source gate** (`validate_source_build_graph`, shared by
`stage-chromium-bundle.py` before any runtime output and by the root packager's
`validate_browser_stage` before merge) requires:

- the **real physical source inputs** (`--source-dir`) — every one of the complete pinned
  Chromium+JPEG input set must exist and match exact bytes + stream-SHA256;
- the **pinned authenticated source manifest** (`--source-pins`); and
- exact source versions and the complete input set — missing/corrupt/extra/replaced or
  wrong-version source entries fail.

There is **no self-asserted bool/nonempty-list acceptance**: the independent counterexample
(`final-pure-source-gate-counterexample.json`), a fabricated `{sources_verified:true,
source_pins:{inputs:[{name:'missing-source.tar.xz',verified:true}]}}` graph with zero
physical files, fails the gate (covered by `test_source_gate_rejects_fabricated_graph`).
`--offline` re-verifies every cached input (never succeeds on a missing/mismatched/corrupt
cache).

## Staged bundle layout and integrity

- The full vendor tree under `usr/lib/chromium` is copied **preserving relative
  hierarchy** (no basename flattening), including the nested `locales/en-US.pak`.
- The private `libjpeg.so.62` is staged under `chromium/lib` as the real file plus a
  correct relative symlink; every symlink is recorded in the integrity manifest and its
  contained target is verified.
- The stage manifest records every file (size/SHA256/mode) and symlink (target). Verify
  cross-checks manifest vs disk, rejects unlisted/orphaned files and symlink escapes.
- Notices are copied into the stage: the exact 155-line Chromium copyright, all
  referenced common-license texts, the private `libjpeg62-turbo` copyright, and the
  required Independent JPEG Group binary documentation acknowledgement ("this software is
  based in part on the work of the Independent JPEG Group").

## Sandbox handling (no false ownership claims)

The vendor `chrome-sandbox` is staged with mode `4755`. The manifest records the
**expected vendor owner as a distinct field (uid/gid 0)** separately from the truthful
**observed uid/gid** of unprivileged staging (here `1000/1000`); it never lies that the
stage owns it as root. Application of root ownership is the root-only packager's step via
`dpkg-deb --build --root-owner-group`. `normalize_permissions` preserves **only** the
exact vendor `chrome-sandbox` (path + payload SHA + mode 4755) and strips any other
SUID/SGID/special bit. No `--no-sandbox` / `--disable-setuid-sandbox` / sysctl /
AppArmor workaround is used, and the live locator verifies the on-disk setuid mode and
the manifest's expected root owner before ever returning the launcher.

## Live locator wiring (C#)

The bundled stage is now **wired** into engine dispatch:

- `BundledChromiumLocator` verifies the manifest (schema + pinned version), the on-disk
  setuid sandbox mode and expected root owner, and the full manifest file set —
  **every** regular payload file (including the main binary) is verified exactly once by
  bytes + SHA256, symlink targets are checked and kept inside the stage, duplicate /
  rooted / `..` / escaped paths are rejected, malformed JSON / missing or mis-typed
  fields (incl. uid/gid, bytes, sha256) and IO errors return false rather than throwing
  into the UI, the manifest file itself is treated as metadata (not payload) in the
  unlisted-file scan, an empty files list fails, and the binary must be an AArch64 ELF
  **and** match its manifest hash after the header — then the bundled launcher is
  returned, fail-closed (null) on any mismatch.
- `ChromiumRuntimeResolver` composes the bundled stage and the installed `ChromiumLocator`
  (bundled wins when verified; else installed fallback) and is used **both** for
  availability (`ChromiumEngineFactory.IsSupported`/`UnsupportedReason`) **and** for
  actual process launch (`ChromiumProfilePool` connection). Installed-default candidates
  and existing test-injection semantics are preserved.

## Static dependency proof (summary, as actually verified)

- `verify-chromium-bundle.py` inspects **every** staged ELF (binary, crashpad, sandbox,
  EGL/GLES/Vulkan, private JPEG) with `file` + `readelf`; any non-zero tool exit or
  truncated/non-ELF is a hard FAIL (never an empty/NEEDED pass).
- Main binary: ELF64 AArch64, interpreter `/lib/ld-linux-aarch64.so.1`.
- The signature of the **Ubuntu 26 (resolute) InRelease is verified** against the pinned
  `ubuntu-archive-keyring.gpg` (VALIDSIG `F6ECB3762474EDA9D21B7022871920D1991BC93C`).
  The `main` and `universe` `binary-arm64/Packages.xz` indexes are downloaded and verified
  against the signed InRelease SHA256/size **before** any provider row is used.
- Every provider is **actually proven**, never table-satisfied: the archive is downloaded
  and verified against the authenticated index (SHA256/size/arm64), extracted, and the
  real ELF whose SONAME matches is inspected (AArch64 class + SONAME + max GLIBC symbol
  version + its own transitive DT_NEEDED closure). Private `libjpeg.so.62` is the staged
  compat library. Any provider that cannot be proven this way is recorded as an exact
  **BLOCKED** entry and the verdict is not PASS.
- Max GLIBC symbol version is parsed from `readelf --version-info` as **numeric tuples**
  (e.g. `GLIBC_2.43` > `GLIBC_2.9`), never lexical string comparison; a failed/empty
  readelf fails closed.
- Current verification result: `PASS` (34 manifest entries; 10 staged ELFs inspected; 47
  root external SONAMEs; 103 provider proofs with 0 BLOCKED; main binary max
  `GLIBC_2.38`, staged-ELF floor `GLIBC_2.39` via `chrome_crashpad_handler`, Ubuntu26
  provider closure resolves up to `GLIBC_2.43`; sandbox mode 4755 preserved). The offline
  re-run against the cached provider archive also `PASS`es. Static verification does
  **not** claim native runtime readiness.

## Files

- **acquire-chromium-debian.py** — fail-closed signed acquisition (keyring bootstrap,
  InRelease STATUS verification, index fetch, binary + source graph verification, atomic
  downloads, `--offline` re-verification).
- **stage-chromium-bundle.py** — pinned-manifest re-verification, vendor-tree + private
  lib + notices staging, source gate, stage manifest.
- **verify-chromium-bundle.py** — every-ELF static inspection + index-authenticated
  provider SONAME resolution.
- **chromium_native_common.py** — shared pure helpers (GPG STATUS, atomic download,
  capacity guard, manifest/symlink checks, permission normalization).
- **browser_stage_guard.py** — packager merge gate (containment, source gate, sandbox
  spec, unlisted-file rejection).
- **test_chromium_native.py** — 38 focused Python tests (incl. the pure source-gate
  counterexample, the JPEG `.orig.tar.gz`-linkage regression, and numeric GLIBC tuple
  comparison).
- **chromium-pinned-manifest.json** — pinned version/package/trust record (incl.
  keyring package SHA).
- **chromium-soname-resolution.json** — every DT_NEEDED SONAME resolution table (root +
  transitive provider closure; 113 sonames, 1 private compat).
- **source-pins.json** — exact source archive URLs + SHA256 (dsc, debian.tar.xz,
  orig-pre-gen, orig, plus the 3 libjpeg-turbo inputs incl. `.orig.tar.gz`) with the
  source build graph authenticated at acquire time.
- **notices/** — the 155-line Chromium copyright, referenced common-license texts, and
  the staged COPY of the libjpeg62-turbo copyright + IJG acknowledgement (copied into the
  runtime stage `NOTICES/`).

## Run steps (as actually implemented)

The commands below reproduce the private evidence acquisition + verification exactly.
They use **task-scoped variables** (not shell `HOME`/global config) and the *actual*
script locations: the pipeline scripts live in `native16-arm64/chromium-native`, while
the root-only `.deb` assembly script lives one directory up (see the ROOT recipe below).

```bash
# Task-scoped variables (explicit, dedicated to this pipeline).
INVESTIGATE=/tmp/9to1-mercury-arm-chromium-bundle-20261021
E=$INVESTIGATE-evidence/debian                  # debian evidence (source/stage/providers)
SUPPLY=/tmp/9to1-mercury-arm-browser-supply-20261021-evidence/apt-browser   # authenticated index evidence (actual retained path)
SCRIPTS=$INVESTIGATE/.github/scripts/native16-arm64
W=$SCRIPTS/chromium-native

# Authenticated Ubuntu 26 (resolute) ARM64 index inputs (public, fetched exactly once).
# Official ports InRelease URL (resolute): https://ports.ubuntu.com/ubuntu-ports/dists/resolute/InRelease
# IR/KR below point at the actual retained private evidence (both must exist).
# When a normal system keyring is provided instead, it is the standard Ubuntu keyring
# /usr/share/keyrings/ubuntu-archive-keyring.gpg.
IR=$SUPPLY/InRelease
KR=$SUPPLY/keyring-extract/usr/share/keyrings/ubuntu-archive-keyring.gpg

# 1. Acquire + authenticate (network); re-run with --offline to re-verify cached evidence.
python3 "$W/acquire-chromium-debian.py" --evidence-dir "$E" \
    --pinned-manifest "$W/chromium-pinned-manifest.json" --source-pins "$W/source-pins.json"

# 2. Stage the private bundle (real physical source gate on the acquire receipt).
python3 "$W/stage-chromium-bundle.py" --debs-dir "$E/chromium-debs" \
    --output-stage "$E/stage-v3" --pinned-manifest "$W/chromium-pinned-manifest.json" \
    --acquire-receipt "$E/chromium-acquire-receipt.json" --notices "$W/notices" \
    --source-dir "$E/source" --source-pins "$W/source-pins.json"

# 3. Static verification against the signed Ubuntu 26 (resolute) ARM64 index
#    (main + universe), downloading/verifying each provider archive. The proof JSON is
#    written to $E/browser-proof.json (the ROOT recipe below consumes exactly this file).
python3 "$W/verify-chromium-bundle.py" --stage "$E/stage-v3/opt/9to1-preview/browser" \
    --resolution "$W/chromium-soname-resolution.json" \
    --ubuntu-inrelease "$IR" --ubuntu-keyring "$KR" \
    --provider-cache "$E/providers" > "$E/browser-proof.json"
# Re-run with --offline to re-verify the cached provider archive.

# 4. Focused tests.
python3 -m unittest "$W/test_chromium_native.py"
```

## Remaining blockers (owned by root/other workers; recorded, not hidden)

- **Native ARM/GNOME runtime acceptance: NOTRUN** and owned by the root packager's
  ARMCI. No READY claim is made from manifest/metadata presence.
- **C# tests are NOTRUN by design** (`BundledChromiumLocatorTruthfulnessTests.cs`,
  `ChromiumLocatorTruthfulnessTests.cs`) — they are compile-only correctness/supply tests
  for the native/independent acceptance step. The bundled locator **is** wired into
  `ChromiumEngineFactory`/`ChromiumProfilePool` via `ChromiumRuntimeResolver`.
- **Root-only .deb assembly** (`package-arm64-deb.ROOT-ONLY.py`, located in the
  **parent** `native16-arm64/` directory — *not* in `chromium-native/`) is edited and
  is `py_compile`-clean but not executed here (root requirement). When `--browser-stage`
  is supplied, **all** of the flags below are mandatory; the recipe fails fast if any is
  omitted (no warning+proceed / Layer-1-only path). The stage path must be the **actual
  browser root** (`$E/stage-v3/opt/9to1-preview/browser`), not the `stage-v3` parent,
  matching `validate_browser_stage`'s expectation:

  ```sh
  # Genuine, required user inputs (root-owned artifacts; not invented finished paths).
  PUBLISH_DIR=...            # real publish/ output dir for the .deb
  OUTPUT_DIRECTORY=...       # new, empty output DIRECTORY (must not exist) that the
                             # packager creates and fills with the staged + .deb outputs
  # Reviewed app preview Debian package version is a REQUIRED root input (NOT the
  # Chromium engine pin, which is 155.0.8059.79-1~deb13u1 and is tracked separately).
  : "${PREVIEW_VERSION:?Set reviewed app preview Debian package version}"
  MAINTAINER=...             # reviewed Debian maintainer "Name <email>"
  BUILD_PROVENANCE=...       # real build-provenance JSON (source_head/rid/self_contained…)
  PKG_README=...             # genuine package README
  PKG_NOTICES=...            # genuine third-party notices dir
  PKG_SOURCE_LICENCE=...     # genuine source licence text
  python3 "$SCRIPTS/package-arm64-deb.ROOT-ONLY.py" \
      --publish "$PUBLISH_DIR" --output "$OUTPUT_DIRECTORY" \
      --version "$PREVIEW_VERSION" --maintainer "$MAINTAINER" \
      --build-provenance "$BUILD_PROVENANCE" \
      --readme "$PKG_README" --notices "$PKG_NOTICES" \
      --source-licence "$PKG_SOURCE_LICENCE" \
      --source-dir "$E/source" --source-pins "$W/source-pins.json" \
      --browser-stage "$E/stage-v3/opt/9to1-preview/browser" \
      --browser-proof "$E/browser-proof.json" \
      --ubuntu-inrelease "$IR" --ubuntu-keyring "$KR" \
      --provider-cache "$E/providers"
  ```

  The browser branch requires: `--source-dir` + `--source-pins` (real physical source
  gate), `--browser-proof` (the static ABI proof artifact saved by step 3 above), and the
  **authenticated** proof inputs `--ubuntu-inrelease` + `--ubuntu-keyring` +
  `--provider-cache` — all three are required so `validate_browser_proof` can run its
  complete Layer 2 authenticated cross-check (no complete PASS without them). It uses
  `validate_browser_stage(stage, source_dir, source_pins)` so the real physical source
  gate runs before merge, plus `normalize_permissions`; includes
  `libasound2t64 | libasound2` in Depends and a `libc6 (>= 2.39)` floor when the browser
  stage is merged, since the staged closure requires `GLIBC_2.39`; and records a
  `bundled_browser` identity — version, sandbox payload sha256, notices sha256, bundled
  status — in the package receipt. Final provider/runtime .deb validation belongs to root
  ARMCI.
