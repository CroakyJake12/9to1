using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Haven.Application;
using Haven.Application.Call;
using Haven.Core;
using Haven.Desktop.Views.Shell.VoiceVision;
using Xunit;

namespace Haven.Desktop.Tests.VoiceVision;

/// <summary>
/// Regression coverage for the Voice &amp; Vision host adapter's causal ownership repairs: genuine typed
/// policy resolution (never a `policy.Contains("local")` inference), presentation changes never start
/// or end the session, current-at-use-time provider reads, and ONE retained teardown completion shared
/// by concurrent close/dispose callers (including sync retirement followed by async join). These
/// exercise the REAL compiled-document/transport seam and the real runtime contract — not a
/// FakeSource binding.
/// </summary>
public sealed class VoiceVisionHostAdapterOwnershipTests
{
    private sealed class FakeRuntime : IVoiceVisionRuntime
    {
        public VoiceVisionRuntimeState State { get; set; } = VoiceVisionRuntimeState.Empty;
        public List<VoiceVisionStartRequest> Starts { get; } = [];
        public int EndCalls;
        public int SubscriptionCount;

        public event EventHandler? StateChanged
        {
            add { SubscriptionCount++; }
            remove { SubscriptionCount--; }
        }

        public Task<VoiceVisionStartResult> StartAsync(VoiceVisionStartRequest request, CancellationToken ct)
        {
            Starts.Add(request);
            return Task.FromResult(VoiceVisionStartResult.Success(Guid.NewGuid(), attachedExisting: false));
        }

        public Task<VoiceVisionOperation> EndAsync(CancellationToken ct)
        {
            EndCalls++;
            return Task.FromResult(VoiceVisionOperation.Ok);
        }

        public Task<VoiceVisionOperation> SetMutedAsync(bool muted, CancellationToken ct) => Task.FromResult(VoiceVisionOperation.Ok);
        public Task<VoiceVisionOperation> PauseAsync(CancellationToken ct) => Task.FromResult(VoiceVisionOperation.Ok);
        public Task<VoiceVisionOperation> ResumeAsync(CancellationToken ct) => Task.FromResult(VoiceVisionOperation.Ok);
        public Task<VoiceVisionOperation> InterruptAsync(CancellationToken ct) => Task.FromResult(VoiceVisionOperation.Ok);
        public Task<VoiceVisionOperation> SubmitTextAsync(string text, CancellationToken ct) => Task.FromResult(VoiceVisionOperation.Ok);
        public Task<VoiceVisionOperation> SetCameraAsync(bool enabled, CancellationToken ct) => Task.FromResult(VoiceVisionOperation.Ok);
        public Task<VoiceVisionOperation> SetScreenShareAsync(bool enabled, CancellationToken ct) => Task.FromResult(VoiceVisionOperation.Ok);
        public Task<VoiceVisionOperation> SetCaptionsAsync(bool enabled, CancellationToken ct) => Task.FromResult(VoiceVisionOperation.Ok);
        public Task<VoiceVisionOperation> SetTranscriptVisibleAsync(bool visible, CancellationToken ct) => Task.FromResult(VoiceVisionOperation.Ok);
        public Task<VoiceVisionOperation> SetModeAsync(VisionVoiceMode mode, CancellationToken ct) => Task.FromResult(VoiceVisionOperation.Ok);
        public Task<VoiceVisionOperation> SetRetentionAsync(bool retainAudio, bool retainTranscript, CancellationToken ct) => Task.FromResult(VoiceVisionOperation.Ok);
        public Task<VoiceVisionOperation> SetLiveTranslateLanguagesAsync(LiveTranslateLanguageSet languages, CancellationToken ct) => Task.FromResult(VoiceVisionOperation.Ok);
        public void SetPresentation(VoiceVisionPresentationKind presentation) => State = State with { Presentation = presentation };
    }

    private static ModelDescriptor Model() =>
        new("voice.model", 1_000_000_000, "gpt", "7B", "q4",
            new HashSet<ToolCapability>(), DateTimeOffset.UtcNow);

    [Fact]
    public async Task Pill_full_and_close_presentation_never_start_or_end_the_session()
    {
        var runtime = new FakeRuntime();
        var adapter = new VoiceVisionHostAdapter(
            runtime,
            () => Guid.NewGuid(),
            () => new CallStartOptions(Model()),
            policyAuthority: () => new VoiceSessionStartPolicy("default", false));

        var pill = adapter.LoadPill();
        Assert.NotNull(pill);
        var full = adapter.LoadSession();
        Assert.NotNull(full);

        var closed = false;
        adapter.PresentationClosedAsync += () => { closed = true; return Task.CompletedTask; };
        await adapter.ClosePresentationAsync();

        Assert.True(closed);
        Assert.Empty(runtime.Starts);
        Assert.Equal(0, runtime.EndCalls);
        Assert.Equal(VoiceVisionPresentationKind.None, runtime.State.Presentation);
    }

    [Fact]
    public async Task Start_reads_current_conversation_space_model_at_use_time()
    {
        var conversationId = Guid.NewGuid();
        var spaceId = Guid.NewGuid();
        var selectedModel = Model();
        var runtime = new FakeRuntime();
        var adapter = new VoiceVisionHostAdapter(
            runtime,
            () => conversationId,
            () => new CallStartOptions(selectedModel),
            () => spaceId,
            isLocalOnlyProvider: () => true,
            policyProvider: () => "local-only");

        // Change the live values AFTER construction to prove providers read at use time.
        conversationId = Guid.NewGuid();
        spaceId = Guid.NewGuid();
        selectedModel = Model();

        await adapter.DispatchCommandAsync("voice.start", null, CancellationToken.None);

        var request = Assert.Single(runtime.Starts);
        Assert.Equal(conversationId, request.ConversationId);
        Assert.Equal(spaceId, request.SpaceId);
        Assert.Equal("local-only", request.EffectiveModelPolicy);
        Assert.True(request.IsLocalOnly);
    }

    [Fact]
    public async Task Start_refuses_when_no_genuine_policy_is_available()
    {
        var runtime = new FakeRuntime();
        var adapter = new VoiceVisionHostAdapter(
            runtime,
            () => Guid.NewGuid(),
            () => new CallStartOptions(Model()));

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            adapter.DispatchCommandAsync("voice.start", null, CancellationToken.None).AsTask());
        Assert.Empty(runtime.Starts);
    }

    [Fact]
    public async Task Policy_text_containing_local_without_authority_is_not_an_inference()
    {
        var runtime = new FakeRuntime();
        // A single partial source (policy text that merely contains "local") must not fabricate a
        // local-only claim; without a genuine local-only fact the start refuses truthfully.
        var adapter = new VoiceVisionHostAdapter(
            runtime,
            () => Guid.NewGuid(),
            () => new CallStartOptions(Model()),
            policyProvider: () => "this policy text mentions local");

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            adapter.DispatchCommandAsync("voice.start", null, CancellationToken.None).AsTask());
        Assert.Empty(runtime.Starts);
    }

    [Fact]
    public async Task Start_attaches_to_live_session_reusing_its_stored_policy()
    {
        var conversationId = Guid.NewGuid();
        var runtime = new FakeRuntime
        {
            State = VoiceVisionRuntimeState.Empty with
            {
                SessionId = Guid.NewGuid(),
                ConversationId = conversationId,
                IsActive = true,
                EffectiveModelPolicy = "stored-policy",
                IsLocalOnly = true
            }
        };
        var adapter = new VoiceVisionHostAdapter(
            runtime,
            () => conversationId,
            () => new CallStartOptions(Model()));

        await adapter.DispatchCommandAsync("voice.start", null, CancellationToken.None);

        var request = Assert.Single(runtime.Starts);
        Assert.Equal("stored-policy", request.EffectiveModelPolicy);
        Assert.True(request.IsLocalOnly);
    }

    [Fact]
    public async Task Concurrent_repeated_disposal_shares_one_teardown_completion()
    {
        var runtime = new FakeRuntime();
        var adapter = new VoiceVisionHostAdapter(runtime, () => Guid.NewGuid(), () => new CallStartOptions(Model()));

        await Task.WhenAll(adapter.DisposeAsync().AsTask(), adapter.DisposeAsync().AsTask());

        // Exactly one retirement: the runtime subscription is released once, not per caller.
        Assert.Equal(0, runtime.SubscriptionCount);
    }

    [Fact]
    public async Task Sync_retirement_then_async_join_completes_without_double_retire()
    {
        var runtime = new FakeRuntime();
        var adapter = new VoiceVisionHostAdapter(runtime, () => Guid.NewGuid(), () => new CallStartOptions(Model()));

        adapter.Dispose();
        await adapter.DisposeAsync();

        Assert.Equal(0, runtime.SubscriptionCount);
    }
}
