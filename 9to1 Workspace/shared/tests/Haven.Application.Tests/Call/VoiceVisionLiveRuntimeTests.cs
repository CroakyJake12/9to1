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
/// share yields zero later frames).
/// </summary>
public sealed class VoiceVisionLiveRuntimeTests
{
    [Fact]
    public async Task Attach_existing_identity_without_calling_Start_again()
    {
        var coordinator = ActiveCoordinator();
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());

        var result = await runtime.StartAsync(Request(ConvoId), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.AttachedExisting);
        Assert.Equal(0, coordinator.StartCount);

        // Reopening the same conversation preserves one canonical SessionID and never starts again.
        var again = await runtime.StartAsync(Request(ConvoId), CancellationToken.None);
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

        var first = await runtime.StartAsync(Request(ConvoId), CancellationToken.None);
        Assert.True(first.Succeeded);
        Assert.False(first.AttachedExisting);
        Assert.Equal(1, coordinator.StartCount);

        // A second surface opens while the session is live: it must attach, not start again.
        var second = await runtime.StartAsync(Request(ConvoId), CancellationToken.None);
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
        await runtime.StartAsync(Request(ConvoId), CancellationToken.None);

        // Close presentation (pill/full) must NOT end the runtime.
        runtime.SetPresentation(VoiceVisionPresentationKind.None);
        Assert.Equal(0, coordinator.EndCount);

        // End is the explicit stop/drain.
        var end = await runtime.EndAsync(CancellationToken.None);
        Assert.True(end.Succeeded);
        Assert.Equal(1, coordinator.EndCount);
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task Late_callbacks_after_End_do_not_resurrect_a_live_session()
    {
        var coordinator = NewCoordinator();
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());
        await runtime.StartAsync(Request(ConvoId), CancellationToken.None);
        await runtime.EndAsync(CancellationToken.None);

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
        var camera = await runtime.SetCameraAsync(true, CancellationToken.None);

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

        var start = await runtime.SetScreenShareAsync(true, CancellationToken.None);
        Assert.True(start.Succeeded);
        Assert.True(runtime.State.IsScreenSharing);
        Assert.Contains(runtime.State.VisualSources, source => source.Type is VisualSourceType.DisplayShare);

        var stop = await runtime.SetScreenShareAsync(false, CancellationToken.None);
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

        var t1 = runtime.StartAsync(Request(ConvoId), CancellationToken.None);
        var t2 = runtime.StartAsync(Request(ConvoId), CancellationToken.None);

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

        var t1 = runtime.StartAsync(Request(ConvoId), CancellationToken.None);
        var t2 = runtime.StartAsync(Request(ConvoId), CancellationToken.None);

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
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());

        var startTask = runtime.StartAsync(Request(ConvoId), CancellationToken.None);
        // Wait until the coordinator has entered Start and is blocked on its hold.
        await WaitUntilAsync(() => coordinator.StartCount == 1);

        var endTask = runtime.EndAsync(CancellationToken.None);
        // End must NOT ACK while the shared Start is still settling.
        await Task.Delay(50);
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
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());

        var startTask = runtime.StartAsync(Request(ConvoId), CancellationToken.None);
        await WaitUntilAsync(() => coordinator.StartCount == 1);

        var disposeTask = runtime.DisposeAsync().AsTask();
        await Task.Delay(50);
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
        var runtime = CreateRuntime(coordinator, new MemorySettingsStore());

        var owner = runtime.StartAsync(Request(ConvoId), CancellationToken.None);
        await WaitUntilAsync(() => coordinator.StartCount == 1);

        using var cts = new CancellationTokenSource();
        var borrower = runtime.StartAsync(Request(ConvoId), cts.Token);
        await Task.Delay(50); // borrower is queued on the lifecycle gate
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
        await runtime.StartAsync(Request(ConvoId), CancellationToken.None);

        var first = await runtime.EndAsync(CancellationToken.None);
        Assert.True(first.Succeeded);
        Assert.Equal(1, coordinator.EndCount);

        var second = await runtime.EndAsync(CancellationToken.None);
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

        var start = await runtime.StartAsync(Request(ConvoId), CancellationToken.None);

        Assert.False(start.Succeeded);
        Assert.Equal(VisionVoiceErrorCode.UnknownFailure, start.Error!.Code);
        Assert.False(runtime.State.IsActive);
        Assert.Equal(MultimodalSessionState.Failed, runtime.State.State);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200; i++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }
        throw new TimeoutException("Timed out waiting for coordinator state.");
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
        MemorySettingsStore store,
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
        public bool FailStart { get; set; }

        public async Task<CallSession> StartAsync(CallStartOptions options, SpeechModelInfo? speechModel, CancellationToken ct)
        {
            StartCount++;
            Events.Add("start-enter");
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
            State = CallState.Idle;
            IsScreenSharing = false;
            Events.Add("end-complete");
            return Task.CompletedTask;
        }

        public void RaiseState(CallState state, string status) => StateChanged?.Invoke(this, new(state, status));
        public void RaiseAudioLevel(double level) => AudioLevelChanged?.Invoke(this, new(level));
        public void RaiseScreenPreview() => ScreenPreviewChanged?.Invoke(this, new ScreenShareSnapshotEventArgs(
            new ScreenShareSnapshot("", 0, 0, DateTimeOffset.UtcNow)));
        public ValueTask DisposeAsync() { State = CallState.Idle; return ValueTask.CompletedTask; }
    }
}
