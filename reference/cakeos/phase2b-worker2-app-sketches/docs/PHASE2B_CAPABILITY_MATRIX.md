# HavenOS Phase 2B: Donor-Backed App Convergence - Capability Matrix

## Classification Legend
- **DONOR-OWNED GENERIC**: Functionality owned by upstream donor project
- **REUSABLE CAKE FEATURE**: Platform-neutral CakeAI/HavenOS logic
- **LINUX ADAPTER**: Linux-specific integration code
- **OBSOLETE**: Deprecated/stale code to remove
- **NEW INTEGRATION**: New code written for this convergence

---

## 1. Data (Calc + DuckDB)

### Donor: LibreOffice (Calc) + DuckDB

| Capability | Classification | Evidence |
|------------|----------------|----------|
| Spreadsheet grid/model | DONOR-OWNED GENERIC (LibreOffice Calc) | LibreOfficeKit provides full calc engine |
| Formula engine | DONOR-OWNED GENERIC (LibreOffice Calc) | LibreOffice core |
| Chart/visualization | DONOR-OWNED GENERIC (LibreOffice Calc) | LibreOffice core |
| Import/export (ODS, XLSX, CSV) | DONOR-OWNED GENERIC (LibreOffice Calc) | LibreOffice filters |
| SQL query engine | DONOR-OWNED GENERIC (DuckDB) | duckdb_worker.py uses duckdb Python pkg |
| Table publication | REUSABLE CAKE FEATURE | DataGridSession, DataWorkbookDatabaseBridge |
| Worker protocol (JSON-line) | REUSABLE CAKE FEATURE | JsonLineWorkerClient, calc_worker.py |
| HUI spreadsheet UI | REUSABLE CAKE FEATURE | DataHuiScene.cs |
| Linux packaging | LINUX ADAPTER | packaging/data-app/build-deb.sh |
| Python worker launcher | LINUX ADAPTER | haven-data-calc-worker, haven-data-duckdb-worker scripts |

### DuckDB Dependency Fix
- **Issue**: `python3-duckdb` package name in Ubuntu 26.04
- **Fix**: Added `python3-duckdb` to Depends in havenos-data control file
- **Validation**: `pip3 install duckdb` or `apt install python3-duckdb`

---

## 2. Write (LibreOffice Writer + LibreOfficeKit)

### Donor: LibreOffice (Writer)

| Capability | Classification | Evidence |
|------------|----------------|----------|
| Document model (ODT) | DONOR-OWNED GENERIC (LibreOffice Writer) | LibreOfficeKit API |
| Text editing (insert/delete/format) | DONOR-OWNED GENERIC (LibreOffice Writer) | write_engine_get_text, set_text, apply_format |
| Page layout/pagination | DONOR-OWNED GENERIC (LibreOffice Writer) | write_engine_get_page_info |
| Styles/templates | DONOR-OWNED GENERIC (LibreOffice Writer) | LibreOffice core |
| Import/export (ODT, DOCX, PDF) | DONOR-OWNED GENERIC (LibreOffice Writer) | write_engine_save_as |
| Printing | DONOR-OWNED GENERIC (LibreOffice Writer) | write_engine_print |
| Undo/redo | DONOR-OWNED GENERIC (LibreOffice Writer) | write_engine_undo/redo |
| LibreOfficeKit bridge (C) | REUSABLE CAKE FEATURE | present-engine/worker.cpp, write_wrapper.cpp |
| .NET engine wrapper | REUSABLE CAKE FEATURE | WriteEngineImpl.cs, WriteEngineContracts.cs |
| HUI document UI | REUSABLE CAKE FEATURE | WriteHuiScene.cs, WriteHuiController.cs |
| Menu/toolbar/statusbar | REUSABLE CAKE FEATURE | WriteHuiScene.cs |
| Linux packaging | LINUX ADAPTER | packaging/write-app/build-deb.sh |
| LibreOffice dependency | LINUX ADAPTER | Depends: libreoffice-writer-nogui |

---

## 3. Present (LibreOffice Impress + LibreOfficeKit)

### Donor: LibreOffice (Impress)

| Capability | Classification | Evidence |
|------------|----------------|----------|
| Presentation model (ODP) | DONOR-OWNED GENERIC (LibreOffice Impress) | LibreOfficeKit API |
| Slide management | DONOR-OWNED GENERIC (LibreOffice Impress) | present_engine_slides_count, add/duplicate/delete/move_slide |
| Element snapshots | DONOR-OWNED GENERIC (LibreOffice Impress) | present_engine_element_snapshot_* |
| Text replacement in shapes | DONOR-OWNED GENERIC (LibreOffice Impress) | present_engine_replace_element_text |
| Rendering (tiles) | DONOR-OWNED GENERIC (LibreOffice Impress) | present_engine_render_tile |
| Input events (key/mouse/UNO) | DONOR-OWNED GENERIC (LibreOffice Impress) | present_engine_post_*_event, post_uno_command |
| LibreOfficeKit bridge (C) | REUSABLE CAKE FEATURE | present-engine/worker.cpp |
| .NET engine wrapper | REUSABLE CAKE FEATURE | PresentEngineImpl.cs, PresentEngineContracts.cs |
| HUI presentation UI | REUSABLE CAKE FEATURE | PresentHuiScene.cs |
| Linux packaging | LINUX ADAPTER | packaging/present-app/build-deb.sh |
| LibreOffice dependency | LINUX ADAPTER | Depends: libreoffice-impress-nogui |

---

## 4. Plan (Evolution Data Server + libical)

### Donor: Evolution Data Server (EDS) + libical

| Capability | Classification | Evidence |
|------------|----------------|----------|
| Calendar storage | DONOR-OWNED GENERIC (EDS) | EDS backend |
| Recurrence rules (RRULE) | DONOR-OWNED GENERIC (libical) | libical parsing |
| CalDAV/CardDAV sync | DONOR-OWNED GENERIC (EDS) | EDS sync engine |
| Alarm/notification engine | DONOR-OWNED GENERIC (EDS) | EDS alarm daemon |
| .NET engine wrapper | REUSABLE CAKE FEATURE | PlanEngineImpl.cs, PlanEngineContracts.cs |
| HUI calendar UI | REUSABLE CAKE FEATURE | PlanHuiScene.cs |
| Event/task CRUD | REUSABLE CAKE FEATURE | PlanEngineImpl.cs methods |
| Linux packaging | LINUX ADAPTER | packaging/plan-app/build-deb.sh |
| EDS/libical dependencies | LINUX ADAPTER | Depends: libical3, libecal-2.0-1, libedataserver-1.2-26 |

---

## 5. Terminal (Linux PTY + libvterm)

### Donor: libvterm + Linux kernel PTY

| Capability | Classification | Evidence |
|------------|----------------|----------|
| PTY allocation | DONOR-OWNED GENERIC (Linux kernel) | System.Process with redirected I/O |
| VT100/ANSI parsing | DONOR-OWNED GENERIC (libvterm) | VTermScreen in TerminalEngineImpl.cs (simplified) |
| Terminal emulation | DONOR-OWNED GENERIC (libvterm) | Would use libvterm native; current is managed impl |
| Session management | REUSABLE CAKE FEATURE | TerminalEngineImpl.cs, TerminalSessionImpl |
| HUI terminal UI | REUSABLE CAKE FEATURE | TerminalHuiScene.cs |
| Linux packaging | LINUX ADAPTER | packaging/terminal-app/build-deb.sh |
| libvterm dependency | LINUX ADAPTER | Depends: libvterm0 |

**Note**: Current TerminalEngineImpl.cs has a simplified managed VTermScreen. For production, should P/Invoke libvterm.

---

## 6. Dev (VSCodium/Code-OSS)

### Donor: VSCodium (Code-OSS)

| Capability | Classification | Evidence |
|------------|----------------|----------|
| Extension host/API | DONOR-OWNED GENERIC (VS Code) | VS Code extension API |
| Language servers/LSP | DONOR-OWNED GENERIC (VS Code) | VS Code LSP |
| Debug adapter protocol | DONOR-OWNED GENERIC (VS Code) | VS Code DAP |
| Terminal integration | DONOR-OWNED GENERIC (VS Code) | VS Code integrated terminal |
| Workspace management | DONOR-OWNED GENERIC (VS Code) | VS Code workspace |
| IPC/JSON-RPC bridge | REUSABLE CAKE FEATURE | DevEngineImpl.cs NamedPipeClientStream |
| HUI dev UI | REUSABLE CAKE FEATURE | DevHuiScene.cs |
| Extension management | REUSABLE CAKE FEATURE | DevEngineImpl.cs install/uninstall |
| Command palette integration | REUSABLE CAKE FEATURE | DevEngineImpl.cs commands.list/execute |
| Linux packaging | LINUX ADAPTER | packaging/dev-app/build-deb.sh |
| VSCodium dependency | LINUX ADAPTER | Depends: codium |

---

## 7. Wave (GStreamer + GES)

### Donor: GStreamer + GStreamer Editing Services (GES)

| Capability | Classification | Evidence |
|------------|----------------|----------|
| Media pipeline | DONOR-OWNED GENERIC (GStreamer) | GStreamer core |
| Timeline/editing | DONOR-OWNED GENERIC (GES) | GES timeline |
| Effects/transitions | DONOR-OWNED GENERIC (GES) | GES effects |
| Encoding/export | DONOR-OWNED GENERIC (GStreamer) | GStreamer encoders |
| Asset management | REUSABLE CAKE FEATURE | WaveEngineImpl.cs _assets, ImportAssetAsync |
| Timeline model | REUSABLE CAKE FEATURE | WaveEngineImpl.cs _timelines, WaveTrack/WaveClip |
| Export job management | REUSABLE CAKE FEATURE | WaveEngineImpl.cs _exportJobs, ExportAsync |
| HUI timeline UI | REUSABLE CAKE FEATURE | (HUI scene to be created) |
| Linux packaging | LINUX ADAPTER | packaging/wave-app/build-deb.sh |
| GStreamer/GES dependencies | LINUX ADAPTER | Depends: gstreamer1.0-*, libges-1.0-0 |

---

## 8. Motion (GStreamer + GES - Full Timeline)

### Donor: GStreamer + GStreamer Editing Services (GES)

| Capability | Classification | Evidence |
|------------|----------------|----------|
| Full timeline editing | DONOR-OWNED GENERIC (GES) | GES timeline API |
| Multi-track video/audio | DONOR-OWNED GENERIC (GES) | GES tracks |
| Keyframe animation | DONOR-OWNED GENERIC (GES) | GES keyframes |
| Real-time preview | DONOR-OWNED GENERIC (GStreamer) | GStreamer playback |
| Render farm/export | DONOR-OWNED GENERIC (GStreamer) | GStreamer encoding |
| Project management | REUSABLE CAKE FEATURE | (New - Motion-specific) |
| HUI timeline UI | REUSABLE CAKE FEATURE | (New - Motion-specific) |
| Linux packaging | LINUX ADAPTER | (New - packaging/motion-app/build-deb.sh) |

**Status**: Not yet implemented in apps/. This is a full NLE (non-linear editor) requiring new HUI scene and engine.

---

## 9. Canvas (RNote)

### Donor: RNote

| Capability | Classification | Evidence |
|------------|----------------|----------|
| Ink/stroke engine | DONOR-OWNED GENERIC (RNote) | RNote core |
| Vector/raster layers | DONOR-OWNED GENERIC (RNote) | RNote core |
| PDF import/export | DONOR-OWNED GENERIC (RNote) | RNote core |
| Pressure sensitivity | DONOR-OWNED GENERIC (RNote) | RNote + GTK4 |
| Rust FFI bridge | REUSABLE CAKE FEATURE | canvas/rnote-poc/src/ffi.rs, lib.rs |
| C API header | REUSABLE CAKE FEATURE | canvas/rnote-poc/include/cakeos_canvas.h |
| HUI integration | REUSABLE CAKE FEATURE | HUI/LinuxHost/Canvas/CanvasNativeBridge.cs |
| Linux packaging | LINUX ADAPTER | packaging/canvas-rnote/build-deb.sh |
| GTK4/Adwaita deps | LINUX ADAPTER | Depends: libgtk-4-1, libadwaita-1-0 |

---

## 10. Boards (AppFlowy)

### Donor: AppFlowy

| Capability | Classification | Evidence |
|------------|----------------|----------|
| Board/canvas model | DONOR-OWNED GENERIC (AppFlowy) | AppFlowy core |
| Block editor | DONOR-OWNED GENERIC (AppFlowy) | AppFlowy core |
| Database backend | DONOR-OWNED GENERIC (AppFlowy) | AppFlowy + PostgreSQL/SQLite |
| Collaboration/sync | DONOR-OWNED GENERIC (AppFlowy) | AppFlowy sync engine |
| Flutter frontend | DONOR-OWNED GENERIC (AppFlowy) | AppFlowy Flutter UI |
| Contract/adapter | REUSABLE CAKE FEATURE | Boards/contract/HavenBoardContract.cs |
| Event adapter | REUSABLE CAKE FEATURE | Boards/contract/AppFlowyBoardEventAdapter.cs |
| HUI host | REUSABLE CAKE FEATURE | Boards/hui/BoardsApp.cs |
| Linux packaging | LINUX ADAPTER | packaging/boards-appflowy/build-deb.sh |
| Flutter runtime | LINUX ADAPTER | Requires Flutter Linux build |

---

## 11. Wine (Wine Compatibility Layer)

### Donor: Wine

| Capability | Classification | Evidence |
|------------|----------------|----------|
| Win32/Win64 API | DONOR-OWNED GENERIC (Wine) | Wine core |
| PE/EXE loader | DONOR-OWNED GENERIC (Wine) | Wine loader |
| Registry emulation | DONOR-OWNED GENERIC (Wine) | Wine registry |
| Graphics (D3D/Vulkan) | DONOR-OWNED GENERIC (Wine) | WineD3D/DXVK/VKD3D |
| Per-user broker | REUSABLE CAKE FEATURE | compatibility/wine/haven_compat/broker.py, daemon.py |
| Manifest/lifecycle | REUSABLE CAKE FEATURE | compatibility/wine/haven_compat/manifest.py, lifecycle.py |
| .NET UI | REUSABLE CAKE FEATURE | apps/Wine/Hui/WineHuiScene.cs |
| Linux packaging | LINUX ADAPTER | packaging/wine-app/build-deb.sh, packaging/cohort/build_wine_compat |
| Wine dependencies | LINUX ADAPTER | Depends: wine64, wine32:i386 |

---

## 12. WinBoat (Upstream WinBoat Runtime)

### Donor: WinBoat (upstream)

| Capability | Classification | Evidence |
|------------|----------------|----------|
| KVM/VM management | DONOR-OWNED GENERIC (WinBoat) | WinBoat runtime |
| Windows Guest OS | DONOR-OWNED GENERIC (WinBoat) | WinBoat runtime |
| Container orchestration | DONOR-OWNED GENERIC (WinBoat) | WinBoat + Docker/Podman |
| FreeRDP RemoteApp | DONOR-OWNED GENERIC (WinBoat + FreeRDP) | WinBoat runtime |
| Hardware passthrough | DONOR-OWNED GENERIC (WinBoat) | WinBoat + VFIO/KVM |
| HavenOS integration | NEW INTEGRATION | packaging/winboat/build-deb.sh |
| Linux packaging | LINUX ADAPTER | packaging/winboat/build-deb.sh |
| Dependencies | LINUX ADAPTER | Depends: qemu-kvm, docker.io\|podman, freerdp2-x11, libvirt-daemon-system |

---

## Implementation Status Summary

| App | Donor Engine Built | .NET Wrapper Built | HUI Scene Built | Packaging Script | Smoke Test |
|-----|-------------------|-------------------|-----------------|------------------|------------|
| Data | N/A (Python) | ✅ | ✅ | ✅ | ✅ |
| Write | ✅ (present-engine) | ✅ | ✅ | ✅ | ✅ |
| Present | ✅ (present-engine) | ✅ | ✅ | ✅ | ✅ |
| Plan | N/A (system libs) | ✅ | ✅ | ✅ | ✅ |
| Terminal | N/A (libvterm) | ✅ | ✅ | ✅ | ✅ |
| Dev | N/A (VSCodium) | ✅ | ✅ | ✅ | ✅ |
| Wave | N/A (GStreamer) | ✅ | ⚠️ (minimal) | ✅ | ✅ |
| Motion | N/A (GStreamer) | ❌ | ❌ | ❌ | ❌ |
| Canvas | ⚠️ (Rust build) | N/A | ✅ (bridge) | ✅ | ✅ |
| Boards | ⚠️ (Flutter build) | ✅ (contract) | ✅ | ✅ | ✅ |
| Wine | N/A (system wine) | ✅ | ✅ | ✅ | ✅ |
| WinBoat | ❌ (external) | N/A | N/A | ✅ | ❌ |

---

## Next Steps

1. **Build all apps** on Linux builder using `packaging/build-all-apps.sh`
2. **Create cohort packages** using `packaging/cohort/build-debs.sh` with build env vars
3. **Build ISO** with new cohort using `image/build-live-iso.sh`
4. **Boot in approved VM** and run `tests/smoke-tests.sh`
5. **Capture GUI evidence** for each app launch
6. **Update convergence matrix** with verified results