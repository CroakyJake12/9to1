using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using NineToOne.Dulche.Den;

namespace Dulche.Runtime.Agents.Tests;

/// <summary>Real Den catalog/state/coordinator and original Chat loop with a clearly synthetic
/// reference issuer, current host and model provider. This is not actual Home/transport/native proof.</summary>
public sealed class DenAgentChatExecutionAdapterTests
{
    [Fact]
    public Task Real_Den_run_uses_original_Chat_model_and_same_session_without_a_second_run_registry() => WithDen(async f =>
    {
        var started = await f.Coordinator.StartAsync(f.Request());
        var original = Assert.IsType<AgentExecutionSnapshot>(started.Value); Assert.Null(started.Error);
        var result = await f.Coordinator.ExecuteNextStepAsync(original.Run.AgentRunId);
        var completed = Assert.IsType<AgentExecutionSnapshot>(result.Value); Assert.Null(result.Error);
        Assert.Equal(AgentRunState.Completed, completed.Run.State); Assert.Equal(1, f.Client.Streams);
        Assert.Equal(original.Run.SessionId, f.Issuer.ConversationId.ToString("N"));
        Assert.Equal(original.Run.AgentRunId, completed.Run.AgentRunId);
        Assert.Equal(f.Record.Id, completed.Run.AgentId); Assert.Equal(f.Record.Revision, completed.Run.DefinitionRevision);
        Assert.Equal("exact-model", f.Client.LastModel); Assert.Equal("exact-model", completed.Run.ModelId);
        Assert.Equal("ollama", completed.Run.ProviderId);
        Assert.Single(await f.Den.ListAsync<AgentRunRecord>("personal"));
        Assert.Single(await f.State.ListRunsAsync(f.Record.Id, 10));
        Assert.Equal("original synthetic provider response", Assert.Single(completed.Events,
            value => value.Kind == "chat.response.completed").Detail);
        Assert.Equal(completed.Run.AgentRunId, Assert.Single(completed.Outputs).EntityId);
        Assert.Equal(1, completed.Run.BudgetUsage.Steps.Value); Assert.Equal(0, completed.Run.BudgetUsage.ToolCalls.Value);
        Assert.Equal(UsageAvailability.ProviderUnavailable, completed.Run.BudgetUsage.Tokens.Availability);
        Assert.Empty(completed.CompletedConsequentialActionIds); Assert.Empty(completed.UncertainConsequentialActionIds);
    });

    [Fact]
    public Task Copied_original_step_is_denied_before_model_or_canonical_run_write() => WithDen(async f =>
    {
        var started = Assert.IsType<AgentExecutionSnapshot>((await f.Coordinator.StartAsync(f.Request())).Value);
        var before = f.Client.InventoryReads;
        var copied = new AgentExecutionStep(started.Run.AgentRunId, started.Run.CurrentAttemptId, started.Run.Objective,
            started.Run.CallerId, started.Run.SessionId, new("ollama", started.Run.SessionId, "exact-model", "ollama",
                new HashSet<string>(), true), new HashSet<string>(), new HashSet<string>(), new(), [], new HashSet<string>(), 1, null);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Adapter.ExecuteStepAsync(copied, (_, _) => ValueTask.CompletedTask, cancellationToken: DulcheOriginalTestCancellation.Current).AsTask());
        Assert.Equal(before, f.Client.InventoryReads); Assert.Equal(0, f.Client.Streams);
        Assert.Equal(started.Revision, (await f.State.ReadAsync(started.Run.AgentRunId))!.Revision);
        Assert.Single(await f.Den.ListAsync<AgentRunRecord>("personal"));
    });

    [Fact]
    public Task Actual_Den_definition_change_during_original_compatibility_inventory_denies_model_dispatch() => WithDen(async f =>
    {
        var started = Assert.IsType<AgentExecutionSnapshot>((await f.Coordinator.StartAsync(f.Request())).Value);
        f.Client.AfterInventory = async () =>
        {
            f.Client.AfterInventory = null;
            await f.Den.SaveAsync(f.Record with { Instructions = "changed original Den revision" }, f.Record.Revision, "change-before-model");
        };
        var result = await f.Coordinator.ExecuteNextStepAsync(started.Run.AgentRunId);
        Assert.Equal(0, f.Client.Streams); Assert.Equal(AgentRunState.Failed, result.Value!.Run.State);
        Assert.Equal(AgentFailureCode.DefinitionRevisionUnavailable.ToString(), result.Value.Run.LastErrorCode);
        Assert.Single(await f.Den.ListAsync<AgentRunRecord>("personal"));
        Assert.Equal(f.Record.Revision + 1, (await f.Den.GetAsync<AgentDefinitionRecord>("personal", f.Record.Id))!.Revision);
    });

    [Fact]
    public Task Finite_unmeasured_token_budget_is_explicitly_refused_without_model_work_or_invented_usage() => WithDen(async f =>
    {
        var result = await f.Coordinator.StartAsync(f.Request() with { Budget = new(MaxTokens: 1) });
        // Saved definition budget has no finite token bound, so creation is real, but the
        // original adapter refuses the narrowed requested token budget before model work.
        var created = Assert.IsType<AgentExecutionSnapshot>(result.Value); Assert.Null(result.Error);
        var refused = await f.Coordinator.ExecuteNextStepAsync(created.Run.AgentRunId);
        Assert.Equal(0, f.Client.Streams); Assert.Equal(AgentRunState.Failed, refused.Value!.Run.State);
        Assert.Equal(AgentFailureCode.BudgetUsageUnavailable.ToString(), refused.Value.Run.LastErrorCode);
        Assert.Equal(0, refused.Value.Run.BudgetUsage.Steps.Value);
        Assert.Single(await f.Den.ListAsync<AgentRunRecord>("personal"));
    });

    [Fact]
    public Task A_changed_original_compatibility_model_family_is_refused_before_model_work() => WithDen(async f =>
    {
        var started = Assert.IsType<AgentExecutionSnapshot>((await f.Coordinator.StartAsync(f.Request())).Value);
        f.Client.CompatibilityFamilyOverride = "different-family";
        var result = await f.Coordinator.ExecuteNextStepAsync(started.Run.AgentRunId);
        Assert.Equal(0, f.Client.Streams);
        Assert.Equal(AgentRunState.Failed, result.Value!.Run.State);
        Assert.Equal(AgentFailureCode.ExecutionFailed.ToString(), result.Value.Run.LastErrorCode);
        Assert.Single(await f.Den.ListAsync<AgentRunRecord>("personal"));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task A_real_Den_stop_or_pause_during_final_projection_cannot_be_overwritten_by_the_original_result(bool pause) =>
        WithDen(f => PreserveOriginalRaceTask(f, pause, holdCommit: false));

    [Fact]
    public Task A_real_Den_stop_after_result_read_is_refused_at_the_original_atomic_commit_revision() =>
        WithDen(f => PreserveOriginalRaceTask(f, pause: false, holdCommit: true));

    [Fact]
    public Task Only_the_same_private_pending_approval_pauses_the_actual_Den_run_and_uncheckpointed_resume_is_refused() => WithDen(async f =>
    {
        var started = Assert.IsType<AgentExecutionSnapshot>((await f.Coordinator.StartAsync(f.Request())).Value);
        var originalFailure = new InvalidOperationException("Synthetic original pending manual request");
        var approval = new AgentApprovalRequirement(Guid.NewGuid().ToString("N"), started.Run.AgentRunId,
            started.Run.CallerId, "synthetic-owning-action", "actual-test-object", "test", ["actual-test-object"],
            "Synthetic action requires an explicit user decision.", "pending", DateTimeOffset.UtcNow, null, null);
        f.Issuer.PendingFailure = originalFailure; f.Issuer.PendingApproval = approval; f.Client.StreamFailure = originalFailure;
        var paused = await f.Coordinator.ExecuteNextStepAsync(started.Run.AgentRunId);
        Assert.Null(paused.Error); Assert.Equal(AgentRunState.AwaitingApproval, paused.Value!.Run.State);
        Assert.Same(approval, f.Issuer.LastReturnedApproval);
        Assert.Equal(approval.ApprovalId, Assert.Single(paused.Value.Approvals).ApprovalId);
        Assert.Equal(AgentFailureCode.ApprovalRequired.ToString(), paused.Value.Run.LastErrorCode);
        Assert.Equal(UsageAvailability.ProviderUnavailable, paused.Value.Run.BudgetUsage.Steps.Availability);
        var copiedError = await f.Issuer.GetOriginalApprovalAsync(f.Issuer.OriginalStep!,
            new InvalidOperationException(originalFailure.Message));
        Assert.Equal(AgentFailureCode.PermissionDenied, copiedError.Error!.Code);
        var copiedStep = await f.Issuer.GetOriginalApprovalAsync(f.Issuer.OriginalStep! with { }, originalFailure);
        Assert.Equal(AgentFailureCode.PermissionDenied, copiedStep.Error!.Code);
        var before = paused.Value.Revision;
        var resumed = await f.Coordinator.ResumeAsync(started.Run.AgentRunId);
        Assert.Equal(AgentFailureCode.CheckpointUnavailable, resumed.Error!.Code);
        Assert.Equal(1, f.Client.Streams);
        var actual = (await f.State.ReadAsync(started.Run.AgentRunId))!;
        Assert.Equal(before, actual.Revision); Assert.Equal(AgentRunState.AwaitingApproval, actual.Run.State);
        Assert.Single(await f.Den.ListAsync<AgentRunRecord>("personal"));
    });

    [Fact]
    public Task An_unregistered_original_runtime_error_cannot_fabricate_an_approval_pause() => WithDen(async f =>
    {
        var started = Assert.IsType<AgentExecutionSnapshot>((await f.Coordinator.StartAsync(f.Request())).Value);
        f.Client.StreamFailure = new InvalidOperationException("Synthetic unrelated actual model error");
        var failed = await f.Coordinator.ExecuteNextStepAsync(started.Run.AgentRunId);
        Assert.Equal(AgentRunState.Failed, failed.Value!.Run.State);
        Assert.Equal(AgentFailureCode.ExecutionFailed.ToString(), failed.Value.Run.LastErrorCode);
        Assert.Empty(failed.Value.Approvals); Assert.Null(f.Issuer.LastReturnedApproval);
        Assert.Single(await f.Den.ListAsync<AgentRunRecord>("personal"));
    });

    [Fact]
    public Task An_approval_pause_committed_after_the_original_resume_read_cannot_be_overwritten_or_replayed() => WithDen(async f =>
    {
        var started = Assert.IsType<AgentExecutionSnapshot>((await f.Coordinator.StartAsync(f.Request())).Value);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<AgentResult<AgentExecutionSnapshot>>? originalResume = null;
        Exception? primary = null; var cleanup = new List<Exception>();
        f.CoordinatorState.AfterOriginalRead = async captured =>
        {
            Assert.Equal(started.Revision, captured!.Revision);
            Assert.Equal(AgentRunState.Queued, captured.Run.State);
            entered.TrySetResult(); await released.Task;
        };
        try
        {
            originalResume = f.Coordinator.ResumeAsync(started.Run.AgentRunId).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(originalResume.IsCompleted); Assert.Equal(0, f.Client.Streams);
            var originalFailure = new InvalidOperationException("Synthetic original manual approval during resume wait");
            var approval = new AgentApprovalRequirement(Guid.NewGuid().ToString("N"), started.Run.AgentRunId,
                started.Run.CallerId, "synthetic-owning-action", "actual-test-object", "test", ["actual-test-object"],
                "Original action requires an explicit decision.", "pending", DateTimeOffset.UtcNow, null, null);
            f.Issuer.PendingFailure = originalFailure; f.Issuer.PendingApproval = approval; f.Client.StreamFailure = originalFailure;
            var paused = await f.Coordinator.ExecuteNextStepAsync(started.Run.AgentRunId);
            Assert.Null(paused.Error); Assert.Equal(AgentRunState.AwaitingApproval, paused.Value!.Run.State);
            Assert.Equal(approval.ApprovalId, Assert.Single(paused.Value.Approvals).ApprovalId);
            Assert.Equal(1, f.Client.Streams);
            released.TrySetResult(); var refused = await originalResume;
            Assert.NotNull(refused.Error); Assert.False(refused.Error!.Retryable);
            Assert.NotNull(f.CoordinatorState.OriginalResumeCommitFailure);
            var actual = Assert.IsType<AgentExecutionSnapshot>(await f.State.ReadAsync(started.Run.AgentRunId));
            Assert.Equal(paused.Value.Revision, actual.Revision);
            Assert.Equal(paused.Value.Run.Revision, actual.Run.Revision);
            Assert.Equal(AgentRunState.AwaitingApproval, actual.Run.State);
            Assert.Equal(AgentFailureCode.ApprovalRequired.ToString(), actual.Run.LastErrorCode);
            Assert.Equal(approval.ApprovalId, Assert.Single(actual.Approvals).ApprovalId);
            Assert.Equal(started.Run.CurrentAttemptId, actual.Run.CurrentAttemptId);
            Assert.Equal(1, f.Client.Streams);
            Assert.DoesNotContain(actual.Events, item => item.Kind == "run.resumed");
            Assert.Single(await f.Den.ListAsync<AgentRunRecord>("personal"));
        }
        catch (Exception failure) { primary = failure; }
        finally
        {
            released.TrySetResult();
            if (originalResume is not null) try { await originalResume; }
                catch (Exception failure) { if (!cleanup.Any(item => ReferenceEquals(item, failure))) cleanup.Add(failure); }
        }
        if (primary is not null && !cleanup.Any(item => ReferenceEquals(item, primary))) cleanup.Insert(0, primary);
        if (cleanup.Count == 1) ExceptionDispatchInfo.Capture(cleanup[0]).Throw();
        if (cleanup.Count > 1) throw new AggregateException(cleanup);
    });

    [Fact]
    public async Task An_unregistered_session_factory_denies_even_matching_observed_identifiers()
    {
        IDenAgentExecutionSessionFactory unavailable = new UnavailableSessionFactory();
        using var lifetime = new CancellationTokenSource();
        var denied = await unavailable.OpenCurrentAsync(new("observed-den", "personal", Guid.NewGuid().ToString("D"), 1),
            new("observed-caller", "observed-host"), lifetime.Token);
        Assert.Equal(AgentFailureCode.PermissionDenied, denied.Error!.Code); Assert.Null(denied.Value);
    }

    [Fact]
    public Task Composed_original_session_uses_the_existing_Chat_and_one_Den_run_while_same_request_joins_the_original_start() => WithDen(async f =>
    {
        var reference = new DenAgentReference(f.Den.Store.Manifest.DenId, "personal", f.Record.Id, f.Record.Revision);
        var opened = await f.Factory.OpenCurrentAsync(reference, f.Invocation, f.Issuer.OriginalLifetime);
        Assert.Null(opened.Error); var session = Assert.IsAssignableFrom<IDenAgentExecutionSession>(opened.Value);
        Assert.Same(session, (await f.Factory.OpenCurrentAsync(reference, f.Invocation, f.Issuer.OriginalLifetime)).Value);
        var request = f.Request(); var original = session.StartAsync(request).AsTask();
        Exception? primary = null; var cleanup = new List<Exception>();
        AgentResult<AgentExecutionSnapshot>? createdResult = null;
        try
        {
            Assert.Same(original, session.StartAsync(request).AsTask());
            createdResult = await original; Assert.Null(createdResult.Error);
        }
        catch (Exception failure) { primary = failure; }
        finally
        {
            try { await original; } catch (Exception failure) { if (!cleanup.Any(item => ReferenceEquals(item, failure))) cleanup.Add(failure); }
        }
        if (primary is not null && !cleanup.Any(item => ReferenceEquals(item, primary))) cleanup.Insert(0, primary);
        if (cleanup.Count == 1) ExceptionDispatchInfo.Capture(cleanup[0]).Throw();
        if (cleanup.Count > 1) throw new AggregateException(cleanup);
        var created = createdResult!;
        var rootId = created.Value!.Run.AgentRunId;
        Assert.Equal(AgentFailureCode.PermissionDenied, (await session.StartAsync(request with { })).Error!.Code);
        Assert.Equal(AgentFailureCode.PermissionDenied, (await session.GetAsync(Guid.NewGuid().ToString("N"))).Error!.Code);
        var result = await session.ExecuteNextStepAsync(rootId);
        Assert.Null(result.Error); Assert.Equal(AgentRunState.Completed, result.Value!.Run.State);
        Assert.Equal(1, f.Client.Streams); Assert.Equal(rootId, result.Value.Run.AgentRunId);
        Assert.Equal(created.Value.Run.SessionId, result.Value.Run.SessionId);
        Assert.Single(await f.Den.ListAsync<AgentRunRecord>("personal"));
        Assert.Single(await f.State.ListRunsAsync(f.Record.Id, 10));
        Assert.Equal(AgentRunState.Completed, (await session.GetAsync(rootId)).Value!.Run.State);
        Assert.Equal("original synthetic provider response", Assert.Single(result.Value.Events,
            item => item.Kind == "chat.response.completed").Detail);
    });

    [Fact]
    public Task Session_composition_refuses_copied_context_foreign_namespace_and_unrelated_lifetime_before_run_creation() => WithDen(async f =>
    {
        var reference = new DenAgentReference(f.Den.Store.Manifest.DenId, "personal", f.Record.Id, f.Record.Revision);
        using var unrelated = new CancellationTokenSource();
        var copied = await f.Factory.OpenCurrentAsync(reference, f.Invocation with { }, f.Issuer.OriginalLifetime);
        var foreign = await f.Factory.OpenCurrentAsync(reference with { NamespaceId = "foreign" }, f.Invocation, f.Issuer.OriginalLifetime);
        var lifetime = await f.Factory.OpenCurrentAsync(reference, f.Invocation, unrelated.Token);
        foreach (var denied in new[] { copied, foreign, lifetime })
        { Assert.Equal(AgentFailureCode.PermissionDenied, denied.Error!.Code); Assert.Null(denied.Value); }
        Assert.Empty(await f.Den.ListAsync<AgentRunRecord>("personal")); Assert.Equal(0, f.Client.Streams);
    });

    [Fact]
    public Task Original_agent_idempotency_collision_is_not_hidden_by_the_private_session_boundary() => WithDen(async f =>
    {
        var priorRequest = f.Request() with { IdempotencyKey = "actual-original-prior-definition-key" };
        var created = Assert.IsType<AgentExecutionSnapshot>((await f.Coordinator.StartAsync(priorRequest)).Value);
        // A genuinely committed earlier definition run remains in the SAME canonical
        // Agent index after the owning definition revision changes. Caller/session
        // identity is never rewritten or granted by a copied context.
        var currentRecord = await f.Den.SaveAsync(f.Record with { Instructions = "new original canonical definition" },
            f.Record.Revision, "actual-current-definition-collision");
        var reference = new DenAgentReference(f.Den.Store.Manifest.DenId, "personal", currentRecord.Id, currentRecord.Revision);
        using var issuer = new SyntheticIssuer(f.Invocation, reference);
        var provider = new SyntheticProvider(); var providers = new ModelProviderRegistry([provider]);
        var registry = new CapabilityRegistryService(new EmptyCapabilities()); var dependencies = new AgentDependencyCatalogService(registry);
        var catalog = new DenPersistentAgentCatalog(f.Den, "personal");
        var validator = new DenAgentRuntimeValidator(f.Den, "personal", dependencies, providers, new ModelRouter(providers));
        var chat = new ChatSessionService(new Conversations(), f.Client, new CapabilityPreflightService(), new Safety(),
            new WorkspaceToolRuntime(Refuse<IWorkspaceToolService>()), new ComputerToolRuntime(Refuse<IComputerToolService>()),
            originalExecutionAdmissions: issuer);
        var runtime = new AgentTaskRuntimeService(Refuse<ICatalogRepository>(), Refuse<IAgentRunRepository>(), f.Client,
            registry, chat, Refuse<IPermissionDecisionEngine>(), canonicalAdmissions: issuer);
        var adapter = new DenAgentChatExecutionAdapter(reference, catalog, validator, issuer, f.State, issuer, runtime);
        var ordinaryCoordinator = new AgentExecutionService(f.State, catalog, issuer, adapter);
        var factory = new DenAgentExecutionSessionFactory(f.Den, reference, f.Invocation, issuer.OriginalLifetime,
            issuer, issuer, dependencies, providers, new ModelRouter(providers), runtime);
        var currentRequest = priorRequest with { DefinitionRevision = currentRecord.Revision };
        var ordinary = await ordinaryCoordinator.StartAsync(currentRequest);
        Assert.Equal(AgentFailureCode.IdempotencyMismatch, ordinary.Error!.Code); Assert.Null(ordinary.Value);
        var session = (await factory.OpenCurrentAsync(reference, f.Invocation, issuer.OriginalLifetime)).Value!;
        var refused = await session.StartAsync(currentRequest);
        Assert.Equal(ordinary.Error!.Code, refused.Error!.Code); Assert.Null(refused.Value);
        var retained = Assert.IsType<AgentExecutionSnapshot>(await f.State.ReadAsync(created.Run.AgentRunId));
        Assert.Equal(created.Revision, retained.Revision); Assert.Equal(created.Run.DefinitionRevision, retained.Run.DefinitionRevision);
        Assert.Equal(created.Run.AgentRunId, retained.Run.AgentRunId); Assert.Equal(created.Run.CallerId, retained.Run.CallerId);
        Assert.Equal(created.Run.CurrentAttemptId, retained.Run.CurrentAttemptId); Assert.Equal(created.Run.State, retained.Run.State);
        Assert.Equal(created.Run.SessionId, retained.Run.SessionId); Assert.Equal(created.Run.IdempotencyKey, retained.Run.IdempotencyKey);
        Assert.Equal(currentRecord.Revision, (await f.Den.GetAsync<AgentDefinitionRecord>("personal", f.Record.Id))!.Revision);
        Assert.Single(await f.State.ListRunsAsync(f.Record.Id, 10));
        Assert.Single(await f.Den.ListAsync<AgentRunRecord>("personal")); Assert.Equal(0, f.Client.Streams);
    });

    [Fact]
    public Task A_retired_definition_denies_a_previously_open_session_without_reading_or_dispatching_its_run() => WithDen(async f =>
    {
        var reference = new DenAgentReference(f.Den.Store.Manifest.DenId, "personal", f.Record.Id, f.Record.Revision);
        var session = (await f.Factory.OpenCurrentAsync(reference, f.Invocation, f.Issuer.OriginalLifetime)).Value!;
        var created = await session.StartAsync(f.Request()); Assert.Null(created.Error);
        await f.Den.SaveAsync(f.Record with { Instructions = "retired original canonical definition" }, f.Record.Revision, "retire-session-definition");
        var denied = await session.GetAsync(created.Value!.Run.AgentRunId);
        Assert.Equal(AgentFailureCode.DefinitionRevisionUnavailable, denied.Error!.Code); Assert.Null(denied.Value);
        Assert.Equal(0, f.Client.Streams); Assert.Single(await f.Den.ListAsync<AgentRunRecord>("personal"));
    });

    [Fact]
    public Task A_retired_original_lifetime_denies_new_session_work_but_keeps_the_same_known_creation_receipt() => WithDen(async f =>
    {
        var reference = new DenAgentReference(f.Den.Store.Manifest.DenId, "personal", f.Record.Id, f.Record.Revision);
        var session = (await f.Factory.OpenCurrentAsync(reference, f.Invocation, f.Issuer.OriginalLifetime)).Value!;
        var request = f.Request(); var original = session.StartAsync(request).AsTask(); var created = await original;
        Assert.Null(created.Error); f.Issuer.CancelOriginalLifetime();
        Assert.Equal(AgentFailureCode.PermissionDenied,
            (await f.Factory.OpenCurrentAsync(reference, f.Invocation, f.Issuer.OriginalLifetime)).Error!.Code);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.GetAsync(created.Value!.Run.AgentRunId, cancellationToken: DulcheOriginalTestCancellation.Current).AsTask());
        Assert.Same(original, session.StartAsync(request).AsTask());
        Assert.Equal(created.Value!.Run.AgentRunId, (await session.StartAsync(request)).Value!.Run.AgentRunId);
        Assert.Equal(0, f.Client.Streams); Assert.Single(await f.Den.ListAsync<AgentRunRecord>("personal"));
    });

    [Fact]
    public Task A_changed_actual_attempt_cannot_reuse_the_opened_original_session_for_reads_or_dispatch() => WithDen(async f =>
    {
        var reference = new DenAgentReference(f.Den.Store.Manifest.DenId, "personal", f.Record.Id, f.Record.Revision);
        var session = (await f.Factory.OpenCurrentAsync(reference, f.Invocation, f.Issuer.OriginalLifetime)).Value!;
        var created = await session.StartAsync(f.Request()); Assert.Null(created.Error);
        var actual = created.Value!; var nextAttemptId = Guid.NewGuid().ToString("N");
        var nextAttempt = Assert.Single(actual.Run.Attempts) with { AttemptId = nextAttemptId, Number = 2 };
        var changed = await f.State.CommitAsync(new(actual.Run.AgentRunId, actual.Revision, "actual-new-attempt",
            Run: actual.Run with { CurrentAttemptId = nextAttemptId, Revision = actual.Run.Revision + 1,
                Attempts = actual.Run.Attempts.Append(nextAttempt).ToArray() }));
        var deniedRead = await session.GetAsync(actual.Run.AgentRunId);
        var deniedDispatch = await session.ExecuteNextStepAsync(actual.Run.AgentRunId);
        Assert.Equal(AgentFailureCode.PermissionDenied, deniedRead.Error!.Code); Assert.Null(deniedRead.Value);
        Assert.Equal(AgentFailureCode.PermissionDenied, deniedDispatch.Error!.Code); Assert.Null(deniedDispatch.Value);
        var retained = (await f.State.ReadAsync(actual.Run.AgentRunId))!;
        Assert.Equal(changed.Revision, retained.Revision); Assert.Equal(nextAttemptId, retained.Run.CurrentAttemptId);
        Assert.Equal(0, f.Client.Streams); Assert.Single(await f.Den.ListAsync<AgentRunRecord>("personal"));
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task A_real_attempt_rotation_during_final_admission_cannot_read_dispatch_or_pause_the_later_attempt(int operation) =>
        WithDen(f => PreserveOriginalSessionRotation(f, operation, afterRead: false));

    [Fact]
    public Task A_real_attempt_rotation_after_the_actual_Get_read_refuses_original_result_publication() =>
        WithDen(f => PreserveOriginalSessionRotation(f, operation: 0, afterRead: true));

    private static async Task PreserveOriginalSessionRotation(Fixture f, int operation, bool afterRead)
    {
        var reference = new DenAgentReference(f.Den.Store.Manifest.DenId, "personal", f.Record.Id, f.Record.Revision);
        var session = (await f.Factory.OpenCurrentAsync(reference, f.Invocation, f.Issuer.OriginalLifetime)).Value!;
        var created = await session.StartAsync(f.Request()); Assert.Null(created.Error);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observations = 0; Task<AgentResult<AgentExecutionSnapshot>>? original = null;
        Exception? primary = null; var cleanup = new List<Exception>();
        f.Issuer.BeforeInvocationObservation = async () =>
        {
            // Each currentness check has two original host reads. Third is after the
            // wrapper's actual root read; fifth follows the owning Get's original read.
            if (Interlocked.Increment(ref observations) == (afterRead ? 5 : 3))
            { entered.TrySetResult(); await released.Task; }
        };
        try
        {
            var id = created.Value!.Run.AgentRunId;
            original = (operation == 0 ? session.GetAsync(id) : operation == 1 ? session.ExecuteNextStepAsync(id) : session.PauseAsync(id)).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(original.IsCompleted); Assert.Equal(0, f.Client.Streams);
            var actual = (await f.State.ReadAsync(id))!; var nextId = Guid.NewGuid().ToString("N");
            var next = Assert.Single(actual.Run.Attempts) with { AttemptId = nextId, Number = 2 };
            var rotated = await f.State.CommitAsync(new(id, actual.Revision, "actual-held-rotation",
                Run: actual.Run with { CurrentAttemptId = nextId, Revision = actual.Run.Revision + 1,
                    Attempts = actual.Run.Attempts.Append(next).ToArray() }));
            released.TrySetResult(); var denied = await original;
            Assert.NotNull(denied.Error); Assert.False(denied.Error!.Retryable); Assert.Null(denied.Value);
            var retained = (await f.State.ReadAsync(id))!;
            Assert.Equal(rotated.Revision, retained.Revision); Assert.Equal(nextId, retained.Run.CurrentAttemptId);
            Assert.Equal(AgentRunState.Queued, retained.Run.State); Assert.Equal(0, f.Client.Streams);
            Assert.DoesNotContain(retained.Events, item => item.Kind is "run.started" or "run.paused");
            Assert.Single(await f.Den.ListAsync<AgentRunRecord>("personal"));
        }
        catch (Exception failure) { primary = failure; }
        finally
        {
            released.TrySetResult();
            if (original is not null) try { await original; }
                catch (Exception failure) { if (!cleanup.Any(item => ReferenceEquals(item, failure))) cleanup.Add(failure); }
            f.Issuer.BeforeInvocationObservation = null;
        }
        if (primary is not null && !cleanup.Any(item => ReferenceEquals(item, primary))) cleanup.Insert(0, primary);
        if (cleanup.Count == 1) ExceptionDispatchInfo.Capture(cleanup[0]).Throw();
        if (cleanup.Count > 1) throw new AggregateException(cleanup);
    }

    private sealed class UnavailableSessionFactory : IDenAgentExecutionSessionFactory { }

    private static async Task PreserveOriginalRaceTask(Fixture f, bool pause, bool holdCommit)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = 0; Task<AgentResult<AgentExecutionSnapshot>>? original = null;
        Exception? primary = null; var failures = new List<Exception>();
        async Task HoldOriginal()
        {
            if (Interlocked.CompareExchange(ref held, 1, 0) != 0) return;
            entered.TrySetResult(); await released.Task;
        }
        try
        {
            var started = Assert.IsType<AgentExecutionSnapshot>((await f.Coordinator.StartAsync(f.Request())).Value);
            if (holdCommit) f.CoordinatorState.BeforeResultCommit = HoldOriginal;
            else f.Policy.BeforeExecuteRead = () => f.Client.Streams == 1 ? HoldOriginal() : Task.CompletedTask;
            original = f.Coordinator.ExecuteNextStepAsync(started.Run.AgentRunId).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(original.IsCompleted); Assert.Equal(1, f.Client.Streams);
            var retired = pause ? await f.Coordinator.PauseAsync(started.Run.AgentRunId) :
                await f.Coordinator.StopAsync(started.Run.AgentRunId);
            Assert.Null(retired.Error); Assert.Equal(pause ? AgentRunState.Paused : AgentRunState.Stopped, retired.Value!.Run.State);
            released.TrySetResult(); var result = await original;
            Assert.NotNull(result.Error); Assert.False(result.Error!.Retryable);
            if (!holdCommit) Assert.Equal(AgentFailureCode.RevisionConflict, result.Error.Code);
            var actual = Assert.IsType<AgentExecutionSnapshot>(await f.State.ReadAsync(started.Run.AgentRunId));
            Assert.Equal(retired.Value.Revision, actual.Revision);
            Assert.Equal(retired.Value.Run.State, actual.Run.State);
            Assert.Equal(started.Run.CurrentAttemptId, actual.Run.CurrentAttemptId);
            Assert.Empty(actual.Outputs);
            Assert.DoesNotContain(actual.Events, value => value.Kind == "chat.response.completed");
            Assert.Single(await f.Den.ListAsync<AgentRunRecord>("personal"));
            if (holdCommit) Assert.NotNull(f.CoordinatorState.OriginalResultCommitFailure);
        }
        catch (Exception failure) { primary = failure; }
        finally
        {
            released.TrySetResult();
            if (original is not null) try { await original; }
                catch (Exception failure) { failures.Add(failure); }
        }
        if (primary is not null && !failures.Any(value => ReferenceEquals(value, primary))) failures.Insert(0, primary);
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Saved_surface_availability_is_consumed_by_actual_target_admission_before_model_work(bool available) => WithDen(async f =>
    {
        var result = await f.Coordinator.StartAsync(f.Request());
        if (available)
        {
            Assert.Null(result.Error);
            var completed = await f.Coordinator.ExecuteNextStepAsync(result.Value!.Run.AgentRunId);
            Assert.Null(completed.Error); Assert.Equal(AgentRunState.Completed, completed.Value!.Run.State);
            Assert.Equal(1, f.Client.Streams); Assert.Single(await f.Den.ListAsync<AgentRunRecord>("personal"));
        }
        else
        {
            Assert.Equal(AgentFailureCode.AgentUnavailableInScope, result.Error?.Code);
            Assert.Null(result.Value); Assert.Equal(0, f.Client.Streams); Assert.Equal(0, f.Client.InventoryReads);
            Assert.Empty(await f.Den.ListAsync<AgentRunRecord>("personal"));
        }
    }, record => record with { AvailabilityBindings = [new(Guid.NewGuid(), AgentAvailabilityScope.Surface,
        available ? "synthetic-original-host" : "unavailable-other-surface")] });

    [Theory]
    [InlineData("knowledge")]
    [InlineData("graph")]
    [InlineData("memory")]
    public Task Unintegrated_saved_owning_routes_are_explicitly_blocked_before_original_model_work(string kind) => WithDen(async f =>
    {
        var result = await f.Coordinator.StartAsync(f.Request());
        Assert.Null(result.Value); Assert.Equal(AgentFailureCode.CapabilityUnavailable, result.Error?.Code);
        Assert.Contains("BLOCKED", result.Error!.Message); Assert.Equal(0, f.Client.Streams);
        Assert.Equal(0, f.Client.InventoryReads); Assert.Empty(await f.Den.ListAsync<AgentRunRecord>("personal"));
        Assert.Equal(f.Record.Revision, (await f.Den.GetAsync<AgentDefinitionRecord>("personal", f.Record.Id))!.Revision);
    }, record => kind switch
    {
        "knowledge" => record with { KnowledgeReferences = [new(Guid.NewGuid(), "Files", "actual-reference-id", "1", "original-scope")] },
        "graph" => record with { GraphReference = new("original-den-reference", "personal", "workflow-reference", 1) },
        _ => record with { MemoryPolicy = new("personal", MemoryScopeKind.User, "original-memory-scope", MemoryFrequency.Always) }
    });

    private static async Task WithDen(Func<Fixture, Task> action,
        Func<AgentDefinitionRecord, AgentDefinitionRecord>? configure = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-den-chat-adapter-" + Guid.NewGuid().ToString("N"));
        DenStore? store = null; SyntheticIssuer? issuer = null; Exception? primary = null; var cleanup = new List<Exception>();
        try
        {
            store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            var policy = new SyntheticPolicy(); var den = new DulcheDen(store, policy, "owner");
            var proposed = new AgentDefinitionRecord
            {
                Id = Guid.NewGuid().ToString("D"), NamespaceId = "personal", DisplayName = "Canonical Agent", Version = "1",
                Enabled = true, Instructions = "same canonical instructions",
                ModelPolicyJson = JsonSerializer.Serialize(new AgentModelPolicy(false, "exact-model", "ollama", false, false,
                    new HashSet<string> { "Text" }), DenJson.Options)
            };
            var record = await den.SaveAsync(configure?.Invoke(proposed) ?? proposed, 0, "create-definition");
            var reference = new DenAgentReference(store.Manifest.DenId, "personal", record.Id, record.Revision);
            var invocation = new AgentInvocationContext("owner", "synthetic-original-host");
            issuer = new(invocation, reference); var state = new DenAgentExecutionStateStore(den, "personal");
            var catalog = new DenPersistentAgentCatalog(den, "personal"); var provider = new SyntheticProvider();
            var providers = new ModelProviderRegistry([provider]); var client = new SyntheticClient(provider);
            var registry = new CapabilityRegistryService(new EmptyCapabilities());
            var validator = new DenAgentRuntimeValidator(den, "personal", new(registry), providers, new ModelRouter(providers));
            var chat = new ChatSessionService(new Conversations(), client, new CapabilityPreflightService(), new Safety(),
                new WorkspaceToolRuntime(Refuse<IWorkspaceToolService>()), new ComputerToolRuntime(Refuse<IComputerToolService>()),
                originalExecutionAdmissions: issuer);
            var runtime = new AgentTaskRuntimeService(Refuse<ICatalogRepository>(), Refuse<IAgentRunRepository>(), client,
                registry, chat, Refuse<IPermissionDecisionEngine>(), canonicalAdmissions: issuer);
            var adapter = new DenAgentChatExecutionAdapter(reference, catalog, validator, issuer, state, issuer, runtime);
            var coordinatorState = new ResultCommitBoundary(state);
            var coordinator = new AgentExecutionService(coordinatorState, catalog, issuer, adapter);
            var factory = new DenAgentExecutionSessionFactory(den, reference, invocation, issuer.OriginalLifetime,
                issuer, issuer, new(registry), providers, new ModelRouter(providers), runtime);
            await action(new(den, record, state, issuer, client, adapter, coordinator, invocation, policy, coordinatorState, factory));
        }
        catch (Exception failure) { primary = failure; }
        finally
        {
            if (issuer is not null) try { issuer.Dispose(); } catch (Exception failure) { cleanup.Add(failure); }
            if (store is not null) try { await store.DisposeAsync(); } catch (Exception failure) { cleanup.Add(failure); }
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception failure) { cleanup.Add(failure); }
        }
        if (primary is not null && !cleanup.Any(value => ReferenceEquals(value, primary))) cleanup.Insert(0, primary);
        if (cleanup.Count == 1) ExceptionDispatchInfo.Capture(cleanup[0]).Throw();
        if (cleanup.Count > 1) throw new AggregateException(cleanup);
    }
    private sealed record Fixture(DulcheDen Den, AgentDefinitionRecord Record, DenAgentExecutionStateStore State,
        SyntheticIssuer Issuer, SyntheticClient Client, DenAgentChatExecutionAdapter Adapter,
        AgentExecutionService Coordinator, AgentInvocationContext Invocation, SyntheticPolicy Policy, ResultCommitBoundary CoordinatorState,
        DenAgentExecutionSessionFactory Factory)
    {
        public AgentRunRequest Request() => new(Record.Id, Record.Revision, "Summarize the original model response",
            AgentTriggerKind.User, "actual-test-trigger", Invocation, new());
    }
    private sealed class SyntheticPolicy : IDenAccessPolicy
    {
        public Func<Task>? BeforeExecuteRead { get; set; }
        public async ValueTask<bool> IsAllowedAsync(string principal, string ns, string id, DenPermission permission, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); if (permission == DenPermission.Execute && BeforeExecuteRead is { } held) await held();
            ct.ThrowIfCancellationRequested(); return principal == "owner" && ns == "personal"; }
    }
    private sealed class SyntheticIssuer(AgentInvocationContext original, DenAgentReference reference) :
        IAgentPermissionBroker, IChatExecutionAdmission, IDenAgentCurrentRuntimeContextSource, IDisposable
    {
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(60));
        private readonly object _authority = new(); private AgentExecutionStep? _step;
        public AgentExecutionStep? OriginalStep => _step;
        public CancellationToken OriginalLifetime => _lifetime.Token;
        public void CancelOriginalLifetime() => _lifetime.Cancel();
        public Exception? PendingFailure { get; set; }
        public AgentApprovalRequirement? PendingApproval { get; set; }
        public AgentApprovalRequirement? LastReturnedApproval { get; private set; }
        public ValueTask<AgentResult<AgentApprovalRequirement>> GetOriginalApprovalAsync(AgentExecutionStep step,
            Exception failure, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (ReferenceEquals(step, _step) && ReferenceEquals(failure, PendingFailure) &&
                PendingApproval is { State: "pending", DecisionId: null, DecidedAtUtc: null } approval)
            { LastReturnedApproval = approval; return ValueTask.FromResult(AgentResult<AgentApprovalRequirement>.Success(approval)); }
            return ValueTask.FromResult(AgentResult<AgentApprovalRequirement>.Failure(new(AgentFailureCode.PermissionDenied,
                "No same private synthetic pending approval.", step.AgentRunId)));
        }
        public Guid ConversationId { get; private set; }
        public ValueTask<AgentResult<IReadOnlySet<string>>> ResolveCapabilitiesAsync(AgentCapabilityPolicy policy,
            AgentInvocationContext context, CancellationToken ct = default) => ValueTask.FromResult(
                ReferenceEquals(context, original) ? AgentResult<IReadOnlySet<string>>.Success(new HashSet<string>()) :
                AgentResult<IReadOnlySet<string>>.Failure(new(AgentFailureCode.PermissionDenied, "Synthetic original context refused.", reference.AgentId)));
        public ValueTask<AgentResult<bool>> BindOriginalRunAsync(AgentInvocationContext context, AgentRunSnapshot run, CancellationToken ct = default) =>
            ValueTask.FromResult(AgentResult<bool>.Success(ReferenceEquals(context, original)));
        public ValueTask<AgentResult<bool>> AuthorizeOriginalStepAsync(AgentInvocationContext context, AgentExecutionStep step, CancellationToken ct = default)
        { if (!ReferenceEquals(context, original)) return ValueTask.FromResult(AgentResult<bool>.Success(false));
            _step = step; ConversationId = Guid.Parse(step.SessionId); return ValueTask.FromResult(AgentResult<bool>.Success(true)); }
        public ValueTask<AgentResult<AgentStepAdmission>> GetOriginalStepAdmissionAsync(AgentExecutionStep step, CancellationToken ct = default) =>
            ValueTask.FromResult(ReferenceEquals(step, _step) ? AgentResult<AgentStepAdmission>.Success(new(new HashSet<string>(), _authority, _lifetime.Token)) :
                AgentResult<AgentStepAdmission>.Failure(new(AgentFailureCode.PermissionDenied, "Synthetic copied step refused.", step.AgentRunId)));
        public ValueTask<AgentResult<AgentApprovalRequirement>> RequestApprovalAsync(AgentApprovalRequirement request, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public ValueTask DemandCurrentAsync(object authority, Guid conversation, string model, OllamaToolCall? call, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); if (!ReferenceEquals(authority, _authority) || conversation != ConversationId || model != "exact-model" || call is not null)
            throw new UnauthorizedAccessException("Synthetic current model-only admission refused."); return ValueTask.CompletedTask; }
        public ValueTask<CancellationToken> GetOriginalLifetimeAsync(object authority, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); if (!ReferenceEquals(authority, _authority)) throw new UnauthorizedAccessException(); return ValueTask.FromResult(_lifetime.Token); }
        public Func<Task>? BeforeInvocationObservation { get; set; }
        public async ValueTask<AgentResult<DenAgentCurrentRuntimeContext>> GetForInvocationAsync(DenAgentReference r,
            AgentInvocationContext context, CancellationToken ct = default)
        {
            if (BeforeInvocationObservation is { } held) await held();
            ct.ThrowIfCancellationRequested(); return await Context(r, ReferenceEquals(context, original));
        }
        public ValueTask<AgentResult<DenAgentCurrentRuntimeContext>> GetForStepAsync(DenAgentReference r,
            AgentExecutionStep step, CancellationToken ct = default) => Context(r, ReferenceEquals(step, _step));
        private ValueTask<AgentResult<DenAgentCurrentRuntimeContext>> Context(DenAgentReference r, bool same) =>
            ValueTask.FromResult(same && r == reference ? AgentResult<DenAgentCurrentRuntimeContext>.Success(
                new(r, CapabilityPlatform.Windows, "agent:" + Guid.Parse(r.AgentId).ToString("N"), null)) :
                AgentResult<DenAgentCurrentRuntimeContext>.Failure(new(AgentFailureCode.PermissionDenied, "Synthetic host reference refused.", r.AgentId)));
        public void Dispose() => _lifetime.Dispose();
    }
    private sealed class SyntheticProvider : IModelProvider
    {
        public string Id => "ollama"; public string DisplayName => "Synthetic"; public ModelProviderKind Kind => ModelProviderKind.Ollama;
        public bool IsLocal => true; public bool CanManageModels => false;
        public ModelDescriptor Descriptor { get; } = new("exact-model", 1, "synthetic", "1", "synthetic",
            new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch);
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([new(Id, true, Descriptor)]); }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken ct) => throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class SyntheticClient(SyntheticProvider provider) : IOllamaClient
    {
        public int Streams { get; private set; } public int InventoryReads { get; private set; } public string? LastModel { get; private set; }
        public Func<Task>? AfterInventory { get; set; }
        public string? CompatibilityFamilyOverride { get; set; }
        public Exception? StreamFailure { get; set; }
        public Task<bool> IsAvailableAsync(CancellationToken ct) => Task.FromResult(true);
        public async Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); ++InventoryReads; if (AfterInventory is { } callback) await callback(); return [CompatibilityFamilyOverride is { } family ? provider.Descriptor with { Family = family } : provider.Descriptor]; }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken ct) => throw new NotSupportedException();
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); ++Streams; LastModel = request.Model; await Task.Yield(); if (StreamFailure is { } failure) throw failure; yield return "original synthetic provider response"; }
    }
    private sealed class EmptyCapabilities : ICapabilityRepository
    {
        public Task<IReadOnlyList<CapabilityDefinition>> GetCapabilitiesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<CapabilityDefinition>>([]);
        public Task UpsertCapabilityAsync(CapabilityDefinition value, CancellationToken ct) => throw new NotSupportedException();
        public Task SetCapabilityEnabledAsync(Guid id, bool value, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteCustomCapabilityAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Safety : IConversationSafetyService
    {
        public Task<ConversationSafetySnapshot> GetSnapshotAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<ConversationSafetyFlagResult> RecordConfirmedFlagAsync(Guid id, ConfirmedSafetyFlag flag, CancellationToken ct) => throw new NotSupportedException();
        public Task EnsureMayActAsync(Guid id, string action, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    }
    private sealed class Conversations : IConversationRepository
    {
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<Conversation>>([]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<Conversation?>(null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken ct) => throw new NotSupportedException();
        public Task AddMessageAsync(ChatMessage message, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteConversationAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }
    // Only the owning result commit is held. Reads and lifecycle commits remain real
    // canonical Den operations, so Stop changes the actual revision during this await.
    private sealed class ResultCommitBoundary(DenAgentExecutionStateStore original) : IAgentExecutionStateStore
    {
        public Func<Task>? BeforeResultCommit { get; set; }
        public Exception? OriginalResultCommitFailure { get; private set; }
        public Func<AgentExecutionSnapshot?, Task>? AfterOriginalRead { get; set; }
        public Exception? OriginalResumeCommitFailure { get; private set; }
        public async ValueTask<AgentExecutionSnapshot?> ReadAsync(string id, CancellationToken ct = default)
        {
            var captured = await original.ReadAsync(id, ct);
            var held = AfterOriginalRead; AfterOriginalRead = null;
            if (held is not null) await held(captured);
            return captured;
        }
        public ValueTask<IReadOnlyList<AgentRunSnapshot>> ListRunsAsync(string id, int limit, CancellationToken ct = default) => original.ListRunsAsync(id, limit, ct);
        public async ValueTask<AgentExecutionSnapshot> CommitAsync(AgentExecutionChangeSet change, CancellationToken ct = default)
        {
            var result = change.OperationId.StartsWith("step.result:", StringComparison.Ordinal);
            if (result && BeforeResultCommit is { } held) await held();
            try { return await original.CommitAsync(change, ct); }
            catch (Exception failure)
            {
                if (result) OriginalResultCommitFailure = failure;
                if (change.OperationId.StartsWith("run.resumed:", StringComparison.Ordinal)) OriginalResumeCommitFailure = failure;
                throw;
            }
        }
    }
    private static T Refuse<T>() where T : class => DispatchProxy.Create<T, RefusingPortProxy>();
    public class RefusingPortProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException("The original canonical path must not call legacy unrelated port: " + targetMethod?.Name);
    }
}
