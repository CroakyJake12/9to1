# Source identity — Lane 16 (pass 2)

## Workspace base

- Isolated base: `ddb2a8b813c8189b31556ef2ee491ac4f6f2b4eb`; frozen parent `7d2a1c4e095a2a6e0168ccf95d5942e9460c370a`
- Workspace: `/workspace/9to1-mercury-extra16-packages`
- Canonical spec SHA-256: `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`

## Typed-transport reference (frozen)

- `shared/9to1-test-evidence/parallel6-transport-pass5-frozen/identity.json`
- Checkpoint/frozen Git object: `dc4811acd5cbc2a0130af7fa335c5e0312741f87`
- Root source-only probe confirming pass-1 defects: `shared/9to1-test-evidence/extra16-package-helper-source-probe/result.json`
- Used only to author fixtures against exact API/resource names; no active transport source was
  overwritten.

## Files changed by this lane (SHA-256, at handoff)

| File | SHA-256 |
| ---- | ------- |
| `framework/CUI/Compiler/CakeOS.Cui.Compiler.csproj` | `12f8170d3cd99e2b4faffcfc0f650923c584c7c340ee4fd4dc38c941a464ead4` |
| `framework/CUI/Build.Tasks/CakeOS.Cui.Build.Compiler.csproj` | `5b49ef8bda1f367371b43655786a9841f388fb7eb0070992c6fd6a57596f0825` |
| `framework/CUI/Build.Tasks/buildTransitive/CakeOS.Cui.Build.Compiler.props` | `8d221cfde6843b9a429bd22c41025b42a0f017b6680b958eb9720fd9c1932c33` |
| `framework/CUI/Build.Tasks.Tests/verify_toolchain_package.py` (pass 2) | `dfec25a20c28af49b29d55b9f5b8c34394210a32a6cd9a92207932aae078b203` |
| `framework/CUI/Build.Tasks.Tests/verify_nuget_consumer_dependencies.py` (pass 2) | `5e62f2a32e6b162e1118c7c13cec2d4e37ce206be88435221ae7db191e03cd8b` |
| `work/extra16-handoff/HANDOFF.md` | `0c4bccc9f305dac195b65e15989778da548ae85a1ff7175de67c9f43d2ebc9d4` |
| `work/extra16-handoff/SOURCE-IDENTITY.md` (this file) | `16e41254c88097d2de79c2763b240befb51f21a6a9315f247eb69868e450fc69` |
| `work/extra16-handoff/TESTS.md` (pass 2) | `94d02a93125305565ed390a4ea887d8076dfc280932e278685bf045e9af81dce` |
| `work/extra16-handoff/PATCH-packaging.md` (pass 2) | `c31a85da224bb48f0ed20e6151de40ec77c7d48bf84100bf3bf77924fbe4d7f4` |

File hashes are `sha256sum` of the on-disk bytes at handoff time.

## Nothing else touched

No other source/test/config files were modified. No commit/push was made; the working tree contains
only these edits plus this handoff. Active transport files (Compiler.cs/targets), Loader/Container,
vendor, and Runtime/Core/Language metadata were not touched.
