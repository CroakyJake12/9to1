# 9to1

9to1 is a Linux-first, cross-platform productivity platform under active consolidation. It combines recovered CakeOS, CakeAI/Haven, and donor source without treating donor code or experimental prototypes as finished products.

## Current State

The repository is **not yet a production-ready ecosystem**. `docs/MASTER-MIGRATION-STATUS.md` is the authoritative evidence ledger. A source tree, test project, HUI scene, AXAML page, or package recipe does not establish CUI parity or platform verification.

## CUI

`framework/CUI/` contains the first active CUI boundary. It loads authored `.cui` documents and explicitly rejects `.axaml` and `.hui` inputs. The Home slice at `9to1 Workspace/Home/UI/Home.cui` is the first authored CUI surface.

The CUI loader is currently parser-only. It has no CUI renderer, compiler, DevTools, resource system, or vendored Avalonia fork. Existing HUI and Avalonia-based hosts are legacy migration material, not the developer-facing CUI architecture. The current constraints and provenance are documented in `docs/cui/README.md` and `docs/architecture/cui.md`.

## Layout

- `9to1 OS/`: OS platform, legacy hosts, packaging, release material, and the CUI foundation.
- `9to1 Workspace/`: standalone app and shared application source.
- `9to1 Workspace/Home/`: embedded OS Home domain and authored CUI surface.
- `9to1 Workspace/Home/Source/Dulche/`: shared local llama.cpp-derived runtime.
- `9-1 OS (Android)/`: Android-specific host and service code.
- `reference/`: retained historical, superseded, and donor material.
- `docs/parity/`: app-level donor/CUI/platform evidence.

## Build And Verification

Use the one entry point from PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File eng/9to1.ps1 -Command restore
powershell -NoProfile -ExecutionPolicy Bypass -File eng/9to1.ps1 -Command build
powershell -NoProfile -ExecutionPolicy Bypass -File eng/9to1.ps1 -Command test
powershell -NoProfile -ExecutionPolicy Bypass -File eng/9to1.ps1 -Command package-linux
powershell -NoProfile -ExecutionPolicy Bypass -File eng/9to1.ps1 -Command package-windows
powershell -NoProfile -ExecutionPolicy Bypass -File eng/9to1.ps1 -Command verify
```

`package-windows` produces a self-contained `win-x64` developer-preview executable. `package-linux` cross-publishes `linux-x64` and produces a developer-preview `.deb` without requiring a Linux builder. Outputs are under `artifacts/developer-preview/` and are explicitly non-distributable legacy-host test artifacts, not CUI, Dulche, Linux-runtime, or release verification. `verify` still fails while the required hosts, runtime smoke environments, notices, and CUI migration are absent.

## Licensing And Provenance

Donor and third-party material remains subject to its original licence and notices. Do not remove notices while migrating. The current provenance process and missing licence evidence are tracked in `docs/architecture/third-party.md`.
