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
