using System.Reflection;
using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Chat;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    [AvaloniaFact]
    public async Task Ordinary_native_chat_stop_drains_only_after_cleanup_without_replaying_or_losing_the_next_draft()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        var model = OrdinaryStopModel(rig);
        rig.Client.AvailableModels = [model];
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalDispose = cleanup.Task;
        rig.Client.OrdinaryProviderBody = async (_, actualToken) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, actualToken);
            return false;
        };
        var directory = Path.Combine(Path.GetTempPath(), "ordinary-chat-stop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var drafts = new NativeComposerDraftStore();
        NewChatPage? page = null;
        Task? sending = null;
        var closed = false;
        try
        {
            page = CreateNativeComposerPage(rig, directory, drafts);
            await page.OriginalInitialization.WaitAsync(TimeSpan.FromSeconds(10), token);
            await page.RefreshModelsAsync();
            page.SelectModel(model);
            Assert.Null(Assert.IsType<Conversation>(typeof(NewChatPage)
                .GetField("_conversation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)).SpaceId);
            page.SetDraft("Original committed request");
            page.RequestComposerSend();
            sending = OrdinaryStopSend(page);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            page.SetDraft("Unsent next draft");

            Assert.True(page.TryStopResponse());
            Assert.False(page.TryStopResponse());
            await rig.Client.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.False(sending.IsCompleted);
            Assert.True(page.IsSending);
            Assert.Equal(1, rig.Client.StreamDisposals);

            cleanup.TrySetResult();
            await sending.WaitAsync(TimeSpan.FromSeconds(20), token);
            Assert.True(rig.Client.OriginalStreamToken.IsCancellationRequested);
            Assert.Equal(1, rig.Client.Dispatches);
            Assert.Equal(1, rig.Client.StreamDisposals);
            Assert.Equal("Original committed request",
                Assert.Single(rig.Conversations.Messages, item => item.Role == MessageRole.User).Content);
            Assert.Equal("Unsent next draft", page.ComposerInput.Text);
            Assert.False(page.IsSending);

            page.RequestRetirement();
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(20), token);
            closed = true;
            Assert.Equal("Unsent next draft", Assert.IsType<ConversationDraft>(drafts.Last).Content);
        }
        finally
        {
            cleanup.TrySetResult();
            if (page is not null)
            {
                page.TryStopResponse();
                if (sending is not null) await Record.ExceptionAsync(() => sending);
                if (!closed)
                {
                    page.RequestRetirement();
                    await Record.ExceptionAsync(() => page.CloseAndDrainAsync());
                }
            }
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTheory]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    public async Task Ordinary_native_chat_stop_retains_faulted_or_foreign_provider_cancellation(bool faulted, bool sameToken, bool throughRouter)
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        var model = OrdinaryStopModel(rig);
        rig.Client.AvailableModels = [model];
        using var foreign = new CancellationTokenSource();
        foreign.Cancel();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalOrdinaryProviderMove = (_, actualToken) =>
        {
            entered.TrySetResult(actualToken);
            return raw.Task;
        };
        var directory = Path.Combine(Path.GetTempPath(), "ordinary-chat-stop-negative-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        NewChatPage? page = null;
        Task? sending = null;
        var closed = false;
        try
        {
            page = CreateOrdinaryStopPage(rig, directory, new NativeComposerDraftStore(), throughRouter);
            await page.OriginalInitialization.WaitAsync(TimeSpan.FromSeconds(10), token);
            await page.RefreshModelsAsync();
            page.SelectModel(model);
            page.SetDraft("Committed once");
            page.RequestComposerSend();
            sending = OrdinaryStopSend(page);
            var businessToken = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            page.SetDraft("Keep this draft");
            Assert.True(page.TryStopResponse());
            var sourceToken = sameToken ? businessToken : foreign.Token;
            var fault = new OperationCanceledException("Actual provider fault, not acknowledged stop", sourceToken);
            if (faulted) raw.SetException(fault);
            else raw.SetCanceled(sourceToken);

            var observedSend = await Record.ExceptionAsync(() => sending.WaitAsync(TimeSpan.FromSeconds(20), token));
            Assert.NotNull(observedSend);
            Assert.Equal(faulted, raw.Task.IsFaulted);
            Assert.Equal(!faulted, raw.Task.IsCanceled);
            if (faulted) Assert.Contains(Leaves(observedSend!), item => ReferenceEquals(item, fault));
            else Assert.Contains(Leaves(observedSend!), item =>
                item is OperationCanceledException cancellation && cancellation.CancellationToken == foreign.Token);
            Assert.Equal(1, rig.Client.Dispatches);
            Assert.Equal(1, rig.Client.StreamDisposals);
            Assert.Single(rig.Conversations.Messages, item => item.Role == MessageRole.User);
            Assert.Equal("Keep this draft", page.ComposerInput.Text);
            Assert.False(page.IsSending);

            page.RequestRetirement();
            var observedClose = await Record.ExceptionAsync(() => page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(20), token));
            closed = true;
            Assert.NotNull(observedClose);
            if (faulted) Assert.Contains(Leaves(observedClose!), item => ReferenceEquals(item, fault));
            else Assert.Contains(Leaves(observedClose!), item =>
                item is OperationCanceledException cancellation && cancellation.CancellationToken == foreign.Token);
        }
        finally
        {
            raw.TrySetResult(false);
            if (page is not null)
            {
                page.TryStopResponse();
                if (sending is not null) await Record.ExceptionAsync(() => sending);
                if (!closed)
                {
                    page.RequestRetirement();
                    await Record.ExceptionAsync(() => page.CloseAndDrainAsync());
                }
            }
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ordinary_native_chat_stop_keeps_independent_provider_cleanup_failure_visible(bool throughRouter)
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        var model = OrdinaryStopModel(rig);
        rig.Client.AvailableModels = [model];
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupFailure = new IOException("Exact provider cleanup failure");
        rig.Client.OriginalDispose = Task.FromException(cleanupFailure);
        rig.Client.OrdinaryProviderBody = async (_, actualToken) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, actualToken);
            return false;
        };
        var directory = Path.Combine(Path.GetTempPath(), "ordinary-chat-stop-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        NewChatPage? page = null;
        Task? sending = null;
        var closed = false;
        try
        {
            page = CreateOrdinaryStopPage(rig, directory, new NativeComposerDraftStore(), throughRouter);
            await page.OriginalInitialization.WaitAsync(TimeSpan.FromSeconds(10), token);
            await page.RefreshModelsAsync();
            page.SelectModel(model);
            page.SetDraft("Committed once");
            page.RequestComposerSend();
            sending = OrdinaryStopSend(page);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            page.SetDraft("Keep this draft");
            Assert.True(page.TryStopResponse());
            var observedSend = await Record.ExceptionAsync(() => sending.WaitAsync(TimeSpan.FromSeconds(20), token));
            Assert.NotNull(observedSend);
            Assert.Contains(Leaves(observedSend!), item => ReferenceEquals(item, cleanupFailure));
            Assert.Equal(1, rig.Client.Dispatches);
            Assert.Equal(1, rig.Client.StreamDisposals);
            Assert.Single(rig.Conversations.Messages, item => item.Role == MessageRole.User);
            Assert.Equal("Keep this draft", page.ComposerInput.Text);

            page.RequestRetirement();
            var observedClose = await Record.ExceptionAsync(() => page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(20), token));
            closed = true;
            Assert.NotNull(observedClose);
            Assert.Contains(Leaves(observedClose!), item => ReferenceEquals(item, cleanupFailure));
        }
        finally
        {
            if (page is not null)
            {
                page.TryStopResponse();
                if (sending is not null) await Record.ExceptionAsync(() => sending);
                if (!closed)
                {
                    page.RequestRetirement();
                    await Record.ExceptionAsync(() => page.CloseAndDrainAsync());
                }
            }
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTheory]
    [InlineData("acknowledged")]
    [InlineData("recovery")]
    [InlineData("unconfirmed")]
    [InlineData("callback")]
    public async Task Layered_Dulche_native_stop_requires_exact_settlement_before_original_page_and_composer_close(string outcome)
    {
        var token = TestContext.Current.CancellationToken;
        var timeout = TimeSpan.FromSeconds(20);
        var succeeds = outcome is "acknowledged" or "recovery";
        await using var rig = new Rig(temporary: false);
        using var paths = new ObservationPaths();
        Directory.CreateDirectory(paths.DataDirectory);
        using var handler = new LayeredStopHttpHandler(outcome);
        var local = new DulcheLocalClient(new UserPreferencesService(paths), handler);
        using var provider = new LayeredStopObservedProvider(new DulcheSocketModelProvider(local));
        var router = OrdinaryStopRouter(rig, provider);
        var sessions = OrdinaryStopSessions(rig, router);
        var drafts = new NativeComposerDraftStore();
        var page = CreateNativeComposerPage(rig, paths.DataDirectory, drafts, modelClient: router, sessions: sessions);
        var window = new Window { Width = 1200, Height = 900, Content = page };
        Task? sending = null, stopping = null, closeWhileStopping = null;
        var closeRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closePublished = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task externalCloseOwner = Task.CompletedTask;
        var closed = false;
        try
        {
            await page.OriginalInitialization.WaitAsync(timeout, token);
            await page.RefreshModelsAsync();
            var model = Assert.Single(await router.GetModelsAsync(token));
            page.SelectModel(model);
            Assert.Equal("dulche-local:owned-text", model.Name);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var composer = Assert.Single(page.GetVisualDescendants().OfType<ChatComposerCuiView>());
            await composer.InitializeAsync().WaitAsync(timeout, token);
            var input = Assert.Single(composer.GetVisualDescendants().OfType<TextBox>(),
                control => AutomationProperties.GetAutomationId(control) == "composer-input");
            // Create the real external close actor before entering the Send/action
            // scope. Its own request and returned original task remain retained;
            // no owner guard or inherited execution context is cleared to close.
            if (outcome == "acknowledged")
                externalCloseOwner = Task.Run(async () =>
                {
                    if (!await closeRequested.Task.ConfigureAwait(false)) return;
                    try
                    {
                        var actual = page.CloseAndDrainAsync();
                        Assert.Same(actual, page.CloseAndDrainAsync());
                        closePublished.TrySetResult(actual);
                        await actual.ConfigureAwait(false);
                    }
                    catch (Exception error) { closePublished.TrySetException(error); throw; }
                }, token);
            input.Text = "One committed layered input";
            sending = InvokeLayeredStopComposer(composer, window, stop: false);
            await handler.ReadEntered.Task.WaitAsync(timeout, token);
            input.Text = "Preserve the next layered draft";

            stopping = InvokeLayeredStopComposer(composer, window, stop: true);

            await handler.StopEntered.Task.WaitAsync(timeout, token);
            Assert.Equal(1, handler.StopRequests);
            Assert.Equal(LayeredStopHttpHandler.RunId, handler.StoppedRunId);
            Assert.False(handler.StopTokenWasCanceled);
            Assert.Equal(1, handler.StreamDisposals);
            Assert.False(sending.IsCompleted);
            Assert.True(page.IsSending);
            Assert.Equal("Preserve the next layered draft", page.ComposerInput.Text);
            Assert.Equal("One committed layered input",
                Assert.Single(rig.Conversations.Messages, message => message.Role == MessageRole.User).Content);

            if (outcome == "acknowledged")
            {
                closeRequested.TrySetResult(true);
                closeWhileStopping = await closePublished.Task.WaitAsync(timeout, token);
                Assert.False(closeWhileStopping.IsCompleted);
                Assert.False(sending.IsCompleted);
            }

            handler.ReleaseStop.TrySetResult();
            var sendFailure = await Record.ExceptionAsync(() => sending.WaitAsync(timeout, token));
            var stopFailure = stopping is null ? null :
                await Record.ExceptionAsync(() => stopping.WaitAsync(timeout, token));
            Assert.Equal(1, handler.ChatRequests);
            Assert.Equal(1, provider.Disposals);
            if (closeWhileStopping is null)
                Assert.Equal("Preserve the next layered draft", page.ComposerInput.Text);
            Assert.False(page.IsSending);
            Assert.Equal(outcome == "callback" ? 1 : 0, handler.CallbackInvocations);
            if (succeeds)
            {
                Assert.Null(sendFailure);
                Assert.Null(stopFailure);
                if (closeWhileStopping is not null)
                {
                    await closeWhileStopping.WaitAsync(timeout, token);
                    await externalCloseOwner.WaitAsync(timeout, token);
                    Assert.Same(closeWhileStopping, page.CloseAndDrainAsync());
                    closed = true;
                    Assert.Equal("Preserve the next layered draft", Assert.IsType<ConversationDraft>(drafts.Last).Content);
                }
                else
                {
                    // The next actual Send uses the existing draft once, and a normally
                    // completed [DONE] response does not request another remote stop.
                    await InvokeLayeredStopComposer(composer, window, stop: false).WaitAsync(timeout, token);
                    Assert.Equal(2, handler.ChatRequests);
                    Assert.Equal(1, handler.StopRequests);
                    Assert.Equal(2, handler.StreamDisposals);
                    Assert.Equal(2, provider.Disposals);
                    Assert.Equal(new[] { "One committed layered input", "Preserve the next layered draft" },
                        rig.Conversations.Messages.Where(message => message.Role == MessageRole.User).Select(message => message.Content));
                    input.Text = "Draft preserved by clean original close";
                    page.RequestRetirement();
                    var close = page.CloseAndDrainAsync();
                    await close.WaitAsync(timeout, token);
                    Assert.Same(close, page.CloseAndDrainAsync());
                    closed = true;
                    Assert.Equal("Draft preserved by clean original close", Assert.IsType<ConversationDraft>(drafts.Last).Content);
                }
            }
            else
            {
                if (outcome == "callback")
                {
                    // Stop's scene notification owns a separate retained page original.
                    // These two operations succeed; its exact callback failure must still
                    // be reported when the external owner joins the page below.
                    Assert.Null(sendFailure);
                    Assert.Null(stopFailure);
                }
                else
                {
                    Assert.True(sendFailure is not null || stopFailure is not null);
                    var operationFailures = new[] { sendFailure, stopFailure }.OfType<Exception>().SelectMany(Leaves).ToArray();
                    Assert.Contains(operationFailures, error => error is DulcheRunStopUnconfirmedException stop &&
                        stop.RunId == LayeredStopHttpHandler.RunId);
                }
                page.RequestRetirement();
                var close = page.CloseAndDrainAsync();
                var closeFailure = await Record.ExceptionAsync(() => close.WaitAsync(timeout, token));
                Assert.Same(close, page.CloseAndDrainAsync());
                closed = true;
                Assert.NotNull(closeFailure);
                if (outcome == "unconfirmed")
                    Assert.Contains(Leaves(closeFailure!), error => error is DulcheRunStopUnconfirmedException stop &&
                        stop.RunId == LayeredStopHttpHandler.RunId);
                else
                    Assert.Contains(Leaves(closeFailure!), error => ReferenceEquals(error, handler.CallbackFailure));
                Assert.Single(rig.Conversations.Messages, message => message.Role == MessageRole.User);
                Assert.Equal("Preserve the next layered draft", Assert.IsType<ConversationDraft>(drafts.Last).Content);
            }
            await provider.JoinObservationsAsync().WaitAsync(timeout, token);
        }
        finally
        {
            handler.ReleaseRead.TrySetResult();
            handler.ReleaseStop.TrySetResult();
            closeRequested.TrySetResult(false);
            _ = Record.Exception(() => { page.TryStopResponse(); });
            _ = await Record.ExceptionAsync(() => externalCloseOwner.WaitAsync(timeout, token));
            if (sending is not null) _ = await Record.ExceptionAsync(() => sending.WaitAsync(timeout, token));
            if (stopping is not null) _ = await Record.ExceptionAsync(() => stopping.WaitAsync(timeout, token));
            if (!closed)
            {
                page.RequestRetirement();
                _ = await Record.ExceptionAsync(() => page.CloseAndDrainAsync().WaitAsync(timeout, token));
            }
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Layered_Dulche_router_backpressure_stop_keeps_exact_task_provenance_and_cleanup(bool cleanupFails)
    {
        var token = TestContext.Current.CancellationToken;
        var timeout = TimeSpan.FromSeconds(20);
        await using var rig = new Rig(temporary: false);
        using var paths = new ObservationPaths();
        Directory.CreateDirectory(paths.DataDirectory);
        using var handler = new LayeredStopHttpHandler(cleanupFails ? "backpressure-cleanup" : "backpressure");
        using var provider = new LayeredStopObservedProvider(
            new DulcheSocketModelProvider(new DulcheLocalClient(new UserPreferencesService(paths), handler)));
        var router = OrdinaryStopRouter(rig, provider);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        var iterator = router.StreamChatAsync(new("dulche-local:owned-text",
            [new("user", "Hold the real bounded writer")], EffortLevel.Medium), caller.Token)
            .GetAsyncEnumerator(caller.Token);
        Task<bool>? move = null;
        var disposed = false;
        try
        {
            move = iterator.MoveNextAsync().AsTask();
            await handler.ReadEntered.Task.WaitAsync(timeout, token);
            handler.ReleaseRead.TrySetResult();
            Assert.True(await move.WaitAsync(timeout, token));
            Assert.Equal("partial 0", iterator.Current);

            // No subsequent public Move is admitted yet. With chunk1 consumed and
            // chunk2 occupying capacity1, the third successful raw provider Move
            // can only advance into the actual router's blocked channel write.
            Assert.True(provider.ThirdSuccessfulMove.Wait(TimeSpan.FromSeconds(5), token));
            Assert.True(provider.ThirdMove!.IsCompletedSuccessfully);
            Assert.Equal(3, provider.Moves);
            Assert.False(handler.ReadAfterBurst.Task.IsCompleted);
            caller.Cancel();

            // Resume the SAME public iterator, allowing any already-buffered chunk
            // to drain. This is caller cancellation, not early iterator abandonment.
            var buffered = 0;
            while (true)
            {
                move = iterator.MoveNextAsync().AsTask();
                if (!move.IsCompletedSuccessfully) break;
                Assert.True(await move);
                Assert.Equal("partial 1", iterator.Current);
                Assert.Equal(1, ++buffered);
            }
            await handler.StopEntered.Task.WaitAsync(timeout, token);
            Assert.False(move.IsCompleted);
            Assert.Equal(3, provider.Moves);
            Assert.False(handler.ReadAfterBurst.Task.IsCompleted);
            Assert.Equal(1, handler.StreamDisposals);
            Assert.Equal(1, handler.StopRequests);
            Assert.Equal(LayeredStopHttpHandler.RunId, handler.StoppedRunId);
            Assert.False(handler.StopTokenWasCanceled);

            handler.ReleaseStop.TrySetResult();
            var failure = await Record.ExceptionAsync(() => move.WaitAsync(timeout, token));
            Assert.NotNull(failure);
            if (cleanupFails)
            {
                Assert.True(move.IsFaulted);
                Assert.Contains(Leaves(failure!), error => ReferenceEquals(error, handler.CleanupFailure));
            }
            else
            {
                Assert.True(move.IsCanceled);
                var canceled = Assert.IsAssignableFrom<OperationCanceledException>(failure);
                Assert.Equal(caller.Token, canceled.CancellationToken);
            }
            await iterator.DisposeAsync().AsTask().WaitAsync(timeout, token);
            disposed = true;
            Assert.Equal(1, provider.Disposals);
            Assert.Equal(1, handler.ChatRequests);
            Assert.Equal(1, handler.StopRequests);
            Assert.Equal(1, handler.StreamDisposals);
            await provider.JoinObservationsAsync().WaitAsync(timeout, token);
        }
        finally
        {
            handler.ReleaseRead.TrySetResult();
            handler.ReleaseStop.TrySetResult();
            caller.Cancel();
            if (move is not null) _ = await Record.ExceptionAsync(() => move.WaitAsync(timeout, token));
            if (!disposed) _ = await Record.ExceptionAsync(() => iterator.DisposeAsync().AsTask().WaitAsync(timeout, token));
        }
    }

    private static Task InvokeLayeredStopComposer(ChatComposerCuiView composer, Window window, bool stop)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var button = Assert.Single(composer.GetVisualDescendants().OfType<Button>(), control => control.IsEffectivelyVisible &&
            AutomationProperties.GetAutomationId(control) == "send-button" &&
            (!stop || (AutomationProperties.GetName(control) ?? string.Empty).StartsWith("Cancel response", StringComparison.Ordinal)));
        Assert.True(button.IsEnabled);
        var peer = Assert.IsAssignableFrom<AutomationPeer>(ControlAutomationPeer.CreatePeerForElement(button));
        Assert.IsAssignableFrom<IInvokeProvider>(peer.GetProvider<IInvokeProvider>()).Invoke();
        return composer.CaptureOriginalActionSettlement();
    }

    private static ChatSessionService OrdinaryStopSessions(Rig rig, IOllamaClient client) =>
        new(rig.Conversations, client, new CapabilityPreflightService(), new Safety(),
            new WorkspaceToolRuntime(new Workspace()), new ComputerToolRuntime(new Computer()));

    private static ResilientProviderRoutingModelClient OrdinaryStopRouter(Rig rig, IModelProvider provider)
    {
        var registry = new ModelProviderRegistry([provider]);
        var privacy = new Privacy();
        return new(new ProviderRoutingModelClient(rig.Client, registry, privacy), registry, new Configurations(), privacy);
    }

    private static NewChatPage CreateOrdinaryStopPage(Rig rig, string directory, NativeComposerDraftStore drafts, bool throughRouter)
    {
        if (!throughRouter) return CreateNativeComposerPage(rig, directory, drafts);
        // Preserve the controlled provider's actual raw Task/iterator while exercising
        // the same linked-frame/channel wrapper that the device-local provider uses.
        var provider = NativeComposerProxy<IModelProvider>((name, arguments) => name switch
        {
            "get_Id" => rig.Provider.Id,
            "get_DisplayName" => rig.Provider.DisplayName,
            "get_Kind" => rig.Provider.Kind,
            "get_IsLocal" => rig.Provider.IsLocal,
            "get_CanManageModels" => false,
            "GetModelsAsync" => rig.Provider.GetModelsAsync((CancellationToken)arguments![0]!),
            "CheckHealthAsync" => rig.Provider.CheckHealthAsync((CancellationToken)arguments![0]!),
            "StreamChatAsync" => rig.Client.StreamChatAsync((OllamaChatRequest)arguments![0]!, (CancellationToken)arguments[1]!),
            _ => throw new InvalidOperationException("Unexpected ordinary Stop fixture provider operation: " + name)
        });
        var router = OrdinaryStopRouter(rig, provider);
        return CreateNativeComposerPage(rig, directory, drafts, modelClient: router, sessions: OrdinaryStopSessions(rig, router));
    }

    // Observation only: every returned Move is the exact underlying Task and every
    // Dispose is the exact original ValueTask, with no cancellation translation.
    private sealed class LayeredStopObservedProvider(IModelProvider inner) : IModelProvider, IDisposable
    {
        private readonly List<Task> _observations = [];
        internal readonly ManualResetEventSlim ThirdSuccessfulMove = new(false);
        internal Task<bool>? ThirdMove;
        internal int Moves, Disposals;
        public string Id => inner.Id;
        public string DisplayName => inner.DisplayName;
        public ModelProviderKind Kind => inner.Kind;
        public bool IsLocal => inner.IsLocal;
        public bool CanManageModels => inner.CanManageModels;
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => inner.GetModelsAsync(token);
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => inner.CheckHealthAsync(token);
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => inner.CompleteAsync(request, token);
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => inner.ChatWithToolsAsync(request, token);
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) =>
            new ObservedStream(this, inner.StreamChatAsync(request, token));
        internal Task JoinObservationsAsync() { lock (_observations) return Task.WhenAll(_observations); }
        public void Dispose() => ThirdSuccessfulMove.Dispose();

        private sealed class ObservedStream(LayeredStopObservedProvider owner, IAsyncEnumerable<string> original) : IAsyncEnumerable<string>
        {
            public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken token = default) =>
                new ObservedIterator(owner, original.GetAsyncEnumerator(token));
        }
        private sealed class ObservedIterator(LayeredStopObservedProvider owner, IAsyncEnumerator<string> original) : IAsyncEnumerator<string>
        {
            public string Current => original.Current;
            public ValueTask<bool> MoveNextAsync()
            {
                var actual = original.MoveNextAsync().AsTask();
                if (Interlocked.Increment(ref owner.Moves) == 3)
                {
                    owner.ThirdMove = actual;
                    var observation = actual.ContinueWith(task =>
                    {
                        if (task.IsCompletedSuccessfully && task.GetAwaiter().GetResult())
                            owner.ThirdSuccessfulMove.Set();
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    lock (owner._observations) owner._observations.Add(observation);
                }
                return new(actual);
            }
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner.Disposals);
                return original.DisposeAsync();
            }
        }
    }

    private sealed class LayeredStopHttpHandler(string outcome) : HttpMessageHandler
    {
        internal const string RunId = "original-layered-owned-run";
        internal readonly TaskCompletionSource ReadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReadAfterBurst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource StopEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly IOException CallbackFailure = new("Exact independent cancellation callback failure");
        internal readonly IOException CleanupFailure = new("Exact independent HTTP response cleanup failure");
        internal int ChatRequests, StopRequests, StreamDisposals, CallbackInvocations;
        internal bool StopTokenWasCanceled;
        internal string? StoppedRunId;
        private string Outcome => outcome;
        private bool Backpressure => outcome.StartsWith("backpressure", StringComparison.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/v1/status")
                return Json("{\"phase\":\"ready\",\"stop_all\":false,\"models\":[{\"id\":\"owned-text\",\"state\":\"loaded\",\"context\":32768}]}");
            if (request.Method == HttpMethod.Post && path == "/v1/chat/completions")
            {
                var ordinal = Interlocked.Increment(ref ChatRequests);
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal("owned-text", payload.RootElement.GetProperty("model").GetString());
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new ResponseStream(this, ordinal == 1))
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                response.Headers.TryAddWithoutValidation("X-Dulche-Run-Id", ordinal == 1 ? RunId : "completed-next-run");
                return response;
            }
            if (request.Method == HttpMethod.Post && path == "/v1/agents/" + RunId + "/stop")
            {
                Interlocked.Increment(ref StopRequests);
                StoppedRunId = RunId;
                StopTokenWasCanceled = token.IsCancellationRequested;
                StopEntered.TrySetResult();
                await ReleaseStop.Task.WaitAsync(token);
                return Json("{\"id\":\"" + RunId + "\",\"state\":\"stopped\",\"settled\":" +
                    (outcome == "unconfirmed" ? "false" : "true") + "}");
            }
            throw new InvalidOperationException("Unexpected layered Stop HTTP request: " + request.Method + " " + path);
        }

        private static HttpResponseMessage Json(string value) =>
            new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };

        private sealed class ResponseStream(LayeredStopHttpHandler owner, bool first) : Stream
        {
            private int _segment, _released;
            private byte[]? _remaining;
            private int _offset;
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use original async response reads.");
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) =>
                new(ReadOriginalAsync(buffer, token));
            private async Task<int> ReadOriginalAsync(Memory<byte> buffer, CancellationToken token)
            {
                if (_remaining is null)
                {
                    if (first && owner.Backpressure && _segment == 0)
                    {
                        owner.ReadEntered.TrySetResult();
                        await owner.ReleaseRead.Task.WaitAsync(token);
                    }
                    if (first && _segment >= (owner.Backpressure ? 3 : 1))
                    {
                        if (owner.Backpressure) owner.ReadAfterBurst.TrySetResult();
                        var actualRead = Task.Delay(Timeout.InfiniteTimeSpan, token);
                        // Register last: cancellation invokes this independent callback
                        // before completing the read whose continuation disposes it.
                        using var callback = owner.Outcome == "callback"
                            ? token.Register(() =>
                            {
                                Interlocked.Increment(ref owner.CallbackInvocations);
                                throw owner.CallbackFailure;
                            }) : default;
                        owner.ReadEntered.TrySetResult();
                        await actualRead;
                        return 0;
                    }
                    if (!first && _segment != 0) return 0;
                    _remaining = Encoding.UTF8.GetBytes(first
                        ? "data: {\"choices\":[{\"delta\":{\"content\":\"partial " + _segment + "\"}}]}\n\n"
                        : "data: {\"choices\":[{\"delta\":{\"content\":\"completed\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n");
                    _offset = 0;
                    _segment++;
                }
                var count = Math.Min(buffer.Length, _remaining.Length - _offset);
                _remaining.AsMemory(_offset, count).CopyTo(buffer);
                _offset += count;
                if (_offset == _remaining.Length) _remaining = null;
                return count;
            }
            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing && Interlocked.Exchange(ref _released, 1) == 0)
                {
                    Interlocked.Increment(ref owner.StreamDisposals);
                    if (first && owner.Outcome == "backpressure-cleanup") throw owner.CleanupFailure;
                }
            }
        }
    }

    private static ModelDescriptor OrdinaryStopModel(Rig rig) => rig.Provider.Model.Model with
    {
        Name = rig.Provider.Model.Key,
        Capabilities = new HashSet<ToolCapability> { ToolCapability.Text }
    };

    private static Task OrdinaryStopSend(NewChatPage page) => Assert.IsAssignableFrom<Task>(typeof(NewChatPage)
        .GetField("_originalSend", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
}
