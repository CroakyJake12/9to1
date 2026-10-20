# Lane R8 — shared composer rich-input engine + typed port + generic CUI renderer integration

**Worker:** Extra08 (lane 8 of the 16-worker authorization). This is ONE assigned worker: no
subagents, model/provider/config/credential/cap changes, no commit/push/deploy, no build/test/native/app
execution (no serial .NET slot granted). **TEXT ONLY** — no image/SVG reading; SVG only via shell
text extraction.

**Frozen base:** resume2 identity `8e7e61e370f87cc93a2f4a061d7fafcad913d366` (parent
`8f77e26d9f8e20c9c206c6b796ba3ee468b060aa` → base `ddb2a8b813c8189b31556ef2ee491ac4f6f2b4eb`).
HEAD is preserved at `ddb2a8b…`; all in-tree changes are uncommitted/untracked. Spec authority is the
canonical v2 spec SHA-256 `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96` and the
owner records read at handoff (continuous-composer, floating-format-bar, composer-icons, SPUI-192/193,
single-app-implementation).

## Resume3 concrete deliverable (this continuation)

Closes the confirmed uncorrected gaps from the partial delivery:

1. **Structural engine wiring.** `ChatSpaceComposer.cs` now constructs the `ChatComposerFormattingPort`
   WITH the shared engine, owns one `_engine` per source, and exposes it via the new internal
   `ISharedRichEngineProvider`. `ChatComposerCuiView` reuses the source's engine when available
   (`ISharedRichEngineProvider`), else creates its own. `ChatHavenScene.Composer`'s
   `HavenComposerSource` also implements the provider and returns the scene's engine.
2. **Engine-bound native editing (actual event bridge).** The typed input ports (`IChatComposerInputPort`
   plus `CuiComposerInputPort` and `HavenChatComposerInputPort`) now reconcile real
   text/selection/paste edits into a `ChatComposerTextEdit(anchor, caret, newText)` delta raised via a
   new `TextEdited` event. The composed CUI view subscribes (`OnNativeTextEdited`) and folds every edit
   into the shared engine's retained document/history (`Rich.ReplaceSelection`). `Changed` repaints the
   surface. No fake-input flag, no hidden delimiters, no per-app visual tree.
3. **Atomic Enter.** `TypeText`/`ReplaceSelection` now run the text insert AND ` block continuation inside
   a single `RecordMutation`, so one Enter is ONE undo transaction. Proof:
   `Enter_block_continuation_is_one_atomic_undo_transaction`.
4. **Emoji shares history.** `InsertEmojiAsync` routes through `_richEngine.ReplaceSelection` when an
   engine is bound (same retained document/undo owner); live plain-text rewrite is no longer used on the
   engine-bound path.
5. **Generic CUI rich-input renderer/control.** New framework control `CuiRichTextInput` (TextBox
   subclass, `ICuiRichTextSource` schema versioned, `EditCommitted` event, fail-closed on unknown
   SchemaVersion) is registered in `CuiControlRegistry` (ctor `RegisterControlType("RichText", …)`).
   New generic feature support traverses registry/compiler→artifact→runtime and rejects unsupported
   consumers before effects.
6. **Registered real Window + authored CUI input tests** (`ChatComposerNativeInputTests.cs`) exercising
   both real consumers through the actual transport adapters and the shared engine, plus explicit
   `net10.0` `<Compile Include>` items for all three test files in `Haven.Desktop.Tests.csproj`.
7. **Haven no-GetResult bridge.** `ChatHavenScene.Composer` now returns/awaits the typed consumer
   `ValueTask` (no `AsTask().GetAwaiter().GetResult()` blocking bridge).

## Engine/formatting engine files (exclusive in-tree writes; uncommitted)

All under `9to1 Workspace/shared/`:

| File | Role |
|------|------|
| `src/Haven.Desktop/Views/Pages/Chat/ChatComposerRichTextModel.cs` | Canonical flat document: text buffer + parallel per-char `Marks`/`Links` arrays; block kinds + inline marks; recordable primitives (`Insert`, `RemoveRange`, `ReplaceSelection`, `ToggleInlineMark`, `ToggleBlockKind`, `LineBounds`/`LineKind`). |
| `src/Haven.Desktop/Views/Pages/Chat/ChatComposerMarkdown.cs` | Lossless bidirectional Markdown projection (backslash-escaping for literal delimiters; block kinds Heading1-6/CodeBlock/OrderedList/LetteredList; inline Underline/Superscript/Subscript/Latex) + coordinate maps + `SerializePlainText`. |
| `src/Haven.Desktop/Views/Pages/Chat/ChatComposerEditor.cs` | Transactional editor: `Mutate`/`RecordMutation` (one undo entry), undo/redo stacks, monotonic revision/transaction identity, typing style. |
| `src/Haven.Desktop/Views/Pages/Chat/ChatComposerRichInputEngine.cs` | Shared REAL engine: `TypeText`/`ReplaceSelection`/`Backspace`/`Delete`/`ApplyCommand`/`Undo`/`Redo`; grapheme/surrogate boundaries; atomic block continuation; `Changed`/`Disposed` event. |
| `src/Haven.Desktop/Views/Pages/Chat/ChatComposerFormattingPort.cs` | Typed adapter: engine-bound `ApplyAsync`/`Undo`/`Redo`, `InsertEmojiAsync` engine routing, fallback live/whole-draft paths for hosts not bound to a renderer. |
| `src/Haven.Desktop/Views/Pages/Chat/ChatSpaceComposer.cs` | ChatSpace source: one shared `_engine` (implements `ISharedRichEngineProvider`), port `richEngine` wiring, `RichEngine` property. |

New test files:
- `tests/Haven.Desktop.Tests/ChatComposerRichInputEngineTests.cs`
- `tests/Haven.Desktop.Tests/ChatComposerRichTextEngineTests.cs`
- `tests/Haven.Desktop.Tests/ChatComposerNativeInputTests.cs`

## SHA-256 of changed/new in-tree files

```
c90053504d9f4941e46529d5fe2bcd1c011460e076f4b0d677fa62e180bf3368  src/Haven.Desktop/Views/Pages/Chat/ChatSpaceComposer.cs
687956164998dbec72b2e2a557b3cdb5696dada4a37f630cfa0ebd6fd2467af8  src/Haven.Desktop/Views/Pages/Chat/ChatComposerEditor.cs
8e3883bb0330e66ca4a1a0ee43a1b1b70b69aafeec160e5daaa5e6d7eb1f4ba9  src/Haven.Desktop/Views/Pages/Chat/ChatComposerFormattingPort.cs
e3735ff51c93c7c864b10414adbccb27fa3f958ad65233b2bc8b3b8ac5b65123  src/Haven.Desktop/Views/Pages/Chat/ChatComposerMarkdown.cs
bd61f54d6db7f4144d9a8897aac708b210324e9dd2c5a7fccf81981da9cd0663  src/Haven.Desktop/Views/Pages/Chat/ChatComposerRichInputEngine.cs
0138dfb54e66055bdf1417b2f9cf00c2898a8b7547d1c95d9f6dc3e232c393ab  src/Haven.Desktop/Views/Pages/Chat/ChatComposerRichTextModel.cs
941a8bb6d4ce908bcb2edb70999c9706328853b820c95f36a913e289840d4a88  tests/Haven.Desktop.Tests/ChatComposerRichInputEngineTests.cs
09ebf13bbdcacd969194f9473f71a5e1ece1da9e761165f0f53df88ed516b908  tests/Haven.Desktop.Tests/ChatComposerRichTextEngineTests.cs
02a4098e236edd953391b58c16d560a020675234cae7639e97ef64a354752719  tests/Haven.Desktop.Tests/ChatComposerNativeInputTests.cs
```

## Complete patch artifacts (root-applicable; DO NOT apply from this lane)

Exact unified diffs, applied in dependency order **after lane1's stable boundary**. Each is verified
with `git apply --check` against the frozen base (all PASS). Preimages are exact HEAD snapshots
(`git show HEAD:…`). Stored under `work/r8-richtext/patches/`.

| # | Patch | Applies to | SHA-256 |
|---|-------|-----------|---------|
| 1 | `IChatComposerInputPort.cs.patch` | `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Chat/IChatComposerInputPort.cs` | `939cfca1234c776221e0ae5c0f7a49046f7b0faffcea6d3b180149c738e6eef8` |
| 2 | `HavenChatComposerInputPort.cs.patch` | `…/HavenChatComposerInputPort.cs` | `f1f7e2ab8046a918a5baf7a8d233596b0c1787874809adc6c6cfaae5dfc5bb86` |
| 3 | `ChatHavenScene.Composer.cs.patch` | `…/ChatHavenScene.Composer.cs` | `55c0b51f3259c5b4569224d9f977b2f4d95aaea8d4b75e254290d57bbc240102` |
| 4 | `ChatComposerCuiView.cs.patch` | `…/ChatComposerCuiView.cs` | `1810039cc4267901b0859d8193fa219a4d891add0332a9cc54572507ecc22475` |
| 5 | `ChatComposer.cui.patch` | `9to1 Workspace/Spaces/UI/Chat/ChatComposer.cui` | `63bb393231c1d7429d59202a9861e8bb2622d5586dbd3c9d0f2bf7bf502d2ebe` |
| 6 | `CuiControlRegistry.cs.patch` | `framework/CUI/Runtime/CuiControlRegistry.cs` | `4ad6f76919e26fcac1f71fdbc3216f9ae9d8dd51622c52b82df06085d9f7f5dd` |
| 7 | `CuiRichTextInput.cs.patch` (new file) | `framework/CUI/Runtime/CuiRichTextInput.cs` | `22e1de12cae400b4d2a1628c2767fd70cb786005fa16230307c641397d549555` |
| 8 | `Haven.Desktop.Tests.csproj.patch` | `9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj` | `4b838be146cfb7ab4e7553927f5fcc3188d1bd1b09bfc89ad03cff9aa3992229` |

## Minimal root integration order

1. Apply patch #1, #2 (port contract + adapters) — introduces `SelectionAnchor`, `IsComposing`,
   `ChatComposerTextEdit`, `TextEdited`.
2. Apply #6, #7 (registry registration + `CuiRichTextInput` renderer control).
3. Apply #4 (ChatComposerCuiView: reuse source engine via `ISharedRichEngineProvider`, `AttachRichRenderer`
   engaged in `ShowGuardedAsync`, `OnNativeTextEdited` → engine).
4. Apply #3 (ChatHavenScene.Composer: `HavenComposerSource` provider + awaited/returned `ValueTask`,
   no `GetResult` bridge).
5. Apply #5 (ChatComposer.cui authored bindings — toolbar/format/emoji wires to the port).
6. Apply #8 (project: `net10.0` `<Compile Include>` for the three `ChatComposer*Test*.cs` files).
7. The in-tree engine/port/source files above are already present in this worktree; root integrates
   them (or applies equivalent source) with the patches.
8. Run the three registered test classes in the serial .NET slot and review with independent acceptance.

## Registered tests (via #8)

- `ChatComposerRichInputEngineTests` — engine causal suite incl. atomic-Enter one-undo proof, escape
  round-trip, grapheme boundaries, block continuation, single shared history.
- `ChatComposerRichTextEngineTests` — model/Markdown projection + undo/redo.
- `ChatComposerNativeInputTests` — real shown Window + authored control input tests: collapsed Bold →
  real insert → styled run; backward selection → exact range/caret; native typing+format+emoji → one
  shared Undo/Redo history; Enter/list continuation one atomic Undo; IME/combining grapheme;
  save/remount + no-late-write after close; Haven adapter reconciles edits into the same engine.

## Execution state

- **NOT RUN:** no .NET build/test/native execution (no serial slot). All `git apply --check` on the
  six shared-seam/renderer/project patches PASS against the frozen base. Source is authored and static-
  checked only.
- No commit/push; no provider/config/credential/cap/global changes; no model subagents; no root edits.

## Remaining SPUI-192/193 items (owned by other lanes / not this lane's scope)

- Floating composed toolbar (Aa) authorship and styling — active lane1; preserved.
- Composer QueryViewport/Repeat/loader — active lane1; preserved.
- Voice/Waveform pill, permission selector, Send/Stop/Resume/queue/edit states, continuous surface —
  other lanes/owner-pending.
- Final independent acceptance + serial .NET execution of the registered tests (root).
