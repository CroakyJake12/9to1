using Haven.Application;
using Haven.Core;
namespace Haven.Core.Tests;

public sealed partial class TaskRunPermissionAuthorityTests
{
    [Theory]
    [InlineData("write_file", RestrictedModelCapability.EditFiles)]
    [InlineData("run_command", RestrictedModelCapability.RunCommands)]
    public async Task Fresh_typed_tool_rule_denies_even_when_original_selection_captured_no_restrictions(string tool, RestrictedModelCapability capability)
    {
        var rig = new Rig(); var task = await rig.StartAsync(); var candidate = await rig.CaptureAsync(task, rig.Local.Model);
        var lease = await rig.Authority.AuthorizeAttemptAsync(task, Guid.NewGuid(), candidate, null, default);
        var admission = new TaskRunAttemptAdmission(task, lease.AttemptId, lease);
        try
        {
            rig.Permissions.Policy = new([ModelPermissionRule.Create(ModelPermissionTargetKind.ExactModel, rig.Local.Model.Name, ModelPermissionScope.ThisDevice, capability)]);
            await lease.RevalidateAsync(default); // The old empty restriction set independently remains healthy.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Authority.ValidateOriginalToolAsync(admission, tool, default));
        }
        finally { await lease.DisposeAsync(); }
    }
    [Fact]
    public async Task Allowed_local_typed_tool_uses_original_model_owner_without_Home_or_cloud()
    {
        var rig = new Rig(); var task = await rig.StartAsync(); var candidate = await rig.CaptureAsync(task, rig.Local.Model);
        var lease = await rig.Authority.AuthorizeAttemptAsync(task, Guid.NewGuid(), candidate, null, default);
        try
        {
            await rig.Authority.ValidateOriginalToolAsync(new(task, lease.AttemptId, lease), "run_command", default);
            Assert.Equal(0, rig.Cloud.Acquisitions);
        }
        finally { await lease.DisposeAsync(); }
    }
    [Fact]
    public async Task Copied_attempt_identity_does_not_expand_tool_permissions()
    {
        var rig = new Rig(); var task = await rig.StartAsync(); var candidate = await rig.CaptureAsync(task, rig.Local.Model);
        var lease = await rig.Authority.AuthorizeAttemptAsync(task, Guid.NewGuid(), candidate, null, default);
        try { await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Authority.ValidateOriginalToolAsync(new(task, Guid.NewGuid(), lease), "run_command", default)); }
        finally { await lease.DisposeAsync(); }
    }
    [Fact]
    public async Task Retired_original_lease_cannot_validate_a_new_typed_tool()
    {
        var rig = new Rig(); var task = await rig.StartAsync(); var candidate = await rig.CaptureAsync(task, rig.Local.Model);
        var lease = await rig.Authority.AuthorizeAttemptAsync(task, Guid.NewGuid(), candidate, null, default);
        await lease.DisposeAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Authority.ValidateOriginalToolAsync(new(task, lease.AttemptId, lease), "write_file", default));
    }
    [Fact]
    public async Task Actual_policy_faulted_OCE_and_sibling_are_retained_before_tool_admission()
    {
        var source = new ToolPolicySource(); var rig = new Rig();
        var authority = new TaskRunPermissionAuthority(rig.Actors, new Registry(rig.Local, rig.Remote), rig.Configurations, rig.Privacy, new(source));
        var proposed = new TaskExecutionSnapshot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Synthetic", TaskExecutionLifecycle.Running, TaskExecutionDurability.PersistedPlan, 1, [], [], [], [], null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var task = proposed with { OwnerBinding = await authority.AuthorizeStartAsync(proposed, default) };
        var route = await authority.CaptureSelectedRouteAsync(task, rig.Local.Model, [ToolCapability.Text], []);
        var lease = await authority.AuthorizeAttemptAsync(task, Guid.NewGuid(), route, null, default);
        var first = new OperationCanceledException("synthetic actual model policy fault"); var second = new IOException("synthetic direct sibling");
        var actual = new TaskCompletionSource<ModelPermissionPolicy>(TaskCreationOptions.RunContinuationsAsynchronously); actual.SetException(new Exception[] { first, second }); source.Original = actual.Task;
        try
        {
            var check = authority.ValidateOriginalToolAsync(new(task, lease.AttemptId, lease), "run_command", default);
            var error = await Assert.ThrowsAsync<AggregateException>(() => check);
            Assert.True(check.IsFaulted); Assert.Same(first, error.InnerExceptions[0]); Assert.Same(second, error.InnerExceptions[1]);
            Assert.Same(actual.Task, source.LastReturned);
        }
        finally { await lease.DisposeAsync(); }
    }
    [Fact]
    public async Task Held_policy_denial_before_native_admission_is_observed()
    {
        var source = new ToolPolicySource(); var rig = new Rig();
        var authority = new TaskRunPermissionAuthority(rig.Actors, new Registry(rig.Local, rig.Remote), rig.Configurations, rig.Privacy, new(source));
        var proposed = new TaskExecutionSnapshot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Synthetic", TaskExecutionLifecycle.Running, TaskExecutionDurability.PersistedPlan, 1, [], [], [], [], null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var task = proposed with { OwnerBinding = await authority.AuthorizeStartAsync(proposed, default) };
        var route = await authority.CaptureSelectedRouteAsync(task, rig.Local.Model, [ToolCapability.Text], []);
        var lease = await authority.AuthorizeAttemptAsync(task, Guid.NewGuid(), route, null, default);
        var actual = new TaskCompletionSource<ModelPermissionPolicy>(TaskCreationOptions.RunContinuationsAsynchronously); source.Original = actual.Task;
        Task? check = null;
        try
        {
            check = authority.ValidateOriginalToolAsync(new(task, lease.AttemptId, lease), "run_command", default);
            Assert.False(check.IsCompleted);
            actual.SetResult(new([ModelPermissionRule.Create(ModelPermissionTargetKind.ExactModel, rig.Local.Model.Name, ModelPermissionScope.ThisDevice, RestrictedModelCapability.RunCommands)]));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => check);
        }
        finally { actual.TrySetResult(ModelPermissionPolicy.Empty); if (check is not null) try { await check; } catch (UnauthorizedAccessException) { } await lease.DisposeAsync(); }
    }
    private sealed class ToolPolicySource : IModelPermissionStore
    {
        public Task<ModelPermissionPolicy> Original = Task.FromResult(ModelPermissionPolicy.Empty);
        public Task<ModelPermissionPolicy>? LastReturned;
        public Task<ModelPermissionPolicy> GetPolicyAsync(CancellationToken token) { LastReturned = Original; return Original; }
        public Task SavePolicyAsync(ModelPermissionPolicy policy, CancellationToken token) => throw new NotSupportedException();
    }
}
