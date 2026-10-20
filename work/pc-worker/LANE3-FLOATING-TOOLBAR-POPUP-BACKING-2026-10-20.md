# Lane 3 handoff — floating formatting toolbar + CUI popup backing — 2026-10-20

Source identity: `/workspace/9to1-mercury-toolbar` (worktree), git HEAD unchanged (`fb70840d` WIP
checkpoint for six isolated Mercury lanes). No commit/push/deploy/credentials. No .NET build/test/app
launch performed: **execution is NOT RUN** until the coordinator grants a serialized .NET slot (and a
fresh compiled CUI bundle for the composer). Static checks (markup inspection, registry/property
cross-reference, template API usage) done by review only.

## Assigned scope (lane boundaries respected)
- Own: `ChatComposer.cui` outer placement/composition + `format-toolbar` segment; generic
  layout/container/layer/placement infra (none required beyond existing); popup template/backend
  support in `CuiControlTemplates.cs` + new `CuiPopupPresentation`.
- NOT edited: `CuiControlLoader.cs`, `CuiPropertyPresentation.cs`, composer plus/emoji/state
  (lane 1); compiler/parser/compiled transport (lane 2); `ChatComposerCuiDocument.cs`.
- No runtime source changes were needed for Part A: the existing CUI runtime already maps Container
  `Type="Overlay"` to a `Panel` and `AddChild` removes cells when `IsVisible=false` (which is what
  keeps the closed toolbar null in the visual tree). The floating behavior is achieved purely by the
  authored overlay composition.

## Part A — floating `format-toolbar` (same-scene overlay sibling)
Changed `9to1 Workspace/Spaces/UI/Chat/ChatComposer.cui`:
- `composer-surface` is now `Type="Overlay"` (was Vertical Stack); removed its `CellGap` (overlay
  does not support CellGap; it lives on the inner body stack).
- The normal-flow children were wrapped, unchanged, in a new `composer-body` Container
  (`Type="Vertical Stack" CellGap="8"`): chips-row, input-column, controls-row, emoji-panel,
  settings-panel. All authored controls/ids/actions/visibility bindings preserved verbatim.
- `format-toolbar` is now a DIRECT sibling overlay child of `composer-surface` (after the body, so
  it renders above), `HorizontalAlignment="Left" VerticalAlignment="Top"`, still
  `IsVisible="{Binding FormattingOpen}"`, same B/I/code/link/quote/list buttons, same
  `accessible-name="Formatting toolbar"`. It is out of the body flow, nonmodal, same CUI scene, and
  hit-testable only over its own bounds (the cell is transparent). It does not move/resize the input
  or controls-row baseline.
- Kept on the surface: `padding`, `background`, `CornerRadius`, `compound-focus`, `owns-backdrop`.

## Part B — CUI popup backing (white rectangle fix)
- `framework/CUI/Runtime/CuiControlTemplates.cs` `TopLevelContent`: special-cases `PopupRoot`
  (used for all CUI popups). The popup root now uses a transparent root frame (no donor white
  Background) and binds both `PART_TransparencyFallback` and `TopLevel.TransparencyBackgroundFallback`
  to the semantic `CuiPopupSurfaceBrush`. Normal full-window roots still use the shared `Frame`
  (owner-bound background), so window look is unchanged. When the compositor cannot negotiate alpha,
  TopLevel paints the themed surface (truthful opaque fallback); when alpha works it clears the
  fallback and the rounded/shadow chrome stays transparent.
- New `framework/CUI/Runtime/CuiPopupPresentation.cs`: per-popup (never global) host-policy matrix.
  Overlay is preferred only when alpha is verified unavailable AND airspace is verified clear;
  every other state retains native. Live pre-show alpha/airspace signals are not reliably available
  to generic code (per prep), so the safe default is native; the overlay branch is exposed and
  tested but not force-wired, to avoid inferring overlay safety.

## Tests authored (NOT RUN — require slot grant + recompiled composer bundle)
- `9to1 Workspace/Browse/Host.Tests/FloatingFormatToolbarTests.cs`: real `ChatComposerCuiView`;
  asserts surface is overlay, body is its vertical stack, input lives in body, toolbar null when
  closed, visible when open, NOT a descendant of body, anchored top/left in surface, overlaps body
  region, input and controls-row bounds unchanged across toggle, B/I dispatch with selection
  preserved, same input identity, narrow/multiline resize stability.
- `framework/CUI/Runtime.Tests/CuiPopupBackingTests.cs`: 9-case host-matrix theory (+ unknown
  guard) and a headless real PopupRoot template assertion (transparent root frame, semantic
  non-white fallback).

## Existing-regression considerations
- `HostComposerLifetimeTests.Shared_composer_aa_button_toggles_floating_toolbar_...` must keep
  passing after recompile: toolbar null when closed (cell removal), IsVisible true when open, B/I
  at indices 0/1, same input identity. The new markup preserves all of these.
- No changes to absolute-layout / grid / stack runtime behavior; `CuiAbsoluteLayoutTests` and the
  other Runtime.Tests suites are unaffected by these edits.
- The composer tests and `CuiPopupBackingTests` require the PACKAGED compiled bundle to be rebuilt
  from the new `ChatComposer.cui` source before they run (slot-gated). Until then they are NOT RUN.

## Dependencies / open notes for coordinator
- Part B overlay-host adoption is BLOCKED on a reviewed pre-show capability/airspace signal
  (platform transparency negotiation + conservative native child-window query). Retaining native
  hosting is the safe default and already fixes the white backing via the template change. If the
  owner requires the same-window overlay branch to activate, a reviewed signal must be supplied;
  I did not invent one.
- Lane-1/lane-2: no new typed property or compiler changes were required; no dependency request.
- `docs/specification/open-decisions.json` was not modified; if the overlay activation needs
  recording as an open decision, the coordinator should add it.
