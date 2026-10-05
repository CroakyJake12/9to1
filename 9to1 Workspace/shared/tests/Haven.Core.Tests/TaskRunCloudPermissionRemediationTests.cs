using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>Real central policy, Task issuer, coordinator, remediation registry and owner;
/// explicitly synthetic actor/catalogue/repositories. No provider request, UI click, real account,
/// Home authority, credential, paid action or monetary balance is manufactured.</summary>
public sealed class TaskRunCloudPermissionRemediationTests
{
    [Fact]
    public async Task Real_original_Ask_requires_explicit_same_scope_approval_and_never_starts_a_provider()
    {
        await RunAsync(async rig =>
        {
            var ask = await rig.AskAsync();
            var publication = rig.Owner.RequestOriginalAsync(ask, default);
            Assert.Same(publication, rig.Owner.RequestOriginalAsync(ask, default));
            var request = await publication;
            Assert.Equal(RemediationState.Waiting, request.State); Assert.Empty(rig.Policy.Grants);
            var approve = rig.Owner.ApproveOriginalAsync(request.Id, default);
            Assert.Same(approve, rig.Owner.ApproveOriginalAsync(request.Id, default));
            await approve;
            var decision = rig.Owner.GetOriginalDecision(request.Id); Assert.NotNull(decision);
            Assert.Equal(PermissionDecisionKind.Allowed, (await decision!).Kind);
            Assert.Equal(new[] { TaskRunCentralCloudUsePermissionSource.ScopeFor(rig.Task.OwnerBinding!, rig.Candidate) }, rig.Policy.Grants);
            Assert.Equal(0, rig.Provider.Starts);
            await using var currentGate = await rig.Source.AcquireOriginalAsync(rig.Task.OwnerBinding!, rig.Candidate, default);
            await currentGate.RevalidateAsync(default); // Central gate only, not credential/context admission.
            Assert.False(rig.Owner.CanRespond(request.Id));
            rig.Owner.RetireResolvedOriginal(request.Id);
            Assert.Null(rig.Owner.GetOriginalDecision(request.Id));
        });
    }

    [Fact]
    public async Task Revocation_after_a_completed_decision_requires_a_fresh_original_Ask_and_new_explicit_response()
    {
        await RunAsync(async rig =>
        {
            var first = await rig.AskAsync(); var firstRequest = await rig.Owner.RequestOriginalAsync(first, default);
            await rig.Owner.ApproveOriginalAsync(firstRequest.Id, default);
            rig.Policy.Revoke(first.OriginalDecision.Scope); Assert.Empty(rig.Policy.Grants);
            var next = await rig.AskAsync(); Assert.NotSame(first.OriginalRequest, next.OriginalRequest);
            var nextRequest = await rig.Owner.RequestOriginalAsync(next, default);
            Assert.NotEqual(firstRequest.Id, nextRequest.Id); Assert.Equal(RemediationState.Waiting, nextRequest.State);
            Assert.True(rig.Owner.CanRespond(nextRequest.Id)); Assert.Empty(rig.Policy.Grants);
            await rig.Owner.ApproveOriginalAsync(nextRequest.Id, default);
            Assert.Contains(next.OriginalDecision.Scope, rig.Policy.Grants); Assert.Equal(0, rig.Provider.Starts);
            rig.Owner.RetireResolvedOriginal(firstRequest.Id); rig.Owner.RetireResolvedOriginal(nextRequest.Id);
        });
    }

    [Fact]
    public async Task Copied_properties_and_legacy_exception_cannot_issue_a_remediation_or_grant()
    {
        await RunAsync(async rig =>
        {
            var actual = await rig.AskAsync();
            var copied = new CopiedAsk(actual.OriginalRequest!);
            Assert.Throws<UnauthorizedAccessException>(() => { _ = rig.Owner.RequestOriginalAsync(copied, default); });
            Assert.Throws<UnauthorizedAccessException>(() => { _ = rig.Owner.RequestOriginalAsync(new TaskRunCloudPermissionRequiredException(actual.OriginalDecision), default); });
            Assert.Equal(0, rig.RemediationRows.Writes); Assert.Empty(rig.Policy.Grants); Assert.Equal(0, rig.Provider.Starts);
        });
    }

    [Fact]
    public async Task A_source_Ask_from_arbitrary_task_IDs_is_rejected_by_actual_canonical_owner_lookup()
    {
        await RunAsync(async rig =>
        {
            var fabricated = rig.Task.OwnerBinding! with { TaskId = Guid.NewGuid(), ExecutionId = Guid.NewGuid() };
            var ask = await Assert.ThrowsAsync<TaskRunCloudPermissionRequiredException>(() => rig.Source.AcquireOriginalAsync(fabricated, rig.Candidate, default).AsTask());
            var original = rig.Owner.RequestOriginalAsync(ask, default);
            var error = await Record.ExceptionAsync(() => original); Assert.NotNull(error); rig.Expect(error!);
            Assert.Contains(Leaves(error!), value => value is UnauthorizedAccessException);
            Assert.Equal(0, rig.RemediationRows.Writes); Assert.Empty(rig.Policy.Grants);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_actor_or_model_retirement_before_approval_cannot_grant_the_retained_Ask(bool modelChanges)
    {
        await RunAsync(async rig =>
        {
            var request = await rig.Owner.RequestOriginalAsync(await rig.AskAsync(), default);
            if (modelChanges) rig.Provider.Model = rig.Provider.Model with { Model = rig.Provider.Model.Model with { Family = "changed-current-family" } };
            else rig.Actors.Current = rig.Actors.Current! with { AuthenticationRevision = "actual-retired-revision" };
            var original = rig.Owner.ApproveOriginalAsync(request.Id, default);
            var error = await Record.ExceptionAsync(() => original); Assert.NotNull(error); rig.Expect(error!);
            Assert.Empty(rig.Policy.Grants); Assert.Equal(0, rig.Provider.Starts);
            Assert.Same(original, rig.Owner.ApproveOriginalAsync(request.Id, default));
        });
    }

    [Fact]
    public async Task Completed_metadata_without_the_original_callback_is_never_grant_proof()
    {
        await RunAsync(async rig =>
        {
            var request = await rig.Owner.RequestOriginalAsync(await rig.AskAsync(), default);
            rig.Registry.Remove(request.Id);
            rig.RemediationRows.Rows[request.Id] = request with { State = RemediationState.Completed };
            var original = rig.Owner.ApproveOriginalAsync(request.Id, default);
            var error = await Record.ExceptionAsync(() => original); Assert.NotNull(error); rig.Expect(error!);
            Assert.Equal(RemediationState.Completed, rig.RemediationRows.Rows[request.Id].State);
            Assert.Null(rig.Owner.GetOriginalDecision(request.Id)); Assert.Empty(rig.Policy.Grants); Assert.Equal(0, rig.Provider.Starts);
        });
    }

    [Fact]
    public async Task Altered_permission_card_description_cannot_grant_the_original_model_scope()
    {
        await RunAsync(async rig =>
        {
            var request = await rig.Owner.RequestOriginalAsync(await rig.AskAsync(), default);
            rig.RemediationRows.Rows[request.Id] = request with { ProviderName = "different-provider/model", Explanation = "Grant unrelated permission" };
            var original = rig.Owner.ApproveOriginalAsync(request.Id, default);
            var error = await Record.ExceptionAsync(() => original); Assert.NotNull(error); rig.Expect(error!);
            Assert.Empty(rig.Policy.Grants); Assert.Equal(0, rig.Provider.Starts);
        });
    }

    [Fact]
    public async Task Explicit_denial_seals_the_actual_callback_and_cannot_be_replaced_by_late_generic_approval()
    {
        await RunAsync(async rig =>
        {
            var request = await rig.Owner.RequestOriginalAsync(await rig.AskAsync(), default);
            var denial = rig.Owner.DenyOriginalAsync(request.Id, default);
            Assert.Same(denial, rig.Owner.DenyOriginalAsync(request.Id, default));
            Assert.Equal(PermissionDecisionKind.Denied, (await denial).Kind);
            Assert.False(rig.Registry.Contains(request.Id)); Assert.False(rig.Owner.CanRespond(request.Id));
            await rig.Remediation.ApproveAndResolveAsync(request.Id, default); // Metadata can change, original decision cannot.
            Assert.Equal(PermissionDecisionKind.Denied, (await rig.Owner.GetOriginalDecision(request.Id)!).Kind);
            Assert.Throws<InvalidOperationException>(() => { _ = rig.Owner.ApproveOriginalAsync(request.Id, default); });
            Assert.Empty(rig.Policy.Grants); Assert.Equal(0, rig.Provider.Starts);
            rig.Owner.RetireResolvedOriginal(request.Id);
        });
    }

    [Fact]
    public async Task Callback_compound_fault_after_metadata_Completed_is_retained_at_actual_owner_close()
    {
        await RunAsync(async rig =>
        {
            var request = await rig.Owner.RequestOriginalAsync(await rig.AskAsync(), default);
            var one = new IOException("Actual canonical lookup one"); var two = new InvalidOperationException("Actual canonical lookup two");
            var failed = new TaskCompletionSource<TaskExecutionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
            failed.SetException(new Exception[] { one, two }); rig.Expect(one); rig.Expect(two);
            rig.Events.OnEvent = value => { if (value.Name == "Action approved once") rig.TaskRows.OverrideRead = failed.Task; };
            var original = rig.Owner.ApproveOriginalAsync(request.Id, default);
            var error = await Record.ExceptionAsync(() => original); Assert.NotNull(error); rig.Expect(error!);
            Assert.Equal(RemediationState.Completed, rig.RemediationRows.Rows[request.Id].State);
            Assert.Contains(Leaves(error!), value => ReferenceEquals(value, one));
            Assert.Contains(Leaves(error!), value => ReferenceEquals(value, two));
            Assert.Empty(rig.Policy.Grants); Assert.Equal(0, rig.Provider.Starts);
            var close = rig.Owner.CloseAndDrainAsync(); var closeError = await Record.ExceptionAsync(() => close);
            Assert.NotNull(closeError); rig.Expect(closeError!);
            Assert.Contains(Leaves(closeError!), value => ReferenceEquals(value, one));
            Assert.Contains(Leaves(closeError!), value => ReferenceEquals(value, two));
            Assert.Same(close, rig.Owner.CloseAndDrainAsync());
        });
    }

    [Fact]
    public async Task Close_seals_before_cancel_and_joins_held_real_approval_callback_outside_its_dependency()
    {
        await RunAsync(async rig =>
        {
            var request = await rig.Owner.RequestOriginalAsync(await rig.AskAsync(), default);
            var held = new TaskCompletionSource<TaskExecutionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = rig.TaskRows.OverrideReadEntered;
            rig.Events.OnEvent = value =>
            {
                if (value.Name != "Action approved once") return;
                rig.TaskRows.OverrideRead = held.Task;
            };
            var original = rig.Owner.ApproveOriginalAsync(request.Id, default);
            Task? close = null;
            try
            {
                await Task.WhenAny(entered.Task, original); if (!entered.Task.IsCompleted) await original; await entered.Task;
                Assert.False(original.IsCompleted);
                close = rig.Owner.CloseAndDrainAsync(); Assert.Same(close, rig.Owner.CloseAndDrainAsync()); Assert.False(close.IsCompleted);
                Assert.False(rig.Owner.CanRespond(request.Id));
                held.TrySetResult(rig.Task);
                var error = await Record.ExceptionAsync(() => original); Assert.NotNull(error);
                Assert.All(Leaves(error!), value => Assert.IsAssignableFrom<OperationCanceledException>(value)); rig.Expect(error!);
                var closeError = await Record.ExceptionAsync(() => close!); Assert.NotNull(closeError);
                Assert.All(Leaves(closeError!), value => Assert.IsAssignableFrom<OperationCanceledException>(value)); rig.Expect(closeError!);
                Assert.Empty(rig.Policy.Grants); Assert.Equal(0, rig.Provider.Starts);
            }
            finally
            {
                held.TrySetResult(rig.Task);
                // All originals settle even if assertions fail. Rig's SAME close collector reports
                // every cause not explicitly expected above; finally never waives unknown errors.
                await Record.ExceptionAsync(() => original);
                if (close is not null) await Record.ExceptionAsync(() => close);
            }
        });
    }

    [Fact]
    public async Task Genuine_successful_denial_retirement_keeps_normal_response_capacity_and_no_leaked_callbacks()
    {
        await RunAsync(async rig =>
        {
            for (var index = 0; index < 131; index++)
            {
                var request = await rig.Owner.RequestOriginalAsync(await rig.AskAsync(), default);
                await rig.Owner.DenyOriginalAsync(request.Id, default);
                Assert.False(rig.Registry.Contains(request.Id));
                rig.Owner.RetireResolvedOriginal(request.Id);
            }
            Assert.Equal(131, rig.RemediationRows.Writes); Assert.Empty(rig.Policy.Grants); Assert.Equal(0, rig.Provider.Starts);
        });
    }

    [Fact]
    public async Task Generic_approval_callback_without_the_private_explicit_owner_response_cannot_grant()
    {
        await RunAsync(async rig =>
        {
            var request = await rig.Owner.RequestOriginalAsync(await rig.AskAsync(), default);
            await rig.Remediation.ApproveAndResolveAsync(request.Id, default); // Actual generic path invokes retained callback.
            Assert.Equal(RemediationState.Completed, rig.RemediationRows.Rows[request.Id].State);
            Assert.Null(rig.Owner.GetOriginalDecision(request.Id)); Assert.Empty(rig.Policy.Grants); Assert.False(rig.Owner.CanRespond(request.Id));
            var close = rig.Owner.CloseAndDrainAsync(); var error = await Record.ExceptionAsync(() => close);
            Assert.NotNull(error); rig.Expect(error!);
            Assert.Contains(Leaves(error!), value => value is UnauthorizedAccessException);
            Assert.Equal(0, rig.Provider.Starts); Assert.Same(close, rig.Owner.CloseAndDrainAsync());
        });
    }

    [Fact]
    public async Task Repository_callback_restoring_prior_execution_context_cannot_synchronously_join_its_publication()
    {
        await RunAsync(async rig =>
        {
            var before = ExecutionContext.Capture(); Assert.NotNull(before);
            Exception? refused = null;
            rig.TaskRows.OnRead = () =>
            {
                rig.TaskRows.OnRead = null;
                ExecutionContext.Run(before!, _ => refused = Record.Exception(() => rig.Owner.CloseAndDrainAsync().GetAwaiter().GetResult()), null);
            };
            var request = await rig.Owner.RequestOriginalAsync(await rig.AskAsync(), default);
            Assert.IsType<InvalidOperationException>(refused); Assert.True(rig.Owner.CanRespond(request.Id));
            Assert.Empty(rig.Policy.Grants); Assert.Equal(0, rig.Provider.Starts);
            await rig.Owner.DenyOriginalAsync(request.Id, default); rig.Owner.RetireResolvedOriginal(request.Id);
        });
    }

    [Fact]
    public async Task Descendant_captured_during_publication_can_join_after_the_actual_original_is_terminal()
    {
        await RunAsync(async rig =>
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? descendant = null;
            rig.TaskRows.OnRead = () =>
            {
                rig.TaskRows.OnRead = null;
                descendant = Task.Run(async () => { await release.Task; await rig.Owner.CloseAndDrainAsync(); });
            };
            try
            {
                var original = rig.Owner.RequestOriginalAsync(await rig.AskAsync(), default);
                await original; Assert.True(original.IsCompletedSuccessfully); Assert.NotNull(descendant);
                release.TrySetResult(); await descendant!;
                Assert.Empty(rig.Policy.Grants); Assert.Equal(0, rig.Provider.Starts);
            }
            finally { release.TrySetResult(); if (descendant is not null) await descendant; }
        });
    }

    [Fact]
    public async Task Cancellation_callback_with_prior_context_refuses_even_the_already_published_same_close()
    {
        await RunAsync(async rig =>
        {
            var before = ExecutionContext.Capture(); Assert.NotNull(before);
            var held = new TaskCompletionSource<AuthenticatedResourceActor?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Exception? refused = null; CancellationTokenRegistration registration = default;
            Task<RemediationRequest>? original = null; Task? close = null;
            try
            {
                // Source Ask uses its own caller token; hold only the admitted owner publication read.
                rig.Actors.OriginalRead = null; var ask = await rig.AskAsync();
                rig.Actors.OriginalRead = token =>
                {
                    rig.Actors.OriginalRead = null;
                    ExecutionContext.Run(before!, _ => registration = token.Register(() =>
                        refused = Record.Exception(() => rig.Owner.CloseAndDrainAsync().GetAwaiter().GetResult())), null);
                    entered.TrySetResult(); return held.Task;
                };
                original = rig.Owner.RequestOriginalAsync(ask, default);
                await Task.WhenAny(entered.Task, original); if (!entered.Task.IsCompleted) await original; await entered.Task;
                close = rig.Owner.CloseAndDrainAsync(); Assert.IsType<InvalidOperationException>(refused); Assert.False(close.IsCompleted);
                held.TrySetResult(rig.Actors.Current);
                var originalError = await Record.ExceptionAsync(() => original!); Assert.NotNull(originalError); rig.Expect(originalError!);
                var closeError = await Record.ExceptionAsync(() => close!); Assert.NotNull(closeError); rig.Expect(closeError!);
                Assert.All(Leaves(closeError!), value => Assert.IsAssignableFrom<OperationCanceledException>(value));
                Assert.Empty(rig.Policy.Grants); Assert.Equal(0, rig.Provider.Starts); Assert.Same(close, rig.Owner.CloseAndDrainAsync());
            }
            finally
            {
                held.TrySetResult(rig.Actors.Current); registration.Dispose();
                if (original is not null) await Record.ExceptionAsync(() => original);
                if (close is not null) await Record.ExceptionAsync(() => close);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_configuration_or_catalogue_compound_fault_preserves_both_direct_original_causes(bool catalogue)
    {
        await RunAsync(async rig =>
        {
            var request = await rig.Owner.RequestOriginalAsync(await rig.AskAsync(), default);
            var one = new IOException("Actual permission read one"); var two = new InvalidOperationException("Actual permission read two");
            rig.Expect(one); rig.Expect(two);
            if (catalogue)
            {
                var original = new TaskCompletionSource<IReadOnlyList<ProviderModelDescriptor>>(); original.SetException(new Exception[] { one, two });
                rig.Providers.OriginalCatalogue = original.Task;
            }
            else
            {
                var original = new TaskCompletionSource<ProviderConfiguration?>(); original.SetException(new Exception[] { one, two });
                rig.Configurations.OriginalRead = original.Task;
            }
            var response = rig.Owner.ApproveOriginalAsync(request.Id, default);
            var error = await Record.ExceptionAsync(() => response); Assert.NotNull(error); rig.Expect(error!);
            Assert.Contains(Leaves(error!), value => ReferenceEquals(value, one)); Assert.Contains(Leaves(error!), value => ReferenceEquals(value, two));
            var closeError = await Record.ExceptionAsync(rig.Owner.CloseAndDrainAsync); Assert.NotNull(closeError); rig.Expect(closeError!);
            Assert.Contains(Leaves(closeError!), value => ReferenceEquals(value, one)); Assert.Contains(Leaves(closeError!), value => ReferenceEquals(value, two));
            Assert.Empty(rig.Policy.Grants); Assert.Equal(0, rig.Provider.Starts);
        });
    }

    [Fact]
    public async Task Faulted_actor_read_with_OCE_first_remains_faulted_with_all_original_siblings()
    {
        await RunAsync(async rig =>
        {
            var request = await rig.Owner.RequestOriginalAsync(await rig.AskAsync(), default);
            var first = new OperationCanceledException("Actual FAULTED actor payload"); var sibling = new IOException("Actual actor sibling");
            var original = new TaskCompletionSource<AuthenticatedResourceActor?>(); original.SetException(new Exception[] { first, sibling });
            rig.Expect(first); rig.Expect(sibling); rig.Actors.OriginalRead = _ => original.Task;
            var response = rig.Owner.ApproveOriginalAsync(request.Id, default);
            var error = await Record.ExceptionAsync(() => response); Assert.NotNull(error); rig.Expect(error!); Assert.True(response.IsFaulted);
            Assert.Contains(Leaves(error!), value => ReferenceEquals(value, first)); Assert.Contains(Leaves(error!), value => ReferenceEquals(value, sibling));
            Assert.Empty(rig.Policy.Grants); Assert.Equal(0, rig.Provider.Starts);
        });
    }

    [Fact]
    public async Task A_genuinely_canceled_actor_read_is_not_reclassified_as_a_faulted_grant()
    {
        await RunAsync(async rig =>
        {
            var request = await rig.Owner.RequestOriginalAsync(await rig.AskAsync(), default);
            using var stop = new CancellationTokenSource(); stop.Cancel();
            var original = Task.FromCanceled<AuthenticatedResourceActor?>(stop.Token);
            rig.Actors.OriginalRead = _ => original;
            var response = rig.Owner.ApproveOriginalAsync(request.Id, default);
            var error = await Record.ExceptionAsync(() => response); Assert.NotNull(error); rig.Expect(error!);
            Assert.True(original.IsCanceled); Assert.All(Leaves(error!), value => Assert.IsAssignableFrom<OperationCanceledException>(value));
            Assert.Empty(rig.Policy.Grants); Assert.Equal(0, rig.Provider.Starts);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Model_or_task_retirement_during_the_final_source_actor_read_is_rechecked_before_Grant(bool taskChanges)
    {
        await RunAsync(async rig =>
        {
            var request = await rig.Owner.RequestOriginalAsync(await rig.AskAsync(), default);
            var held = new TaskCompletionSource<AuthenticatedResourceActor?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var reads = 0;
            rig.Events.OnEvent = value =>
            {
                if (value.Name != "Action approved once") return;
                rig.Actors.OriginalRead = _ =>
                {
                    if (++reads != 3) return Task.FromResult(rig.Actors.Current); // Two owner selection reads, then source's final read.
                    entered.TrySetResult(); return held.Task;
                };
            };
            var response = rig.Owner.ApproveOriginalAsync(request.Id, default);
            try
            {
                await Task.WhenAny(entered.Task, response); if (!entered.Task.IsCompleted) await response; await entered.Task;
                Assert.False(response.IsCompleted); Assert.Empty(rig.Policy.Grants);
                if (taskChanges)
                {
                    // Actual canonical owner repository transition while the source actor read is held.
                    await rig.TaskRows.UpsertAsync(rig.Task with { State = TaskExecutionLifecycle.Cancelled, PersistenceRevision = rig.Task.PersistenceRevision + 1 }, default);
                }
                else rig.Provider.Model = rig.Provider.Model with { Model = rig.Provider.Model.Model with { Family = "retired-during-final-source-read" } };
                held.TrySetResult(rig.Actors.Current);
                var error = await Record.ExceptionAsync(() => response); Assert.NotNull(error); rig.Expect(error!);
                Assert.Contains(Leaves(error!), value => value is UnauthorizedAccessException);
                Assert.Empty(rig.Policy.Grants); Assert.Equal(0, rig.Provider.Starts);
            }
            finally { held.TrySetResult(rig.Actors.Current); await Record.ExceptionAsync(() => response); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Own_permission_telemetry_uses_actual_task_run_permission_action_without_completing_or_resuming_the_run(bool allow)
    {
        await RunAsync(async rig =>
        {
            var request = await rig.Owner.RequestOriginalAsync(await rig.AskAsync(), default);
            Assert.True(rig.Owner.HasCanonicalTelemetry); Assert.Equal(rig.Task.OwnerBinding, rig.Owner.GetOriginalOwner(request.Id));
            if (allow) await rig.Owner.ApproveOriginalAsync(request.Id, default);
            else await rig.Owner.DenyOriginalAsync(request.Id, default);
            var actual = rig.Events.Accepted.Where(value => value.SafeMetadata?.GetValueOrDefault("eventKind") == "task-cloud-permission").ToArray();
            Assert.Equal(2, actual.Length);
            Assert.All(actual, value =>
            {
                Assert.Equal(rig.Task.TaskId, value.TaskId); Assert.Equal(rig.Task.ExecutionId, value.ExecutionId);
                Assert.Equal(request.ActionId, value.ActionId); Assert.Equal(request.Id, value.RemediationId);
                Assert.Equal(ExecutionActionStatus.Suspended, value.Status);
            });
            Assert.Equal("request", actual[0].SafeMetadata!["permissionPhase"]);
            Assert.Equal("decision", actual[1].SafeMetadata!["permissionPhase"]);
            Assert.Equal(allow ? "Allowed" : "Denied", actual[1].SafeMetadata!["permissionDecision"]);
            Assert.Equal("completed", actual[1].SafeMetadata!["permissionResponseStatus"]);
            Assert.Equal("not-resumed", actual[1].SafeMetadata!["taskRunStatus"]);
            Assert.Equal("not-implied", actual[1].SafeMetadata!["taskRunCompletion"]);
            var current = await rig.Tasks.GetAsync(rig.Task.TaskId, default); Assert.NotNull(current);
            Assert.Equal(rig.Task.State, current!.State); Assert.Equal(0, rig.Provider.Starts);
            rig.Owner.RetireResolvedOriginal(request.Id);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_compound_event_fault_after_known_permission_decision_is_retained_without_replay_or_revocation(bool allow)
    {
        await RunAsync(async rig =>
        {
            var request = await rig.Owner.RequestOriginalAsync(await rig.AskAsync(), default);
            var first = new IOException("Actual canonical event failure one"); var second = new InvalidOperationException("Actual canonical event failure two");
            rig.Expect(first); rig.Expect(second);
            rig.Events.OnEvent = value =>
            {
                if (value.SafeMetadata?.GetValueOrDefault("permissionPhase") == "decision") throw new AggregateException(first, second);
            };
            Task response = allow ? rig.Owner.ApproveOriginalAsync(request.Id, default) : rig.Owner.DenyOriginalAsync(request.Id, default);
            var error = await Record.ExceptionAsync(() => response); Assert.NotNull(error); rig.Expect(error!);
            Assert.Contains(Leaves(error!), value => ReferenceEquals(value, first)); Assert.Contains(Leaves(error!), value => ReferenceEquals(value, second));
            var scope = TaskRunCentralCloudUsePermissionSource.ScopeFor(rig.Task.OwnerBinding!, rig.Candidate);
            if (allow) Assert.Contains(scope, rig.Policy.Grants); else Assert.Empty(rig.Policy.Grants);
            var acknowledged = rig.Owner.GetAcknowledgedOriginalDecision(request.Id); Assert.NotNull(acknowledged);
            Assert.Equal(allow ? PermissionDecisionKind.Allowed : PermissionDecisionKind.Denied, acknowledged!.Kind);
            Assert.Equal(scope, acknowledged.Scope); Assert.True(rig.Owner.GetOriginalDecision(request.Id)!.IsFaulted);
            Assert.False(rig.Owner.CanRespond(request.Id)); Assert.Equal(0, rig.Provider.Starts);
            Task same = allow ? rig.Owner.ApproveOriginalAsync(request.Id, default) : rig.Owner.DenyOriginalAsync(request.Id, default);
            Assert.Same(response, same); Assert.Throws<InvalidOperationException>(() => rig.Owner.RetireResolvedOriginal(request.Id));
            var closeError = await Record.ExceptionAsync(rig.Owner.CloseAndDrainAsync); Assert.NotNull(closeError); rig.Expect(closeError!);
            Assert.Contains(Leaves(closeError!), value => ReferenceEquals(value, first)); Assert.Contains(Leaves(closeError!), value => ReferenceEquals(value, second));
            if (allow) Assert.Contains(scope, rig.Policy.Grants); else Assert.Empty(rig.Policy.Grants); // An event failure cannot undo/pretend no grant.
        });
    }

    [Fact]
    public async Task Request_event_refusal_after_real_request_ACK_is_retained_and_never_approves_or_retries()
    {
        await RunAsync(async rig =>
        {
            rig.Events.AcceptOriginal = value => value.SafeMetadata?.GetValueOrDefault("permissionPhase") != "request";
            var original = rig.Owner.RequestOriginalAsync(await rig.AskAsync(), default);
            var error = await Record.ExceptionAsync(() => original); Assert.NotNull(error); rig.Expect(error!);
            Assert.Contains(Leaves(error!), value => value is InvalidOperationException);
            Assert.Single(rig.RemediationRows.Rows); Assert.Empty(rig.Policy.Grants); Assert.Equal(0, rig.Provider.Starts);
            var persisted = rig.RemediationRows.Rows.Values.Single();
            Assert.Null(rig.Owner.GetOriginalOwner(persisted.Id)); Assert.Null(rig.Owner.GetAcknowledgedOriginalDecision(persisted.Id));
            Assert.False(rig.Owner.CanRespond(persisted.Id));
            var closeError = await Record.ExceptionAsync(rig.Owner.CloseAndDrainAsync); Assert.NotNull(closeError); rig.Expect(closeError!);
        });
    }

    private static async Task RunAsync(Func<Rig, Task> body)
    {
        Rig? rig = null; Task? actual = null; Task? close = null; var errors = new List<Exception>();
        try { rig = await Rig.CreateAsync(); actual = body(rig); await actual; }
        catch (Exception error) { errors.Add((Exception?)actual?.Exception ?? error); }
        finally
        {
            if (rig is not null)
            {
                try { close = rig.DisposeAsync().AsTask(); await close; }
                catch (Exception error) { var original = (Exception?)close?.Exception ?? error; if (!errors.Any(value => ReferenceEquals(value, original))) errors.Add(original); }
            }
        }
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Actual control and independent fixture teardown failed.", errors);
    }
    private static IEnumerable<Exception> Leaves(Exception value)
    {
        if (value is AggregateException { InnerExceptions.Count: > 0 } group)
            foreach (var child in group.InnerExceptions) foreach (var leaf in Leaves(child)) yield return leaf;
        else yield return value;
    }
    private sealed class CopiedAsk(ITaskRunCloudPermissionOriginalRequest original) : ITaskRunCloudPermissionOriginalRequest
    {
        public TaskExecutionOwnerBinding OriginalOwner => original.OriginalOwner;
        public TaskRunRouteCandidate OriginalCandidate => original.OriginalCandidate;
        public PermissionDecision OriginalDecision => original.OriginalDecision;
    }
    private sealed class Rig : IAsyncDisposable
    {
        public Actors Actors { get; } = new(); public Provider Provider { get; } = new();
        public TaskRows TaskRows { get; } = new(); public RemediationRows RemediationRows { get; } = new();
        public Configurations Configurations { get; } = new(); public ProviderRegistry Providers { get; }
        public Events Events { get; } = new(); public PermissionDecisionEngine Policy { get; } = new();
        public RemediationContinuationRegistry Registry { get; } = new();
        public TaskRunCentralCloudUsePermissionSource Source { get; }
        public TaskRunPermissionAuthority Authority { get; }
        public TaskExecutionCoordinator Tasks { get; }
        public RemediationCoordinator Remediation { get; }
        public TaskRunCloudPermissionRemediationOwner Owner { get; }
        public TaskExecutionSnapshot Task { get; private set; } = null!;
        public TaskRunRouteCandidate Candidate { get; private set; } = null!;
        private readonly HashSet<Exception> _expected = new(ReferenceEqualityComparer.Instance);
        private Rig()
        {
            Source = new(Actors, Policy);
            Providers = new(Provider);
            Authority = new(Actors, Providers, Configurations, new Privacy(), new(new Permissions()));
            Tasks = new(TaskRows, Events, admissionAuthority: Authority);
            Remediation = new(RemediationRows, new Secrets(), Events, Registry);
            Owner = new(Source, Authority, () => Tasks, Remediation, RemediationRows, Registry, Events);
        }
        public static async Task<Rig> CreateAsync()
        {
            var rig = new Rig();
            try
            {
                rig.Task = await rig.Tasks.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "Synthetic permission task", TaskExecutionDurability.PersistedPlan, [], default);
                rig.Candidate = await rig.Authority.CaptureSelectedRouteAsync(rig.Task, rig.Provider.Model, [ToolCapability.Text], []);
                return rig;
            }
            catch (Exception error)
            {
                Task? close = null;
                try { close = rig.DisposeAsync().AsTask(); await close; }
                catch (Exception cleanup) { throw new AggregateException(error, (Exception?)close?.Exception ?? cleanup); }
                throw;
            }
        }
        public Task<TaskRunCloudPermissionRequiredException> AskAsync() => Assert.ThrowsAsync<TaskRunCloudPermissionRequiredException>(() => Source.AcquireOriginalAsync(Task.OwnerBinding!, Candidate, default).AsTask());
        public void Expect(Exception value) { foreach (var leaf in Leaves(value)) _expected.Add(leaf); }
        public async ValueTask DisposeAsync()
        {
            var errors = new List<Exception>(); Task? original = null;
            try { original = Owner.CloseAndDrainAsync(); await original; }
            catch (Exception error) { var actual = (Exception?)original?.Exception ?? error; if (Leaves(actual).Any(value => !_expected.Contains(value))) errors.Add(actual); }
            try { Remediation.Dispose(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            if (errors.Count > 1) throw new AggregateException("Actual owned close and existing remediation disposal failed.", errors);
        }
    }
    private sealed class TaskRows : ITaskExecutionRepository
    {
        private readonly Dictionary<Guid, string> _rows = [];
        public Task<TaskExecutionSnapshot?>? OverrideRead;
        public Action? OnRead = null;
        public TaskCompletionSource OverrideReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task UpsertAsync(TaskExecutionSnapshot value, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var before = _rows.TryGetValue(value.TaskId, out var json) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(json) : null;
            if (value.PersistenceRevision != (before?.PersistenceRevision ?? 0) + 1) throw new TaskExecutionRevisionConflictException(value.TaskId, value.PersistenceRevision - 1, before?.PersistenceRevision ?? 0);
            _rows[value.TaskId] = JsonSerializer.Serialize(value); return Task.CompletedTask;
        }
        public Task<TaskExecutionSnapshot?> GetAsync(Guid id, CancellationToken token)
        {
            OnRead?.Invoke();
            if (OverrideRead is { } actual) { OverrideReadEntered.TrySetResult(); return actual; }
            return Task.FromResult(_rows.TryGetValue(id, out var row) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(row) : null);
        }
        public async Task<TaskExecutionSnapshot?> GetByContextAsync(Guid id, CancellationToken token) => (await GetResumableAsync(token)).FirstOrDefault(value => value.ContextId == id);
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<TaskExecutionSnapshot>>(_rows.Values.Select(value => JsonSerializer.Deserialize<TaskExecutionSnapshot>(value)!).ToArray());
    }
    private sealed class RemediationRows : IRemediationRepository
    {
        public readonly Dictionary<Guid, RemediationRequest> Rows = []; public int Writes;
        public Task UpsertAsync(RemediationRequest value, CancellationToken token) { token.ThrowIfCancellationRequested(); Writes++; Rows[value.Id] = value; return Task.CompletedTask; }
        public Task<RemediationRequest?> GetAsync(Guid id, CancellationToken token) => Task.FromResult(Rows.GetValueOrDefault(id));
        public Task<IReadOnlyList<RemediationRequest>> GetWaitingAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<RemediationRequest>>(Rows.Values.Where(value => value.State is RemediationState.Waiting or RemediationState.InProgress).ToArray());
    }
    private sealed class Events : IExecutionEventSink
    {
        public Action<ExecutionEvent>? OnEvent;
        public Func<ExecutionEvent, bool>? AcceptOriginal = null;
        public readonly List<ExecutionEvent> Accepted = new();
        public bool TryPublish(ExecutionEvent value)
        {
            OnEvent?.Invoke(value);
            if (AcceptOriginal?.Invoke(value) == false) return false;
            Accepted.Add(value); return true;
        }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Current = new("synthetic-task-owner", "synthetic-task-profile", null, null, "current-revision");
        public Func<CancellationToken, Task<AuthenticatedResourceActor?>>? OriginalRead = null;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return OriginalRead is { } read ? new(read(token)) : ValueTask.FromResult(Current); }
    }
    private sealed class Provider : IModelProvider
    {
        public string Id => "synthetic-provider"; public string DisplayName => Id; public bool IsLocal => false; public bool CanManageModels => false;
        public ModelProviderKind Kind => ModelProviderKind.OpenAI; public int Starts;
        public ProviderModelDescriptor Model = new("synthetic-provider", false, new ModelDescriptor("synthetic-model", 10, "synthetic-family", "7B", "Q8", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch));
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>(new[] { Model });
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => Task.FromResult(new ProviderHealthStatus(Id, true, "synthetic", TimeSpan.Zero, DateTimeOffset.UnixEpoch));
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) { Starts++; throw new NotSupportedException(); }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) { Starts++; throw new NotSupportedException(); }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) { Starts++; throw new NotSupportedException(); }
    }
    private sealed class ProviderRegistry(IModelProvider original) : IModelProviderRegistry
    {
        public IReadOnlyList<IModelProvider> Providers => new[] { original };
        public IModelProvider? Find(string id) => original.Id == id ? original : null;
        public IModelProvider GetRequired(string id) => Find(id) ?? throw new InvalidOperationException();
        public Task<IReadOnlyList<ProviderModelDescriptor>>? OriginalCatalogue = null;
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => original.GetModelsAsync(token);
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(ModelCataloguePolicy policy, CancellationToken token) => OriginalCatalogue ?? original.GetModelsAsync(token);
    }
    private sealed class Configurations : IProviderConfigurationStore
    {
        private readonly ProviderConfiguration _value = new("synthetic-provider", ModelProviderKind.OpenAI, "synthetic-provider", "https://synthetic.invalid", true, false, false, new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        public Task<ProviderConfiguration?>? OriginalRead = null;
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) => OriginalRead ?? Task.FromResult<ProviderConfiguration?>(id == _value.Id ? _value : null);
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ProviderConfiguration>>(new[] { _value });
        public Task UpsertAsync(ProviderConfiguration value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Privacy : IPrivacyPreferenceStore
    { public PrivacyPreferences Current => PrivacyPreferences.Default with { LocalOnlyMode = false }; public Task UpdateAsync(PrivacyPreferences value, CancellationToken token) => throw new NotSupportedException(); }
    private sealed class Permissions : IModelPermissionStore
    { public Task<ModelPermissionPolicy> GetPolicyAsync(CancellationToken token) => Task.FromResult(ModelPermissionPolicy.Empty); public Task SavePolicyAsync(ModelPermissionPolicy value, CancellationToken token) => throw new NotSupportedException(); }
    private sealed class Secrets : IProviderSecretStore
    {
        public Task<string?> GetAsync(string provider, string name, CancellationToken token) => Task.FromResult<string?>(null);
        public Task SetAsync(string provider, string name, string value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string provider, string name, CancellationToken token) => throw new NotSupportedException();
    }
}
