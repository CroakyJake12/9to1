# Write native LibreOfficeKit bridge

The process-local C ABI uses the actual LibreOfficeKit header and donor document pointer. The Windows build verifies the pinned upstream headers and source revision `d3fca73f81dfb19a72a0da41598b7390eaf5845b`. Startup compares LibreOfficeKit’s reported BuildId to that identity. Check optional functions before use.

Keep native handle ownership and release in the bridge. Verify ABI compatibility and a real document operation before connecting the bridge to a host. An engine result and a successful round-trip through the user-facing Write editor are separate observations.

```powershell
./build-windows.ps1 -DonorSource <pinned-source-checkout> -OutputDirectory <isolated-build-directory>
python ./test-abi.py <isolated-build-directory>/9to1-write-lok.dll
```

## ABI check boundaries

The public bridge obtains optional functions from the upstream LibreOfficeKit header and document vtable rather than copying offsets into a second declaration. Handle a missing symbol or mismatched build cleanly. Keep the document pointer and exported buffer under their defined owners throughout close. `test-abi.py` checks the local interface; shipping Write still needs its actual host and engine together.

[C ABI](9to1_write_lok.h) · [Windows build](build-windows.ps1) · [ABI test](test-abi.py) · [Write host](../README.md)
