# Boards development test build

The owner requests a test version after **all** Boards UI changes from the active
thread are applied. Full independent acceptance remains paused. A development
package must say `TEST_BUILD_NOT_ACCEPTED`, preserve known limitations, and keep
source, compilation, static package checks, native use and delivery separate.
Neither this document nor a generated manifest declares the app100%implemented.

The existing production receiver, `eng/release-delivery/installer_delivery.py`,
still requires an independently READY release. Its checks are unchanged. Do not
feed a development-test manifest to it, forge READY, or remove the ARM workflow's
private-custody checks. The source repository is public; the existing same-repo
workflow therefore deliberately refuses assembly/publication. This change adds
no public artifact route and does not configure private CI credentials.

## Source and basic checks

Root first integrates all applicable Boards UI changes and retains an exact
committed source review. Refresh native/inference source pins only after the final
composition; old manifests or test results cannot identify a newer package.
Generate native metadata through the existing committed-source helper, then review
and compose it through Root:

```sh
python3 -B eng/development/native_source_identity.py \
  --repo /actual/existing/repository --revision EXACT_FINAL_COMMIT \
  --out /fresh/private/native-identity.json
```

The final scope review supplied to the test manifest has `source_revision`,
`status: "ALL_REQUESTED_BOARDS_UI_APPLIED"`, a nonempty `requirements` list covering
all active-thread changes, and the actual `reviewer`. This is Root's explicit
source assertion, not a machine-created claim of completeness. Preserve the
review's underlying evidence. The basic smoke input is the existing owning runner
receipt: same `source_revision`, actual `command`, `result: "PASS"`, `exit_code:0`,
and nonzero `tests` totals with all passed and none failed/skipped. This can be a
focused managed check; it does not establish an ARM/GNOME application journey.

## Existing ARM producer and native Rnote stage

Use the existing native ARM producer and coordinator admission, resource,
source/custody and process/time guards. The actual build host must be
`aarch64`/Debian`arm64`; .NET10.0.301 and at least the existing6GiB free-output
preflight remain required. Allow additional space for the Rust target and retained
vendored source. Normal restore/build is required; no new binary/cache reuse is
authorised. `ubuntu-24.04-arm`/Xvfb is distinct from the owner's Ubuntu26.04/GNOME.

Before any .NET compilation, `build-native-arm64.py` runs the new read-only Rnote
source/ABI preflight. The existing producer then builds the normal .NET graph and
Root launcher and invokes `rnote-native/rnote_native_stage.py` before publish
identity inspection/package assembly. The stage requires:

- A real native Rust toolchain meeting the crate's `rust-version`1.92, with host
  `aarch64-unknown-linux-gnu`, Cargo and the existing committed `Cargo.lock`.
- C build tools, CMake, pkg-config, binutils/readelf, ldconfig, dpkg/dpkg-query,
  and GLib/GIO/GObject, Cairo, Pango/PangoCairo and librsvg development providers.
  Their usual Ubuntu build packages are `build-essential cmake pkg-config
  libglib2.0-dev libcairo2-dev libpango1.0-dev librsvg2-dev`; actual Cargo/pkg-config
  errors still govern missing prerequisites. The current workflow does not yet
  provision the added Rust/development dependencies; do not claim it runs as-is.
- Supported network access for Cargo's exact locked registry/git source graph.
  `cargo vendor --locked --versioned-dirs` retains the real dependency source and
  licences. The actual build uses `cargo build --frozen --release --jobs1` with the
  generated vendor configuration, explicit native ARM target and a fresh external
  target directory. It does not regenerate Cargo.lock.

The stage copies `libcakeos_canvas_rnote_poc.so` into the genuine publish tree.
It verifies little-endian64bit ARM ELF shared-object identity; defined public
symbols for every current managed Rnote P/Invoke; the managed/Rust/header ABI
version; and actual `DT_NEEDED` providers. It also resolves the rasterizer's
librsvg, GLib/GObject and Cairo dlopen providers. ARM dpkg ownership, observed
provider versions and file hashes form a conservative versioned dependency list.
No library is loaded or app launched by these static checks.

The source archive contains current crate/managed inputs and the locked vendored
dependency sources/licences. It is a separate corresponding-source sidecar in the
existing evidence directory, not an executable or a licence-review verdict.
Retain `rnote-native-stage.json` and `rnote-corresponding-source.tar.gz` with the
product source archive. To reproduce from this archive, extract it, change to
`rnote-source`, and use its portable `vendor-config.toml` with the crate manifest
under `repository/9to1 Workspace/Canvas/rnote-poc`; a native toolchain and the same
system development providers remain necessary.

The existing package assembler takes `--rnote-native-receipt`, rechecks actual
source/library/provider bytes, includes its proof in the existing product build
receipt, installs the native receipt in `/usr/share/doc/9to1-preview`, and declares
the derived runtime dependency floors. It also installs/verifies the actual
Rnote GPL licence text and current provenance. It streams the actual resulting DEB to
verify native library and receipt bytes plus declared dependencies. Its existing
`ASSEMBLED_NOT_NATIVE_ACCEPTED` status is unchanged. No second package rebuild or
new packaging framework is used.

The initial25October source audit found twelve missing managed exports. The
retained019a9278 six-file bridge has been recovered for seven; a subsequent
source-only implementation adds the remaining five through exact controlled
patches to the same locked8cac558 donor. The producer applies those patches to
Cargo-vendored source, verifies exact pre/postimage and Cargo file checksums,
retains modified source/licences, and compiles using that configuration. The
unchanged upstream pin is provenance for a declared derivative, not a claim that
the patched engine is byte-identical to upstream. Native method/task handling,
Rust compilation/tests and installed behavior remain NOT RUN here. The existing
v0.14 actual Cargo/v0.15 selected producer identity conflict remains explicit.
Recover useful historical source first; do not substitute obsolete HUI/AppFlowy
or bypass an actual ABI/source failure.

## Immutable development-test manifest

After actual build/package/basic-smoke results exist, run the separate metadata
helper into a new owner-only directory outside Git. Use real private destination
IDs from existing discovery; do not put personal IDs or credentials in source.

```sh
python3 -B eng/release-delivery/test_build_manifest.py \
  --package-receipt /actual/package/package-receipt.json \
  --build-receipt /actual/evidence/native-build-receipt.json \
  --smoke-receipt /actual/owning-run/receipt.json \
  --scope-evidence /private/all-boards-ui-review.json \
  --native-receipt /actual/evidence/rnote-native-stage.json \
  --source-receipt /actual/evidence/corresponding-source-receipt.json \
  --drive-id ACTUAL_EXISTING_DRIVE_ID --folder-id ACTUAL_EXISTING_FOLDER_ID \
  --limitation 'Full independent acceptance PAUSED; Ubuntu26/GNOME use NOT RUN' \
  --out /fresh/private/test-build.json
```

If the package bundles inference or Chromium, also supply the actual retained
`--engine-source FILE` / `--browser-source FILE` sidecars. Do not drop required
component sources to shrink transport. Add every known material limitation with
another `--limitation`. The helper binds actual DEB name/architecture/bytes/SHA,
existing build/package/native/source/smoke receipts, final UI source review and
supporting source files. It creates one new0600manifest and refuses overwrite.
It has no upload, credentials, install or native-execution capability. Structured
receipt checks cannot establish the semantic truth of arbitrary review claims.

## Private transfer and laptop receipt

Use the existing owner-only OneDrive project folder through the supported
`upload_drive_item_content` connector and actual absolute file input. Recheck
folder identity/effective owner-only permissions, use conflict=fail, verify the
returned item/parent/size and permissions, and hash authenticated raw readback.
Do not create sharing links or upload private package/source to public Actions.
A real43MB private binary roundtrip was retained; that does not prove full-size
package upload/input support or laptop delivery. Tool caps remain in force.

The manifest records transfer and laptop receipt as NOT RUN. It carries the
original startup-only baseline identity from the unchanged production helper.
The receiving laptop must preserve that original file, use a separate private
candidate directory, hash-verify actual source/package bytes and return path,
size, SHA and time. Installation and fresh-profile Boards open/edit/save/reopen/
normal-close checks are separate, actual actions. Label Ubuntu26/GNOME and native
runtime NOT RUN wherever they have not happened.

A fresh installed9to1connector status observation on25October still advertises
only `health`, `status`, `read_evidence`; `new_worker_creation:false` and
`worker_supervisor:false`, with131072byte bounded reads. The registered host is
the laptop. Online status does not grant ARM build, transfer or shell invocation.
The other discovered session connector controls documents only. No supported
remote ARM execution path was established. Owner/local invocation or a separately
verified existing execution capability remains a concrete operational blocker.

## Bounded infrastructure verification

```sh
python3 -B -m unittest discover -s .github/scripts/native16-arm64 -p test_rnote_native_stage.py -v
python3 -B -m unittest discover -s eng/release-delivery -p test_test_build_manifest.py -v
```

These use tiny real files/DEBs and fake provider/tool observations. They never
build Rust/.NET, load Rnote, contact a model/provider, upload or install an app.
Actual ARM build, native drawing/runtime, GNOME and laptop transport remain open.
