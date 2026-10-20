# Lane R8 — shared composer rich-text engine + typed formatting port

**Worker:** Extra08 (lane 8 of the 16-worker authorization; latest direct owner 16-worker
authorization overrides earlier 6/1-worker text). This is ONE assigned worker: no subagents,
model/provider/config/credential/cap changes, no commit/push/deploy, no build/test/native/app
execution (no .NET slot granted).

**Starting identity:** base commit `ddb2a8b813c8189b31556ef2ee491ac4f6f2b4eb` (clean tree).
Base of the shared composer work is the canonical v2 spec SHA-256
`3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96` and the owner records read at
handoff (continuous-composer, floating-format-bar, composer-icons, SPUI-192/SPUI-193).

## What was implemented (concrete, not a plan)

A shared **non-visual rich-text engine** and its **typed composer port**, both in
`Haven.Desktop.Views.Pages.Chat`:

| File | Role |
|------|------|
| `ChatComposerRichTextModel.cs` | Canonical flat document: text buffer + parallel per-char `Marks`/`Links` arrays; block kind derived from line markers; low-level recordable primitives (`Insert`, `RemoveRange`, `ToggleInlineMark`, `ToggleBlockKind`, `OverlappingLines`, `LineBounds`). |
| `ChatComposerMarkdown.cs` | Lossless bidirectional projection between the document and its portable Markdown draft (`SerializeDraft`, `SerializeDraftWithMap`, `TryParseDraft`, `SerializePlainText`) plus draft↔source coordinate maps used to translate the real selection. |
| `ChatComposerEditor.cs` | Transactional editor: every mutation is a reversible transaction; undo/redo stacks; monotonic revision/transaction identity; no-selection typing style. |
| `ChatComposerFormattingPort.cs` | The one typed adapter both standalone (ChatSpace) and Haven consume. Routes named format commands to the engine, syncs from the live draft, re-projects, rewrites the input and the post-edit selection; forwards the fallback draft setter's admitted ValueTask. |

Formatting scope attached to the real input: **Bold / Italic / Code / Link / Quote / Bullet /
Strikethrough** across selection & collapsed caret, plus undo/redo and lossless plain-text/draft
projection. No `_settingsOpen`/`_emojiOpen`/author buttons in C#; presentation remains CUI-owned.

### Consumer integration
- `ChatSpaceComposer.cs` (the only pre-existing file I edited) now routes `FormatSelection` and
  `InsertEmoji` through `ChatComposerFormattingPort`; the duplicated Markdown `Wrap` and
  whole-draft fallback are gone. One shared engine serves the mounted CUI input and the unmounted
  whole-draft path.
- `ChatHavenScene.Composer.cs` is **NOT** edited in-tree (not in my exclusive write set). An exact
  root-applicable unified diff is provided at
  `work/r8-richtext/integration-chathavenscene-composer.patch`. It swaps the scene's duplicated
  wrap logic for the shared port (`ChatComposerFormattingPort(() => Input)`).
- `ChatComposerCuiView.cs:278` needs **no change**: it already forwards `composer.format` →
  `IChatComposerSource.FormatSelection`, which both consumers now satisfy through the engine.
- `IChatComposerInputPort.cs`, `HavenChatComposerInputPort.cs`, `CuiComposerInputPort` are
  **untouched** (owned by other lanes). The engine works with the existing port contract; see the
  host port contract below for the optional direction/undo extension.

## Exclusive changed/new paths and hashes (SHA-256)

In-tree changes (all in `9to1 Workspace/shared/`):

```
cb72041e0f596898cbb62540a3a11474bce84be1a5567f4c9e32cb1213b9daab  src/Haven.Desktop/Views/Pages/Chat/ChatSpaceComposer.cs          (edited for integration)
67d55c4289f2b42da86c1cfe8ddb38de8b159b4100a80f22149f565cffc23a3f  src/Haven.Desktop/Views/Pages/Chat/ChatComposerRichTextModel.cs   (new)
810b86e6933ae17b90aee3aaaf5bbef482b3d05eedbc449a669e1129e93a6369  src/Haven.Desktop/Views/Pages/Chat/ChatComposerMarkdown.cs       (new)
6697fbfb3e9ca26224da1ec2086dd7dd2f9209504de28d3fd65afb30703a531f  src/Haven.Desktop/Views/Pages/Chat/ChatComposerEditor.cs          (new)
08b77b534ac91fee9ac5c54f05df8508899e8708a2e6a7f488000d03352cf47d  src/Haven.Desktop/Views/Pages/Chat/ChatComposerFormattingPort.cs  (new)
09ebf13bbdcacd969194f9473f71a5e1ece1da9e761165f0f53df88ed516b908  tests/Haven.Desktop.Tests/ChatComposerRichTextEngineTests.cs       (new test)
```

Integration patch file (new, in worktree):
`work/r8-richtext/integration-chathavenscene-composer.patch` (apply with `git apply` on the base to
`ChatHavenScene.Composer.cs`).

## Test registration (root integrator)

New test file compiles automatically on the Windows TFM (default compile items). For the
`net10.0` target the project disables default items, so add one line to
`shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj` in the `net10.0` item group (after
`<Compile Include="ChatSpacePageAcceptanceTests.cs" />`):

```xml
<Compile Include="ChatComposerRichTextEngineTests.cs" />
```

## Host port contract (what a host must satisfy)

1. **Sync:** before each format command, the engine re-parses the current draft from
   `IChatComposerInputPort.Text` and re-maps the selection via the draft↔source coordinate maps,
   so external plain typing is reflected.
2. **Post-edit write:** the port writes `Text` then `SetSelection(anchor, caret)`. The existing
   port normalizes anchor/caret to start/length, so exact backward-selection direction is currently
   lost across the boundary. To preserve direction, a host may extend the input port with an
   anchor/caret pair; the engine itself is direction-aware (normalizes internally) and is tested
   with reversed ranges.
3. **Undo/redo:** the engine keeps its own transaction stack (`Editor.Undo()/Redo()`), independent
   of native TextBox undo. A host that wants Ctrl+Z/Ctrl+Y routed to the engine should call the
   port's `Undo()/Redo()` (rewrites the live draft). This is exposed but not wired from CUI (that
   wiring is owned by other lanes).
4. **Admitted tasks:** the live path is synchronous (returns `ValueTask.CompletedTask`); the
   unmounted fallback returns the real `chat.update-draft` dispatch ValueTask. No swallowed
   failures: unknown format commands throw `InvalidOperationException`.

## Explicitly unimplemented (honest boundaries, NOT faked)

- **True WYSIWYG styled-input rendering** is not implemented; the editable surface remains plain
  Markdown (delimiters visible). The engine is the genuine model; serialization is not "hiding
  delimiters".
- Backslash escaping of literal delimiter characters (e.g., a literal `*` in prose) — draft
  re-parse treats delimiters canonically (documented in `ChatComposerMarkdown.cs`).
- Full **grapheme-cluster segmentation** (ICU) — the engine is surrogate-pair safe but does not
  segment extended grapheme clusters; some boundaries are documented.
- Block continuation on Enter (typing a newline inside a list/quoted line keeps the kind is NOT
  auto-applied; inserted lines carry the typing style marks but block kind is left to the block
  command).
- Email/webview rich-text not used; `MailRichTextEditor.cs` was intentionally NOT instantiated or
  copied.

## Execution state

- **NOT RUN:** no .NET build, no tests executed, no app/native display (no serial SDK slot). All
  tests are authored but unexecuted.
- No commit/push; no changes to shared feedback/registry; no changes outside this worktree.

## Suggested next step (for root/lane owner)

Apply `work/r8-richtext/integration-chathavenscene-composer.patch`, register the test file in the
`net10.0` target, then run the engine test class in the serial .NET slot and review with
independent acceptance.
