using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
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
    public async Task Presentation_intents_are_forwarded_to_the_host_without_start_or_end()
    {
        var runtime = new FakeRuntime();
        var adapter = new VoiceVisionHostAdapter(
            runtime,
            () => Guid.NewGuid(),
            () => new CallStartOptions(Model()),
            policyAuthority: () => new VoiceSessionStartPolicy("default", false));

        var intents = new List<VoicePresentationIntent>();
        adapter.PresentationIntentRequested += intent => { intents.Add(intent); return Task.CompletedTask; };

        await adapter.DispatchCommandAsync("voice.open-full", null, CancellationToken.None);
        await adapter.DispatchCommandAsync("voice.return", null, CancellationToken.None);
        await adapter.DispatchCommandAsync("voice.toggle", null, CancellationToken.None);
        await adapter.DispatchCommandAsync("voice.hide", null, CancellationToken.None);

        Assert.Equal(
            [VoicePresentationIntent.OpenFull, VoicePresentationIntent.ReturnToPill,
             VoicePresentationIntent.TogglePill, VoicePresentationIntent.Hide],
            intents);
        // Presentation changes never start or end the session.
        Assert.Empty(runtime.Starts);
        Assert.Equal(0, runtime.EndCalls);
    }

    [Fact]
    public async Task Dispatch_command_after_retirement_throws_ObjectDisposed()
    {
        var runtime = new FakeRuntime();
        var adapter = new VoiceVisionHostAdapter(runtime, () => Guid.NewGuid(), () => new CallStartOptions(Model()));
        await adapter.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            adapter.DispatchCommandAsync("voice.start", null, CancellationToken.None).AsTask());
        Assert.Empty(runtime.Starts);
    }

    [Fact]
    public void Close_reopen_advances_mount_generation()
    {
        var runtime = new FakeRuntime();
        var adapter = new VoiceVisionHostAdapter(runtime, () => Guid.NewGuid(), () => new CallStartOptions(Model()));
        var first = adapter.MountGeneration;

        adapter.LoadPill();
        var pill = adapter.MountGeneration;
        Assert.True(pill > first);

        adapter.LoadSession();
        var full = adapter.MountGeneration;
        Assert.True(full > pill);
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

    /// <summary>
    /// The external-owner deferral contract: the host accepts a presentation intent with the SAME
    /// original loader settlement, returns the admission ACK immediately (so the requesting action
    /// settles), and only an external pump later awaits that settlement before remounting. This test
    /// proves the real loader-raised OpenFull AND Return actions complete even though each captured
    /// settlement snapshot contains the very action that raised it — the exact self-join that
    /// previously deadlocked.
    /// </summary>
    [AvaloniaFact]
    public async Task Real_loader_raised_open_full_and_return_settle_despite_captured_self_join()
    {
        var runtime = new FakeRuntime();
        var adapter = new VoiceVisionHostAdapter(
            runtime,
            () => Guid.NewGuid(),
            () => new CallStartOptions(Model()),
            policyAuthority: () => new VoiceSessionStartPolicy("default", false));

        var intents = new List<VoicePresentationIntent>();
        var settlements = new List<Task>();
        adapter.PresentationIntentRequested += intent =>
        {
            intents.Add(intent);
            // Capture the SAME original loader settlement for THIS intent (includes the requesting action).
            settlements.Add(adapter.CaptureActionSettlement());
            // Return admission ACK immediately so the requesting action can settle.
            return Task.CompletedTask;
        };

        // Load the real compiled pill through the adapter's loader (wires the action dispatcher).
        var pill = adapter.LoadPill();
        using var window = new Window { Content = pill, Width = 420, Height = 300 };
        try
        {
            window.Show();
            window.Measure(new Avalonia.Size(420, 300));
            window.Arrange(new Avalonia.Rect(0, 0, 420, 300));
            window.UpdateLayout();

            // PILL → OpenFull: raise the REAL authored action through the loader (click → dispatcher → adapter).
            var open = Assert.Single(pill.GetVisualDescendants().OfType<Button>(), b => b.Name == "voice-pill-open");
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains(VoicePresentationIntent.OpenFull, intents);
            // The requesting action settles despite the capture; the external pump would later await it.
            await settlements[0].WaitAsync(TimeSpan.FromSeconds(5));

            // FULL → Return: the host mounts the compiled full surface after OpenFull, then the
            // authored Return action is raised through the loader with its own captured settlement.
            var session = adapter.LoadSession();
            window.Content = session;
            window.Measure(new Avalonia.Size(720, 520));
            window.Arrange(new Avalonia.Rect(0, 0, 720, 520));
            window.UpdateLayout();

            var back = Assert.Single(session.GetVisualDescendants().OfType<Button>(), b => b.Name == "voice-session-return");
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains(VoicePresentationIntent.ReturnToPill, intents);
            await settlements[1].WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Item 2 core mechanism: the single adapter loader owns BOTH documents, so the host must
    /// genuinely reload each surface on every transition rather than cache one root per document.
    /// This proves the reload-each-time contract the host relies on — each LoadPill/LoadSession
    /// returns a FRESH control (never the previously retired lineage), advances the mount
    /// generation monotonically, and publishes the current mode on EVERY transition so runtime
    /// Presentation always matches the visible surface (OpenFull→Return→OpenFull).
    /// </summary>
    [Fact]
    public void Reload_each_time_reuses_no_retired_lineage_and_publishes_mode()
    {
        var runtime = new FakeRuntime();
        var adapter = new VoiceVisionHostAdapter(
            runtime,
            () => Guid.NewGuid(),
            () => new CallStartOptions(Model()),
            policyAuthority: () => new VoiceSessionStartPolicy("default", false));

        var g0 = adapter.MountGeneration;

        var pill1 = adapter.LoadPill();          // OpenFull from pill
        var g1 = adapter.MountGeneration;
        Assert.Equal(VoiceVisionPresentationKind.Pill, runtime.State.Presentation);

        var full1 = adapter.LoadSession();       // OpenFull
        var g2 = adapter.MountGeneration;
        Assert.Equal(VoiceVisionPresentationKind.Full, runtime.State.Presentation);

        var pill2 = adapter.LoadPill();          // Return
        var g3 = adapter.MountGeneration;
        Assert.Equal(VoiceVisionPresentationKind.Pill, runtime.State.Presentation);

        var full2 = adapter.LoadSession();       // OpenFull again
        var g4 = adapter.MountGeneration;
        Assert.Equal(VoiceVisionPresentationKind.Full, runtime.State.Presentation);

        // Every transition genuinely reloads: none of the returned surfaces is a reused/retired
        // control, so the host can never reveal an inert old root.
        Assert.NotSame(pill1, pill2);
        Assert.NotSame(full1, full2);
        Assert.NotSame(pill1, full1);
        Assert.NotSame(pill2, full2);

        // Mount generation advances with every document load (never reuses a prior generation).
        Assert.True(g1 > g0);
        Assert.True(g2 > g1);
        Assert.True(g3 > g2);
        Assert.True(g4 > g3);
    }
}
