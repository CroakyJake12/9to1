using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>Independent synthetic providers/actors exercise the real authority and central evaluator.
/// These controls issue no production profile, provider credential, resource grant or cloud consent.</summary>
public sealed class TaskRunPermissionAuthorityTests
{
    [Fact]
    public async Task Current_local_selection_issues_one_attempt_without_Home_or_cloud_authority()
    {
        var rig = new Rig(); var task = await rig.StartAsync();
        var candidate = await rig.CaptureAsync(task, rig.Local.Model);
        Assert.False(candidate.UsesCloud);
        Assert.Equal("task-selected:" + task.TaskId.ToString("D"), candidate.RouteId);
        Assert.Equal(1, candidate.RouteRevision);
        var id = Guid.NewGuid(); var lease = await rig.Authority.AuthorizeAttemptAsync(task, id, candidate, null, default);
        try
        {
            Assert.Equal(id, lease.AttemptId); Assert.Equal(task.OwnerBinding, lease.Owner);
            await lease.RevalidateAsync(default); await lease.RevalidateAsync(default);
            Assert.Equal(0, rig.Cloud.Acquisitions);
        }
        finally { await lease.DisposeAsync(); }
    }

    [Fact]
    public async Task Recorded_scopes_and_candidate_labels_do_not_issue_uncaptured_model_authority()
    {
        var rig = new Rig(); var task = await rig.StartAsync();
        var fake = new TaskRunRouteCandidate("client-approved", 91, "ollama", rig.Local.Model.Name,
            null, false, ["Text"]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Authority.AuthorizeAttemptAsync(task,
            Guid.NewGuid(), fake, null, default));
        var captured = await rig.CaptureAsync(task, rig.Local.Model);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Authority.AuthorizeAttemptAsync(task,
            Guid.NewGuid(), captured with { RequiredCapabilities = ["Tools"] }, null, default));
        Assert.Equal(0, rig.Cloud.Acquisitions);
    }

    [Fact]
    public async Task Copied_task_owner_receipt_does_not_authorize_another_task_or_actor()
    {
        var rig = new Rig(); var task = await rig.StartAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.CaptureAsync(task with { TaskId = Guid.NewGuid() }, rig.Local.Model));
        rig.Actors.Actor = rig.Actors.Actor! with { AuthenticationRevision = "retired" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.CaptureAsync(task, rig.Local.Model));
    }

    [Fact]
    public async Task Central_model_rule_revocation_is_checked_again_on_the_same_live_lease()
    {
        var rig = new Rig(); var task = await rig.StartAsync();
        var candidate = await rig.Authority.CaptureSelectedRouteAsync(task, rig.Local.Model,
            [ToolCapability.Text], [RestrictedModelCapability.RunCommands]);
        var lease = await rig.Authority.AuthorizeAttemptAsync(task, Guid.NewGuid(), candidate, null, default);
        try
        {
            rig.Permissions.Policy = new([ModelPermissionRule.Create(ModelPermissionTargetKind.ExactModel,
                rig.Local.Model.Name, ModelPermissionScope.ThisDevice, RestrictedModelCapability.RunCommands)]);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => lease.RevalidateAsync(default).AsTask());
        }
        finally { await lease.DisposeAsync(); }
    }

    [Theory]
    [InlineData("size")]
    [InlineData("quantization")]
    public async Task Actual_model_identity_changes_refuse_the_captured_attempt(string changed)
    {
        var rig = new Rig(); var task = await rig.StartAsync(); var candidate = await rig.CaptureAsync(task, rig.Local.Model);
        var lease = await rig.Authority.AuthorizeAttemptAsync(task, Guid.NewGuid(), candidate, null, default);
        try
        {
            var model = rig.Local.Model.Model;
            rig.Local.Model = rig.Local.Model with { Model = changed switch
            {
                "size" => model with { SizeBytes = model.SizeBytes + 1 },
                "quantization" => model with { Quantization = "Q4" },
                _ => throw new InvalidOperationException("Unexpected synthetic identity field.")
            } };
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => lease.RevalidateAsync(default).AsTask());
        }
        finally { await lease.DisposeAsync(); }
    }

    [Fact]
    public async Task Fresh_catalogue_observation_time_is_not_an_invented_artifact_revision()
    {
        var rig = new Rig(); var task = await rig.StartAsync(); var candidate = await rig.CaptureAsync(task, rig.Local.Model);
        var lease = await rig.Authority.AuthorizeAttemptAsync(task, Guid.NewGuid(), candidate, null, default);
        try
        {
            rig.Local.Model = rig.Local.Model with { Model = rig.Local.Model.Model with { ModifiedAt = DateTimeOffset.UtcNow } };
            await lease.RevalidateAsync(default); Assert.Null(candidate.ArtifactIdentity);
            var retained = await rig.Authority.GetRetainedSelectionAsync(task, rig.Local.Model.Key, [ToolCapability.Text]);
            Assert.NotNull(retained); Assert.Equal(DateTimeOffset.UnixEpoch, retained.Model.ModifiedAt);
        }
        finally { await lease.DisposeAsync(); }
    }

    [Fact]
    public async Task Lease_rechecks_actor_after_an_actual_held_provider_catalogue_read()
    {
        var rig = new Rig(); var task = await rig.StartAsync(); var candidate = await rig.CaptureAsync(task, rig.Local.Model);
        var lease = await rig.Authority.AuthorizeAttemptAsync(task, Guid.NewGuid(), candidate, null, default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? check = null;
        try
        {
            rig.Local.HeldRead = async () => { entered.TrySetResult(); await release.Task; };
            check = lease.RevalidateAsync(default).AsTask(); await entered.Task;
            Assert.False(check.IsCompleted);
            rig.Actors.Actor = rig.Actors.Actor! with { AuthenticationRevision = "changed-during-read" };
            release.TrySetResult();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => check!);
        }
        finally
        {
            release.TrySetResult();
            if (check is not null) { try { await check; } catch (UnauthorizedAccessException) { } }
            await lease.DisposeAsync();
        }
    }

    [Fact]
    public async Task Local_to_cloud_checks_the_original_provider_fallback_policy_and_a_real_owned_permit()
    {
        var rig = new Rig(); var task = await rig.StartAsync();
        var first = await rig.CaptureAsync(task, rig.Local.Model); var previousId = Guid.NewGuid();
        var previous = await rig.Authority.AuthorizeAttemptAsync(task, previousId, first, null, default);
        await previous.DisposeAsync();
        var failed = task with { Attempts = [new(previousId, first, TaskRunAttemptState.Failed,
            previous.ReceiptReference, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)] };
        var cloud = await rig.CaptureAsync(failed, rig.Remote.Model);
        Assert.Equal(first.RouteId, cloud.RouteId); Assert.Equal(first.RouteRevision, cloud.RouteRevision);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Authority.AuthorizeAttemptAsync(failed,
            Guid.NewGuid(), cloud, previousId, default));
        Assert.Equal(0, rig.Cloud.Acquisitions);
        rig.Configurations.Rows[rig.Local.Id] = rig.Configurations.Rows[rig.Local.Id] with { AllowCloudFallback = true };
        var actual = await rig.Authority.AuthorizeAttemptAsync(failed, Guid.NewGuid(), cloud, previousId, default);
        try { Assert.Equal(1, rig.Cloud.Acquisitions); Assert.Equal(0, rig.Cloud.Lease.Closes); }
        finally { await actual.DisposeAsync(); }
        Assert.Equal(1, rig.Cloud.Lease.Closes);
    }

    [Fact]
    public async Task Remote_route_observation_without_a_genuine_permit_stops_before_dispatch()
    {
        var rig = new Rig(includeCloud: false); var task = await rig.StartAsync();
        var candidate = await rig.CaptureAsync(task, rig.Remote.Model);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Authority.AuthorizeAttemptAsync(task,
            Guid.NewGuid(), candidate, null, default));
        Assert.Equal(0, rig.Cloud.Acquisitions);
        rig.Privacy.Current = rig.Privacy.Current with { LocalOnlyMode = true };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.CaptureAsync(task, rig.Remote.Model));
    }

    [Fact]
    public async Task Attempt_close_is_the_same_task_and_waits_for_the_actual_original_commit_pin()
    {
        var rig = new Rig(); var task = await rig.StartAsync(); var candidate = await rig.CaptureAsync(task, rig.Local.Model);
        var lease = await rig.Authority.AuthorizeAttemptAsync(task, Guid.NewGuid(), candidate, null, default);
        var pin = await Assert.IsAssignableFrom<ITaskRunAdmissionCommitLease>(lease).AcquireOriginalCommitPinAsync(default);
        Assert.NotNull(pin);
        Task? close = null;
        try
        {
            close = lease.DisposeAsync().AsTask();
            Assert.Same(close, lease.DisposeAsync().AsTask()); Assert.False(close.IsCompleted);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => lease.RevalidateAsync(default).AsTask());
        }
        finally { await pin!.DisposeAsync(); if (close is not null) await close; }
        Assert.Null(await ((ITaskRunAdmissionCommitLease)lease).AcquireOriginalCommitPinAsync(default));
    }

    [Fact]
    public async Task Compound_original_cloud_close_faults_are_retained_without_releasing_success()
    {
        var rig = new Rig(); var task = await rig.StartAsync(); var candidate = await rig.CaptureAsync(task, rig.Remote.Model);
        var lease = await rig.Authority.AuthorizeAttemptAsync(task, Guid.NewGuid(), candidate, null, default);
        var first = new IOException("synthetic owned close one"); var second = new InvalidOperationException("synthetic owned close two");
        var original = Task.WhenAll(Task.FromException(first), Task.FromException(second));
        rig.Cloud.Lease.OriginalClose = original;
        var close = lease.DisposeAsync().AsTask(); var error = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Same(error, close.Exception!.InnerExceptions[0]); Assert.Same(first, error.InnerExceptions[0]);
        Assert.Same(second, error.InnerExceptions[1]); Assert.Same(close, lease.DisposeAsync().AsTask());
        Assert.Equal(1, rig.Cloud.Lease.Closes);
    }

    [Fact]
    public async Task Configured_route_revision_is_rechecked_in_the_actual_owner_reader()
    {
        var routes = new Routes(); var rig = new Rig(routes: routes); var task = await rig.StartAsync();
        var candidate = await rig.CaptureAsync(task, rig.Local.Model);
        Assert.Equal("real-configured-route", candidate.RouteId); Assert.Equal(12, candidate.RouteRevision);
        var lease = await rig.Authority.AuthorizeAttemptAsync(task, Guid.NewGuid(), candidate, null, default);
        try { routes.Revision++; await Assert.ThrowsAsync<UnauthorizedAccessException>(() => lease.RevalidateAsync(default).AsTask()); }
        finally { await lease.DisposeAsync(); }
    }

    [Fact]
    public async Task Persisted_task_command_requires_fresh_real_actor_even_without_old_in_memory_issuer()
    {
        var original = new Rig(); var task = await original.StartAsync();
        var restarted = new Rig();
        await restarted.Authority.ValidateTaskCommandAsync(task, "task:queue:edit", default);
        restarted.Actors.Actor = restarted.Actors.Actor! with { AuthenticationRevision = "new-host-activation" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => restarted.Authority.ValidateTaskCommandAsync(task, "task:steer", default));
        // Current command authentication does not reconstruct an attempt/model capability grant.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => restarted.CaptureAsync(task, restarted.Local.Model));
    }

    [Fact]
    public async Task Concurrent_held_first_route_cannot_publish_an_incompatible_original_route_after_another_capture_wins()
    {
        var routes = new ConcurrentRoutes(); var rig = new Rig(routes: routes); var task = await rig.StartAsync();
        var local = rig.CaptureAsync(task, rig.Local.Model);
        try
        {
            await routes.Entered.Task;
            var actualFirst = await rig.CaptureAsync(task, rig.Remote.Model);
            Assert.Equal("remote-original", actualFirst.RouteId);
            routes.Release.TrySetResult();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => local);
            var retained = await rig.Authority.GetRetainedSelectionAsync(task, rig.Local.Model.Key, [ToolCapability.Text]);
            Assert.Null(retained);
        }
        finally { routes.Release.TrySetResult(); try { await local; } catch (UnauthorizedAccessException) { } }
    }

    private sealed class ConcurrentRoutes : ITaskRunRouteObservationSource
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<TaskRunRouteCandidate?> ObserveOriginalAsync(TaskExecutionSnapshot snapshot,
            ProviderModelDescriptor model, IReadOnlyList<string> requirements, CancellationToken token)
        {
            if (model.IsLocal) { Entered.TrySetResult(); await Release.Task; }
            token.ThrowIfCancellationRequested();
            return new(model.IsLocal ? "local-original" : "remote-original", model.IsLocal ? 41 : 42,
                model.ProviderId, model.Name, null, !model.IsLocal, requirements);
        }
        public ValueTask DemandOriginalCurrentAsync(TaskExecutionSnapshot snapshot, TaskRunRouteCandidate observation, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
    }

    private sealed class Rig
    {
        public Actors Actors { get; } = new(); public Provider Local { get; } = new("ollama", true);
        public Provider Remote { get; } = new("synthetic-remote", false);
        public Configurations Configurations { get; } = new(); public Privacy Privacy { get; } = new();
        public Permissions Permissions { get; } = new(); public Cloud Cloud { get; } = new();
        public TaskRunPermissionAuthority Authority { get; }
        public Rig(bool includeCloud = true, ITaskRunRouteObservationSource? routes = null)
        {
            foreach (var provider in new[] { Local, Remote }) Configurations.Rows[provider.Id] = new(provider.Id,
                provider.Kind, provider.DisplayName, provider.IsLocal ? "http://127.0.0.1:11434" : "https://synthetic.invalid",
                true, provider.IsLocal, false, new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
            Authority = new(Actors, new Registry(Local, Remote), Configurations, Privacy, new(Permissions),
                includeCloud ? Cloud : null, routes: routes);
        }
        public async Task<TaskExecutionSnapshot> StartAsync()
        {
            var task = new TaskExecutionSnapshot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Synthetic task",
                TaskExecutionLifecycle.Running, TaskExecutionDurability.PersistedPlan, 1, [], [], [],
                ["client-claimed-all-scopes"], null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
            return task with { OwnerBinding = await Authority.AuthorizeStartAsync(task, default) };
        }
        public Task<TaskRunRouteCandidate> CaptureAsync(TaskExecutionSnapshot task, ProviderModelDescriptor model) =>
            Authority.CaptureSelectedRouteAsync(task, model, [ToolCapability.Text], []);
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Actor = new("synthetic-product-owner", "synthetic-profile", null, null, "revision-one");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(Actor); }
    }
    private sealed class Provider(string id, bool local) : IModelProvider
    {
        public string Id => id; public string DisplayName => id; public bool IsLocal => local; public bool CanManageModels => false;
        public ModelProviderKind Kind => local ? ModelProviderKind.Ollama : ModelProviderKind.OpenAI;
        public ProviderModelDescriptor Model = new(id, local, new ModelDescriptor("synthetic-model", 123, "synthetic", "7B", "Q8",
            new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UnixEpoch));
        public Func<Task>? HeldRead;
        public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        { if (HeldRead is { } read) await read(); token.ThrowIfCancellationRequested(); return [Model]; }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => Task.FromResult(new ProviderHealthStatus(id, true, "synthetic", TimeSpan.Zero, DateTimeOffset.UnixEpoch));
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Registry(params IModelProvider[] providers) : IModelProviderRegistry
    {
        public IReadOnlyList<IModelProvider> Providers => providers;
        public IModelProvider? Find(string id) => providers.SingleOrDefault(provider => provider.Id == id);
        public IModelProvider GetRequired(string id) => Find(id) ?? throw new InvalidOperationException();
        public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        { var result = new List<ProviderModelDescriptor>(); foreach (var provider in providers) result.AddRange(await provider.GetModelsAsync(token)); return result; }
    }
    private sealed class Configurations : IProviderConfigurationStore
    {
        public readonly Dictionary<string, ProviderConfiguration> Rows = new(StringComparer.Ordinal);
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) => Task.FromResult(Rows.GetValueOrDefault(id));
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ProviderConfiguration>>(Rows.Values.ToArray());
        public Task UpsertAsync(ProviderConfiguration config, CancellationToken token) { Rows[config.Id] = config; return Task.CompletedTask; }
        public Task DeleteAsync(string id, CancellationToken token) { Rows.Remove(id); return Task.CompletedTask; }
    }
    private sealed class Privacy : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; set; } = PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences value, CancellationToken token) { Current = value; return Task.CompletedTask; }
    }
    private sealed class Permissions : IModelPermissionStore
    {
        public ModelPermissionPolicy Policy = ModelPermissionPolicy.Empty;
        public Task<ModelPermissionPolicy> GetPolicyAsync(CancellationToken token) => Task.FromResult(Policy);
        public Task SavePolicyAsync(ModelPermissionPolicy value, CancellationToken token) { Policy = value; return Task.CompletedTask; }
    }
    private sealed class Cloud : ITaskRunCloudAdmissionSource
    {
        public readonly CloudLease Lease = new(); public int Acquisitions;
        public ValueTask<ITaskRunCloudAdmissionLease?> AcquireOriginalAsync(TaskExecutionOwnerBinding owner,
            ProviderModelDescriptor model, ProviderConfiguration configuration, TaskRunRouteCandidate candidate, CancellationToken token)
        { Acquisitions++; return ValueTask.FromResult<ITaskRunCloudAdmissionLease?>(Lease); }
    }
    private sealed class CloudLease : ITaskRunCloudAdmissionLease
    {
        public int Closes; public Task OriginalClose = Task.CompletedTask;
        public ValueTask RevalidateAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() { Closes++; return new(OriginalClose); }
    }
    private sealed class Routes : ITaskRunRouteObservationSource
    {
        public long Revision = 12;
        public ValueTask<TaskRunRouteCandidate?> ObserveOriginalAsync(TaskExecutionSnapshot task,
            ProviderModelDescriptor model, IReadOnlyList<string> required, CancellationToken token) =>
            ValueTask.FromResult<TaskRunRouteCandidate?>(new("real-configured-route", Revision, model.ProviderId, model.Name, null, !model.IsLocal, required));
        public ValueTask DemandOriginalCurrentAsync(TaskExecutionSnapshot task, TaskRunRouteCandidate original, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (original.RouteRevision != Revision) throw new UnauthorizedAccessException("synthetic route changed"); return ValueTask.CompletedTask; }
    }
}
