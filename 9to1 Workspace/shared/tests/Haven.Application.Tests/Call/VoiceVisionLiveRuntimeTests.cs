using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Haven.Application.Call;
using Haven.Core;

namespace Haven.Application.Tests.Call;

/// <summary>
/// Focused tests for the Voice &amp; Vision single-owner runtime contract:
/// attach-existing identity without a second Start, ID preservation, no second runtime,
/// start/end/close causal ordering, late-callback suppression after End, and camera/screen
/// capture permission gates (denied/absent backend yields zero capture starts; stopping
/// share yields zero later frames). All tests carry the xUnit cancellation projection so a
/// cancelled test run aborts promptly; the deliberate borrower-cancellation test uses its own
/// token so it can cancel only that borrower's wait, never the shared original.
/// </summary>
public sealed class VoiceVisionLiveRuntimeTests
{
    [Fact]
    public async Task Attach_existing_identity_without_calling_Start_again()
    {
        var coordinator = ActiveCoordinator();
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());

        var result = await runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.True(result.AttachedExisting);
        Assert.Equal(0, coordinator.StartCount);

        // Reopening the same conversation preserves one canonical SessionID and never starts again.
        var again = await runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);
        Assert.True(again.Succeeded);
        Assert.True(again.AttachedExisting);
        Assert.Equal(result.SessionId, again.SessionId);
        Assert.Equal(0, coordinator.StartCount);
    }

    [Fact]
    public async Task Second_presentation_reuses_one_runtime_and_one_SessionID()
    {
        var coordinator = NewCoordinator();
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());

        var first = await runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded);
        Assert.False(first.AttachedExisting);
        Assert.Equal(1, coordinator.StartCount);

        // A second surface opens while the session is live: it must attach, not start again.
        var second = await runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);
        Assert.True(second.Succeeded);
        Assert.True(second.AttachedExisting);
        Assert.Equal(first.SessionId, second.SessionId);
        Assert.Equal(1, coordinator.StartCount);
    }

    [Fact]
    public async Task Closing_a_presentation_is_not_End_and_End_drains_the_runtime()
    {
        var coordinator = NewCoordinator();
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());
        await runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);

        // Close presentation (pill/full) must NOT end the runtime.
        runtime.SetPresentation(VoiceVisionPresentationKind.None);
        Assert.Equal(0, coordinator.EndCount);

        // End is the explicit stop/drain.
        var end = await runtime.EndAsync(TestContext.Current.CancellationToken);
        Assert.True(end.Succeeded);
        Assert.Equal(1, coordinator.EndCount);
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task Late_callbacks_after_End_do_not_resurrect_a_live_session()
    {
        var coordinator = NewCoordinator();
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());
        await runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);
        await runtime.EndAsync(TestContext.Current.CancellationToken);

        coordinator.RaiseAudioLevel(0.9);
        coordinator.RaiseState(CallState.Listening, "late");

        Assert.False(runtime.State.IsActive);
        Assert.Equal(Guid.Empty, runtime.State.SessionId);
        Assert.Equal(MultimodalSessionState.Stopped, runtime.State.State);
    }

    [Fact]
    public async Task Unsupported_or_denied_camera_yields_zero_capture_starts()
    {
        var coordinator = NewCoordinator();
        // Default backend profile reports SupportsCamera=false truthfully.
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());

        Assert.False(runtime.State.CanUseCamera);
        var camera = await runtime.SetCameraAsync(true, TestContext.Current.CancellationToken);

        Assert.False(camera.Succeeded);
        Assert.False(runtime.State.HasCameraSource);
        Assert.Empty(runtime.State.VisualSources);
        // A denied/absent camera never begins capture.
        Assert.Contains(camera.Error!.Code, new[] { VisionVoiceErrorCode.CameraUnavailable, VisionVoiceErrorCode.PermissionRequired });
    }

    [Fact]
    public async Task Stopping_screen_share_stops_new_frames()
    {
        var coordinator = NewCoordinator();
        coordinator.Capabilities = coordinator.Capabilities with { CanShareScreen = true };
        var backend = VoiceVisionBackendProfile.Default with { SupportsScreenShare = true };
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore(), backend);

        var start = await runtime.SetScreenShareAsync(true, TestContext.Current.CancellationToken);
        Assert.True(start.Succeeded);
        Assert.True(runtime.State.IsScreenSharing);
        Assert.Contains(runtime.State.VisualSources, source => source.Type is VisualSourceType.DisplayShare);

        var stop = await runtime.SetScreenShareAsync(false, TestContext.Current.CancellationToken);
        Assert.True(stop.Succeeded);
        Assert.Equal(1, coordinator.StopScreenShareCount);
        Assert.Empty(runtime.State.VisualSources);

        // Even a late snapshot event cannot restore frames after stop.
        coordinator.RaiseScreenPreview();
        Assert.Empty(runtime.State.VisualSources);
    }

    [Fact]
    public async Task Concurrent_starts_coalesce_to_one_session()
    {
        var coordinator = NewCoordinator();
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());

        var t1 = runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);
        var t2 = runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);

        var r1 = await t1;
        var r2 = await t2;

        Assert.True(r1.Succeeded);
        Assert.True(r2.Succeeded);
        Assert.Equal(r1.SessionId, r2.SessionId);
        // At most one Start ran; the loser attached to the same session.
        Assert.Equal(1, coordinator.StartCount);
    }

    [Fact]
    public async Task Simultaneous_attach_to_already_live_session_shares_one_canonical_SessionID()
    {
        var coordinator = ActiveCoordinator();
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());

        var t1 = runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);
        var t2 = runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);

        var r1 = await t1;
        var r2 = await t2;

        Assert.True(r1.Succeeded);
        Assert.True(r2.Succeeded);
        Assert.Equal(r1.SessionId, r2.SessionId);
        // The attach path must never invoke the real coordinator Start for either caller.
        Assert.Equal(0, coordinator.StartCount);
        Assert.True(r1.AttachedExisting);
        Assert.True(r2.AttachedExisting);
    }

    [Fact]
    public async Task End_joins_a_held_Start_before_draining()
    {
        var coordinator = NewCoordinator();
        coordinator.StartRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StartEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());

        var startTask = runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);
        // Deterministic barrier: the coordinator has entered Start and holds the lifecycle gate.
        await coordinator.StartEntered!.Task;

        var endTask = runtime.EndAsync(TestContext.Current.CancellationToken);
        // End must NOT ACK while the shared Start is still settling.
        Assert.False(endTask.IsCompleted);
        Assert.Equal(0, coordinator.EndCount);

        // Release the held Start; only then End settles and drains the real runtime.
        coordinator.StartRelease.SetResult(true);
        var start = await startTask;
        var end = await endTask;

        Assert.True(start.Succeeded);
        Assert.True(end.Succeeded);
        Assert.Equal(1, coordinator.EndCount);
        Assert.False(runtime.State.IsActive);

        var si = coordinator.Events.IndexOf("start-enter");
        var sc = coordinator.Events.IndexOf("start-complete");
        var ee = coordinator.Events.IndexOf("end-enter");
        var ec = coordinator.Events.IndexOf("end-complete");
        Assert.True(si >= 0 && sc > si && ee > sc && ec > ee);
    }

    [Fact]
    public async Task Dispose_joins_a_held_Start_and_drains_before_release()
    {
        var coordinator = NewCoordinator();
        coordinator.StartRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StartEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());

        var startTask = runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);
        await coordinator.StartEntered!.Task;

        var disposeTask = runtime.DisposeAsync().AsTask();
        // Dispose waits for the held Start to settle before draining; it cannot ACK early.
        Assert.False(disposeTask.IsCompleted);
        Assert.Equal(0, coordinator.EndCount);

        coordinator.StartRelease.SetResult(true);
        await startTask;
        await disposeTask;

        Assert.Equal(1, coordinator.EndCount);
    }

    [Fact]
    public async Task Cancelled_waiting_borrower_does_not_cancel_or_settle_a_shared_Start()
    {
        var coordinator = NewCoordinator();
        coordinator.StartRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StartEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());

        var owner = runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);
        await coordinator.StartEntered!.Task; // owner holds the gate; the borrower will queue

        // Deliberate borrower cancellation: only the borrower's wait uses this token, so the
        // shared original is never cancelled or settled by it.
        using var cts = new CancellationTokenSource();
        var borrower = runtime.StartAsync(Request(ConvoId), cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => borrower);

        // The shared, still-owned Start is unaffected and settles normally.
        coordinator.StartRelease.SetResult(true);
        var owned = await owner;
        Assert.True(owned.Succeeded);
        Assert.Equal(1, coordinator.StartCount);
        Assert.True(runtime.State.IsActive);
    }

    [Fact]
    public async Task Reentrant_End_is_idempotent_and_drains_once()
    {
        var coordinator = NewCoordinator();
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());
        await runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);

        var first = await runtime.EndAsync(TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded);
        Assert.Equal(1, coordinator.EndCount);

        var second = await runtime.EndAsync(TestContext.Current.CancellationToken);
        Assert.True(second.Succeeded);
        Assert.Equal(1, coordinator.EndCount);
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task Start_fault_marks_session_failed_and_publishes_no_active_capture()
    {
        var coordinator = NewCoordinator();
        coordinator.FailStart = true;
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());

        var start = await runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);

        Assert.False(start.Succeeded);
        Assert.Equal(VisionVoiceErrorCode.UnknownFailure, start.Error!.Code);
        Assert.False(runtime.State.IsActive);
        Assert.Equal(MultimodalSessionState.Failed, runtime.State.State);
    }

    [Fact]
    public void Coordinator_transcript_event_is_raised_to_subscribed_observer()
    {
        var coordinator = NewCoordinator();
        CallTranscriptEventArgs? received = null;
        coordinator.TranscriptChanged += (_, args) => received = args;

        coordinator.RaiseTranscript(Guid.NewGuid(), MessageRole.User, "hello", isDelta: false, isFinal: true);

        Assert.NotNull(received);
        Assert.Equal("hello", received!.Text);
        Assert.Equal(MessageRole.User, received.Role);
        Assert.True(received.IsFinal);
    }

    [Fact]
    public async Task Dispose_coalesces_one_real_stop_and_queued_Start_End_fail()
    {
        var coordinator = NewCoordinator();
        coordinator.StartRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StartEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());

        var startTask = runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);
        await coordinator.StartEntered!.Task;

        var dispose1 = runtime.DisposeAsync().AsTask();
        var dispose2 = runtime.DisposeAsync().AsTask();
        var queuedStart = runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);
        var queuedEnd = runtime.EndAsync(TestContext.Current.CancellationToken);

        // Dispose must still wait for the held Start; it cannot settle or ACK early.
        Assert.False(dispose1.IsCompleted);
        Assert.False(dispose2.IsCompleted);

        coordinator.StartRelease.SetResult(true);
        var start = await startTask;
        Assert.True(start.Succeeded);

        await dispose1;
        await dispose2;

        // Exactly one real stop ran for the single live runtime.
        Assert.Equal(1, coordinator.EndCount);

        // Queued Start/End were sealed and failed truthfully (no disposed-semaphore release).
        var sf = await queuedStart;
        Assert.False(sf.Succeeded);
        var ef = await queuedEnd;
        Assert.False(ef.Succeeded);

        Assert.False(runtime.State.IsActive);
        Assert.Equal(Guid.Empty, runtime.State.SessionId);
    }

    [Fact]
    public async Task Faulting_stop_retains_custody_and_does_not_ACK_success()
    {
        var coordinator = NewCoordinator();
        coordinator.FailEnd = true;
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());
        await runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);

        var end = await runtime.EndAsync(TestContext.Current.CancellationToken);

        // End must not ACK success when the real stop fails; custody of the live session is kept.
        Assert.False(end.Succeeded);
        Assert.True(runtime.State.IsActive);
        Assert.NotEqual(Guid.Empty, runtime.State.SessionId);
    }

    [Fact]
    public async Task Faulting_stop_during_dispose_retains_custody_and_throws()
    {
        var coordinator = NewCoordinator();
        coordinator.FailEnd = true;
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());
        await runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);

        // A failed real stop must not be swallowed: disposal surfaces it and retains custody.
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => runtime.DisposeAsync().AsTask());

        Assert.True(runtime.State.IsActive);
        Assert.NotEqual(Guid.Empty, runtime.State.SessionId);
    }

    [Fact]
    public async Task Mapping_failure_is_reported_truthfully_without_active_capture()
    {
        var coordinator = NewCoordinator();
        var runtime = CreateRuntime(coordinator, new FailingMappingStore());

        var result = await runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);

        // A failure to persist the canonical mapping must surface as a truthful start failure,
        // never as a silently started session or an exception escaping to the caller.
        Assert.False(result.Succeeded);
        Assert.False(runtime.State.IsActive);
        Assert.Empty(runtime.State.VisualSources);
    }

    [Fact]
    public async Task Reentrant_close_is_safe_and_late_callbacks_do_not_resurrect()
    {
        var coordinator = NewCoordinator();
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());
        await runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);

        await runtime.DisposeAsync();
        await runtime.DisposeAsync(); // reentrant close is safe/idempotent

        Assert.Equal(1, coordinator.EndCount);

        // Late callbacks after dispose must not resurrect a live session.
        coordinator.RaiseAudioLevel(0.9);
        coordinator.RaiseState(CallState.Listening, "late");

        Assert.False(runtime.State.IsActive);
        Assert.Equal(Guid.Empty, runtime.State.SessionId);
    }

    [Fact]
    public async Task End_that_inactivates_then_throws_is_not_ACKed_as_success_across_dispose_borrowers()
    {
        // Real EndCore marks the call Completed BEFORE its awaited durable settlement, so a session
        // can become inactive and STILL throw. The retained stop fault must govern, not IsActive.
        var coordinator = NewCoordinator();
        coordinator.FailEndAfterIdle = true;
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());
        await runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);

        // Two concurrent disposal borrowers share the real teardown original; both must observe the
        // retained stop/persistence fault, never an ACK of success.
        var d1 = runtime.DisposeAsync().AsTask();
        var d2 = runtime.DisposeAsync().AsTask();

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => d1);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => d2);

        // Failed durable settlement retains custody: the live session is not cleared/unhooked.
        Assert.NotEqual(Guid.Empty, runtime.State.SessionId);
        Assert.Equal(MultimodalSessionState.Listening, runtime.State.State);
    }

    [Fact]
    public async Task Concurrent_start_end_dispose_storm_never_leaks_disposed_semaphore_errors()
    {
        var coordinator = NewCoordinator();
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());

        var tasks = new List<Task>();
        for (var i = 0; i < 40; i++)
        {
            tasks.Add(Exercise(() => runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken)));
            tasks.Add(Exercise(() => runtime.EndAsync(TestContext.Current.CancellationToken)));
        }
        tasks.Add(ExerciseDispose());

        await Task.WhenAll(tasks);

        // After dispose the runtime is sealed: further Start/End return typed failures, never
        // disposed-semaphore exceptions or other thrown lifecycle errors.
        var start = await runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);
        Assert.False(start.Succeeded);
        var end = await runtime.EndAsync(TestContext.Current.CancellationToken);
        Assert.False(end.Succeeded);

        static async Task Exercise(Func<Task> operation)
        {
            try { await operation(); }
            catch (OperationCanceledException) { }
            // Any other exception (e.g. ObjectDisposedException) propagates and fails the test.
        }

        async Task ExerciseDispose()
        {
            try { await runtime.DisposeAsync(); }
            catch (InvalidOperationException) { } // retained stop-fault leg
            catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Attach_existing_mapping_read_fault_returns_structured_failure_without_a_second_capture()
    {
        var coordinator = ActiveCoordinator();
        var runtime = CreateRuntime(coordinator, new FailingReadMappingStore());

        var result = await runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(VisionVoiceErrorCode.UnknownFailure, result.Error!.Code);
        Assert.Equal(0, coordinator.StartCount);           // never started a second capture
        Assert.Equal(Guid.Empty, runtime.State.SessionId); // no invented identity published
    }

    [Fact]
    public async Task Attach_existing_mapping_write_fault_returns_structured_failure()
    {
        var coordinator = ActiveCoordinator();
        var runtime = CreateRuntime(coordinator, new FailingMappingStore());

        var result = await runtime.StartAsync(Request(ConvoId), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(VisionVoiceErrorCode.UnknownFailure, result.Error!.Code);
        Assert.Equal(0, coordinator.StartCount);
        Assert.Equal(Guid.Empty, runtime.State.SessionId);
    }

    [Fact]
    public async Task Attach_existing_held_mapping_read_obeys_cancellation()
    {
        var coordinator = ActiveCoordinator();
        var runtime = CreateRuntime(coordinator, new HeldMappingReadStore());

        using var cts = new CancellationTokenSource();
        var startTask = runtime.StartAsync(Request(ConvoId), cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startTask);
    }

    // -- fixed conversation identity --
    private static readonly Guid ConvoId = new("11111111-1111-1111-1111-111111111111");

    private static FakeCallCoordinator NewCoordinator() => new()
    {
        Capabilities = new CallCapabilities(
            HasSpeechInput: true,
            HasSpeechOutput: true,
            CanShareScreen: false,
            SpeechInputUnavailableReason: null,
            SpeechOutputUnavailableReason: null,
            ScreenShareUnavailableReason: null,
            InputDevices: [],
            OutputDevices: [],
            Voices: [new CallVoice("v1", "Neutral")])
    };

    private static FakeCallCoordinator ActiveCoordinator()
    {
        var coordinator = NewCoordinator();
        coordinator.CurrentConversation = new Conversation(
            ConvoId, HavenMode.Chat, ConversationKind.Chat, "Voice", null, null, false, false,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        coordinator.State = CallState.Listening;
        coordinator.CurrentSession = new CallSession(Guid.NewGuid(), ConvoId, "model", null, null,
            "Neutral", CallInputMode.HandsFree, false, CallSessionStatus.Active, DateTimeOffset.UtcNow);
        return coordinator;
    }

    private static VoiceVisionLiveRuntime CreateRuntime(
        FakeCallCoordinator coordinator,
        IVersionedSettingsStore store,
        VoiceVisionBackendProfile? backend = null)
    {
        var service = new VisionVoiceSessionService(new MultimodalSessionStore(store));
        return new VoiceVisionLiveRuntime(coordinator, service, store, backendProfile: backend);
    }

    private static VoiceVisionStartRequest Request(Guid conversationId) => new(
        conversationId,
        SpaceId: null,
        VisionVoiceMode.Conversational,
        new CallStartOptions(
            new ModelDescriptor("test", 1, "test", "1B", "q4", new HashSet<ToolCapability>(), DateTimeOffset.UtcNow),
            InputMode: CallInputMode.HandsFree),
        EffectiveModelPolicy: "local-test",
        IsLocalOnly: true);

    /// <summary>Dual-interface in-memory settings store required by the atomic session store and the
    /// runtime's canonical SessionID mapping. The existing test store only implements
    /// IVersionedSettingsStore; here values are kept as serialized JSON so CompareExchange
    /// semantics match the production atomic store.</summary>
    private sealed class MemorySettingsStore : IVersionedSettingsStore, IVersionedSettingsCompareExchange
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
        private int _version;

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_values.TryGetValue(key, out var json)) return Task.FromResult<T?>(null);
            return Task.FromResult(JsonSerializer.Deserialize<T>(json));
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
            foreach (var pair in manifest.Settings) _values[pair.Key] = pair.Value;
            _version++;
            return Task.FromResult(new SettingsImportResult(true, new Dictionary<string, string>(_values), "Imported"));
        }

        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expectedJson,
            string? replacementJson, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values.TryGetValue(key, out var current);
            if (!string.Equals(current, expectedJson, StringComparison.Ordinal))
                return Task.FromResult(new SettingsCompareExchangeResult(false, current, _version));
            if (replacementJson is null) _values.Remove(key);
            else _values[key] = replacementJson;
            _version++;
            return Task.FromResult(new SettingsCompareExchangeResult(true, replacementJson, _version));
        }
    }

    /// <summary>A settings store whose canonical conversation→SessionID mapping persistence fails,
    /// while session/metadata writes succeed. Used to prove start failure is reported truthfully.</summary>
    private sealed class FailingMappingStore : IVersionedSettingsStore, IVersionedSettingsCompareExchange
    {
        private readonly MemorySettingsStore _inner = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
            => _inner.GetAsync<T>(key, cancellationToken);

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
        {
            if (key.StartsWith("visionvoice.session-by-conversation.v1.", StringComparison.Ordinal))
                throw new InvalidOperationException("mapping store unavailable");
            return _inner.SetAsync(key, value, cancellationToken);
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken) => _inner.RemoveAsync(key, cancellationToken);

        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken)
            => _inner.ExportAsync(cancellationToken);

        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken)
            => _inner.ImportAsync(manifest, cancellationToken);

        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expectedJson,
            string? replacementJson, CancellationToken cancellationToken)
            => _inner.CompareExchangeAsync(key, expectedJson, replacementJson, cancellationToken);
    }

    /// <summary>A settings store whose canonical conversation→SessionID mapping READ faults, while
    /// session/metadata writes succeed. Proves attach-existing mapping-read failures surface as typed
    /// start failures instead of escaping.</summary>
    private sealed class FailingReadMappingStore : IVersionedSettingsStore, IVersionedSettingsCompareExchange
    {
        private readonly MemorySettingsStore _inner = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
        {
            if (key.StartsWith("visionvoice.session-by-conversation.v1.", StringComparison.Ordinal))
                throw new InvalidOperationException("mapping read unavailable");
            return _inner.GetAsync<T>(key, cancellationToken);
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
            => _inner.SetAsync(key, value, cancellationToken);

        public Task RemoveAsync(string key, CancellationToken cancellationToken) => _inner.RemoveAsync(key, cancellationToken);

        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken)
            => _inner.ExportAsync(cancellationToken);

        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken)
            => _inner.ImportAsync(manifest, cancellationToken);

        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expectedJson,
            string? replacementJson, CancellationToken cancellationToken)
            => _inner.CompareExchangeAsync(key, expectedJson, replacementJson, cancellationToken);
    }

    /// <summary>A settings store whose canonical mapping read holds until cancelled, proving the
    /// held attach-existing read honours borrower cancellation rather than settling or corrupting.</summary>
    private sealed class HeldMappingReadStore : IVersionedSettingsStore, IVersionedSettingsCompareExchange
    {
        private readonly MemorySettingsStore _inner = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
        {
            if (key.StartsWith("visionvoice.session-by-conversation.v1.", StringComparison.Ordinal))
                return HoldAsync<T>(cancellationToken);
            return _inner.GetAsync<T>(key, cancellationToken);
        }

        private static async Task<T?> HoldAsync<T>(CancellationToken cancellationToken) where T : class
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return null;
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
            => _inner.SetAsync(key, value, cancellationToken);

        public Task RemoveAsync(string key, CancellationToken cancellationToken) => _inner.RemoveAsync(key, cancellationToken);

        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken)
            => _inner.ExportAsync(cancellationToken);

        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken)
            => _inner.ImportAsync(manifest, cancellationToken);

        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expectedJson,
            string? replacementJson, CancellationToken cancellationToken)
            => _inner.CompareExchangeAsync(key, expectedJson, replacementJson, cancellationToken);
    }

    private sealed class FakeCallCoordinator : ICallCoordinator
    {
        public CallState State { get; set; } = CallState.Idle;
        public CallSession? CurrentSession { get; set; }
        public Conversation? CurrentConversation { get; set; }
        public CallCapabilities Capabilities { get; set; } = new(true, true, false, null, null, null, [], [], []);
        public bool IsActive => State != CallState.Idle;
        public bool IsMuted { get; set; }
        public bool IsScreenSharing { get; set; }

        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        public event EventHandler<CallTranscriptEventArgs>? TranscriptChanged;
        public event EventHandler<CallAudioLevelEventArgs>? AudioLevelChanged;
        public event EventHandler<ScreenShareSnapshotEventArgs>? ScreenPreviewChanged;

        public int StartCount { get; private set; }
        public int EndCount { get; private set; }
        public int PauseCount { get; private set; }
        public int ResumeCount { get; private set; }
        public int StopScreenShareCount { get; private set; }
        public List<string> SubmittedText { get; } = new();
        public List<string> Events { get; } = new();
        public TaskCompletionSource<bool>? StartRelease { get; set; }
        public TaskCompletionSource<bool>? StartEntered { get; set; }
        public bool FailStart { get; set; }
        public bool FailEnd { get; set; }
        // Simulates the real EndCore: mark the call idle/Completed BEFORE the awaited durable
        // settlement throws. IsActive is therefore false while the retained persistence fault is
        // still real.
        public bool FailEndAfterIdle { get; set; }

        public async Task<CallSession> StartAsync(CallStartOptions options, SpeechModelInfo? speechModel, CancellationToken ct)
        {
            StartCount++;
            Events.Add("start-enter");
            StartEntered?.TrySetResult(true);
            ct.ThrowIfCancellationRequested();
            if (FailStart) throw new InvalidOperationException("start failed");
            State = CallState.Listening;
            var conversationId = options.ConversationId ?? Guid.NewGuid();
            CurrentConversation = new Conversation(conversationId, HavenMode.Chat, ConversationKind.Chat,
                "Voice", null, null, false, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            if (StartRelease is { } release) await release.Task.ConfigureAwait(false);
            var session = new CallSession(Guid.NewGuid(), conversationId, "model",
                options.InputDeviceId, options.OutputDeviceId, options.VoiceName, options.InputMode, false,
                CallSessionStatus.Active, DateTimeOffset.UtcNow);
            CurrentSession = session;
            Events.Add("start-complete");
            return session;
        }

        public Task SubmitTextAsync(string text, CancellationToken ct) { SubmittedText.Add(text); return Task.CompletedTask; }
        public Task BeginPushToTalkAsync(CancellationToken ct) => Task.CompletedTask;
        public Task EndPushToTalkAsync(CancellationToken ct) => Task.CompletedTask;
        public Task SetMutedAsync(bool muted, CancellationToken ct) { IsMuted = muted; return Task.CompletedTask; }
        public Task PauseAsync(CancellationToken ct) { State = CallState.Paused; PauseCount++; return Task.CompletedTask; }
        public Task ResumeAsync(CancellationToken ct) { State = CallState.Listening; ResumeCount++; return Task.CompletedTask; }
        public Task StartScreenShareAsync(CancellationToken ct) { IsScreenSharing = true; return Task.CompletedTask; }
        public Task StopScreenShareAsync(CancellationToken ct) { IsScreenSharing = false; StopScreenShareCount++; return Task.CompletedTask; }
        public Task InterruptAsync(CancellationToken ct) => Task.CompletedTask;
        public Task EndAsync(CancellationToken ct)
        {
            EndCount++;
            Events.Add("end-enter");
            if (FailEnd) throw new InvalidOperationException("end failed");
            State = CallState.Idle;
            IsScreenSharing = false;
            Events.Add("end-complete");
            if (FailEndAfterIdle) throw new InvalidOperationException("durable settlement failed after the call ended");
            return Task.CompletedTask;
        }

        public void RaiseState(CallState state, string status) => StateChanged?.Invoke(this, new(state, status));
        public void RaiseAudioLevel(double level) => AudioLevelChanged?.Invoke(this, new(level));
        public void RaiseScreenPreview() => ScreenPreviewChanged?.Invoke(this, new ScreenShareSnapshotEventArgs(
            new ScreenShareSnapshot("", 0, 0, DateTimeOffset.UtcNow)));
        public void RaiseTranscript(Guid messageId, MessageRole role, string text, bool isDelta, bool isFinal) =>
            TranscriptChanged?.Invoke(this, new CallTranscriptEventArgs(messageId, role, text, isDelta, isFinal));
        public ValueTask DisposeAsync() { State = CallState.Idle; return ValueTask.CompletedTask; }
    }
}
