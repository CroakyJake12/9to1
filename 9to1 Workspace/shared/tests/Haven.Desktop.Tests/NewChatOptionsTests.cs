using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Infrastructure;
using Haven.Infrastructure.Tests;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    [AvaloniaFact]
    public async Task Native_Options_Temporary_preserves_the_unsent_draft_and_started_chat_requires_explicit_local_Save()
    {
        var token = TestContext.Current.CancellationToken;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        await using var rig = new Rig(temporary: false);
        CaptureCompactionWire(rig);
        var model = ConfigureCompactionModel(rig);
        var page = CreateCompactionPage(rig, database, new CompactionModelClient(rig.Client));
        await using var scope = new OptionsChatPageScope(page);
        try
        {
            scope.Window.Show();
            await PrepareCompactionPageAsync(page, model, token);
            await scope.PrepareComposerAsync(token);
            var id = page.ConversationId;
            page.SetDraft("Keep this exact unsent draft.");
            Assert.False(page.IsTemporary);
            Assert.Null(await database.Conversations.GetAsync(id, token));

            await scope.InvokeOptionAsync("options-temporary", token);
            Assert.True(page.IsTemporary);
            Assert.Equal(id, page.ConversationId);
            Assert.Equal("Keep this exact unsent draft.", page.ComposerInput.Text);
            Assert.Null(await database.Conversations.GetAsync(id, token));

            await scope.InvokeOptionAsync("options-temporary", token);
            Assert.False(page.IsTemporary);
            Assert.Equal(id, page.ConversationId);
            Assert.Equal("Keep this exact unsent draft.", page.ComposerInput.Text);
            Assert.Null(await database.Conversations.GetAsync(id, token));
            await scope.InvokeOptionAsync("options-temporary", token);
            await SendCompactionProbeAsync(page, "A retained Temporary exchange", token);
            var history = page.ProjectionState.Messages.Select(message => (message.Id, message.Content)).ToArray();
            Assert.Equal(2, history.Length);
            page.SetDraft("Unsent draft while deciding whether to save.");

            await scope.InvokeOptionAsync("options-temporary", token);
            Assert.True(page.IsTemporary);
            Assert.Null(await database.Conversations.GetAsync(id, token));
            InvokeButton(await FindChoiceAsync(page, "Cancel", token));
            Assert.True(page.IsTemporary);
            Assert.Equal(history, page.ProjectionState.Messages.Select(message => (message.Id, message.Content)));
            Assert.Equal("Unsent draft while deciding whether to save.", page.ComposerInput.Text);

            await scope.InvokeOptionAsync("options-temporary", token);
            var saved = WaitForStatus(page, "This chat is saved locally.");
            InvokeButton(await FindChoiceAsync(page, "Save locally", token));
            await saved.WaitAsync(TimeSpan.FromSeconds(15), token);
            await page.JoinAdmittedOriginalWorkAsync().WaitAsync(TimeSpan.FromSeconds(15), token);
            Assert.Equal(id, page.ConversationId);
            Assert.False(page.IsTemporary);
            var stored = Assert.IsType<Conversation>(await database.Conversations.GetAsync(id, token));
            Assert.False(stored.IsTemporary);
            Assert.Equal(history, (await database.Conversations.GetMessagesAsync(id, token)).Select(message => (message.Id, message.Content)));
            Assert.Equal("Unsent draft while deciding whether to save.", page.ComposerInput.Text);
            var panel = await scope.OpenOptionsAsync(token);
            Assert.False(OptionsChatControl<Button>(panel, "options-temporary").IsEffectivelyEnabled);
            Assert.Contains("saved history", scope.Composer.Source.ConversationActions!.Current!.TemporaryStatus);
        }
        catch (Exception error) { scope.TestFailure = error; throw; }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_Options_compaction_dispatches_the_selected_model_and_retains_complete_raw_history(bool temporary)
    {
        var token = TestContext.Current.CancellationToken;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        await using var rig = new Rig(temporary: false);
        CaptureCompactionWire(rig);
        var model = ConfigureCompactionModel(rig);
        var client = new CompactionModelClient(rig.Client);
        var page = CreateCompactionPage(rig, database, client);
        await using var scope = new OptionsChatPageScope(page);
        try
        {
            scope.Window.Show();
            await PrepareCompactionPageAsync(page, model, token);
            await scope.PrepareComposerAsync(token);
            if (temporary) await scope.InvokeOptionAsync("options-temporary", token);
            for (var index = 0; index < 6; index++)
                await SendCompactionProbeAsync(page, $"Options source requirement {index}", token);
            var original = page.ProjectionState.Messages.Select(message => (message.Id, message.Content)).ToArray();
            Assert.Equal(12, original.Length);
            page.SetDraft("Keep the next unsent request.");
            Assert.Empty(client.CompactionRequests);

            await scope.InvokeOptionAsync("options-compact", token);

            var request = Assert.Single(client.CompactionRequests);
            Assert.Equal(model.Name, request.Model);
            Assert.Contains("Options source requirement 0", Assert.Single(request.Messages).Content);
            Assert.Equal(original, page.ProjectionState.Messages.Select(message => (message.Id, message.Content)));
            Assert.Equal("Keep the next unsent request.", page.ComposerInput.Text);
            var summaries = await database.Conversations.GetContextEntriesAsync(page.ConversationId, token);
            if (temporary)
            {
                Assert.Empty(summaries);
                Assert.Null(await database.Conversations.GetAsync(page.ConversationId, token));
                Assert.Contains("session-only summary", page.ProjectionState.StatusText);
            }
            else
            {
                Assert.Equal(CompactionModelClient.Summary, Assert.Single(summaries).Content);
                var stored = await database.Conversations.GetMessagesAsync(page.ConversationId, token);
                Assert.Equal(6, stored.Count(message => message.IsCompacted));
                Assert.Equal(original, stored.Select(message => (message.Id, message.Content)));
            }
            var panel = await scope.OpenOptionsAsync(token);
            Assert.False(OptionsChatControl<Button>(panel, "options-compact").IsEffectivelyEnabled);
            Assert.False(scope.Composer.Source.ConversationActions!.Current!.CanCompactContext);
            Assert.Single(client.CompactionRequests); // Reopening the menu never invokes inference.
        }
        catch (Exception error) { scope.TestFailure = error; throw; }
    }

    [AvaloniaFact]
    public async Task Native_Options_compaction_eligibility_follows_the_active_branch_prompt_context_and_withdraws_after_success()
    {
        var token = TestContext.Current.CancellationToken;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        await using var rig = new Rig(temporary: false);
        var wire = CaptureCompactionWire(rig);
        var model = ConfigureCompactionModel(rig);
        var client = new CompactionModelClient(rig.Client);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var onA = await database.CaptureCompactionAsync(conversation.Id, history.Take(8).ToArray(), token);
        await database.Conversations.CommitCompactionAsync(conversation.Id, onA, token);
        var branchB = await database.Production.CreateBranchAsync(conversation.Id, onA.SourceBranchId!.Value,
            null, "Same text, different effective branch version", ConversationBranchReason.EditedUserMessage, token);
        await database.Production.AddVersionAsync(history[0].Id, branchB.Id,
            MessageVersionKind.UserEdit, history[0].Content, null, true, token);
        await database.Production.SetCurrentBranchAsync(conversation.Id, onA.SourceBranchId.Value, token);
        var saved = (await database.Conversations.GetAsync(conversation.Id, token))!;
        var page = CreateCompactionPage(rig, database, client);
        await using var scope = new OptionsChatPageScope(page);
        try
        {
            scope.Window.Show();
            await PrepareCompactionPageAsync(page, model, token);
            await page.LoadConversationAsync(saved); page.SelectModel(model);
            await scope.PrepareComposerAsync(token);
            var onAPanel = await scope.OpenOptionsAsync(token);
            Assert.False(OptionsChatControl<Button>(onAPanel, "options-compact").IsEffectivelyEnabled);
            OptionsChatControl<Button>(onAPanel, "options-cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await scope.Composer.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(15), token);

            await database.Production.SetCurrentBranchAsync(conversation.Id, branchB.Id, token);
            await page.LoadConversationAsync(saved); page.SelectModel(model);
            await scope.PrepareComposerAsync(token);
            page.SetDraft("Retain the branch B follow-up.");
            var before = await database.Conversations.GetPromptContextAsync(conversation.Id, token);
            Assert.Equal(branchB.Id, before.BranchId); Assert.Empty(before.Compactions);
            Assert.Equal(12, before.Messages.Count);
            Assert.Equal(8, before.OriginalMessages.Count(message => message.IsCompacted));

            var panel = await scope.OpenOptionsAsync(token);
            var compact = OptionsChatControl<Button>(panel, "options-compact");
            Assert.True(compact.IsEffectivelyEnabled);
            Assert.True(scope.Composer.Source.ConversationActions!.Current!.CanCompactContext);
            Assert.Empty(wire); Assert.Empty(client.CompactionRequests);
            compact.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await scope.Composer.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(15), token);

            Assert.Equal(model.Name, Assert.Single(client.CompactionRequests).Model);
            Assert.Equal("Retain the branch B follow-up.", page.ComposerInput.Text);
            var after = await database.Conversations.GetPromptContextAsync(conversation.Id, token);
            Assert.Equal(branchB.Id, after.BranchId);
            Assert.Equal(CompactionModelClient.Summary, Assert.Single(after.Compactions).Summary.Content);
            Assert.Equal(6, after.Messages.Count);
            Assert.Equal(history.Select(message => (message.Id, message.Content)),
                after.OriginalMessages.Select(message => (message.Id, message.Content)));
            var completed = await scope.OpenOptionsAsync(token);
            Assert.False(OptionsChatControl<Button>(completed, "options-compact").IsEffectivelyEnabled);
            Assert.False(scope.Composer.Source.ConversationActions!.Current!.CanCompactContext);
            Assert.Single(client.CompactionRequests); Assert.Empty(wire);
        }
        catch (Exception error) { scope.TestFailure = error; throw; }
    }

    [AvaloniaFact]
    public async Task Native_Options_context_observation_retains_a_held_read_and_its_late_failure_during_close()
    {
        var token = TestContext.Current.CancellationToken;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        await using var rig = new Rig(temporary: false);
        var model = ConfigureCompactionModel(rig);
        var client = new CompactionModelClient(rig.Client);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<ConversationPromptContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new IOException("controlled late Options context observation failure");
        var holdRead = false;
        var source = NativeComposerProxy<ICompactionReadRepository>((name, arguments) =>
        {
            if (holdRead && name == nameof(IConversationPromptContextRepository.GetPromptContextAsync) &&
                Equals(arguments![0], conversation.Id))
            {
                entered.TrySetResult();
                return held.Task; // The actual storage operation ignores waiter cancellation.
            }
            return typeof(ConversationRepository).GetMethod(name)!.Invoke(database.Conversations, arguments);
        });
        var page = CreateCompactionPage(rig, database, client, source);
        await using var scope = new OptionsChatPageScope(page)
        {
            Release = () => held.TrySetException(late), ExpectedOriginalCause = late
        };
        scope.AdditionalOriginals.Add(held.Task);
        try
        {
            scope.Window.Show();
            await PrepareCompactionPageAsync(page, model, token);
            await page.LoadConversationAsync(conversation); page.SelectModel(model);
            await scope.PrepareComposerAsync(token);
            holdRead = true;
            OptionsChatControl<Button>(scope.Composer, "settings-button").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var opening = scope.Composer.CaptureOriginalActionSettlement();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            Assert.False(opening.IsCompleted);
            Assert.Null(scope.Composer.OptionsPopup);
            Assert.False(scope.Composer.Source.ConversationActions!.Current!.CanCompactContext);
            Assert.Empty(client.CompactionRequests);
            var close = page.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            held.SetException(late);
            var openingFailure = await Record.ExceptionAsync(() => opening.WaitAsync(TimeSpan.FromSeconds(15), token));
            var closeFailure = await Record.ExceptionAsync(() => close.WaitAsync(TimeSpan.FromSeconds(15), token));
            Assert.NotNull(closeFailure); Assert.True(ContainsCompactionCause(closeFailure, late));
            if (openingFailure is not null) Assert.True(OptionsRetirementCause(openingFailure, late));
            scope.ObservedCloseFailure = closeFailure;
            Assert.Empty(client.CompactionRequests);
            Assert.Equal(history, await database.Conversations.GetMessagesAsync(conversation.Id, token));
            Assert.Empty(await database.Conversations.GetContextEntriesAsync(conversation.Id, token));
        }
        catch (Exception error) { scope.TestFailure = error; throw; }
    }

    [AvaloniaFact]
    public async Task Native_Options_preserve_active_mention_text_selection_and_popup_focus_while_response_and_permissions_change()
    {
        var token = TestContext.Current.CancellationToken;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        await using var rig = new Rig(temporary: false);
        var wire = CaptureCompactionWire(rig);
        var model = ConfigureCompactionModel(rig);
        var client = new CompactionModelClient(rig.Client);
        var page = CreateCompactionPage(rig, database, client);
        await using var scope = new OptionsChatPageScope(page);
        try
        {
            scope.Window.Show();
            await PrepareCompactionPageAsync(page, model, token);
            await scope.PrepareComposerAsync(token);
            const string draft = "Keep this exact draft @budget";
            // Editing the actual composer creates the mention through its real parser;
            // no mention state is injected into the page.
            page.SetDraft(draft + " ");
            OptionsChatControl<TextBox>(scope.Composer, "composer-input").Text = draft;
            page.ComposerInput.PlaceCaretAtEnd();
            var mention = Assert.IsType<ChatMentionQuery>(typeof(NewChatPage)
                .GetField("_activeMention", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
            Assert.Equal("budget", mention.Query);
            page.ComposerInput.SetSelection(draft.Length - 3, draft.Length);
            var selection = (page.ComposerInput.SelectionStart, page.ComposerInput.SelectionLength, page.ComposerInput.CaretIndex);
            var id = page.ConversationId;
            var opener = OptionsChatControl<Button>(scope.Composer, "settings-button");
            var panel = await scope.OpenOptionsAsync(token);
            var response = Assert.Single(panel.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Prefer text"));
            response.BringIntoView(); scope.Window.UpdateLayout();
            Assert.True(response.Focus());
            response.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await scope.Composer.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(15), token);
            scope.Window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); scope.Window.UpdateLayout();
            Assert.Equal(GenerativeUiResponseMode.PreferText, scope.Composer.Source.ResponseMode);
            Assert.Same(panel, scope.Composer.OptionsPopup!.Child);
            Assert.Same(response, TopLevel.GetTopLevel(panel)!.FocusManager?.GetFocusedElement());
            Assert.Equal(draft, page.ComposerInput.Text);
            Assert.Equal(selection, (page.ComposerInput.SelectionStart, page.ComposerInput.SelectionLength, page.ComposerInput.CaretIndex));

            OptionsChatControl<Button>(panel, "options-permissions-toggle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await scope.Composer.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(15), token);
            scope.Window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); scope.Window.UpdateLayout();
            Assert.Single(panel.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Just chat"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await scope.Composer.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(15), token);
            Assert.Equal(ChatActionMode.AllowBasicActions, page.EffectiveChatActionMode);
            OptionsChatControl<Button>(panel, "options-save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await scope.Composer.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(15), token);
            scope.Window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); scope.Window.UpdateLayout();
            Assert.Equal(ChatActionMode.JustChat, page.EffectiveChatActionMode);
            Assert.Equal(GenerativeUiResponseMode.PreferText, scope.Composer.Source.ResponseMode);
            Assert.Null(scope.Composer.OptionsPopup);
            Assert.Same(opener, scope.Window.FocusManager?.GetFocusedElement());
            Assert.Equal(id, page.ConversationId);
            Assert.Equal(draft, page.ComposerInput.Text);
            Assert.Equal(selection, (page.ComposerInput.SelectionStart, page.ComposerInput.SelectionLength, page.ComposerInput.CaretIndex));
            Assert.Empty(wire); Assert.Empty(client.CompactionRequests);
        }
        catch (Exception error) { scope.TestFailure = error; throw; }
    }

    [AvaloniaFact]
    public async Task Native_Options_compaction_close_joins_the_actual_held_request_and_retains_its_late_failure()
    {
        var token = TestContext.Current.CancellationToken;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        await using var rig = new Rig(temporary: false);
        var model = ConfigureCompactionModel(rig);
        var held = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new IOException("controlled late Options compaction failure");
        var client = new CompactionModelClient(rig.Client)
        {
            // Deliberately keep the real returned source task alive after its waiter is cancelled.
            Complete = (_, _) => { entered.TrySetResult(); return held.Task; }
        };
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var page = CreateCompactionPage(rig, database, client);
        await using var scope = new OptionsChatPageScope(page)
        {
            Release = () => held.TrySetException(late), ExpectedOriginalCause = late
        };
        scope.AdditionalOriginals.Add(held.Task);
        try
        {
            scope.Window.Show();
            await PrepareCompactionPageAsync(page, model, token);
            await page.LoadConversationAsync(conversation); page.SelectModel(model);
            await scope.PrepareComposerAsync(token);
            page.SetDraft("Retain the pending input after failure.");
            var panel = await scope.OpenOptionsAsync(token);
            OptionsChatControl<Button>(panel, "options-compact").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var action = scope.Composer.CaptureOriginalActionSettlement();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            Assert.False(action.IsCompleted);
            Assert.Single(client.CompactionRequests);
            var close = page.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            held.SetException(late);
            var actionError = await Record.ExceptionAsync(() => action.WaitAsync(TimeSpan.FromSeconds(15), token));
            var closeError = await Record.ExceptionAsync(() => close.WaitAsync(TimeSpan.FromSeconds(15), token));
            Assert.NotNull(closeError);
            Assert.True(ContainsCompactionCause(closeError, late));
            if (actionError is not null) Assert.True(OptionsRetirementCause(actionError, late));
            scope.ObservedCloseFailure = closeError;
            Assert.Empty(await database.Conversations.GetContextEntriesAsync(conversation.Id, token));
            var retained = await database.Conversations.GetMessagesAsync(conversation.Id, token);
            Assert.Equal(history.Select(message => (message.Id, message.Content)), retained.Select(message => (message.Id, message.Content)));
            Assert.DoesNotContain(retained, message => message.IsCompacted);
        }
        catch (Exception error) { scope.TestFailure = error; throw; }
    }

    private static bool OptionsRetirementCause(Exception error, Exception expected) => ReferenceEquals(error, expected) ||
        error is OperationCanceledException || error is AggregateException aggregate && aggregate.InnerExceptions.All(cause => OptionsRetirementCause(cause, expected));

    private static T OptionsChatControl<T>(Control root, string id) where T : Control => Assert.Single(
        root.GetVisualDescendants().OfType<Control>().Prepend(root).OfType<T>(),
        control => AutomationProperties.GetAutomationId(control) == id || control.Name == id);

    private sealed class OptionsChatPageScope(NewChatPage page) : IAsyncDisposable
    {
        internal readonly Window Window = new() { Content = page, Width = 1000, Height = 760 };
        internal ChatComposerCuiView Composer => Assert.Single(page.GetVisualDescendants().OfType<ChatComposerCuiView>());
        internal Exception? TestFailure, ObservedCloseFailure, ExpectedOriginalCause;
        internal Action? Release;
        internal readonly List<Task> AdditionalOriginals = [];
        private Task? _close;
        internal async Task PrepareComposerAsync(CancellationToken token)
        {
            Window.Activate();
            Window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); Window.UpdateLayout();
            await Composer.InitializeAsync(token).WaitAsync(TimeSpan.FromSeconds(15), token);
            Assert.NotNull(Composer.Source.ConversationActions);
            Window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); Window.UpdateLayout();
        }
        internal async Task<Control> OpenOptionsAsync(CancellationToken token)
        {
            Composer.RefreshSurface();
            OptionsChatControl<Button>(Composer, "settings-button").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Composer.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(15), token);
            Window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); Window.UpdateLayout();
            Assert.True(Composer.OptionsPopup?.IsOpen);
            return Assert.IsAssignableFrom<Control>(Composer.OptionsPopup!.Child);
        }
        internal async Task InvokeOptionAsync(string id, CancellationToken token)
        {
            var panel = await OpenOptionsAsync(token);
            var button = OptionsChatControl<Button>(panel, id);
            Assert.True(button.IsEffectivelyEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Composer.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(15), token);
            Window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); Window.UpdateLayout();
            Assert.Null(Composer.OptionsPopup);
        }
        public ValueTask DisposeAsync() => new(_close ??= CloseAsync());
        private async Task CloseAsync()
        {
            var failures = new List<Exception>();
            try { Release?.Invoke(); } catch (Exception error) { failures.Add(error); }
            Task? close = null;
            try { close = page.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
            var originals = new List<Task>(AdditionalOriginals);
            if (close is not null) originals.Add(close);
            foreach (var original in originals.Distinct())
            {
                try { await original.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken); }
                catch (Exception error)
                {
                    if (!ReferenceEquals(error, ObservedCloseFailure) && !ReferenceEquals(error, ExpectedOriginalCause)) failures.Add(error);
                }
            }
            if (close?.IsCompleted == true && originals.All(original => original.IsCompleted))
            {
                try { Window.Content = null; Window.Close(); }
                catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count == 0) return;
            if (TestFailure is not null) failures.Insert(0, TestFailure);
            throw new AggregateException("Native Options test and original chat cleanup did not settle cleanly.", failures);
        }
    }
}
