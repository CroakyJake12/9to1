# Haven Accessibility and keyboard controls

## Home gear, Layout and Accessibility

One primary click on the Home gear opened Appearance on Linux. In Layout, opening Interface scale reached the dedicated Accessibility page at both widths. Its selector previews 100%, 125%, 150% or 175%. A saved 125% reopened at that size in a new process. At 720 × 520, the 175% preview kept Revert and Apply reachable by Tab. Closing the window with that preview open preserved the saved 125%. [Gear before](native/home-gear-before.png) · [Appearance after one click](native/home-gear-opens-appearance.png) · [125% after restart](native/125-after-relaunch.png) · [Revert focused](native/scale-175-focus-revert.png) · [Apply focused](native/scale-175-focus-apply.png)

| Atlas route | 1440 × 900 | 720 × 520 |
| --- | --- | --- |
| Layout | [Wide](native/settings-layout-1440x900.png) | [Narrow](native/settings-layout-720x520.png) |
| Accessibility | [Wide](native/settings-accessibility-1440x900.png) | [Narrow](native/settings-accessibility-720x520.png) |

Each image pairs the pinned Atlas route with a real Haven window at the same viewport. The native profile determines selection and switch state. The recorded High contrast pointer action and Reduced motion Space action each advanced the profile revision once. [Runtime hashes and native receipts](native/native-scale-and-shortcut.json) · [Switch gestures](native/switch-gestures.jsonl)

## Dashboard and New Tab

At a fixed 1024 × 900, a 150% preview stacked Today and Study and resized the existing widget cards. Revert returned identical pixels. The widget’s text, placement and saved profile survived the preview. [100%](dashboard/dashboard-100.png) · [150%](dashboard/dashboard-150.png)

The New Tab tile menu offers Open, Open in new tab, Rename shortcut, Change icon, Move and Remove shortcut. Owning Skia tests exercised right-click, the Menu key, Shift+F10, Escape, and guarded removal at both widths. The X11 icon captures show an earlier build saving Bookmark on the registered Boards shortcut while retaining its identity. That build lost Escape focus after pointer selection. The later focused tests cover the correction. The pinned Atlas specifies Change icon without supplying an editable palette screen. [Saved icon](native/icon-bookmark-saved-earlier-build.png) · [Earlier Escape failure](native/icon-immediate-escape-earlier-build-failure.png)

## Keyboard reference

From the Home gear, Settings search opens Keyboard Shortcuts, Navigation and Menus & focus. A disposable Linux profile retained byte-identical preferences throughout the two keyboard tests. The key labels describe the current default controls. Editing assignments requires a shortcut preference owner.

Each comparison pairs the committed Atlas HTML in Chromium with the running Linux Haven window on X11. At 720 × 520, the native category directory sits above a scrollable content pane. The Atlas retains its desktop canvas at that viewport. The captures use the keyboard change built separately from its verified parent. The receipt identifies the exact executable and frames. [Capture receipt](keyboard/receipt.json)

| Atlas route | 1440 × 900 | 720 × 520 |
| --- | --- | --- |
| Keyboard Shortcuts | [Wide](keyboard/compare-settings-haven-shortcuts-1440x900.png) | [Narrow](keyboard/compare-settings-haven-shortcuts-720x520.png) |
| Navigation | [Wide](keyboard/compare-settings-haven-key-navigation-1440x900.png) | [Narrow](keyboard/compare-settings-haven-key-navigation-720x520.png) |
| Menus & focus | [Wide](keyboard/compare-settings-haven-key-menus-1440x900.png) | [Narrow](keyboard/compare-settings-haven-key-menus-720x520.png) |

## Continuation checkpoint

Work stopped at the owner’s request. The last product change is `295b19d7c674c4f356640ca091e5fcf3adebe736` on `codex/overnight-haven-shell`. The original local handoff and its evidence are preserved. Allowance telemetry was unavailable. Each batch was pushed and its branch revision checked against GitHub.

Two preview edge cases remain. Rapid arrow input during a slow profile check can snap back to the earlier choice. An unexpected error during that check can leave the previous preview active without a specific message. The final change also hides both accessibility switches if the profile becomes unavailable. A native profile-refusal check is still pending. The broader outstanding Atlas work includes contextual chat placement, shortcut remapping, cross-app recent documents and the onboarding route choice.

The final Linux Release build finished with zero warnings and zero errors. All four final Skia checks passed, covering Layout → Accessibility, native switch writes, shortcut icon preview and reset, and preservation of unknown saved icon keys at 1440 × 900 and 720 × 520. [Final build and test receipt](verification.json) The native image receipts name the exact earlier build used for each gesture. [Earlier capture and failure receipts](native/interim-native-receipt.json)
