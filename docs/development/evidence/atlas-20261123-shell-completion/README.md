# Haven shell, Settings, Dashboard and New Tab

The cloud checkout continued on `codex/overnight-haven-shell` from `83b330fe0778921b47e51dca19d02cf1cb5b7de4`. The starting handoff on main was `b1009894406d984dfb7c453b60a61f2422fc22b8`. All native observations used isolated Linux profiles and the real Haven apphost. Chromium rendered the repository’s pinned Atlas v4. [Owner reference](../../../specification/owner-references/2026-11-22-atlas-v4/README.md) · [Earlier native comparison](../atlas-20261123-visual-review/README.md)

## Settings and Appearance

The Home gear opens the Settings rail. The native directory resolves all 73 Atlas destinations across Global, Local, 9to1 Haven and Apps. Keyboard search for New Tab followed by Enter opens its settings. Customise opens the New Tab editor while retaining the source tab. At 720 × 520 the selected category scrolls into view when the rail changes orientation. Pages backed by native services open their existing owners. Some account, permission and app-specific editing controls still have no available service in this host.

A new profile starts in Professional Dark. The first-run native canvas sampled `#17181B` and the rail sampled `#212329`. An existing profile retained SuperDark and its original samples of `#090B0E` and `#1C1C1E`. Both applications exited normally. [Fresh launch](startup/fresh-dark-fresh.png) · [Saved SuperDark launch](startup/fresh-dark-existing.png) · [Pixel and preference receipt](startup/receipt.json)

Create theme opens the shared editor from Appearance and Theme Options. Its draft has an inherited base and local material preview. In the native app, Escape restored focus to Create theme and preserved the saved preference. Explicit Save created and activated the named theme. A full restart recovered its complete record and selected gallery card. [Native theme preview](journeys/stage5-create-theme-preview.png) · [Escape and focus](journeys/stage5-theme-cancel-focus.png) · [Selected theme after restart](journeys/stage5-theme-save-and-relaunch.png)

Entering `browse://home/settings/custom-theme` in the address bar now opens Create theme directly. At 1440 pixels the checked Save theme button sits beside the address field. At 720 pixels it is the first item in the profile menu. Clicking it there saved and activated the theme, and restarting the same profile recovered the identical stored theme. At 1440 pixels the native heading begins at x=331, compared with x=329 in Atlas. [Save in the compact menu](journeys/create-theme-narrow-save-menu.png) · [Native actions, profile readback and image hashes](journeys/creator-toolbar-journeys.json)

## New Tab

Focus, Balanced and More widgets use a layout scoped to the current profile. Changing a preset and background survived a full restart. A separate in-session keyboard check recovered the exact unsent search text after closing Customise. The Linux Upload button opened the native file chooser, and Escape returned to the editor. [Compact background choice](journeys/stage5-customise-horizon-narrow.png) · [Layout after restart](journeys/stage5-new-tab-after-relaunch.png) · [Retained search text](journeys/stage5-new-tab-draft-after-escape.png)

Apps, Library and Dashboard are the three initial shortcuts. In the native app, adding a fourth shortcut to Settings survived a full restart. Moving and renaming it kept the same placement ID and destination. Removing it restored the original three in order. Every other stored layout field stayed equal across those edits. Attempting to add Dashboard a second time showed validation before the first layout write. [After restart](journeys/stage11-shortcut-relaunch.png) · [After moving and renaming](journeys/stage11-shortcut-move-rename.png) · [After removal](journeys/stage11-shortcut-remove.png) · [Stored revisions and image hashes](journeys/shortcut-lifecycle.json)

Early integrated launches exposed a gear crash, colliding control IDs in Customise, and a widget picker action that initially still addressed the older inline UI. Separate private profiles retained those failures and the successful follow-up journeys. Only the early failed profile needed forced shutdown. Both subsequent Settings and custom-theme sessions and their relaunches closed normally. [Intermediate runs and image hashes](journeys/intermediate-journeys.json)

## Comic

The ninth built-in uses Comic surfaces and control feedback. An existing profile with a saved Cinematic selection displayed Comic on first opening the gallery. Reading the profile preserved the original stored name. Explicitly selecting Comic recorded both the new identifier and the original Cinematic name. Exact custom-theme identities were also exercised through the native owner. [Native comparison](comparisons/comic-appearance-wide.png) · [Migration receipt](appearance/comic-migration.json)

The shared CUI renderer exercised the Comic primary, disabled, hovered and pressed controls in Dark, Light and High contrast. The illustrated Local versus Local + Cloud onboarding screen still needs a native owner for its provider and routing choices. Current onboarding handles account access and local model eligibility. [Dark](appearance/Comic-dark.png) · [Light](appearance/Comic-light.png) · [High contrast](appearance/Comic-high-contrast.png)

## Matched Settings routes

Each comparison shows the pinned Atlas on the left and the running Haven application on the right at the same viewport. The 720-pixel Atlas can clip its fixed desktop canvas. Haven gives the rail and content separate vertical scrolling at that width. The Atlas creator example uses an authored gradient. The captured native creator starts from its actual solid default. The combined PNGs are stored here. The receipt identifies the original cloud captures and the native assemblies used for each pair. [Rendered image and assembly hashes](comparisons/receipt.json)

| Route | 1440 × 900 | 720 × 520 |
| --- | --- | --- |
| Settings → General | [Wide](comparisons/settings-general-wide.png) | [Narrow](comparisons/settings-general-narrow.png) |
| Settings → Appearance | [Wide](comparisons/appearance-wide.png) | [Narrow](comparisons/appearance-narrow.png) |
| Accent palette | [Wide](comparisons/accent-palette-wide.png) | [Narrow](comparisons/accent-palette-narrow.png) |
| Global → Appearance → Layout | [Wide](comparisons/settings-layout-wide.png) | [Narrow](comparisons/settings-layout-narrow.png) |
| Create theme | [Wide](comparisons/create-theme-wide.png) | [Narrow](comparisons/create-theme-narrow.png) |

Layout opens Dashboard, the New Tab editor and Accessibility. Following Customise on the New Tab row opened the editor in its own tab and retained Layout. The global chat-bar position and interface-scale preferences still lack native owners. The current Accessibility destination controls high contrast and reduced motion.

In Accent palette, the native panel and the Atlas panel share the same horizontal bounds at 1440 pixels. The native capture shows the profile’s actual saved-colour state. Following Back to appearance returned focus to Theme, and the original New Tab still had Customise open. The same colour controls serve Theme Options and the direct palette address. [Action and profile receipt](journeys/registered-shortcut-journey.json)

## Matched New Tab routes

| Route | 1440 × 900 | 720 × 520 |
| --- | --- | --- |
| New Tab | [Wide](comparisons/new-tab-wide.png) | [Narrow](comparisons/new-tab-narrow.png) |
| Customise → Add shortcut | [Wide](comparisons/add-shortcuts-wide.png) | [Narrow](comparisons/add-shortcuts-narrow.png) |
| Customise | [Wide](comparisons/customise-wide.png) | [Narrow](comparisons/customise-narrow.png) |
| Add widgets | [Wide](comparisons/widgets-wide.png) | [Narrow](comparisons/widgets-narrow.png) |

At 1440 × 900 both Customise panels measure 280 pixels tall. Add widget and Done share the heading row. The native catalogue has three preview columns at the wide width and two scrollable columns at 720 pixels. The reference thumbnails illustrate populated documents and calendars. The native thumbnails label the actual source type, and placed cards show the profile’s available data. A real GTK file choice previewed the image before any layout write. Explicit Use background recorded its profile-scoped content hash. [Native upload and widget receipts](journeys/upload-and-widget-journeys.json)

Add shortcuts opens a searchable grid. Existing pins show a check. At 720 pixels the grid has two columns. Typing Boards and pressing Tab then Enter pinned the registered app. After restart, the tile opened the real Boards home at `9to1://boards`. Custom address accepts a validated web address and Cancel restores the picker filter. [Native input, stored identity and restart](journeys/registered-shortcut-journey.json)

The edit-mode grip moved Boards from fourth to first at 1440 pixels. At 720 pixels, Escape during a drag preserved the exact stored layout. A completed drag and Alt+Left then moved the same Boards pin to third. Restarting Haven recovered Apps, Library, Boards and Dashboard in that order. [Keyboard focus after moving Boards](journeys/shortcut-grip-keyboard-720.png) · [Restored order after restart](journeys/stage16-grip-keyboard-order-relaunch.png) · [Drag operations, hashes and stored revisions](journeys/final-home-drag-and-surfaces.json)

## Dashboard

An uncustomised Dashboard opens with one Dashboard heading, Continue working, Today and Study this week. The native comparison uses an empty profile. Today reads the profile’s agenda and Study reads recorded lesson time. View files, Open Tasks and View progress opened the existing Library, Tasks Space and Study Space. Device and model controls remain reachable below the activity section. A profile with its own Dashboard layout keeps its saved arrangement. The host has no cross-app list of recently opened documents, so Continue working links to Library. [Dashboard at 1440 × 900](comparisons/dashboard-wide.png) · [Dashboard at 720 × 520](comparisons/dashboard-narrow.png) · [Native Dashboard, Layout and Accent input receipts](journeys/layout-palette-dashboard.json)

## Ask AI and tab recovery

On a fresh Linux profile, Ask AI kept the original New Tab open and displayed the exact typed prompt in the Spaces composer. Returning to New Tab recovered the same text. Returning to Spaces recovered the same draft. The database contained one conversation, one current branch and one composer draft. Message, turn and model usage counts remained zero throughout. [Spaces composer](journeys/stage10-ask-ai-spaces-draft.png) · [Original New Tab](journeys/stage10-ask-ai-source-recovered.png) · [Profile, SQLite and window receipts](journeys/refined-native-journeys.json)

## Verification

The final Linux Release build completed with zero warnings and zero errors. All 87 focused Desktop cases passed with Skia. The 20 Browse keyboard and overflow cases and 15 infrastructure persistence cases also passed. The comparison receipts identify the renderer, source and viewport for each image. [Test cases, filters and binary hashes](verification.json)

## Cloud checkout and continuation

The handoff on `main` was `b1009894406d984dfb7c453b60a61f2422fc22b8`. The final implementation commit on `codex/overnight-haven-shell` is `9d332d71bf5055d00e05e433bf96d0ed85c1b796`. Later evidence commits can be picked up from that same branch. Codex allowance telemetry was unavailable. Each reviewed batch was pushed through the cloud checkout and checked against the remote branch.

The real application owns the profile state. The remaining Atlas differences that require additional implementation include an authorised cross-app recent-document source, file or artifact selection and icon editing for shortcuts, global chat-bar position and interface scale, and the Local versus Local + Cloud onboarding choice. The current Settings catalogue identifies prerequisites for unavailable account, policy and app-management services. Verification used Linux x64, Skia and a private X11 display. The original local pass and its evidence remain in the repository. [Feature inventory](../../refactor-2026-11-18/feature-inventory.md) · [Cloud handoff](../../CLOUD-HANDOFF-2026-11-22.md)
