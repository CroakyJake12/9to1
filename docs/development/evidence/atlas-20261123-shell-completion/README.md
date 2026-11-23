# Haven Settings and New Tab

The cloud checkout continued on `codex/overnight-haven-shell` from `83b330fe0778921b47e51dca19d02cf1cb5b7de4`. The starting handoff on main was `b1009894406d984dfb7c453b60a61f2422fc22b8`. All native observations used isolated Linux profiles and the real Haven apphost. Chromium rendered the repository’s pinned Atlas v4. [Owner reference](../../../specification/owner-references/2026-11-22-atlas-v4/README.md) · [Earlier native comparison](../atlas-20261123-visual-review/README.md)

## Settings and Appearance

The Home gear opens the Settings rail. The native directory resolves all 73 Atlas destinations across Global, Local, 9to1 Haven and Apps. Keyboard search for New Tab followed by Enter opens its settings. Customise opens the New Tab editor while retaining the source tab. At 720 × 520 the selected category scrolls into view when the rail changes orientation. Pages backed by native services open their existing owners. Some account, permission and app-specific editing controls still have no available service in this host.

A new profile starts in Professional Dark. The first-run native canvas sampled `#17181B` and the rail sampled `#212329`. An existing profile retained SuperDark and its original samples of `#090B0E` and `#1C1C1E`. Both applications exited normally. [Fresh launch](startup/fresh-dark-fresh.png) · [Saved SuperDark launch](startup/fresh-dark-existing.png) · [Pixel and preference receipt](startup/receipt.json)

Create theme opens the shared editor from Appearance and Theme Options. Its draft has an inherited base and local material preview. In the native app, Escape restored focus to Create theme and preserved the saved preference. Explicit Save created and activated the named theme. A full restart recovered its complete record and selected gallery card. [Native theme preview](journeys/stage5-create-theme-preview.png) · [Escape and focus](journeys/stage5-theme-cancel-focus.png) · [Selected theme after restart](journeys/stage5-theme-save-and-relaunch.png)

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
| Create theme | [Wide](comparisons/create-theme-wide.png) | [Narrow](comparisons/create-theme-narrow.png) |

## Matched New Tab routes

| Route | 1440 × 900 | 720 × 520 |
| --- | --- | --- |
| New Tab | [Wide](comparisons/new-tab-wide.png) | [Narrow](comparisons/new-tab-narrow.png) |
| Customise | [Wide](comparisons/customise-wide.png) | [Narrow](comparisons/customise-narrow.png) |
| Add widgets | [Wide](comparisons/widgets-wide.png) | [Narrow](comparisons/widgets-narrow.png) |

At 1440 × 900 both Customise panels measure 280 pixels tall. Add widget and Done share the heading row. The native catalogue has three preview columns at the wide width and two scrollable columns at 720 pixels. The reference thumbnails illustrate populated documents and calendars. The native thumbnails label the actual source type, and placed cards show the profile’s available data. A real GTK file choice previewed the image before any layout write. Explicit Use background recorded its profile-scoped content hash. [Native upload and widget receipts](journeys/upload-and-widget-journeys.json)

## Ask AI and tab recovery

On a fresh Linux profile, Ask AI kept the original New Tab open and displayed the exact typed prompt in the Spaces composer. Returning to New Tab recovered the same text. Returning to Spaces recovered the same draft. The database contained one conversation, one current branch and one composer draft. Message, turn and model usage counts remained zero throughout. [Spaces composer](journeys/stage10-ask-ai-spaces-draft.png) · [Original New Tab](journeys/stage10-ask-ai-source-recovered.png) · [Profile, SQLite and window receipts](journeys/refined-native-journeys.json)
