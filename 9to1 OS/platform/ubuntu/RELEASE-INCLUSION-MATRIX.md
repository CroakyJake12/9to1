# HavenOS release inclusion matrix

States are evidence classifications. `UNKNOWN` means the evidence source has
not been recovered; it does not mean the component is absent. GNOME/Mutter
local checkout state is recorded in the evidence, not promoted to a global
provenance state.

| Component | Evidence | State |
| --- | --- | --- |
| Ubuntu base | Recovered ISO `C:\Temp\havenos-transfer\HavenOS-26.04.1-LTS-hui-preview-uefi-fixed-amd64.iso`, 2,384,451,584 bytes, SHA-256 `BDF689CFC40C79F070000DAF0A069E9B31CA9FA1A0E7C4CBB7FD195E1C2F1216`; builder provenance and boot evidence UNKNOWN | PARTIAL |
| GNOME Shell | Expected `1d66bb011e56b1759a65eb8d3e9f194c92523baa`; coordinator checkout `733a0886f1ba839b377be520972b7489a63a2b3a`; global provenance not asserted | UNKNOWN |
| Mutter | Expected `90d032ae67645cfa1f78159e45b626ce479ba358`; coordinator checkout `733a0886f1ba839b377be520972b7489a63a2b3a`; global provenance not asserted | UNKNOWN |
| Write | No authoritative implementation recovered; not present in `platform/ubuntu/release-staging.json` as a stageable artifact | BLOCKED |
| Present | Proven branch/runtime evidence; release package, launcher, image and VM evidence outstanding | PARTIAL |
| Data | Proven branch/runtime evidence; release package, launcher, image and VM evidence outstanding | PARTIAL |
| Canvas/Rnote | Proven source and CI graphical evidence; release package, launcher, image and VM evidence outstanding | PARTIAL |
| Boards/AppFlowy | Proven implementation/evidence ledger; release package, launcher, image and VM evidence outstanding | PARTIAL |
| Wine compatibility | Proven Linux workflow; release artifact/package boundary and runtime evidence outstanding | PARTIAL |
| HUI runtime | `haven-hui-preview_0.1.0+git2a578502c319_amd64.deb`, SHA-256 `104F8FF3929B8B0E1C90673804D7CFBE79A31EA5C2763DC040246A7EAD53575A`; Linux runtime verified, immutable CI repository/workflow/artifact metadata and approved VM/image outstanding | READY |
| llama.cpp runtime | Immutable `haven-llamacpp-runtime_0.4.0+haven0.1_amd64.deb`, SHA-256 `13ea16e4ffa92d1e4b2f31a1a32ba00dc8802ec9077733d7db96a68dad92142d`; artifact 10031771129 and staging evidence verified; CI repository/workflow metadata outstanding | READY |

## Composition and validation status

- The live-build configuration targets Ubuntu `resolute`, `amd64`, and a live installer.
- No new ISO build was started. Approved builder live state and existing ISO live path are UNKNOWN because the sandbox coordinator returned HTTP 502.
- The approved VM remains the configured VM at snapshot `f25b7456-c562-4ba4-9798-5814ee17fe25`.
- The minimal package-list repair is preserved; no wholesale component-history merge was performed.
- `platform/ubuntu/release-staging.json` is the cohort staging source of truth.
  Only READY records may enter `artifacts/packages`; PARTIAL, BLOCKED, and
  UNKNOWN records are excluded.
- Android compatibility is excluded from this release scope.

READY states are package-level cohort staging only. Image inclusion, boot,
installation, launcher smoke, backend smoke, offline behavior, and persistence
remain open release gates; immutable CI repository/workflow/artifact metadata
must also be supplied before the staging harness can pass.
