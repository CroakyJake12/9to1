using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces.Tasks;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

/// <summary>Managed caller controls against the real coordinator and real Space registry.
/// Held repositories and command authority are test controls, not product identity or persistence proof.</summary>
public sealed class SpaceTaskWorkspaceTests
{
    [Fact]
    public async Task Reads_the_same_existing_task_run_context_and_accepted_action_without_starting_work()
    {
        var rig = await Rig.CreateAsync();
        var view = await rig.Service.ReadAsync(rig.Space.Id, rig.Conversation.Id);
        Assert.Equal(rig.Space.Id, view.SpaceId);
        Assert.Equal(rig.Conversation.Id, view.Snapshot!.ContextId);
        Assert.Equal(rig.Snapshot.TaskId, view.Snapshot.TaskId);
        Assert.Equal(rig.Snapshot.ExecutionId, view.Snapshot.ExecutionId);
        Assert.Equal(rig.Snapshot.PersistenceRevision, view.Snapshot.PersistenceRevision);
        Assert.Equal(rig.Snapshot.Plan[0].ActionId, Assert.Single(view.Projection!.AcceptedActions).ActionId);
        Assert.False(view.OriginalAttemptRetained);
        Assert.Empty(view.Snapshot.Attempts);
        Assert.Equal(0, rig.Tasks.Writes);
        Assert.Equal(0, rig.Authority.Commands);
    }

    [Fact]
    public async Task Foreign_or_ordinary_conversation_refuses_before_reading_a_task()
    {
        var rig = await Rig.CreateAsync();
        rig.Conversations.Current = rig.Conversation with { SpaceId = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Service.ReadAsync(rig.Space.Id, rig.Conversation.Id));
        Assert.Equal(0, rig.Tasks.ContextReads);
        rig.Conversations.Current = rig.Conversation with { Mode = HavenMode.Chat };
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Service.ReadAsync(rig.Space.Id, rig.Conversation.Id));
        Assert.Equal(0, rig.Tasks.ContextReads);
        Assert.Equal(0, rig.Tasks.Writes);
    }

    [Fact]
    public async Task Membership_change_during_the_same_held_task_read_prevents_publication()
    {
        var rig = await Rig.CreateAsync();
        var held = new TaskCompletionSource<TaskExecutionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Tasks.ContextRead = held.Task;
        var actual = rig.Service.ReadAsync(rig.Space.Id, rig.Conversation.Id);
        Assert.False(actual.IsCompleted);
        Assert.Equal(1, rig.Tasks.ContextReads);
        rig.Conversations.Current = rig.Conversation with { SpaceId = Guid.NewGuid() };
        held.SetResult(rig.Snapshot);
        await Assert.ThrowsAsync<InvalidOperationException>(() => actual);
        Assert.Equal(0, rig.Tasks.Writes);
        Assert.Equal(0, rig.Authority.Commands);
    }

    [Fact]
    public async Task Queue_uses_fresh_owner_checks_and_preserves_the_same_task_run_and_accepted_work()
    {
        var rig = await Rig.CreateAsync();
        var reply = await rig.Service.SubmitFollowUpAsync(rig.Space.Id, rig.Conversation.Id,
            rig.Snapshot.TaskId, rig.Snapshot.ExecutionId, rig.Snapshot.PersistenceRevision,
            "after that inspect the test output", TaskFollowUpMode.Queue);
        Assert.Equal(rig.Snapshot.TaskId, reply.Snapshot.TaskId);
        Assert.Equal(rig.Snapshot.ExecutionId, reply.Snapshot.ExecutionId);
        Assert.Equal(rig.Conversation.Id, reply.Snapshot.ContextId);
        Assert.Equal(rig.Snapshot.PersistenceRevision + 1, reply.Snapshot.PersistenceRevision);
        var conserved = Assert.Single(reply.Snapshot.Plan);
        Assert.Equal(rig.Snapshot.Plan[0].ActionId, conserved.ActionId);
        Assert.Equal(TaskPlanNodeState.Completed, conserved.State);
        Assert.Equal(rig.Snapshot.Plan[0].Acceptance, conserved.Acceptance);
        Assert.Equal(rig.Snapshot.Plan[0].OriginalToolIntent, conserved.OriginalToolIntent);
        Assert.Equal(rig.Snapshot.TaskId, reply.QueuedTask!.OwnerTaskId);
        Assert.Equal(rig.Snapshot.ExecutionId, reply.QueuedTask.OriginatingExecutionId);
        Assert.Equal(2, rig.Authority.Commands); // Original follow-up + original queue authorization.
        Assert.Equal(1, rig.Tasks.Writes);
        Assert.Empty(reply.Snapshot.Attempts);
    }

    [Fact]
    public async Task Stale_revision_and_missing_command_owner_do_not_mutate_or_start_a_task()
    {
        var rig = await Rig.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Service.SubmitFollowUpAsync(
            rig.Space.Id, rig.Conversation.Id, rig.Snapshot.TaskId, rig.Snapshot.ExecutionId,
            rig.Snapshot.PersistenceRevision - 1, "change the plan", TaskFollowUpMode.Steer));
        Assert.Equal(0, rig.Authority.Commands);
        Assert.Equal(0, rig.Tasks.Writes);
        rig.Tasks.Current = rig.Snapshot with { OwnerBinding = null };
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Service.SubmitFollowUpAsync(
            rig.Space.Id, rig.Conversation.Id, rig.Snapshot.TaskId, rig.Snapshot.ExecutionId,
            rig.Snapshot.PersistenceRevision, "change the plan", TaskFollowUpMode.Steer));
        Assert.Equal(0, rig.Authority.Commands);
        Assert.Equal(0, rig.Tasks.Writes);
    }

    [Fact]
    public async Task Same_raw_repository_task_keeps_all_compound_fault_siblings()
    {
        var rig = await Rig.CreateAsync();
        var first = new IOException("original repository failure");
        var second = new InvalidOperationException("independent repository cleanup failure");
        var raw = new TaskCompletionSource<TaskExecutionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Tasks.ContextRead = raw.Task;
        var actual = rig.Service.ReadAsync(rig.Space.Id, rig.Conversation.Id);
        Assert.False(actual.IsCompleted);
        raw.SetException([first, second]);
        var retained = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.Same(first, retained.InnerExceptions[0]);
        Assert.Same(second, retained.InnerExceptions[1]);
        Assert.Equal(2, retained.InnerExceptions.Count);
        Assert.Equal(0, rig.Tasks.Writes);
    }

    [Fact]
    public async Task An_unstarted_conversation_is_honest_and_a_duplicate_page_is_refused()
    {
        var rig = await Rig.CreateAsync();
        rig.Tasks.Current = null;
        var observation = await rig.Service.ReadAsync(rig.Space.Id, rig.Conversation.Id);
        Assert.Null(observation.Snapshot);
        Assert.Null(observation.Projection);
        Assert.Contains("No canonical task", observation.ExecutionObservation);
        rig.Conversations.Page = [rig.Conversation, rig.Conversation];
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Service.ReadPageAsync(rig.Space.Id));
        Assert.Equal(0, rig.Tasks.Writes);
    }

    private sealed record Rig(SpaceDefinition Space, Conversation Conversation, TaskExecutionSnapshot Snapshot,
        ControlledConversations Conversations, ControlledTasks Tasks, ControlledAuthority Authority, SpaceTaskWorkspaceService Service)
    {
        internal static async Task<Rig> CreateAsync()
        {
            var spaces = new SpaceRegistry(new MemorySettings());
            var space = await spaces.CreateAsync("Development controls");
            var now = DateTimeOffset.UtcNow;
            var conversation = new Conversation(Guid.NewGuid(), HavenMode.Tasks, ConversationKind.Task,
                "Original task", null, null, false, false, now, now, SpaceId: space.Id);
            var taskId = Guid.NewGuid(); var runId = Guid.NewGuid(); var acceptedId = Guid.NewGuid();
            var snapshot = new TaskExecutionSnapshot(taskId, conversation.Id, runId, "Original objective",
                TaskExecutionLifecycle.Suspended, TaskExecutionDurability.PersistedPlan, 1,
                [new TaskPlanNode(acceptedId, null, "Original accepted work", TaskPlanNodeState.Completed,
                    TaskActionInterruptionPolicy.AtomicCommit, 1) { Acceptance = new(Guid.NewGuid(), "test-only owner receipt", now) }],
                [], [], [], acceptedId, now, now)
            { PersistenceRevision = 4, OwnerBinding = new(taskId, conversation.Id, runId,
                "test actor", "test profile", null, null, "test revision", "test observation only") };
            var rows = new ControlledConversations(conversation); var tasks = new ControlledTasks { Current = snapshot };
            var authority = new ControlledAuthority();
            var coordinator = new TaskExecutionCoordinator(tasks, new NullEvents(), admissionAuthority: authority);
            return new(space, conversation, snapshot, rows, tasks, authority, new(spaces, rows, coordinator));
        }
    }
    private sealed class ControlledAuthority : ITaskRunCommandAuthority
    {
        internal int Commands;
        public Task ValidateTaskCommandAsync(TaskExecutionSnapshot snapshot, string command, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Commands++; return Task.CompletedTask; }
        public Task<TaskExecutionOwnerBinding> AuthorizeStartAsync(TaskExecutionSnapshot value, CancellationToken token) => throw new NotSupportedException();
        public Task<ITaskRunAdmissionLease> AuthorizeAttemptAsync(TaskExecutionSnapshot value, Guid id, TaskRunRouteCandidate route, Guid? previous, CancellationToken token) => throw new NotSupportedException();
        public Task ValidateAcceptedActionAsync(TaskExecutionSnapshot value, Guid attempt, Guid action, string receipt, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class ControlledTasks : ITaskExecutionRepository
    {
        internal TaskExecutionSnapshot? Current;
        internal Task<TaskExecutionSnapshot?>? ContextRead;
        internal int Writes, ContextReads;
        public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid id, CancellationToken token)
        { ContextReads++; return ContextRead ?? Task.FromResult(Current?.ContextId == id ? Current : null); }
        public Task<TaskExecutionSnapshot?> GetAsync(Guid id, CancellationToken token) => Task.FromResult(Current?.TaskId == id ? Current : null);
        public Task UpsertAsync(TaskExecutionSnapshot value, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Writes++; Current = value; return Task.CompletedTask; }
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class ControlledConversations(Conversation initial) : IConversationRepository
    {
        internal Conversation Current = initial;
        internal IReadOnlyList<Conversation>? Page;
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token) => Task.FromResult<Conversation?>(Current.Id == id ? Current : null);
        public Task<IReadOnlyList<Conversation>> GetBySpaceAsync(Guid id, int limit, CancellationToken token) => Task.FromResult(Page ?? (IReadOnlyList<Conversation>)[Current]);
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertConversationAsync(Conversation value, CancellationToken token) => throw new NotSupportedException();
        public Task AddMessageAsync(ChatMessage value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class NullEvents : IExecutionEventSink
    { public bool TryPublish(ExecutionEvent value) => true; }
    private sealed class MemorySettings : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _rows = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => Task.FromResult(_rows.GetValueOrDefault(key) as T);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class { _rows[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken token) { _rows.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken token) => throw new NotSupportedException();
    }
}
