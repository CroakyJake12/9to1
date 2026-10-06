using System.Reflection;
using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace HavenOS.Home.Tests;

// Actual Home source publication/close and scoped resource components. Controlled journal
// identity below is configuration-only, never a protected claim or a positive project grant.
public sealed partial class HomeColdProjectReadReconciliationTests
{
    [Fact]
    public async Task Actual_source_retention_callback_cannot_join_its_pending_source_close()
    {
        await WithSource(async (source, factoryCause, known) =>
        {
            var callbacks = 0; Task? actual = null;
            actual = source.PrepareOriginalProjectInputWithinSourceAsync(null!, null!, "public reference", body => body(), _ =>
            {
                callbacks++;
                var refusal = Assert.Throws<InvalidOperationException>(() => { _ = source.CloseAndDrainOriginalAsync(); });
                Assert.NotNull(refusal);
                Assert.Null(typeof(HomeColdProjectReadReconciliation).GetField("_close", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(source));
            }, CancellationToken.None);
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled); Assert.Equal(1, callbacks);
            Assert.Contains(Leaves(error), value => ReferenceEquals(value, factoryCause)); known.Add(factoryCause);
        });
    }
    [Fact]
    public async Task Actual_source_publication_OCE_is_faulted_without_a_canceled_raw_task()
    {
        await WithSource(async (source, _, known) =>
        {
            var cause = new OperationCanceledException("actual synchronous retention fault"); known.Add(cause);
            var actual = source.PrepareOriginalProjectInputWithinSourceAsync(null!, null!, "public reference", body => body(), _ => throw cause, CancellationToken.None);
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.Contains(Leaves(error), value => ReferenceEquals(value, cause));
        });
    }
    [Fact]
    public async Task Swallowed_second_scope_invocation_retains_exact_refusal_and_refuses_productive_selection()
    {
        await WithSource(async (source, factoryCause, known) =>
        {
            Exception? refusal = null;
            var actual = source.PrepareOriginalProjectInputWithinSourceAsync(null!, null!, "public reference", body =>
            {
                body(); try { body(); } catch (Exception cause) { refusal = cause; known.Add(cause); }
            }, _ => { }, CancellationToken.None);
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.True(actual.IsFaulted); Assert.IsType<InvalidOperationException>(refusal);
            Assert.Contains(Leaves(error), value => ReferenceEquals(value, refusal));
            Assert.DoesNotContain(Leaves(error), value => ReferenceEquals(value, factoryCause));
        });
    }
    [Fact]
    public async Task Copied_initial_input_never_reaches_current_File_native_or_resource_reads()
    {
        await WithSource((source, _, _) =>
        {
            var copy = new PublicInput();
            Assert.False(source.IsIssuedOriginalProjectInput(copy)); Assert.False(source.IsOwnedOriginalProjectInput(copy));
            Assert.Throws<UnauthorizedAccessException>(() => { _ = source.ValidateOriginalProjectInputWithinSourceAsync(copy, null!, body => body(), _ => { }, CancellationToken.None); });
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task Current_project_action_is_manual_and_remains_pending_until_real_decline()
    {
        await WithSource(async (source, _, _) =>
        {
            var permissions = (HomePermissionTrustService)typeof(HomeColdProjectReadReconciliation).GetField("_permissions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(source)!;
            var actual = await permissions.AuthorizeAsync(new(null, new("dev:current-project-control", "READ component", "actual source control", "control", false),
                "actual-manual-control", new("dev", HomeColdProjectReadReconciliation.ReadAction, [new("dev.project.current", "control")]),
                new(["dev.project.current"], 1, [new("dev.project.current", "control")], false)), CancellationToken.None);
            Assert.Equal(HomePermissionRequestState.PendingApproval, actual.State);
            Assert.False((await permissions.BeginExecutionAsync(actual.RequestId, CancellationToken.None)).IsAllowed);
            Assert.True((await permissions.DecideAsync(actual.RequestId, HomeApprovalChoice.Decline, cancellationToken: CancellationToken.None)).Succeeded);
            Assert.Equal(HomePermissionRequestState.Denied, (await permissions.GetAuthorizationAsync(actual.RequestId, CancellationToken.None)).State);
        });
    }
    [Fact]
    public async Task Actual_scoped_prepared_review_gate_is_released_after_raw_retention_failure()
    {
        await WithSource(async (source, _, known) =>
        {
            var broker = (HomeResourceOperationBroker)typeof(HomeColdProjectReadReconciliation).GetField("_broker", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(source)!;
            var prepared = broker.PrepareReviewForActor(new("controlled-prefix", "controlled-prefix-profile", null, null, "controlled"), "dev",
                HomeColdProjectReadReconciliation.ReadAction, [new("dev.project.current", "controlled", "r1", ResourceAccess.Read)],
                System.Text.Json.JsonSerializer.SerializeToElement(new { controlled = true }), "No resource grant in gate control", null, "prefix-control");
            var cause = new IOException("exact actual gate retention failure"); known.Add(cause); var originals = new List<Task>();
            Task? actual = null; Exception? primary = null; var errors = new List<Exception>();
            try
            {
                actual = broker.AuthorizePreparedReviewWithinOriginalSourceAsync(prepared, body => body(), raw => { originals.Add(raw); throw cause; }, CancellationToken.None);
                var failure = await Assert.ThrowsAsync<AggregateException>(() => actual);
                Assert.True(actual.IsFaulted); Assert.Contains(Leaves(failure), error => ReferenceEquals(error, cause));
                Assert.Single(originals); Assert.True(originals[0].IsCompletedSuccessfully);
                var gate = (SemaphoreSlim)typeof(HomeResourcePreparedReview).GetField("Gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(prepared)!;
                Assert.Equal(1, gate.CurrentCount);
            }
            catch (Exception error) { primary = error; }
            finally
            {
                foreach (var raw in (actual is null ? originals : originals.Append(actual)).Distinct<Task>(ReferenceEqualityComparer.Instance))
                    try { await raw; } catch (Exception error) { errors.Add(raw.Exception ?? error); }
            }
            var unexpected = errors.SelectMany(Leaves).Where(error => !ReferenceEquals(error, cause)).ToArray();
            if (unexpected.Length != 0) throw new AggregateException("Actual broker originals failed.", primary is null ? unexpected : new[] { primary }.Concat(unexpected));
            if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        });
    }
    [Fact]
    public async Task Admitted_owner_reservation_does_not_bypass_public_external_join_or_issue_an_input()
    {
        await WithSource((source, _, _) =>
        {
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(source);
            Assert.Throws<InvalidOperationException>(() => { _ = source.PrepareOriginalProjectInputWithinSourceAsync(null!, null!, "no grant", body => body(), _ => { }, CancellationToken.None); });
            var reserved = typeof(HomeColdProjectReadReconciliation).GetMethod("ReserveAdmitted", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(source, [null]);
            Assert.NotNull(reserved);
            var input = Assert.IsAssignableFrom<ITaskRunColdOriginalProjectInput>(reserved);
            Assert.False(source.IsIssuedOriginalProjectInput(input)); Assert.False(source.IsOwnedOriginalProjectInput(input));
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task Borrowed_ACK_getters_with_restored_context_keep_the_actual_finite_owner_guard()
    {
        var beforeOwner = ExecutionContext.Capture()!;
        await WithSource((source, _, _) =>
        {
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(source);
            var contextType = typeof(HomeColdProjectReadReconciliation).GetNestedType("Context", BindingFlags.NonPublic)!;
            var context = Activator.CreateInstance(contextType, BindingFlags.Instance | BindingFlags.NonPublic, null,
                [source, null, (Action<Action>)(body => body()), (Action<Task>)(_ => { }), true], null)!;
            var ack = new RestoringAcknowledgment(source, beforeOwner);
            _ = typeof(HomeColdProjectReadReconciliation).GetMethod("ObserveClosedProjectAcknowledgment", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [context, ack]);
            Assert.Equal(2, ack.Callbacks);
            Assert.Null(typeof(HomeColdProjectReadReconciliation).GetField("_close", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(source));
            return Task.CompletedTask;
        });
    }
    private sealed class RestoringAcknowledgment(HomeColdProjectReadReconciliation source, ExecutionContext beforeOwner) : ITaskRunColdJournalAcknowledgment
    {
        internal int Callbacks;
        private void Observe()
        {
            ExecutionContext.Run(beforeOwner.CreateCopy(), _ =>
            {
                Callbacks++;
                Assert.Throws<InvalidOperationException>(() => { _ = source.CloseAndDrainOriginalAsync(); });
            }, null);
        }
        public ITaskRunColdJournalClaim OriginalClaim { get { Observe(); return null!; } }
        public TaskExecutionSnapshot AcknowledgedTask { get { Observe(); return null!; } }
    }
    private static async Task WithSource(Func<HomeColdProjectReadReconciliation, Exception, List<Exception>, Task> body)
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Directory.CreateTempSubdirectory("home-current-project-read-").FullName;
        HomeLocalDomainComposition? domain = null; HomeColdProjectReadReconciliation? source = null; Exception? primary = null;
        var known = new List<Exception>(); var failures = new List<Exception>(); Task? sourceClose = null, domainClose = null;
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var store = new FileHomeCoreStateStore(Path.Combine(directory, "state.json"));
            domain = new(store, new OperatingSystemPrincipalSource(), originalResourceResolvers: [new HomeColdProjectReadResourceResolver(() => source!)],
                originalActionPolicies: [new HomeColdProjectReadActionPolicySource()]);
            var actors = new NoTaskActor(); var journal = new NoJournal(actors); var unavailable = new IOException("actual missing configured Files factory"); known.Add(unavailable);
            source = new(store, domain.Profiles, domain.Resources, domain.Broker, domain.Permissions, journal, actors,
                () => throw unavailable, () => throw new IOException("native source must remain unacquired"));
            await body(source, unavailable, known);
        }
        catch (Exception cause) { primary = cause; }
        finally
        {
            try { if (source is not null) sourceClose = source.CloseAndDrainOriginalAsync(); } catch (Exception cause) { failures.Add(cause); }
            if (sourceClose is not null) try { await sourceClose; } catch (Exception cause) { failures.Add(sourceClose.Exception ?? cause); }
            try { if (domain is not null) domainClose = domain.CloseAndDrainAsync(); } catch (Exception cause) { failures.Add(cause); }
            if (domainClose is not null) try { await domainClose; } catch (Exception cause) { failures.Add(domainClose.Exception ?? cause); }
            try { Directory.Delete(directory, true); } catch (Exception cause) { failures.Add(cause); }
        }
        var unexpected = failures.SelectMany(Leaves).Distinct<Exception>(ReferenceEqualityComparer.Instance)
            .Where(cause => !known.Any(expected => ReferenceEquals(expected, cause))).ToArray();
        if (unexpected.Length != 0) throw new AggregateException("Actual Home source/domain independent cleanup failed.", primary is null ? unexpected : new[] { primary }.Concat(unexpected));
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }
    private static IEnumerable<Exception> Leaves(Exception cause) => cause is AggregateException { InnerExceptions.Count: > 0 } compound
        ? compound.InnerExceptions.SelectMany(Leaves) : [cause];
    private sealed class PublicInput : ITaskRunColdOriginalProjectInput
    {
        public Conversation OriginalConversation => null!; public ContainerDefinition OriginalContainer => null!;
        public TaskRunColdProjectIdentity OriginalIdentity => null!; public Task OriginalPreparation => Task.CompletedTask;
    }
    private sealed class NoTaskActor : IAuthenticatedResourceActorSource
    { public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) => throw new NotSupportedException("No Task grant in this source control"); }
    private sealed class NoJournal(IAuthenticatedResourceActorSource actors) : ITaskRunColdRecoveryJournal, ITaskRunColdAuthoritySourceScope
    {
        public bool HasOriginalTaskActorSource(IAuthenticatedResourceActorSource sameSource) => ReferenceEquals(actors, sameSource);
        public bool IsIssuedOriginalEntry(ITaskRunColdJournalEntry value) => false;
        public bool IsIssuedOriginalAcknowledgment(ITaskRunColdJournalAcknowledgment value, ITaskRunColdJournalClaim claim) => false;
        public Task PublishOriginalAsync(TaskRunOriginalColdCapture value, TaskRunColdOriginalSourceScope scope, CancellationToken token) => throw new NotSupportedException();
        public Task<ITaskRunColdJournalEntry?> ReadOriginalAsync(Guid task, Guid run, TaskRunColdOriginalSourceScope scope, CancellationToken token) => throw new NotSupportedException();
        public Task<ITaskRunColdJournalClaim> ClaimOriginalAsync(ITaskRunColdJournalEntry value, TaskExecutionSnapshot expected, CancellationToken token) => throw new NotSupportedException();
        public Task ValidateOriginalClaimAsync(ITaskRunColdJournalClaim claim, TaskExecutionSnapshot expected, CancellationToken token) => throw new NotSupportedException();
        public Task<ITaskRunColdJournalAcknowledgment> CommitOriginalAsync(ITaskRunColdJournalClaim claim, TaskExecutionSnapshot next, CancellationToken token) => throw new NotSupportedException();
        public Task ValidateOriginalAcknowledgmentAsync(ITaskRunColdJournalAcknowledgment acknowledgment, CancellationToken token) => throw new NotSupportedException();
        public Task ValidateOriginalRestoredInputAsync(ITaskRunColdJournalAcknowledgment acknowledgment, TaskExecutionSnapshot expected, TaskRunColdOriginalSourceScope scope, CancellationToken token) => throw new NotSupportedException();
        public Task RecordOriginalTerminalAsync(ITaskRunColdJournalAcknowledgment acknowledgment, TaskExecutionSnapshot expected, CancellationToken token) => throw new NotSupportedException();
        public Task ValidateOriginalClaimWithinSourceAsync(ITaskRunColdJournalClaim claim, TaskExecutionSnapshot expected, Action<Action> scope, Action<Task> retain, CancellationToken token) => throw new NotSupportedException();
        public ValueTask ValidateOriginalContextWithinSourceAsync(ITaskRunColdContextLease context, TaskExecutionSnapshot expected, Action<Action> scope, Action<Task> retain, CancellationToken token) => throw new NotSupportedException();
        public ValueTask ValidateOriginalClosedContextWithinSourceAsync(ITaskRunColdContextLease context, ITaskRunColdJournalAcknowledgment acknowledgment, Action<Action> scope, Action<Task> retain, CancellationToken token) => throw new NotSupportedException();
        public Task ValidateOriginalAcknowledgmentWithinSourceAsync(ITaskRunColdJournalAcknowledgment acknowledgment, Action<Action> scope, Action<Task> retain, CancellationToken token) => throw new NotSupportedException();
    }
}
