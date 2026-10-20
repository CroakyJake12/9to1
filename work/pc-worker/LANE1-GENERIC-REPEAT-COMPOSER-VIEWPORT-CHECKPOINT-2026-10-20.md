# Lane-1 Checkpoint — Generic Repeat composition repair + composer emoji viewport

**Date:** 2026-10-20
**Worker lane:** 1 of 6 (chat/CUI lane)
**Worktree:** `/workspace/9to1-chat-cui`
**Branch:** `codex/cloud-native-chat-cui-20261020` (HEAD `ddb2a8b8`)
**Model:** Mercury 3 (mercury/mercury-3)

## Scope (assigned and delivered)
1. Repair the generic `Repeat` layout-composition defect: a `Repeat` directly under a
   `CuiContainerControl` must emit its generated item roots **as individual cells into the owning
   Container's own layout** (Wrap/Horizontal Stack/Vertical Stack/Grid), not into a hidden vertical
   sub-panel.
2. Make the emoji picker a complete, usable, **bounded viewport** with real full-catalogue paging,
   preserving the existing emoji trigger/allowlist work.

## Authority
- Canonical spec `docs/specification/9to1-development-specification-v2.md`
  (SHA-256 `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`):
  - Nonvisual constructs / core elements: lines 1840–1858.
  - Repeated UI: lines 2383–2395 ("Repeat creates one generated UI instance per source item and
    MUST preserve item identity across live collection additions/removals/reordering"). The spec does
    **not** define a hidden panel; the repair makes generated instances participate in the owning
    Container's declared layout while preserving keyed identity.
- Owner corrections read: `2026-10-20-composer-icons/owner-correction.json`,
  `2026-10-20-floating-format-bar/owner-correction.json`,
  `2026-10-20-chat-composer-dulche/owner-correction.json`,
  `2026-10-20-single-app-implementation/owner-correction.json`.

## Change summary

### Framework — generic container-mode Repeat (`framework/CUI/Runtime`)
- `CuiControlLoader.cs`
  - `LoadComponent` threads `parent` into child loading; `LoadRepeat(component, parent)` branches.
  - Canonical container mode: when `!explicitPanel` and `parent is CuiContainerControl`, a
    `ContainerCellRepeatHost` is created and each generated item root is keyed-added into the owning
    Container's layout. No hidden panel, no `ApplyProperties`/theme on a phantom host. Returns a
    `CuiRepeatFragment` sentinel that `AddChild` skips (bookkeeping only).
  - Legacy `Panel=` (and non-container parents) unchanged on the historical nested-panel path for
    compatibility (`LegacyPanelRepeatHost`).
  - New `ICuiRepeatHost` abstraction (`Count/IndexOf/Insert/Remove/Move/IsGrid`) so
    `ReplaceRepeatItems` works for both paths with stable keyed identity (no clear/re-add).
  - `CuiRepeatState` now holds `ICuiRepeatHost` + `HostControl`; `DetachControlTree` uses
    `HostControl` for teardown membership.
- `CuiContainerControl.cs`
  - `InsertAuthoredChild(index, child)`, `MoveAuthoredChild(from,to)`, `AuthoredChildCount`;
    `AddChild` delegates to `InsertAuthoredChild`. All preserve stable cells, subscriptions, and
    physical attachment order.

### Composer emoji picker (bounded viewport + full-catalogue paging)
- `9to1 Workspace/Spaces/UI/Chat/ChatComposer.cui`
  - `emoji-grid-viewport`: `Wrap` with `VerticalScrolling="True"`, `MaxWidth="320"`,
    `MaxHeight="150"` so the grid genuinely wraps into multiple columns/rows **and** overflows a
    bounded vertical viewport (real scroll). `HorizontalScrolling="False"`.
  - New `emoji-pager` (`Prev`/label/`Next`) + `composer.emoji-prev`/`composer.emoji-next` actions.
- `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Chat/ChatComposerCuiView.cs`
  - Paging/query state: `EmojiPageSize = 48`, `EmojiPageEntries`, total/start/end metadata,
    `HasPrevious/HasNext`, page label; query change resets+clamps page.
  - Picker search spans **names, groups, and exact glyphs** (matches the canonical controller's
    search semantics) so flags and trigger-excluded sequences stay reachable in the picker while the
    random-trigger icon pool still applies the allowlist/flag/suggestive filters.

### Tests (authored; NOT RUN)
- `framework/CUI/Runtime.Tests/CuiRepeatContainerLayoutTests.cs` — 4 tests:
  1. wrap: item roots flow into owning Wrap as individual cells with static siblings, multi-row wrap,
     distinct X on a row, real hit targets;
  2. keyed add/remove/reorder preserves instance identity and positions;
  3. two repeat ranges + static siblings emit into the owning container in one authored order;
  4. legacy `Panel=` keeps the nested host-panel compatibility escape.
- `9to1 Workspace/Browse/Host.Tests/ChatComposerEmojiPickerViewportTests.cs` — 3 tests:
  1. picker opens a real multi-column Wrap grid inside a bounded scroll viewport (overflow + offset
     change);
  2. realized count stays bounded (<= 48) while full-catalogue paging reaches first/last/flags and
     search keeps trigger-excluded sequences;
  3. pointer click on a noninitial, emoji inserts exact Unicode once, preserving draft/caret/input
     identity/focus.

## File hashes (SHA-256)
| File | Hash |
| --- | --- |
| `framework/CUI/Runtime/CuiControlLoader.cs` | `706b8bc09cc6565f82bf5cf26c618de0b398d6cb79f9709ccd43082259da92f5` |
| `framework/CUI/Runtime/CuiContainerControl.cs` | `d487844ef36967aef6aeffb62d8126afbb6fb2c695e9ae42ce1e7e9e869fb290` |
| `9to1 Workspace/Spaces/UI/Chat/ChatComposer.cui` | `bd858a236bfa98f50b87fb2a62526ed5814124ed8f67b7fbef3e3cf678682687` |
| `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Chat/ChatComposerCuiView.cs` | `00e0bdef38a3aa44dd1d4fca1a652d3a89129071bf995f3588cd6da8315f9a4e` |
| `framework/CUI/Runtime.Tests/CuiRepeatContainerLayoutTests.cs` | `70eb82667e3b23e6d566ea370f1b4330c5f79deb58313e2b7b5a5827c1f1af35` |
| `9to1 Workspace/Browse/Host.Tests/ChatComposerEmojiPickerViewportTests.cs` | `05010ce8ee78159db8182eb46bd20604805512a476e79af64e5871e12f25b3fa` |

All changes are uncommitted in the worktree. Prior state was clean at HEAD `ddb2a8b8`.

## Behavior-change scope (documentation)
The container-mode path now activates for any `Repeat` that is a direct child of a `Container` without
a `Panel=` attribute. Existing `.cui` surfaces that match this pattern and now emit into the owning
Container's layout (behavior change from hidden vertical panel → real container layout) include, e.g.:
- `Assistants/NativeUI/UI/AssistantConfiguration.cui`, `AssistantConversation.cui`
- `Stacks/NativeUI/UI/StacksWorkspace.cui`
- `Mail/UI/MailWorkspace.cui` (has `Type`/no `Panel` under a container)
- `Spaces/UI/Chat/ChatSpace.cui` (transcript), `ChatComposer.cui` (chips, emoji grid)
- `Spaces/UI/Home/SpacesHome.cui` (custom spaces, switcher, recents)
- `Browse/NativeUI/UI/BrowseWorkspace.cui` (bookmarks/history; tabs use explicit `Panel=` → legacy)
- `shared/src/Haven.Desktop/Views/Pages/Home/UnifiedHomeApps.cui` (uses explicit `Panel=` → legacy)

For vertical-stack containers this is visually equivalent (cells already ordered identically); the
repair is most visible where the owning container is a `Wrap`/`Grid` (items now genuinely participate
in that layout). These other surfaces are the responsibility of their owning lanes; lane-1 only
delivered the framework primitive + the composer picker.

## Test status
- `CuiRepeatContainerLayoutTests` (framework layer): **authored, NOT RUN** — requires a root
  build/restore/test slot (no .NET allowed to this worker).
- `ChatComposerEmojiPickerViewportTests`: **authored, NOT RUN** — additionally depends on root/lane-2
  rebuilding the compiled artifact `CUI.Compilation.v1` from the updated `ChatComposer.cui`; cannot
  pass until that artifact reflects the new viewport/pager markup.

## Remaining gates / open items
- **Native FAIL is NOT fixed by source alone.** The observed clipping (single vertical emoji column from
  the legacy hidden panel) is addressed structurally; acceptance requires root's independent native
  validation of the rebuilt artifact.
- Compiled-artifact rebuild must precede the three composer picker tests.
- Existing strict regressions must stay green: `CuiTranscriptRepeatCapabilityTests.cs`,
  `CuiRepeatedInputFocusTests.cs`, `CuiRepeatedActionScopeTests.cs`, `CuiPerItemTemplateTests.cs`,
  `HostComposerLifetimeTests.cs` (the `Panel=`/non-container paths were left untouched for this).
- No commit/push performed (per worker constraints).

## Next move for coordinator
1. Grant lane-1 a .NET source-test slot to run `CuiRepeatContainerLayoutTests` (no artifact dependency).
2. Rebuild `CUI.Compilation.v1` from the updated `ChatComposer.cui`, then run
   `ChatComposerEmojiPickerViewportTests`.
