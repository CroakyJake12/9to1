using System.Text.Json;
using Haven.Application;
using Haven.Application.Call;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Shell.Overlays;
using Haven.UI;
using Haven.UI.Components;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class VoiceSessionContinuityTests
{
    [Fact]
    public async Task Late_capture_start_ack_does_not_override_a_concurrent_mute()
    {
        var speechInput = new RetainedCallbackSpeechInput { BlockStart = true };
        await using var coordinator = new CallCoordinator(
            new TestCallRepository(), new TestConversationRepository(), new UnusedOllamaClient(),
            speechInput, new UnusedSpeechOutput(), new UnusedScreenShare());
        var options = new CallStartOptions(new ModelDescriptor("test-model", 1, "test", "1B", "Q4",
            new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch));

        var starting = coordinator.StartAsync(options, null, CancellationToken.None);
        await speechInput.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await coordinator.SetMutedAsync(true, CancellationToken.None);
        Assert.Equal(VoiceInputState.Muted, coordinator.InputStatus.State);

        speechInput.ReleaseStart.TrySetResult(true);
        await starting.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(coordinator.IsMuted);
        Assert.Equal(VoiceInputState.Muted, coordinator.InputStatus.State);
        Assert.False(speechInput.IsCapturing);
    }

    [Fact]
    public async Task Retired_turn_completion_cannot_publish_state_into_a_replacement_session()
    {
        var ollama = new UnusedOllamaClient { BlockStream = true };
        await using var coordinator = new CallCoordinator(
            new TestCallRepository(), new TestConversationRepository(), ollama,
            new RetainedCallbackSpeechInput(), new UnusedSpeechOutput(), new UnusedScreenShare());
        var options = new CallStartOptions(new ModelDescriptor("test-model", 1, "test", "1B", "Q4",
            new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch));
        var stateChanges = new List<CallStateChangedEventArgs>();
        coordinator.StateChanged += (_, args) => stateChanges.Add(args);

        await coordinator.StartAsync(options, null, CancellationToken.None);
        var originalSessionId = coordinator.CurrentSession!.Id;
        var turn = coordinator.SubmitTextAsync("finish after retirement", CancellationToken.None);
        await ollama.StreamEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await coordinator.EndAsync(CancellationToken.None);
        await coordinator.StartAsync(options, null, CancellationToken.None);
        Assert.NotEqual(originalSessionId, coordinator.CurrentSession!.Id);
        var stateChangesAfterReplacement = stateChanges.Count;

        ollama.ReleaseStream.TrySetResult(true);
        await turn.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(stateChanges.Skip(stateChangesAfterReplacement));
        Assert.Equal(CallState.Listening, coordinator.State);
        Assert.Equal(1, ollama.StreamCallCount);
    }

    [Fact]
    public async Task Retired_ephemeral_assistant_fragment_is_not_added_to_replacement_context()
    {
        var ollama = new UnusedOllamaClient { HoldFirstStreamAfterPartial = true };
        await using var coordinator = new CallCoordinator(
            new TestCallRepository(), new TestConversationRepository(), ollama,
            new RetainedCallbackSpeechInput(), new UnusedSpeechOutput(), new UnusedScreenShare());
        var model = new ModelDescriptor("test-model", 1, "test", "1B", "Q4",
            new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch);
        var ephemeralProfile = new VoiceProfile("ephemeral-test", "Ephemeral test", "Test-only profile",
            "Keep this fake-only test concise.", RetainTranscript: false);
        var options = new CallStartOptions(model, VoiceProfile: ephemeralProfile);
        var partialProduced = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.TranscriptChanged += (_, args) =>
        {
            if (args.Role == MessageRole.Assistant && args.IsDelta && args.Text == "old ephemeral assistant fragment")
                partialProduced.TrySetResult(true);
        };

        await coordinator.StartAsync(options, null, CancellationToken.None);
        var oldConversationId = coordinator.CurrentConversation!.Id;
        var oldTurn = coordinator.SubmitTextAsync("old ephemeral prompt", CancellationToken.None);
        await ollama.StreamEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await partialProduced.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await coordinator.EndAsync(CancellationToken.None);
        await coordinator.StartAsync(options, null, CancellationToken.None);
        var replacementConversationId = coordinator.CurrentConversation!.Id;
        Assert.NotEqual(oldConversationId, replacementConversationId);

        ollama.ReleaseStream.TrySetResult(true);
        await oldTurn.WaitAsync(TimeSpan.FromSeconds(2));
        await coordinator.SubmitTextAsync("new ephemeral prompt", CancellationToken.None);

        Assert.Equal(2, ollama.Requests.Count);
        Assert.DoesNotContain(ollama.Requests[1].Messages,
            message => message.Content.Contains("old ephemeral assistant fragment", StringComparison.Ordinal));
        Assert.Contains(ollama.Requests[1].Messages,
            message => message.Role == "user" && message.Content == "new ephemeral prompt");
    }

    [Fact]
    public async Task Retired_final_transcript_queued_on_turn_gate_is_not_written_to_replacement_call()
    {
        var speechInput = new RetainedCallbackSpeechInput();
        var conversations = new TestConversationRepository();
        var ollama = new UnusedOllamaClient { BlockStream = true };
        await using var coordinator = new CallCoordinator(
            new TestCallRepository(), conversations, ollama,
            speechInput, new UnusedSpeechOutput(), new UnusedScreenShare());
        var options = new CallStartOptions(new ModelDescriptor("test-model", 1, "test", "1B", "Q4",
            new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch));
        var stateChanges = new List<CallStateChangedEventArgs>();
        coordinator.StateChanged += (_, args) => stateChanges.Add(args);

        await coordinator.StartAsync(options, null, CancellationToken.None);
        var retiredCallback = Assert.Single(speechInput.Callbacks);
        var firstConversationId = coordinator.CurrentConversation!.Id;
        var occupyingTurn = coordinator.SubmitTextAsync("blocking typed turn", CancellationToken.None);
        await ollama.StreamEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var queuedStaleTranscript = retiredCallback(
            new SpeechInputEvent(SpeechInputEventKind.FinalTranscript, "retired speech text"),
            CancellationToken.None);
        Assert.False(queuedStaleTranscript.IsCompleted);

        await coordinator.EndAsync(CancellationToken.None);
        await coordinator.StartAsync(options, null, CancellationToken.None);
        var replacementConversationId = coordinator.CurrentConversation!.Id;
        Assert.NotEqual(firstConversationId, replacementConversationId);
        var changesAfterReplacement = stateChanges.Count;

        ollama.ReleaseStream.TrySetResult(true);
        await occupyingTurn.WaitAsync(TimeSpan.FromSeconds(2));
        await queuedStaleTranscript.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(conversations.Messages,
            message => message.Content == "retired speech text" ||
                message.ConversationId == replacementConversationId && message.Content == "blocking typed turn");
        Assert.Empty(stateChanges.Skip(changesAfterReplacement));
        Assert.Equal(1, ollama.StreamCallCount);
        Assert.Equal(CallState.Listening, coordinator.State);
    }

    [Fact]
    public async Task Retired_microphone_callbacks_cannot_mutate_a_resumed_or_replacement_call()
    {
        var speechInput = new RetainedCallbackSpeechInput();
        var ollama = new UnusedOllamaClient();
        await using var coordinator = new CallCoordinator(
            new TestCallRepository(), new TestConversationRepository(), ollama,
            speechInput, new UnusedSpeechOutput(), new UnusedScreenShare());
        var options = new CallStartOptions(new ModelDescriptor("test-model", 1, "test", "1B", "Q4",
            new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch));

        await coordinator.StartAsync(options, null, CancellationToken.None);
        var retiredCallback = Assert.Single(speechInput.Callbacks);
        var originalSessionId = coordinator.CurrentSession!.Id;
        await coordinator.PauseAsync(CancellationToken.None);
        await coordinator.ResumeAsync(CancellationToken.None);
        Assert.Equal(originalSessionId, coordinator.CurrentSession!.Id);
        var stopCountAfterResume = speechInput.StopCount;

        await retiredCallback(new SpeechInputEvent(SpeechInputEventKind.SourceClosed), CancellationToken.None);
        Assert.Equal(stopCountAfterResume, speechInput.StopCount);
        Assert.Equal(CallState.Listening, coordinator.State);
        Assert.Equal(VoiceInputState.Listening, coordinator.InputStatus.State);

        await coordinator.EndAsync(CancellationToken.None);
        await coordinator.StartAsync(options, null, CancellationToken.None);
        Assert.Equal(3, speechInput.Callbacks.Count);
        var stopCount = speechInput.StopCount;
        var transcriptEvents = new List<CallTranscriptEventArgs>();
        coordinator.TranscriptChanged += (_, args) => transcriptEvents.Add(args);

        // Deliberately use a non-cancelled token: session/capture identity must
        // reject a late callback even when an adapter fails to propagate cancellation.
        await retiredCallback(new SpeechInputEvent(SpeechInputEventKind.SourceClosed), CancellationToken.None);
        await retiredCallback(new SpeechInputEvent(SpeechInputEventKind.Error, Error: "retired capture failure"), CancellationToken.None);
        await retiredCallback(new SpeechInputEvent(SpeechInputEventKind.SpeechStarted), CancellationToken.None);
        await retiredCallback(new SpeechInputEvent(SpeechInputEventKind.PartialTranscript, "retired partial"), CancellationToken.None);
        await retiredCallback(new SpeechInputEvent(SpeechInputEventKind.FinalTranscript, "retired final"), CancellationToken.None);

        Assert.Equal(stopCount, speechInput.StopCount);
        Assert.Equal(CallState.Listening, coordinator.State);
        Assert.Equal(VoiceInputState.Listening, coordinator.InputStatus.State);
        Assert.Empty(transcriptEvents);
        Assert.Equal(0, ollama.StreamCallCount);
    }

    [Fact]
    public void Restarting_same_multimodal_session_restores_capture_state_without_granting_permission()
    {
        var session = new MultimodalSession(
            Guid.NewGuid(), Guid.NewGuid(), null, VisionVoiceMode.Conversational, null,
            "local-only", null, null, MultimodalSessionState.Starting,
            MicrophoneCaptureState.Starting, [], null, null, DateTimeOffset.UtcNow,
            1, VisionVoiceRetention.Ephemeral);

        Assert.True(MultimodalSessionLifecycle.TryTransition(session, MultimodalSessionState.Starting,
            out var repeatedStarting, out _));
        Assert.Equal(session.SessionId, repeatedStarting.SessionId);
        Assert.Equal(session.Revision + 1, repeatedStarting.Revision);
        Assert.True(MultimodalSessionLifecycle.TryTransition(repeatedStarting, MultimodalSessionState.Listening,
            out var listening, out _));
        Assert.Equal(session.SessionId, listening.SessionId);
        Assert.Equal(MicrophoneCaptureState.Listening, listening.MicrophoneState);

        Assert.True(MultimodalSessionLifecycle.TryTransition(listening, MultimodalSessionState.Paused,
            out var paused, out _));
        Assert.Equal(MicrophoneCaptureState.Paused, paused.MicrophoneState);
        Assert.True(MultimodalSessionLifecycle.TryTransition(paused, MultimodalSessionState.Listening,
            out var resumed, out _));
        Assert.Equal(session.SessionId, resumed.SessionId);
        Assert.Equal(MicrophoneCaptureState.Listening, resumed.MicrophoneState);

        Assert.True(MultimodalSessionLifecycle.TryTransition(resumed, MultimodalSessionState.Stopped,
            out var stopped, out _));
        Assert.Equal(session.SessionId, stopped.SessionId);
        Assert.Equal(MicrophoneCaptureState.Paused, stopped.MicrophoneState);
        Assert.True(MultimodalSessionLifecycle.TryTransition(stopped, MultimodalSessionState.Stopped,
            out var repeatedStop, out _));
        Assert.Equal(session.SessionId, repeatedStop.SessionId);
        Assert.Equal(stopped.Revision + 1, repeatedStop.Revision);
        Assert.Equal(MicrophoneCaptureState.Paused, repeatedStop.MicrophoneState);
        Assert.False(MultimodalSessionLifecycle.TryTransition(repeatedStop, MultimodalSessionState.Listening,
            out var staleListening, out _));
        Assert.Equal(repeatedStop, staleListening);
        Assert.False(MultimodalSessionLifecycle.TryTransition(repeatedStop, MultimodalSessionState.Processing,
            out var staleProcessing, out _));
        Assert.Equal(repeatedStop, staleProcessing);

        Assert.True(MultimodalSessionLifecycle.TryTransition(repeatedStop, MultimodalSessionState.Starting,
            out var restarting, out _));
        Assert.True(MultimodalSessionLifecycle.TryTransition(restarting, MultimodalSessionState.Listening,
            out var restarted, out _));
        Assert.Equal(session.SessionId, restarted.SessionId);
        Assert.Equal(session.ConversationId, restarted.ConversationId);
        Assert.Equal(repeatedStop.Revision + 2, restarted.Revision);
        Assert.Equal(MicrophoneCaptureState.Listening, restarted.MicrophoneState);

        var permissionWithdrawn = restarted with { MicrophoneState = MicrophoneCaptureState.PermissionDenied };
        Assert.True(MultimodalSessionLifecycle.TryTransition(permissionWithdrawn, MultimodalSessionState.Stopped,
            out var deniedStopped, out _));
        Assert.True(MultimodalSessionLifecycle.TryTransition(deniedStopped, MultimodalSessionState.Starting,
            out var deniedRestarting, out _));
        Assert.True(MultimodalSessionLifecycle.TryTransition(deniedRestarting, MultimodalSessionState.Listening,
            out var stillDenied, out _));
        Assert.Equal(MicrophoneCaptureState.PermissionDenied, stillDenied.MicrophoneState);

        var disconnected = restarted with { MicrophoneState = MicrophoneCaptureState.Disconnected };
        Assert.True(MultimodalSessionLifecycle.TryTransition(disconnected, MultimodalSessionState.Reconnecting,
            out var reconnecting, out _));
        Assert.Equal(MicrophoneCaptureState.Disconnected, reconnecting.MicrophoneState);
        Assert.True(MultimodalSessionLifecycle.TryTransition(reconnecting, MultimodalSessionState.Listening,
            out var recovered, out _));
        Assert.Equal(MicrophoneCaptureState.Listening, recovered.MicrophoneState);
        Assert.True(MultimodalSessionLifecycle.TryTransition(recovered, MultimodalSessionState.Stopped,
            out var stoppedDisconnected, out _));
        Assert.True(MultimodalSessionLifecycle.TryTransition(stoppedDisconnected, MultimodalSessionState.Starting,
            out var startingDisconnected, out _));
        Assert.True(MultimodalSessionLifecycle.TryTransition(startingDisconnected, MultimodalSessionState.Listening,
            out var stillDisconnected, out _));
        Assert.Equal(MicrophoneCaptureState.Listening, stillDisconnected.MicrophoneState);
    }

    [Theory]
    [InlineData(MicrophoneCaptureState.Off)]
    [InlineData(MicrophoneCaptureState.PermissionDenied)]
    [InlineData(MicrophoneCaptureState.Unavailable)]
    [InlineData(MicrophoneCaptureState.Muted)]
    public void Reconnecting_does_not_turn_inactive_microphone_state_into_listening(MicrophoneCaptureState microphone)
    {
        var session = new MultimodalSession(
            Guid.NewGuid(), Guid.NewGuid(), null, VisionVoiceMode.Conversational, null,
            "local-only", null, null, MultimodalSessionState.Listening,
            microphone, [], null, null, DateTimeOffset.UtcNow,
            1, VisionVoiceRetention.Ephemeral);

        Assert.True(MultimodalSessionLifecycle.TryTransition(session, MultimodalSessionState.Reconnecting,
            out var reconnecting, out _));
        Assert.Equal(microphone, reconnecting.MicrophoneState);
        Assert.True(MultimodalSessionLifecycle.TryTransition(reconnecting, MultimodalSessionState.Listening,
            out var reconnected, out _));
        Assert.Equal(microphone, reconnected.MicrophoneState);
        Assert.Equal(session.SessionId, reconnected.SessionId);
        Assert.Equal(session.ConversationId, reconnected.ConversationId);
        Assert.Equal(session.Revision + 2, reconnected.Revision);
    }

    [Fact]
    public void Explicitly_off_microphone_stays_off_through_start_and_failed_reconnect()
    {
        var stopped = new MultimodalSession(
            Guid.NewGuid(), Guid.NewGuid(), null, VisionVoiceMode.Conversational, null,
            "local-only", null, null, MultimodalSessionState.Stopped,
            MicrophoneCaptureState.Off, [], null, null, DateTimeOffset.UnixEpoch,
            1, VisionVoiceRetention.Ephemeral);

        Assert.True(MultimodalSessionLifecycle.TryTransition(stopped, MultimodalSessionState.Starting,
            out var starting, out _));
        Assert.Equal(MicrophoneCaptureState.Off, starting.MicrophoneState);
        Assert.True(MultimodalSessionLifecycle.TryTransition(starting, MultimodalSessionState.Listening,
            out var listening, out _));
        Assert.Equal(MicrophoneCaptureState.Off, listening.MicrophoneState);
        Assert.Equal(stopped.SessionId, listening.SessionId);

        Assert.True(MultimodalSessionLifecycle.TryTransition(stopped, MultimodalSessionState.Failed,
            out var failed, out _));
        Assert.Equal(MicrophoneCaptureState.Off, failed.MicrophoneState);
        Assert.True(MultimodalSessionLifecycle.TryTransition(failed, MultimodalSessionState.Reconnecting,
            out var reconnecting, out _));
        Assert.Equal(MicrophoneCaptureState.Off, reconnecting.MicrophoneState);
        Assert.True(MultimodalSessionLifecycle.TryTransition(reconnecting, MultimodalSessionState.Listening,
            out var recovered, out _));
        Assert.Equal(MicrophoneCaptureState.Off, recovered.MicrophoneState);
        Assert.Equal(stopped.SessionId, recovered.SessionId);
        Assert.Equal(stopped.ConversationId, recovered.ConversationId);
    }

    [Fact]
    public async Task Canonical_conversational_session_pauses_reconnects_and_resumes_with_mic_off()
    {
        var conversationId = Guid.NewGuid();
        var service = new VisionVoiceSessionService(
            new MultimodalSessionStore(new AtomicTestSettingsStore()));
        var started = await service.StartAsync(conversationId, null, VisionVoiceMode.Conversational,
            "local-only", isLocalOnly: true, cancellationToken: CancellationToken.None);
        Assert.True(started.IsSuccess, started.Error?.Message);
        var initial = started.Value!;
        Assert.Equal(MultimodalSessionState.Starting, initial.State);
        Assert.Equal(MicrophoneCaptureState.Off, initial.MicrophoneState);

        async Task<MultimodalSession> Transition(MultimodalSession session, MultimodalSessionState next)
        {
            var result = await service.TransitionAsync(session.SessionId, session.Revision, next,
                CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error?.Message);
            return result.Value!;
        }

        var listening = await Transition(initial, MultimodalSessionState.Listening);
        var paused = await Transition(listening, MultimodalSessionState.Paused);
        var reconnecting = await Transition(paused, MultimodalSessionState.Reconnecting);
        var resumed = await Transition(reconnecting, MultimodalSessionState.Listening);

        Assert.Equal(initial.SessionId, resumed.SessionId);
        Assert.Equal(conversationId, resumed.ConversationId);
        Assert.Equal(VisionVoiceMode.Conversational, resumed.VoiceMode);
        Assert.Equal(5, resumed.Revision);
        Assert.Equal(MultimodalSessionState.Listening, resumed.State);
        Assert.Equal(MicrophoneCaptureState.Off, resumed.MicrophoneState);

        var reopened = await service.GetSessionAsync(initial.SessionId, CancellationToken.None);
        Assert.True(reopened.IsSuccess, reopened.Error?.Message);
        Assert.Equal(resumed.SessionId, reopened.Value!.SessionId);
        Assert.Equal(resumed.ConversationId, reopened.Value.ConversationId);
        Assert.Equal(resumed.VoiceMode, reopened.Value.VoiceMode);
        Assert.Equal(resumed.State, reopened.Value.State);
        Assert.Equal(resumed.MicrophoneState, reopened.Value.MicrophoneState);
        Assert.Equal(resumed.Revision, reopened.Value.Revision);
    }

    [Fact]
    public async Task Monologue_pause_resume_uses_original_playback_and_persists_confirmed_position()
    {
        var service = new VisionVoiceSessionService(
            new MultimodalSessionStore(new AtomicTestSettingsStore()));
        var conversationId = Guid.NewGuid();
        var started = await service.StartAsync(conversationId, null, VisionVoiceMode.Monologue,
            "local-only", isLocalOnly: true, cancellationToken: CancellationToken.None);
        Assert.True(started.IsSuccess, started.Error?.Message);
        var startedSession = started.Value!;
        var planned = await service.PlanMonologueAsync(startedSession.SessionId, startedSession.Revision,
            "A short briefing", null, ["Opening", "Close"], [], CancellationToken.None);
        Assert.True(planned.IsSuccess, planned.Error?.Message);
        var plan = planned.Value!;
        var activated = await service.RecordMonologueProgressAsync(plan.SessionId, plan.Revision,
            plan.Monologue!.RunId, 0, TimeSpan.Zero, paused: false, cancellationToken: CancellationToken.None);
        Assert.True(activated.IsSuccess, activated.Error?.Message);
        var initial = activated.Value!;
        var runId = initial.Monologue!.RunId;
        var provider = new RetainedOriginalSpeechOutput();

        var original = await MonologueOriginalPlayback.StartAsync(service, provider,
            initial.SessionId, initial.Revision, runId, 0,
            "The original briefing audio", "voice.test", null, () => true, CancellationToken.None);
        var paused = await original.PauseAsync(CancellationToken.None);
        Assert.True(paused.IsSuccess, paused.Error?.Message);
        var pausedSession = paused.Value!;
        var pausedRun = pausedSession.Monologue!;
        Assert.True(pausedRun.IsPaused);
        Assert.Equal(TimeSpan.FromSeconds(13), pausedRun.Position);

        var resumed = await original.ResumeAsync(CancellationToken.None);
        Assert.True(resumed.IsSuccess, resumed.Error?.Message);
        var resumedSession = resumed.Value!;
        var resumedRun = resumedSession.Monologue!;
        Assert.False(resumedRun.IsPaused);
        Assert.Equal(TimeSpan.FromSeconds(13), resumedRun.Position);
        Assert.Equal(initial.SessionId, resumedSession.SessionId);
        Assert.Equal(conversationId, resumedSession.ConversationId);
        Assert.Equal(VisionVoiceMode.Monologue, resumedSession.VoiceMode);
        Assert.Equal(new[] { "Opening", "Close" }, resumedRun.Sections);
        Assert.Equal(0, resumedRun.CurrentSection);
        Assert.Equal(1, provider.StartCount);
        Assert.Equal(1, provider.Playback.PauseCount);
        Assert.Equal(1, provider.Playback.ResumeCount);

        var persisted = await service.GetSessionAsync(initial.SessionId, CancellationToken.None);
        Assert.True(persisted.IsSuccess, persisted.Error?.Message);
        var persistedSession = persisted.Value!;
        Assert.Equal(resumedSession.Revision, persistedSession.Revision);
        Assert.Equal(resumedRun.PlaybackReceipt, persistedSession.Monologue!.PlaybackReceipt);
    }

    [Fact]
    public async Task Monologue_output_permission_failure_preserves_the_original_session_checkpoint()
    {
        var service = new VisionVoiceSessionService(
            new MultimodalSessionStore(new AtomicTestSettingsStore()));
        var started = await service.StartAsync(Guid.NewGuid(), null, VisionVoiceMode.Monologue,
            "local-only", isLocalOnly: true, cancellationToken: CancellationToken.None);
        Assert.True(started.IsSuccess, started.Error?.Message);
        var initial = started.Value!;
        var planned = await service.PlanMonologueAsync(initial.SessionId, initial.Revision,
            "Preserve the original checkpoint", null, ["Opening", "Close"], [], CancellationToken.None);
        Assert.True(planned.IsSuccess, planned.Error?.Message);
        var plan = planned.Value!;
        var plannedRun = plan.Monologue!;
        var active = await service.RecordMonologueProgressAsync(plan.SessionId, plan.Revision,
            plannedRun.RunId, 0, TimeSpan.FromSeconds(7), paused: false,
            cancellationToken: CancellationToken.None);
        Assert.True(active.IsSuccess, active.Error?.Message);
        var beforeFailure = active.Value!;
        var beforeRun = beforeFailure.Monologue!;
        var provider = new RetainedOriginalSpeechOutput
        {
            StartFailure = new UnauthorizedAccessException("The selected output device is not permitted.")
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => MonologueOriginalPlayback.StartAsync(
            service, provider, beforeFailure.SessionId, beforeFailure.Revision, plannedRun.RunId, 0,
            "Original narration", "voice.test", null, () => true, CancellationToken.None));

        var afterFailure = await service.GetSessionAsync(beforeFailure.SessionId, CancellationToken.None);
        Assert.True(afterFailure.IsSuccess, afterFailure.Error?.Message);
        var afterSession = afterFailure.Value!;
        var afterRun = afterSession.Monologue!;
        Assert.Equal(beforeFailure.Revision, afterSession.Revision);
        Assert.Equal(beforeFailure.SessionId, afterSession.SessionId);
        Assert.Equal(beforeFailure.ConversationId, afterSession.ConversationId);
        Assert.Equal(beforeRun.RunId, afterRun.RunId);
        Assert.Equal(TimeSpan.FromSeconds(7), afterRun.Position);
        Assert.False(afterRun.IsPaused);
        Assert.Equal(beforeRun.PlaybackReceipt, afterRun.PlaybackReceipt);
    }

    [Fact]
    public void Recreated_voice_presentation_hydrates_the_existing_runtime_session()
    {
        var now = new DateTimeOffset(2026, 8, 17, 19, 0, 0, TimeSpan.Zero);
        var conversation = new Conversation(
            Guid.NewGuid(),
            HavenMode.Chat,
            ConversationKind.Call,
            "Voice call",
            null,
            null,
            false,
            false,
            now,
            now);
        var session = new CallSession(
            Guid.NewGuid(),
            conversation.Id,
            "voice-model",
            "mic-one",
            "speaker-one",
            "Voice One",
            CallInputMode.HandsFree,
            true,
            CallSessionStatus.Active,
            now);
        var coordinator = new ActiveCallCoordinator(session, conversation);

        using var viewModel = new InChatCallWidgetViewModel(coordinator, new StubConversationRepository());
        using var scene = new GlobalCallHavenScene(viewModel);

        Assert.True(viewModel.IsActive);
        Assert.True(viewModel.IsOpen);
        Assert.True(viewModel.IsMuted);
        Assert.Equal(conversation.Id, viewModel.LinkedCallConversationId);
        Assert.Equal("Speaking", viewModel.Status);
        Assert.Equal("End", scene.CallButton.Content);
        Assert.Equal(ButtonVariant.Danger, scene.CallButton.Variant);
        Assert.Equal("Mic off", scene.MuteButton.Content);
        Assert.Equal(0, coordinator.StartCount);
    }

    [Fact]
    public async Task Active_push_to_talk_voice_executes_runtime_controls()
    {
        var now = new DateTimeOffset(2026, 8, 17, 19, 0, 0, TimeSpan.Zero);
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Call, "Voice call", null, null, false, false, now, now);
        var session = new CallSession(Guid.NewGuid(), conversation.Id, "voice-model", "mic-one", "speaker-one", "Voice One", CallInputMode.PushToTalk, true, CallSessionStatus.Active, now);
        var coordinator = new ActiveCallCoordinator(session, conversation, muted: false);
        using var viewModel = new InChatCallWidgetViewModel(coordinator, new StubConversationRepository());
        using var scene = new GlobalCallHavenScene(viewModel);

        Assert.Equal(CallInputMode.PushToTalk, viewModel.InputMode);
        Assert.True(viewModel.CanPushToTalk);
        scene.Refresh();
        Assert.True(scene.PushToTalkButton.GetValue(HavenProperties.Enabled));

        await viewModel.TogglePushToTalkCommand.ExecuteAsync();
        Assert.True(viewModel.IsPushToTalkRecording);
        Assert.Equal(1, coordinator.BeginPushToTalkCount);
        await viewModel.TogglePushToTalkCommand.ExecuteAsync();
        Assert.False(viewModel.IsPushToTalkRecording);
        Assert.Equal(1, coordinator.EndPushToTalkCount);

        await viewModel.InterruptCommand.ExecuteAsync();
        Assert.Equal(1, coordinator.InterruptCount);
        await viewModel.TogglePauseCommand.ExecuteAsync();
        Assert.True(viewModel.IsPaused);
        Assert.Equal(1, coordinator.PauseCount);
        await viewModel.TogglePauseCommand.ExecuteAsync();
        Assert.False(viewModel.IsPaused);
        Assert.Equal(1, coordinator.ResumeCount);
    }

    private sealed class StubConversationRepository : IConversationRepository
    {
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Conversation>>([]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Conversation?>(null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RetainedCallbackSpeechInput : ISpeechInputService
    {
        public List<Func<SpeechInputEvent, CancellationToken, Task>> Callbacks { get; } = [];
        public int StopCount { get; private set; }
        public bool BlockStart { get; init; }
        public bool IsCapturing { get; private set; }
        public TaskCompletionSource<bool> StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsAvailable => true;
        public string? UnavailableReason => null;
        public IReadOnlyList<CallAudioDevice> Devices { get; } = [];
        public async Task StartAsync(SpeechInputOptions options, Func<SpeechInputEvent, CancellationToken, Task> onEvent,
            CancellationToken cancellationToken)
        {
            Callbacks.Add(onEvent);
            if (BlockStart)
            {
                StartEntered.TrySetResult(true);
                await ReleaseStart.Task.ConfigureAwait(false);
            }
            IsCapturing = true;
        }
        public Task BeginPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EndPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            IsCapturing = false;
            return Task.CompletedTask;
        }
    }

    private sealed class TestCallRepository : ICallRepository
    {
        public Task UpsertAsync(CallSession session, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CallSession?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<CallSession?>(null);
        public Task<IReadOnlyList<CallSession>> GetRecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CallSession>>([]);
    }

    private sealed class TestConversationRepository : IConversationRepository
    {
        public List<ChatMessage> Messages { get; } = [];
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Conversation>>([]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Conversation?>(null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
        public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class AtomicTestSettingsStore : IVersionedSettingsStore, IVersionedSettingsCompareExchange
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
        private int _version;

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_values.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : null);
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values[key] = JsonSerializer.Serialize(value);
            _version++;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_values.Remove(key)) _version++;
            return Task.CompletedTask;
        }

        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new SettingsExportManifest
            {
                Version = _version,
                Settings = new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase)
            });
        }

        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var item in manifest.Settings) _values[item.Key] = item.Value;
            _version++;
            return Task.FromResult(new SettingsImportResult(true, manifest.Settings, "Imported"));
        }

        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expectedJson,
            string? replacementJson, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var exists = _values.TryGetValue(key, out var currentJson);
            if (exists ? !string.Equals(currentJson, expectedJson, StringComparison.Ordinal) : expectedJson is not null)
                return Task.FromResult(new SettingsCompareExchangeResult(false, currentJson, _version));
            if (replacementJson is null) _values.Remove(key);
            else _values[key] = replacementJson;
            _version++;
            return Task.FromResult(new SettingsCompareExchangeResult(true, replacementJson, _version));
        }
    }

    private sealed class RetainedOriginalSpeechOutput : IContinuableSpeechOutputService, IOriginalSpeechPlaybackReceiptIssuer
    {
        public int StartCount { get; private set; }
        public Exception? StartFailure { get; init; }
        public RetainedOriginalSpeechPlayback Playback { get; private set; } = null!;
        public bool CanContinueVoice(string voiceId) => voiceId == "voice.test";
        public Task<ISpeechPlaybackContinuation> StartContinuableAsync(string text, string? voiceName,
            string? outputDeviceId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            if (StartFailure is { } error) return Task.FromException<ISpeechPlaybackContinuation>(error);
            Playback = new RetainedOriginalSpeechPlayback("voice.test");
            return Task.FromResult<ISpeechPlaybackContinuation>(Playback);
        }
        public bool IsOriginalPlayback(ISpeechPlaybackContinuation playback) => ReferenceEquals(Playback, playback);
        public bool WasIssuedPlayback(ISpeechPlaybackContinuation playback) => ReferenceEquals(Playback, playback);
    }

    private sealed class RetainedOriginalSpeechPlayback(string voiceId) : ISpeechPlaybackContinuation
    {
        private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid PlaybackId { get; } = Guid.NewGuid();
        public string VoiceId { get; } = voiceId;
        public Task Completion => _completion.Task;
        public int PauseCount { get; private set; }
        public int ResumeCount { get; private set; }
        public TimeSpan Position { get; private set; }
        public bool IsPaused { get; private set; }

        public Task<SpeechPlaybackCheckpoint> PauseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PauseCount++;
            IsPaused = true;
            Position = TimeSpan.FromSeconds(13);
            return Task.FromResult(new SpeechPlaybackCheckpoint(PlaybackId, VoiceId, Position, IsPaused));
        }

        public Task<SpeechPlaybackCheckpoint> ResumeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResumeCount++;
            IsPaused = false;
            return Task.FromResult(new SpeechPlaybackCheckpoint(PlaybackId, VoiceId, Position, IsPaused));
        }
    }

    private sealed class UnusedOllamaClient : IOllamaClient
    {
        public bool BlockStream { get; init; }
        public bool HoldFirstStreamAfterPartial { get; init; }
        public int StreamCallCount { get; private set; }
        public List<OllamaChatRequest> Requests { get; } = [];
        public TaskCompletionSource<bool> StreamEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseStream { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            StreamCallCount++;
            Requests.Add(request);
            StreamEntered.TrySetResult(true);
            if (HoldFirstStreamAfterPartial && StreamCallCount == 1)
            {
                yield return "old ephemeral assistant fragment";
                await ReleaseStream.Task.ConfigureAwait(false);
                yield break;
            }
            if (BlockStream) await ReleaseStream.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield break;
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromException<OllamaToolResponse>(new NotSupportedException());
    }

    private sealed class UnusedSpeechOutput : ISpeechOutputService
    {
        public bool IsAvailable => false;
        public string? UnavailableReason => "Not used by this regression.";
        public IReadOnlyList<CallAudioDevice> Devices { get; } = [];
        public IReadOnlyList<CallVoice> Voices { get; } = [];
        public Task SpeakAsync(string text, string? voiceName, string? outputDeviceId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class UnusedScreenShare : IScreenShareService
    {
        public bool IsSupported => false;
        public bool IsSharing => false;
        public string? UnavailableReason => "Not used by this regression.";
        public ScreenShareSource? CurrentSource => null;
        public event EventHandler? SourceClosed { add { } remove { } }
        public event EventHandler<ScreenShareSnapshotEventArgs>? SnapshotAvailable { add { } remove { } }
        public Task<ScreenShareSource> StartWithSystemPickerAsync(CancellationToken cancellationToken) =>
            Task.FromException<ScreenShareSource>(new NotSupportedException());
        public Task<ScreenShareSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ScreenShareSnapshot?>(null);
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ActiveCallCoordinator(CallSession session, Conversation conversation, bool muted = true) : ICallCoordinator
    {
        public int StartCount { get; private set; }
        public int BeginPushToTalkCount { get; private set; }
        public int EndPushToTalkCount { get; private set; }
        public int PauseCount { get; private set; }
        public int ResumeCount { get; private set; }
        public int InterruptCount { get; private set; }
        private CallState _state = CallState.Speaking;
        public CallState State => _state;
        public CallSession? CurrentSession => session;
        public Conversation? CurrentConversation => conversation;
        public CallCapabilities Capabilities { get; } = new(
            true,
            true,
            true,
            null,
            null,
            null,
            [new CallAudioDevice("mic-one", "Mic One", true)],
            [new CallAudioDevice("speaker-one", "Speaker One", true)],
            [new CallVoice("voice-one", "Voice One", null, true)]);
        public bool IsActive => true;
        public bool IsMuted => muted;
        public bool IsScreenSharing => true;

        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        public event EventHandler<CallTranscriptEventArgs>? TranscriptChanged { add { } remove { } }
        public event EventHandler<CallAudioLevelEventArgs>? AudioLevelChanged { add { } remove { } }
        public event EventHandler<ScreenShareSnapshotEventArgs>? ScreenPreviewChanged { add { } remove { } }

        public Task<CallSession> StartAsync(CallStartOptions options, SpeechModelInfo? speechModel, CancellationToken cancellationToken)
        {
            StartCount++;
            return Task.FromResult(session);
        }

        public Task SubmitTextAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task BeginPushToTalkAsync(CancellationToken cancellationToken) { BeginPushToTalkCount++; return Task.CompletedTask; }
        public Task EndPushToTalkAsync(CancellationToken cancellationToken) { EndPushToTalkCount++; return Task.CompletedTask; }
        public Task SetMutedAsync(bool muted, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken) { PauseCount++; _state = CallState.Paused; StateChanged?.Invoke(this, new CallStateChangedEventArgs(_state, "Paused")); return Task.CompletedTask; }
        public Task ResumeAsync(CancellationToken cancellationToken) { ResumeCount++; _state = CallState.Listening; StateChanged?.Invoke(this, new CallStateChangedEventArgs(_state, "Listening")); return Task.CompletedTask; }
        public Task StartScreenShareAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopScreenShareAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task InterruptAsync(CancellationToken cancellationToken) { InterruptCount++; return Task.CompletedTask; }
        public Task EndAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
