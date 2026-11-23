# Haven and Atlas v4 visual comparison

The starting screenshot came from the real Home gear in a new Linux profile on `c0d98d73711620f2cc18ad253d40e3813632e339`. The comparison pairs show the committed Atlas rendered in Chromium on the left and the running Avalonia application on X11 on the right. Both images in each pair use the same viewport and scale. The receipt records the observed preference values, native assembly digests, pixel dimensions and image hashes. [Appearance receipt](appearance-receipt.json) [Starting Appearance comparison](appearance-before.png) · [Atlas source](../../../specification/owner-references/2026-11-22-atlas-v4/README.md)

## Appearance

| Route and state | Full-resolution comparison |
| --- | --- |
| Appearance, Professional Dark, 1440 × 900 | [Atlas and native](appearance-dark-wide.png) |
| Appearance, Professional Dark, 720 × 520 | [Atlas and native](appearance-dark-narrow.png) |
| Appearance, untouched first-run profile, 1440 × 900 | [Atlas Dark and native SuperDark](appearance-first-run.png) |

The gear now opens the Theme child under Global → Appearance. Settings search retains the matching category’s parents. Enter opens the result and Escape clears the search. The browsing group leads to the native General, Bookmarks and History destinations while keeping Settings available. The selected tab and unfocused address field show the Settings title. Focusing the address field exposes the original editable address, including the pinned Home address when entered through its gear. [Search results](settings-search.png) · [Native Bookmarks destination](settings-open-bookmarks.png) · [Focused address](settings-address.png)

At 1440 × 900 the desktop Settings rail occupies 276 DIP. The theme gallery has five cards above four, with 136 DIP cards and 62 DIP previews. On the line 385 pixels from the left, the native Theme panel begins at y=219 and Display at y=603. The reference begins at y=220 and y=606 respectively. The Display rows carry their labels, descriptions and independent controls. Brightness and colour contrast retain their explicit Apply action. The Accent section saves one of four presets even when override is Off and retains existing per-app colours. The device profile remains the persistence scope. [Iris saved with override Off](accent-iris-off.png) · [Iris applied](accent-iris-on.png)

A fresh installation starts in legacy Professional SuperDark. Its Settings mode is labelled Dark. Choosing Dark explicitly through the running app selects Professional Dark. In the matched Professional Dark comparison, the canvas sample is `#17181B` and the tab-strip sample is `#212329` in both products. Fresh-profile captures preserve the darker original startup choice. At 720 × 520 the native directory stacks above the scrollable page. The desktop reference uses a fixed 1440-pixel canvas and clips when viewed at that width.

The directory still lacks much of the Atlas taxonomy, including Theme Options. The theme builder, Comic migration, custom colour input and per-app palette editor remain open. The current app exposes the existing Cinematic theme. Atlas account, model and workspace examples were kept out of user data.

## New Tab

The first comparison with working shortcut cards shows the Professional Dark New Tab at 1440 × 900 and 720 × 520 on source `fd7b0d0b8`. The clock displays local time and the date. Apps, Library and Dashboard open their existing destinations. At the narrow width the cards wrap into two rows. Scrolling and activating Dashboard opened it in a separate tab and preserved New Tab. [Wide comparison](new-tab-shortcuts-wide.png) · [Narrow comparison](new-tab-shortcuts-narrow.png) · [Wrapped cards](new-tab-narrow-scrolled.png) · [Dashboard after activation](new-tab-opened-dashboard.png) · [Capture receipt](new-tab-shortcuts-receipt.json)

The Atlas uses a wider content column, a Home heading above the clock, separate Ask AI and All apps actions, Customise and a Widgets section. The early native capture shows a New Tab heading, a search-mode selector and three shortcuts. The narrow Atlas browser clips its fixed 1440-pixel canvas.

## Verification

The Linux Release build uses .NET SDK 10.0.401. The 23 focused Desktop cases passed using the real Skia headless renderer on the source for the Settings captures, `ef50cd393d4ad00f65ec703da2c8ea3e2eb2848b`. Cases cover category focus, filtering, scroll reachability, canonical native destinations, guarded writes, the saved and pending display state, accent preservation across reopen, Dashboard identity and New Tab search. Ten focused Browse cases passed, including the model button, wide and compact row dimensions, physical close/reopen keys and overflow. The shell uses the repository’s original 9to1 mark.

Thirty of 31 broader theme tests passed. The Frosted Glass test expecting the older solid brush failed identically with the starting and updated Themes assemblies. A broader Browse fixture looks for the retired More button and failed in all four widths against both assemblies. Three Desktop capture cases require Skia and returned no image with the fast drawing stub. Windows standalone Home still has nine missing Files integration types, also observed against the handoff sources. Earlier native and cloud evidence is retained. [Previous evidence](../atlas-20261122-cloud/README.md)
