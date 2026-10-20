# Lane 7 handoff — Voice & Vision / Voice pill (Mercury)

- **Worktree**: `9to1-mercury-extra07-voice` (branch `codex/mercury-extra07-voice-20261020`), sparse checkout (34% files; sources under `9to1 Workspace/`).
- **Owner**: lane 7, Mercury worker. Read `AGENTS.md` + `docs/specification/owner-references/2026-10-20-single-app-implementation/owner-correction.json` at this handoff.
- **Status**: SOURCE/TESTS/STATIC-ANALYSIS complete. NO dotnet/MSBuild/restore/tests/apps/capture/display/live-audio-video/model commands were run (root owns the build slot). Compile/score NOT verified locally.

## Source identity (changed/created in this lane)
All under `/workspace/9to1-mercury-extra07-voice/9to1 Workspace/`:

- `shared/src/Haven.Application/Call/VoiceVisionRuntimeState.cs` (new): typed projection record + `VoiceVisionPresentationKind` + `VoiceVisionBackendProfile` + `VoiceVisionStartRequest/Result` + `VoiceVisionOperation` + `IVoiceVisionRuntime` contract (includes `SetPresentation`).
- `shared/src/Haven.Application/Call/IVoiceVisionRuntime.cs` (new): typed production contract; `SetPresentation` added at this verification pass.
- `shared/src/Haven.Application/Call/VoiceVisionLiveRuntime.cs` (new): single-owner runtime facade over `ICallCoordinator` + `VisionVoiceSessionService` + `IVersionedSettingsStore` mapping store. Added `using Haven.Core;` (CallState) at this pass.
- `shared/src/Haven.Desktop/Interface/Shell/Pop-Ups/VoiceVision/` (new): `VoiceVisionPill.cui`, `VoiceVisionSession.cui`, `VoiceVisionCuiDocument.cs`, `VoiceVisionCuiBindings.cs`, `VoiceVisionHostAdapter.cs`.
- `shared/tests/Haven.Application.Tests/Call/VoiceVisionLiveRuntimeTests.cs` (new, auto-globbed): runtime contract tests. Fixed `HavenMode.AI` → `HavenMode.Chat` at this pass.
- `shared/tests/Haven.Desktop.Tests/VoiceVision/VoiceVisionCuiPresentationTests.cs` (new, needs `Compile` line via patch): CUI parse + bindings. Added `using Haven.Desktop.Views.Shell.VoiceVision;` at this pass.
- `work/pc-worker/voice-vision-integration.patch` (private, NOT applied): EmbeddedResource lines, Desktop.Tests `Compile` line, DI registration scaffold, host wiring notes.

## Assigned scope / requirement coverage
- Spec `Voice & Vision` §[5901–6203]: one canonical multimodal surface; single canonical SessionID/ConversationID/SpaceID across pill/full; no second runtime; End is real stop/drain; closing presentation is not End; camera/screen NEVER auto-start from voice/model toggle; explicit permission; persistent capture indicators; local-only truthful (no silent cloud); capability-checked modes (do not claim Monologue/LiveListener/LiveTranslate operational).
- Spec `Chat Voice Mode` §[5132–5160]: idle composer waveform Voice toggle opens the same-session floating Voice & Vision pill (delivered as the reusable pill; the composer's own hookup is a host seam in the patch, owned by root/owner).
- Owner corrections consumed: CUI-only authoring; Canonical icon catalog gap (mic/camera/captions glyphs) reused as best-fit keys (`icon:wave/window/vision/play/close/text`); `CuiFontRole` restricted to Heading/Body.

## Observed state / static checks performed
- Verified `ICallCoordinator` members used (State, CurrentSession, CurrentConversation, Capabilities, IsActive, IsMuted, IsScreenSharing, StateChanged, AudioLevelChanged, StartAsync, SubmitTextAsync, SetMutedAsync, PauseAsync, ResumeAsync, StartScreenShareAsync, StopScreenShareAsync, InterruptAsync, EndAsync) exist in `CallAbstractions.cs`.
- Verified `IVoiceInputStatusSource.InputStatusChanged`; `CallCapabilities.HasSpeechInput/CanShareScreen/Voices`; `CallState` members (Idle/Listening/Transcribing/Thinking/Speaking/Paused/Error) in `Haven.Core/Models/CallModels.cs`; `Conversation.Id`; `CallSession.VoiceName`; `CallVoice.Name`.
- Verified `VisionVoiceSessionService.StartAsync/TransitionAsync/SetModeAsync/SetRetentionAsync/GetSessionAsync` signatures; `MultimodalSession` fields; `VisualSourceDescriptor/Type/State`; `VisionVoiceError(Code)` all in `Haven.Application.Call`.
- Verified `IVersionedSettingsStore.GetAsync<T>/SetAsync<T>` and `IVersionedSettingsCompareExchange.CompareExchangeAsync` signatures; `MultimodalSessionStore(IVersionedSettingsStore)` ctor.
- Verified CUI: only `CuiFontRole` Heading/Body; `Input Type="Select"` uses `ItemsSource`+`SelectedIndex` two-way; `Repeat` item paths (`displayName/typeLabel/stateLabel`); `If` condition bindings; icon keys present in canonical catalog.
- Verified `CuiControlLoader(CuiControlRegistry)` ctor + `TryLoad`/`WireBindings`/`Dispose` via canonical `UnifiedHomePage.cs`/`App.OriginalLifetime.cs`/`FilesWorkspaceSetupPage.cs` patterns; `NineToOne.Web.BrowserCanonicalIcons.Register` mirrored from `HomeAppsCuiDocument.cs`.
- Verified binding context implements `ICuiWritableBindingContext`/`ICuiActionDispatcher`/`ICuiActionAvailability`/`ICuiRepeatItemBindingContext` with signatures matching `HomeAppsCuiBindings.cs`.

## Fixes applied during this verification pass
1. `VoiceVisionLiveRuntime.cs`: added `using Haven.Core;` (required for `CallState`).
2. `VoiceVisionLiveRuntimeTests.cs`: `HavenMode.AI` → `HavenMode.Chat` (HavenMode has no AI member).
3. `VoiceVisionCuiPresentationTests.cs`: added `using Haven.Desktop.Views.Shell.VoiceVision;`.
4. `VoiceVisionCuiBindings.cs`: captions availability (thepill/session.canEnableCaptions`, `voice.captions`) now `true` unconditionally, matching runtime `CanEnableCaptions=true` and the "captions can always be toggled" intent.
5. `VoiceVisionHostAdapter.cs`: `CuiControlLoader.Load(document)` → canonical `TryLoad(document).Root` + `WireBindings(root)`.
6. `IVoiceVisionRuntime.cs`: added `SetPresentation(VoiceVisionPresentationKind)` (host calls it via the interface).

## Tests present (NOT run in this lane)
- `VoiceVisionLiveRuntimeTests`: attach-existing-identity-without-Start; second-presentation-reuses-one-runtime/SessionID; closing-presentation-not-End + End-drains; late-callback-suppression-after-End; unsupported/denied camera → zero capture starts; stopping screen share → zero later frames; concurrent-starts-coalesce (single Start).
- `VoiceVisionCuiPresentationTests`: both .cui documents parse without CUI errors; bindings project truthful capability-gated state (status, canStart/canEnd, muteOpacity, visualSources item labels); availability gating (empty vs active); known-command dispatch + unknown-command throws.

## Next steps owned by root/owner
- Allocate the build slot; apply `voice-vision-integration.patch` seams (EmbeddedResource × 2, Desktop.Tests `Compile` × 1, DI singleton registration, host wiring incl. composer waveform toggle → pill and adapter `RequestFullSurface` → full surface; adapter `ClosePresentation()` on overlay close, never `EndAsync`).
- Run `dotnet` (guarded entrypoint `/home/jacob/.local/bin/dotnet`, `-m:1`) build + the two new test files.
- Report missing dedicated mic/camera/captions glyphs to the owning icon lane (canonical catalog best-fit keys used as stopgap).

## Unresolved / held decisions
- Host wiring + DI (steps 3–4 in patch) deliberately NOT edited in this lane (read-only boundaries; must be consulted by owner). They are recorded as seams only.
- Monologue/LiveListener/LiveTranslate remain backend-capability-gated (default `VoiceVisionBackendProfile` reports them unsupported → truthful unavailable state). No operational claims made.
- No speculative performance/behavior claims; acceptance must verify the applicable spec sections.
