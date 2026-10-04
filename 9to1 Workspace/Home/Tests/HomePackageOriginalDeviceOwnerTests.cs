using System.Net.Sockets;
using System.IO.Pipes;
using System.Security.Principal;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
using PermissionRisk = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk;

namespace HavenOS.Home.Tests;

/// <summary>
/// Actual Windows named pipe or Linux Unix socket/held Home lease, canonical manual broker and actual isolated File device
/// store journeys. Installed identity, catalogue, signer and root channel are explicitly supplied
/// test configuration. They do not prove protected publisher enrollment, root privilege, signed
/// native payload, production install/ABI/default activation or a genuine release declaration.
/// </summary>
public sealed class HomePackageOriginalDeviceOwnerTests
{
    [Fact]
    public Task Same_accepted_native_caller_requires_manual_approval_then_installs_once() => WithRig(async (rig, failures) =>
    {
        var review = await rig.Adapter.RequestAsync(rig.Request(), rig.Token);
        var observed = await rig.Permissions.ReadRequestObservationAsync(review.Prepared.RequestId, rig.Token);
        Assert.NotNull(observed);
        Assert.Equal("installed:" + rig.Verifier.Peer.InstalledApplicationId.ToString("N"), observed!.Caller.CallerId);
        Assert.Equal(review.Prepared.RequestId, observed.RequestId);
        Assert.Equal(HomePermissionRequestState.PendingApproval, observed.State);
        Assert.Equal(HomePackageOperationState.Pending, (await rig.Adapter.ExecuteAsync(review, rig.Token)).State);
        Assert.Equal(0, rig.Root.OpenCalls);
        Assert.True((await rig.Permissions.DecideAsync(review.Prepared.RequestId, HomeApprovalChoice.Accept, cancellationToken: rig.Token)).Succeeded);
        var actual = rig.Adapter.ExecuteAsync(review, rig.Token);
        var result = await actual;
        Assert.Equal(HomePackageOperationState.Succeeded, result.State);
        Assert.Equal(1, rig.Root.Effects);
        var snapshot = await rig.ReadDeviceAsync();
        Assert.Equal(HomePackageJournalState.Succeeded, Assert.Single(snapshot.RecentOperations).State);
        Assert.Equal("1", Assert.Single(snapshot.Packages).InstalledVersion);
        Assert.Equal(HomePermissionRequestState.Succeeded,
            (await rig.Permissions.GetAuthorizationAsync(review.Prepared.RequestId, rig.Token)).State);
        var original = rig.Owner.ObserveOriginalOperation(result.OperationId);
        Assert.True(original.CanonicalSettlementConfirmed);
        Assert.True(original.OriginalTask.IsCompletedSuccessfully);
        var staleRepeat = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Adapter.ExecuteAsync(review, rig.Token));
        failures.Observe(staleRepeat);
        Assert.Equal(1, rig.Root.Effects);
        Assert.False(ReferenceEquals(rig.PermissionStore, ((IHomePackageOriginalPlatformOwner)rig.Owner).DeviceStore));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Same_persisted_reservation_keeps_original_audit_after_owner_retirement_or_lost_ack(bool lostAcknowledgement) => WithRig(async (rig, failures) =>
    {
        var entered = NewSignal(); var release = NewSignal();
        var lost = new IOException("Controlled original reservation postcommit acknowledgement loss.");
        Task<HomePackageActionResult>? original = null;
        Task<HomePackageOriginalOperationObservation>? audit = null;
        Task? adapterClose = null, ownerClose = null;
        var review = await rig.Adapter.RequestAsync(rig.Request(), rig.Token);
        Assert.True((await rig.Permissions.DecideAsync(review.Prepared.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: rig.Token)).Succeeded);
        rig.DevicePersisted.BeforeAcknowledgement = async (id, revision, token) =>
        {
            if (id != HomePackageDatabase.RecordId || revision != 1) return;
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            if (lostAcknowledgement) throw lost;
            rig.Actors.Current = rig.Actors.Current with { AuthenticationRevision = "retired-after-original-reservation" };
        };
        try
        {
            original = rig.Adapter.ExecuteAsync(review, rig.Token);
            await entered.Task.WaitAsync(rig.Token);
            Assert.False(original.IsCompleted);
            var actualPending = await rig.ReadDeviceAsync();
            Assert.Equal(1, actualPending.Revision);
            Assert.Equal(HomePackageJournalState.Pending, Assert.Single(actualPending.RecentOperations).State);
            Assert.Equal(review.OperationId, Assert.Single(actualPending.RecentOperations).OperationId);
            Assert.Equal(0, rig.Root.Effects);
            var sameRoot = rig.Root.LastMutation ?? throw new InvalidOperationException("SAME original root mutation absent.");
            Assert.NotNull(sameRoot);
            Assert.False(sameRoot.IsClosed);
            release.TrySetResult();
            var refused = await Assert.ThrowsAsync<AggregateException>(() => original!);
            failures.Observe(refused);
            if (lostAcknowledgement) Assert.True(ContainsSame(refused, lost));
            else Assert.Contains(refused.Flatten().InnerExceptions, error => error is UnauthorizedAccessException);
            var ownerOriginal = rig.Owner.ObserveOriginalOperation(review.OperationId);
            var ownerFailure = await Assert.ThrowsAsync<AggregateException>(() => ownerOriginal.OriginalTask);
            failures.Observe(ownerFailure);
            Assert.True(ContainsSame(refused, ownerFailure));
            Assert.False(ownerOriginal.OriginalEffectAttempted);
            Assert.False(ownerOriginal.RootOutcomeKnown);
            Assert.False(ownerOriginal.CanonicalSettlementConfirmed);
            Assert.Equal(HomePackageJournalState.Pending, Assert.Single((await rig.ReadDeviceAsync()).RecentOperations).State);
            Assert.False(sameRoot.IsClosed);
            Assert.Equal(1, rig.Root.OpenCalls); Assert.Equal(0, rig.Root.Effects);
            var demands = rig.Root.DemandCalls;
            rig.DevicePersisted.BeforeAcknowledgement = null;
            rig.Actors.Current = rig.Actors.Current with { AuthenticationRevision = "retired-before-audit-only-recovery" };
            rig.Root.ConfirmOriginalNoDispatch = true;
            audit = rig.Owner.RecoverOriginalSettlementAsync(review.OperationId, CancellationToken.None);
            var recovered = await audit;
            Assert.Same(ownerOriginal.OriginalTask, recovered.OriginalTask);
            Assert.False(recovered.OriginalEffectAttempted);
            Assert.True(recovered.RootOutcomeKnown); Assert.True(recovered.CanonicalSettlementConfirmed);
            Assert.Equal(HomePackageOperationState.Rejected, recovered.ObservedRootResult!.State);
            Assert.Equal(HomePackageJournalState.Failed, Assert.Single((await rig.ReadDeviceAsync()).RecentOperations).State);
            Assert.Equal(CancellationToken.None, rig.Root.LastQueryToken);
            Assert.Equal(demands, rig.Root.DemandCalls);
            Assert.Equal(1, rig.Root.OpenCalls); Assert.Equal(0, rig.Root.Effects);
            Assert.True(sameRoot.IsClosed);
        }
        finally
        {
            release.TrySetResult();
            if (original is not null) await failures.DrainAsync(original);
            if (audit is not null) await failures.DrainAsync(audit);
            try { adapterClose = rig.Adapter.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
            try { ownerClose = rig.Owner.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
            if (adapterClose is not null) await failures.DrainPreviouslyObservedOriginalsAsync(adapterClose);
            if (ownerClose is not null) await failures.DrainPreviouslyObservedOriginalsAsync(ownerClose);
        }
    });

    [Fact]
    public Task Actor_retirement_after_original_artifact_await_refuses_before_any_prompt_or_root_effect() => WithRig(async (rig, failures) =>
    {
        var entered = NewSignal(); var release = NewSignal();
        Task<HomePackageOriginalReview>? original = null;
        rig.Artifacts.BeforeResolve = async _ => { entered.TrySetResult(); await release.Task; };
        try
        {
            original = rig.Adapter.RequestAsync(rig.Request(), rig.Token);
            await entered.Task.WaitAsync(rig.Token);
            rig.Actors.Current = rig.Actors.Current with { AuthenticationRevision = "retired-original" };
            release.TrySetResult();
            var refusal = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => original!);
            failures.Observe(refusal);
            Assert.Empty((await rig.Permissions.GetSnapshotAsync(cancellationToken: rig.Token)).PendingRequests);
            Assert.Equal(0, rig.Root.OpenCalls); Assert.Equal(0, rig.Root.Effects);
        }
        finally
        {
            release.TrySetResult();
            if (original is not null) await failures.DrainAsync(original);
        }
    });

    [Fact]
    public Task Caller_first_original_provider_cancellation_survives_later_owner_close_and_held_finally() => WithRig(async (rig, failures) =>
    {
        using var caller = new CancellationTokenSource();
        var entered = NewSignal(); var canceled = NewSignal(); var release = NewSignal();
        OperationCanceledException? exact = null;
        Task<HomePackageOriginalReview>? original = null;
        Task? adapterClose = null, ownerClose = null, sessionClose = null;
        rig.Artifacts.BeforeResolve = async token =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException error) { exact = error; canceled.TrySetResult(); throw; }
            finally { await release.Task; }
        };
        try
        {
            original = rig.Adapter.RequestAsync(rig.Request(), caller.Token);
            await entered.Task.WaitAsync(rig.Token);
            caller.Cancel();
            await canceled.Task.WaitAsync(rig.Token);
            adapterClose = rig.Adapter.CloseAndDrainAsync();
            ownerClose = rig.Owner.CloseAndDrainAsync();
            sessionClose = rig.Session.DisposeAsync().AsTask();
            Assert.Same(adapterClose, rig.Adapter.CloseAndDrainAsync());
            Assert.Same(ownerClose, rig.Owner.CloseAndDrainAsync());
            Assert.False(original.IsCompleted); Assert.False(adapterClose.IsCompleted);
            Assert.False(ownerClose.IsCompleted); Assert.False(sessionClose.IsCompleted);
            release.TrySetResult();
            var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original!);
            Assert.Same(exact, observed); failures.Observe(observed);
            var adapterFailure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapterClose!);
            Assert.Same(exact, adapterFailure); failures.Observe(adapterFailure);
            var ownerFailure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ownerClose!);
            Assert.Same(exact, ownerFailure); failures.Observe(ownerFailure);
            await sessionClose;
            Assert.Equal(0, rig.Root.OpenCalls); Assert.Equal(0, rig.Root.Effects);
        }
        finally
        {
            release.TrySetResult();
            foreach (var task in new Task?[] { original, adapterClose, ownerClose, sessionClose })
                if (task is not null) await failures.DrainAsync(task);
        }
    });

    [Fact]
    public Task Root_effect_then_caller_cancel_and_lost_ack_retains_same_known_outcome_without_repeat_dispatch() => WithRig(async (rig, failures) =>
    {
        using var caller = new CancellationTokenSource();
        var review = await rig.Adapter.RequestAsync(rig.Request(), rig.Token);
        Assert.True((await rig.Permissions.DecideAsync(review.Prepared.RequestId, HomeApprovalChoice.Accept, cancellationToken: rig.Token)).Succeeded);
        var lost = new IOException("Controlled original root acknowledgement loss after one effect.");
        rig.Root.AfterEffect = () => { caller.Cancel(); throw lost; };
        var original = rig.Adapter.ExecuteAsync(review, caller.Token);
        var observed = await Assert.ThrowsAsync<IOException>(() => original!);
        Assert.Same(lost, observed); failures.Observe(observed);
        Assert.Equal(1, rig.Root.Effects); Assert.Equal(1, rig.Root.Queries);
        Assert.Equal(CancellationToken.None, rig.Root.LastQueryToken);
        Assert.Same(rig.Root.LastOutcome, rig.Root.LastMutation!.ObservedSameOutcome);
        var state = rig.Owner.ObserveOriginalOperation(review.OperationId);
        Assert.True(state.RootOutcomeKnown); Assert.True(state.CanonicalSettlementConfirmed);
        Assert.Equal(HomePackageOperationState.Succeeded, state.ObservedRootResult!.State);
        Assert.Equal(HomePackageJournalState.Succeeded, Assert.Single((await rig.ReadDeviceAsync()).RecentOperations).State);
        Assert.Equal(HomePermissionRequestState.Succeeded,
            (await rig.Permissions.GetAuthorizationAsync(review.Prepared.RequestId, rig.Token)).State);
        var staleRepeat = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Adapter.ExecuteAsync(review, rig.Token));
        failures.Observe(staleRepeat);
        Assert.Equal(1, rig.Root.Effects);
        await rig.Adapter.RetryOriginalSettlementAsync(review, CancellationToken.None);
        Assert.Equal(1, rig.Root.Effects);
    });

    [Fact]
    public Task Known_root_effect_with_interrupted_canonical_settlement_recovers_only_original_journal_after_retirement() => WithRig(async (rig, failures) =>
    {
        var review = await rig.Adapter.RequestAsync(rig.Request(), rig.Token);
        Assert.True((await rig.Permissions.DecideAsync(review.Prepared.RequestId, HomeApprovalChoice.Accept, cancellationToken: rig.Token)).Succeeded);
        rig.Root.RefuseSettlement = true;
        var original = rig.Adapter.ExecuteAsync(review, rig.Token);
        var interruption = await Assert.ThrowsAsync<HomePackageDatabaseException>(() => original!);
        failures.Observe(interruption);
        var pending = await rig.ReadDeviceAsync();
        Assert.Equal(HomePackageJournalState.Pending, Assert.Single(pending.RecentOperations).State);
        Assert.Equal(1, rig.Root.Effects);
        var known = rig.Owner.ObserveOriginalOperation(review.OperationId);
        Assert.True(known.RootOutcomeKnown); Assert.False(known.CanonicalSettlementConfirmed);
        Assert.Equal(HomePackageOperationState.PartiallySucceeded, review.OriginalResult!.State);
        Assert.Equal(HomePermissionRequestState.PartiallyCompleted,
            (await rig.Permissions.GetAuthorizationAsync(review.Prepared.RequestId, rig.Token)).State);
        var demandCalls = rig.Root.DemandCalls;
        rig.Actors.Current = rig.Actors.Current with { AuthenticationRevision = "retired-after-known-effect" };
        rig.Root.RefuseSettlement = false;
        var audit = rig.Owner.RecoverOriginalSettlementAsync(review.OperationId, CancellationToken.None);
        var recovered = await audit;
        Assert.True(recovered.CanonicalSettlementConfirmed);
        Assert.Same(known.OriginalTask, recovered.OriginalTask);
        Assert.Equal(demandCalls, rig.Root.DemandCalls);
        Assert.Equal(1, rig.Root.OpenCalls); Assert.Equal(1, rig.Root.Effects);
        Assert.Equal(HomePackageJournalState.Succeeded, Assert.Single((await rig.ReadDeviceAsync()).RecentOperations).State);
        // The first original command and its truthful partial audit are never relabelled.
        Assert.Equal(HomePackageOperationState.PartiallySucceeded, review.OriginalResult.State);
        Assert.Equal(HomePermissionRequestState.PartiallyCompleted,
            (await rig.Permissions.GetAuthorizationAsync(review.Prepared.RequestId, CancellationToken.None)).State);
    });

    [Fact]
    public Task Ambiguous_original_root_effect_stays_pending_and_close_retains_causes_without_repeat_dispatch() => WithRig(async (rig, failures) =>
    {
        var review = await rig.Adapter.RequestAsync(rig.Request(), rig.Token);
        Assert.True((await rig.Permissions.DecideAsync(review.Prepared.RequestId, HomeApprovalChoice.Accept, cancellationToken: rig.Token)).Succeeded);
        var lost = new IOException("Controlled ambiguous root acknowledgement after one isolated effect.");
        rig.Root.AmbiguousAfterEffect = lost;
        var original = rig.Adapter.ExecuteAsync(review, rig.Token);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => original);
        Assert.True(ContainsSame(failure, lost)); failures.Observe(failure);
        var state = rig.Owner.ObserveOriginalOperation(review.OperationId);
        Assert.True(state.OriginalEffectAttempted); Assert.False(state.RootOutcomeKnown);
        Assert.False(state.CanonicalSettlementConfirmed);
        Assert.Equal(HomePackageJournalState.Pending, Assert.Single((await rig.ReadDeviceAsync()).RecentOperations).State);
        Assert.Equal(HomePermissionRequestState.PartiallyCompleted,
            (await rig.Permissions.GetAuthorizationAsync(review.Prepared.RequestId, rig.Token)).State);
        Assert.Equal(HomePackageOperationState.Rejected, (await rig.Adapter.ExecuteAsync(review, rig.Token)).State);
        Assert.Equal(1, rig.Root.OpenCalls); Assert.Equal(1, rig.Root.Effects);
        var adapterClose = rig.Adapter.CloseAndDrainAsync();
        var ownerClose = rig.Owner.CloseAndDrainAsync();
        var adapterError = await Assert.ThrowsAsync<AggregateException>(() => adapterClose);
        var ownerError = await Assert.ThrowsAsync<AggregateException>(() => ownerClose);
        Assert.True(ContainsSame(adapterError, lost)); Assert.True(ContainsSame(ownerError, lost));
        failures.Observe(adapterError); failures.Observe(ownerError);
        Assert.Equal(1, rig.Root.Effects);
    });

    private static bool ContainsSame(Exception observed, Exception exact) =>
        ReferenceEquals(observed, exact) || observed is AggregateException aggregate &&
        aggregate.InnerExceptions.Any(item => ContainsSame(item, exact));

    [Fact]
    public async Task Guarded_original_intent_is_detached_before_held_writer_and_immediate_caller_mutation()
    {
        var root = Path.Combine(Path.GetTempPath(), "home-package-detach-" + Guid.NewGuid().ToString("N"));
        var entered = NewSignal(); var release = NewSignal();
        var failures = new Failures(); Task<HomePackageDatabaseWriteResult>? original = null;
        try
        {
            Directory.CreateDirectory(root);
            var store = new FileHomeCoreStateStore(Path.Combine(root, "device.json"));
            var database = new HomePackageDatabase(store);
            var actor = new AuthenticatedResourceActor("detached-fixture", "profile", null, null, "original-v1");
            var dependencies = new[] { new HomePackageDependency("dependency.original", "1", "2") };
            var evidence = new[] { new HomePackageIntegrityEvidence("1", new string('a', 64), "fixture-key",
                HomePackageIntegrityState.Verified, DateTimeOffset.UnixEpoch) };
            var rollback = new[] { "0" };
            var succeeded = new[] { "original-step" };
            var fields = new Dictionary<string, JsonElement> { ["original"] = JsonSerializer.SerializeToElement(new { value = "before" }) };
            var package = new HomePackageDatabaseEntry("fixture.package", "fixture.app", "Fixture", null,
                "1", "1", "stable", HomePackageInstallState.Installed, HomePackageCompatibility.Compatible,
                null, dependencies, evidence, "1", rollback, true, 1) { UnknownFields = fields };
            var journal = new HomePackageJournalEntry("original-key", "original-operation", "fixture.package",
                HomePackageAction.Install.ToString(), HomePackageJournalState.Succeeded,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "ORIGINAL", false, true,
                succeeded, [], [], []) { UnknownFields = fields };
            var snapshot = HomePackageDatabaseSnapshot.Empty with { Packages = new[] { package }, RecentOperations = new[] { journal },
                UnknownFields = fields };
            var guard = new HeldGuard(actor, entered, release);
            original = database.SaveGuardedAsync(snapshot, 0, actor, guard, CancellationToken.None);
            // Method return follows its synchronous complete nested capture, before the real writer
            // can pass the held raw-current admission guard.
            dependencies[0] = new("dependency.changed"); evidence[0] = evidence[0] with { Version = "changed" };
            rollback[0] = "changed"; succeeded[0] = "changed";
            fields["original"] = JsonSerializer.SerializeToElement(new { value = "after" });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(original.IsCompleted);
            release.TrySetResult();
            Assert.True((await original).Succeeded);
            var read = await database.ReadAsync();
            Assert.True(read.Succeeded);
            var actual = Assert.Single(read.Snapshot!.Packages);
            Assert.Equal("dependency.original", Assert.Single(actual.Dependencies).PackageId);
            Assert.Equal("1", Assert.Single(actual.IntegrityEvidence).Version);
            Assert.Equal("0", Assert.Single(actual.RetainedRollbackVersions));
            Assert.Equal("original-step", Assert.Single(Assert.Single(read.Snapshot.RecentOperations).SucceededSteps));
            Assert.Equal("before", actual.UnknownFields!["original"].GetProperty("value").GetString());
            Assert.Equal("before", read.Snapshot.UnknownFields!["original"].GetProperty("value").GetString());
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            release.TrySetResult();
            if (original is not null) await failures.DrainAsync(original);
            failures.Attempt(() => { if (Directory.Exists(root)) Directory.Delete(root, true); });
        }
        failures.ThrowIfAny();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    [Fact]
    public Task Dependency_run_retains_known_effect_after_lost_ack_and_recovers_without_another_dispatch() => WithRig(async (rig, failures) =>
    {
        HomePackageOriginalLifecycleRun? run = null;
        Task<HomePackageActionResult>? original = null;
        Task<HomePackageOriginalLifecycleObservation>? audit = null;
        Task? close = null;
        var lost = new IOException("Controlled same installer root acknowledgement loss.");
        try
        {
            var registry = await rig.ReadDeviceAsync();
            var request = rig.Request();
            var platform = (IHomePackageOriginalPlatformOwner)rig.Owner;
            var material = await platform.ResolveOriginalArtifactAsync(request, registry, rig.Token)
                ?? throw new InvalidOperationException("Explicit fixture material missing.");
            var selection = HomePackageArtifactSelection.Capture(platform, material, request);
            var policy = new ScriptedLifecyclePolicy(selection);
            var planner = new HomePackageOriginalDependencyLifecycle(platform, policy, new[] { selection });
            var plan = Assert.IsType<HomePackageOriginalLifecyclePlan>(planner.Plan(request, registry, false).Plan);
            run = new(rig.Adapter, rig.Owner, planner, plan);
            var prompt = run.ContinueOriginalAsync(rig.Token);
            Assert.Equal(HomePackageOperationState.Pending, (await prompt).State);
            Assert.Equal(0, rig.Root.Effects);
            var permissionId = run.ObserveOriginal().OriginalPermissionRequestId
                ?? throw new InvalidOperationException("Original permission request absent.");
            Assert.True((await rig.Permissions.DecideAsync(permissionId, HomeApprovalChoice.Accept,
                cancellationToken: rig.Token)).Succeeded);
            rig.Root.AfterEffect = () => throw lost;
            original = run.ContinueOriginalAsync(rig.Token);
            var observed = await Assert.ThrowsAsync<IOException>(() => original!);
            Assert.Same(lost, observed); failures.Observe(observed);
            var retained = run.ObserveOriginal();
            Assert.True(retained.AuditOnlyAfterAttempt);
            var known = Assert.Single(retained.KnownOriginalResults);
            Assert.Equal(HomePackageOperationState.Succeeded, known.State);
            Assert.Equal(1, rig.Root.Effects);
            Action continueAfterAttempt = () => { run.ContinueOriginalAsync(rig.Token); };
            Assert.Throws<InvalidOperationException>(continueAfterAttempt);
            rig.Actors.Current = rig.Actors.Current with { AuthenticationRevision = "retired-before-original-installer-audit" };
            audit = run.RecoverOriginalAsync(CancellationToken.None);
            var recovered = await audit;
            Assert.Equal(known.OperationId, Assert.Single(recovered.KnownOriginalResults).OperationId);
            Assert.True(recovered.AuditOnlyAfterAttempt);
            Assert.Equal(1, rig.Root.Effects);
            Assert.Equal(HomePackageJournalState.Succeeded,
                Assert.Single((await rig.ReadDeviceAsync()).RecentOperations).State);
            var ownerOriginal = rig.Owner.ObserveOriginalOperation(known.OperationId);
            await failures.DrainPreviouslyObservedOriginalsAsync(ownerOriginal.OriginalTask);
            close = run.CloseAndDrainAsync();
            Assert.Same(close, run.CloseAndDrainAsync());
            var closeFailure = await Assert.ThrowsAsync<IOException>(() => close!);
            Assert.Same(lost, closeFailure); failures.Observe(closeFailure);
        }
        finally
        {
            if (original is not null) await failures.DrainAsync(original);
            if (audit is not null) await failures.DrainAsync(audit);
            try { if (run is not null) close ??= run.CloseAndDrainAsync(); }
            catch (Exception error) { failures.Add(error); }
            if (close is not null) await failures.DrainPreviouslyObservedOriginalsAsync(close);
        }
    });

    [Fact]
    public Task Installer_close_retains_cancel_callback_fault_and_waits_for_the_same_provider_finally() => WithRig(async (rig, failures) =>
    {
        HomePackageOriginalLifecycleRun? run = null;
        Task<HomePackageActionResult>? original = null;
        Task? close = null, seenClose = null;
        var entered = NewSignal(); var canceled = NewSignal(); var callbackEntered = NewSignal(); var release = NewSignal();
        var callbackFailure = new IOException("Controlled installer cancellation callback failure.");
        OperationCanceledException? sameProviderFailure = null;
        try
        {
            var registry = await rig.ReadDeviceAsync();
            var request = rig.Request();
            var platform = (IHomePackageOriginalPlatformOwner)rig.Owner;
            var material = await platform.ResolveOriginalArtifactAsync(request, registry, rig.Token)
                ?? throw new InvalidOperationException("Explicit fixture material missing.");
            var selection = HomePackageArtifactSelection.Capture(platform, material, request);
            var planner = new HomePackageOriginalDependencyLifecycle(platform, new ScriptedLifecyclePolicy(selection),
                new[] { selection });
            var plan = Assert.IsType<HomePackageOriginalLifecyclePlan>(planner.Plan(request, registry, false).Plan);
            run = new(rig.Adapter, rig.Owner, planner, plan);
            var sameRun = run;
            rig.Artifacts.BeforeResolve = async token =>
            {
                using var registration = token.Register(() =>
                {
                    seenClose = sameRun.CloseAndDrainAsync();
                    callbackEntered.TrySetResult();
                    throw callbackFailure;
                });
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException error)
                { sameProviderFailure = error; canceled.TrySetResult(); throw; }
                finally { await release.Task; }
            };
            original = run.ContinueOriginalAsync(rig.Token);
            await entered.Task.WaitAsync(rig.Token);
            close = run.CloseAndDrainAsync();
            await canceled.Task.WaitAsync(rig.Token);
            await callbackEntered.Task.WaitAsync(rig.Token);
            Assert.Same(close, seenClose);
            Assert.False(original.IsCompleted);
            Assert.False(close.IsCompleted);
            Assert.Equal(0, rig.Root.Effects);
            release.TrySetResult();
            var originalFailure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original!);
            Assert.Same(sameProviderFailure, originalFailure); failures.Observe(originalFailure);
            var closing = await Assert.ThrowsAsync<AggregateException>(() => close!);
            Assert.True(ContainsSame(closing, callbackFailure));
            Assert.True(ContainsSame(closing, originalFailure));
            failures.Observe(closing);
            Assert.Equal(0, rig.Root.OpenCalls);
            Assert.Equal(0, rig.Root.Effects);
        }
        finally
        {
            release.TrySetResult();
            if (original is not null) await failures.DrainAsync(original);
            try { if (run is not null) close ??= run.CloseAndDrainAsync(); }
            catch (Exception error) { failures.Add(error); }
            if (close is not null) await failures.DrainPreviouslyObservedOriginalsAsync(close);
            rig.Artifacts.BeforeResolve = null;
        }
    });

    // Explicit fixture classification only; fixture.package is NOT a genuine Home
    // release declaration and the counter root is NOT an installed privileged adapter.
    private sealed class ScriptedLifecyclePolicy(HomePackageArtifactSelection selection) : IHomePackageOriginalLifecyclePolicy
    {
        public string OriginalHomePackageId => selection.Descriptor.PackageId;
        public string Platform => selection.Descriptor.Platform;
        public string Abi => selection.Descriptor.Abi;
        public HomePackageComponentClass? ClassifyOriginal(HomePackageArtifactSelection original) =>
            ReferenceEquals(original, selection) ? HomePackageComponentClass.MandatorySharedCore : null;
        public bool SupportsOriginalAction(HomePackageArtifactSelection original, HomePackageAction action) =>
            ReferenceEquals(original, selection) && action == HomePackageAction.Install;
        public bool TrySatisfyOriginalVersion(string actualVersion, HomePackageDependency range, out bool satisfies)
        {
            satisfies = actualVersion == "1" && range.MinimumVersion is null && range.MaximumVersionExclusive is null;
            return range.MinimumVersion is null && range.MaximumVersionExclusive is null;
        }
    }

    private static async Task WithRig(Func<Rig, Failures, Task> body)
    {
        var rig = new Rig(); var failures = new Failures();
        try { await rig.InitializeAsync(); await body(rig, failures); }
        catch (Exception error) { failures.Add(error); }
        finally { await rig.CloseAsync(failures); }
        failures.ThrowIfAny();
    }
    private sealed class Failures
    {
        private readonly List<Exception> _errors = [];
        private readonly HashSet<Exception> _observed = new(ReferenceEqualityComparer.Instance);
        internal void Observe(Exception same) => _observed.Add(same);
        internal void Add(Exception error)
        {
            if (!_observed.Contains(error) && !_errors.Any(item => ReferenceEquals(item, error))) _errors.Add(error);
        }
        internal void Attempt(Action action) { try { action(); } catch (Exception error) { Add(error); } }
        internal async Task DrainAsync(Task same) { try { await same; } catch (Exception error) { Add(error); } }
        internal async Task DrainPreviouslyObservedOriginalsAsync(Task same)
        {
            try { await same; }
            catch (Exception error)
            {
                static IEnumerable<Exception> Leaves(Exception original) => original is AggregateException aggregate
                    ? aggregate.InnerExceptions.SelectMany(Leaves) : new[] { original };
                var known = _observed.SelectMany(Leaves).ToHashSet(ReferenceEqualityComparer.Instance);
                if (Leaves(error).All(known.Contains)) Observe(error);
                else Add(error); // Distinct callback/cleanup failure remains independently visible.
            }
        }
        internal void ThrowIfAny()
        {
            if (_errors.Count == 1) ExceptionDispatchInfo.Capture(_errors[0]).Throw();
            if (_errors.Count != 0) throw new AggregateException("Original package fixture body/drain/cleanup failures retained.", _errors);
        }
    }

    private sealed class Rig
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "home-package-original-" + Guid.NewGuid().ToString("N"));
        private CancellationTokenSource? _deadline, _connection, _deviceLifetime;
        private Socket? _listener, _client, _accepted;
        private NamedPipeServerStream? _pipeServer;
        private NamedPipeClientStream? _pipeClient;
        private Task? _pipeAccept, _pipeConnect;
        private HomeNativeSessionLease? _lease;
        internal readonly Actors Actors = new();
        internal Verifier Verifier = null!;
        internal HomePermissionTrustService Permissions = null!;
        internal FileHomeCoreStateStore PermissionStore = null!;
        internal HomeNativeCoreApiSessions Sessions = null!;
        internal HomeCoreRuntime Runtime = null!;
        internal HomeNativeCoreApiSessions.Session Session = null!;
        internal ArtifactProvider Artifacts = null!;
        internal RootPort Root = null!;
        internal readonly PersistedObserver DevicePersisted = new();
        internal HomePackageOriginalDeviceOwner Owner = null!;
        internal HomePackageOriginalOperationAdmission Adapter = null!;
        internal CancellationToken Token => _deadline!.Token;
        internal async Task InitializeAsync()
        {
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Actual original socket/pipe fixture requires Linux or Windows; no unsupported-platform pass is claimed.");
            _deadline = new(TimeSpan.FromSeconds(30));
            _connection = CancellationTokenSource.CreateLinkedTokenSource(_deadline.Token);
            _deviceLifetime = new();
            Directory.CreateDirectory(_root);
            Verifier = new(Actors);
            PermissionStore = new(Path.Combine(_root, "permissions.json"));
            var corePolicy = new HomeCoreServiceReadActionPolicies();
            Permissions = new(PermissionStore, (app, action) =>
                app == ArtifactProvider.OwnerApp && action == ArtifactProvider.Action
                    ? new(PermissionRisk.High, true, false, true) : corePolicy.TryGet(app, action));
            IHomeCoreApi? api = null;
            Sessions = new(Permissions, Actors, Verifier, () => api ?? throw new InvalidOperationException("Actual Core API not initialized."));
            Runtime = new(authorization: Sessions);
            api = new HomeCoreApi(Runtime, Sessions, Actors);
            Artifacts = new(); Root = new(Actors, Artifacts);
            Owner = new(new FileHomeCoreStateStore(Path.Combine(_root, "device.json"), DevicePersisted), Artifacts, Root, _deviceLifetime.Token);
            if (OperatingSystem.IsWindows())
            {
                var name = "home-package-original-" + Guid.NewGuid().ToString("N");
                _pipeServer = new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                _pipeClient = new(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
                _pipeAccept = _pipeServer.WaitForConnectionAsync(_connection.Token);
                _pipeConnect = _pipeClient.ConnectAsync(_connection.Token);
                await Task.WhenAll(_pipeAccept, _pipeConnect);
                var observed = HomeNativePeerObservation.FromConnectedWindowsPipe(_pipeServer)
                    ?? throw new UnauthorizedAccessException("The actual fixture pipe peer was not observed.");
                if (observed.ProcessId != Environment.ProcessId)
                    throw new UnauthorizedAccessException("The actual fixture pipe process differs from this test process.");
                using var current = WindowsIdentity.GetCurrent();
                if (observed.OperatingSystemPrincipalId != "windows-sid:" + current.User?.Value)
                    throw new UnauthorizedAccessException("The actual fixture SID differs from the pipe observation.");
            }
            else
            {
                _listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                _client = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                var address = new UnixDomainSocketEndPoint(Path.Combine(_root, "peer.sock"));
                _listener.Bind(address); _listener.Listen(1);
                await _client.ConnectAsync(address, Token); _accepted = await _listener.AcceptAsync(Token);
                Assert.Equal(Environment.ProcessId, HomeNativePeerObservation.FromAcceptedUnixSocket(_accepted)!.ProcessId);
            }
            _lease = await HomeNativeSessionLease.TryAcquireAsync(Actors, new Paths(_root), Token)
                ?? throw new InvalidOperationException("Actual original Home lease refused.");
            await Runtime.StartAsync(Token);
            var session = OperatingSystem.IsWindows()
                ? await Sessions.AcceptWindowsPipeAsync(_pipeServer!, _lease, _connection.Token, Token)
                : await Sessions.AcceptUnixAsync(_accepted!, _lease, _connection.Token, Token);
            if (session is null)
                throw new UnauthorizedAccessException("Supplied synthetic original installed attestation refused.");
            Session = session;
            var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(Actors, [new PackageResourceOwner(Actors)]), Permissions);
            Adapter = Session.OpenOriginalPackageOperations(Owner, broker);
        }
        internal HomePackageActionRequest Request() => new("fixture.package", HomePackageAction.Install,
            Guid.NewGuid().ToString("N"), "1", "stable", "catalogue-v1");
        internal async Task<HomePackageDatabaseSnapshot> ReadDeviceAsync()
        {
            var result = await ((IHomePackageOriginalPlatformOwner)Owner).Database.ReadAsync(Token);
            Assert.True(result.Succeeded); return result.Snapshot!;
        }
        internal async Task CloseAsync(Failures failures)
        {
            // Both borrowed owner graphs close before their one actual socket/lease/store inputs.
            Task? adapterClose = null, ownerClose = null;
            try { if (Adapter is not null) adapterClose = Adapter.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
            try { if (Owner is not null) ownerClose = Owner.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
            if (adapterClose is not null) await failures.DrainPreviouslyObservedOriginalsAsync(adapterClose);
            if (ownerClose is not null) await failures.DrainPreviouslyObservedOriginalsAsync(ownerClose);
            if (_connection is not null) failures.Attempt(_connection.Cancel);
            if (_pipeAccept is not null) await failures.DrainAsync(_pipeAccept);
            if (_pipeConnect is not null) await failures.DrainAsync(_pipeConnect);
            try { if (Session is not null) await Session.DisposeAsync(); } catch (Exception error) { failures.Add(error); }
            try { if (Sessions is not null) await Sessions.DisposeAsync(); } catch (Exception error) { failures.Add(error); }
            try { if (Runtime is not null) await Runtime.DisposeAsync(); } catch (Exception error) { failures.Add(error); }
            failures.Attempt(() => _accepted?.Dispose());
            failures.Attempt(() => _pipeServer?.Dispose()); failures.Attempt(() => _pipeClient?.Dispose());
            failures.Attempt(() => _lease?.Dispose());
            failures.Attempt(() => _client?.Dispose()); failures.Attempt(() => _listener?.Dispose());
            failures.Attempt(() => Artifacts?.Dispose()); failures.Attempt(() => _connection?.Dispose());
            failures.Attempt(() => _deviceLifetime?.Dispose()); failures.Attempt(() => _deadline?.Dispose());
            failures.Attempt(() => { if (Directory.Exists(_root)) Directory.Delete(_root, true); });
        }
    }

    private sealed class PersistedObserver : IHomePersistedWriteObserver
    {
        internal Func<string, long, CancellationToken, Task>? BeforeAcknowledgement;
        public Task OnPersistedWriteAsync(string id, long revision, CancellationToken token) =>
            BeforeAcknowledgement?.Invoke(id, revision, token) ?? Task.CompletedTask;
    }

    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        internal AuthenticatedResourceActor Current = new("package-native-fixture", "profile", null, null, "original-v1");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<AuthenticatedResourceActor?>(Current); }
    }
    private sealed class Verifier(Actors actors) : IHomeNativeInstalledPeerOriginalActorVerifier
    {
        internal readonly HomeNativeInstalledPeer Peer = new("explicit-synthetic-installed-fixture",
            Guid.NewGuid(), "fixture-install-v1", "fixture-executable", new HashSet<string> { "home.core" });
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer peer, CancellationToken token)
            => ValueTask.FromResult<HomeNativeInstalledPeer?>(null);
        public ValueTask<HomeNativeInstalledPeer?> VerifyForActorAsync(HomeNativeObservedPeer peer,
            AuthenticatedResourceActor original, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<HomeNativeInstalledPeer?>(peer.ProcessId == Environment.ProcessId &&
                SameObservedPrincipal(peer) && original == actors.Current ? Peer : null);
        }
        private static bool SameObservedPrincipal(HomeNativeObservedPeer peer)
        {
            if (OperatingSystem.IsWindows())
            {
                using var current = WindowsIdentity.GetCurrent();
                return current.User is not null && peer.OperatingSystemPrincipalId == "windows-sid:" + current.User.Value;
            }
            return OperatingSystem.IsLinux() &&
                peer.OperatingSystemPrincipalId.StartsWith("unix-euid:", StringComparison.Ordinal);
        }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root; public string DatabasePath => Path.Combine(root, "db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser"); public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs"); public string LegacyStatePath => Path.Combine(root, "legacy");
    }
    private sealed class PackageResourceOwner(Actors actors) : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => "fixture-package";
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor,
            string action, ResourceScope scope, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ResourceAccessDecision(actor == actors.Current &&
                action == ArtifactProvider.Action && scope.Id == "fixture.package" && scope.Access == ResourceAccess.Write,
                "explicit-fixture-resource-policy", actor.ActorId, "resource-v1", null));
        }
    }

    private sealed class ArtifactProvider : IHomePackageOriginalArtifactProvider, IDisposable
    {
        internal const string OwnerApp = "fixture-package-owner";
        internal const string Action = "fixture-package-owner.install";
        private readonly RSA _signer = RSA.Create(3072);
        internal readonly HomePackageArtifactDescriptor Descriptor;
        internal readonly byte[] Payload, Signed;
        internal Func<CancellationToken, Task>? BeforeResolve;
        internal ArtifactProvider()
        {
            Descriptor = new(1, "fixture.package", "fixture.app", "1", "stable", "native-app", "linux", "x64",
                [], ["home.core"], new string('a', 64), new string('b', 64), 123);
            Payload = JsonSerializer.SerializeToUtf8Bytes(Descriptor, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Signed = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, issuerKeyId = "explicit-fixture-key",
                payload = Convert.ToBase64String(Payload),
                signature = Convert.ToBase64String(_signer.SignData(Payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) });
        }
        public async ValueTask<HomePackageOriginalArtifactObservation?> ResolveAsync(HomePackageActionRequest request,
            HomePackageDatabaseSnapshot registry, CancellationToken token)
        {
            if (BeforeResolve is { } hook) await hook(token);
            token.ThrowIfCancellationRequested();
            return new(Signed, Payload, "catalogue-v1", this);
        }
        public HomePackageOriginalActionMap? ResolveAction(HomePackageAction action, HomePackageArtifactDescriptor descriptor) =>
            action == HomePackageAction.Install && descriptor.PackageId == Descriptor.PackageId
                ? new(OwnerApp, Action, "home.core", [new("fixture-package", "fixture.package", "resource-v1", ResourceAccess.Write)]) : null;
        public Task DemandOriginalCurrentAsync(HomePackageOriginalArtifactObservation original,
            AuthenticatedResourceActor actor, HomeNativeInstalledPeer caller, string session, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(original.OriginalProviderEvidence, this) || !Verify(original.SignedDescriptorBytes.Span, original.DescriptorPayloadBytes.Span))
                throw new UnauthorizedAccessException("Actual isolated fixture signature/evidence changed.");
            return Task.CompletedTask;
        }
        internal bool Verify(ReadOnlySpan<byte> signed, ReadOnlySpan<byte> payload)
        {
            using var envelope = JsonDocument.Parse(signed.ToArray());
            var bytes = Convert.FromBase64String(envelope.RootElement.GetProperty("payload").GetString()!);
            var signature = Convert.FromBase64String(envelope.RootElement.GetProperty("signature").GetString()!);
            return bytes.AsSpan().SequenceEqual(payload) &&
                _signer.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        public void Dispose() => _signer.Dispose();
    }

    // This concrete test channel executes a retained isolated counter effect and supplies the
    // actual raw File-store guard. It is NOT an installed Linux privileged/root adapter.
    private sealed class RootPort(Actors actors, ArtifactProvider artifacts) : IHomePackageOriginalRootMutationPort
    {
        internal int OpenCalls, Effects, Queries, DemandCalls;
        internal bool RefuseSettlement;
        internal bool ConfirmOriginalNoDispatch;
        internal Action? AfterEffect;
        internal Exception? AmbiguousAfterEffect;
        internal Mutation? LastMutation;
        internal HomePackageOriginalRootOutcome? LastOutcome;
        internal CancellationToken LastQueryToken;
        public async Task<IHomePackageOriginalRootMutation> OpenOriginalAsync(HomePackageOriginalRootRequest original,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await original.DemandOriginalCurrentBeforeEffectAsync(token);
            if (!artifacts.Verify(original.CopySignedDescriptor().Span, original.CopyDescriptorPayload().Span) ||
                original.Actor != actors.Current || original.OriginalActionPolicy.Risk != PermissionRisk.High)
                throw new UnauthorizedAccessException("Original isolated root tuple refused.");
            OpenCalls++;
            return LastMutation = new(this, actors, original);
        }
        internal sealed class Mutation(RootPort owner, Actors actors, HomePackageOriginalRootRequest request) : IHomePackageOriginalRootMutation
        {
            private readonly object _gate = new(); private int _attempted; private Task? _close;
            public HomePackageOriginalRootRequest OriginalRequest => request;
            public IHomeStateCommitActorGuard OriginalAdmissionGuard => new RootGuard(this, false);
            internal HomePackageOriginalRootOutcome? ObservedSameOutcome;
            internal bool IsClosed => _close is not null;
            private HomePackageOriginalRootOutcome? _knownOutcome;
            public async Task DemandOriginalChannelCurrentAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); owner.DemandCalls++;
                if (request.Actor != actors.Current || _close is not null) throw new UnauthorizedAccessException("Original fixture channel retired.");
                await request.DemandOriginalCurrentBeforeEffectAsync(token);
            }
            public async Task<HomePackageOriginalRootOutcome> ExecuteOriginalAsync(CancellationToken token)
            {
                if (Interlocked.CompareExchange(ref _attempted, 1, 0) != 0)
                    throw new InvalidOperationException("The SAME original root effect cannot dispatch twice.");
                await DemandOriginalChannelCurrentAsync(token);
                owner.Effects++;
                if (owner.AmbiguousAfterEffect is { } ambiguous) throw ambiguous;
                var descriptor = request.Artifact;
                var package = new HomePackageDatabaseEntry(descriptor.PackageId, descriptor.AppId, "Explicit fixture package",
                    null, descriptor.Version, descriptor.Version, descriptor.Channel, HomePackageInstallState.Installed,
                    HomePackageCompatibility.Compatible, null, [], [new(descriptor.Version, descriptor.PayloadSha256,
                        "explicit-fixture-key", HomePackageIntegrityState.Verified, DateTimeOffset.UtcNow)],
                    descriptor.Version, [], true, 1);
                owner.LastOutcome = _knownOutcome = new(new(request.OperationId, request.Action.PackageId, request.Action.Action,
                    HomePackageOperationState.Succeeded, "FIXTURE_ORIGINAL_INSTALLED", "One isolated fixture effect was observed.",
                    false, false, false, ["original-isolated-effect"], [], [], []), package, this);
                owner.AfterEffect?.Invoke();
                return _knownOutcome!;
            }
            public Task<HomePackageOriginalRootOutcome?> ObserveOriginalOutcomeAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); owner.Queries++; owner.LastQueryToken = token;
                if (_close is not null) throw new InvalidOperationException("Original fixture audit channel closed.");
                // Explicit isolated root-ledger observation, never inferred from caller cancellation
                // or a missing ACK. An actually attempted effect cannot take this route.
                if (_knownOutcome is null && owner.ConfirmOriginalNoDispatch && Volatile.Read(ref _attempted) == 0)
                    owner.LastOutcome = _knownOutcome = new(new(request.OperationId, request.Action.PackageId,
                        request.Action.Action, HomePackageOperationState.Rejected, "FIXTURE_ORIGINAL_NO_DISPATCH",
                        "The SAME isolated root ledger confirms no original effect dispatch.",
                        false, false, false, [], [], ["original-effect-not-dispatched"], []), null, this);
                ObservedSameOutcome = _knownOutcome;
                return Task.FromResult(ObservedSameOutcome);
            }
            public IHomeStateCommitActorGuard OriginalSettlementGuard(HomePackageOriginalRootOutcome sameKnown)
            {
                if (!ReferenceEquals(sameKnown, _knownOutcome) || !ReferenceEquals(sameKnown.OriginalRootOutcomeEvidence, this))
                    throw new UnauthorizedAccessException("SAME actual original known fixture effect required.");
                return new RootGuard(this, true);
            }
            private bool AllowsRaw(AuthenticatedResourceActor expected, bool settlement) =>
                expected == request.Actor && (settlement ?
                    _knownOutcome is not null && ReferenceEquals(_knownOutcome.OriginalRootOutcomeEvidence, this) && !owner.RefuseSettlement :
                    actors.Current == expected && _close is null);
            private sealed class RootGuard(Mutation original, bool settlement) : IHomeStateCommitActorGuard
            {
                public ValueTask<bool> CheckAsync(HomeCoreStoredState lockedState, AuthenticatedResourceActor expected,
                    HomeStateCommitPhase phase, CancellationToken token)
                {
                    token.ThrowIfCancellationRequested();
                    // Raw bound fields only, no broker/profile/root-channel/Home-store I/O or Gate reentry.
                    return ValueTask.FromResult(original.AllowsRaw(expected, settlement));
                }
            }
            public ValueTask DisposeAsync()
            {
                lock (_gate) return new(_close ??= Task.CompletedTask);
            }
        }
    }
    private sealed class HeldGuard(AuthenticatedResourceActor original,
        TaskCompletionSource entered, TaskCompletionSource release) : IHomeStateCommitActorGuard
    {
        public async ValueTask<bool> CheckAsync(HomeCoreStoredState lockedState, AuthenticatedResourceActor expected,
            HomeStateCommitPhase phase, CancellationToken token)
        {
            if (phase == HomeStateCommitPhase.Admission)
            {
                entered.TrySetResult(); await release.Task.WaitAsync(token);
            }
            return expected == original;
        }
    }
}
