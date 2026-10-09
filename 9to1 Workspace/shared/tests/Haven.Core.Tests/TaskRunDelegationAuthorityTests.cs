using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>Real canonical coordinator and real Task authority over synthetic actor/provider/CAS
/// controls. These cases perform no child launch, Home grant, cloud call or OS mutation.</summary>
public sealed class TaskRunDelegationAuthorityTests
{
    [Fact]
    public async Task Copied_parent_admission_and_caller_original_cannot_issue_fixed_child()
    {
        await RunControlAsync(async rig =>
        {
        var setup = await rig.CreateIntentAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Authority.CaptureOriginalDelegationAsync(
            setup.Parent with { }, setup.Current, setup.Intent, setup.Child, default).AsTask());
        var original = await rig.CaptureAsync(setup);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Authority.AuthorizeOriginalChildAsync(
            new CopiedOriginal(original), setup.Parent, setup.Current, setup.Child, default).AsTask());
        Assert.Empty(rig.Repository.ChildWrites);
        });
    }

    [Fact]
    public async Task Missing_actual_coordinator_lookup_cannot_authorize_from_persisted_parent_ids()
    {
        await RunControlAsync(async rig =>
        {
        var setup = await rig.CreateIntentAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.CaptureAsync(setup));
        Assert.Empty(rig.Repository.ChildWrites);
        }, includeLookup: false);
    }

    [Fact]
    public async Task Child_owner_preserves_actual_actor_and_fixed_ids_but_never_accepts_wider_intent()
    {
        await RunControlAsync(async rig =>
        {
        var setup = await rig.CreateIntentAsync(); var original = await rig.CaptureAsync(setup);
        var scope = await rig.AuthorizeAsync(original, setup);
        Assert.Equal(setup.Child.TaskId, scope.ChildOwner.TaskId); Assert.Equal(setup.Child.ExecutionId, scope.ChildOwner.ExecutionId);
        Assert.Equal(setup.Parent.Lease.Owner.ActorId, scope.ChildOwner.ActorId);
        Assert.Equal(setup.Parent.Lease.Owner.ProfileId, scope.ChildOwner.ProfileId);
        Assert.Equal(setup.Parent.Lease.Owner.AuthenticationRevision, scope.ChildOwner.AuthenticationRevision);
        Assert.Null(scope.ChildOwner.AccountId); Assert.Null(scope.ChildOwner.OrganisationId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Authority.AuthorizeOriginalChildAsync(original,
            setup.Parent, setup.Current, setup.Child with { PromptSummary = "changed after original capture" }, default).AsTask());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Authority.CaptureOriginalDelegationAsync(setup.Parent,
            setup.Current, setup.Intent, setup.Child with { ApprovedPermissionScopes = ["workspace:run_command"] }, default).AsTask());
        Assert.Empty(rig.Repository.ChildWrites);
        });
    }

    [Fact]
    public async Task Same_task_owned_original_rebinds_only_after_actual_old_lease_close_and_fresh_parent_attempt()
    {
        await RunControlAsync(async rig =>
        {
        var setup = await rig.CreateIntentAsync(); var original = await rig.CaptureAsync(setup);
        var first = await rig.AuthorizeAsync(original, setup); var owner = first.ChildOwner; await first.DisposeAsync();
        var current = await rig.FallbackAsync(setup.Parent);
        Assert.NotEqual(setup.Parent.AttemptId, current.AttemptId); Assert.Equal(setup.Parent.Snapshot.TaskId, current.Snapshot.TaskId);
        Assert.Equal(setup.Parent.Snapshot.ExecutionId, current.Snapshot.ExecutionId); Assert.Equal(1, rig.Settlement.Joins);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => setup.Parent.Lease.RevalidateAsync(default).AsTask());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Authority.AuthorizeOriginalChildAsync(original,
            setup.Parent, current.Snapshot, setup.Child, default).AsTask());
        var rebound = await rig.AuthorizeAsync(original, setup with { Parent = current, Current = current.Snapshot });
        Assert.Same(original, rebound.OriginalDelegation); Assert.Same(current, rebound.OriginalParentAttempt); Assert.Equal(owner, rebound.ChildOwner);
        await rebound.RevalidateAsync(default); Assert.Empty(rig.Repository.ChildWrites);
        });
    }

    [Fact]
    public async Task Final_held_parent_repository_read_cannot_hide_actor_retirement()
    {
        await RunControlAsync(async rig =>
        {
        var setup = await rig.CreateIntentAsync(); var original = await rig.CaptureAsync(setup);
        var scope = await rig.AuthorizeAsync(original, setup);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); rig.Releases.Add(() => release.TrySetResult());
        var calls = 0;
        rig.Repository.Read = async (_, stored) => { if (++calls == 2) { entered.TrySetResult(); await release.Task; } return stored; };
        var check = scope.RevalidateAsync(default).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(check.IsCompleted);
        rig.Actors.Actor = rig.Actors.Actor with { AuthenticationRevision = "revoked while actual repository awaited" };
        release.TrySetResult(); await rig.ExpectFaultAsync(check);
        Assert.Contains(Leaves(check.Exception!), cause => cause is UnauthorizedAccessException);
        var close = scope.DisposeAsync().AsTask(); await rig.ExpectFaultAsync(close); Assert.True(close.IsFaulted);
        Assert.Empty(rig.Repository.ChildWrites);
        });
    }

    [Fact]
    public async Task Actual_parent_pin_blocks_scope_and_parent_close_until_its_same_disposal_finishes()
    {
        await RunControlAsync(async rig =>
        {
        var setup = await rig.CreateIntentAsync(); var original = await rig.CaptureAsync(setup);
        var scope = await rig.AuthorizeAsync(original, setup); await scope.RevalidateAsync(default);
        var pin = await scope.AcquireOriginalParentCommitPinAsync(default); Assert.NotNull(pin);
        Task? scopeClose = null; Task? parentClose = null;
        try
        {
            scopeClose = scope.DisposeAsync().AsTask(); parentClose = setup.Parent.Lease.DisposeAsync().AsTask();
            Assert.Same(scopeClose, scope.DisposeAsync().AsTask()); Assert.Same(parentClose, setup.Parent.Lease.DisposeAsync().AsTask());
            Assert.False(scopeClose.IsCompleted); Assert.False(parentClose.IsCompleted);
        }
        finally
        {
            await pin!.DisposeAsync();
            if (scopeClose is not null) await scopeClose;
            if (parentClose is not null) await parentClose;
        }
        Assert.Null(await scope.AcquireOriginalParentCommitPinAsync(default)); Assert.Empty(rig.Repository.ChildWrites);
        });
    }

    [Fact]
    public async Task Actual_faulted_OCE_first_repository_task_preserves_both_direct_causes_and_failed_scope()
    {
        await RunControlAsync(async rig =>
        {
        var setup = await rig.CreateIntentAsync(); var original = await rig.CaptureAsync(setup);
        var scope = await rig.AuthorizeAsync(original, setup);
        var cancelledFault = new OperationCanceledException("synthetic FAULTED repository cause"); var second = new IOException("synthetic sibling");
        var actual = new TaskCompletionSource<TaskExecutionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        actual.SetException([cancelledFault, second]); rig.Repository.Read = (_, _) => actual.Task;
        var check = scope.RevalidateAsync(default).AsTask(); await rig.ExpectFaultAsync(check);
        Assert.True(actual.Task.IsFaulted); Assert.True(check.IsFaulted); Assert.False(check.IsCanceled);
        Assert.Contains(cancelledFault, Leaves(check.Exception!)); Assert.Contains(second, Leaves(check.Exception!));
        var close = scope.DisposeAsync().AsTask(); await rig.ExpectFaultAsync(close);
        Assert.Contains(cancelledFault, Leaves(close.Exception!)); Assert.Contains(second, Leaves(close.Exception!));
        rig.Repository.Read = null;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Authority.AuthorizeOriginalChildAsync(original,
            setup.Parent, setup.Current, setup.Child, default).AsTask());
        Assert.Empty(rig.Repository.ChildWrites);
        });
    }

    [Fact]
    public async Task Actual_actor_callback_restoring_foreign_context_cannot_join_its_own_creation_validation()
    {
        await RunControlAsync(async rig =>
        {
        var setup = await rig.CreateIntentAsync(); var original = await rig.CaptureAsync(setup);
        var scope = await rig.AuthorizeAsync(original, setup); var outside = ExecutionContext.Capture(); Assert.NotNull(outside);
        var checkedCallbacks = 0; Task? returnedClose = null;
        rig.Actors.Callback = () => ExecutionContext.Run(outside!.CreateCopy(), _ =>
        {
            Assert.Throws<InvalidOperationException>(() => { returnedClose = scope.DisposeAsync().AsTask(); }); checkedCallbacks++;
        }, null);
        await scope.RevalidateAsync(default); rig.Actors.Callback = null;
        Assert.True(checkedCallbacks > 0); Assert.Null(returnedClose);
        await scope.DisposeAsync(); Assert.Empty(rig.Repository.ChildWrites);
        });
    }

    [Fact]
    public async Task Linked_child_dispatch_uses_fresh_parent_after_fallback_and_declared_tool_narrowing()
    {
        await RunControlAsync(async rig =>
        {
        var setup = await rig.CreateIntentAsync(); var original = await rig.CaptureAsync(setup);
        var scope = await rig.AuthorizeAsync(original, setup); await scope.RevalidateAsync(default);
        var pin = await scope.AcquireOriginalParentCommitPinAsync(default) ?? throw new InvalidOperationException();
        var acknowledgedChild = setup.Child with { OwnerBinding = scope.ChildOwner, PersistenceRevision = 1 };
        try { await rig.Repository.UpsertAsync(acknowledgedChild, default); }
        finally { await pin.DisposeAsync(); }
        await scope.DisposeAsync();
        var parent = await rig.Repository.GetAsync(setup.Current.TaskId, default) ?? throw new InvalidOperationException();
        var linked = setup.Intent with { State = TaskRunDelegationState.ChildLinked, AcknowledgedChildRevision = 1 };
        await rig.Repository.UpsertAsync(parent with { Delegations = [linked], PersistenceRevision = parent.PersistenceRevision + 1 }, default);
        var childCandidate = await rig.Authority.CaptureSelectedRouteAsync(acknowledgedChild, rig.Provider.Model,
            [ToolCapability.Text], []);
        var child = await rig.Coordinator.StartAttemptAsync(acknowledgedChild.TaskId, acknowledgedChild.ExecutionId, childCandidate, default);
        rig.Attempts.Add(child); await child.Lease.RevalidateAsync(default);
        var freshParent = await rig.FallbackAsync(setup.Parent); await child.Lease.RevalidateAsync(default);
        Assert.NotEqual(setup.Parent.AttemptId, freshParent.AttemptId); Assert.Single(rig.Repository.ChildWrites);
        var policy = new PermissionDecisionEngine(); var effects = new WorkspaceTaskRunEffectAuthority(rig.Authority, policy, policy);
        var issue = typeof(WorkspaceTaskRunEffectAuthority).GetMethod("IssueOriginal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Actual owner final-fence producer absent.");
        var readCapability = CapabilityRegistryCatalog.BuiltIns.Single(value => value.Key == "read-file");
        var read = issue.Invoke(effects, [child, Guid.NewGuid(), Path.GetFullPath("."), new OllamaToolCall("read_file", new Dictionary<string, JsonElement>()), readCapability]);
        Assert.NotNull(read);
        var writeCapability = CapabilityRegistryCatalog.BuiltIns.Single(value => value.Key == "write-file");
        policy.Grant("capability:write-file"); // Independent synthetic central grant still cannot widen the child intent.
        var denied = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
        { _ = issue.Invoke(effects, [child, Guid.NewGuid(), Path.GetFullPath("."), new OllamaToolCall("write_file", new Dictionary<string, JsonElement>()), writeCapability]); });
        Assert.IsType<UnauthorizedAccessException>(denied.InnerException);
        policy.Revoke("capability:write-file");
        });
    }

    [Fact]
    public async Task Healthy_creation_scope_closes_do_not_permanently_exhaust_the_retained_retry_cohort()
    {
        await RunControlAsync(async rig =>
        {
        var setup = await rig.CreateIntentAsync(); var original = await rig.CaptureAsync(setup);
        TaskExecutionOwnerBinding? actualOwner = null;
        for (var index = 0; index < 132; index++)
        {
            var scope = await rig.AuthorizeAsync(original, setup); actualOwner ??= scope.ChildOwner;
            Assert.Equal(actualOwner, scope.ChildOwner); await scope.RevalidateAsync(default);
            var close = scope.DisposeAsync().AsTask(); Assert.Same(close, scope.DisposeAsync().AsTask()); await close;
        }
        Assert.Empty(rig.Repository.ChildWrites);
        });
    }

    [Fact]
    public async Task Actual_callback_cancelling_supplied_token_then_throwing_independent_OCE_stays_faulted()
    {
        await RunControlAsync(async rig =>
        {
            var setup = await rig.CreateIntentAsync(); var original = await rig.CaptureAsync(setup); var scope = await rig.AuthorizeAsync(original, setup);
            using var cancellation = new CancellationTokenSource();
            var actual = new OperationCanceledException("Independent synchronous actor failure after token cancellation");
            rig.Actors.Callback = () => { cancellation.Cancel(); throw actual; };
            var check = scope.RevalidateAsync(cancellation.Token).AsTask(); await rig.ExpectFaultAsync(check);
            rig.Actors.Callback = null;
            Assert.True(cancellation.IsCancellationRequested); Assert.True(check.IsFaulted); Assert.False(check.IsCanceled);
            Assert.Contains(actual, Leaves(check.Exception!));
            var close = scope.DisposeAsync().AsTask(); await rig.ExpectFaultAsync(close); Assert.Contains(actual, Leaves(close.Exception!));
            Assert.Empty(rig.Repository.ChildWrites);
        });
    }

    [Fact]
    public async Task Genuine_returned_canceled_repository_task_keeps_canceled_validation_and_observed_close()
    {
        await RunControlAsync(async rig =>
        {
            var setup = await rig.CreateIntentAsync(); var original = await rig.CaptureAsync(setup); var scope = await rig.AuthorizeAsync(original, setup);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var actual = Task.FromCanceled<TaskExecutionSnapshot?>(cancellation.Token); rig.Repository.Read = (_, _) => actual;
            var check = scope.RevalidateAsync(default).AsTask();
            Assert.NotNull(await Record.ExceptionAsync(() => check)); Assert.True(actual.IsCanceled); Assert.True(check.IsCanceled);
            rig.Repository.Read = null;
            var close = scope.DisposeAsync().AsTask(); await rig.ExpectFaultAsync(close);
            Assert.Contains(Leaves(close.Exception!), cause => cause is OperationCanceledException);
            Assert.Empty(rig.Repository.ChildWrites);
        });
    }

    private static async Task RunControlAsync(Func<Rig, Task> body, bool includeLookup = true)
    {
        var rig = new Rig(includeLookup); var errors = new List<Exception>(); Task? actualBody = null; Task? actualClose = null;
        try { actualBody = body(rig); await actualBody; }
        catch (Exception error) { errors.Add((Exception?)actualBody?.Exception ?? error); }
        finally
        {
            try { actualClose = rig.DisposeAsync().AsTask(); await actualClose; }
            catch (Exception error) { errors.Add((Exception?)actualClose?.Exception ?? error); }
        }
        if (errors.Count > 0) throw new AggregateException("Actual independent child control and original teardown failed.", errors);
    }

    private sealed record Setup(TaskRunAttemptAdmission Parent, TaskExecutionSnapshot Current,
        TaskRunDelegationIntent Intent, TaskExecutionSnapshot Child);
    private sealed class CopiedOriginal(ITaskRunDelegationOriginal actual) : ITaskRunDelegationOriginal
    {
        public Guid ParentTaskId => actual.ParentTaskId; public Guid ParentContextId => actual.ParentContextId;
        public Guid ParentExecutionId => actual.ParentExecutionId; public TaskRunDelegationIntent OriginalIntent => actual.OriginalIntent;
    }
    private static IEnumerable<Exception> Leaves(Exception error) => error is AggregateException group
        ? group.InnerExceptions.SelectMany(Leaves) : [error];
    private sealed class Rig : IAsyncDisposable
    {
        public Actors Actors { get; } = new(); public Provider Provider { get; } = new();
        public Repository Repository { get; } = new(); public Settlement Settlement { get; } = new();
        public TaskRunPermissionAuthority Authority { get; } public TaskExecutionCoordinator Coordinator { get; }
        public List<TaskRunAttemptAdmission> Attempts { get; } = []; public List<ITaskRunDelegationAdmission> Scopes { get; } = [];
        public List<Action> Releases { get; } = []; private readonly HashSet<Exception> _expected = new(ReferenceEqualityComparer.Instance);
        public Rig(bool includeLookup = true)
        {
            TaskExecutionCoordinator? actual = null;
            Authority = new(Actors, new Registry(Provider), new Configurations(Provider), new Privacy(), new(new Permissions()),
                originalTasks: includeLookup ? () => actual ?? throw new InvalidOperationException() : null);
            Coordinator = actual = new(Repository, new Events(), admissionAuthority: Authority, runtimeSettlement: Settlement);
            Settlement.Original = id => Attempts.Single(attempt => attempt.AttemptId == id);
        }
        public async Task<Setup> CreateIntentAsync()
        {
            var parent = await Coordinator.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "Synthetic parent",
                TaskExecutionDurability.PersistedPlan, ["workspace:read_file"], default);
            var candidate = await Authority.CaptureSelectedRouteAsync(parent, Provider.Model, [ToolCapability.Text], []);
            var admission = await Coordinator.StartAttemptAsync(parent.TaskId, parent.ExecutionId, candidate, default); Attempts.Add(admission);
            var now = DateTimeOffset.UnixEpoch;
            var intent = new TaskRunDelegationIntent(Guid.NewGuid(), parent.TaskId, parent.ExecutionId, admission.AttemptId,
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "synthetic-child-once", new string('a', 64), "Synthetic child",
                ["workspace:read_file"], TaskRunDelegationState.IntentAcknowledged, now, now);
            // This fixture owns the real CAS acknowledgement; it does not claim the pending production child caller exists.
            var current = admission.Snapshot with { Delegations = [intent], PersistenceRevision = admission.Snapshot.PersistenceRevision + 1 };
            await Repository.UpsertAsync(current, default); current = await Repository.GetAsync(current.TaskId, default) ?? throw new InvalidOperationException();
            var child = new TaskExecutionSnapshot(intent.ChildTaskId, intent.ChildContextId, intent.ChildExecutionId,
                intent.PromptSummary, TaskExecutionLifecycle.Running, TaskExecutionDurability.PersistedPlan, 1, [], [], [],
                intent.RequestedPermissionScopes, null, now, now)
                { ParentDelegation = new(parent.TaskId, parent.ContextId, parent.ExecutionId, intent.Id) };
            return new(admission, current, intent, child);
        }
        public Task<ITaskRunDelegationOriginal> CaptureAsync(Setup setup) => Authority.CaptureOriginalDelegationAsync(
            setup.Parent, setup.Current, setup.Intent, setup.Child, default).AsTask();
        public async Task<ITaskRunDelegationAdmission> AuthorizeAsync(ITaskRunDelegationOriginal original, Setup setup)
        { var scope = await Authority.AuthorizeOriginalChildAsync(original, setup.Parent, setup.Current, setup.Child, default); Scopes.Add(scope); return scope; }
        public async Task<TaskRunAttemptAdmission> FallbackAsync(TaskRunAttemptAdmission previous)
        {
            var failed = await Coordinator.RecordAttemptFailureAsync(previous.Snapshot.TaskId, previous.Snapshot.ExecutionId,
                previous.AttemptId, new("synthetic-network", "Synthetic unavailable", "No provider was invoked"), default);
            var candidate = await Authority.CaptureSelectedRouteAsync(failed, Provider.Model, [ToolCapability.Text], []);
            var resumed = await Coordinator.ResumeAttemptAsync(failed.TaskId, failed.ExecutionId, previous.AttemptId, candidate, default);
            Attempts.Add(resumed); return resumed;
        }
        public async Task ExpectFaultAsync(Task actual)
        {
            var caught = await Record.ExceptionAsync(() => actual); Assert.NotNull(caught); Assert.True(actual.IsFaulted);
            foreach (var cause in Leaves(actual.Exception!)) _expected.Add(cause);
        }
        public async ValueTask DisposeAsync()
        {
            var errors = new List<Exception>();
            foreach (var release in Releases) try { release(); } catch (Exception error) { errors.Add(error); }
            Actors.Callback = null; Repository.Read = null;
            foreach (var scope in Scopes)
            {
                Task? actual = null;
                try { actual = scope.DisposeAsync().AsTask(); await actual; }
                catch (Exception error) { var cause = (Exception?)actual?.Exception ?? error; if (Leaves(cause).Any(leaf => !_expected.Contains(leaf))) errors.Add(cause); }
            }
            foreach (var attempt in Attempts)
            {
                Task? actual = null;
                try { actual = attempt.Lease.DisposeAsync().AsTask(); await actual; }
                catch (Exception error) { var cause = (Exception?)actual?.Exception ?? error; if (Leaves(cause).Any(leaf => !_expected.Contains(leaf))) errors.Add(cause); }
            }
            if (errors.Count > 0) throw new AggregateException("Independent child fixture original retirement failed.", errors);
        }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Actor = new("synthetic-local-task-owner", "synthetic-account-profile", null, null, "synthetic-activation-one");
        public Action? Callback;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); Callback?.Invoke(); return ValueTask.FromResult<AuthenticatedResourceActor?>(Actor); }
    }
    private sealed class Settlement : ITaskRunRuntimeSettlement
    {
        public Func<Guid, TaskRunAttemptAdmission> Original = _ => throw new InvalidOperationException(); public int Joins;
        public async Task AwaitSettlementAsync(Guid task, Guid execution, Guid attempt, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var actual = Original(attempt);
            if (actual.Snapshot.TaskId != task || actual.Snapshot.ExecutionId != execution) throw new InvalidOperationException();
            var close = actual.Lease.DisposeAsync().AsTask(); await close; Joins++;
        }
    }
    private sealed class Repository : ITaskExecutionRepository
    {
        private readonly Dictionary<Guid, string> _rows = []; public readonly List<Guid> ChildWrites = [];
        public Func<Guid, TaskExecutionSnapshot?, Task<TaskExecutionSnapshot?>>? Read;
        public Task UpsertAsync(TaskExecutionSnapshot next, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var previous = _rows.TryGetValue(next.TaskId, out var json) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(json) : null;
            if (next.PersistenceRevision != (previous?.PersistenceRevision ?? 0) + 1)
                throw new TaskExecutionRevisionConflictException(next.TaskId, next.PersistenceRevision - 1, previous?.PersistenceRevision ?? 0);
            _rows[next.TaskId] = JsonSerializer.Serialize(next); if (next.ParentDelegation is not null && previous is null) ChildWrites.Add(next.TaskId);
            return Task.CompletedTask;
        }
        public Task<TaskExecutionSnapshot?> GetAsync(Guid id, CancellationToken token)
        { token.ThrowIfCancellationRequested(); var current = _rows.TryGetValue(id, out var json) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(json) : null; return Read is { } read ? read(id, current) : Task.FromResult(current); }
        public async Task<TaskExecutionSnapshot?> GetByContextAsync(Guid context, CancellationToken token) => (await GetResumableAsync(token)).FirstOrDefault(row => row.ContextId == context);
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<TaskExecutionSnapshot>>(_rows.Values.Select(json => JsonSerializer.Deserialize<TaskExecutionSnapshot>(json)!).ToArray());
    }
    private sealed class Events : IExecutionEventSink { public bool TryPublish(ExecutionEvent value) => true; }
    private sealed class Provider : IModelProvider
    {
        public string Id => "ollama"; public string DisplayName => "Synthetic local"; public bool IsLocal => true; public bool CanManageModels => false; public ModelProviderKind Kind => ModelProviderKind.Ollama;
        public ProviderModelDescriptor Model { get; } = new("ollama", true, new ModelDescriptor("synthetic-child-model", 123, "synthetic", "7B", "Q8", new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UnixEpoch));
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([Model]); }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => Task.FromResult(new ProviderHealthStatus(Id, true, "Synthetic", TimeSpan.Zero, DateTimeOffset.UnixEpoch));
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Registry(IModelProvider provider) : IModelProviderRegistry
    {
        public IReadOnlyList<IModelProvider> Providers => [provider]; public IModelProvider? Find(string id) => id == provider.Id ? provider : null;
        public IModelProvider GetRequired(string id) => Find(id) ?? throw new InvalidOperationException();
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => provider.GetModelsAsync(token);
    }
    private sealed class Configurations(Provider provider) : IProviderConfigurationStore
    {
        private ProviderConfiguration Current => new(provider.Id, provider.Kind, provider.DisplayName, "http://127.0.0.1:11434", true, true, false, new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) => Task.FromResult<ProviderConfiguration?>(id == provider.Id ? Current : null);
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ProviderConfiguration>>([Current]);
        public Task UpsertAsync(ProviderConfiguration value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Privacy : IPrivacyPreferenceStore
    { public PrivacyPreferences Current => PrivacyPreferences.Default; public Task UpdateAsync(PrivacyPreferences value, CancellationToken token) => throw new NotSupportedException(); }
    private sealed class Permissions : IModelPermissionStore
    {
        public Task<ModelPermissionPolicy> GetPolicyAsync(CancellationToken token) => Task.FromResult(ModelPermissionPolicy.Empty);
        public Task SavePolicyAsync(ModelPermissionPolicy value, CancellationToken token) => throw new NotSupportedException();
    }
}
