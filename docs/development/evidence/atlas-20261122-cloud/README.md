# Haven cloud continuation · 22 November 2026

The cloud checkout started clean on `work` at `81400baec8add3db82e6b0c2dd8c20d681df5b83`. That branch is an ancestor of the handoff and was retained. The overnight branch `codex/overnight-haven-shell` was created and published from `main` at `b1009894406d984dfb7c453b60a61f2422fc22b8`. `git ls-remote` confirmed the initial push. The live Codex allowance was unavailable, so each reviewed batch is pushed as it lands.

The previous native implementation is `5e0178908cd2854875fc8a767b84f6a848cdb38d`. All 29 file sizes and hashes in the private comparison archive match its manifest. The archive SHA-256 is `b2419affcafe0137954ef26d4e81d4fbad8bf149342edf24dcf591947c043532`. The committed Atlas HTML SHA-256 is `612a9aa9fdc01b1c8a71e82532b191614992e1a6b78602fe5100aee9fa8bf4d2`. The archive and the previous pass records remain in their original locations. [Previous pass](../../../../work/coordination/atlas-20261122/PASS_STATE.md)

## Settings navigation

The Home gear opens the supported part of the Atlas Settings hierarchy. Global contains Appearance with Theme, Layout and Accessibility. Theme and Accessibility reveal their existing controls. Layout links to Dashboard customisation and reloads saved device appearance through its existing preference owner. Back returns to the prior Home section. At compact widths the categories scroll above the content, with keyboard movement independent from selection.

The Atlas `appearance`, `appearance-palette` and `settings-layout` routes were rendered in Chromium. Its fixed 1440 × 900 desktop canvas clips at narrow viewport widths. The previous native Appearance capture has the icon-only Home rail beside the gallery. The Settings view uses that space for readable category controls. The supported routes keep the same appearance preference scope, draft and write owner. The rest of the Settings taxonomy, search, accent palette, theme editor and global layout options still need implementation.

Linux Release `net10.0` with SDK `10.0.401` passed all 11 focused `UnifiedHomeContentTests.Settings_` and `Configured_Home_Settings` cases. These exercise the rendered buttons, keyboard navigation, scroll reachability, a saved theme, the uncommitted brightness draft, explicit reload, and the existing access and write lifecycle. The initial test host tried a read-only default configuration path. Subsequent runs used precreated, private `HAVEN_DATA_DIR` and XDG directories.

Both interaction cases passed again with headless Skia enabled on `53f68b49bf317ea9d47087e0358e8ed59ac47843`. The 1280 × 900 Professional Dark view preserves the reference's theme gallery and separates the active category from plain resting rows. At 520 × 520 the orange-accent view stacks two scroll areas. Scrolling the categories can take Back to Home out of view. Each adjacent JSON receipt records the viewport, actual fixture appearance and accent, and image and assembly hashes.

| Surface | Wide | Narrow |
| --- | --- | --- |
| Appearance | [1280 × 900 · Professional / Blue](settings/home-settings-appearance-1280x900.png) | [520 × 520 · Professional / Orange](settings/home-settings-appearance-520x520.png) |
| Layout | [1280 × 900 · Glow / Blue](settings/home-settings-layout-1280x900.png) | [520 × 520 · Professional / Orange](settings/home-settings-layout-520x520.png) |
