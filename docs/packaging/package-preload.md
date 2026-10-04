# Package Preload Model

## Current model

`9to1 OS/release/package-preload-manifest.json` is the release-owned allowlist
for package preloading. Each entry names a standalone `.deb` by filename and
SHA-256 and records its package metadata, immutable source provenance, workflow
artifact identity, and runtime dependency metadata. It does not name an
application source tree, publish directory, or build command.

The only listed artifacts are:

| Package | Immutable artifact | Source mapping | Preload status |
| --- | --- | --- | --- |
| `haven-hui-preview` | `haven-hui-preview_0.1.0+git2a578502c319_amd64.deb` | `CroakyJake12/CakeOS`, `platform/hui-linux-graphical-preview`, revision `2a578502c31973fbefa3b3f82efa22d7ad11db53`, artifact `10031358716` | BLOCKED: package bytes are absent |
| `haven-llamacpp-runtime` | `haven-llamacpp-runtime_0.4.0+haven0.1_amd64.deb` | `CroakyJake12/CakeOS`, `main`, revision `11be1b700022095e4b598768b5286adfe966f85a`, artifact `10031771129` | BLOCKED: package bytes are absent |

The manifest is cross-checked against:

- `9to1 OS/packaging/llamacpp/cohort-artifact.lock.json`
- `9to1 OS/platform/ubuntu/release-staging.json`
- `9to1 OS/release/emergency-preview-0.1.json`

`assemble-emergency-preview-0.1.sh` only hashes and copies the listed package
bytes into the cohort output. It does not fetch, build, extract, install, start
services, or change a VM. Package files are copied as opaque bytes, preserving
any license and notice files embedded in the original artifacts.

The existing image builder accepts this cohort through `HAVENOS_DEB_DIR` and
copies `.deb` files into the live-build package directory. It is not modified by
this model. The preload manifest is therefore the admission gate before calling
the image builder, rather than a claim that an image has been built.

## Developer Preview Outputs

`eng/9to1.ps1 -Command package-windows` creates a self-contained `win-x64`
desktop executable. `package-linux` cross-publishes the same current host for
`linux-x64` and assembles `9-1-legacy-desktop-preview_*.deb` using archive tooling
available on Windows. These outputs are written under
`artifacts/developer-preview/`; they are excluded from the release cohort,
preload manifest, image builder, and release staging records.

They are developer test artifacts only. They package the current legacy desktop
host, do not prove a CUI renderer or Dulche runtime, carry an explicit
non-redistribution notice, and have not been installed or run on Linux. The
release model above remains unchanged.

## Metadata validation

Run this from the repository root on PowerShell. The default validation reads
only JSON and referenced repository files; it requires neither package bytes nor
`dpkg-deb`, `jq`, `live-build`, Docker, or a VM.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "9to1 OS/release/tests/validate-package-preload.ps1"
```

After the exact standalone artifacts have been staged under
`9to1 OS/artifacts/packages`, add `-RequireArtifacts` to verify regular-file
status and SHA-256. This still does not inspect Debian control metadata or
install anything.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "9to1 OS/release/tests/validate-package-preload.ps1" -RequireArtifacts
```

On a Linux staging host, assemble the verified copy-only cohort and give that
directory to the existing image builder:

```sh
"9to1 OS/release/assemble-emergency-preview-0.1.sh"
HAVENOS_DEB_DIR="9to1 OS/artifacts/emergency-preview-0.1-cohort" "9to1 OS/image/build-live-iso.sh"
```

The second command remains blocked until a dedicated Linux builder and the
exact package artifacts are available.

## Current .deb gaps

- No release-admitted `.deb` files exist in this checkout. `9to1 OS/artifacts/packages`
  does not exist, so neither listed artifact can be byte-verified or assembled.
  `artifacts/developer-preview/` is explicitly excluded from this release path.
- No image or ISO artifact exists. There is no package-install, boot, launcher,
  runtime, or approved-VM evidence for this release.
- The image builder currently accepts every `.deb` in `HAVENOS_DEB_DIR`; it does
  not itself read the preload manifest or enforce its SHA-256 values. Use the
  validated copy-only cohort directory, not a broad package folder.
- Canvas/Rnote and Boards/AppFlowy have builders but lack immutable standalone
  package artifacts and the required hash, entrypoint, dependency-control, and
  install or runtime smoke evidence. The emergency manifest previously cited a
  `component-packaging.yml` workflow that is not present in this checkout; it
  now records the builders as the available evidence and the absent workflow as
  part of the blocker.
- Welcome, HavenOS Shell, and HavenOS Studio have packaging recipes but no
  hash-pinned `.deb` evidence in this checkout. Studio also lacks its required
  published output, and the emergency manifest's former
  `tooling/HavenOS.Studio` source-path citation is not present in this checkout.
- Data, Present, and Wine compatibility are excluded. The retained cohort
  helper scripts now fail intentionally so they cannot create source-carrier
  archives from application source. Write, Plan, Terminal, Wave, and Images
  have no accepted standalone package artifacts.
- The llama.cpp package manifest records its upstream MIT notice path. No
  equivalent license-content evidence for the unavailable HUI artifact is in
  this checkout; preloading can preserve package bytes but cannot validate
  license contents until the real artifact is staged.
