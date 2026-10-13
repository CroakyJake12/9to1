using System.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;

namespace Haven.Desktop.Tests;

/// <summary>
/// Operates the actual Chat page and scene with held local boundary acknowledgements.
/// These controls establish page state/custody only, not live-provider or product acceptance.
/// Every unexpected repository, versioning, tool or provider call fails the test.
/// </summary>
public sealed class TemporaryConversationPageTests
{
    [AvaloniaFact]
    public async Task Pending_save_keeps_original_history_and_temporary_state_until_acknowledgement()
    {
        await using var fixture = new Fixture();
        await fixture.LoadTemporaryAsync();
        var page = fixture.Page;
        var original = page.CurrentConversation;
        var reply = NewSource<Conversation>();
        fixture.Save = (_, _) => reply.Task;
        Assert.Throws<InvalidOperationException>(() => page.CaptureSavedConversationForTabDuplication());
        var save = page.SetTemporaryAsync(false);
        try
        {
            await fixture.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(save.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => page.CaptureSavedConversationForTabDuplication());
            Assert.True(page.IsTemporary);
            Assert.Equal(original, page.CurrentConversation);
            await page.AssignSpaceAsync(Guid.NewGuid());
            await page.TogglePinAsync();
            await page.UndoLatestAsync();
            await page.DeleteConversationAsync();
            await page.SubmitOriginalInstructionAsync("Do not execute during the held save");
            Assert.Equal(original, page.CurrentConversation);
            Assert.Equal(fixture.History.Select(message => message.Id), page.ProjectionState.Messages.Select(message => message.Id));
            Assert.Equal(0, fixture.OrdinaryWrites);
            Assert.Equal(0, fixture.ModelExecutions);
            reply.SetResult(original with { IsTemporary = false });
            await save;
            Assert.False(page.IsTemporary);
            Assert.Same(page.CurrentConversation, page.CaptureSavedConversationForTabDuplication());
            Assert.Equal(original.Id, page.ConversationId);
            Assert.Single(fixture.SaveAttempts);
            Assert.Equal(fixture.History, fixture.SaveAttempts[0].History);
        }
        finally { reply.TrySetResult(original with { IsTemporary = false }); await save; }
    }

    [AvaloniaFact]
    public async Task Pending_save_refuses_foreign_load_instead_of_acknowledging_unchanged_page()
    {
        await using var fixture = new Fixture();
        await fixture.LoadTemporaryAsync();
        var page = fixture.Page;
        var original = page.CurrentConversation;
        var acknowledgement = NewSource<Conversation>();
        fixture.Save = (_, _) => acknowledgement.Task;
        var save = page.SetTemporaryAsync(false);
        try
        {
            await fixture.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var differentSavedConversation = original with { Id = Guid.NewGuid(), IsTemporary = false };
            await Assert.ThrowsAsync<InvalidOperationException>(() => page.LoadConversationAsync(differentSavedConversation));
            Assert.Same(original, page.CurrentConversation);
            Assert.True(page.IsTemporary);
            Assert.Equal(fixture.History.Select(message => message.Id), page.ProjectionState.Messages.Select(message => message.Id));
            Assert.Equal(0, fixture.HistoryReads);
            Assert.Equal(0, fixture.OrdinaryWrites);
            acknowledgement.SetResult(original with { IsTemporary = false });
            await save;
            Assert.Equal(original.Id, page.ConversationId);
            Assert.False(page.IsTemporary);
        }
        finally
        {
            acknowledgement.TrySetResult(original with { IsTemporary = false });
            await save;
        }
    }

    [AvaloniaFact]
    public async Task Lost_save_acknowledgement_retains_exact_snapshot_and_refuses_mutation_until_retry()
    {
        await using var fixture = new Fixture();
        await fixture.LoadTemporaryAsync();
        var page = fixture.Page;
        var original = page.CurrentConversation;
        var lostAcknowledgement = new SaveAcknowledgementException();
        fixture.ExpectedCloseFailures.Add(lostAcknowledgement);
        fixture.Save = (conversation, _) =>
        {
            fixture.Stored = conversation with { IsTemporary = false };
            return Task.FromException<Conversation>(lostAcknowledgement);
        };

        await page.SetTemporaryAsync(false);
        Assert.True(page.IsTemporary);
        Assert.Equal(original, page.CurrentConversation);
        Assert.Contains("acknowledgement", page.ProjectionState.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() => page.CaptureSavedConversationForTabDuplication());
        await page.UndoLatestAsync();
        await page.AssignSpaceAsync(Guid.NewGuid());
        await page.DeleteConversationAsync();
        await page.SubmitOriginalInstructionAsync("Do not replay the original response");
        Assert.Equal(original, page.CurrentConversation);
        Assert.Equal(fixture.History.Select(message => message.Id), page.ProjectionState.Messages.Select(message => message.Id));
        Assert.Equal(0, fixture.OrdinaryWrites);
        Assert.Equal(0, fixture.ModelExecutions);

        fixture.Save = (_, _) => Task.FromResult(fixture.Stored!);
        await page.SetTemporaryAsync(false);
        Assert.False(page.IsTemporary);
        Assert.Equal(original.Id, page.ConversationId);
        Assert.Equal(2, fixture.SaveAttempts.Count);
        Assert.Same(fixture.SaveAttempts[0].Conversation, fixture.SaveAttempts[1].Conversation);
        Assert.Equal(fixture.SaveAttempts[0].History, fixture.SaveAttempts[1].History);
        Assert.Equal(0, fixture.OrdinaryWrites);
        Assert.Equal(0, fixture.ModelExecutions);
    }

    [AvaloniaFact]
    public async Task Admitted_send_in_safety_preflight_prevents_conversion_before_is_sending_becomes_true()
    {
        await using var fixture = new Fixture();
        await fixture.LoadTemporaryAsync();
        var page = fixture.Page;
        var original = page.CurrentConversation;
        var entered = NewSource();
        var release = NewSource();
        var refusalObserved = NewSource();
        var refused = new ConversationSafetyLockException(original.Id, "chat.send");
        fixture.ExpectedCloseFailures.Add(refused);
        fixture.SafetySnapshotObserved = () => refusalObserved.TrySetResult();
        fixture.EnsureSafety = (_, operation) =>
        {
            if (operation != "chat.send") return Task.CompletedTask;
            entered.TrySetResult();
            return release.Task;
        };
        try
        {
            await page.SubmitOriginalInstructionAsync("Held before any provider execution");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(page.IsSending);
            await page.SetTemporaryAsync(false);
            Assert.Empty(fixture.SaveAttempts);
            Assert.True(page.IsTemporary);
            Assert.Equal(original, page.CurrentConversation);
            Assert.Equal(0, fixture.ModelExecutions);
        }
        finally
        {
            release.TrySetException(refused);
            await refusalObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
    }

    [AvaloniaFact]
    public async Task Empty_saved_draft_creates_a_new_temporary_identity_without_downgrading_saved_source()
    {
        await using var fixture = new Fixture();
        await fixture.Page.RefreshModelsAsync();
        await fixture.DrainObservedPageWorkAsync();
        await fixture.Page.StartFreshConversationAsync();
        var savedOriginal = fixture.Stored!;
        var writes = fixture.OrdinaryWrites;
        await fixture.Page.SetTemporaryAsync(true);
        Assert.True(fixture.Page.IsTemporary);
        Assert.NotEqual(savedOriginal.Id, fixture.Page.ConversationId);
        Assert.Equal(savedOriginal, fixture.Stored);
        Assert.False(fixture.Stored!.IsTemporary);
        Assert.Equal(writes, fixture.OrdinaryWrites);
        Assert.Empty(fixture.SaveAttempts);
        Assert.Equal(0, fixture.ModelExecutions);
    }

    [AvaloniaFact]
    public async Task Rapid_saved_draft_to_temporary_transition_does_not_poison_page_retirement()
    {
        await using var fixture = new Fixture();
        await fixture.Page.RefreshModelsAsync();
        await fixture.DrainObservedPageWorkAsync();
        await fixture.Page.StartFreshConversationAsync();
        var saved = fixture.Page.CurrentConversation;
        // Deliberately do not settle the old presentation's queued cosmetic scroll.
        // A real user can choose Temporary immediately after creating an empty chat.
        await fixture.Page.SetTemporaryAsync(true);
        Assert.True(fixture.Page.IsTemporary);
        Assert.NotEqual(saved.Id, fixture.Page.ConversationId);
        Assert.Equal(saved, fixture.Stored);
        // Old cosmetic publication must settle without effect, not poison later close.
        await fixture.DrainObservedPageWorkAsync();
    }

    [AvaloniaFact]
    public async Task Temporary_compaction_transfers_its_actual_summary_with_history_only_on_explicit_save()
    {
        await using var fixture = new Fixture();
        await fixture.LoadTemporaryAsync(messageCount: 10);
        fixture.CompactionSummary = "Original requirements and decisions retained for the next turn.";
        await fixture.Page.CompactContextAsync();
        Assert.True(fixture.Page.IsTemporary);
        Assert.Equal(1, fixture.ModelExecutions);
        Assert.Equal(0, fixture.OrdinaryWrites);
        Assert.Empty(fixture.SaveAttempts);

        await fixture.Page.SetTemporaryAsync(false);
        var save = Assert.Single(fixture.SaveAttempts);
        var summary = Assert.Single(save.Context);
        Assert.Equal(ContextEntryKind.CompactSummary, summary.Kind);
        Assert.Equal(fixture.CompactionSummary, summary.Content);
        Assert.Equal(fixture.Page.ConversationId, summary.ConversationId);
        Assert.Equal(4, save.History.Count(message => message.IsCompacted));
        Assert.Equal(fixture.History.Select(message => message.Id), save.History.Select(message => message.Id));
        Assert.False(fixture.Page.IsTemporary);
        Assert.Equal(1, fixture.ModelExecutions); // Saving must not ask the model again.
        Assert.Equal(0, fixture.OrdinaryWrites);
    }

    [AvaloniaFact]
    public async Task Fresh_temporary_identity_cannot_redo_messages_from_the_previous_saved_identity()
    {
        await using var fixture = new Fixture();
        await fixture.LoadTemporaryAsync(messageCount: 1, temporary: false);
        var oldId = fixture.Page.ConversationId;
        await fixture.Page.UndoLatestAsync();
        Assert.Empty(fixture.Page.ProjectionState.Messages);
        await fixture.Page.SetTemporaryAsync(true);
        Assert.True(fixture.Page.IsTemporary);
        Assert.NotEqual(oldId, fixture.Page.ConversationId);
        await fixture.Page.RedoLatestAsync();
        Assert.Empty(fixture.Page.ProjectionState.Messages);
        await fixture.Page.SetTemporaryAsync(false);
        var save = Assert.Single(fixture.SaveAttempts);
        Assert.Empty(save.History);
        Assert.Equal(fixture.Page.ConversationId, save.Conversation.Id);
    }

    [AvaloniaFact]
    public async Task Closing_page_joins_the_original_save_and_refuses_late_saved_state_publication()
    {
        await using var fixture = new Fixture();
        await fixture.LoadTemporaryAsync();
        var original = fixture.Page.CurrentConversation;
        var acknowledgement = NewSource<Conversation>();
        fixture.Save = (_, _) => acknowledgement.Task;
        var save = fixture.Page.SetTemporaryAsync(false);
        await fixture.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Page.RequestRetirement();
        var close = fixture.Page.CloseAndDrainAsync();
        try { Assert.False(close.IsCompleted); }
        finally { acknowledgement.TrySetResult(original with { IsTemporary = false }); }
        var failedPublication = await Record.ExceptionAsync(() => save);
        Assert.NotNull(failedPublication);
        var causes = failedPublication is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions.ToArray() : [failedPublication];
        Assert.All(causes, cause => Assert.IsAssignableFrom<OperationCanceledException>(cause));
        fixture.ExpectedCloseFailures.AddRange(causes);
        Assert.True(fixture.Page.IsTemporary);
        Assert.Equal(original, fixture.Page.CurrentConversation);
        Assert.Equal(0, fixture.ModelExecutions);
        Assert.Equal(0, fixture.OrdinaryWrites);
        Assert.Single(fixture.SaveAttempts);
        // DisposeAsync joins this same close and checks that it retains these exact causes.
    }

    [AvaloniaFact]
    public async Task Foreign_temporary_descriptor_cannot_hydrate_durable_history()
    {
        await using var fixture = new Fixture();
        await fixture.Page.RefreshModelsAsync();
        await fixture.DrainObservedPageWorkAsync();
        var current = fixture.Page.CurrentConversation;
        var now = DateTimeOffset.UtcNow;
        var foreign = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
            "Foreign descriptor", null, null, false, true, now, now);
        var beforeReads = fixture.HistoryReads;
        var failure = await Record.ExceptionAsync(() => fixture.Page.LoadConversationAsync(foreign));
        if (failure is not null)
        {
            var causes = failure is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.ToArray() : [failure];
            Assert.All(causes, cause => Assert.IsAssignableFrom<InvalidOperationException>(cause));
            fixture.ExpectedCloseFailures.AddRange(causes);
        }
        Assert.Equal(current, fixture.Page.CurrentConversation);
        Assert.Equal(beforeReads, fixture.HistoryReads);
        Assert.Empty(fixture.Page.ProjectionState.Messages);
    }

    [AvaloniaFact]
    public async Task Changed_older_messages_reject_a_late_compaction_summary_without_losing_current_history()
    {
        await using var fixture = new Fixture();
        await fixture.LoadTemporaryAsync(messageCount: 10);
        var entered = NewSource();
        var response = NewSource<string>();
        fixture.Compaction = () => { entered.TrySetResult(); return response.Task; };
        var compact = fixture.Page.CompactContextAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Six newest messages were outside the submitted summary; the seventh
            // undo removes one of the four actual source messages being summarised.
            for (var index = 0; index < 7; index++) await fixture.Page.UndoLatestAsync();
            response.SetResult("Late summary containing a message the user removed.");
            await compact;
            Assert.Contains("changed during compaction", fixture.Page.ProjectionState.StatusText, StringComparison.OrdinalIgnoreCase);
            await fixture.Page.SetTemporaryAsync(false);
            var save = Assert.Single(fixture.SaveAttempts);
            Assert.Empty(save.Context);
            Assert.Equal(fixture.History.Take(3), save.History);
            Assert.All(save.History, message => Assert.False(message.IsCompacted));
            Assert.Equal(1, fixture.ModelExecutions);
            Assert.Equal(0, fixture.OrdinaryWrites);
        }
        finally { response.TrySetResult("Released held test summary."); await compact; }
    }

    private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource<T> NewSource<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public interface IPageConversationProbe : IConversationRepository, ITemporaryConversationSaveRepository { }

    // DispatchProxy keeps unexercised ports explicitly unavailable instead of granting
    // fake permissions, executing replacement tools or fabricating provider results.
    public class BoundaryProbe : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = (method, _) => throw new InvalidOperationException("Unexpected boundary: " + method.Name);
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args ?? []);
    }

    private static T Probe<T>(Func<MethodInfo, object?[], object?>? handler = null) where T : class
    {
        var value = DispatchProxy.Create<T, BoundaryProbe>();
        if (handler is not null) ((BoundaryProbe)(object)value).Handler = handler;
        return value;
    }

    private sealed class SaveAcknowledgementException() : DbException("The committed save acknowledgement was lost.");

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "9to1-temporary-page", Guid.NewGuid().ToString("N"));
        private readonly HavenEventBus _bus = new();
        private bool _seeding;
        public NewChatPage Page { get; }
        public Conversation? Stored { get; set; }
        public ChatMessage[] History { get; private set; } = [];
        public int OrdinaryWrites { get; private set; }
        public int ModelExecutions { get; private set; }
        public int HistoryReads { get; private set; }
        public string? CompactionSummary { get; set; }
        public Func<Task<string>>? Compaction { get; set; }
        public Action? SafetySnapshotObserved { get; set; }
        public List<(Conversation Conversation, ChatMessage[] History, ConversationContextEntry[] Context)> SaveAttempts { get; } = [];
        public List<Exception> ExpectedCloseFailures { get; } = [];
        public TaskCompletionSource SaveEntered { get; } = NewSource();
        public Func<Conversation, IReadOnlyList<ChatMessage>, Task<Conversation>> Save { get; set; } =
            (conversation, _) => Task.FromResult(conversation with { IsTemporary = false });
        public Func<Guid, string, Task> EnsureSafety { get; set; } = (_, _) => Task.CompletedTask;

        public Fixture()
        {
            Directory.CreateDirectory(_root);
            var repository = Probe<IPageConversationProbe>((method, args) =>
            {
                switch (method.Name)
                {
                    case nameof(IConversationRepository.GetAsync): return Task.FromResult(Stored?.Id == (Guid)args[0]! ? Stored : null);
                    case nameof(IConversationRepository.GetMessagesAsync):
                    case nameof(IConversationRepository.GetContextMessagesAsync): HistoryReads++; return Task.FromResult<IReadOnlyList<ChatMessage>>(History);
                    case nameof(IConversationRepository.GetContextEntriesAsync): return Task.FromResult<IReadOnlyList<ConversationContextEntry>>([]);
                    case nameof(IConversationRepository.UpsertConversationAsync): OrdinaryWrites++; Stored = (Conversation)args[0]!; return Task.CompletedTask;
                    case nameof(IConversationRepository.DeleteMessageAsync):
                        OrdinaryWrites++;
                        History = History.Where(message => message.Id != (Guid)args[1]!).ToArray();
                        return Task.CompletedTask;
                    case nameof(ITemporaryConversationSaveRepository.SaveTemporaryLocallyAsync):
                        var conversation = (Conversation)args[0]!;
                        var history = (IReadOnlyList<ChatMessage>)args[1]!;
                        var context = args.Length == 4 ? (IReadOnlyList<ConversationContextEntry>)args[2]! : [];
                        SaveAttempts.Add((conversation, history.ToArray(), context.ToArray()));
                        SaveEntered.TrySetResult();
                        return Save(conversation, history);
                    default: throw new InvalidOperationException("Unexpected conversation boundary: " + method.Name);
                }
            });
            var model = new ModelDescriptor("local-page-probe", 1, "test", "test", "test", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow);
            var client = Probe<IOllamaClient>((method, _) =>
            {
                if (method.Name == nameof(IOllamaClient.GetModelsAsync)) return Task.FromResult<IReadOnlyList<ModelDescriptor>>([model]);
                if (method.Name == "get_TransportEndpoint") return new Uri("http://127.0.0.1:11434");
                if (method.Name == "get_IsDeviceLocalTransportVerified") return true;
                if (method.Name == nameof(IOllamaClient.IsAvailableAsync)) return Task.FromResult(true);
                ModelExecutions++;
                if (_seeding && method.Name == nameof(IOllamaClient.StreamChatAsync)) return SeedResponse();
                if (method.Name == nameof(IOllamaClient.CompleteAsync) && Compaction is not null) return Compaction();
                if (method.Name == nameof(IOllamaClient.CompleteAsync) && CompactionSummary is not null)
                    return Task.FromResult(CompactionSummary);
                throw new InvalidOperationException("This page-state test must not execute a model: " + method.Name);
            });
            var safety = Probe<IConversationSafetyService>((method, args) =>
            {
                if (method.Name == nameof(IConversationSafetyService.EnsureMayActAsync))
                    return EnsureSafety((Guid)args[0]!, (string)args[1]!);
                if (method.Name == nameof(IConversationSafetyService.GetSnapshotAsync))
                {
                    SafetySnapshotObserved?.Invoke();
                    return Task.FromResult(new ConversationSafetySnapshot((Guid)args[0]!, 0, ConversationSafetyState.Active, null, 0));
                }
                throw new InvalidOperationException("Unexpected safety boundary: " + method.Name);
            });
            var sessions = new ChatSessionService(repository, client, new CapabilityPreflightService(), safety,
                new WorkspaceToolRuntime(Probe<IWorkspaceToolService>()), new ComputerToolRuntime(Probe<IComputerToolService>((method, _) =>
                    method.Name == "get_IsSupported" ? false : throw new InvalidOperationException("Unexpected computer boundary: " + method.Name))));
            var instances = new GenUiInstanceStore();
            var localActions = new GenUiLocalActionRegistry();
            var router = new GenerativeUiEventRouter([localActions], Probe<IGenUiEventAuditSink>(), instances);
            var paths = Probe<IAppPaths>((method, _) => method.Name == "get_DataDirectory" ? _root : Path.Combine(_root, method.Name));
            Page = new NewChatPage(_bus, repository, client, sessions, safety, Probe<IConversationVersioningService>(),
                new UserPreferencesService(paths), router, instances,
                new CalculatorTemplateRuntime(localActions, instances), new StructuredFormTemplateRuntime(localActions, instances),
                new ChoicePromptTemplateRuntime(localActions), new ChecklistTemplateRuntime(localActions, instances),
                new DataGridTemplateRuntime(localActions), new CardDeckTemplateRuntime(localActions, instances),
                new GraphTemplateRuntime(localActions), new TaskListTemplateRuntime(localActions),
                new DashboardTemplateRuntime(localActions), new AssessmentTemplateRuntime(localActions),
                new WorkflowTemplateRuntime(localActions), new CustomTemplateRuntime(localActions, instances));
        }

        public async Task LoadTemporaryAsync(int messageCount = 2, bool temporary = true)
        {
            await Page.RefreshModelsAsync();
            await DrainObservedPageWorkAsync();
            if (temporary)
            {
                Assert.Equal(0, messageCount % 2);
                await Page.SetTemporaryAsync(true);
                Assert.True(Page.IsTemporary);
                _seeding = true;
                try
                {
                    for (var turn = 0; turn < messageCount / 2; turn++)
                    {
                        await Page.SubmitOriginalInstructionAsync("Fixture original question " + turn);
                        // Observe the SAME admitted send task; no replacement send is created.
                        // There is no public awaitable send-completion port on the page.
                        var actualSend = Assert.IsAssignableFrom<Task>(typeof(NewChatPage)
                            .GetField("_originalSend", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Page));
                        await actualSend.WaitAsync(TimeSpan.FromSeconds(5));
                        await DrainObservedPageWorkAsync();
                        Assert.Equal((turn + 1) * 2, Page.ProjectionState.Messages.Length);
                        Assert.Equal("Fixture original response", Page.ProjectionState.Messages[^1].Content);
                    }
                }
                finally { _seeding = false; }
                // Read actual source-created message values for preservation assertions;
                // never inject messages or forge an ephemeral descriptor into the page.
                History = Assert.IsType<List<ChatMessage>>(typeof(NewChatPage)
                    .GetField("_messages", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Page)).ToArray();
                Assert.Equal(0, OrdinaryWrites);
                Assert.Equal(0, HistoryReads);
                ModelExecutions = 0;
                return;
            }
            var now = DateTimeOffset.UtcNow;
            var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
                "Original temporary chat", null, null, false, temporary, now, now, SpaceId: SpaceRegistry.ChatSpaceId);
            History = Enumerable.Range(0, messageCount).Select(index => new ChatMessage(Guid.NewGuid(), conversation.Id,
                index % 2 == 0 ? MessageRole.User : MessageRole.Assistant, "Original message " + index,
                null, index % 2 == 0 ? null : "local-page-probe", null, now.AddSeconds(index))).ToArray();
            if (!temporary) Stored = conversation;
            await Page.LoadConversationAsync(conversation);
            await DrainObservedPageWorkAsync();
        }

        private static async IAsyncEnumerable<string> SeedResponse([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield return "Fixture original response";
        }

        public async Task DrainObservedPageWorkAsync()
        {
            // Observe actual admitted tasks, including constructor/scroll/send-terminal
            // dispatcher publications. Closing immediately after a public operation may
            // legitimately cancel those queued publications; it is not a fixture failure.
            // Drain them without cancellation and without inventing a replacement task.
            var owner = Assert.IsAssignableFrom<DesktopOriginalWorkLifetime>(typeof(NewChatPage)
                .GetField("_originalWork", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Page));
            if (owner.IsRetiring) return;
            var gate = typeof(DesktopOriginalWorkLifetime).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
            var originals = Assert.IsType<List<DesktopOriginalWorkLifetime.Original>>(typeof(DesktopOriginalWorkLifetime)
                .GetField("_originals", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner));
            for (var round = 0; round < 64; round++)
            {
                Task[] pending;
                lock (gate) pending = originals.Select(original => original.Task).Where(task => !task.IsCompleted).ToArray();
                if (pending.Length == 0) return;
                foreach (var actual in pending)
                {
                    try { await actual.WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (Exception error)
                    {
                        var causes = error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.ToArray() : [error];
                        Assert.All(causes, cause => Assert.Contains(cause, ExpectedCloseFailures));
                    }
                }
            }
            Assert.Fail("The actual page work did not settle within its bounded observation rounds.");
        }

        public async ValueTask DisposeAsync()
        {
            await DrainObservedPageWorkAsync();
            try { await Page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception error)
            {
                var causes = error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.ToArray() : [error];
                Assert.All(causes, cause => Assert.Contains(cause, ExpectedCloseFailures));
            }
            finally { _bus.Dispose(); Directory.Delete(_root, recursive: true); }
        }
    }
}
