using System.Runtime.CompilerServices;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Application.Call;
using Haven.Core;
using Haven.Desktop.Views.Pages.Call;
using Haven.Desktop.Services;
using Haven.Desktop.Events;
using Microsoft.Extensions.DependencyInjection;
namespace Haven.Desktop.Tests;

// Actual Call owner/native input/physical metadata CAS. Model, speech, call/conversation
// repositories and microphone/screen adapters are controlled protocol; no runtime/audio grant.
public sealed class CallMonologueNarrationRouteTests
{
    private static async Task<CallCoordinator> OpenCall(Speech speech, CancellationToken ct)
    {
        var owner = new CallCoordinator(new MemoryCallRepository(), new MemoryConversationRepository(),
            new FakeOllamaClient(["The exact original completed reply."]), new FakeSpeechInput(), speech, new FakeScreenShare());
        await owner.StartAsync(new CallStartOptions(Model(), VoiceName: "original-voice", EnableSpeechOutput: true), null, ct);
        await owner.SubmitTextAsync("Produce the original reply", ct);
        return owner;
    }
    private static async Task Press(Window window, StackPanel host, string label, Func<Task> idle, CancellationToken originalTestToken)
    {
        var button = Assert.Single(host.Children.OfType<Button>(), item => Equals(item.Content, label));
        Assert.True(button.IsEnabled); Assert.True(button.Focus());
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await idle().WaitAsync(TimeSpan.FromSeconds(10), originalTestToken);
    }
    [AvaloniaFact]
    public async Task Actual_Call_reply_native_admission_binds_same_conversation_and_exact_handle_until_owner_end()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken; var speech = new Speech();
        await using var owner = await OpenCall(speech, ct);
        var selection = Assert.IsType<CallOriginalNarrationSelection>(owner.CaptureOriginalNarration());
        var services = new ServiceCollection().AddHavenDesktopCallServices();
        services.AddSingleton(owner); services.AddSingleton<ICallCoordinator>(owner);
        services.AddSingleton<IVersionedSettingsStore>(new VersionedAtomicSettingsStore(f.AppPaths));
        using var provider = services.BuildServiceProvider();
        var route = provider.GetRequiredService<CallMonologueNarrationRoute>();
        Assert.True(route.IsBoundTo(owner)); Assert.False(File.Exists(f.StatePath));
        using var host = Assert.IsType<CallMonologueNarrationHost>(route.CreateOriginalHost());
        var window = new Window { Content = host }; window.Show(); Exception? primary = null;
        try
        {
            await Press(window, host, "Narrate this completed reply", host.WhenActionIdleAsync, ct);
            var original = Assert.IsType<MonologueOriginalPlayback>(host.OriginalPlayback);
            Assert.Equal(selection.Text, speech.NarrationText); Assert.Equal("original-voice", speech.NarrationVoice);
            var controls = Assert.Single(host.Children.OfType<MonologueOriginalPlaybackHost>());
            await Press(window, controls, "Pause original narration", controls.WhenActionIdleAsync, ct);
            var saved = controls.LastObservation!.Value!;
            Assert.Equal(selection.ConversationId, saved.ConversationId);
            Assert.Equal(original.CanonicalReceipt, (await f.Reopen().GetSessionAsync(saved.SessionId, ct)).Value!.Monologue!.PlaybackReceipt);
            await Press(window, controls, "Resume original narration", controls.WhenActionIdleAsync, ct);
            var bytes = await File.ReadAllBytesAsync(f.StatePath, ct);
            await owner.EndAsync(ct);
            Assert.False(owner.IsOriginalNarrationCurrent(selection));
            Assert.Equal(1, speech.Handle.Stops); Assert.True(original.Completion.IsCanceled);
            Assert.False((await original.ResumeAsync(ct)).IsSuccess);
            Assert.Equal(1, speech.Starts); Assert.Equal(1, speech.Handle.Pauses); Assert.Equal(1, speech.Handle.Resumes);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath, ct));
        }
        catch (Exception error) { primary = error; throw; }
        finally { window.Close(); try { await host.WhenActionIdleAsync(); } catch when (primary is not null) { } }
    }
    [AvaloniaFact]
    public async Task Actual_Call_end_during_committed_checkpoint_return_retains_exact_ACK_without_resume_replay()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken; var speech = new Speech();
        await using var owner = await OpenCall(speech, ct);
        var store = new HeldReturn(new VersionedAtomicSettingsStore(f.AppPaths));
        var sessions = new VisionVoiceSessionService(new MultimodalSessionStore(store));
        using var host = Assert.IsType<CallMonologueNarrationHost>(new CallMonologueNarrationRoute(owner, sessions).CreateOriginalHost());
        var window = new Window { Content = host }; window.Show(); Task? pending = null; Exception? primary = null;
        try
        {
            await Press(window, host, "Narrate this completed reply", host.WhenActionIdleAsync, ct);
            var original = host.OriginalPlayback!; var controls = Assert.Single(host.Children.OfType<MonologueOriginalPlaybackHost>());
            store.HoldNext = true;
            var button = Assert.Single(controls.Children.OfType<Button>(), item => Equals(item.Content, "Pause original narration"));
            Assert.True(button.Focus()); window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
            pending = controls.WhenActionIdleAsync();
            await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.False(pending.IsCompleted); var committed = await File.ReadAllBytesAsync(f.StatePath, ct);
            await owner.EndAsync(ct); Assert.Equal(1, speech.Handle.Stops);
            store.Release.TrySetResult(); await pending.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.True(controls.LastObservation!.IsSuccess); Assert.NotNull(original.CanonicalReceipt);
            Assert.Null(original.PendingCheckpoint); Assert.False((await original.ResumeAsync(ct)).IsSuccess);
            Assert.Equal(committed, await File.ReadAllBytesAsync(f.StatePath, ct));
            Assert.Equal(1, speech.Starts); Assert.Equal(1, speech.Handle.Pauses); Assert.Equal(0, speech.Handle.Resumes);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            store.Release.TrySetResult();
            try { if (pending is not null) await pending; } catch when (primary is not null) { }
            finally { window.Close(); try { await host.WhenActionIdleAsync(); } catch when (primary is not null) { } }
        }
    }
    [AvaloniaFact]
    public async Task Actual_Call_end_during_real_metadata_start_cannot_adopt_late_narration_or_start_synthesis()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken; var speech = new Speech();
        await using var owner = await OpenCall(speech, ct);
        var store = new HeldReturn(new VersionedAtomicSettingsStore(f.AppPaths)) { HoldNext = true };
        var sessions = new VisionVoiceSessionService(new MultimodalSessionStore(store));
        using var host = Assert.IsType<CallMonologueNarrationHost>(new CallMonologueNarrationRoute(owner, sessions).CreateOriginalHost());
        var window = new Window { Content = host }; window.Show(); Task? pending = null; Exception? primary = null;
        try
        {
            var button = Assert.Single(host.Children.OfType<Button>());
            Assert.True(button.Focus()); window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
            pending = host.WhenActionIdleAsync();
            await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.False(pending.IsCompleted); var realMetadata = await File.ReadAllBytesAsync(f.StatePath, ct);
            await owner.EndAsync(ct); store.Release.TrySetResult();
            await Assert.ThrowsAsync<InvalidOperationException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10), ct));
            Assert.IsType<InvalidOperationException>(host.LastFailure);
            Assert.Null(host.OriginalPlayback); Assert.Equal(0, speech.Starts);
            Assert.Equal(realMetadata, await File.ReadAllBytesAsync(f.StatePath, ct));
            window.Content = null; window.Content = host;
            Assert.False(button.IsEnabled); Assert.Null(owner.CaptureOriginalNarration());
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            store.Release.TrySetResult();
            try { if (pending is not null) await pending; } catch when (primary is not null || host.LastFailure is not null) { }
            finally { window.Close(); try { await host.WhenActionIdleAsync(); } catch when (primary is not null || host.LastFailure is not null) { } }
        }
    }

    [AvaloniaFact]
    public async Task Retained_actual_native_reply_choice_cannot_rebind_identical_later_Call_turn()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken; var speech = new Speech();
        await using var owner = await OpenCall(speech, ct);
        var original = owner.CaptureOriginalNarration()!;
        var route = new CallMonologueNarrationRoute(owner, f.Service);
        using var host = Assert.IsType<CallMonologueNarrationHost>(route.CreateOriginalHost());
        var window = new Window { Content = host }; window.Show(); Exception? primary = null;
        try
        {
            await owner.SubmitTextAsync("Produce another identical reply", ct);
            var replacement = owner.CaptureOriginalNarration()!;
            Assert.NotSame(original, replacement); Assert.NotEqual(original.ReplyId, replacement.ReplyId); Assert.Equal(original.Text, replacement.Text);
            Assert.Equal(original.CallId, replacement.CallId); Assert.Equal(original.ConversationId, replacement.ConversationId);
            Assert.False(owner.IsOriginalNarrationCurrent(original)); Assert.True(owner.IsOriginalNarrationCurrent(replacement));
            await Press(window, host, "Narrate this completed reply", host.WhenActionIdleAsync, ct);
            Assert.Null(host.OriginalPlayback); Assert.Equal(0, speech.Starts);
            Assert.False(File.Exists(f.StatePath)); Assert.False(Assert.Single(host.Children.OfType<Button>()).IsEnabled);
            using var freshHost = Assert.IsType<CallMonologueNarrationHost>(route.CreateOriginalHost());
            Assert.True(Assert.Single(freshHost.Children.OfType<Button>()).IsEnabled);
        }
        catch (Exception error) { primary = error; throw; }
        finally { window.Close(); try { await host.WhenActionIdleAsync(); } catch when (primary is not null) { } }
    }

    [AvaloniaFact]
    public async Task Actual_CallPage_detach_and_reattach_cannot_adopt_queued_original_attachment_narration()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken; var speech = new Speech();
        await using var owner = await OpenCall(speech, ct); using var bus = new HavenEventBus();
        var route = new CallMonologueNarrationRoute(owner, f.Service);
        var page = new CallPage(bus, owner, new FakeOllamaClient([]), new SpeechModels(), new VoiceProfileCatalog(),
            new UserPreferencesService(f.AppPaths), route);
        var window = new Window { Content = page }; window.Show();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? pending = null; Exception? primary = null;
        try
        {
            // Timing only: hold the actual captured/queued native presentation; never create a
            // selection, substitute an owner result or invoke the native callback directly.
            var delay = typeof(CallPage).GetField("_narrationPresentationDelay", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(delay); delay.SetValue(page, release.Task);
            // Observe genuine loaded device setup before capturing the completed turn;
            // setup can legitimately change the owning Call's original options.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await page.WhenSetupIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), ct);
            await owner.SubmitTextAsync("Produce the actual reply while the page is attached", ct);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            pending = page.WhenOriginalNarrationPresentationIdleAsync(); Assert.False(pending.IsCompleted);
            var sameOriginal = owner.CaptureOriginalNarration()!; Assert.True(owner.IsOriginalNarrationCurrent(sameOriginal));
            Assert.Empty(page.GetVisualDescendants().OfType<CallMonologueNarrationHost>());
            window.Content = null; window.Content = page;
            Assert.True(owner.IsOriginalNarrationCurrent(sameOriginal)); // Isolates native attachment retirement.
            release.TrySetResult(); await pending.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.Null(page.LastNarrationPresentationFailure);
            Assert.Empty(page.GetVisualDescendants().OfType<CallMonologueNarrationHost>());
            Assert.Equal(0, speech.Starts); Assert.False(File.Exists(f.StatePath));
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            release.TrySetResult();
            try { if (pending is not null) await pending; } catch when (primary is not null) { }
            finally { window.Close(); try { await page.WhenOriginalNarrationPresentationIdleAsync(); } catch when (primary is not null) { } }
        }
    }
    private sealed class SpeechModels : ISpeechModelManager
    {
        public Task<IReadOnlyList<SpeechModelInfo>> GetModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SpeechModelInfo>>([]);
        public Task<SpeechModelInfo> DownloadAsync(SpeechModelSize size, IProgress<double>? progress, CancellationToken ct) =>
            Task.FromException<SpeechModelInfo>(new NotSupportedException("No model installation in this fixture."));
        public Task DeleteAsync(SpeechModelSize size, CancellationToken ct) => Task.FromException(new NotSupportedException("No model deletion in this fixture."));
    }

    private static ModelDescriptor Model(bool vision = false) => new(
        "qwen-test",
        1,
        "qwen",
        "test",
        "test",
        vision
            ? new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Vision }
            : new HashSet<ToolCapability> { ToolCapability.Text },
        DateTimeOffset.UtcNow);

    /// <summary>
    /// Represents memory call repository and keeps its related state and behavior together.
    /// </summary>
    private sealed class MemoryCallRepository : ICallRepository
    {
        /// <summary>
        /// Gets or updates items, the bindable or domain state represented by this property.
        /// </summary>
        public Dictionary<Guid, CallSession> Items { get; } = [];
        /// <summary>
        /// Performs upsert asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task UpsertAsync(CallSession session, CancellationToken cancellationToken)
        {
            Items[session.Id] = session;
            return Task.CompletedTask;
        }
        /// <summary>
        /// Retrieves async for the current operation.
        /// </summary>
        public Task<CallSession?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.GetValueOrDefault(id));
        /// <summary>
        /// Retrieves recent async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<CallSession>> GetRecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CallSession>>(Items.Values.OrderByDescending(item => item.StartedAt).Take(limit).ToArray());
    }

    /// <summary>
    /// Represents memory conversation repository and keeps its related state and behavior together.
    /// </summary>
    private sealed class MemoryConversationRepository : IConversationRepository
    {
        /// <summary>
        /// Gets or updates items, the bindable or domain state represented by this property.
        /// </summary>
        public Dictionary<Guid, Conversation> Items { get; } = [];
        /// <summary>
        /// Gets or updates messages, the bindable or domain state represented by this property.
        /// </summary>
        public Dictionary<Guid, List<ChatMessage>> Messages { get; } = [];

        /// <summary>
        /// Retrieves recent async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Conversation>>(Items.Values.Take(limit).ToArray());
        /// <summary>
        /// Retrieves async for the current operation.
        /// </summary>
        public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.GetValueOrDefault(id));
        /// <summary>
        /// Retrieves messages async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.GetValueOrDefault(conversationId) ?? []);
        /// <summary>
        /// Performs upsert conversation asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken)
        {
            Items[conversation.Id] = conversation;
            Messages.TryAdd(conversation.Id, []);
            return Task.CompletedTask;
        }
        /// <summary>
        /// Performs add message asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken)
        {
            Messages.TryAdd(message.ConversationId, []);
            Messages[message.ConversationId].Add(message);
            return Task.CompletedTask;
        }
        /// <summary>
        /// Performs delete conversation asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken)
        {
            Items.Remove(id);
            Messages.Remove(id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOllamaClient(IReadOnlyList<string> chunks) : IOllamaClient
    {
        /// <summary>
        /// Gets or updates last request, the bindable or domain state represented by this property.
        /// </summary>
        public OllamaChatRequest? LastRequest { get; private set; }
        /// <summary>
        /// Gets or updates wait after first chunk, the bindable or domain state represented by this property.
        /// </summary>
        public bool WaitAfterFirstChunk { get; set; }
        public bool ThrowOnStream { get; set; }
        /// <summary>
        /// Gets or updates first chunk, the bindable or domain state represented by this property.
        /// </summary>
        public TaskCompletionSource FirstChunk { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Reports whether available async applies to the current state.
        /// </summary>
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        /// <summary>
        /// Retrieves models async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([Model()]);

        /// <summary>
        /// Performs stream chat asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public async IAsyncEnumerable<string> StreamChatAsync(
            OllamaChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (ThrowOnStream)
                throw new InvalidOperationException("Provider request failed.");

            for (var index = 0; index < chunks.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunks[index];
                if (index == 0)
                {
                    FirstChunk.TrySetResult();
                    if (WaitAfterFirstChunk)
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Performs complete asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(string.Concat(chunks));
        /// <summary>
        /// Performs chat with tools asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(string.Concat(chunks), []));
    }

    /// <summary>
    /// Represents fake speech input and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeSpeechInput : ISpeechInputService
    {
        /// <summary>
        /// Stores callback locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private Func<SpeechInputEvent, CancellationToken, Task>? _callback;
        /// <summary>
        /// Stores callback token locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private CancellationToken _callbackToken;
        public bool Available { get; set; } = true;
        /// <summary>
        /// Reports whether available applies to the current state.
        /// </summary>
        public bool IsAvailable => Available;
        /// <summary>
        /// Gets or updates unavailable reason, the bindable or domain state represented by this property.
        /// </summary>
        public string? UnavailableReason { get; set; }
        /// <summary>
        /// Gets or updates devices, the bindable or domain state represented by this property.
        /// </summary>
        public IReadOnlyList<CallAudioDevice> Devices { get; } = [new("mic", "Test microphone", true)];
        /// <summary>
        /// Gets or updates stop count, the bindable or domain state represented by this property.
        /// </summary>
        public int StopCount { get; private set; }
        public int StartCount { get; private set; }
        public bool ThrowUnauthorizedOnStart { get; set; }

        /// <summary>
        /// Performs start asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task StartAsync(
            SpeechInputOptions options,
            Func<SpeechInputEvent, CancellationToken, Task> onEvent,
            CancellationToken cancellationToken)
        {
            StartCount++;
            if (ThrowUnauthorizedOnStart)
                throw new UnauthorizedAccessException("Microphone permission is required for Haven Voice.");
            _callback = onEvent;
            _callbackToken = cancellationToken;
            return Task.CompletedTask;
        }
        /// <summary>
        /// Performs begin push to talk asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task BeginPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs end push to talk asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task EndPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs stop asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }
        /// <summary>
        /// Performs emit asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task EmitAsync(SpeechInputEvent value) =>
            _callback?.Invoke(value, _callbackToken) ?? Task.CompletedTask;
    }

    private sealed class FakeScreenShare : IScreenShareService
    {
        /// <summary>
        /// Stores frame data locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public const string FrameData = "PRIVATE_FRAME_BASE64";
        /// <summary>
        /// Reports whether supported applies to the current state.
        /// </summary>
        public bool IsSupported => true;
        /// <summary>
        /// Reports whether sharing applies to the current state.
        /// </summary>
        public bool IsSharing { get; private set; }
        /// <summary>
        /// Gets or updates unavailable reason, the bindable or domain state represented by this property.
        /// </summary>
        public string? UnavailableReason => null;
        /// <summary>
        /// Gets or updates current source, the bindable or domain state represented by this property.
        /// </summary>
        public ScreenShareSource? CurrentSource { get; private set; }
        /// <summary>
        /// Stores source closed locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public event EventHandler? SourceClosed;
        /// <summary>
        /// Stores snapshot available locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public event EventHandler<ScreenShareSnapshotEventArgs>? SnapshotAvailable;
        /// <summary>
        /// Gets or updates stop count, the bindable or domain state represented by this property.
        /// </summary>
        public int StopCount { get; private set; }
        /// <summary>
        /// Retrieves snapshot count for the current operation.
        /// </summary>
        public int GetSnapshotCount { get; private set; }
        /// <summary>
        /// Performs start with system picker asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<ScreenShareSource> StartWithSystemPickerAsync(CancellationToken cancellationToken)
        {
            IsSharing = true;
            CurrentSource = new("screen-1", "Test screen", ScreenShareSourceKind.Screen);
            return Task.FromResult(CurrentSource);
        }
        /// <summary>
        /// Retrieves latest snapshot async for the current operation.
        /// </summary>
        public Task<ScreenShareSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken)
        {
            GetSnapshotCount++;
            return Task.FromResult<ScreenShareSnapshot?>(new(FrameData, 1280, 720, DateTimeOffset.UtcNow));
        }
        /// <summary>
        /// Performs stop asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            IsSharing = false;
            CurrentSource = null;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Performs the raise source closed step owned by this component.
        /// </summary>
        public void RaiseSourceClosed() => SourceClosed?.Invoke(this, EventArgs.Empty);
        /// <summary>
        /// Performs the raise snapshot step owned by this component.
        /// </summary>
        public void RaiseSnapshot(ScreenShareSnapshot value) => SnapshotAvailable?.Invoke(this, new(value));
    }    private sealed class Speech : ISpeechOutputService, IContinuableSpeechOutputService, IOriginalSpeechPlaybackReceiptIssuer
    {
        public bool IsAvailable => true;
        public string? UnavailableReason => null;
        public IReadOnlyList<CallAudioDevice> Devices => [];
        public IReadOnlyList<CallVoice> Voices => [];
        public Task SpeakAsync(string text, string? voice, string? device, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
        public string? NarrationText, NarrationVoice, NarrationDevice;
        public int Starts; public Handle Handle { get; } = new();
        public bool CanContinueVoice(string voice) => voice == "original-voice";
        public bool WasIssuedPlayback(ISpeechPlaybackContinuation playback) => ReferenceEquals(playback, Handle);
        public bool IsOriginalPlayback(ISpeechPlaybackContinuation playback) => WasIssuedPlayback(playback) && !Handle.Completion.IsCompleted;
        public Task<ISpeechPlaybackContinuation> StartContinuableAsync(string text, string? voice, string? device, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Starts++; NarrationText = text; NarrationVoice = voice; NarrationDevice = device; return Task.FromResult<ISpeechPlaybackContinuation>(Handle); }
    }
    private sealed class Handle : IOriginalSpeechPlaybackStop
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid PlaybackId { get; } = Guid.NewGuid(); public string VoiceId => "original-voice";
        public Task Completion => _completion.Task; public int Pauses, Resumes, Stops;
        public Task<SpeechPlaybackCheckpoint> PauseAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Pauses++; return Task.FromResult(new SpeechPlaybackCheckpoint(PlaybackId, VoiceId, TimeSpan.FromSeconds(12), true)); }
        public Task<SpeechPlaybackCheckpoint> ResumeAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Resumes++; return Task.FromResult(new SpeechPlaybackCheckpoint(PlaybackId, VoiceId, TimeSpan.FromSeconds(12), false)); }
        public Task<bool> StopOriginalAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); if (_completion.Task.IsCompleted) return Task.FromResult(false); Stops++; _completion.TrySetCanceled(); return Task.FromResult(true); }
    }
    private sealed class HeldReturn(VersionedAtomicSettingsStore actual) : IVersionedSettingsStore, IVersionedSettingsCompareExchange
    {
        public bool HoldNext; public int Exchanges; public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T?> GetAsync<T>(string key, CancellationToken ct) where T : class => actual.GetAsync<T>(key, ct);
        public Task SetAsync<T>(string key, T value, CancellationToken ct) where T : class => actual.SetAsync(key, value, ct);
        public Task RemoveAsync(string key, CancellationToken ct) => actual.RemoveAsync(key, ct);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken ct) => actual.ExportAsync(ct);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken ct) => actual.ImportAsync(value, ct);
        public async Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expected, string? replacement, CancellationToken ct)
        {
            var result = await actual.CompareExchangeAsync(key, expected, replacement, ct);
            Exchanges++;
            if (HoldNext && result.Exchanged) { HoldNext = false; Entered.TrySetResult(); await Release.Task; }
            return result;
        }
    }
    [AvaloniaFact]
    public async Task Actual_native_Voice_widget_admits_only_its_same_owner_completed_reply_and_original_playback()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken; var speech = new Speech();
        await using var owner = await OpenCall(speech, ct);
        using var vm = new Haven.Desktop.ViewModels.InChatCallWidgetViewModel(owner, new MemoryConversationRepository());
        using var widget = new Haven.Desktop.Views.Shell.Overlays.GlobalCallWidget(vm);
        var route = new CallMonologueNarrationRoute(owner, f.Reopen());
        widget.BindOriginalNarrationRoute(vm, owner, route);
        var window = new Window { Content = widget }; window.Show(); Exception? primary = null;
        try
        {
            Assert.False(File.Exists(f.StatePath)); Assert.Equal(0, speech.Starts);
            await owner.SubmitTextAsync("Complete the original mounted widget reply", ct);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await widget.WhenOriginalNarrationIdleAsync();
            var host = Assert.Single(widget.GetVisualDescendants().OfType<CallMonologueNarrationHost>());
            var selected = Assert.IsType<CallOriginalNarrationSelection>(owner.CaptureOriginalNarration());
            Assert.False(File.Exists(f.StatePath)); Assert.Equal(0, speech.Starts);
            await Press(window, host, "Narrate this completed reply", widget.WhenOriginalNarrationIdleAsync, ct);
            Assert.Equal(selected.Text, speech.NarrationText); Assert.Equal(1, speech.Starts);
            var controls = Assert.Single(host.Children.OfType<MonologueOriginalPlaybackHost>());
            await Press(window, controls, "Pause original narration", widget.WhenOriginalNarrationIdleAsync, ct);
            Assert.NotNull(host.OriginalPlayback!.CanonicalReceipt);
            var bytes = await File.ReadAllBytesAsync(f.StatePath, ct);
            window.Content = null; window.Content = widget;
            Assert.False((await host.OriginalPlayback.ResumeAsync(ct)).IsSuccess);
            Assert.Equal(1, speech.Handle.Stops); Assert.Equal(0, speech.Handle.Resumes);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(f.StatePath, ct));
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            window.Close(); widget.Dispose();
            try { await widget.WhenOriginalNarrationIdleAsync(); } catch when (primary is not null) { }
        }
    }

    [AvaloniaFact]
    public async Task Actual_native_Voice_widget_queued_reply_cannot_create_narration_after_original_attachment_retirement()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken; var speech = new Speech();
        await using var owner = await OpenCall(speech, ct);
        using var vm = new Haven.Desktop.ViewModels.InChatCallWidgetViewModel(owner, new MemoryConversationRepository());
        using var widget = new Haven.Desktop.Views.Shell.Overlays.GlobalCallWidget(vm);
        widget.BindOriginalNarrationRoute(vm, owner, new CallMonologueNarrationRoute(owner, f.Reopen()));
        var delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        typeof(Haven.Desktop.Views.Shell.Overlays.GlobalCallWidget).GetField("_originalNarrationPresentationDelay", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(widget, delay.Task);
        var window = new Window { Content = widget }; window.Show(); Task? pending = null; Exception? primary = null;
        try
        {
            await owner.SubmitTextAsync("Complete the original deferred native widget reply", ct);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            pending = widget.WhenOriginalNarrationIdleAsync(); Assert.False(pending.IsCompleted);
            var selected = Assert.IsType<CallOriginalNarrationSelection>(owner.CaptureOriginalNarration());
            Assert.True(owner.IsOriginalNarrationCurrent(selected));
            window.Content = null; window.Content = widget;
            delay.TrySetResult(); await pending.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.True(owner.IsOriginalNarrationCurrent(selected));
            Assert.Empty(widget.GetVisualDescendants().OfType<CallMonologueNarrationHost>());
            Assert.Equal(0, speech.Starts); Assert.False(File.Exists(f.StatePath));
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            delay.TrySetResult();
            try { if (pending is not null) await pending; } catch when (primary is not null) { }
            finally { window.Close(); widget.Dispose(); try { await widget.WhenOriginalNarrationIdleAsync(); } catch when (primary is not null) { } }
        }
    }

    [AvaloniaFact]
    public async Task Actual_native_Voice_widget_owner_end_during_committed_checkpoint_return_retains_known_ACK()
    {
        using var f = new Fixture(); var ct = TestContext.Current.CancellationToken; var speech = new Speech();
        await using var owner = await OpenCall(speech, ct);
        var store = new HeldReturn(new VersionedAtomicSettingsStore(f.AppPaths));
        var sessions = new VisionVoiceSessionService(new MultimodalSessionStore(store));
        using var vm = new Haven.Desktop.ViewModels.InChatCallWidgetViewModel(owner, new MemoryConversationRepository());
        using var widget = new Haven.Desktop.Views.Shell.Overlays.GlobalCallWidget(vm);
        widget.BindOriginalNarrationRoute(vm, owner, new CallMonologueNarrationRoute(owner, sessions));
        var window = new Window { Content = widget }; window.Show(); Task? pending = null; Exception? primary = null;
        try
        {
            await owner.SubmitTextAsync("Complete the original acknowledged widget reply", ct);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await widget.WhenOriginalNarrationIdleAsync();
            var host = Assert.Single(widget.GetVisualDescendants().OfType<CallMonologueNarrationHost>());
            await Press(window, host, "Narrate this completed reply", widget.WhenOriginalNarrationIdleAsync, ct);
            var controls = Assert.Single(host.Children.OfType<MonologueOriginalPlaybackHost>());
            store.HoldNext = true;
            var button = Assert.Single(controls.Children.OfType<Button>(), item => Equals(item.Content, "Pause original narration"));
            Assert.True(button.Focus()); window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
            pending = controls.WhenActionIdleAsync(); await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            var committed = await File.ReadAllBytesAsync(f.StatePath, ct); Assert.False(pending.IsCompleted);
            await owner.EndAsync(ct); store.Release.TrySetResult(); await pending.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.True(controls.LastObservation!.IsSuccess); Assert.NotNull(host.OriginalPlayback!.CanonicalReceipt);
            Assert.Null(host.OriginalPlayback.PendingCheckpoint); Assert.False((await host.OriginalPlayback.ResumeAsync(ct)).IsSuccess);
            Assert.Equal(1, speech.Starts); Assert.Equal(1, speech.Handle.Pauses); Assert.Equal(0, speech.Handle.Resumes);
            Assert.Equal(1, speech.Handle.Stops); Assert.Equal(committed, await File.ReadAllBytesAsync(f.StatePath, ct));
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            store.Release.TrySetResult();
            try { if (pending is not null) await pending; } catch when (primary is not null) { }
            finally { window.Close(); widget.Dispose(); try { await widget.WhenOriginalNarrationIdleAsync(); } catch when (primary is not null) { } }
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-original-playback-" + Guid.NewGuid().ToString("N"));
        public string StatePath => Path.Combine(_root, "settings.json");
        private readonly IAppPaths _paths;
        public IAppPaths AppPaths => _paths;
        public VisionVoiceSessionService Service { get; }
        public Fixture() { Directory.CreateDirectory(_root); _paths = new Paths(_root); Service = Reopen(); }
        public VisionVoiceSessionService Reopen() => new(new MultimodalSessionStore(new VersionedAtomicSettingsStore(_paths)));
        public async Task<MultimodalSession> Plan(CancellationToken ct)
        {
            var started = await Service.StartAsync(Guid.NewGuid(), null, VisionVoiceMode.Monologue, "local-only", true, cancellationToken: ct);
            var planned = await Service.PlanMonologueAsync(started.Value!.SessionId, started.Value.Revision, "Original objective", null, ["Original section"], [], ct);
            Assert.True(planned.IsSuccess); return planned.Value!;
        }
        public void Dispose() => Directory.Delete(_root, true);
    }
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "db"); public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments"); public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy");
    }
}
