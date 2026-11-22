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

The same `Haven.dll` ran in an isolated Linux X11 profile. Pointer and keyboard opened the gear, Layout and Accessibility. High contrast retained the stored Blue accent, and Back from Apps immediately restored gear focus. Back from Dashboard restored gear focus when its refresh finished. The receipt preserves that timing difference and the normal window close with exit 0. [Wide Appearance](settings/native/02-appearance-wide.png) · [Narrow Layout](settings/native/05-layout-narrow.png) · [Narrow High contrast](settings/native/07-high-contrast-narrow.png) · [Native receipt](settings/native/receipt.json)

## Dashboard and New Tab identity

The Dashboard rail and the Settings Layout shortcut open `browse://home/dashboard` as its own tab. `browse://newtab` keeps its search input and draft. The genuine Browse and Home controls were exercised in four passing targeted desktop cases, including tab close/reopen, strict private and nested-route refusals, and Settings regression checks. Also ran the existing Files double-click cases. The New Tab variant failed while finding its rail before layout on both this build and the preceding Settings build. The Home variant passed.

Back from Settings now schedules rail focus when the prior section is published, ahead of any Dashboard refresh. The captured Atlas `home-dashboard` and `host-newtab` routes have independent overview and search surfaces. The route change preserves the existing dashboard widgets and New Tab action owner. Clock, presets and remaining New Tab widgets are open.

The X11 follow-up confirmed separate Dashboard and New Tab identifiers, a retained New Tab search draft through selection, resize and close, and immediate Settings gear focus after returning to the published Dashboard. It also exposed an existing shell gap. After a pointer close, Ctrl+Shift+T required first focusing the surviving tab. This is the before state for the Browse focus batch. [Dashboard at 1180 × 760](dashboard/native/04-dashboard-from-new-tab.png) · [New Tab at 720 × 520](dashboard/native/10-new-tab-draft-narrow.png) · [Native receipt](dashboard/native/receipt.json)

In the same native session, Spaces Chat retained an unsent draft through Options and Escape at 1180 × 760 and 720 × 520. Escape restored Options focus, and Enter reopened the panel. The wide global and Chat rails are wider than the Atlas `spaces-chat` rails. At 720 pixels the floating panel can cover the top chrome while keeping its commands reachable. This check used the app's local model-unavailable state. [Wide Options](dashboard/native/15-spaces-chat-options-wide.png) · [Narrow Options](dashboard/native/18-spaces-chat-options-narrow.png) · [Draft after Escape](dashboard/native/19-spaces-chat-escape-narrow.png)

## Browse tab keyboard focus

Arrow keys and Home/End move focus along the live tab titles while Enter still selects. Each tab's Close remains keyboard reachable. Close and reopen use the existing workspace commands and transfer focus to the surviving or reopened tab. A surviving address editor retains its draft and focus on background close. The shell leaves a later focus choice in place.

Thirteen focused Browse NativeUI cases passed with the production assembly `598cd7eaa7b7bc1660005004a6fa8f9c2e20581cf3cb3217bdbf59114f815ae2`. The genuine controls were exercised at 360 and 1180 pixels. The cases include physical inline Close followed immediately by Ctrl+Shift+T, keyboard Close, selected tab styling, a retained address draft and the private reopen rule. [360 × 800 Skia](tabs/reopened-tab-focus-360x800.png) · [1180 × 800 Skia](tabs/reopened-tab-focus-1180x800.png)

The CUI overflow controls transfer focus to the opposite arrow when Home or End hides the focused arrow. Seventeen owning runtime checks and eight Browse checks passed. Shift+Tab from New Tab reached the visible arrow, and End scrolled the strip while preserving the selected tab. The runtime assembly SHA-256 was `bc6771e71a972f4568d6bdb14a13cdbf04ca18ad3603b2cbe4ea9c3f147dfb31`. [Skia view after End](tabs/overflow-keyboard-end.png)

## Responsive Home and Settings

Home displays labels on the existing rail at desktop widths, with the same controls, tooltips and accessible names in compact mode. New Tab places its Search and Go controls in a fluid row above the mode and quick links. The disabled Ask AI option explains its availability and preserves the typed input. Settings keeps Back and its heading above the scrolling category list.

The Linux owning desktop build passed with zero warnings and errors. Ten genuine-control cases passed at component widths from 360 to 1440 pixels, including rail dimensions, button and keyboard reachability, the New Tab draft and the Settings Back location after opening Layout and Accessibility.

Six fixture cases passed again under Skia. The matching native X11 build confirmed labeled navigation at 1180 × 760, compact search at 720 × 520, Back visible while the Settings categories scroll, and a preserved Spaces draft after closing Dashboard. Ctrl+Shift+T reopened and focused Dashboard with no intervening pointer click. The native overflow arrows transferred focus at both endpoints while Dashboard remained selected. Normal close returned exit 0. [Wide Home](responsive/native/02a-home-wide-labels.png) · [Narrow New Tab](responsive/native/05-new-tab-narrow-draft.png) · [Skia at 360 pixels](responsive/home-newtab-360.png) · [Sticky Back](responsive/native/07-layout-sticky-back-narrow.png) · [Native reopen](responsive/native/12a-dashboard-reopened-after-publication.png) · [Native overflow](responsive/native/16-overflow-end-focus-to-left.png)

## Compact model picker and expanded settings

The top-bar model action opens a compact picker with the resolved Main model, its source, advertised reasoning and reported catalogue capacity when the owner can supply those values. More settings opens the existing route editor in the same session, and Back or Escape returns to the compact view. Save uses the existing route writer. Discard explicitly reloads saved route data. Dismissing the picker restores the model trigger.

The `host-model-picker` and `host-model-settings` comparison drove the narrower top-right compact view and the distinct expanded view. The compact backdrop is transparent. The Atlas also shows independent Main, Voice and Vision selectors and conversation context Apply. Those runtime paths are still open. The native isolated catalogue reported Qwen4 as planned, with no active model or registered fallback. The rendered owner fixture separately exercised a selected model and its reported capacity. Sixty-two Home model cases passed, including a Back-preserved draft and an explicit discard without a route write.

The final backdrop build passed six Browse cases, including the compact and expanded controls at 360 × 520, 720 × 520 and 1440 × 920. The checks cover visible catalogue copy, scroll reachability, independent Close and the shell tab-focus regression. [Wide compact](model-picker/skia/compact-model-picker-1440x920.png) · [Narrow compact](model-picker/skia/compact-model-picker-360x520.png) · [Narrow settings](model-picker/skia/expanded-model-settings-360x520.png)
