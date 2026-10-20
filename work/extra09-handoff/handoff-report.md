# extra09 handoff report (pass 3) — real typed CUI AI-access and permission selector

Lane: **Mercury extra09 (lane 9)** — `/workspace/9to1-mercury-extra09-permissions`
Resumed session `ses_ea558d848ffeVkSTOnaeInT1po`. Isolated base
`b69afadccbf1c86d26fafebf7f55debd10a282ba` (parent `1c080a7bbd1c6b40b09a7eb73f440de9cea7b81d`).

Owner authority: **10 additional Mercury workers authorised (target 16 total including Thunder),
superseding the older six-worker cap; $5 spending cap unchanged.** This lane implemented
source/tests/static checks + patch bundling only; **all SDK execution is NOT RUN until root
allocation** (no .NET/MSBuild/restore/test/compiler/app/model/subagent/provider/config/credentials/
cap/commit/push/external actions).

## 1. Source identity & authority read

- Canonical spec SHA-256
  `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96` (provenance.json).
- Read applicable sections: shared persistent AI bar, Home permission/trust broker, CUI
  declarative authoring boundaries, shared Chat controls & DEC-001 (open — no invented
  Ask/trust/approval-policy semantics).
- Shared source preimages recorded: `ChatComposerCuiView.cs`
  `ac26394fe5a7702147214c9115ffddad68c1096082227deb62b466a48968eb60`; `ChatComposer.cui`
  `6345c1458f593ef44fb5b82649c8532142b1c7e770b54a37eca2e0054b55cd06`; `HomeAppAiServices.cs`
  `590504a311b109c0d41d6451da54edd4bbe74c42aa264ed844ef141fe47a9bb6`.

## 2. Owned files (exclusive direct-edit scope)

| File | SHA-256 |
| ---- | ------- |
| `framework/CUI/AI/AppAiAccessSelector.cs` | `915faf92fb04ef12166e44a59e8d7ce9f9e3f4bbcffa6cf2a4285048cc252eb9` |
| `framework/CUI/AI/UI/AiAccessSelector.cui` | `b0821db8526a7024edbc343b444fc4267240a83967d0cb0bb25b3b3d33a5de28` |
| `framework/CUI/AI.Tests/AppAiAccessSelectorTests.cs` | `c733482a277aa05f3565e66c082bdf738bf1c08f9701d2047110ac4932584104` |

Handoff artifacts (`work/extra09-handoff/`):

| File | SHA-256 | Role |
| ---- | ------- | ---- |
| `AppAiAccessSelectorBindings.cs` | `aaa56cee1d7dffb95c07a6a20087e6be57aafb1bbfaa611db653513f2234f319` | Host bridge (INotifyPropertyChanged invalidation; owns no shared state). |
| `AiAccessSelectorScene.cs` | `53daeb587143223b72c740cac026b0057065b08e6638ecb88dd0d06019d691b9` | Host loader/parser of the packaged selector resource. |
| `AppAiAccessSelectorHostMount.cs` | `25e0ed27ab5d80a6c27e01cb3be7935bbd6711a2986ef7ff10dd797ba505b489` | Typed mount/scene adapter for a genuinely supplied owner (BLOCKED production dependency). |
| `AppAiAccessSelectorBindingsTests.cs` | `5495feb0fce9458bd7eaae108372e0698e2ad72171d4513674d0142dfce4a8b5` | Real bridge test (new-file inclusion patch). |
| `chat-composer-cuiview.patch` | `ac9c6f2adbaf7bf839b12bce8473e72465b1e80ba483138965547a825a9b5a52` | Unified patch for `ChatComposerCuiView.cs`. |
| `chat-composer-cui.patch` | `de23caf0fbdec86377a16d8c642d52c165ca91a7a55bb668f5f7326eb4e99e9f` | Unified patch for `ChatComposer.cui`. |
| `add-AppAiAccessSelectorBindings.cs.patch` | `213fae546a5d4c56f06f7929c6839e8daba079861f61c7dcb92167272caa08f7` | New-file patch. |
| `add-AiAccessSelectorScene.cs.patch` | `b99b5457e82e073a08925fd1df225937fb999c253ee8e920ce5cf12f27aa3199` | New-file patch. |
| `add-AppAiAccessSelectorHostMount.cs.patch` | `e4a086d7417746d4bec203f027b4189880dd8d29ab42d4a9b4c3f1552cb6f0a4` | New-file patch. |
| `add-AppAiAccessSelectorBindingsTests.cs.patch` | `fdf676fe77bc816af2539c6fc4dd2c5813a7acd8190caf2cb6c11ab6f3127205` | New-file test inclusion patch. |
| `integration-patch.md` | (see below) | Exact patch set + BLOCKED production dependency. |
| `handoff-report.md` | (this file) | Report. |

All patches verified with `git apply --check -p1` against the recorded preimages. No shared file
is modified by this lane.

## 3. Pass-3 corrections

1. **Test Command assertion**: `AppAiAccessSelectorTests.cs` now asserts the actual document-level
   command via `document.Actions["access.select-readonly"].Command` (and write), while retaining the
   component action-name routing assertions, the real SubmitAsync mode-request roundtrip, and the
   exact invalid-enum/rejection tests. `AiAccessSelector.cui` now declares the two actions in a
   canonical `<Actions>` section (Name → Command), matching the ChatComposer pattern.
2. **Exact unified patches**: `integration-patch.md` now ships real unified `.patch` files with
   hunk headers generated against the recorded shared-source preimages (`git apply --check`
   clean), plus explicit new-file additions for the host bridge, scene loader, mount adapter, and
   the real bridge test. No literal ellipses, unplaced snippets, or guessed APIs remain.
3. **Ownership corrected**: the composer view creates the bridge and its bindings dispose it on
   close (`ChatComposerBindings.Dispose` unsubscribes its forwarding callback and disposes the
   bridge; `ChatComposerCuiView.Dispose`/`DisposeAsync` call `_bindings.Dispose()`). The shared
   `AppAiAccessSelector` and `FloatingAiBarState` remain owned by Home and are never disposed here.
   The bridge test checks notifications after bridge close cannot affect the current owner and that
   rebind targets the same supplied state without leaks.
4. **Honest BLOCKED production dependency**: no production floating-bar render host/call site
   exists in this worktree. This lane does not claim integration and does not call a placeholder
   `homeAppAiServices.Create(context, actions)`. `AppAiAccessSelectorHostMount.cs` is a compilable
   typed mount/scene adapter for a genuine supplied owner; the bridge test is an explicit new-file
   inclusion patch to `Haven.Desktop.Tests`.

## 4. Preserved invariants

- Mode changes delegate to `FloatingAiBarState.SetAccessMode`; no divergent mode cache, no invented
  grants, no clearing of real pending approvals, no action re-execution.
- Read-only/Write stay distinct from host Edit/Suggest/View, legacy `PermissionMode` (ToolEnums),
  `ChatActionMode`, and per-action approval.
- Pending/denied/unavailable presentation backed only by real typed observations; absent source ⇒
  no status; DEC-001-safe wording (never implies mode switch awaits a grant).
- Existing composer commands/input/close life, preserved; no second bar/state created.

## 5. NOT RUN / pending root allocation

- [ ] No SDK execution (compile/test/app/display) — all NOT RUN until root executes.
- [ ] Root next gate: `framework/CUI/AI.Tests/NineToOne.Cui.AI.Tests.csproj` filtered
      `FullyQualifiedName~AppAiAccessSelectorTests`.
- [ ] Then host bridge tests after explicit inclusion of the new-file patches against accepted
      Runtime binaries.
- [ ] Root applies `work/extra09-handoff/*.patch` with `git apply -p1`; verify preimage hashes
      above match before applying.
