using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Dev;
using HavenOS.Apps.Spaces.Development;
using HavenOS.Apps.Spaces.Tasks;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

/// <summary>Real versioned Space settings and Dev workspace files with fresh service instances.
/// Task/conversation repositories are controlled observations; no native actor, installed Home,
/// SQLite crash recovery, model execution or accepted physical effect is certified here.</summary>
public sealed class SpaceDevelopmentWorkspaceTests
{
    [Fact]
    public async Task Saved_attachment_reopens_from_fresh_store_instances_with_same_ids_and_accepted_checkpoint()
    {
        await using var rig = await Rig.CreateAsync();
        var attached = await rig.AttachAsync();
        var savedBytes = await File.ReadAllBytesAsync(rig.Paths.SettingsPath);
        var fresh = rig.NewSession(new ControlledStore(new FileDeveloperWorkspaceStore(rig.MetadataDirectory)),
            new SpaceRegistry(new VersionedAtomicSettingsStore(rig.Paths)));
        var reopened = await fresh.OpenAsync(attached.Space.Id, attached.ContextReferenceId);
        Assert.Equal(attached.ContextReferenceId, reopened.ContextReferenceId);
        Assert.Equal(attached.Link, reopened.Link);
        Assert.Equal(rig.Reference, reopened.Project.Reference);
        Assert.Equal(rig.Workspace.WorkspaceId, reopened.Project.Workspace.WorkspaceId);
        Assert.Equal(rig.Workspace.Revision, reopened.Project.Workspace.Revision);
        Assert.Equal(rig.Workspace.Projects[0].ProjectId, reopened.Project.Project.ProjectId);
        Assert.Equal(rig.Workspace.Projects[0].Name, reopened.Project.Project.Name);
        Assert.Equal(rig.Workspace.Roots[0], reopened.Project.Root);
        Assert.Equal(rig.Workspace.SourceControlBindings[0], reopened.Project.Repository);
        Assert.Equal(rig.Workspace.Roots, reopened.Project.Workspace.Roots);
        Assert.Equal(rig.Workspace.Projects, reopened.Project.Workspace.Projects);
        Assert.Equal(rig.Original.TaskId, reopened.Task.Snapshot!.TaskId);
        Assert.Equal(rig.Original.ExecutionId, reopened.Task.Snapshot.ExecutionId);
        Assert.Equal(rig.Conversations.Current.Id, reopened.Task.Snapshot.ContextId);
        Assert.Equal(rig.Original.CheckpointId, reopened.Task.Snapshot.CheckpointId);
        Assert.Same(rig.Original.Plan[0].Acceptance, reopened.Task.Snapshot.Plan[0].Acceptance);
        Assert.Equal(savedBytes, await File.ReadAllBytesAsync(rig.Paths.SettingsPath));
        Assert.Equal(SpaceContextPermission.Unknown, Assert.Single(reopened.Space.ContextReferences!).Permission);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replacement_during_held_project_open_refuses_the_old_task_or_run(bool replaceRun)
    {
        await using var rig = await Rig.CreateAsync();
        var attached = await rig.AttachAsync();
        var held = rig.Store.HoldNextRead();
        var actual = rig.Retain(rig.Session.OpenAsync(attached.Space.Id, attached.ContextReferenceId));
        await rig.Store.Entered.Task;
        Assert.False(actual.IsCompleted);
        rig.Tasks.Current = replaceRun ? rig.Original with { ExecutionId = Guid.NewGuid() }
            : rig.Original with { TaskId = Guid.NewGuid() };
        held.SetResult(DeveloperOperationResult<DeveloperWorkspace>.Success(rig.Workspace));
        await Assert.ThrowsAsync<InvalidOperationException>(() => actual);
        rig.MarkObservedFailure(actual);
        Assert.Equal(attached.Space.Revision, (await rig.Spaces.ReadExistingAsync(attached.Space.Id))!.Revision);
    }

    [Fact]
    public async Task Membership_withdrawal_during_project_open_refuses_publication()
    {
        await using var rig = await Rig.CreateAsync();
        var attached = await rig.AttachAsync();
        var held = rig.Store.HoldNextRead();
        var actual = rig.Retain(rig.Session.OpenAsync(attached.Space.Id, attached.ContextReferenceId));
        await rig.Store.Entered.Task;
        rig.Conversations.Current = rig.Conversations.Current with { SpaceId = Guid.NewGuid() };
        held.SetResult(DeveloperOperationResult<DeveloperWorkspace>.Success(rig.Workspace));
        await Assert.ThrowsAsync<InvalidOperationException>(() => actual);
        rig.MarkObservedFailure(actual);
    }

    [Fact]
    public async Task Same_ids_advancing_during_project_open_publish_current_accepted_work_and_checkpoint()
    {
        await using var rig = await Rig.CreateAsync();
        var attached = await rig.AttachAsync();
        var held = rig.Store.HoldNextRead();
        var actual = rig.Retain(rig.Session.OpenAsync(attached.Space.Id, attached.ContextReferenceId));
        await rig.Store.Entered.Task;
        var latest = rig.Original with
        {
            PersistenceRevision = rig.Original.PersistenceRevision + 1,
            CheckpointId = Guid.NewGuid(), UpdatedAt = rig.Original.UpdatedAt.AddSeconds(1)
        };
        rig.Tasks.Current = latest;
        held.SetResult(DeveloperOperationResult<DeveloperWorkspace>.Success(rig.Workspace));
        var reopened = await actual;
        Assert.Same(latest, reopened.Task.Snapshot);
        Assert.Equal(latest.CheckpointId, reopened.Task.Snapshot!.CheckpointId);
        Assert.Equal(latest.PersistenceRevision, reopened.Task.Snapshot.PersistenceRevision);
        Assert.Same(rig.Original.Plan, reopened.Task.Snapshot.Plan);
        Assert.Equal(attached.Link, reopened.Link);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Attachment_refuses_replaced_task_or_membership_before_metadata_write(bool withdrawMembership)
    {
        await using var rig = await Rig.CreateAsync();
        var before = await File.ReadAllBytesAsync(rig.Paths.SettingsPath);
        var held = rig.Store.HoldNextRead();
        var actual = rig.AttachAsync();
        await rig.Store.Entered.Task;
        if (withdrawMembership) rig.Conversations.Current = rig.Conversations.Current with { SpaceId = Guid.NewGuid() };
        else rig.Tasks.Current = rig.Original with { ExecutionId = Guid.NewGuid() };
        held.SetResult(DeveloperOperationResult<DeveloperWorkspace>.Success(rig.Workspace));
        await Assert.ThrowsAsync<InvalidOperationException>(() => actual);
        rig.MarkObservedFailure(actual);
        Assert.Equal(before, await File.ReadAllBytesAsync(rig.Paths.SettingsPath));
        var unchanged = await rig.Spaces.ReadExistingAsync(rig.Space.Id);
        Assert.Equal(rig.Space.Revision, unchanged!.Revision);
        Assert.Empty(unchanged.ContextReferences ?? []);
    }

    [Fact]
    public async Task Explicit_current_project_reattachment_preserves_reference_identity_and_original_task_run()
    {
        await using var rig = await Rig.CreateAsync();
        var attached = await rig.AttachAsync();
        var firstRow = Assert.Single(attached.Space.ContextReferences!);
        var changedProject = rig.Workspace.Projects[0] with { Name = "Renamed existing project", Revision = 2 };
        var saved = await rig.Store.Inner.SaveAsync(rig.Workspace with { Projects = [changedProject] }, 1);
        Assert.True(saved.Succeeded);
        var staleOriginal = rig.Retain(rig.Session.OpenAsync(attached.Space.Id, attached.ContextReferenceId));
        var stale = await Assert.ThrowsAsync<SpaceDevelopmentResolutionException>(() => staleOriginal);
        rig.MarkObservedFailure(staleOriginal);
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, stale.OriginalResult.Error!.Code);
        var currentReference = rig.Reference with { WorkspaceRevision = saved.Value!.Revision, ProjectRevision = changedProject.Revision };
        var updated = await rig.Session.AttachAsync(attached.Space.Id, attached.Space.Revision,
            rig.Original.ContextId, rig.Original.TaskId, rig.Original.ExecutionId, currentReference);
        var currentRow = Assert.Single(updated.Space.ContextReferences!);
        Assert.Equal(firstRow.ContextId, currentRow.ContextId);
        Assert.Equal(firstRow.AddedAt, currentRow.AddedAt);
        Assert.Equal(currentReference, updated.Link.Project);
        Assert.Equal(rig.Original.TaskId, updated.Link.TaskId);
        Assert.Equal(rig.Original.ExecutionId, updated.Link.ExecutionId);
        var fresh = rig.NewSession(new ControlledStore(new FileDeveloperWorkspaceStore(rig.MetadataDirectory)),
            new SpaceRegistry(new VersionedAtomicSettingsStore(rig.Paths)));
        Assert.Equal(currentReference, (await fresh.OpenAsync(updated.Space.Id, currentRow.ContextId)).Project.Reference);
    }

    [Fact]
    public async Task Concurrent_space_revision_change_refuses_attachment_and_preserves_other_user_edit()
    {
        await using var rig = await Rig.CreateAsync();
        var held = rig.Store.HoldNextRead();
        var actual = rig.AttachAsync();
        await rig.Store.Entered.Task;
        var edited = await rig.Spaces.UpdateAsync(rig.Space with { Name = "User's current Space" }, rig.Space.Revision);
        var editedBytes = await File.ReadAllBytesAsync(rig.Paths.SettingsPath);
        held.SetResult(DeveloperOperationResult<DeveloperWorkspace>.Success(rig.Workspace));
        await Assert.ThrowsAsync<SpaceRevisionConflictException>(() => actual);
        rig.MarkObservedFailure(actual);
        Assert.Equal(editedBytes, await File.ReadAllBytesAsync(rig.Paths.SettingsPath));
        Assert.Equal(edited.Name, (await rig.Spaces.ReadExistingAsync(rig.Space.Id))!.Name);
    }

    [Fact]
    public async Task Acquisition_callback_failure_joins_held_original_and_keeps_faulted_OCE_siblings()
    {
        await using var rig = await Rig.CreateAsync();
        var attached = await rig.AttachAsync();
        var held = rig.Store.HoldNextRead();
        var before = rig.Store.Reads;
        var callbackFault = new InvalidOperationException("controlled enrollment failure after capture");
        var originalFault = new OperationCanceledException("controlled faulted original, not a canceled Task");
        var sibling = new IOException("controlled independent original sibling");
        void Scope(Action capture)
        {
            capture();
            if (rig.Store.Reads > before) throw callbackFault;
        }
        var actual = rig.Retain(rig.Session.OpenAsync(attached.Space.Id, attached.ContextReferenceId, default, Scope));
        await rig.Store.Entered.Task;
        Assert.False(actual.IsCompleted);
        Assert.Same(held.Task, rig.Store.LastRead);
        held.SetException([originalFault, sibling]);
        var failed = await Assert.ThrowsAsync<AggregateException>(() => actual);
        rig.MarkObservedFailure(actual);
        Assert.True(held.Task.IsFaulted);
        Assert.False(actual.IsCanceled);
        Assert.Contains(callbackFault, failed.Flatten().InnerExceptions);
        Assert.Contains(originalFault, failed.Flatten().InnerExceptions);
        Assert.Contains(sibling, failed.Flatten().InnerExceptions);
    }

    [Fact]
    public async Task Persisted_attachment_ack_survives_following_actual_project_refresh_fault()
    {
        await using var rig = await Rig.CreateAsync();
        var fault = new IOException("controlled project refresh after acknowledged save");
        rig.Store.FaultOnRead = 2;
        rig.Store.Fault = fault;
        var actual = rig.AttachAsync();
        var failed = await Assert.ThrowsAsync<SpaceDevelopmentAttachmentAcknowledgedException>(() => actual);
        rig.MarkObservedFailure(actual);
        Assert.Contains(fault, ((AggregateException)failed.InnerException!).Flatten().InnerExceptions);
        var freshSpace = await new SpaceRegistry(new VersionedAtomicSettingsStore(rig.Paths)).ReadExistingAsync(rig.Space.Id);
        Assert.Equal(failed.AcknowledgedSpace.Revision, freshSpace!.Revision);
        Assert.Equal(failed.AcknowledgedReference, Assert.Single(freshSpace.ContextReferences!));
        Assert.Equal(rig.Original.TaskId, SpaceDevelopmentWorkspace.ReadLink(failed.AcknowledgedReference).TaskId);
        Assert.Equal(rig.Original.ExecutionId, SpaceDevelopmentWorkspace.ReadLink(failed.AcknowledgedReference).ExecutionId);
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly List<DeveloperTaskWorkspaceService> _borrowed = [];
        private readonly List<ControlledStore> _stores = [];
        private readonly List<Task> _originals = [];
        private readonly HashSet<Task> _observedFailures = new(ReferenceEqualityComparer.Instance);
        internal Paths Paths = null!;
        internal string MetadataDirectory = null!;
        internal SpaceRegistry Spaces = null!;
        internal SpaceDefinition Space = null!;
        internal DeveloperWorkspace Workspace = null!;
        internal DeveloperProjectReference Reference = null!;
        internal Conversations Conversations = null!;
        internal Tasks Tasks = new();
        internal TaskExecutionSnapshot Original = null!;
        internal ControlledStore Store = null!;
        internal SpaceDevelopmentWorkspace Session = null!;
        internal TaskExecutionCoordinator Canonical = null!;
        private readonly RefusingTools _tools = new();
        private readonly RefusingOwner _owner = new();

        internal static async Task<Rig> CreateAsync()
        {
            var rig = new Rig();
            rig.Paths = new(Path.Combine(Path.GetTempPath(), "haven-spaces-dev-reopen-controls", Guid.NewGuid().ToString("N")));
            rig.MetadataDirectory = Path.Combine(rig.Paths.DataDirectory, "dev");
            rig.Spaces = new(new VersionedAtomicSettingsStore(rig.Paths));
            rig.Space = await rig.Spaces.CreateAsync("Existing development Space");
            var root = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.Combine(rig.Paths.DataDirectory, "source"));
            var project = new DeveloperProject(Guid.NewGuid(), "existing", "Existing project", [root.RootId],
                "C#", null, "dotnet", [], [], [], [], null);
            var workspace = DeveloperWorkspace.Create([root]) with
            { Projects = [project], SourceControlBindings = [new("existing-repository", root.RootId, "git", "observed-repository-id")] };
            var disk = new FileDeveloperWorkspaceStore(rig.MetadataDirectory);
            var created = await disk.CreateAsync(workspace);
            Assert.True(created.Succeeded);
            rig.Workspace = created.Value!;
            rig.Reference = new(workspace.WorkspaceId, 1, project.ProjectId, 1, root.RootId, "existing-repository");
            var now = workspace.CreatedAt;
            var conversation = new Conversation(Guid.NewGuid(), HavenMode.Tasks, ConversationKind.Task,
                "Existing task", null, null, false, false, now, now, SpaceId: rig.Space.Id);
            rig.Conversations = new(conversation);
            var actionId = Guid.NewGuid();
            rig.Original = new(Guid.NewGuid(), conversation.Id, Guid.NewGuid(), "Existing objective",
                TaskExecutionLifecycle.Suspended, TaskExecutionDurability.PersistedPlan, 1,
                [new(actionId, null, "Historical controlled observation", TaskPlanNodeState.Completed,
                    TaskActionInterruptionPolicy.AtomicCommit, 1)
                { Acceptance = new(Guid.NewGuid(), "controlled historical observation; no physical grant", now) }],
                [], [], [], actionId, now, now)
            { PersistenceRevision = 4, CheckpointId = Guid.NewGuid() };
            rig.Tasks.Current = rig.Original;
            rig.Canonical = new(rig.Tasks, new Events());
            rig.Store = new(disk);
            rig.Session = rig.NewSession(rig.Store, rig.Spaces);
            return rig;
        }

        internal SpaceDevelopmentWorkspace NewSession(ControlledStore store, SpaceRegistry spaces)
        {
            var dev = new DeveloperTaskWorkspaceService(store, new(Conversations, new RefusingContainers()),
                Canonical, _owner, new(_tools));
            _borrowed.Add(dev); _stores.Add(store);
            return new(spaces, new SpaceTaskWorkspaceService(spaces, Conversations, Canonical), dev);
        }
        internal Task<SpaceDeveloperView> AttachAsync() => Retain(Session.AttachAsync(Space.Id, Space.Revision,
            Original.ContextId, Original.TaskId, Original.ExecutionId, Reference));
        internal Task<T> Retain<T>(Task<T> actual) { _originals.Add(actual); return actual; }
        internal void MarkObservedFailure(Task actual) => _observedFailures.Add(actual);
        public async ValueTask DisposeAsync()
        {
            // Release controls even after a failed assertion, then independently join every
            // retained original. Only failures already observed by the test are expected.
            var errors = new List<Exception>();
            foreach (var store in _stores) store.ReleaseHeldReads(Workspace);
            foreach (var actual in _originals)
                try { await actual.ConfigureAwait(false); }
                catch (Exception error)
                { if (!_observedFailures.Contains(actual)) errors.Add((Exception?)actual.Exception ?? error); }
            foreach (var borrowed in _borrowed)
                try { await borrowed.CloseAndDrainAsync(); }
                catch (Exception error) { errors.Add(error); }
            try
            {
                Assert.Equal(0, Tasks.Writes);
                Assert.Equal(0, _tools.Calls);
                Assert.Equal(0, _owner.Calls);
            }
            catch (Exception error) { errors.Add(error); }
            try { if (Directory.Exists(Paths.DataDirectory)) Directory.Delete(Paths.DataDirectory, true); }
            catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException("Controlled originals/cleanup did not settle as observed.", errors);
        }
    }

    private sealed class ControlledStore(FileDeveloperWorkspaceStore inner) : IDeveloperWorkspaceStore
    {
        internal FileDeveloperWorkspaceStore Inner { get; } = inner;
        internal int Reads, FaultOnRead;
        internal Exception? Fault;
        internal Task<DeveloperOperationResult<DeveloperWorkspace>>? LastRead;
        private TaskCompletionSource<DeveloperOperationResult<DeveloperWorkspace>>? _held;
        private readonly List<TaskCompletionSource<DeveloperOperationResult<DeveloperWorkspace>>> _controls = [];
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<DeveloperOperationResult<DeveloperWorkspace>> HoldNextRead()
        {
            Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _controls.Add(_held);
            return _held;
        }
        internal void ReleaseHeldReads(DeveloperWorkspace workspace)
        { foreach (var control in _controls) control.TrySetResult(DeveloperOperationResult<DeveloperWorkspace>.Success(workspace)); }
        public Task<DeveloperOperationResult<DeveloperWorkspace>> GetAsync(Guid id, CancellationToken token = default)
        {
            Reads++;
            var held = _held; _held = null;
            var actual = held?.Task ?? (Reads == FaultOnRead ? Task.FromException<DeveloperOperationResult<DeveloperWorkspace>>(Fault!) : Inner.GetAsync(id, token));
            LastRead = actual;
            if (held is not null) Entered.TrySetResult();
            return actual;
        }
        public Task<DeveloperOperationResult<DeveloperWorkspace>> CreateAsync(DeveloperWorkspace value, CancellationToken token = default) => throw new NotSupportedException("Reopening must not create a workspace.");
        public Task<DeveloperOperationResult<DeveloperWorkspace>> SaveAsync(DeveloperWorkspace value, long revision, CancellationToken token = default) => throw new NotSupportedException("Reopening must not save a project.");
    }
    private sealed class Tasks : ITaskExecutionRepository
    {
        internal TaskExecutionSnapshot Current = null!;
        internal int Writes;
        public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid id, CancellationToken token) => Task.FromResult<TaskExecutionSnapshot?>(Current.ContextId == id ? Current : null);
        public Task<TaskExecutionSnapshot?> GetAsync(Guid id, CancellationToken token) => Task.FromResult<TaskExecutionSnapshot?>(Current.TaskId == id ? Current : null);
        public Task UpsertAsync(TaskExecutionSnapshot value, CancellationToken token) { Writes++; throw new NotSupportedException("Reopening must not write task state."); }
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Conversations(Conversation current) : IConversationRepository
    {
        internal Conversation Current = current;
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token) => Task.FromResult<Conversation?>(Current.Id == id ? Current : null);
        public Task<IReadOnlyList<Conversation>> GetBySpaceAsync(Guid id, int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<Conversation>>([Current]);
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertConversationAsync(Conversation value, CancellationToken token) => throw new NotSupportedException();
        public Task AddMessageAsync(ChatMessage value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class RefusingContainers : IContainerRepository
    {
        public Task<IReadOnlyList<ContainerDefinition>> GetByModeAsync(HavenMode mode, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertAsync(ContainerDefinition value, CancellationToken token) => throw new NotSupportedException();
        public Task<Lesson> CreateSubjectAsync(ContainerDefinition value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAndDetachConversationsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<Lesson>> GetLessonsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertLessonAsync(Lesson value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteLessonAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class RefusingTools : IWorkspaceToolService
    {
        internal int Calls;
        private Exception Refuse() { Calls++; return new NotSupportedException("No physical tools during project reopening."); }
        public string ResolveWorkspacePath(string root, string relative) => throw Refuse();
        public Task<string> ReadTextAsync(string root, string relative, CancellationToken token) => throw Refuse();
        public Task WriteTextAtomicAsync(string root, string relative, string text, CancellationToken token) => throw Refuse();
        public Task<IReadOnlyList<string>> SearchFilesAsync(string root, string query, CancellationToken token) => throw Refuse();
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken token) => throw Refuse();
    }
    private sealed class RefusingOwner : ITaskRunToolActionOwner
    {
        internal int Calls;
        private Exception Refuse() { Calls++; return new NotSupportedException("No original tool dispatch during reopening."); }
        public bool SupportsCanonicalInvocation(ToolRuntimeKind runtime, string name) { Calls++; return false; }
        public Task<ITaskRunToolActionPreparation> PrepareOriginalAsync(TaskRunAttemptAdmission attempt, TaskExecutionSnapshot current,
            Guid actionId, OllamaToolCall call, ToolRuntimeKind runtime, PermissionMode permission, string? root, CancellationToken token) => throw Refuse();
        public Task<TaskRunToolActionResult> ExecuteOriginalAsync(ITaskRunToolActionPreparation original,
            Func<CancellationToken, Task<WorkspaceToolResult>> body, CancellationToken token) => throw Refuse();
        public ValueTask ValidateOriginalPreparationAsync(ITaskRunToolActionPreparation original, TaskExecutionSnapshot current, CancellationToken token) => throw Refuse();
        public ValueTask ValidateOriginalResultAsync(ITaskRunToolActionPreparation original, TaskRunToolActionResult result, CancellationToken token) => throw Refuse();
        public ValueTask RetireAcknowledgedOriginalAsync(ITaskRunToolActionPreparation original, TaskExecutionSnapshot current, CancellationToken token) => throw Refuse();
    }
    private sealed class Events : IExecutionEventSink
    { public bool TryPublish(ExecutionEvent value) => true; }
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        internal string SettingsPath => Path.Combine(DataDirectory, "settings.json");
        public string DatabasePath => Path.Combine(DataDirectory, "unused-test-database.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "unused-legacy-state.json");
    }
}
