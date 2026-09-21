# 9to1

9to1 is a Linux-first, cross-platform productivity ecosystem. The repository
contains the operating system integration, shared application sources, Android
host, donor material, and the in-progress CUI user-interface framework.

> **Migration status:** CUI consolidation is active and incomplete. Existing
> AXAML and HUI sources remain migration inputs until their CUI replacements
> pass runtime and parity verification. See
> [`docs/MASTER-MIGRATION-STATUS.md`](docs/MASTER-MIGRATION-STATUS.md); source
> presence or a successful build is not treated as product verification.

## Repository map

| Path | Purpose |
| --- | --- |
| `framework/CUI/` | Avalonia-derived CUI foundation, compiler/tooling, DevTools and platform adapters |
| `9to1 Workspace/` | One source tree per application plus shared domain and service code |
| `9to1 OS/` | Home/OS integration, image tooling and package-preload release contracts |
| `9-1 OS (Android)/` | Android host and platform adapters |
| `reference/` | Historical and donor evidence; not active product source |
| `docs/parity/` | Per-product donor and runtime evidence |

The detailed source classification is in
[`docs/SOURCE-CLASSIFICATION.md`](docs/SOURCE-CLASSIFICATION.md).

## CUI

CUI is the authoritative 9to1 UI identity and `.cui` is its application markup
format. CUI retains the mature compositor, rendering, text, layout, input,
accessibility and platform architecture of an explicitly pinned Avalonia source
fork while replacing normal application AXAML authoring with CUI project,
compiler, resource and runtime support.

Normal CUI authoring accepts `.cui` only. `.axaml` and `.hui` are unsupported as
active CUI documents; explicit migration tools may read them as legacy input.
The framework also owns reusable controls, platform adaptation, source mapping
and CUI DevTools. Current implementation details and limitations are recorded in
[`docs/cui/README.md`](docs/cui/README.md).

## Products

- **Home** is embedded in the OS and manages the app catalogue, installed
  packages, updates, settings, models, voice and Dulche lifecycle.
- **Spaces** supplies separate Chat, Study and Tasks environments plus custom
  Spaces. It is not a prompt switch inside one chat screen.
- **Dulche** is the shared, single-instance local model runtime. Applications
  consume its broker API rather than starting one model server per app.
- Standalone applications build from `9to1 Workspace/`; the OS consumes their
  `.deb` artifacts through a hash-pinned preload manifest instead of carrying
  duplicate app implementations.
- Shared contextual AI uses semantic app context and typed actions. It should
  not simulate raw UI input when an approved domain action is available.
  The shared framework lives at `framework/CUI/AI/`.

## Platform model

Application domain code and CUI content are shared. Linux is the first release
target and Debian packages are the default distribution. Windows uses the same
application source with platform adapters for native chrome, dialogs, taskbar,
clipboard, drag/drop and other operating-system integration. Android-specific
hosting remains behind platform contracts.

## Build and test

The repository currently contains several independently buildable .NET, Python,
native and packaging components. The root orchestrator exposes the buildable
consolidation sets and fails honestly for package paths that do not exist:

```powershell
./eng/9to1.ps1 restore -Component core
./eng/9to1.ps1 build -Component core
./eng/9to1.ps1 test -Component core
./eng/9to1.ps1 verify -Component core
```

See [`eng/README.md`](eng/README.md) for component and packaging commands.

Linux image and `.deb` commands require a Linux build host and the native
dependencies named by each package recipe. Windows and UI work additionally
requires real launch/render/interaction evidence; compilation alone is not
enough.

## Verification and licensing

The acceptance chain and current blockers are maintained in the master status
ledger and per-app parity records. Third-party provenance is indexed in
[`THIRD_PARTY.md`](THIRD_PARTY.md). Preserve upstream notices and licences when
moving or adapting donor code.
