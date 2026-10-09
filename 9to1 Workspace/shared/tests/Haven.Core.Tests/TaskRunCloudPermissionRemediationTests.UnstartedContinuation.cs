using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>Source-owned continuation lifetime controls using the real central policy,
/// permission owner and canonical Task coordinator. Actor, catalogue and repositories remain
/// explicitly synthetic. No provider request, native login, Home grant or monetary reservation.</summary>
public sealed partial class TaskRunCloudPermissionRemediationTests
{
    [Fact]
    public async Task Original_approved_continuation_keeps_the_same_task_and_run_and_requires_independent_scope_close()
    {
        await RunAsync(async rig =>
        {
            var ask = await rig.AskAsync();
            var publication = rig.Owner.RequestOriginalAsync(ask, default);
            var request = await publication;
            var suspended = await SuspendContinuationTaskAsync(rig);
            Assert.False(rig.Owner.HasOriginalAllowedUnstartedResponse(ask, publication));
            Assert.Throws<UnauthorizedAccessException>(() =>
            { _ = rig.Owner.AcquireOriginalUnstartedContinuationAsync(ask, publication, suspended, default); });
            await rig.Owner.ApproveOriginalAsync(request.Id, default);
            Assert.True(rig.Owner.HasOriginalAllowedUnstartedResponse(ask, publication));
            var acquisition = rig.Owner.AcquireOriginalUnstartedContinuationAsync(ask, publication, suspended, default).AsTask();
            Assert.Same(acquisition, rig.Owner.AcquireOriginalUnstartedContinuationAsync(ask, publication, suspended, default).AsTask());
            var scope = await acquisition;
            await WithContinuationScopeAsync(scope, async () =>
            {
                await scope.RevalidateOriginalAsync(suspended, default);
                Assert.Throws<InvalidOperationException>(() => rig.Owner.RetireResolvedOriginal(request.Id));
                var pin = await scope.AcquireOriginalCommitPinAsync(default);
                try
                {
                    // Only the finite synthetic repository CAS runs beneath this lifetime pin.
                    var acknowledged = suspended with { PersistenceRevision = suspended.PersistenceRevision + 1 };
                    var actualWrite = rig.TaskRows.UpsertAsync(acknowledged, default);
                    await actualWrite;
                }
                finally { await pin.DisposeAsync(); }
                // The ordinary observation read occurs after releasing the pure lifetime pin.
                var current = await rig.TaskRows.GetAsync(suspended.TaskId, default);
                Assert.NotNull(current); Assert.Equal(suspended.ExecutionId, current!.ExecutionId);
                Assert.Equal(suspended.ContextId, current.ContextId); Assert.Empty(current.Attempts);
                Assert.Equal(0, rig.Provider.Starts);
            });
            rig.Owner.RetireResolvedOriginal(request.Id);
            Assert.False(rig.Owner.HasOriginalAllowedUnstartedResponse(ask, publication));
        });
    }

    [Fact]
    public async Task Copied_publication_and_legacy_Ask_cannot_issue_an_original_continuation_scope()
    {
        await RunAsync(async rig =>
        {
            var prepared = await PrepareAllowedContinuationAsync(rig);
            var copied = Task.FromResult(await prepared.Publication);
            Assert.False(rig.Owner.HasOriginalAllowedUnstartedResponse(prepared.Ask, copied));
            Assert.Throws<UnauthorizedAccessException>(() =>
            { _ = rig.Owner.AcquireOriginalUnstartedContinuationAsync(prepared.Ask, copied, prepared.Suspended, default); });
            var legacy = new TaskRunCloudPermissionRequiredException(prepared.Ask.OriginalDecision);
            Assert.False(rig.Owner.HasOriginalAllowedUnstartedResponse(legacy, prepared.Publication));
            Assert.Throws<UnauthorizedAccessException>(() =>
            { _ = rig.Owner.AcquireOriginalUnstartedContinuationAsync(legacy, prepared.Publication, prepared.Suspended, default); });
            Assert.Equal(0, rig.Provider.Starts); Assert.Empty(prepared.Suspended.Attempts);
            var scope = await rig.Owner.AcquireOriginalUnstartedContinuationAsync(prepared.Ask, prepared.Publication, prepared.Suspended, default);
            await WithContinuationScopeAsync(scope, () => Task.CompletedTask);
        });
    }

    [Fact]
    public async Task Revoked_scope_after_acknowledged_Allow_refuses_fresh_acquisition_without_replaying_the_original()
    {
        await RunAsync(async rig =>
        {
            var prepared = await PrepareAllowedContinuationAsync(rig);
            rig.Policy.Revoke(prepared.Ask.OriginalDecision.Scope);
            Assert.True(rig.Owner.HasOriginalAllowedUnstartedResponse(prepared.Ask, prepared.Publication)); // Historical response only.
            var actual = rig.Owner.AcquireOriginalUnstartedContinuationAsync(prepared.Ask, prepared.Publication, prepared.Suspended, default).AsTask();
            var failure = await Record.ExceptionAsync(() => actual);
            Assert.NotNull(failure); rig.Expect(failure!);
            Assert.True(actual.IsFaulted);
            Assert.Contains(Leaves(failure!), value => value is TaskRunCloudPermissionRequiredException);
            Assert.Same(actual, rig.Owner.AcquireOriginalUnstartedContinuationAsync(prepared.Ask, prepared.Publication, prepared.Suspended, default).AsTask());
            Assert.Empty(rig.Policy.Grants); Assert.Equal(0, rig.Provider.Starts); Assert.Empty(prepared.Suspended.Attempts);
        });
    }

    [Fact]
    public async Task Independent_scope_close_waits_for_the_same_held_revalidation_and_conserves_its_terminal_failure()
    {
        await RunAsync(async rig =>
        {
            var prepared = await PrepareAllowedContinuationAsync(rig);
            var scope = await rig.Owner.AcquireOriginalUnstartedContinuationAsync(prepared.Ask, prepared.Publication, prepared.Suspended, default);
            var expectedClose = new List<Exception>();
            await WithContinuationScopeAsync(scope, async () =>
            {
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<AuthenticatedResourceActor?>(TaskCreationOptions.RunContinuationsAsynchronously);
                rig.Actors.OriginalRead = _ => { entered.TrySetResult(); return release.Task; };
                var original = scope.RevalidateOriginalAsync(prepared.Suspended, default).AsTask();
                Task? close = null;
                try
                {
                    Assert.Same(entered.Task, await Task.WhenAny(entered.Task, original));
                    close = scope.DisposeAsync().AsTask();
                    Assert.False(close.IsCompleted); Assert.False(original.IsCompleted);
                }
                finally { release.TrySetResult(rig.Actors.Current); rig.Actors.OriginalRead = null; }
                var failure = await Record.ExceptionAsync(() => original);
                Assert.NotNull(failure); rig.Expect(failure!); expectedClose.AddRange(Leaves(failure!));
                Assert.Contains(Leaves(failure!), value => value is ObjectDisposedException);
                Assert.NotNull(close); Assert.Same(close, scope.DisposeAsync().AsTask());
                var closeFailure = await Record.ExceptionAsync(() => close!);
                Assert.NotNull(closeFailure); rig.Expect(closeFailure!);
                Assert.All(Leaves(failure!), cause => Assert.Contains(Leaves(closeFailure!), value => ReferenceEquals(value, cause)));
                Assert.True(close!.IsFaulted); Assert.Equal(0, rig.Provider.Starts);
            }, expectedClose);
        });
    }

    [Fact]
    public async Task Parent_retirement_waits_for_actual_finite_commit_pin_and_returns_the_same_successful_close()
    {
        await RunAsync(async rig =>
        {
            var prepared = await PrepareAllowedContinuationAsync(rig);
            var scope = await rig.Owner.AcquireOriginalUnstartedContinuationAsync(prepared.Ask, prepared.Publication, prepared.Suspended, default);
            await WithContinuationScopeAsync(scope, async () =>
            {
                var pin = await scope.AcquireOriginalCommitPinAsync(default);
                Task? originalClose = null;
                try
                {
                    originalClose = rig.Owner.CloseAndDrainAsync();
                    Assert.False(originalClose.IsCompleted);
                    Assert.Same(originalClose, rig.Owner.CloseAndDrainAsync());
                    Assert.Equal(0, rig.Provider.Starts);
                }
                finally { await pin.DisposeAsync(); }
                Assert.NotNull(originalClose); await originalClose!;
                Assert.True(originalClose!.IsCompletedSuccessfully);
                Assert.False(rig.Owner.HasOriginalAllowedUnstartedResponse(prepared.Ask, prepared.Publication));
            });
        });
    }

    [Fact]
    public async Task Actual_faulted_OCE_first_actor_read_preserves_all_direct_causes_through_scope_and_parent_close()
    {
        await RunAsync(async rig =>
        {
            var prepared = await PrepareAllowedContinuationAsync(rig);
            var scope = await rig.Owner.AcquireOriginalUnstartedContinuationAsync(prepared.Ask, prepared.Publication, prepared.Suspended, default);
            var canceledFault = new OperationCanceledException("Synthetic FAULTED actor payload, not a canceled Task.");
            var sibling = new IOException("Synthetic same original actor payload sibling.");
            var actualActor = new TaskCompletionSource<AuthenticatedResourceActor?>(TaskCreationOptions.RunContinuationsAsynchronously);
            actualActor.SetException(new Exception[] { canceledFault, sibling });
            rig.Expect(canceledFault); rig.Expect(sibling);
            var expectedClose = new List<Exception> { canceledFault, sibling };
            await WithContinuationScopeAsync(scope, async () =>
            {
                rig.Actors.OriginalRead = _ => actualActor.Task;
                Task? original = null;
                try
                {
                    original = scope.RevalidateOriginalAsync(prepared.Suspended, default).AsTask();
                    var failure = await Record.ExceptionAsync(() => original!);
                    Assert.NotNull(failure); rig.Expect(failure!);
                    Assert.True(actualActor.Task.IsFaulted); Assert.True(original.IsFaulted); Assert.False(original.IsCanceled);
                    Assert.Contains(Leaves(failure!), value => ReferenceEquals(value, canceledFault));
                    Assert.Contains(Leaves(failure!), value => ReferenceEquals(value, sibling));
                }
                finally { rig.Actors.OriginalRead = null; }
            }, expectedClose);
            var actualClose = rig.Owner.CloseAndDrainAsync();
            var closeFailure = await Record.ExceptionAsync(() => actualClose);
            Assert.NotNull(closeFailure); rig.Expect(closeFailure!);
            Assert.True(actualClose.IsFaulted);
            Assert.Contains(Leaves(closeFailure!), value => ReferenceEquals(value, canceledFault));
            Assert.Contains(Leaves(closeFailure!), value => ReferenceEquals(value, sibling));
            Assert.Equal(0, rig.Provider.Starts);
        });
    }

    [Fact]
    public async Task Model_retirement_during_actual_held_revalidation_refuses_continuation_before_any_provider_start()
    {
        await RunAsync(async rig =>
        {
            var prepared = await PrepareAllowedContinuationAsync(rig);
            var scope = await rig.Owner.AcquireOriginalUnstartedContinuationAsync(prepared.Ask, prepared.Publication, prepared.Suspended, default);
            var expectedClose = new List<Exception>();
            await WithContinuationScopeAsync(scope, async () =>
            {
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<AuthenticatedResourceActor?>(TaskCreationOptions.RunContinuationsAsynchronously);
                rig.Actors.OriginalRead = _ => { entered.TrySetResult(); return release.Task; };
                var actual = scope.RevalidateOriginalAsync(prepared.Suspended, default).AsTask();
                try
                {
                    Assert.Same(entered.Task, await Task.WhenAny(entered.Task, actual));
                    rig.Provider.Model = rig.Provider.Model with { Model = rig.Provider.Model.Model with { Family = "retired-synthetic-family" } };
                    Assert.False(actual.IsCompleted);
                }
                finally { release.TrySetResult(rig.Actors.Current); rig.Actors.OriginalRead = null; }
                var failure = await Record.ExceptionAsync(() => actual);
                Assert.NotNull(failure); rig.Expect(failure!); expectedClose.AddRange(Leaves(failure!));
                Assert.Contains(Leaves(failure!), value => value is UnauthorizedAccessException);
                Assert.True(actual.IsFaulted); Assert.Equal(0, rig.Provider.Starts);
            }, expectedClose);
        });
    }

    [Fact]
    public async Task Actual_actor_callback_restoring_earlier_context_cannot_request_its_encompassing_scope_close()
    {
        await RunAsync(async rig =>
        {
            var prepared = await PrepareAllowedContinuationAsync(rig);
            var scope = await rig.Owner.AcquireOriginalUnstartedContinuationAsync(prepared.Ask, prepared.Publication, prepared.Suspended, default);
            await WithContinuationScopeAsync(scope, async () =>
            {
                var priorContext = ExecutionContext.Capture(); Assert.NotNull(priorContext);
                Exception? refusal = null;
                rig.Actors.OriginalRead = token =>
                {
                    ExecutionContext.Run(priorContext!, state =>
                    {
                        refusal = Record.Exception(() => { _ = scope.DisposeAsync(); });
                    }, null);
                    return Task.FromResult(rig.Actors.Current);
                };
                try
                {
                    var actual = scope.RevalidateOriginalAsync(prepared.Suspended, default).AsTask();
                    await actual;
                    Assert.IsType<InvalidOperationException>(refusal);
                    Assert.True(actual.IsCompletedSuccessfully); Assert.Equal(0, rig.Provider.Starts);
                }
                finally { rig.Actors.OriginalRead = null; }
            });
        });
    }

    [Fact]
    public async Task Actor_retirement_during_the_last_actual_repository_read_refuses_fresh_scope_acquisition()
    {
        await RunAsync(async rig =>
        {
            var prepared = await PrepareAllowedContinuationAsync(rig);
            var finalRead = new TaskCompletionSource<TaskExecutionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads = 0;
            rig.TaskRows.OnRead = () =>
            {
                if (++reads == 2) rig.TaskRows.OverrideRead = finalRead.Task;
            };
            var original = rig.Owner.AcquireOriginalUnstartedContinuationAsync(prepared.Ask,
                prepared.Publication, prepared.Suspended, default).AsTask();
            try
            {
                Assert.Same(rig.TaskRows.OverrideReadEntered.Task,
                    await Task.WhenAny(rig.TaskRows.OverrideReadEntered.Task, original));
                rig.Actors.Current = rig.Actors.Current! with { AuthenticationRevision = "revoked-during-final-task-read" };
                Assert.False(original.IsCompleted);
            }
            finally
            {
                rig.TaskRows.OnRead = null; rig.TaskRows.OverrideRead = null;
                finalRead.TrySetResult(prepared.Suspended);
            }
            var failure = await Record.ExceptionAsync(() => original);
            Assert.NotNull(failure); rig.Expect(failure!);
            Assert.Contains(Leaves(failure!), value => value is UnauthorizedAccessException);
            Assert.True(original.IsFaulted); Assert.True(finalRead.Task.IsCompletedSuccessfully);
            Assert.Equal(0, rig.Provider.Starts); Assert.Empty(prepared.Suspended.Attempts);
        });
    }

    private static async Task<TaskExecutionSnapshot> SuspendContinuationTaskAsync(Rig rig)
    {
        var suspended = rig.Task with { State = TaskExecutionLifecycle.Suspended, PersistenceRevision = rig.Task.PersistenceRevision + 1 };
        var actual = rig.TaskRows.UpsertAsync(suspended, default);
        await actual;
        var reread = await rig.TaskRows.GetAsync(suspended.TaskId, default);
        Assert.NotNull(reread); Assert.Equal(suspended.PersistenceRevision, reread!.PersistenceRevision);
        return reread;
    }
    private static async Task<(TaskRunCloudPermissionRequiredException Ask, Task<RemediationRequest> Publication,
        TaskExecutionSnapshot Suspended)> PrepareAllowedContinuationAsync(Rig rig)
    {
        var ask = await rig.AskAsync(); var publication = rig.Owner.RequestOriginalAsync(ask, default);
        var request = await publication; var suspended = await SuspendContinuationTaskAsync(rig);
        await rig.Owner.ApproveOriginalAsync(request.Id, default);
        Assert.True(rig.Owner.HasOriginalAllowedUnstartedResponse(ask, publication));
        return (ask, publication, suspended);
    }
    private static async Task WithContinuationScopeAsync(ITaskRunUnstartedContinuationPermissionLease sameScope,
        Func<Task> body, IReadOnlyList<Exception>? alreadyObservedCloseCauses = null)
    {
        var failures = new List<Exception>(); Task? originalBody = null, originalClose = null;
        try { originalBody = body(); await originalBody; }
        catch (Exception failure) { failures.Add((Exception?)originalBody?.Exception ?? failure); }
        finally
        {
            try { originalClose = sameScope.DisposeAsync().AsTask(); await originalClose; }
            catch (Exception failure)
            {
                var actual = (Exception?)originalClose?.Exception ?? failure;
                // Only exact references already observed from this original's known terminal task
                // may be acknowledged; distinct cleanup/unknown causes still fail the control.
                if (alreadyObservedCloseCauses is null || Leaves(actual).Any(value =>
                    !alreadyObservedCloseCauses.Any(observed => ReferenceEquals(observed, value)))) failures.Add(actual);
            }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Actual continuation control and independent scope retirement failed.", failures);
    }
}
