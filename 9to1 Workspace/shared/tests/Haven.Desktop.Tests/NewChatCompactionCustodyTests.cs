using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Application.Haven;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Infrastructure;
using Haven.Infrastructure.Tests;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    [AvaloniaFact]
    public async Task Temporary_compaction_retains_raw_history_and_one_summary_through_explicit_Save_and_reopen()
    {
        var token = TestContext.Current.CancellationToken;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        await using var rig = new Rig(temporary: false);
        var wire = CaptureCompactionWire(rig);
        var client = new CompactionModelClient(rig.Client);
        var model = ConfigureCompactionModel(rig);
        var page = CreateCompactionPage(rig, database, client);
        var window = new Window { Width = 1000, Height = 760, Content = page };
        try
        {
            await PrepareCompactionPageAsync(page, model, token);
            window.Show();
            await page.SetTemporaryAsync(true);
            for (var index = 0; index < 6; index++)
                await SendCompactionProbeAsync(page, $"Original Temporary fact {index}", token);
            var before = page.ProjectionState.Messages.ToArray();
            Assert.Equal(12, before.Length);
            Assert.Null(await database.Conversations.GetAsync(page.ConversationId, token));
            page.SetDraft("Keep the exact pending draft");

            await page.CompactContextAsync().WaitAsync(TimeSpan.FromSeconds(15), token);

            Assert.Equal(before.Select(message => (message.Id, message.Content)),
                page.ProjectionState.Messages.Select(message => (message.Id, message.Content)));
            Assert.Equal("Keep the exact pending draft", page.ComposerInput.Text);
            Assert.Contains("session-only summary", page.ProjectionState.StatusText);
            Assert.Empty(await database.Conversations.GetContextEntriesAsync(page.ConversationId, token));
            Assert.Contains("Original Temporary fact 0", Assert.Single(client.CompactionRequests).Messages.Single().Content);
            await SendCompactionProbeAsync(page, "A new turn after session compaction", token);
            Assert.Equal(1, CountOccurrences(wire[^1].SystemPrompt, CompactionModelClient.Summary));
            Assert.Contains(wire[^1].Messages, message => message.Content == "Original Temporary fact 5");
            page.SetDraft("Saved unsent draft");
            var originalId = page.ConversationId;
            await SaveCompactionChatAsync(page, token);

            var saved = Assert.IsType<Conversation>(await database.Conversations.GetAsync(originalId, token));
            Assert.False(saved.IsTemporary);
            var history = await database.Conversations.GetMessagesAsync(originalId, token);
            Assert.Equal(before.Select(message => (message.Id, message.Content)),
                history.Take(before.Length).Select(message => (message.Id, message.Content)));
            Assert.Equal(6, history.Count(message => message.IsCompacted));
            Assert.Equal(CompactionModelClient.Summary,
                Assert.Single(await database.Conversations.GetContextEntriesAsync(originalId, token)).Content);
            Assert.Equal("Saved unsent draft", page.ComposerInput.Text);
            await SendCompactionProbeAsync(page, "A new ordinary turn after Save", token);
            Assert.Equal(1, CountOccurrences(wire[^1].SystemPrompt, CompactionModelClient.Summary));

            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(15), token);
            window.Content = null;
            page = CreateCompactionPage(rig, database, client);
            window.Content = page;
            await PrepareCompactionPageAsync(page, model, token);
            await page.LoadConversationAsync((await database.Conversations.GetAsync(originalId, token))!);
            page.SelectModel(model);
            await SendCompactionProbeAsync(page, "A turn after reopening the actual saved conversation", token);
            Assert.Equal(originalId, page.ConversationId);
            Assert.Equal(1, CountOccurrences(wire[^1].SystemPrompt, CompactionModelClient.Summary));
            Assert.DoesNotContain(wire[^1].Messages, message => message.Content == "Original Temporary fact 0");
            Assert.Single(await database.Conversations.GetContextEntriesAsync(originalId, token));
        }
        finally
        {
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(15), token);
            window.Content = null; window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Temporary_failed_or_empty_compaction_retains_raw_prompt_context_and_saves_it_without_flags(bool empty)
    {
        var token = TestContext.Current.CancellationToken;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        await using var rig = new Rig(temporary: false);
        var wire = CaptureCompactionWire(rig);
        var failure = new IOException("Actual controlled summary generation failed.");
        var client = new CompactionModelClient(rig.Client)
        {
            Complete = (_, _) => empty ? Task.FromResult("  ") : Task.FromException<string>(failure)
        };
        var model = ConfigureCompactionModel(rig);
        var page = CreateCompactionPage(rig, database, client);
        Exception? closeCause = null;
        try
        {
            await PrepareCompactionPageAsync(page, model, token);
            await page.SetTemporaryAsync(true);
            for (var index = 0; index < 5; index++)
                await SendCompactionProbeAsync(page, $"Original Temporary fact {index}", token);
            var before = page.ProjectionState.Messages.ToArray();
            page.SetDraft("Draft retained on failed compaction");

            await page.CompactContextAsync().WaitAsync(TimeSpan.FromSeconds(15), token);

            Assert.StartsWith("Context compaction failed:", page.ProjectionState.StatusText);
            Assert.Equal(before.Select(message => (message.Id, message.Content)),
                page.ProjectionState.Messages.Select(message => (message.Id, message.Content)));
            Assert.Equal("Draft retained on failed compaction", page.ComposerInput.Text);
            await SendCompactionProbeAsync(page, "Verify retained raw context", token);
            Assert.Contains(wire[^1].Messages, message => message.Content == "Original Temporary fact 0");
            Assert.Equal(0, CountOccurrences(wire[^1].SystemPrompt, CompactionModelClient.Summary));
            await SaveCompactionChatAsync(page, token);
            var stored = await database.Conversations.GetMessagesAsync(page.ConversationId, token);
            Assert.All(stored, message => Assert.False(message.IsCompacted));
            Assert.Equal(before.Select(message => (message.Id, message.Content)),
                stored.Take(before.Length).Select(message => (message.Id, message.Content)));
            Assert.Empty(await database.Conversations.GetContextEntriesAsync(page.ConversationId, token));
        }
        finally { closeCause = await Record.ExceptionAsync(() => page.CloseAndDrainAsync()); }
        Assert.NotNull(closeCause);
        if (empty) Assert.True(ContainsCompactionCause<InvalidOperationException>(closeCause));
        else Assert.True(ContainsCompactionCause(closeCause, failure));
    }

    [AvaloniaFact]
    public async Task Ordinary_compaction_storage_failure_keeps_raw_context_and_retry_injects_one_retained_summary()
    {
        var token = TestContext.Current.CancellationToken;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        await using var rig = new Rig(temporary: false);
        var wire = CaptureCompactionWire(rig);
        var client = new CompactionModelClient(rig.Client);
        var model = ConfigureCompactionModel(rig);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var page = CreateCompactionPage(rig, database, client);
        Exception? closeCause = null;
        try
        {
            await PrepareCompactionPageAsync(page, model, token);
            await page.LoadConversationAsync(conversation);
            page.SelectModel(model);
            page.SetDraft("Retain the current draft");
            await database.ExecuteAsync("""
                CREATE TRIGGER fail_native_summary BEFORE INSERT ON conversation_context
                BEGIN SELECT RAISE(ABORT,'actual native summary storage failure'); END;
                """, token);

            await page.CompactContextAsync().WaitAsync(TimeSpan.FromSeconds(15), token);

            Assert.StartsWith("Context compaction failed:", page.ProjectionState.StatusText);
            Assert.Equal("Retain the current draft", page.ComposerInput.Text);
            Assert.Equal(history, await database.Conversations.GetContextMessagesAsync(conversation.Id, token));
            Assert.Empty(await database.Conversations.GetContextEntriesAsync(conversation.Id, token));
            await SendCompactionProbeAsync(page, "Read the unchanged source context", token);
            Assert.Contains(wire[^1].Messages, message => message.Content == history[0].Content);
            await database.ExecuteAsync("DROP TRIGGER fail_native_summary;", token);
            await page.CompactContextAsync().WaitAsync(TimeSpan.FromSeconds(15), token);
            Assert.Contains("saved summary", page.ProjectionState.StatusText);
            await SendCompactionProbeAsync(page, "Read the successfully compacted context", token);
            Assert.Equal(1, CountOccurrences(wire[^1].SystemPrompt, CompactionModelClient.Summary));
            Assert.Single(await database.Conversations.GetContextEntriesAsync(conversation.Id, token));
            Assert.Equal(history.Select(message => (message.Id, message.Content)),
                (await database.Conversations.GetMessagesAsync(conversation.Id, token)).Take(history.Length).Select(message => (message.Id, message.Content)));
        }
        finally { closeCause = await Record.ExceptionAsync(() => page.CloseAndDrainAsync()); }
        Assert.True(ContainsCompactionCause<SqliteException>(closeCause));
    }

    [AvaloniaFact]
    public async Task Reopened_compacted_chat_refuses_provider_dispatch_after_summary_read_failure_and_allows_read_retry()
    {
        var token = TestContext.Current.CancellationToken;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        await using var rig = new Rig(temporary: false);
        var wire = CaptureCompactionWire(rig);
        var client = new CompactionModelClient(rig.Client);
        var model = ConfigureCompactionModel(rig);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var replacement = await database.CaptureCompactionAsync(conversation.Id, history.Take(4).ToArray(), token);
        await database.Conversations.CommitCompactionAsync(conversation.Id, replacement, token);
        var saved = (await database.Conversations.GetAsync(conversation.Id, token))!;
        var before = await database.Conversations.GetMessagesAsync(conversation.Id, token);
        var readFailure = new IOException("Actual retained-summary read failed.");
        var failRead = true;
        var delayRead = false;
        var readEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<ConversationPromptContext> FailOriginalReadAsync(CancellationToken cancellationToken)
        {
            if (delayRead)
            {
                readEntered.TrySetResult();
                await releaseRead.Task.WaitAsync(cancellationToken);
            }
            throw readFailure;
        }
        var source = NativeComposerProxy<ICompactionReadRepository>((name, arguments) =>
            name == nameof(IConversationPromptContextRepository.GetPromptContextAsync) &&
                Equals(arguments![0], conversation.Id) && failRead
                ? FailOriginalReadAsync((CancellationToken)arguments[1]!)
                : typeof(ConversationRepository).GetMethod(name)!.Invoke(database.Conversations, arguments));
        var page = CreateCompactionPage(rig, database, client, source);
        Exception? closeCause = null;
        try
        {
            await PrepareCompactionPageAsync(page, model, token);
            await page.LoadConversationAsync(saved);
            Assert.Contains("Context entries could not be loaded", page.ProjectionState.StatusText);
            page.SelectModel(model);

            delayRead = true;
            var sending = SendCompactionProbeAsync(page, "Accepted input while context is unavailable", token);
            await readEntered.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            page.SetDraft("A newer unsent draft must survive the failed read");
            releaseRead.TrySetResult();
            var sendFailure = await Record.ExceptionAsync(() => sending);
            if (sendFailure is not null) Assert.True(ContainsCompactionCause(sendFailure, readFailure));

            Assert.Empty(wire);
            Assert.Equal("A newer unsent draft must survive the failed read", page.ComposerInput.Text);
            var failedHistory = await database.Conversations.GetMessagesAsync(conversation.Id, token);
            Assert.Equal(before, failedHistory.Take(before.Count));
            Assert.Single(failedHistory, message => message.Content == "Accepted input while context is unavailable");
            Assert.Equal(replacement.Summary,
                Assert.Single(await database.Conversations.GetContextEntriesAsync(conversation.Id, token)));

            failRead = false;
            await page.LoadConversationAsync(saved);
            page.SelectModel(model);
            await SendCompactionProbeAsync(page, "Read the restored summary after retry", token);

            Assert.Single(wire);
            Assert.Equal(1, CountOccurrences(wire[0].SystemPrompt, replacement.Summary.Content));
            Assert.DoesNotContain(wire[0].Messages, message => message.Content == history[0].Content);
            Assert.Equal(history.Select(message => (message.Id, message.Content)),
                (await database.Conversations.GetMessagesAsync(conversation.Id, token)).Take(history.Length)
                    .Select(message => (message.Id, message.Content)));
        }
        finally
        {
            releaseRead.TrySetResult();
            closeCause = await Record.ExceptionAsync(() => page.CloseAndDrainAsync());
        }
        Assert.True(ContainsCompactionCause(closeCause, readFailure));
    }

    [AvaloniaFact]
    public async Task Oversized_original_message_is_not_silently_truncated_and_marked_compacted()
    {
        var token = TestContext.Current.CancellationToken;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        await using var rig = new Rig(temporary: false);
        var client = new CompactionModelClient(rig.Client);
        var model = ConfigureCompactionModel(rig);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        history[0] = history[0] with { Content = new string('x', 180_001) + "Original final evidence" };
        await database.StoreAsync(conversation, history, token);
        var page = CreateCompactionPage(rig, database, client);
        try
        {
            await PrepareCompactionPageAsync(page, model, token);
            await page.LoadConversationAsync(conversation);
            page.SelectModel(model);
            page.SetDraft("Keep this unsent draft");

            await page.CompactContextAsync().WaitAsync(TimeSpan.FromSeconds(15), token);

            Assert.Contains("No messages were excluded", page.ProjectionState.StatusText);
            Assert.Empty(client.CompactionRequests);
            Assert.Equal(history, await database.Conversations.GetContextMessagesAsync(conversation.Id, token));
            Assert.Empty(await database.Conversations.GetContextEntriesAsync(conversation.Id, token));
            Assert.Equal("Keep this unsent draft", page.ComposerInput.Text);
        }
        finally { await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(15), token); }
    }

    [AvaloniaFact]
    public async Task Native_branch_switch_replaces_A_summary_with_B_raw_sources_and_can_return_to_A()
    {
        var token = TestContext.Current.CancellationToken;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        await using var rig = new Rig(temporary: false);
        var wire = CaptureCompactionWire(rig);
        var client = new CompactionModelClient(rig.Client);
        var model = ConfigureCompactionModel(rig);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var a = (await database.Production.GetCurrentBranchAsync(conversation.Id, token))!;
        var b = await database.Production.CreateBranchAsync(conversation.Id, a.Id, history[3].Id, "B", ConversationBranchReason.Manual, token);
        var bOnly = new ChatMessage(Guid.NewGuid(), conversation.Id, MessageRole.User,
            "Native branch B-only source", null, null, null, DateTimeOffset.UtcNow);
        await database.Conversations.AddMessageAsync(bOnly, token);
        await database.Production.SetCurrentBranchAsync(conversation.Id, a.Id, token);
        var replacement = await database.CaptureCompactionAsync(conversation.Id, history.Take(8).ToArray(), token);
        await database.Conversations.CommitCompactionAsync(conversation.Id, replacement, token);
        var page = CreateCompactionPage(rig, database, client);
        try
        {
            await PrepareCompactionPageAsync(page, model, token);
            await page.LoadConversationAsync((await database.Conversations.GetAsync(conversation.Id, token))!);
            page.SelectModel(model);
            await SendCompactionProbeAsync(page, "Continue on branch A", token);
            Assert.Equal(1, CountOccurrences(wire[^1].SystemPrompt, replacement.Summary.Content));
            Assert.DoesNotContain(wire[^1].Messages, message => message.Content == history[0].Content);

            await database.Production.SetCurrentBranchAsync(conversation.Id, b.Id, token);
            await page.LoadConversationAsync((await database.Conversations.GetAsync(conversation.Id, token))!);
            page.SelectModel(model);
            await SendCompactionProbeAsync(page, "Continue on branch B", token);

            Assert.Equal(0, CountOccurrences(wire[^1].SystemPrompt, replacement.Summary.Content));
            Assert.Contains(wire[^1].Messages, message => message.Content == history[0].Content);
            Assert.Contains(wire[^1].Messages, message => message.Content == bOnly.Content);
            Assert.DoesNotContain(wire[^1].Messages, message => message.Content == history[4].Content);
            Assert.DoesNotContain(wire[^1].Messages, message => message.Content == "Continue on branch A");
            await database.Production.SetCurrentBranchAsync(conversation.Id, a.Id, token);
            await page.LoadConversationAsync((await database.Conversations.GetAsync(conversation.Id, token))!);
            page.SelectModel(model);
            await SendCompactionProbeAsync(page, "Return to branch A", token);
            Assert.Equal(1, CountOccurrences(wire[^1].SystemPrompt, replacement.Summary.Content));
            Assert.DoesNotContain(wire[^1].Messages, message => message.Content == bOnly.Content);
        }
        finally { await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(15), token); }
    }

    [AvaloniaFact]
    public async Task Native_compaction_pins_the_effective_version_before_its_delayed_model_response()
    {
        var token = TestContext.Current.CancellationToken;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        await using var rig = new Rig(temporary: false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new CompactionModelClient(rig.Client)
        {
            Complete = (_, cancellationToken) => { entered.TrySetResult(); return release.Task.WaitAsync(cancellationToken); }
        };
        var model = ConfigureCompactionModel(rig);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var page = CreateCompactionPage(rig, database, client);
        Exception? closeCause = null;
        try
        {
            await PrepareCompactionPageAsync(page, model, token);
            await page.LoadConversationAsync(conversation); page.SelectModel(model);
            page.SetDraft("Retain the draft while a source version changes");
            var compacting = page.CompactContextAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            var branch = (await database.Production.GetCurrentBranchAsync(conversation.Id, token))!;
            await database.Production.AddVersionAsync(history[0].Id, branch.Id, MessageVersionKind.UserEdit,
                history[0].Content, history[0].MetadataJson, true, token);
            release.TrySetResult(CompactionModelClient.Summary);
            await compacting.WaitAsync(TimeSpan.FromSeconds(15), token);

            Assert.StartsWith("Context compaction failed:", page.ProjectionState.StatusText);
            Assert.Equal("Retain the draft while a source version changes", page.ComposerInput.Text);
            Assert.Empty(await database.Conversations.GetContextEntriesAsync(conversation.Id, token));
            Assert.Equal(history, (await database.Conversations.GetPromptContextAsync(conversation.Id, token)).Messages);
            Assert.All(await database.Conversations.GetMessagesAsync(conversation.Id, token), message => Assert.False(message.IsCompacted));
        }
        finally
        {
            release.TrySetResult(CompactionModelClient.Summary);
            closeCause = await Record.ExceptionAsync(() => page.CloseAndDrainAsync());
        }
        Assert.True(ContainsCompactionCause<InvalidOperationException>(closeCause));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Restricted_Space_keeps_canonical_history_replacement_unless_source_only_deliberately_excludes_it(bool temporary, bool sourceOnly)
    {
        var token = TestContext.Current.CancellationToken;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        await using var rig = new Rig(temporary: false);
        var wire = CaptureCompactionWire(rig);
        var model = ConfigureCompactionModel(rig);
        var registry = new SpaceRegistry(new VersionedAtomicSettingsStore(database));
        var space = await registry.CreateAsync("Actual compaction policy", cancellationToken: token);
        space = await registry.UpdateAsync(space with
        {
            ContextPolicy = new SpaceContextPolicy(SourceOnly: sourceOnly, IncludeCurrentSelection: false)
        }, space.Revision, token);
        var (original, history) = CompactionTestDatabase.History(temporary);
        var conversation = original with { SpaceId = space.Id };
        ConversationCompaction replacement;
        TemporaryConversationContext? transient = null;
        if (temporary)
        {
            replacement = CompactionTestDatabase.Replacement(conversation.Id, history.Take(4).ToArray());
            var retained = history.Select((message, index) => index < 4 ? message with { IsCompacted = true } : message).ToArray();
            transient = new TemporaryConversationContext(conversation, retained, [replacement]);
        }
        else
        {
            await database.StoreAsync(conversation, history, token);
            replacement = await database.CaptureCompactionAsync(conversation.Id, history.Take(4).ToArray(), token);
            await database.Conversations.CommitCompactionAsync(conversation.Id, replacement, token);
        }
        var source = new CompactionSpacePolicySource(registry);
        var service = new ChatSessionService(database.Conversations, new CompactionSpaceClient(rig.Client),
            new CapabilityPreflightService(), new Safety(), new WorkspaceToolRuntime(rig.Workspace), new ComputerToolRuntime(new Computer()));
        service.BindOriginalSpacePolicySource(source);
        var options = new GenerationOptions(0.3, 8192, 7) { OriginalTemporaryContext = transient };
        const string prompt = "Current user prompt remains explicit";

        await foreach (var _ in service.SendWithCommittedInputFromOriginalAsync(action => action(), conversation, prompt,
            model, EffortLevel.Medium, [], "Haven", "", DuoMode.Solo, null, null, null, null, token,
            registeredContext: "UNQUALIFIED_PAGE_CONTEXT_MUST_BE_REPLACED", generationOptions: options)) { }

        var request = Assert.Single(wire);
        Assert.True(source.Validations > 0);
        Assert.Contains(CompactionSpacePolicySource.Configuration, request.SystemPrompt);
        Assert.DoesNotContain("UNQUALIFIED_PAGE_CONTEXT_MUST_BE_REPLACED", request.SystemPrompt);
        Assert.Equal(sourceOnly ? 0 : 1, CountOccurrences(request.SystemPrompt, replacement.Summary.Content));
        Assert.DoesNotContain(request.Messages, message => message.Content == history[0].Content);
        if (sourceOnly) Assert.DoesNotContain(request.Messages, message => history.Any(sourceMessage => sourceMessage.Content == message.Content));
        else Assert.Contains(request.Messages, message => message.Content == history[^1].Content);
        Assert.Contains(request.Messages, message => message.Role == "user" && message.Content == prompt);
        Assert.DoesNotContain(nameof(GenerationOptions.OriginalTemporaryContext), JsonSerializer.Serialize(request.Options), StringComparison.Ordinal);
        if (temporary) Assert.Null(await database.Conversations.GetAsync(conversation.Id, token));
        else Assert.Equal(history.Select(message => (message.Id, message.Content)),
            (await database.Conversations.GetMessagesAsync(conversation.Id, token)).Take(history.Length).Select(message => (message.Id, message.Content)));
    }

    [Fact]
    public async Task Compacted_Temporary_context_cannot_be_reconstructed_from_serialized_options_after_its_live_owner_is_missing()
    {
        var token = TestContext.Current.CancellationToken;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        await using var rig = new Rig(temporary: false);
        var wire = CaptureCompactionWire(rig);
        var model = ConfigureCompactionModel(rig);
        var (original, history) = CompactionTestDatabase.History(temporary: true);
        var conversation = original with { CompactedAt = DateTimeOffset.UtcNow };
        var replacement = CompactionTestDatabase.Replacement(conversation.Id, history.Take(4).ToArray());
        var snapshot = new TemporaryConversationContext(conversation,
            history.Select((message, index) => index < 4 ? message with { IsCompacted = true } : message).ToArray(), [replacement]);
        var options = new GenerationOptions { OriginalTemporaryContext = snapshot };
        Assert.Same(snapshot, (options with { Temperature = 0.1 }).OriginalTemporaryContext);
        var serialized = JsonSerializer.Deserialize<GenerationOptions>(JsonSerializer.Serialize(options))!;
        var service = new ChatSessionService(database.Conversations, rig.Client, new CapabilityPreflightService(), new Safety(),
            new WorkspaceToolRuntime(rig.Workspace), new ComputerToolRuntime(new Computer()));
        async Task SendAsync(GenerationOptions actual)
        {
            await foreach (var _ in service.SendWithCommittedInputFromOriginalAsync(action => action(), conversation,
                "Explicit current input", model, EffortLevel.Medium, [], "Haven", "", DuoMode.Solo,
                null, null, null, null, token, generationOptions: actual)) { }
        }

        var failure = await Record.ExceptionAsync(() => SendAsync(serialized));

        Assert.NotNull(failure);
        Assert.True(ContainsCompactionCause<InvalidOperationException>(failure));
        Assert.Empty(wire);
        Assert.Equal(history.Select(message => (message.Id, message.Content)), snapshot.Context.OriginalMessages.Select(message => (message.Id, message.Content)));
        await SendAsync(options);
        Assert.Equal(1, CountOccurrences(Assert.Single(wire).SystemPrompt, replacement.Summary.Content));
        Assert.Null(await database.Conversations.GetAsync(conversation.Id, token));
    }

    [Fact]
    public async Task Canonical_Task_context_keeps_raw_compacted_history_and_waits_for_its_actual_permission_owner()
    {
        var token = TestContext.Current.CancellationToken;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        await using var rig = new Rig(temporary: false);
        var model = ConfigureCompactionModel(rig);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var replacement = await database.CaptureCompactionAsync(conversation.Id, history.Take(4).ToArray(), token);
        await database.Conversations.CommitCompactionAsync(conversation.Id, replacement, token);
        var service = new ChatSessionService(database.Conversations, rig.Client, new CapabilityPreflightService(), new Safety(),
            new WorkspaceToolRuntime(rig.Workspace), new ComputerToolRuntime(new Computer()), taskCoordinator: rig.Tasks,
            taskToolOwner: rig.ToolOwner, taskProviderContextCapture: rig.Capture, taskCloudPermissionRemediation: rig.Owner);
        var stream = new List<ChatStreamEvent>();

        await foreach (var value in service.SendAsync(conversation, "An explicit canonical Task request", model,
            EffortLevel.Medium, [], "Haven", "", DuoMode.Solo, null, null, null, null, token,
            taskExecutionIntent: TaskRunExecutionIntent.CanonicalAgenticTask)) stream.Add(value);

        using var captured = JsonDocument.Parse(Assert.Single(rig.Capture.ChatWire));
        Assert.DoesNotContain(replacement.Summary.Content, captured.RootElement.GetProperty("SystemPrompt").GetString());
        var messages = captured.RootElement.GetProperty("Messages").EnumerateArray().ToArray();
        Assert.Contains(messages, message => message.GetProperty("Content").GetString() == history[0].Content);
        Assert.Contains(messages, message => message.GetProperty("Content").GetString() == history[^1].Content);
        Assert.Single(stream, value => value.Kind == ChatStreamEventKind.PermissionRequired);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Equal(0, rig.Provider.Starts);
        Assert.Equal(TaskExecutionLifecycle.Suspended, service.CurrentCanonicalTask!.State);
    }

    private static ModelDescriptor ConfigureCompactionModel(Rig rig)
    {
        var model = rig.Provider.Model.Model with
        {
            Name = rig.Provider.Model.Key, Capabilities = new HashSet<ToolCapability> { ToolCapability.Text }
        };
        rig.Client.AvailableModels = [model];
        return model;
    }

    private static List<OllamaChatRequest> CaptureCompactionWire(Rig rig)
    {
        var requests = new List<OllamaChatRequest>();
        var observed = new HashSet<OllamaChatRequest>(ReferenceEqualityComparer.Instance);
        rig.Client.OrdinaryProviderBody = (request, _) =>
        {
            if (!observed.Add(request)) return Task.FromResult(false);
            requests.Add(request); return Task.FromResult(true);
        };
        return requests;
    }

    private static async Task PrepareCompactionPageAsync(NewChatPage page, ModelDescriptor model, CancellationToken token)
    {
        await page.OriginalInitialization.WaitAsync(TimeSpan.FromSeconds(15), token);
        await page.RefreshModelsAsync();
        page.SelectModel(model);
    }

    private static async Task SendCompactionProbeAsync(NewChatPage page, string prompt, CancellationToken token)
    {
        page.SetDraft(prompt);
        var field = typeof(NewChatPage).GetField("_originalSend", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previous = field.GetValue(page);
        page.RequestComposerSend();
        var admitted = Assert.IsAssignableFrom<Task>(field.GetValue(page));
        Assert.NotSame(previous, admitted);
        await admitted.WaitAsync(TimeSpan.FromSeconds(15), token);
        Assert.False(page.IsSending);
    }

    private static async Task SaveCompactionChatAsync(NewChatPage page, CancellationToken token)
    {
        var saved = WaitForStatus(page, "This chat is saved locally.");
        await page.ToggleTemporaryAsync();
        InvokeButton(await FindChoiceAsync(page, "Save locally", token));
        await saved.WaitAsync(TimeSpan.FromSeconds(15), token);
        var saving = typeof(NewChatPage).GetField("_temporarySaveInProgress", BindingFlags.Instance | BindingFlags.NonPublic)!;
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(TimeSpan.FromSeconds(15));
        while (Equals(saving.GetValue(page), 1)) await Task.Delay(1, bounded.Token);
        Assert.False(page.IsTemporary);
    }

    private static NewChatPage CreateCompactionPage(Rig rig, CompactionTestDatabase database, IOllamaClient client,
        IConversationRepository? readSource = null)
    {
        var local = new GenUiLocalActionRegistry(); var instances = new GenUiInstanceStore();
        var repository = readSource ?? database.Conversations;
        var sessions = new ChatSessionService(repository, rig.Client, new CapabilityPreflightService(), new Safety(),
            new WorkspaceToolRuntime(rig.Workspace), new ComputerToolRuntime(new Computer()), taskCoordinator: rig.Tasks,
            taskToolOwner: rig.ToolOwner, taskProviderContextCapture: rig.Capture, taskCloudPermissionRemediation: rig.Owner);
        return new(new HavenEventBus(), repository, client, sessions, new Safety(),
            new ConversationVersioningService(database.Conversations, database.Production), new UserPreferencesService(database),
            new GenerativeUiEventRouter([], new NativeComposerAudit(), instances), instances,
            new CalculatorTemplateRuntime(local, instances), new StructuredFormTemplateRuntime(local, instances),
            new ChoicePromptTemplateRuntime(local), new ChecklistTemplateRuntime(local, instances), new DataGridTemplateRuntime(local),
            new CardDeckTemplateRuntime(local, instances), new GraphTemplateRuntime(local), new TaskListTemplateRuntime(local),
            new DashboardTemplateRuntime(local), new AssessmentTemplateRuntime(local), new WorkflowTemplateRuntime(local),
            new CustomTemplateRuntime(local, instances), conversationProduction: database.Production, enableFreshChatTemporaryLifecycle: true);
    }

    private static int CountOccurrences(string? text, string value) =>
        (text ?? string.Empty).Split(value, StringSplitOptions.None).Length - 1;

    private static bool ContainsCompactionCause<T>(Exception? cause) where T : Exception => cause is T ||
        cause is AggregateException aggregate && aggregate.InnerExceptions.Any(ContainsCompactionCause<T>) ||
        cause?.InnerException is { } inner && ContainsCompactionCause<T>(inner);

    private static bool ContainsCompactionCause(Exception? cause, Exception expected) => ReferenceEquals(cause, expected) ||
        cause is AggregateException aggregate && aggregate.InnerExceptions.Any(item => ContainsCompactionCause(item, expected)) ||
        cause?.InnerException is { } inner && ContainsCompactionCause(inner, expected);

    private sealed class CompactionSpacePolicySource(SpaceRegistry registry) : IOriginalSpaceRequestPolicySource
    {
        internal const string Configuration = "REVIEWED_COMPACTION_SPACE_CONFIGURATION";
        internal int Validations;
        private sealed record Input(CompactionSpacePolicySource Owner, Guid ConversationId, OriginalSpacePolicySnapshot Snapshot) : IOriginalSpaceRequestPolicyInput;
        public async Task<IOriginalSpaceRequestPolicyInput> CaptureAsync(Conversation conversation, Action<Action> acquire, CancellationToken token)
        {
            var original = await new SpacePolicyResolver(registry).ResolveExistingAsync(conversation.SpaceId!.Value, acquire, _ => { }, token);
            return new Input(this, conversation.Id, new(original.Policy, original.Lineage,
                new OriginalSpaceCapturedContext(original.Policy.SpaceId, Configuration, [])));
        }
        public async Task<OriginalSpacePolicySnapshot> ReadAsync(IOriginalSpaceRequestPolicyInput input, Conversation conversation, Action<Action> acquire, CancellationToken token)
        {
            await ValidateAsync(input, conversation, acquire, token);
            return ((Input)input).Snapshot;
        }
        public async Task ValidateAsync(IOriginalSpaceRequestPolicyInput input, Conversation conversation, Action<Action> acquire, CancellationToken token)
        {
            Interlocked.Increment(ref Validations);
            if (input is not Input original || !ReferenceEquals(original.Owner, this) || original.ConversationId != conversation.Id ||
                original.Snapshot.Lineage.SpaceId != conversation.SpaceId)
                throw new InvalidOperationException("The original Space source changed.");
            var current = await new SpacePolicyResolver(registry).ResolveExistingAsync(conversation.SpaceId!.Value, acquire, _ => { }, token);
            if (!current.Lineage.Equals(original.Snapshot.Lineage)) throw new InvalidOperationException("The actual Space policy revision changed.");
        }
    }

    private sealed class CompactionSpaceClient(IOllamaClient inner) : IOriginalPrivateContextModelClient
    {
        private readonly CompactionLocalProvider _provider = new();
        public Task<bool> IsAvailableAsync(CancellationToken token) => inner.IsAvailableAsync(token);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) => inner.GetModelsAsync(token);
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => inner.CompleteAsync(request, token);
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new InvalidOperationException("No tool authority is supplied by this ordinary context fixture.");
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            await OriginalPrivateContextDispatch.ValidateProviderAsync(request.Options, _provider, token);
            await foreach (var chunk in inner.StreamChatAsync(request, token).WithCancellation(token)) yield return chunk;
        }
    }

    private sealed class CompactionLocalProvider : IOriginalPrivateContextModelProvider
    {
        public string Id => "controlled-local-compaction";
        public string DisplayName => Id;
        public ModelProviderKind Kind => ModelProviderKind.Ollama;
        public bool IsLocal => true;
        public bool CanManageModels => false;
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
    }

    public interface ICompactionReadRepository : IConversationRepository, IConversationPromptContextRepository,
        IConversationCompactionRepository, IConversationCreateOnlyRepository, ITemporaryChatSaveRepository, IConversationBranchBoundRepository { }

    private sealed class CompactionModelClient(Client inner) : IOllamaClient, IProviderModelClient
    {
        internal const string Summary = "RETAINED_COMPACTION_SUMMARY_ONCE: preserve the original named requirements.";
        internal List<OllamaChatRequest> CompactionRequests { get; } = [];
        internal Func<OllamaChatRequest, CancellationToken, Task<string>> Complete { get; init; } = (_, _) => Task.FromResult(Summary);
        public Task<bool> IsAvailableAsync(CancellationToken token) => inner.IsAvailableAsync(token);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) => inner.GetModelsAsync(token);
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => inner.StreamChatAsync(request, token);
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token)
        { CompactionRequests.Add(request); return Complete(request, token); }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => inner.ChatWithToolsAsync(request, token);
    }
}
