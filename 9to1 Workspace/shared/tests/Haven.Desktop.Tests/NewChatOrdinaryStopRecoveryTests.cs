using System.Reflection;
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
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Ordinary_native_chat_stop_retains_faulted_or_foreign_provider_cancellation(bool faulted, bool sameToken)
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
            page = CreateNativeComposerPage(rig, directory, new NativeComposerDraftStore());
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

    [AvaloniaFact]
    public async Task Ordinary_native_chat_stop_keeps_independent_provider_cleanup_failure_visible()
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
            page = CreateNativeComposerPage(rig, directory, new NativeComposerDraftStore());
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

    private static ModelDescriptor OrdinaryStopModel(Rig rig) => rig.Provider.Model.Model with
    {
        Name = rig.Provider.Model.Key,
        Capabilities = new HashSet<ToolCapability> { ToolCapability.Text }
    };

    private static Task OrdinaryStopSend(NewChatPage page) => Assert.IsAssignableFrom<Task>(typeof(NewChatPage)
        .GetField("_originalSend", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
}
