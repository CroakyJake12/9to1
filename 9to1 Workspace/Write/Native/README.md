# 9to1 Write: pinned LibreOfficeKit bridge

This additive process-local C ABI uses the actual LibreOfficeKit header and donor document pointer. It neither replaces existing service APIs nor supplies a substitute document model. It is not yet connected to the shipping Write CUI surface.

The pinned source revision is `d3fca73f81dfb19a72a0da41598b7390eaf5845b`. `build-windows.ps1` verifies that revision and unmodified upstream headers before compiling. Runtime initialisation independently compares the donor's `getVersionInfo().BuildId` with the same commit; a different release is refused. No upstream vtable offsets are reproduced by hand. Every optional function is checked before use.

```powershell
./build-windows.ps1 -DonorSource <pinned-source-checkout> -OutputDirectory <isolated-build-directory>
python ./test-abi.py <isolated-build-directory>/9to1-write-lok.dll
```

The current machine has the Visual C++ compiler, but no matching Windows LOK runtime. The source's Windows loader supports `sofficeapp.dll` and `mergedlo.dll`. A complete donor build, including its dependent libraries, fonts, filters and configuration, is required; copying a single DLL is insufficient. Build the pinned checkout in an isolated donor build workspace using its own Windows build procedure (WSL helper and Visual Studio). Do not modify the existing worker fixture WSL distribution. A downloaded stable release cannot satisfy the exact BuildId check. The official Windows build guide was access-denied during this run, so its current prerequisites have not been independently revalidated beyond the pinned README and installed toolchain inspection.

The native bridge forwards tile painting, document dimensions/pixel format, key and pointer input, IME composition, text selection, typed clipboard paste, UNO commands (including donor undo/redo), and save to the **same** live donor document. It preserves callback type/payload; command submission success is not command completion. Memory returned by LOK is freed through the donor allocator, and returned bridge copies through `nto_lok_free`.

The caller must supply owner-brokered local file URLs and an isolated profile, retain the callback delegate, copy callback data before dispatch, and run all donor calls on one dedicated engine thread. Callback processing must never re-enter the engine. The adapter currently permits one engine lifetime and one open document in its owning process; the future owner adapter must manage multiple sessions without silently replacing a draft. Close is explicit and does not save; persistence policy remains with the existing owner. Capability and permission decisions remain outside this transport.

Remaining implementation gates include the process/runtime event loop, dedicated-thread managed lifetime and crash supervision, tiled CUI Object renderer, cursor/selection overlays, input/key mapping and IME lifecycle, read-only capability guards, cancellation, structured donor command state and completion, multiple document/view handling, owner persistence publication and format metadata. The current browser Notes editor is not evidence for any of those donor paths.

Validation must distinguish:

| Evidence | Status |
| --- | --- |
| Compile bridge against pinned actual Windows donor headers | Run by build script; see generated build-evidence.json |
| Load real bridge, ABI pin, invalid argument and absent runtime refusal | Run by test-abi.py |
| Matching donor loaded and tiled document edited | NOT RUN: matching binary absent |
| Selection, clipboard, IME, undo, save and fresh-process reopen | NOT RUN |
| Shipping CUI browser/native integration | NOT RUN |
| Exhaustive donor feature inventory and acceptance | NOT RUN |

These gaps remain required work, not exceptions or acceptance waivers. Upstream integration reference: [LibreOfficeKit documentation](https://docs.libreoffice.org/libreofficekit.html); the pinned source headers are the ABI authority.
