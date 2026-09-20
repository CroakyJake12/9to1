# Master Migration Status

Last audited: 2026-09-20. This ledger records evidence, not intent. `VERIFIED` requires the complete acceptance chain in the master task; no system currently meets that chain.

| System | Donor/source | CUI port | AXAML removed | Known features | AI context / Dulche | Linux / Windows | Packaging | Tests | Status | Owner | Blockers |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| CUI framework | PARTIAL | Parser/load only | No | Parser only | N/A | No host / No host | No | 4 parser tests pass | UNFINISHED | Framework | No compiler, renderer, fork, DevTools, controls, or native runtime |
| Legacy platform hosts | PRESENT | None | No | Avalonia host compilation | No / managed build only | Managed builds pass / managed build only | Preview recipe only | Managed builds pass | UNFINISHED | Platform | Still HUI; graphical smoke and CUI host absent |
| Home | PARTIAL | Authored `Home.cui`, load only | N/A | Catalog, installed, updates, settings, runtime models | Typed runtime state, no live provider | No / No | No | Domain and CUI-load tests | UNFINISHED | Home | No renderer, host route, package backend, or runtime smoke |
| Dulche | PRESENT | N/A | N/A | Shared multi-slot broker, model/runtime contracts, native tool permission flow, cancellation control path | Shared slot-worker design; context changes are turn-safe | Unix target only / no Windows native run | Recipe only | 36 focused logic tests under a non-production Windows lock shim; Unix integration pending | UNFINISHED | Dulche | No real GGUF, Linux service, OS tool sandbox, one-instance, or Windows runtime evidence |
| Spaces | PARTIAL | None | No | Separate Chat, Study, Tasks, custom-space descriptors | Typed context/action boundary only | No / No | No | 11 model tests pass | UNFINISHED | Spaces | No CUI surface, node editor, host integration, or app AI bar |
| Chat | PRESENT | None | No | Existing legacy surface only | Not re-audited | No / No | No | Not re-audited | UNFINISHED | Spaces | CUI, parity, Dulche integration, and host validation absent |
| Study | PRESENT | None | No | Existing legacy surface only | Not re-audited | No / No | No | Not re-audited | UNFINISHED | Spaces | CUI, subject/progress acceptance, voice integration, and validation absent |
| Tasks | PARTIAL | None | No | Existing agent/task source only | Not re-audited | No / No | No | Not re-audited | UNFINISHED | Spaces | Separate task surface, CUI, approvals, and parity absent |
| Files | DONOR only | None | N/A | Windows donor tabs, panes, operations | None | No / donor only | No | Donor shared projects build | MISSING | Files | No active portable app or CUI; Windows Shell/COM coupling |
| Canvas | PRESENT | Legacy HUI only | N/A | Canvas donor/domain tests | No shared AI bar | No runtime / No runtime | Recipe only | 27 tests previously pass | UNFINISHED | Canvas | CUI surface, full Rnote parity, packages, platform smoke absent |
| Boards | PRESENT | Legacy HUI only | N/A | Board contracts and HUI tests | No typed active AI contract | No runtime / No runtime | Recipe only | 44 and 34 tests previously pass | UNFINISHED | Boards | CUI port, donor parity, packages, platform smoke absent |
| Browse | PRESENT | None | No | Domain builds | Not re-audited | No / No | No | Build passes | UNFINISHED | Browse | Browser engine, CUI chrome, runtime, package, parity absent |
| Write | PRESENT | None | No | LibreOffice POC | No active CUI contract | Linux dependency missing / No | No | Semantic Python compile passes | UNFINISHED | Write | CUI editor, LibreOffice runtime, AI actions, packages absent |
| Data | PRESENT | Legacy HUI only | N/A | Calc/DuckDB engine | No active CUI contract | LibreOffice missing / No | No | App/HUI build and smoke pass | UNFINISHED | Data | CUI spreadsheet, Linux Calc runtime, AI actions, package absent |
| Present | PRESENT | None | No | Presentation domain | No active CUI contract | Native dependency missing / No | No | Build/headless test pass | UNFINISHED | Present | CUI editor, native engine runtime, AI actions, package absent |
| Studio / Dev / Terminal | PARTIAL | None | No | Project source present | Not re-audited | No / No | No | Shared Release build and 1,824 shared tests pass | UNFINISHED | Developer apps | CUI, platform-neutral PTY, LSP/Git acceptance, package, parity absent |
| Notes / Plan / Call / Automations | Not re-audited | None | Unknown | Not re-audited | Not re-audited | No / No | No | Not re-audited | MISSING | Productivity apps | Current source and donor acceptance inventory incomplete |
| Picture / Wave / Motion | PARTIAL | None | Active AXAML exists in Picture | Not re-audited | Not re-audited | No / No | No | Not re-audited | UNFINISHED | Media | CUI replacement, engines, packages, and parity absent |
| Connectors | PRESENT | N/A | N/A | Existing connector source | Not re-audited | N/A | N/A | Infrastructure suite: 368 passed | UNFINISHED | Connectors | Trust/provider audit and validation incomplete |
| Android | PRESENT | Legacy HUI | No | Android host/services | No CUI integration | Android only / N/A | APK source only | No current validation | UNFINISHED | Android | HUI runtime remains, generated files tracked, CUI migration absent |
| OS preload / releases | PARTIAL | N/A | N/A | Hash-locked copy-only manifest | Dulche artifact entry only | Linux image unverified / N/A | No package bytes | Metadata validation passes | UNFINISHED | Release | No `.deb` artifacts, image, VM boot, install, or launcher evidence |

## Reference Evidence

- CUI parser: `docs/cui/README.md`.
- Home: `docs/parity/home.md`.
- Dulche cancellation: `docs/dulche/shared-runtime-cancellation.md`.
- Files: `docs/parity/files.md`.
- Data, Write, and Present: `docs/parity/data.md`, `docs/parity/write.md`, and `docs/parity/present.md`.
- Package preload: `docs/packaging/package-preload.md`.

## Observed Verification

| Command | Result |
| --- | --- |
| `powershell -NoProfile -ExecutionPolicy Bypass -File eng/9to1.ps1 -Command build` | PASS. CUI, Home, and the shared Release solution built with 0 warnings and 0 errors. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File eng/9to1.ps1 -Command test` | PASS. CUI 4/4, Home 6/6, and shared solution 1,824/1,824. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File eng/9to1.ps1 -Command package-linux` | BLOCKED. `9to1 OS/artifacts/packages` is absent. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File eng/9to1.ps1 -Command package-windows` | BLOCKED. No shared-CUI Windows package pipeline or runtime evidence exists. |
| `git diff --check` | PASS. |
| Generated-file audit | PASS for tracked known Python bytecode, `obj-hui`, and `obj-apk` paths: 0 remain. |

## Current Global Blockers

- No maintained, licensed Avalonia-derived CUI fork or CUI renderer/compiler/DevTools exists.
- Active HUI and AXAML source remains throughout migration-era projects.
- No standalone app has the complete `.cui`, host, package, Linux launch, Windows launch, AI action, and donor-parity acceptance chain.
- Required Linux dependencies, Windows native runtime tests, Debian artifacts, image builder, and VM evidence are unavailable in this workspace.
- Known tracked `obj-hui`, `obj-apk`, and Python bytecode files were removed and are ignored; a broader generated-output re-audit remains required before release.
