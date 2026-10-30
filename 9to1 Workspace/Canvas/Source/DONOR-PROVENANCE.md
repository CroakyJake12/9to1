# Canvas Rnote source

Upstream https://github.com/flxzt/rnote (`main`) and controlled fork https://github.com/CroakyJake12/rnote are both pinned to `1a728d6a85db3528f9c79dc0990700e91b22696f` (selected 2026-09-24 UTC). Actual unmodified source is in `Source/Rnote/`; GPL-3.0-or-later is retained in `Source/Rnote/LICENSE`. No patch applies. The existing Cargo proof still pins older Rnote `8cac558fa73ee63d958cec05124ea2fdbd7d608d` (v0.14.2); its compatibility with this new source is **unverified**, not silently updated. See `eng/donor-sources.json`.

## Pinned as a submodule 30 October 2026 (measured, not inferred)

- Gitlink pinned at `8cac558fa73ee63d958cec05124ea2fdbd7d608d` = upstream **v0.14.2**, which is the
  revision the Canvas bridge actually compiles against (verified: its `Cargo.toml` declares
  `version = "0.14.2"`).
- The donor manifest's recorded pin `1a728d6a85db3528f9c79dc0990700e91b22696f` declares
  `version = "0.15.0"` (verified the same way). That upgrade remains **unresolved**: the geometry
  dependency and API change, and the bridge has not been migrated. Pinning is not a claim that
  v0.15 is in use.
- Our own modification exists as a separate branch in the controlled fork:
  `astra/lossless-canvas-serialization-20261003`, tip `53981d09b0cdcc7f12af9ee579c4c4bfb95d6821`.
  Measured: it adds **exactly one** commit of ours, `Add opt-in lossless native geometry
  serialization`; the remaining 36 commits between v0.14.2 and that branch are upstream commits
  (i18n, stylus fixes, dependency bumps). Carried patch files also live in
  `Canvas/rnote-poc/patches/`. GPL-3.0-or-later notice retained with the patches.

## Engine fork branch created 30 October 2026 (forking authorised by the owner)

Applying the owner's rule, Canvas is a **fork of Rnote's engine crates**, because the measured
modifications land in the engine, not in an application layer:

- Controlled branch: `9to1/canvas-engine-20261030`, tip **`44ff108978184c6fd7e7e5fce686059ff865efa9`**
  (pushed to the controlled remote `CroakyJake12/rnote`).
- It is two commits on top of the consumed upstream pin `8cac558fa73e` (v0.14.2):
  `Add keyed stroke operations for canonical Board/Canvas block edits` and
  `Add bounded headless task consumption and split lineage`.
- Verified without a compiler, by reproducing the hashes our own patch manifest already recorded:
  after commit 1, `crates/rnote-engine/src/engine/mod.rs` = `c9297ad60653…` (expected `c9297ad60653…`);
  after commit 2 it = `46c0e665915f…` (expected `46c0e665915f…`). The patches are sequential by
  construction, which is why applying 0002 before 0001 fails: 0002's before-state is 0001's
  after-state.
- **Not cherry-picked, deliberately:** the branch `astra/lossless-canvas-serialization-20261003`
  (tip `53981d09`) carries our opt-in lossless serialization work, but it is written against the
  **v0.15 API** (`p2d::glamx`, `Vector2`) while v0.14.2 still uses nalgebra. Cherry-picking it onto
  this branch produced a conflict importing foreign API types, so it was aborted. Measured: nothing in
  this repository requests the `lossless-serialization` feature (searched `.toml`, `.rs`, `.cs`,
  `.md`), so it is **unused work on a branch**, not a missing feature. Migrating it is a v0.15 upgrade
  decision, recorded here rather than smuggled into the pin.
- Consumption is **not switched over yet**, and this commit does not pretend otherwise:
  `rnote-poc/Cargo.toml` and `Cargo.lock` still fetch upstream `flxzt/rnote` at
  `?rev=8cac558fa73e…#8cac558fa73e…` (verified in the lock), and
  `.github/scripts/native16-arm64/rnote-native/rnote_source_patches.py` asserts exactly that
  upstream source line, then applies both patches to the vendor copy while checking every
  pre/post-file hash. So the fork branch exists as the durable home for the modification, and the
  consumer still reconstructs it at build time. Moving the consumer onto the fork revision is the
  planned next unit (needs a Cargo lock regeneration plus the stage's own tests) and is listed in
  `docs/development/donor-forking.md` for owner approval, because it changes how the app is built.
- Toolchain limit stated plainly: this PC has **no cargo/rustc** (measured: absent; the rustup
  install did not produce a usable `~/.cargo/bin/cargo`), so no Rust build of the bridge was
  attempted here. Every Rust-level claim in this file is **NOT RUN**.
