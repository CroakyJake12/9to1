using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace Haven.Infrastructure.Tests;

// Real Home store/profile/review/Accept/claim/held leases. Saved-root and Task preparation
// issuers plus the finite native callback are synthetic; these prove no Task/Files/process grant.
public sealed class HomeDeveloperWorkspaceExecutionConsentTests
{
    [Fact] public Task Copied_public_binding_is_refused_before_review_or_tool_validation() => Run(async rig =>
    {
        await rig.Prepare(); var copy = rig.Bindings.Binding! with { };
        var actual = rig.Own(rig.Source.AcquireOriginalAsync(copy, rig.Tools.Preparation!, rig.Tools.Current!, rig.Callback, default));
        var error = await Assert.ThrowsAsync<AggregateException>(() => actual); rig.Expect(actual); rig.ExpectedClose = true;
        Assert.Contains(Leaves(error), cause => cause is UnauthorizedAccessException); Assert.Equal(0, rig.Tools.Validations);
        Assert.Empty((await rig.Own(rig.Permissions.GetSnapshotAsync())).PendingRequests);
    });
    [Fact] public Task Pending_execution_review_has_no_held_entry_or_native_start() => Run(async rig =>
    {
        await rig.Prepare(); var acquisition = rig.Acquire(); var pending = await rig.Pending();
        Assert.Equal(HomeDeveloperWorkspaceExecutionConsentSource.ExecuteAction, pending.Scope.ActionName);
        Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State); Assert.False(acquisition.IsCompleted); Assert.Equal(0, rig.Starts);
        rig.Expect(acquisition); rig.ExpectedClose = true; rig.Source.RequestOriginalExecutionRetirement();
        try { await acquisition; } catch { _ = acquisition.Exception; }
    });
    [Fact] public Task Accepted_original_review_issues_one_held_start_entry_and_closes_before_Home_reads() => Run(async rig =>
    {
        var consent = await rig.Accept(); var enter = rig.Own(rig.Source.EnterOriginalProcessStartAsync(consent, rig.Tools.Preparation!, default));
        Assert.Same(enter, rig.Source.EnterOriginalProcessStartAsync(consent, rig.Tools.Preparation!, default)); var entry = await enter;
        Assert.True(rig.Source.IsIssuedOriginalEntry(consent, rig.Tools.Preparation!, entry));
        var homeRead = rig.Own(rig.Permissions.GetSnapshotAsync()); Assert.False(homeRead.IsCompleted);
        Assert.True(entry.RunOriginalProcessStart(rig.Binding!.CanonicalRoot, "synthetic-fixed-shell", new('a', 64), () => { rig.Starts++; return true; }));
        Assert.Throws<UnauthorizedAccessException>(() => entry.RunOriginalProcessStart(rig.Binding.CanonicalRoot, "synthetic-fixed-shell", new('a', 64), () => true));
        var close = rig.Own(entry.DisposeAsync().AsTask()); Assert.Same(close, entry.DisposeAsync().AsTask()); await close;
        await homeRead.WaitAsync(TimeSpan.FromSeconds(10)); Assert.True(close.IsCompletedSuccessfully); Assert.False(rig.Source.IsIssuedOriginalEntry(consent, rig.Tools.Preparation!, entry));
        await rig.Own(consent.DisposeAsync().AsTask());
        var snapshot = await rig.Own(rig.Permissions.GetSnapshotAsync());
        Assert.Contains(snapshot.RecentAuditEvents, value => value.ResultCode == "DEV_ORIGINAL_PROCESS_START_ACKNOWLEDGED" && value.RequestState == HomePermissionRequestState.Succeeded);
        Assert.Equal(1, rig.Starts);
    });
    [Fact] public Task Changed_saved_root_issuer_after_review_refuses_the_start_entry() => Run(async rig =>
    {
        var consent = await rig.Accept(); rig.CurrentBindings = new BindingSource();
        var actual = rig.Own(rig.Source.EnterOriginalProcessStartAsync(consent, rig.Tools.Preparation!, default));
        var error = await Assert.ThrowsAsync<AggregateException>(() => actual); rig.Expect(actual); rig.ExpectedClose = true;
        Assert.Contains(Leaves(error), cause => cause is UnauthorizedAccessException); Assert.Equal(0, rig.Starts);
    });
    [Fact] public Task Genuine_Home_caller_revocation_after_review_refuses_native_entry() => Run(async rig =>
    {
        var consent = await rig.Accept(); Assert.True((await rig.Own(rig.Permissions.BlockCallerAsync(rig.Binding!.OriginalActor.ActorId))).Succeeded);
        var actual = rig.Own(rig.Source.EnterOriginalProcessStartAsync(consent, rig.Tools.Preparation!, default));
        var error = await Assert.ThrowsAsync<AggregateException>(() => actual); rig.Expect(actual); rig.ExpectedClose = true;
        Assert.Contains(Leaves(error), cause => cause is UnauthorizedAccessException); Assert.Equal(0, rig.Starts);
    });
    [Fact] public Task Retired_source_joins_held_raw_validation_and_both_faulted_direct_causes() => Run(async rig =>
    {
        var consent = await rig.Accept(); var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Bindings.Validation = () => { entered.TrySetResult(); return held.Task; };
        var validation = rig.Own(rig.Source.ValidateOriginalConsentAsync(consent, rig.Tools.Preparation!, default));
        Task? close = null; var first = new OperationCanceledException("faulted saved-root read"); var second = new IOException("independent original sibling");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); close = rig.Own(rig.Source.CloseAndDrainOriginalExecutionsAsync());
            Assert.False(close.IsCompleted); Assert.False(validation.IsCompleted);
        }
        finally
        {
            held.TrySetException([first, second]); rig.Expect(validation); if (close is not null) rig.Expect(close);
            rig.ExpectedClose = true; rig.Bindings.Validation = null;
        }
        close ??= rig.Own(rig.Source.CloseAndDrainOriginalExecutionsAsync());
        var error = await Assert.ThrowsAsync<AggregateException>(() => close.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains(first, Leaves(error)); Assert.Contains(second, Leaves(error)); Assert.True(held.Task.IsFaulted); Assert.True(validation.IsFaulted); Assert.False(validation.IsCanceled);
    });
    [Fact] public Task Entry_release_starts_before_joining_public_validation_waiting_on_actual_Home_gate() => Run(async rig =>
    {
        var consent = await rig.Accept(); var entry = await rig.Own(rig.Source.EnterOriginalProcessStartAsync(consent, rig.Tools.Preparation!, default));
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); rig.Bindings.Validation = () => { reached.TrySetResult(); return Task.CompletedTask; };
        var validation = rig.Own(rig.Source.ValidateOriginalConsentAsync(consent, rig.Tools.Preparation!, default));
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(validation.IsCompleted); rig.Bindings.Validation = null;
        var close = rig.Own(rig.Source.CloseAndDrainOriginalExecutionsAsync()); rig.Expect(validation); rig.Expect(close); rig.ExpectedClose = true;
        try
        {
            var error = await Assert.ThrowsAsync<AggregateException>(() => close.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Contains(Leaves(error), cause => cause is ObjectDisposedException); Assert.True(validation.IsFaulted); Assert.True(close.IsFaulted);
            var entryClose = rig.Own(entry.DisposeAsync().AsTask()); await entryClose; Assert.True(entryClose.IsCompletedSuccessfully); Assert.Equal(0, rig.Starts);
        }
        finally { await rig.Own(entry.DisposeAsync().AsTask()); }
    });
    [Fact] public Task Restored_context_binding_callback_cannot_join_encompassing_consent() => Run(async rig =>
    {
        var prior = ExecutionContext.Capture(); Assert.NotNull(prior); var consent = await rig.Accept(); var attempts = 0;
        rig.Bindings.Validation = () =>
        {
            ExecutionContext.Run(prior!, _ => { attempts++; Assert.Throws<InvalidOperationException>(() => { _ = rig.Source.CloseAndDrainOriginalExecutionsAsync(); }); }, null);
            return Task.CompletedTask;
        };
        await rig.Own(rig.Source.ValidateOriginalConsentAsync(consent, rig.Tools.Preparation!, default)); rig.Bindings.Validation = null; Assert.True(attempts > 0);
    });
    [Fact] public Task Restored_context_native_start_callback_cannot_join_its_actual_held_entry_owner() => Run(async rig =>
    {
        var prior = ExecutionContext.Capture(); Assert.NotNull(prior); var consent = await rig.Accept();
        var entry = await rig.Own(rig.Source.EnterOriginalProcessStartAsync(consent, rig.Tools.Preparation!, default));
        Assert.True(entry.RunOriginalProcessStart(rig.Binding!.CanonicalRoot, "synthetic-fixed-shell", new('a', 64), () =>
        {
            ExecutionContext.Run(prior!, _ => Assert.Throws<InvalidOperationException>(() => { _ = rig.Source.CloseAndDrainOriginalExecutionsAsync(); }), null);
            rig.Starts++; return true;
        }));
        await rig.Own(entry.DisposeAsync().AsTask()); Assert.Equal(1, rig.Starts);
    });
    [Fact] public Task Held_actual_profile_read_then_restored_context_principal_cannot_join_original_consent() => Run(async rig =>
    {
        var prior = ExecutionContext.Capture(); Assert.NotNull(prior); var consent = await rig.Accept();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously); int reads = 0, guarded = 0;
        rig.Principals.Read = principalToken =>
        {
            if (Interlocked.Increment(ref reads) == 1) { entered.TrySetResult(); return new(held.Task); }
            ExecutionContext.Run(prior!, ignoredContext =>
            {
                guarded++; Assert.Throws<InvalidOperationException>(() => { _ = rig.Source.CloseAndDrainOriginalExecutionsAsync(); });
            }, null);
            return ValueTask.FromResult<string?>(Principal.Original);
        };
        Task? validation = null;
        try
        {
            validation = rig.Own(rig.Source.ValidateOriginalConsentAsync(consent, rig.Tools.Preparation!, default));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(validation.IsCompleted);
            held.TrySetResult(Principal.Original); await validation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(held.Task.IsCompletedSuccessfully); Assert.True(validation.IsCompletedSuccessfully); Assert.True(reads >= 2); Assert.True(guarded >= 1);
        }
        finally
        {
            held.TrySetResult(Principal.Original); rig.Principals.Read = null;
            if (validation is not null) await validation;
        }
        Assert.Equal(0, rig.Starts);
    });
    [Fact] public async Task Empty_source_retirement_is_permanent_and_returns_same_actual_close()
    {
        var rig = new Rig(); try
        {
            rig.Source.RequestOriginalExecutionRetirement(); var close = rig.Source.CloseAndDrainOriginalExecutionsAsync();
            Assert.Same(close, rig.Source.CloseAndDrainOriginalExecutionsAsync()); await close; await rig.Prepare();
            Assert.Throws<ObjectDisposedException>(() => { _ = rig.Acquire(); }); Assert.Equal(0, rig.Starts);
        }
        finally { await rig.Source.DisposeAsync(); Directory.Delete(rig.Root, true); }
    }
    [Fact] public Task Delayed_nested_tool_validation_callback_is_revoked_before_consent_retirement() => Run(async rig =>
    {
        await rig.Prepare(); Action? retained = null; int callbacks = 0;
        Action<Action> caller = body =>
        {
            // The owning acquisition-body callback precedes binding source, tool source,
            // and issuer recognition; the fifth is its genuine tool-validation factory.
            if (++callbacks == 5) { retained = body; return; }
            body();
        };
        var actual = rig.Own(rig.Source.AcquireOriginalAsync(rig.Binding!, rig.Tools.Preparation!, rig.Tools.Current!, caller, default));
        rig.Expect(actual); rig.ExpectedClose = true;
        var failure = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.Contains(Leaves(failure), cause => cause is InvalidOperationException);
        Assert.NotNull(retained); Assert.Equal(0, rig.Tools.Validations);
        rig.Source.RequestOriginalExecutionRetirement();
        var close = rig.Own(rig.Source.CloseAndDrainOriginalExecutionsAsync()); rig.Expect(close);
        await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.True(actual.IsFaulted); Assert.True(close.IsCompleted);
        Assert.Throws<InvalidOperationException>((Action)(() => retained!()));
        Assert.Equal(0, rig.Tools.Validations); Assert.Equal(0, rig.Starts);
    });
    private static IEnumerable<Exception> Leaves(Exception cause) => cause is AggregateException group ? group.InnerExceptions.SelectMany(Leaves) : [cause];
    private static async Task Run(Func<Rig, Task> body)
    {
        var rig = new Rig(); var errors = new List<Exception>();
        try { await body(rig); } catch (Exception cause) { errors.Add(cause); }
        try { await rig.Source.CloseAndDrainOriginalExecutionsAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception cause) { if (!rig.ExpectedClose) errors.Add(cause); }
        foreach (var actual in rig.Originals) try { await actual; } catch (Exception cause) { if (!rig.Expected.Contains(actual)) errors.Add((Exception?)actual.Exception ?? cause); }
        try { Directory.Delete(rig.Root, true); } catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("Actual Home execution consent control/drain failed.", errors);
        // Expected negatives assert required exact causes, not absence of extra unknown cleanup causes.
    }
    private sealed class Rig
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "haven-dev-execution-" + Guid.NewGuid().ToString("N"));
        internal readonly Principal Principals = new();
        internal readonly HomeLocalProfileIdentity Profiles; internal readonly HomePermissionTrustService Permissions;
        internal readonly HomeDeveloperWorkspaceExecutionConsentSource Source; internal readonly BindingSource Bindings = new(); internal BindingSource CurrentBindings;
        internal readonly ToolSource Tools = new(); internal Binding? Binding => Bindings.Binding;
        internal readonly Action<Action> Callback = body => body();
        internal readonly List<Task> Originals = []; internal readonly HashSet<Task> Expected = new(ReferenceEqualityComparer.Instance);
        internal bool ExpectedClose; internal int Starts; internal Guid RequestId;
        internal Rig()
        {
            Directory.CreateDirectory(Root); var store = new FileHomeCoreStateStore(Path.Combine(Root, "home.json")); Profiles = new(store, Principals);
            var policy = new HomeDeveloperWorkspaceExecutionActionPolicySource(); Permissions = new(store, policy.TryGet); CurrentBindings = Bindings;
            var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(Profiles, [Bindings]), Permissions);
            Source = new(store, Profiles, broker, Permissions, () => CurrentBindings, () => Tools);
        }
        internal T Own<T>(T actual) where T : Task { Originals.Add(actual); return actual; }
        internal void Expect(Task actual) => Expected.Add(actual);
        internal async Task Prepare()
        {
            var actor = await Own(Profiles.GetCurrentAsync(default).AsTask()); Assert.NotNull(actor);
            Bindings.Binding = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Path.GetFullPath(Root), actor!); Tools.Prepare(Bindings.Binding.CanonicalRoot);
        }
        internal Task<IWorkspaceOriginalProcessStartConsent> Acquire() => Own(Source.AcquireOriginalAsync(Binding!, Tools.Preparation!, Tools.Current!, Callback, default));
        internal async Task<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest> Pending()
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < deadline)
            {
                var snapshot = await Own(Permissions.GetSnapshotAsync()); if (snapshot.PendingRequests.SingleOrDefault() is { } request) return request;
                await Task.Delay(10);
            }
            throw new TimeoutException("The genuine Home command review was not published.");
        }
        internal async Task<IWorkspaceOriginalProcessStartConsent> Accept()
        {
            await Prepare(); var original = Acquire(); var request = await Pending(); RequestId = request.RequestId;
            Assert.True((await Own(Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept))).Succeeded); return await original;
        }
    }
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        internal const string Original = "synthetic-device-local-command-review-principal";
        internal Func<CancellationToken, ValueTask<string?>>? Read;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token) => Read?.Invoke(token) ?? ValueTask.FromResult<string?>(Original);
    }
    private sealed record Binding(Guid WorkspaceId, Guid ProjectId, Guid RootId, long WorkspaceRevision, string CanonicalRoot,
        AuthenticatedResourceActor OriginalActor) : IDeveloperWorkspaceOriginalExecutionBinding;
    private sealed class BindingSource : IDeveloperWorkspaceOriginalExecutionBindingSource, ICanonicalResourceAccessResolver
    {
        internal Binding? Binding; internal Func<Task>? Validation;
        private readonly ResourceScope _scope = new("dev.workspace.execute", "synthetic-private-saved-root", "saved-root-revision-1", ResourceAccess.Execute);
        public string ResourceKind => _scope.Kind;
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token)
            => ValueTask.FromResult(new ResourceAccessDecision(actor == Binding?.OriginalActor && scope == _scope && action == HomeDeveloperWorkspaceExecutionConsentSource.ExecuteAction,
                "synthetic-private-saved-root", actor.ActorId, scope.Revision, actor.OrganisationId));
        public bool IsIssuedOriginalBinding(IDeveloperWorkspaceOriginalExecutionBinding sameBinding) => ReferenceEquals(sameBinding, Binding);
        public Task RevalidateOriginalAsync(IDeveloperWorkspaceOriginalExecutionBinding sameBinding, AuthenticatedResourceActor sameActor, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); if (!IsIssuedOriginalBinding(sameBinding) || sameActor != Binding?.OriginalActor) throw new UnauthorizedAccessException();
            return Validation?.Invoke() ?? Task.CompletedTask;
        }
        public IReadOnlyList<ResourceScope> GetOriginalExecutionScopes(IDeveloperWorkspaceOriginalExecutionBinding sameBinding)
            => IsIssuedOriginalBinding(sameBinding) ? [_scope] : throw new UnauthorizedAccessException();
        public void DemandExternalOriginalExecutionBindingJoin() { }
    }
    private sealed class ToolSource : ITaskRunToolActionOwner
    {
        internal Preparation? Preparation; internal TaskExecutionSnapshot? Current; internal int Validations;
        internal void Prepare(string root)
        {
            Current = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Synthetic Task preparation observation", TaskExecutionLifecycle.Running,
                TaskExecutionDurability.PersistedPlan, 1, [], [], [], [], null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            Preparation = new(new(Current, Guid.NewGuid(), new Lease()), root);
        }
        public bool SupportsCanonicalInvocation(ToolRuntimeKind kind, string name) => false;
        public Task<ITaskRunToolActionPreparation> PrepareOriginalAsync(TaskRunAttemptAdmission attempt, TaskExecutionSnapshot current, Guid action,
            OllamaToolCall call, ToolRuntimeKind kind, PermissionMode mode, string? root, CancellationToken token) => throw new NotSupportedException();
        public Task<TaskRunToolActionResult> ExecuteOriginalAsync(ITaskRunToolActionPreparation preparation, Func<CancellationToken, Task<WorkspaceToolResult>> body, CancellationToken token) => throw new NotSupportedException();
        public ValueTask ValidateOriginalPreparationAsync(ITaskRunToolActionPreparation preparation, TaskExecutionSnapshot current, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!ReferenceEquals(preparation, Preparation) || !ReferenceEquals(current, Current)) throw new UnauthorizedAccessException(); Validations++; return ValueTask.CompletedTask; }
        public ValueTask ValidateOriginalResultAsync(ITaskRunToolActionPreparation preparation, TaskRunToolActionResult result, CancellationToken token) => throw new NotSupportedException();
        public ValueTask RetireAcknowledgedOriginalAsync(ITaskRunToolActionPreparation preparation, TaskExecutionSnapshot current, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Preparation(TaskRunAttemptAdmission attempt, string root) : IWorkspaceToolActionPreparation
    {
        public TaskRunAttemptAdmission OriginalAttempt => attempt;
        public Guid ActionId { get; } = Guid.NewGuid();
        public TaskActionInterruptionPolicy InterruptionPolicy => TaskActionInterruptionPolicy.AtomicCommit;
        public IReadOnlyList<string> RequiredPermissionScopes => [];
        public TaskOriginalToolIntent OriginalToolIntent => new("Workspace", OriginalCall.Name, root, WorkspaceToolOriginalDigest.Call(OriginalCall));
        public string CanonicalWorkspaceRoot => root;
        public OllamaToolCall OriginalCall { get; } = new("run_command", new Dictionary<string, JsonElement> { ["command"] = JsonSerializer.SerializeToElement("synthetic-only") });
        public IWorkspaceOriginalInvocation? OriginalInvocation => null;
        public bool IsIssuedOriginalRuntime(IWorkspaceToolService service, string actualRoot, OllamaToolCall call) => false;
        public Task<WorkspaceToolResult> RunOriginalRuntimeAsync(IWorkspaceToolService service, string actualRoot, OllamaToolCall call,
            Func<CancellationToken, Task<WorkspaceToolResult>> body, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Lease : ITaskRunAdmissionLease
    {
        public TaskExecutionOwnerBinding Owner => throw new NotSupportedException();
        public Guid AttemptId => throw new NotSupportedException();
        public TaskRunRouteCandidate Candidate => throw new NotSupportedException();
        public string ReceiptReference => "synthetic-only-never-a-task-grant";
        public ValueTask RevalidateAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
