using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using Xunit;

namespace HavenOS.Apps.Assistants.Tests;

/// <summary>Runs SAME actual Chat/host with controlled raw provider/writer Tasks.
/// No definition authority, installed app, model transport or tool parity is claimed.</summary>
public sealed class AssistantOrdinaryOriginalCloseTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public Task App_owned_actual_canceled_provider_joins_held_cleanup_then_closes_cleanly() => RunAsync(async rig =>
    {
        rig.Start();
        await rig.Provider.MoveStarted.Task.WaitAsync(TestToken);
        Assert.Equal(1, rig.Repository.ConversationWrites);
        Assert.Equal(1, rig.Repository.UserWrites);
        rig.Business.Cancel();
        rig.Provider.Move.SetCanceled(rig.Business.Token);
        await rig.Provider.DisposeStarted.Task.WaitAsync(TestToken);
        var close = rig.Host.CloseAndDrainAsync();
        Assert.Same(close, rig.Host.CloseAndDrainAsync());
        Assert.False(close.IsCompleted);
        rig.Provider.Dispose.SetResult();
        await close;
        Assert.True(rig.Provider.Move.Task.IsCanceled);
        Assert.True(rig.Provider.Dispose.Task.IsCompletedSuccessfully);
        Assert.Equal(0, rig.Repository.AssistantWrites);
        var result = await rig.Observation!.WaitAsync(TestToken);
        Assert.Equal(rig.Conversation.Id, result.ConversationId);
    });

    [Fact]
    public Task Faulted_provider_oce_with_same_requested_token_remains_an_unknown_failure() => RunAsync(async rig =>
    {
        rig.Start(); await rig.Provider.MoveStarted.Task.WaitAsync(TestToken);
        rig.Business.Cancel();
        var actual = new OperationCanceledException("Faulted source, never TaskCanceled.", rig.Business.Token);
        rig.Provider.Move.SetException(actual);
        rig.Provider.Dispose.SetResult();
        await rig.ExpectFailureAsync(actual);
        Assert.True(rig.Provider.Move.Task.IsFaulted);
        Assert.False(rig.Provider.Move.Task.IsCanceled);
    });

    [Fact]
    public Task Mixed_faulted_oce_and_io_siblings_are_both_retained() => RunAsync(async rig =>
    {
        rig.Start(); await rig.Provider.MoveStarted.Task.WaitAsync(TestToken);
        rig.Business.Cancel();
        var cancellation = new OperationCanceledException("Actual faulted cancellation cause", rig.Business.Token);
        var io = new IOException("Actual independent provider sibling");
        rig.Provider.Move.SetException([cancellation, io]);
        rig.Provider.Dispose.SetResult();
        await rig.ExpectFailureAsync(cancellation, io);
    });

    [Fact]
    public Task Foreign_empty_aggregate_is_retained_with_its_original_identity() => RunAsync(async rig =>
    {
        rig.Start(); await rig.Provider.MoveStarted.Task.WaitAsync(TestToken);
        var opaque = new AggregateException("Foreign empty original");
        rig.Provider.Move.SetException(opaque);
        rig.Provider.Dispose.SetResult();
        await rig.ExpectFailureAsync(opaque);
    });

    [Fact]
    public Task Canceled_accepted_conversation_writer_never_becomes_a_clean_business_stop() => RunAsync(async rig =>
    {
        rig.Repository.HoldConversationWrite = true;
        rig.Start(); await rig.Repository.ConversationWriteStarted.Task.WaitAsync(TestToken);
        rig.Business.Cancel(); rig.Repository.ConversationWrite.SetCanceled(rig.Business.Token);
        var failure = await rig.ExpectFailureAsync();
        Assert.True(ContainsCancellation(failure));
        Assert.True(rig.Repository.ConversationWrite.Task.IsCanceled);
        Assert.Equal(0, rig.Repository.UserWrites);
        Assert.False(rig.Provider.MoveStarted.Task.IsCompleted);
    });

    [Fact]
    public Task Canceled_final_assistant_writer_is_retained_after_actual_provider_success() => RunAsync(async rig =>
    {
        rig.Repository.HoldAssistantWrite = true;
        rig.Provider.HealthyChunks = 1;
        rig.Start(); await rig.Repository.AssistantWriteStarted.Task.WaitAsync(TestToken);
        rig.Business.Cancel(); rig.Repository.AssistantWrite.SetCanceled(rig.Business.Token);
        var failure = await rig.ExpectFailureAsync();
        Assert.True(ContainsCancellation(failure));
        Assert.True(rig.Repository.AssistantWrite.Task.IsCanceled);
        Assert.Equal(1, rig.Repository.UserWrites);
        Assert.True(rig.Provider.Dispose.Task.IsCompletedSuccessfully);
    });

    [Fact]
    public Task Canceled_provider_from_a_different_token_is_not_the_owned_app_stop() => RunAsync(async rig =>
    {
        using var foreign = new CancellationTokenSource(); foreign.Cancel();
        rig.Start(); await rig.Provider.MoveStarted.Task.WaitAsync(TestToken);
        rig.Business.Cancel(); rig.Provider.Move.SetCanceled(foreign.Token);
        rig.Provider.Dispose.SetResult();
        var failure = await rig.ExpectFailureAsync();
        Assert.True(ContainsCancellation(failure));
    });

    [Fact]
    public Task Presentation_detach_does_not_cancel_borrowed_business_producer() => RunAsync(async rig =>
    {
        rig.Start(); await rig.Provider.MoveStarted.Task.WaitAsync(TestToken);
        var observationClose = rig.Observation!.CloseAndDrainAsync(); await observationClose;
        Assert.Same(observationClose, rig.Observation.CloseAndDrainAsync());
        Assert.False(rig.Business.IsCancellationRequested);
        Assert.False(rig.Provider.Move.Task.IsCompleted);
        rig.Business.Cancel(); rig.Provider.Move.SetCanceled(rig.Business.Token); rig.Provider.Dispose.SetResult();
        await rig.Host.CloseAndDrainAsync();
    });

    [Fact]
    public Task Suppressed_context_actual_provider_factory_cannot_join_own_host() => RunAsync(async rig =>
    {
        var guarded = false;
        rig.Provider.OnFactory = () =>
        {
            using (ExecutionContext.SuppressFlow())
                Assert.Throws<InvalidOperationException>(() => { _ = rig.Host.CloseAndDrainAsync(); });
            guarded = true;
        };
        rig.Start(); await rig.Provider.MoveStarted.Task.WaitAsync(TestToken);
        Assert.True(guarded);
        rig.Business.Cancel(); rig.Provider.Move.SetCanceled(rig.Business.Token); rig.Provider.Dispose.SetResult();
        await rig.Host.CloseAndDrainAsync();
    });

    [Fact]
    public Task Long_stream_releases_joined_healthy_originals_without_exhausting_source_bound() => RunAsync(async rig =>
    {
        rig.Provider.HealthyChunks = 400;
        rig.Start();
        await rig.Host.CloseAndDrainAsync();
        Assert.Equal(400, rig.Provider.EmittedChunks);
        Assert.Equal(1, rig.Repository.AssistantWrites);
        Assert.Equal(400, rig.Repository.Messages.Single(row => row.Role == MessageRole.Assistant).Content.Length);
    });

    [Fact]
    public Task Same_exception_alias_on_actual_canceled_move_and_faulted_cleanup_stays_failed() => RunAsync(async rig =>
    {
        var trigger = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sameCause = new TaskCanceledException("SAME original exception alias", null, rig.Business.Token);
        var actualCanceledMove = ThrowOwnedCancellationAsync(trigger.Task, sameCause);
        rig.Provider.IssuedMoveOverride = actualCanceledMove;
        rig.Start(); await rig.Provider.MoveStarted.Task.WaitAsync(TestToken);
        rig.Business.Cancel(); trigger.SetResult();
        await rig.Provider.DisposeStarted.Task.WaitAsync(TestToken);
        rig.Provider.Dispose.SetException(sameCause);
        await rig.ExpectFailureAsync(sameCause);
        Assert.True(actualCanceledMove.IsCanceled);
        Assert.Same(sameCause, await Assert.ThrowsAsync<TaskCanceledException>(() => actualCanceledMove));
        Assert.True(rig.Provider.Dispose.Task.IsFaulted);
        Assert.Same(sameCause, Assert.Single(rig.Provider.Dispose.Task.Exception!.InnerExceptions));
    });

    [Fact]
    public Task Post_callback_scope_fault_retains_accepted_writer_until_its_actual_join() => RunAsync(async rig =>
    {
        rig.Repository.HoldConversationWrite = true;
        var scopeFault = new IOException("Actual source scope post-callback refusal");
        var writerFault = new IOException("Actual accepted writer independent fault");
        var scopeFailed = false;
        var source = rig.Chat.CreateOriginalOrdinaryConversation(rig.Conversation, "Hello", rig.Model, EffortLevel.Medium,
            "Controlled", "", new GenerationOptions { RequestedRoutingConstraints = new(false, false), RequestedContextConstraints = new(false) },
            rig.Business.Token, callback =>
            {
                callback();
                if (rig.Repository.ConversationWriteStarted.Task.IsCompleted && !scopeFailed)
                { scopeFailed = true; throw scopeFault; }
            });
        var iterator = source.ConsumeOriginal().GetAsyncEnumerator(rig.Business.Token);
        Task? joined = null;
        var expected = false;
        try
        {
            Assert.True(await iterator.MoveNextAsync().AsTask());
            var move = iterator.MoveNextAsync().AsTask();
            Assert.Same(scopeFault, await Assert.ThrowsAsync<IOException>(() => move));
            await iterator.DisposeAsync();
            joined = source.JoinOriginalSourcesAsync();
            Assert.False(joined.IsCompleted);
            rig.Repository.ConversationWrite.SetException(writerFault);
            var failure = await Assert.ThrowsAsync<AggregateException>(() => joined);
            Assert.True(Contains(failure, scopeFault)); Assert.True(Contains(failure, writerFault));
            Assert.False(rig.Provider.MoveStarted.Task.IsCompleted);
            expected = true;
        }
        finally
        {
            rig.Repository.ConversationWrite.TrySetResult();
            foreach (var actual in source.ReadOriginalBodyMoves())
                try { await actual; } catch { /* SAME source ledger below retains the actual causes. */ }
            await iterator.DisposeAsync();
            joined ??= source.JoinOriginalSourcesAsync();
            try { await joined; } catch when (expected) { }
        }
    });

    [Fact]
    public Task Failed_provider_dispose_retains_same_issued_source_and_iterator_for_inspection() => RunAsync(async rig =>
    {
        rig.Start(); await rig.Provider.MoveStarted.Task.WaitAsync(TestToken);
        rig.Business.Cancel(); rig.Provider.Move.SetCanceled(rig.Business.Token);
        var io = new IOException("Actual failed provider cleanup; owning objects must survive.");
        rig.Provider.Dispose.SetException(io);
        await rig.ExpectFailureAsync(io);
        var original = Assert.Single(rig.Host.ReadRetainedOriginalInvocations());
        Assert.True(rig.Chat.IsIssuedOriginalOrdinaryConversation(original.Source));
        Assert.Same(original.Iterator, original.Source.OriginalBodyIterator);
        Assert.NotNull(original.SourceJoin); Assert.True(original.SourceJoin.IsFaulted);
        Assert.Same(original.Source, Assert.Single(rig.Host.ReadRetainedOriginalInvocations()).Source);
        Assert.Equal(1, rig.Provider.DisposeCalls);
    });

    [Fact]
    public Task Post_callback_cleanup_refusal_keeps_same_held_raw_dispose_and_never_retries_it() => RunAsync(async rig =>
    {
        var scopeFault = new IOException("Actual cleanup post-callback scope fault");
        var cleanupFault = new IOException("Actual independent accepted cleanup fault");
        var scopeFailed = false;
        var source = rig.Chat.CreateOriginalOrdinaryConversation(rig.Conversation, "Hello", rig.Model, EffortLevel.Medium,
            "Controlled", "", new GenerationOptions { RequestedRoutingConstraints = new(false, false), RequestedContextConstraints = new(false) },
            rig.Business.Token, callback =>
            {
                callback();
                if (rig.Provider.DisposeStarted.Task.IsCompleted && !scopeFailed)
                { scopeFailed = true; throw scopeFault; }
            });
        var iterator = source.ConsumeOriginal().GetAsyncEnumerator(rig.Business.Token);
        Task? joined = null; var expected = false;
        try
        {
            Assert.True(await iterator.MoveNextAsync().AsTask());
            Assert.Equal(ChatStreamEventKind.UserMessage, iterator.Current.Kind);
            Assert.True(await iterator.MoveNextAsync().AsTask());
            Assert.Equal(ChatStreamEventKind.AssistantStarted, iterator.Current.Kind);
            // The SAME third move enters provider dispatch. Race its actual terminal
            // against the fixture gate so an early source failure cannot hide in a wait.
            var actualBodyMove = iterator.MoveNextAsync().AsTask();
            await Task.WhenAny(rig.Provider.MoveStarted.Task, actualBodyMove).WaitAsync(TestToken);
            if (actualBodyMove.IsCompleted)
            {
                var unexpectedNext = await actualBodyMove;
                throw new InvalidOperationException($"Original Chat returned {unexpectedNext} before the held provider move.");
            }
            await rig.Provider.MoveStarted.Task.WaitAsync(TestToken);
            rig.Business.Cancel(); rig.Provider.Move.SetCanceled(rig.Business.Token);
            var bodyFailure = await Assert.ThrowsAsync<IOException>(() => actualBodyMove);
            Assert.Same(scopeFault, bodyFailure);
            var actualDispose = iterator.DisposeAsync().AsTask(); await actualDispose;
            Assert.Same(actualDispose, iterator.DisposeAsync().AsTask());
            joined = source.JoinOriginalSourcesAsync();
            Assert.False(joined.IsCompleted); Assert.Equal(1, rig.Provider.DisposeCalls);
            rig.Provider.Dispose.SetException(cleanupFault);
            var failure = await Assert.ThrowsAsync<AggregateException>(() => joined);
            Assert.True(Contains(failure, scopeFault)); Assert.True(Contains(failure, cleanupFault));
            Assert.Equal(1, rig.Provider.DisposeCalls); expected = true;
        }
        finally
        {
            rig.Provider.Move.TrySetCanceled(rig.Business.Token); rig.Provider.Dispose.TrySetResult();
            foreach (var actual in source.ReadOriginalBodyMoves())
                try { await actual; } catch { /* Final ledger preserves these actual failures. */ }
            await iterator.DisposeAsync(); joined ??= source.JoinOriginalSourcesAsync();
            try { await joined; } catch when (expected) { }
        }
    });

    private static async Task<bool> ThrowOwnedCancellationAsync(Task trigger, OperationCanceledException sameCause)
    { await trigger.ConfigureAwait(false); throw sameCause; }

    private static bool Contains(Exception actual, Exception expected) => ReferenceEquals(actual, expected) ||
        actual is AggregateException group && group.InnerExceptions.Any(child => Contains(child, expected));
    private static bool ContainsCancellation(Exception actual) => actual is OperationCanceledException ||
        actual is AggregateException group && group.InnerExceptions.Any(ContainsCancellation);
    private static async Task RunAsync(Func<Rig, Task> body)
    {
        var rig = new Rig(); var failures = new List<Exception>();
        try { await body(rig); } catch (Exception cause) { failures.Add(cause); }
        finally
        {
            rig.Business.Cancel();
            rig.Provider.Move.TrySetCanceled(rig.Business.Token);
            rig.Provider.Dispose.TrySetResult();
            rig.Repository.ConversationWrite.TrySetResult(); rig.Repository.AssistantWrite.TrySetResult();
            try { await rig.Host.CloseAndDrainAsync(); }
            catch (Exception cause) { if (!rig.ExpectedCloseFailure) failures.Add(cause); }
            if (rig.Observation is { } observation)
                try { await observation.CloseAndDrainAsync(); }
                catch (Exception cause) { if (!rig.ExpectedCloseFailure) failures.Add(cause); }
            rig.Business.Dispose();
        }
        if (failures.Count != 0) throw new AggregateException("Original control and cleanup failed.", failures);
    }

    private sealed class Rig
    {
        internal readonly CancellationTokenSource Business = new();
        internal readonly Provider Provider = new();
        internal readonly Repository Repository = new();
        internal readonly AssistantOriginalConversationHost Host;
        internal readonly Conversation Conversation;
        private readonly ChatSessionService _chat;
        private readonly ModelDescriptor _model;
        internal AssistantOriginalConversationObservation? Observation;
        internal bool ExpectedCloseFailure;
        internal ChatSessionService Chat => _chat;
        internal ModelDescriptor Model => _model;
        internal Rig()
        {
            Host = new(Business.Token);
            var now = DateTimeOffset.UtcNow;
            Conversation = new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Actual controlled ordinary conversation", null, null, false, false, now, now);
            _model = new("controlled", 1, "controlled", "controlled", "controlled", new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Streaming }, now);
            _chat = new(Repository, Provider, new CapabilityPreflightService(), new Safety(),
                new WorkspaceToolRuntime(new Workspace()), new ComputerToolRuntime(new Computer()));
        }
        internal void Start()
        {
            var definition = new AssistantDefinitionSnapshot(new("controlled-not-an-authority-grant", "personal", "controlled"), 1,
                ConfiguredIdentityKind.Assistant, new() { Name = "Controlled" }, []);
            var binding = new AssistantConversationBinding(new object(), definition, "controlled", 1, Conversation);
            Observation = Host.StartOriginal(binding, _chat, Conversation, "Hello", _model, EffortLevel.Medium, "Controlled", "",
                new GenerationOptions { RequestedRoutingConstraints = new(false, false), RequestedContextConstraints = new(false) });
        }
        internal async Task<AggregateException> ExpectFailureAsync(params Exception[] actualCauses)
        {
            var rawClose = Host.CloseAndDrainAsync();
            var failure = await Assert.ThrowsAsync<AggregateException>(() => rawClose);
            foreach (var original in actualCauses) Assert.True(Contains(failure, original));
            ExpectedCloseFailure = true;
            Assert.Same(rawClose, Host.CloseAndDrainAsync());
            return failure;
        }
    }

    private sealed class Repository : IConversationRepository
    {
        internal readonly TaskCompletionSource ConversationWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ConversationWriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource AssistantWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource AssistantWriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<ChatMessage> Messages = [];
        internal bool HoldConversationWrite, HoldAssistantWrite;
        internal int ConversationWrites, UserWrites, AssistantWrites;
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<Conversation>>([]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token) => Task.FromResult<Conversation?>(null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) => Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.ToArray());
        public Task UpsertConversationAsync(Conversation row, CancellationToken token)
        { ConversationWrites++; ConversationWriteStarted.TrySetResult(); return HoldConversationWrite ? ConversationWrite.Task : Task.CompletedTask; }
        public Task AddMessageAsync(ChatMessage row, CancellationToken token)
        {
            if (row.Role == MessageRole.User) UserWrites++; else AssistantWrites++;
            if (row.Role == MessageRole.Assistant && HoldAssistantWrite)
            { AssistantWriteStarted.TrySetResult(); return AssistantWrite.Task; }
            Messages.Add(row); return Task.CompletedTask;
        }
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => throw new InvalidOperationException("No delete grant.");
    }

    private sealed class Provider : IOllamaClient, IAsyncEnumerable<string>, IAsyncEnumerator<string>
    {
        internal readonly TaskCompletionSource<bool> Move = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource MoveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Dispose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource DisposeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action? OnFactory;
        internal Task<bool>? IssuedMoveOverride;
        internal int HealthyChunks, EmittedChunks, DisposeCalls;
        public string Current => "x";
        public Task<bool> IsAvailableAsync(CancellationToken token) => throw new InvalidOperationException("No unrestricted health preflight.");
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) => throw new InvalidOperationException("No unrestricted catalogue preflight.");
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token)
        { OnFactory?.Invoke(); return this; }
        public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken token = default) => this;
        public ValueTask<bool> MoveNextAsync()
        {
            MoveStarted.TrySetResult();
            if (HealthyChunks == 0) return new(IssuedMoveOverride ?? Move.Task);
            if (EmittedChunks >= HealthyChunks) return ValueTask.FromResult(false);
            EmittedChunks++; return ValueTask.FromResult(true);
        }
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            DisposeStarted.TrySetResult();
            if (HealthyChunks != 0) Dispose.TrySetResult();
            return new(Dispose.Task);
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => Task.FromResult("1 minute");
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new InvalidOperationException("No canonical tool grant.");
    }
    private sealed class Safety : IConversationSafetyService
    {
        public Task<ConversationSafetySnapshot> GetSnapshotAsync(Guid id, CancellationToken token) =>
            Task.FromResult(new ConversationSafetySnapshot(id, 0, ConversationSafetyState.Active, null, 0));
        public Task<ConversationSafetyFlagResult> RecordConfirmedFlagAsync(Guid id, ConfirmedSafetyFlag flag, CancellationToken token) =>
            throw new InvalidOperationException("Unexpected confirmed safety flag.");
        public Task EnsureMayActAsync(Guid id, string operation, CancellationToken token) => Task.CompletedTask;
    }

    private sealed class Workspace : IWorkspaceToolService
    {
        public string ResolveWorkspacePath(string root, string path) => throw new InvalidOperationException("No workspace grant.");
        public Task<string> ReadTextAsync(string root, string path, CancellationToken token) => throw new InvalidOperationException("No workspace grant.");
        public Task WriteTextAtomicAsync(string root, string path, string content, CancellationToken token) => throw new InvalidOperationException("No workspace grant.");
        public Task<IReadOnlyList<string>> SearchFilesAsync(string root, string pattern, CancellationToken token) => throw new InvalidOperationException("No workspace grant.");
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken token) => throw new InvalidOperationException("No execution grant.");
    }

    private sealed class Computer : IComputerToolService
    {
        public bool IsSupported => false;
        public Task<string> SnapshotAsync(CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> ListWindowsAsync(CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> LaunchAppAsync(string name, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> FocusWindowAsync(string title, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> InvokeAsync(string title, string name, string id, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> ClickAsync(string title, int x, int y, string button, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> TypeAsync(string title, string text, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> PressAsync(string title, string keys, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> CloseWindowAsync(string title, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
    }
}
