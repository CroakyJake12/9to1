# Linux x64 desktop test delivery — 26 October 2026

Owner-requested test delivery, separate from the ARM release pipeline and paused independent acceptance.

- Application/package source: `59d664964649c04661eea87921ddad3bf0db1f74` on `codex/desktop-recordings-recovery-20261026` ([PR #38](https://github.com/CroakyJake12/9to1/pull/38)). Later documentation commits do not change this binary identity.
- Package: `haven_0.1.0+test20261026.git59d66496_amd64.deb`, 82,553,588 bytes.
- SHA-256: `9b04119adc3d21c2311887cc348d952d47c694802c1c95bc858f52c8fdfa08d6`.
- Delivered to the owner's existing private OneDrive project folder. The existing ledger retains item identity, permission observation and upload/checksum receipts. No public installer or sharing link was created.

The normal `Haven.Desktop.csproj` publish targets `net10.0`, `linux-x64`, self-contained, single SDK allocation, normal dependencies and CUI compilation. The build-only CUI task reference now clears application RID/deployment/platform properties and compiles for its AnyCPU SDK host; this fixes the reproduced CS8012 publish failure without bypassing CUI. Two resource-guard stops remain recorded. Byte-verified duplicate generated outputs were consolidated within their private build closure; no source/evidence paths were deleted and no guard was lowered.

The existing `shared/scripts/linux/package-haven.sh` now validates the x64 self-contained payload, version, output location and overwrite protection, declares Linux dependencies and normalizes installed-file access under a private umask. Package identity and launcher remain `haven`; no removal or migration hooks are added. Compression used one thread and the source commit timestamp.

## Actual evidence

| Check | Result |
|---|---|
| Small ELF packaging contract fixtures | 7/7 PASS; fixtures are not native app acceptance |
| Full self-contained publish | PASS, 97.284 seconds |
| Package control/architecture and archive verification | PASS; 409 archive files, including all 407 published payload files |
| Packaged apphost on isolated native X11 | PASS; welcome rendered, actual main window normal close, exit 0; guarded duration 8.040 seconds |
| OneDrive readback | Exact size and locally computed QuickXorHash match uploaded item; SHA-256 retained separately |

The initial shutdown probes selected helper/temporary windows. Their failed receipts and exact scripts remain preserved. The final probe reads `_NET_WM_NAME`, selects the actual 9to1 main window and retains the same exit-zero assertion. The packaged application was not changed between probes.

## Limits and next work

This is not an accepted release or proof of installation on the owner's desktop. GNOME/Wayland, returning-session data, actual browser/provider use, gated HF downloads and the recording workflows still need native verification. A legacy hover test remains failing; see [the current recovery matrix](desktop-recordings-2026-10-26.md).

The managed publish does not bundle Chromium, a verified x64 Thunder engine/default model, or the native Rnote bridge. Browser use requires supported installed Chromium; local chat requires a usable Dulche runtime/model. Full Canvas drawing cannot be validated from this package alone; Canvas SVG rasterization also depends on system librsvg/Cairo/GLib. Preserve these as explicit dependency/implementation work, not successful acceptance.

The owner can use the existing swap workflow and `sudo apt install ./haven_0.1.0+test20261026.git59d66496_amd64.deb`. The package supplies .NET; no SDK is needed. The separate owner uninstaller determines any extra data-removal behavior. Keep OneDrive delivery private and publish source changes normally.
