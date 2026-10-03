# 9to1

9to1 is a cross-platform productivity ecosystem comprising Workspace and AI applications, shared CUI and Dulche frameworks, and 9to1-OS.

The [current 9to1 Development Specification](https://docs.google.com/document/d/1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg/edit) is the product authority, amended by explicit user instructions. Repository notes and existing implementations do not redefine its scope. The current four-worker ownership, observed checks and unresolved requirements are recorded in [the Astra working ledger](docs/ASTRA-WORKING-LEDGER.md). The product remains incomplete; bounded tests and builds do not establish whole-product verification.

## Repository map

| Path | Purpose |
| --- | --- |
| `framework/CUI/` | CUI language, compiler, runtime, themes, semantic AI and developer tools, with preserved Avalonia donor source |
| `9to1 Workspace/` | Canonical application sources and shared domain, service and host implementations |
| `9to1 Workspace/Home/` | Home and shared runtime/package coordination |
| `9to1 Models/` | Dulche runtime and model-related source |
| `9to1 OS/` | OS integration, compatibility, image and release/package tooling |
| `9-1 OS (Android)/` | Android host and platform adapters |
| `reference/` | Preserved historical/donor source, rather than active product authority |

CUI uses `.cui` application markup. Existing HUI and AXAML implementations remain useful migration inputs until replacements meet the specification and relevant runtime checks. Source classification is documented in [SOURCE-CLASSIFICATION](docs/SOURCE-CLASSIFICATION.md); donor licences and provenance are indexed in [THIRD_PARTY](THIRD_PARTY.md) and component records.

## Build and validation

The repository contains .NET, native, Python and packaging components. [eng/README.md](eng/README.md) describes the build entry point and its currently registered sets:

```powershell
./eng/9to1.ps1 -Action restore -Component core
./eng/9to1.ps1 -Action build -Component core -Configuration Release
./eng/9to1.ps1 -Action test -Component core -Configuration Release
./eng/9to1.ps1 -Action verify -Component core
```

The `core` set is a bounded check; it does not cover all required apps, donor runtimes or platform/packages. Exact commands and current outcomes belong in the working ledger. Production DNS changes, replacing live sites, retiring WordPress and purchases require specific approval; preserve existing production content.
