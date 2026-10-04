using System.Collections.Frozen;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Dulche.Runtime.Agents;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Real Home ownership, held lease, durable permission broker and original Den run store.
/// The model inventory and controlled read wrapper are synthetic; no model, native display,
/// transport, DI or external tool execution is inferred from these source-only fixtures.</summary>
public sealed class HomeAgentExecutionAdmissionsTests
{
    [Fact]
    public Task Personal_ownership_without_the_actual_Home_decision_cannot_admit_execution() => WithFixture(async f =>
    {
        var preparation = await f.Issuer.PrepareAsync(f.Reference, Fixture.Objective, f.Token);
        var pending = await Assert.ThrowsAsync<HomeAgentPermissionRequiredException>(() => f.Issuer.AdmitAsync(preparation, f.Token));
        Assert.Equal(preparation.RequestId, pending.RequestId);
        var request = Assert.IsType<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest>(
            await f.Permissions.ReadRequestObservationAsync(preparation.RequestId, f.Token));
        Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
        Assert.True(request.Policy.RequiresPerActionApproval);
        Assert.Null(request.AppliedGrantId);
        Assert.False(await f.Personal.Den.AccessPolicy.IsAllowedAsync(f.Personal.Actor.ActorId, "personal", f.Definition.Id, DenPermission.Execute, f.Token));
        Assert.Empty(await f.Personal.Den.ListAsync<AgentRunRecord>("personal", f.Token));
    });

    [Fact]
    public Task Same_real_decision_admits_only_the_private_context_and_exact_saved_Agent() => WithFixture(async f =>
    {
        var admitted = await f.AdmitAsync();
        var policy = new AgentCapabilityPolicy(new HashSet<string>(), new HashSet<string>());
        Assert.Null((await f.Issuer.ResolveCapabilitiesAsync(policy, admitted.Context, f.Token)).Error);
        Assert.NotNull((await f.Issuer.ResolveCapabilitiesAsync(policy, admitted.Context with { }, f.Token)).Error);
        Assert.True(await admitted.Den.AccessPolicy.IsAllowedAsync(f.Personal.Actor.ActorId, "personal", f.Definition.Id, DenPermission.Execute, f.Token));
        Assert.False(await admitted.Den.AccessPolicy.IsAllowedAsync(f.Personal.Actor.ActorId, "personal", Guid.NewGuid().ToString("D"), DenPermission.Execute, f.Token));
        Assert.False(await admitted.Den.AccessPolicy.IsAllowedAsync(f.Personal.Actor.ActorId, "personal", f.Definition.Id, DenPermission.Administer, f.Token));
        Assert.False(await f.Personal.Den.AccessPolicy.IsAllowedAsync(f.Personal.Actor.ActorId, "personal", f.Definition.Id, DenPermission.Execute, f.Token));
        Assert.True(admitted.OriginalLifetime.CanBeCanceled);
        Assert.False(admitted.OriginalLifetime.IsCancellationRequested);
        Assert.Equal(HomePermissionRequestState.Executing,
            (await f.Permissions.ReadRequestObservationAsync(f.Preparation!.RequestId, f.Token))!.State);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.AdmitAsync(f.Preparation!, f.Token));
    });

    [Fact]
    public Task Actual_Den_run_and_same_step_get_a_private_token_while_matching_copies_fail() => WithFixture(async f =>
    {
        var (admitted, state, run, step) = await f.StartStepAsync();
        Assert.NotNull((await f.Issuer.BindOriginalRunAsync(admitted.Context with { }, run, f.Token)).Error);
        Assert.NotNull((await f.Issuer.GetOriginalStepAdmissionAsync(step with { }, f.Token)).Error);
        var observation = await f.Issuer.GetOriginalStepAdmissionAsync(step, f.Token);
        Assert.Null(observation.Error);
        var authority = Assert.IsType<AgentStepAdmission>(observation.Value);
        await f.Issuer.DemandCurrentAsync(authority.OriginalExecutionAuthority, Guid.Parse(run.SessionId), "exact-local-model", null, f.Token);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.DemandCurrentAsync(new object(), Guid.Parse(run.SessionId), "exact-local-model", null, f.Token).AsTask());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.DemandCurrentAsync(authority.OriginalExecutionAuthority, Guid.NewGuid(), "exact-local-model", null, f.Token).AsTask());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.DemandCurrentAsync(authority.OriginalExecutionAuthority, Guid.Parse(run.SessionId), "other-model", null, f.Token).AsTask());
        Assert.Equal(run.AgentRunId, (await state.ReadAsync(run.AgentRunId, f.Token))!.Run.AgentRunId);
        Assert.Single(await f.Personal.Den.ListAsync<AgentRunRecord>("personal", f.Token));
    });

    [Fact]
    public Task Actual_caller_block_retires_the_original_step_without_another_grant_or_run() => WithFixture(async f =>
    {
        var (_, _, run, step) = await f.StartStepAsync();
        var admission = Assert.IsType<AgentStepAdmission>((await f.Issuer.GetOriginalStepAdmissionAsync(step, f.Token)).Value);
        Assert.True((await f.Permissions.BlockCallerAsync(f.Personal.Actor.ActorId, f.Token)).Succeeded);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.DemandCurrentAsync(admission.OriginalExecutionAuthority, Guid.Parse(run.SessionId), "exact-local-model", null, f.Token).AsTask());
        Assert.Single(await f.Personal.Den.ListAsync<AgentRunRecord>("personal", f.Token));
    });

    [Fact]
    public Task Actual_authentication_revision_change_denies_retained_context() => WithFixture(async f =>
    {
        var admitted = await f.AdmitAsync();
        f.Actors.Changed = f.Personal.Actor with { AuthenticationRevision = "retired-original-authentication" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.ResolveCapabilitiesAsync(
            new(new HashSet<string>(), new HashSet<string>()), admitted.Context, f.Token).AsTask());
    });

    [Fact]
    public Task Actual_saved_definition_revision_change_denies_retained_step() => WithFixture(async f =>
    {
        var (_, _, run, step) = await f.StartStepAsync();
        var admission = Assert.IsType<AgentStepAdmission>((await f.Issuer.GetOriginalStepAdmissionAsync(step, f.Token)).Value);
        await f.Personal.Den.SaveAsync(f.Definition with { Instructions = "new canonical instruction" }, f.Definition.Revision, "replace-original-definition", f.Token);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.DemandCurrentAsync(admission.OriginalExecutionAuthority, Guid.Parse(run.SessionId), "exact-local-model", null, f.Token).AsTask());
        Assert.Single(await f.Personal.Den.ListAsync<AgentRunRecord>("personal", f.Token));
    });

    [Fact]
    public Task Zero_actual_call_budget_denies_before_the_owning_tool_policy_or_any_new_request() => WithFixture(async f =>
    {
        var (_, _, run, step) = await f.StartStepAsync();
        var admission = Assert.IsType<AgentStepAdmission>((await f.Issuer.GetOriginalStepAdmissionAsync(step, f.Token)).Value);
        var before = await f.Permissions.GetSnapshotAsync(cancellationToken: f.Token);
        var call = new OllamaToolCall("workspace_write_file", new Dictionary<string, JsonElement>());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.DemandCurrentAsync(admission.OriginalExecutionAuthority, Guid.Parse(run.SessionId), "exact-local-model", call, f.Token).AsTask());
        Assert.Equal(0, f.Tools.Resolutions);
        var after = await f.Permissions.GetSnapshotAsync(cancellationToken: f.Token);
        Assert.Equal(before.PendingRequests, after.PendingRequests);
        Assert.Equal(before.RecentAuditEvents, after.RecentAuditEvents);
        await f.Issuer.DemandCurrentAsync(admission.OriginalExecutionAuthority, Guid.Parse(run.SessionId), "exact-local-model", null, f.Token);
    });

    [Fact]
    public Task Held_lease_retirement_denies_the_original_step() => WithFixture(async f =>
    {
        var (_, _, run, step) = await f.StartStepAsync();
        var admission = Assert.IsType<AgentStepAdmission>((await f.Issuer.GetOriginalStepAdmissionAsync(step, f.Token)).Value);
        f.Lease.Dispose();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.DemandCurrentAsync(admission.OriginalExecutionAuthority, Guid.Parse(run.SessionId), "exact-local-model", null, f.Token).AsTask());
    });

    [Fact]
    public Task Shutdown_awaits_the_same_original_read_cleanup_before_retiring_issuer_and_audit() => WithFixture(async f =>
    {
        var admitted = await f.AdmitAsync();
        var writer = new DenAgentExecutionStateStore(admitted.Den, "personal");
        var run = Run(f, AgentRunState.Queued);
        var committed = await writer.CommitAsync(new(run.AgentRunId, 0, "original-run-create", Run: run), f.Token);
        f.State.HoldNextRead = true;
        var original = f.Issuer.BindOriginalRunAsync(admitted.Context, committed.Run, f.Token).AsTask();
        Exception? primary = null;
        Task? close = null;
        OperationCanceledException? observedCancellation = null;
        var failures = new List<Exception>();
        try
        {
            await f.State.ReadEntered.Task.WaitAsync(f.Token);
            close = f.Issuer.DisposeAsync().AsTask();
            Assert.Same(close, f.Issuer.DisposeAsync().AsTask());
            await f.State.ReadCleanupEntered.Task.WaitAsync(f.Token);
            Assert.False(original.IsCompleted);
            Assert.False(close.IsCompleted);
            f.State.ReleaseCleanup.TrySetResult();
            observedCancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original);
            await close.WaitAsync(f.Token);
            Assert.True(original.IsCompleted);
            Assert.True(admitted.OriginalLifetime.IsCancellationRequested);
            Assert.Equal(HomePermissionRequestState.PartiallyCompleted,
                (await f.Permissions.ReadRequestObservationAsync(f.Preparation!.RequestId, f.Token))!.State);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            f.State.ReleaseCleanup.TrySetResult();
            try { await original.WaitAsync(TimeSpan.FromSeconds(20), CancellationToken.None); }
            catch (OperationCanceledException error) when (ReferenceEquals(error, observedCancellation)) { }
            catch (Exception error) { Add(failures, error); }
            if (close is not null) try { await close.WaitAsync(TimeSpan.FromSeconds(20), CancellationToken.None); } catch (Exception error) { Add(failures, error); }
        }
        Rethrow(primary, failures);
    });

    [Fact]
    public Task Exact_issuer_thrown_pending_request_can_pause_while_copied_exception_and_decided_request_refuse() => WithFixture(async f =>
    {
        var (_, _, run, step) = await f.StartStepAsync();
        var admission = Assert.IsType<AgentStepAdmission>((await f.Issuer.GetOriginalStepAdmissionAsync(step, f.Token)).Value);
        f.Tools.PendingRead = true;
        var call = new OllamaToolCall("fixture_read", new Dictionary<string, JsonElement>());
        var original = await Assert.ThrowsAsync<HomeAgentPermissionRequiredException>(() => f.Issuer.DemandCurrentAsync(
            admission.OriginalExecutionAuthority, Guid.Parse(run.SessionId), "exact-local-model", call, f.Token).AsTask());
        var lookup = await f.Issuer.GetOriginalApprovalAsync(step, original, f.Token);
        Assert.Null(lookup.Error);
        var approval = Assert.IsType<AgentApprovalRequirement>(lookup.Value);
        Assert.Equal(original.RequestId, approval.ApprovalId);
        Assert.Equal(run.AgentRunId, approval.AgentRunId);
        Assert.Equal("Pending", approval.State);
        Assert.NotNull((await f.Issuer.GetOriginalApprovalAsync(step with { }, original, f.Token)).Error);
        Assert.NotNull((await f.Issuer.GetOriginalApprovalAsync(step, new HomeAgentPermissionRequiredException(original.RequestId), f.Token)).Error);
        Assert.NotNull((await f.Issuer.GetOriginalApprovalAsync(step, new AggregateException(original), f.Token)).Error);
        Assert.True((await f.Permissions.DecideAsync(original.RequestId, HomeApprovalChoice.Accept, cancellationToken: f.Token)).Succeeded);
        Assert.NotNull((await f.Issuer.GetOriginalApprovalAsync(step, original, f.Token)).Error);
        Assert.Equal(HomePermissionRequestState.Approved, (await f.Permissions.ReadRequestObservationAsync(original.RequestId, f.Token))!.State);
    }, maximumCalls: 1);

    [Fact]
    public Task Original_host_lifetime_cancellation_is_carried_into_the_private_step_token() => WithFixture(async f =>
    {
        var (_, _, run, step) = await f.StartStepAsync();
        var admission = Assert.IsType<AgentStepAdmission>((await f.Issuer.GetOriginalStepAdmissionAsync(step, f.Token)).Value);
        f.Lifetime.Cancel();
        Assert.True(admission.OriginalLifetime.IsCancellationRequested);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Issuer.DemandCurrentAsync(
            admission.OriginalExecutionAuthority, Guid.Parse(run.SessionId), "exact-local-model", null, CancellationToken.None).AsTask());
    });

    [Fact]
    public Task Actual_current_Text_feature_retirement_denies_the_same_bound_run_and_token() => WithFixture(async f =>
    {
        var (_, state, run, step) = await f.StartStepAsync();
        var admission = Assert.IsType<AgentStepAdmission>((await f.Issuer.GetOriginalStepAdmissionAsync(step, f.Token)).Value);
        Assert.True(f.Models.Features.Remove(ToolCapability.Text));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.DemandCurrentAsync(
            admission.OriginalExecutionAuthority, Guid.Parse(run.SessionId), "exact-local-model", null, f.Token).AsTask());
        Assert.Equal(run.AgentRunId, (await state.ReadAsync(run.AgentRunId, f.Token))!.Run.AgentRunId);
        Assert.Equal(0, f.Tools.Resolutions);
        Assert.Single(await f.Personal.Den.ListAsync<AgentRunRecord>("personal", f.Token));
    });

    [Fact]
    public Task Actual_saved_required_feature_retirement_denies_without_provider_or_model_identity_change() => WithFixture(async f =>
    {
        var (_, _, run, step) = await f.StartStepAsync();
        var admission = Assert.IsType<AgentStepAdmission>((await f.Issuer.GetOriginalStepAdmissionAsync(step, f.Token)).Value);
        Assert.True(f.Models.Features.Remove(ToolCapability.Vision));
        Assert.Contains(ToolCapability.Text, f.Models.Features);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.DemandCurrentAsync(
            admission.OriginalExecutionAuthority, Guid.Parse(run.SessionId), "exact-local-model", null, f.Token).AsTask());
        Assert.Equal(0, f.Tools.Resolutions);
    }, requiredFeature: "Vision");

    [Theory]
    [InlineData("vision")]
    [InlineData("1")]
    [InlineData("Unsupported")]
    public Task Noncanonical_saved_feature_tokens_refuse_the_actual_committed_run(string requiredFeature) => WithFixture(async f =>
    {
        var admitted = await f.AdmitAsync();
        var writer = new DenAgentExecutionStateStore(admitted.Den, "personal");
        var run = Run(f, AgentRunState.Queued);
        var committed = await writer.CommitAsync(new(run.AgentRunId, 0, "unknown-feature-run", Run: run), f.Token);
        Assert.NotNull((await f.Issuer.BindOriginalRunAsync(admitted.Context, committed.Run, f.Token)).Error);
        Assert.Equal(0, f.Tools.Resolutions);
        Assert.Single(await f.Personal.Den.ListAsync<AgentRunRecord>("personal", f.Token));
    }, requiredFeature: requiredFeature);

    [Fact]
    public Task Genuine_fresh_Empty_time_admits_then_original_elapsed_limit_and_unknown_or_prior_usage_refuse() => WithFixture(async f =>
    {
        var (_, state, run, step) = await f.StartStepAsync();
        Assert.Equal(UsageAvailability.Empty, run.BudgetUsage.Time.Availability);
        Assert.Null(run.BudgetUsage.Time.Value);
        var admission = Assert.IsType<AgentStepAdmission>((await f.Issuer.GetOriginalStepAdmissionAsync(step, f.Token)).Value);
        await f.Issuer.DemandCurrentAsync(admission.OriginalExecutionAuthority, Guid.Parse(run.SessionId), "exact-local-model", null, f.Token);
        var original = Assert.IsType<AgentExecutionSnapshot>(await state.ReadAsync(run.AgentRunId, f.Token));
        var unavailable = await state.CommitAsync(new(run.AgentRunId, original.Revision, "time-provider-unavailable",
            Run: original.Run with { Revision = original.Run.Revision + 1,
                BudgetUsage = original.Run.BudgetUsage with { Time = UsageValue<TimeSpan>.Unavailable("actual missing measurement") } }), f.Token);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.DemandCurrentAsync(
            admission.OriginalExecutionAuthority, Guid.Parse(run.SessionId), "exact-local-model", null, f.Token).AsTask());
        var prior = await state.CommitAsync(new(run.AgentRunId, unavailable.Revision, "noninitial-empty-time",
            Run: original.Run with { Revision = unavailable.Run.Revision + 1,
                BudgetUsage = AgentBudgetUsage.Empty with { ToolCalls = UsageValue<long>.Measured(1) } }), f.Token);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.DemandCurrentAsync(
            admission.OriginalExecutionAuthority, Guid.Parse(run.SessionId), "exact-local-model", null, f.Token).AsTask());
        await state.CommitAsync(new(run.AgentRunId, prior.Revision, "restore-original-initial-usage",
            Run: original.Run with { Revision = prior.Run.Revision + 1 }), f.Token);
        // This is the SAME privately admitted step and its real Stopwatch interval.
        await Task.Delay(TimeSpan.FromMilliseconds(5200), f.Token);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.DemandCurrentAsync(
            admission.OriginalExecutionAuthority, Guid.Parse(run.SessionId), "exact-local-model", null, f.Token).AsTask());
        Assert.Equal(0, f.Tools.Resolutions);
        Assert.Single(await f.Personal.Den.ListAsync<AgentRunRecord>("personal", f.Token));
    }, maximumTime: TimeSpan.FromSeconds(5));

    [Fact]
    public Task Original_inherited_route_preview_retains_its_snapshot_but_final_current_feature_retirement_refuses() => WithFixture(async f =>
    {
        var admitted = await f.AdmitAsync();
        var writer = new DenAgentExecutionStateStore(admitted.Den, "personal");
        var run = Run(f, AgentRunState.Queued);
        var committed = await writer.CommitAsync(new(run.AgentRunId, 0, "original-inherited-route-create", Run: run), f.Token);
        // Read1 is the issuer's detached inventory; Read2 is the actual maintained
        // route preview, which returns its already captured synthetic inventory.
        f.Models.HoldAtRead = 2;
        var original = f.Issuer.BindOriginalRunAsync(admitted.Context, committed.Run, f.Token).AsTask();
        Exception? primary = null;
        var cleanup = new List<Exception>();
        try
        {
            await f.Models.ReadEntered.Task.WaitAsync(f.Token);
            Assert.False(original.IsCompleted);
            Assert.True(f.Models.Features.Remove(ToolCapability.Text));
            f.Models.ReleaseRead.TrySetResult();
            var refused = await original.WaitAsync(f.Token);
            Assert.NotNull(refused.Error);
            Assert.True(f.Models.Reads >= 3);
            Assert.Equal(0, f.Tools.Resolutions);
            Assert.Single(await f.Personal.Den.ListAsync<AgentRunRecord>("personal", f.Token));
        }
        catch (Exception error) { primary = error; }
        finally
        {
            f.Models.ReleaseRead.TrySetResult();
            try { await original.WaitAsync(TimeSpan.FromSeconds(20), CancellationToken.None); }
            catch (Exception error) { Add(cleanup, error); }
        }
        Rethrow(primary, cleanup);
    }, inheritedModel: true);

    private static AgentRunSnapshot Run(Fixture f, AgentRunState state)
    {
        var id = Guid.NewGuid().ToString("N"); var attempt = Guid.NewGuid().ToString("N");
        var session = Guid.NewGuid().ToString("D"); var now = DateTimeOffset.UtcNow;
        var budget = new AgentBudgetLimits(MaxTime: f.MaximumTime, MaxToolCalls: f.MaximumCalls);
        return new(id, f.Definition.Id, f.Definition.Revision, Fixture.Objective, AgentTriggerKind.User, "actual-user-trigger",
            null, null, "actual-original-request", null, null, [], state,
            f.Personal.Actor.ActorId, "home.agent", null, null, session, "local", null, "exact-local-model", "ollama",
            new HashSet<string>(), [], new(new HashSet<string>(), new HashSet<string>(), false, 1, 0, budget),
            budget, AgentBudgetUsage.Empty, [], [], [], [], null, null, now, null, null, null, 0, attempt,
            [new(attempt, 1, null, session, "local", "exact-local-model", "ollama", "new", [], now,
                null, null, state, AgentBudgetUsage.Empty)]);
    }

    [Fact]
    public Task Exact_original_owner_receipt_settles_real_broker_while_copied_call_result_and_replay_refuse() => WithFixture(async f =>
    {
        var (_, _, _, step) = await f.StartStepAsync();
        var admission = Assert.IsType<AgentStepAdmission>((await f.Issuer.GetOriginalStepAdmissionAsync(step, f.Token)).Value);
        f.Tools.PendingRead = true;
        var call = new OllamaToolCall("read_file", new Dictionary<string, JsonElement>());
        await f.Issuer.DemandCurrentAsync(admission.OriginalExecutionAuthority, Guid.Parse(step.SessionId), "exact-local-model", call, f.Token);
        var dispatch = await f.Issuer.GetOriginalDispatchCallAsync(admission.OriginalExecutionAuthority,
            Guid.Parse(step.SessionId), "exact-local-model", call, f.Token);
        var requestId = Assert.IsType<string>(f.Tools.OriginalRequestId);
        var result = new WorkspaceToolResult(new ToolActivity(Guid.NewGuid(), "Synthetic owning receipt", "Exact controlled result",
            true, TimeSpan.Zero, DateTimeOffset.UtcNow), "Synthetic original owning result");
        f.Tools.ExpectedResult = result;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Issuer.CompleteOriginalCallAsync(
            admission.OriginalExecutionAuthority, Guid.Parse(step.SessionId), "exact-local-model", call with { }, dispatch, result, null, f.Token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Issuer.CompleteOriginalCallAsync(
            admission.OriginalExecutionAuthority, Guid.Parse(step.SessionId), "exact-local-model", call, dispatch, result with { }, null, f.Token));
        var executing = Assert.IsType<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest>(
            await f.Permissions.ReadRequestObservationAsync(requestId, f.Token));
        Assert.Equal(HomePermissionRequestState.Executing, executing.State);
        await f.Issuer.CompleteOriginalCallAsync(admission.OriginalExecutionAuthority, Guid.Parse(step.SessionId),
            "exact-local-model", call, dispatch, result, null, f.Token);
        var recorded = Assert.IsType<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest>(
            await f.Permissions.ReadRequestObservationAsync(requestId, f.Token));
        Assert.Equal(HomePermissionRequestState.Succeeded, recorded.State);
        Assert.Equal("SYNTHETIC_OWNING_RESULT_VERIFIED", recorded.ResultCode);
        Assert.Same(call, f.Tools.LastVerifiedCall); Assert.Same(result, f.Tools.LastVerifiedResult);
        Assert.Null(f.Tools.LastVerifiedFailure);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Issuer.CompleteOriginalCallAsync(
            admission.OriginalExecutionAuthority, Guid.Parse(step.SessionId), "exact-local-model", call, dispatch, result, null, f.Token));
    }, maximumCalls: 2, syntheticToolReview: true);

    [Fact]
    public Task Missing_original_owner_receipt_blocks_next_call_and_original_close_records_partial() => WithFixture(async f =>
    {
        var (_, _, _, step) = await f.StartStepAsync();
        var admission = Assert.IsType<AgentStepAdmission>((await f.Issuer.GetOriginalStepAdmissionAsync(step, f.Token)).Value);
        f.Tools.PendingRead = true;
        var call = new OllamaToolCall("read_file", new Dictionary<string, JsonElement>());
        await f.Issuer.DemandCurrentAsync(admission.OriginalExecutionAuthority, Guid.Parse(step.SessionId), "exact-local-model", call, f.Token);
        var dispatch = await f.Issuer.GetOriginalDispatchCallAsync(admission.OriginalExecutionAuthority,
            Guid.Parse(step.SessionId), "exact-local-model", call, f.Token);
        var requestId = Assert.IsType<string>(f.Tools.OriginalRequestId);
        var result = new WorkspaceToolResult(new ToolActivity(Guid.NewGuid(), "Unconfirmed synthetic result", "No owner receipt",
            true, TimeSpan.Zero, DateTimeOffset.UtcNow), "Result text is not a receipt");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Issuer.CompleteOriginalCallAsync(
            admission.OriginalExecutionAuthority, Guid.Parse(step.SessionId), "exact-local-model", call, dispatch, result, null, f.Token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Issuer.DemandCurrentAsync(
            admission.OriginalExecutionAuthority, Guid.Parse(step.SessionId), "exact-local-model", call with { }, f.Token));
        Assert.Equal(1, f.Tools.Resolutions);
        var close = f.Issuer.DisposeAsync().AsTask(); Assert.Same(close, f.Issuer.DisposeAsync().AsTask()); await close;
        var recorded = Assert.IsType<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest>(
            await f.Permissions.ReadRequestObservationAsync(requestId, f.Token));
        Assert.Equal(HomePermissionRequestState.PartiallyCompleted, recorded.State);
        Assert.Equal("HOME_AGENT_OUTCOME_UNCONFIRMED", recorded.ResultCode);
    }, maximumCalls: 2, syntheticToolReview: true);

    [Fact]
    public Task Exact_original_failure_receipt_records_failed_without_success_from_tool_text() => WithFixture(async f =>
    {
        var (_, _, _, step) = await f.StartStepAsync();
        var admission = Assert.IsType<AgentStepAdmission>((await f.Issuer.GetOriginalStepAdmissionAsync(step, f.Token)).Value);
        f.Tools.PendingRead = true;
        var call = new OllamaToolCall("read_file", new Dictionary<string, JsonElement>());
        await f.Issuer.DemandCurrentAsync(admission.OriginalExecutionAuthority, Guid.Parse(step.SessionId), "exact-local-model", call, f.Token);
        var dispatch = await f.Issuer.GetOriginalDispatchCallAsync(admission.OriginalExecutionAuthority,
            Guid.Parse(step.SessionId), "exact-local-model", call, f.Token);
        var requestId = Assert.IsType<string>(f.Tools.OriginalRequestId);
        var original = new IOException("Synthetic original owning runtime refusal");
        f.Tools.ExpectedFailure = original;
        await f.Issuer.CompleteOriginalCallAsync(admission.OriginalExecutionAuthority, Guid.Parse(step.SessionId),
            "exact-local-model", call, dispatch, null, original, f.Token);
        var recorded = Assert.IsType<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest>(
            await f.Permissions.ReadRequestObservationAsync(requestId, f.Token));
        Assert.Equal(HomePermissionRequestState.Failed, recorded.State);
        Assert.Equal("SYNTHETIC_OWNING_FAILURE_VERIFIED", recorded.ResultCode);
        Assert.Same(original, f.Tools.LastVerifiedFailure); Assert.Same(call, f.Tools.LastVerifiedCall);
        Assert.Null(f.Tools.LastVerifiedResult);
    }, maximumCalls: 1, syntheticToolReview: true);

    [Fact]
    public Task Original_arguments_changed_during_same_held_review_refuse_before_Begin_and_keep_approved_snapshot() => WithFixture(async f =>
    {
        var (_, _, _, step) = await f.StartStepAsync();
        var admission = Assert.IsType<AgentStepAdmission>((await f.Issuer.GetOriginalStepAdmissionAsync(step, f.Token)).Value);
        f.Tools.PendingRead = true; f.Tools.HoldReview = true;
        using var originalJson = JsonDocument.Parse("{\"nested\":{\"b\":2,\"a\":1},\"path\":\"approved\"}");
        var arguments = originalJson.RootElement.EnumerateObject().ToDictionary(item => item.Name, item => item.Value);
        var call = new OllamaToolCall("read_file", arguments);
        var original = f.Issuer.DemandCurrentAsync(admission.OriginalExecutionAuthority, Guid.Parse(step.SessionId),
            "exact-local-model", call, f.Token).AsTask();
        Exception? primary = null; Exception? expected = null; var cleanup = new List<Exception>();
        try
        {
            await f.Tools.ReviewEntered.Task.WaitAsync(f.Token); Assert.False(original.IsCompleted);
            var detached = Assert.IsType<OllamaToolCall>(f.Tools.OriginalDispatch);
            Assert.NotSame(call, detached); Assert.IsAssignableFrom<FrozenDictionary<string, JsonElement>>(detached.Arguments);
            arguments["path"] = JsonSerializer.SerializeToElement("changed");
            f.Tools.ReleaseReview.TrySetResult();
            expected = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => original);
            Assert.Equal("approved", detached.Arguments["path"].GetString());
            Assert.Equal(1, detached.Arguments["nested"].GetProperty("a").GetInt32());
            var request = Assert.IsType<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest>(
                await f.Permissions.ReadRequestObservationAsync(Assert.IsType<string>(f.Tools.OriginalRequestId), f.Token));
            Assert.Equal(HomePermissionRequestState.Approved, request.State); Assert.Equal("HOME_PERMISSION_ACCEPTED", request.ResultCode);
            Assert.Equal(1, f.Tools.Resolutions); Assert.Null(f.Tools.LastVerifiedResult);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Issuer.GetOriginalDispatchCallAsync(
                admission.OriginalExecutionAuthority, Guid.Parse(step.SessionId), "exact-local-model", call, f.Token));
        }
        catch (Exception error) { primary = error; }
        finally
        {
            try { f.Tools.ReleaseReview.TrySetResult(); } catch (Exception error) { Add(cleanup, error); }
            try { await original.WaitAsync(TimeSpan.FromSeconds(20), CancellationToken.None); }
            catch (Exception error) when (ReferenceEquals(error, expected) || ReferenceEquals(error, primary)) { }
            catch (Exception error) { Add(cleanup, error); }
        }
        Rethrow(primary, cleanup);
    }, maximumCalls: 1, syntheticToolReview: true);

    [Fact]
    public Task Same_frozen_dispatch_survives_document_disposal_and_original_order_changes_but_changed_body_refuses() => WithFixture(async f =>
    {
        var (_, _, _, step) = await f.StartStepAsync();
        var admission = Assert.IsType<AgentStepAdmission>((await f.Issuer.GetOriginalStepAdmissionAsync(step, f.Token)).Value);
        f.Tools.PendingRead = true;
        using var document = JsonDocument.Parse("{\"z\":{\"b\":2,\"a\":1},\"a\":[1,2]}");
        var arguments = document.RootElement.EnumerateObject().ToDictionary(item => item.Name, item => item.Value);
        var call = new OllamaToolCall("read_file", arguments, "original-id");
        await f.Issuer.DemandCurrentAsync(admission.OriginalExecutionAuthority, Guid.Parse(step.SessionId), "exact-local-model", call, f.Token);
        var first = await f.Issuer.GetOriginalDispatchCallAsync(admission.OriginalExecutionAuthority,
            Guid.Parse(step.SessionId), "exact-local-model", call, f.Token);
        arguments.Clear(); arguments.Add("a", JsonSerializer.SerializeToElement(new[] { 1, 2 }));
        arguments.Add("z", JsonSerializer.SerializeToElement(new { a = 1, b = 2 })); document.Dispose();
        Assert.Same(first, await f.Issuer.GetOriginalDispatchCallAsync(admission.OriginalExecutionAuthority,
            Guid.Parse(step.SessionId), "exact-local-model", call, f.Token));
        Assert.Equal(2, first.Arguments["z"].GetProperty("b").GetInt32());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Issuer.GetOriginalDispatchCallAsync(
            admission.OriginalExecutionAuthority, Guid.Parse(step.SessionId), "exact-local-model", call with { }, f.Token));
        arguments["a"] = JsonSerializer.SerializeToElement(new[] { 2, 1 });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Issuer.DemandCurrentAsync(
            admission.OriginalExecutionAuthority, Guid.Parse(step.SessionId), "exact-local-model", null, f.Token));
        var outcome = new WorkspaceToolResult(new ToolActivity(Guid.NewGuid(), "Synthetic snapshot receipt", "Controlled exact snapshot",
            true, TimeSpan.Zero, DateTimeOffset.UtcNow), "Synthetic"); f.Tools.ExpectedResult = outcome;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Issuer.CompleteOriginalCallAsync(
            admission.OriginalExecutionAuthority, Guid.Parse(step.SessionId), "exact-local-model", call, first with { }, outcome, null, f.Token));
        await f.Issuer.CompleteOriginalCallAsync(admission.OriginalExecutionAuthority, Guid.Parse(step.SessionId),
            "exact-local-model", call, first, outcome, null, f.Token);
        Assert.Same(first, f.Tools.LastVerifiedDispatch); Assert.Same(call, f.Tools.LastVerifiedCall);
        Assert.Equal(HomePermissionRequestState.Succeeded, (await f.Permissions.ReadRequestObservationAsync(
            Assert.IsType<string>(f.Tools.OriginalRequestId), f.Token))?.State);
    }, maximumCalls: 1, syntheticToolReview: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Already_admitted_original_receipt_records_after_host_cancellation_or_caller_revocation(bool cancelHost) => WithFixture(async f =>
    {
        var (_, _, _, step) = await f.StartStepAsync();
        var admission = Assert.IsType<AgentStepAdmission>((await f.Issuer.GetOriginalStepAdmissionAsync(step, f.Token)).Value);
        f.Tools.PendingRead = true;
        var call = new OllamaToolCall("read_file", new Dictionary<string, JsonElement>());
        var conversation = Guid.Parse(step.SessionId);
        await f.Issuer.DemandCurrentAsync(admission.OriginalExecutionAuthority, conversation, "exact-local-model", call, f.Token);
        var dispatch = await f.Issuer.GetOriginalDispatchCallAsync(admission.OriginalExecutionAuthority,
            conversation, "exact-local-model", call, f.Token);
        var commit = await f.Issuer.GetOriginalCommitAdmissionAsync(admission.OriginalExecutionAuthority,
            conversation, "exact-local-model", dispatch, f.Token);
        Assert.Same(commit, await f.Issuer.GetOriginalCommitAdmissionAsync(admission.OriginalExecutionAuthority,
            conversation, "exact-local-model", dispatch, f.Token));
        Assert.Equal(f.Personal.Actor, await f.Issuer.DemandOriginalCommitCurrentAsync(commit, f.Token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.DemandOriginalCommitCurrentAsync(new object(), f.Token).AsTask());
        var result = new WorkspaceToolResult(new ToolActivity(Guid.NewGuid(), "Synthetic receipt", "Observed flags are not authority",
            false, TimeSpan.Zero, DateTimeOffset.UtcNow), "Text and false activity cannot erase the exact owner receipt");
        f.Tools.ExpectedResult = result;
        if (cancelHost) f.Lifetime.Cancel();
        else Assert.True((await f.Permissions.BlockCallerAsync(f.Personal.Actor.ActorId, f.Token)).Succeeded);
        if (cancelHost)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Issuer.DemandOriginalCommitCurrentAsync(commit, CancellationToken.None).AsTask());
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.DemandOriginalCommitCurrentAsync(commit, CancellationToken.None).AsTask());
        await f.Issuer.CompleteOriginalCallAsync(admission.OriginalExecutionAuthority, conversation,
            "exact-local-model", call, dispatch, result, null, CancellationToken.None);
        var observed = Assert.IsType<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest>(
            await f.Permissions.ReadRequestObservationAsync(Assert.IsType<string>(f.Tools.OriginalRequestId), CancellationToken.None));
        Assert.Equal(HomePermissionRequestState.Succeeded, observed.State);
        Assert.Equal("SYNTHETIC_OWNING_RESULT_VERIFIED", observed.ResultCode);
        Assert.Same(result, f.Tools.LastVerifiedResult); Assert.Same(call, f.Tools.LastVerifiedCall);
        Assert.Same(dispatch, f.Tools.LastVerifiedDispatch);
        if (cancelHost)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Issuer.DemandCurrentAsync(admission.OriginalExecutionAuthority,
                conversation, "exact-local-model", call with { }, CancellationToken.None).AsTask());
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.DemandCurrentAsync(admission.OriginalExecutionAuthority,
                conversation, "exact-local-model", call with { }, CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.CompleteOriginalCallAsync(
            admission.OriginalExecutionAuthority, conversation, "exact-local-model", call, dispatch, result, null, CancellationToken.None).AsTask());
        Assert.Equal(1, f.Tools.Resolutions);
    }, maximumCalls: 2, syntheticToolReview: true);

    [Fact]
    public Task Saved_policy_narrows_original_permissions_without_changing_consequential_approval() => WithFixture(async f =>
    {
        f.Definition = await f.Personal.Den.SaveAsync(f.Definition with
        {
            AllowedPermissions = new[] { "files.write", "files.rename" },
            CapabilityPolicyJson = JsonSerializer.Serialize(new AgentCapabilityPolicy(
                new HashSet<string> { "files.write" }, new HashSet<string> { "FILES.WRITE" },
                RequireApprovalForConsequentialActions: false), DenJson.Options)
        }, f.Definition.Revision, "saved-upper-bound", f.Token);
        var admitted = await f.AdmitAsync();
        Assert.Empty(admitted.Context.SurfaceCapabilities ?? Array.Empty<string>().ToFrozenSet());
        Assert.Empty((await f.Issuer.ResolveCapabilitiesAsync(new(
            new HashSet<string> { "files.write", "files.rename" }, new HashSet<string>()), admitted.Context, f.Token)).Value!);
        var observed = Assert.IsType<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest>(
            await f.Permissions.ReadRequestObservationAsync(f.Preparation!.RequestId, f.Token));
        Assert.True(observed.Policy.RequiresPerActionApproval);
    });

    [Fact]
    public Task Unknown_saved_policy_property_refuses_before_creating_a_Home_run_request() => WithFixture(async f =>
    {
        f.Definition = await f.Personal.Den.SaveAsync(f.Definition with
        {
            CapabilityPolicyJson = "{\"allowedCapabilities\":[],\"deniedCapabilities\":[],\"unexpectedGrant\":true}"
        }, f.Definition.Revision, "invalid-saved-policy", f.Token);
        await Assert.ThrowsAsync<JsonException>(() => f.Issuer.PrepareAsync(f.Reference, Fixture.Objective, f.Token));
        Assert.Null(f.Preparation);
        Assert.Empty(await f.Personal.Den.ListAsync<NineToOne.Dulche.Den.AgentRunRecord>("personal", f.Token));
    });

    [Fact]
    public Task Same_original_terminal_run_is_audited_once_and_repeated_read_retains_the_recorded_outcome() => WithFixture(async f =>
    {
        var original = await f.StartStepAsync();
        var current = Assert.IsType<AgentExecutionSnapshot>(await original.State.ReadAsync(original.Run.AgentRunId, f.Token));
        await original.State.CommitAsync(new(current.Run.AgentRunId, current.Revision, "original-terminal",
            Run: current.Run with { State = AgentRunState.Completed }), f.Token);
        await f.Issuer.AuditOriginalTerminalAsync(original.Invocation.Context);
        var actual = Assert.IsType<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest>(
            await f.Permissions.ReadRequestObservationAsync(f.Preparation!.RequestId, CancellationToken.None));
        Assert.Equal(HomePermissionRequestState.Succeeded, actual.State);
        Assert.Equal("HOME_AGENT_CANONICAL_COMPLETED", actual.ResultCode);
        await f.Issuer.AuditOriginalTerminalAsync(original.Invocation.Context, original.Run.AgentRunId);
        var repeated = Assert.IsType<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest>(
            await f.Permissions.ReadRequestObservationAsync(actual.RequestId, CancellationToken.None));
        Assert.Equal(actual.RequestId, repeated.RequestId); Assert.Equal(actual.State, repeated.State);
        Assert.Equal(actual.ResultCode, repeated.ResultCode); Assert.Equal(actual.Impact.ArgumentsDigest, repeated.Impact.ArgumentsDigest);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.AuditOriginalTerminalAsync(
            original.Invocation.Context with { }, original.Run.AgentRunId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Issuer.AuditOriginalTerminalAsync(
            original.Invocation.Context, "copied-run"));
    });

    [Fact]
    public Task Recorded_partial_child_survives_retirement_in_same_original_terminal_root_audit() => WithFixture(async f =>
    {
        var original = await f.StartStepAsync();
        var admitted = Assert.IsType<AgentStepAdmission>((await f.Issuer.GetOriginalStepAdmissionAsync(original.Step, f.Token)).Value);
        f.Tools.PendingRead = true; f.Tools.ExpectedOutcomeState = HomePermissionRequestState.PartiallyCompleted;
        var call = new OllamaToolCall("fixture_read", new Dictionary<string, JsonElement>(), "actual-partial-call");
        await f.Issuer.DemandCurrentAsync(admitted.OriginalExecutionAuthority, Guid.Parse(original.Run.SessionId), "exact-local-model", call, f.Token);
        var dispatch = await f.Issuer.GetOriginalDispatchCallAsync(admitted.OriginalExecutionAuthority,
            Guid.Parse(original.Run.SessionId), "exact-local-model", call, f.Token);
        var result = new WorkspaceToolResult(new ToolActivity(Guid.NewGuid(), "Synthetic Partial receipt", "Original controlled result",
            false, TimeSpan.Zero, DateTimeOffset.UtcNow), "Exact synthetic owner confirms Partial; no Files effect is claimed");
        f.Tools.ExpectedResult = result;
        var current = Assert.IsType<AgentExecutionSnapshot>(await original.State.ReadAsync(original.Run.AgentRunId, f.Token));
        await original.State.CommitAsync(new(current.Run.AgentRunId, current.Revision, "original-failed-after-partial",
            Run: current.Run with { State = AgentRunState.Failed }), f.Token);
        f.Lifetime.Cancel();
        await f.Issuer.CompleteOriginalCallAsync(admitted.OriginalExecutionAuthority, Guid.Parse(original.Run.SessionId),
            "exact-local-model", call, dispatch, result, null, CancellationToken.None);
        await f.Issuer.AuditOriginalTerminalAsync(original.Invocation.Context);
        var root = Assert.IsType<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest>(
            await f.Permissions.ReadRequestObservationAsync(f.Preparation!.RequestId, CancellationToken.None));
        Assert.Equal(HomePermissionRequestState.PartiallyCompleted, root.State);
        Assert.Equal("HOME_AGENT_CANONICAL_FAILED", root.ResultCode);
        Assert.Equal(HomePermissionRequestState.PartiallyCompleted,
            (await f.Permissions.ReadRequestObservationAsync(f.Tools.OriginalRequestId!, CancellationToken.None))!.State);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Issuer.DemandCurrentAsync(
            admitted.OriginalExecutionAuthority, Guid.Parse(original.Run.SessionId), "exact-local-model", null, CancellationToken.None).AsTask());
    }, maximumCalls: 1, syntheticToolReview: true);

    [Fact]
    public Task Registered_current_Den_resolver_borrows_same_Store_and_refuses_duplicates_or_changed_actor() => WithFixture(async f =>
    {
        var profiles = new HomeLocalProfileIdentity(f.Home, new OperatingSystemPrincipalSource());
        var ownership = new HomeLocalStoreOwnership(f.Home, profiles, new HomeLocalStoreEvidenceRegistry([f.Provider]), f.Permissions);
        var authority = new HomeResourceStoreOwnershipAuthority(ownership, f.Actors);
        var source = new HomeRegisteredCurrentDenSource([f.Provider], authority, f.Actors);
        var factory = Assert.IsType<HomePersonalDenFactory>(await source.ResolveCurrentAsync(f.Personal.DenId, f.Personal.Actor, f.Token));
        var same = await factory.OpenAsync(f.Token);
        Assert.Same(f.Personal.Den.Store, same.Den.Store); Assert.Equal(f.Personal.Actor, same.Actor);
        Assert.False(await same.Den.AccessPolicy.IsAllowedAsync(same.Actor.ActorId, "personal", f.Definition.Id, DenPermission.Execute, f.Token));
        Assert.False(await same.Den.AccessPolicy.IsAllowedAsync(same.Actor.ActorId, "personal", f.Definition.Id, DenPermission.Administer, f.Token));
        Assert.Null(await new HomeRegisteredCurrentDenSource([f.Provider, f.Provider], authority, f.Actors)
            .ResolveCurrentAsync(f.Personal.DenId, f.Personal.Actor, f.Token));
        Assert.Null(await source.ResolveCurrentAsync("unregistered-den", f.Personal.Actor, f.Token));
        f.Actors.Changed = f.Personal.Actor with { ActorId = "retired-original" };
        Assert.Null(await source.ResolveCurrentAsync(f.Personal.DenId, f.Personal.Actor, f.Token));
    });

    [Fact]
    public Task Caller_cancellation_held_in_original_read_finally_survives_later_host_close() => WithFixture(async f =>
    {
        var admitted = await f.AdmitAsync();
        var writer = new DenAgentExecutionStateStore(admitted.Den, "personal");
        var run = Run(f, AgentRunState.Queued);
        var committed = await writer.CommitAsync(new(run.AgentRunId, 0, "caller-first-original-run", Run: run), f.Token);
        using var caller = new CancellationTokenSource();
        f.State.HoldNextRead = true;
        var original = f.Issuer.BindOriginalRunAsync(admitted.Context, committed.Run, caller.Token).AsTask();
        var ownerCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration ownerWitness = default;
        Task? close = null;
        OperationCanceledException? originalFailure = null;
        Exception? primary = null;
        var cleanup = new List<Exception>();
        try
        {
            ownerWitness = admitted.OriginalLifetime.Register(() => ownerCancelled.TrySetResult());
            await f.State.ReadEntered.Task.WaitAsync(f.Token);
            caller.Cancel();
            await f.State.ReadCleanupEntered.Task.WaitAsync(f.Token);
            Assert.True(caller.IsCancellationRequested);
            Assert.False(admitted.OriginalLifetime.IsCancellationRequested);
            Assert.False(original.IsCompleted);
            close = f.Issuer.DisposeAsync().AsTask();
            Assert.Same(close, f.Issuer.DisposeAsync().AsTask());
            await ownerCancelled.Task.WaitAsync(f.Token);
            Assert.True(admitted.OriginalLifetime.IsCancellationRequested);
            Assert.False(f.Token.IsCancellationRequested);
            Assert.False(original.IsCompleted);
            Assert.False(close.IsCompleted);
            f.State.ReleaseCleanup.TrySetResult();
            originalFailure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original);
            Assert.True(originalFailure.CancellationToken.IsCancellationRequested);
            Assert.NotEqual(caller.Token, originalFailure.CancellationToken);
            f.ExpectedCloseFailure = originalFailure;
            var closeFailure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => close);
            Assert.Same(originalFailure, closeFailure);
            Assert.True(original.IsCompleted);
            Assert.True(close.IsCompleted);
            Assert.Equal(HomePermissionRequestState.PartiallyCompleted,
                (await f.Permissions.ReadRequestObservationAsync(f.Preparation!.RequestId, f.Token))!.State);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            try { f.State.ReleaseCleanup.TrySetResult(); } catch (Exception error) { Add(cleanup, error); }
            try { await original.WaitAsync(TimeSpan.FromSeconds(20), CancellationToken.None); }
            catch (Exception error) when (ReferenceEquals(error, originalFailure) || ReferenceEquals(error, primary)) { }
            catch (Exception error) { Add(cleanup, error); }
            if (close is not null)
                try { await close.WaitAsync(TimeSpan.FromSeconds(20), CancellationToken.None); }
                catch (Exception error) when (ReferenceEquals(error, originalFailure) || ReferenceEquals(error, primary)) { }
                catch (Exception error) { Add(cleanup, error); }
            try { ownerWitness.Dispose(); } catch (Exception error) { Add(cleanup, error); }
        }
        Rethrow(primary, cleanup);
    });

    private sealed class Fixture
    {
        internal const string Objective = "Run the exact saved Agent without tool calls.";
        internal required string Root;
        internal required CancellationTokenSource Lifetime;
        internal CancellationToken Token => Lifetime.Token;
        internal required FileHomeCoreStateStore Home;
        internal required Actors Actors;
        internal required HomeDenStoreEvidenceProvider Provider;
        internal required HomeNativeSessionLease Lease;
        internal required HomePersonalDenSession Personal;
        internal required AgentDefinitionRecord Definition;
        internal required ControlledState State;
        internal required HomePermissionTrustService Permissions;
        internal required HomeAgentExecutionAdmissions Issuer;
        internal Exception? ExpectedCloseFailure;
        internal required ToolPolicy Tools;
        internal required Models Models;
        internal long MaximumCalls;
        internal TimeSpan? MaximumTime;
        internal HomeAgentExecutionAdmissions.Preparation? Preparation;
        internal DenAgentReference Reference => new(Personal.DenId, "personal", Definition.Id, Definition.Revision);

        internal async Task<HomeAgentExecutionAdmissions.OriginalInvocation> AdmitAsync()
        {
            Preparation = await Issuer.PrepareAsync(Reference, Objective, Token);
            Assert.True((await Permissions.DecideAsync(Preparation.RequestId, HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded);
            return await Issuer.AdmitAsync(Preparation, Token);
        }
        internal async Task<(HomeAgentExecutionAdmissions.OriginalInvocation Invocation, DenAgentExecutionStateStore State,
            AgentRunSnapshot Run, AgentExecutionStep Step)> StartStepAsync()
        {
            var invocation = await AdmitAsync();
            var writer = new DenAgentExecutionStateStore(invocation.Den, "personal");
            var queued = Run(this, AgentRunState.Queued);
            var actual = await writer.CommitAsync(new(queued.AgentRunId, 0, "original-create", Run: queued), Token);
            Assert.Null((await Issuer.BindOriginalRunAsync(invocation.Context, actual.Run, Token)).Error);
            var running = await writer.CommitAsync(new(queued.AgentRunId, actual.Revision, "original-running",
                Run: queued with { State = AgentRunState.Running, Revision = 1 }), Token);
            var step = new AgentExecutionStep(running.Run.AgentRunId, running.Run.CurrentAttemptId, running.Run.Objective,
                running.Run.CallerId, running.Run.SessionId, new("local", running.Run.SessionId, "exact-local-model", "ollama", new HashSet<string>(), true),
                new HashSet<string>(), new HashSet<string>(), running.Run.BudgetLimits, [], new HashSet<string>(), 0, null);
            Assert.Null((await Issuer.AuthorizeOriginalStepAsync(invocation.Context, step, Token)).Error);
            return (invocation, writer, running.Run, step);
        }
    }

    private static async Task WithFixture(Func<Fixture, Task> action, long maximumCalls = 0, TimeSpan? maximumTime = null, string? requiredFeature = null, bool inheritedModel = false, bool syntheticToolReview = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-original-agent-issuer-" + Guid.NewGuid().ToString("N"));
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        HomeDenStoreEvidenceProvider? provider = null; HomeNativeSessionLease? lease = null;
        HomeAgentExecutionAdmissions? issuer = null; Fixture? sameFixture = null;
        Exception? primary = null; var cleanup = new List<Exception>();
        try
        {
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var actors = new Actors(profiles);
            provider = await HomeDenStoreEvidenceProvider.CreateAsync(Path.Combine(root, "den"), actors, lifetime.Token);
            var permissions = new HomePermissionTrustService(home, new FixturePolicies().TryGet);
            var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([provider]), permissions);
            await ownership.BindNewEmptyAsync("den", provider.Store.Manifest.DenId, lifetime.Token);
            var factory = new HomePersonalDenFactory(provider, new HomeResourceStoreOwnershipAuthority(ownership, actors), actors);
            var personal = await factory.OpenAsync(lifetime.Token);
            var definition = await personal.Den.SaveAsync(new AgentDefinitionRecord
            {
                Id = Guid.NewGuid().ToString("D"), NamespaceId = "personal", DisplayName = "Original saved Agent", Version = "1", Enabled = true,
                Instructions = "Original exact instructions", AllowedPermissions = [],
                ModelPolicyJson = JsonSerializer.Serialize(new AgentModelPolicy(inheritedModel, ModelId: inheritedModel ? null : "exact-local-model", ProviderId: inheritedModel ? null : "ollama",
                    RequiredCapabilities: requiredFeature is null ? null : new HashSet<string> { requiredFeature }),
                    new JsonSerializerOptions(DenJson.Options) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }),
                BudgetJson = JsonSerializer.Serialize(new AgentBudgetLimits(MaxTime: maximumTime, MaxToolCalls: maximumCalls), DenJson.Options)
            }, 0, "save-original-definition", lifetime.Token);
            lease = Assert.IsType<HomeNativeSessionLease>(await HomeNativeSessionLease.TryAcquireAsync(actors, new Paths(root), lifetime.Token));
            var state = new ControlledState(new DenAgentExecutionStateStore(personal.Den, "personal"));
            var tools = new ToolPolicy();
            var models = new Models();
            var resources = new ResourceAuthorizationService(actors, [new ObjectOwner(), new HomeModelRouteProfileOwner(profiles)]);
            var routes = new Dulche.Runtime.InMemoryModelRouteRepository();
            if (inheritedModel)
                Assert.True(await routes.TrySaveAsync(new(
                    HomeModelPickerFeatureProvider.RouteId(personal.Actor.ProfileId, Dulche.Runtime.ModelCapabilityCategory.Active),
                    1, Dulche.Runtime.ModelRouteScope.User, personal.Actor.ProfileId, Dulche.Runtime.ModelCapabilityCategory.Active,
                    [new(new("ollama", "exact-local-model"))], new(AllowRemote: false, AllowCloud: false, AllowFallback: false,
                        RequiredCapabilities: new HashSet<string> { "Text" })), 0, lifetime.Token));
            issuer = new(factory, actors, lease, permissions, state, models, resources, lifetime.Token,
                originalAuditReaders: (sameDen, originalNamespace) => new DenAgentExecutionStateStore(sameDen, originalNamespace),
                routes: new HomePersonalModelRoutes(profiles, routes, resources),
                presenter: syntheticToolReview ? new SyntheticToolReview(permissions, tools) : null, tools: tools);
            sameFixture = new() { Root = root, Lifetime = lifetime, Home = home, Actors = actors, Provider = provider,
                Lease = lease, Personal = personal, Definition = definition, State = state, Permissions = permissions, Issuer = issuer, Tools = tools, Models = models, MaximumCalls = maximumCalls, MaximumTime = maximumTime };
            await action(sameFixture);
        }
        catch (Exception error) { primary = error; }
        try { if (issuer is not null) await issuer.DisposeAsync(); }
        catch (Exception error) when (sameFixture?.ExpectedCloseFailure is { } expected && ReferenceEquals(error, expected)) { }
        catch (Exception error) { Add(cleanup, error); }
        try { lease?.Dispose(); } catch (Exception error) { Add(cleanup, error); }
        try { if (provider is not null) await provider.DisposeAsync(); } catch (Exception error) { Add(cleanup, error); }
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch (Exception error) { Add(cleanup, error); }
        Rethrow(primary, cleanup);
    }

    private sealed class ControlledState(IAgentExecutionStateStore original) : IAgentExecutionStateStore
    {
        internal bool HoldNextRead;
        internal TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReadCleanupEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseCleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<AgentExecutionSnapshot?> ReadAsync(string id, CancellationToken cancellationToken = default)
        {
            var actual = await original.ReadAsync(id, cancellationToken);
            if (!HoldNextRead) return actual;
            HoldNextRead = false;
            ReadEntered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            finally { ReadCleanupEntered.TrySetResult(); await ReleaseCleanup.Task; }
            return actual;
        }
        public ValueTask<IReadOnlyList<AgentRunSnapshot>> ListRunsAsync(string id, int limit, CancellationToken token = default) => original.ListRunsAsync(id, limit, token);
        public ValueTask<AgentExecutionSnapshot> CommitAsync(AgentExecutionChangeSet change, CancellationToken token = default) => original.CommitAsync(change, token);
    }
    private sealed class Actors(HomeLocalProfileIdentity profiles) : IAuthenticatedResourceActorSource
    {
        internal AuthenticatedResourceActor? Changed;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) =>
            Changed is null ? profiles.GetCurrentAsync(cancellationToken) : ValueTask.FromResult<AuthenticatedResourceActor?>(Changed);
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "fixture.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
    private sealed class ToolPolicy : IHomeAgentOriginalToolPolicySource
    {
        internal int Resolutions;
        internal HomePermissionRequestState? ExpectedOutcomeState;
        internal bool PendingRead;
        internal string? OriginalRequestId;
        internal OllamaToolCall? OriginalCall;
        internal OllamaToolCall? OriginalDispatch;
        internal OllamaToolCall? LastVerifiedDispatch;
        internal bool HoldReview;
        internal TaskCompletionSource ReviewEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseReview { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal WorkspaceToolResult? ExpectedResult;
        internal Exception? ExpectedFailure;
        internal OllamaToolCall? LastVerifiedCall;
        internal WorkspaceToolResult? LastVerifiedResult;
        internal Exception? LastVerifiedFailure;
        public ValueTask<HomeAgentToolDemand?> ResolveAsync(AuthenticatedResourceActor actor, DenAgentReference reference,
            AgentExecutionStep step, OllamaToolCall call, OllamaToolCall dispatchCall, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Resolutions++; OriginalCall = call; OriginalDispatch = dispatchCall;
            return ValueTask.FromResult<HomeAgentToolDemand?>(PendingRead ? new("fixture.read",
                [new("fixture.object", "original")], [new("fixture.object", "original", "1", ResourceAccess.Read)],
                new HashSet<string>(), FixturePolicies.ReadPolicy) : null);
        }
        public ValueTask<HomeExecutionOutcome?> VerifyOriginalOutcomeAsync(AuthenticatedResourceActor actor,
            DenAgentReference reference, AgentExecutionStep step, OllamaToolCall call, OllamaToolCall dispatchCall, string requestId,
            WorkspaceToolResult? result, Exception? failure, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(call, OriginalCall) || !ReferenceEquals(dispatchCall, OriginalDispatch) || requestId != OriginalRequestId ||
                (result is null) == (failure is null) ||
                result is not null && !ReferenceEquals(result, ExpectedResult) ||
                failure is not null && !ReferenceEquals(failure, ExpectedFailure))
                return ValueTask.FromResult<HomeExecutionOutcome?>(null);
            LastVerifiedCall = call; LastVerifiedDispatch = dispatchCall; LastVerifiedResult = result; LastVerifiedFailure = failure;
            return ValueTask.FromResult<HomeExecutionOutcome?>(new(
                ExpectedOutcomeState ?? (failure is null ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed),
                failure is null ? "SYNTHETIC_OWNING_RESULT_VERIFIED" : "SYNTHETIC_OWNING_FAILURE_VERIFIED",
                "Controlled exact-reference owning receipt fixture; no production tool execution claimed.",
                [new("fixture.object", "original")]));
        }
    }
    /// <summary>Explicit synthetic display acknowledgement only; no real mounted UI is asserted.
    /// It validates the SAME actual pending tool request and makes the real manual durable decision.</summary>
    private sealed class SyntheticToolReview(HomePermissionTrustService permissions, ToolPolicy tools) : IHomeApprovalPromptPresenter
    {
        public async ValueTask<bool> ShowPendingRequestAsync(string requestId, CancellationToken token)
        {
            var pending = Assert.IsType<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest>(
                await permissions.ReadRequestObservationAsync(requestId, token));
            Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
            Assert.Equal("home.agent", pending.Scope.TargetAppId); Assert.Equal("fixture.read", pending.Scope.ActionName);
            Assert.True(pending.Policy.RequiresPerActionApproval);
            tools.OriginalRequestId = pending.RequestId;
            if (tools.HoldReview)
            { tools.ReviewEntered.TrySetResult(); await tools.ReleaseReview.Task.WaitAsync(token); }
            Assert.True((await permissions.DecideAsync(requestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            return true;
        }
    }
    private sealed class FixturePolicies : IHomeActionPolicySource
    {
        internal static HomePermissionActionPolicy ReadPolicy { get; } = new(
            HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true);
        public HomePermissionActionPolicy? TryGet(string app, string action) =>
            app == "home.agent" && action == "fixture.read" ? ReadPolicy : new HomeAgentExecutionActionPolicies().TryGet(app, action);
    }
    private sealed class ObjectOwner : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => "fixture.object";
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action,
            ResourceScope scope, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ResourceAccessDecision(action == "fixture.read" && scope.Id == "original" &&
                scope.Revision == "1" && scope.Access == ResourceAccess.Read, "SYNTHETIC_OWNER_READ", actor.ActorId, "1", actor.OrganisationId));
        }
    }
    private sealed class Models : IModelProviderRegistry
    {
        internal HashSet<ToolCapability> Features { get; } = [ToolCapability.Text, ToolCapability.Vision];
        internal int Reads;
        internal int HoldAtRead = -1;
        internal TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ProviderModelDescriptor Original => new("ollama", true,
            new("exact-local-model", 1, "fixture", "fixture", "fixture", Features, DateTimeOffset.UnixEpoch));
        public IReadOnlyList<IModelProvider> Providers => [];
        public IModelProvider? Find(string id) => null;
        public IModelProvider GetRequired(string id) => throw new InvalidOperationException("No synthetic provider dispatch is supplied.");
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => GetModelsAsync(new(), token);
        public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(ModelCataloguePolicy policy, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            IReadOnlyList<ProviderModelDescriptor> captured = policy.AllowLocal
                ? [Original with { Model = Original.Model with { Capabilities = Features.ToFrozenSet() } }] : [];
            if (Interlocked.Increment(ref Reads) == HoldAtRead)
            {
                ReadEntered.TrySetResult();
                await ReleaseRead.Task.WaitAsync(token);
            }
            return captured;
        }
    }
    private static void Add(List<Exception> errors, Exception error)
    { if (!errors.Any(item => ReferenceEquals(item, error))) errors.Add(error); }
    private static void Rethrow(Exception? primary, List<Exception> errors)
    {
        if (primary is not null && !errors.Any(item => ReferenceEquals(item, primary))) errors.Insert(0, primary);
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException(errors);
    }
}
