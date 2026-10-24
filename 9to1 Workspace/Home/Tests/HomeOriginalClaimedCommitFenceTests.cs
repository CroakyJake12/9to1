using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

// Real local Home/Den binding, manual approval, exact claimed handle and held Home
// lease. The bounded test ACL is controlled; no native Files effect is certified.
public sealed class HomeOriginalClaimedCommitFenceTests
{
    [Fact]
    public Task Close_joins_the_accepted_held_validation_and_restored_context_cannot_join_its_own_fence() => Run(async rig =>
    {
        var prior = ExecutionContext.Capture(); Assert.NotNull(prior);
        var held = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Principal.Next = () =>
        {
            ExecutionContext.Run(prior!, _ => Assert.Throws<InvalidOperationException>((Action)(() => { _ = rig.Fence.DisposeAsync(); })), null);
            return new(held.Task);
        };
        rig.Held = held.Task;
        var actual = rig.Fence.ValidateWithinOriginalSourceAsync(rig.Scope, rig.Retain, rig.Token).AsTask(); rig.Retain(actual);
        Task? close = null; Exception? bodyFailure = null;
        try
        {
            await rig.WaitForHeldOrTerminal(actual);
            Assert.False(actual.IsCompleted); Assert.Contains(rig.RawSnapshot(), raw => ReferenceEquals(raw, held.Task));
            close = rig.Fence.DisposeWithinOriginalSourceAsync(rig.Scope, rig.Retain).AsTask(); rig.Retain(close);
            Assert.Same(close, rig.Fence.OriginalClose); Assert.False(close.IsCompleted);
        }
        catch (Exception cause) { bodyFailure = cause; throw; }
        finally
        {
            held.TrySetResult(rig.OriginalPrincipal);
            List<Exception> errors = [];
            try { await actual; } catch (Exception cause) { errors.Add(actual.Exception ?? cause); }
            try
            {
                close ??= rig.Fence.DisposeWithinOriginalSourceAsync(rig.Scope, rig.Retain).AsTask();
                rig.Retain(close);
            }
            catch (Exception cause) { errors.Add(cause); }
            if (close is not null) try { await close; } catch (Exception cause) { errors.Add(close.Exception ?? cause); }
            if (errors.Count != 0)
            {
                if (bodyFailure is not null) errors.Insert(0, bodyFailure);
                throw new AggregateException("Held validation and SAME independent fence close failed.", errors);
            }
        }
        Assert.True(await actual);
        var repeated = rig.Fence.DisposeWithinOriginalSourceAsync(rig.Scope, rig.Retain).AsTask();
        Assert.Same(close, repeated); await repeated;
        Assert.True((await rig.Broker.CompleteExecutionAsync(rig.Capability,
            new(HomePermissionRequestState.Succeeded, "TEST_FENCE_CLOSED", "Actual finite test fence closed; no owner mutation occurred.", []), rig.Token)).Succeeded);
    });

    [Fact]
    public Task Canceled_actual_principal_and_independent_post_callback_OCE_remain_two_fault_occurrences() => Run(async rig =>
    {
        var held = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Principal.Next = () => new(held.Task); rig.Held = held.Task;
        var foreign = new OperationCanceledException("Independent synchronous fence callback occurrence.");
        bool refused = false;
        void Scope(Action body)
        {
            rig.Scope(body);
            if (!refused && rig.RawSnapshot().Any(raw => ReferenceEquals(raw, held.Task))) { refused = true; throw foreign; }
        }
        var actual = rig.Fence.ValidateWithinOriginalSourceAsync(Scope, rig.Retain, rig.Token).AsTask(); rig.Retain(actual);
        Exception? failure = null;
        try
        {
            await rig.WaitForHeldOrTerminal(actual); Assert.False(actual.IsCompleted);
            Assert.True(refused); Assert.Contains(rig.RawSnapshot(), raw => ReferenceEquals(raw, held.Task));
        }
        finally
        {
            held.TrySetCanceled(rig.Token); rig.ExpectedCanceled.Add(held.Task);
            failure = await Record.ExceptionAsync(() => actual);
            if (failure is not null)
            {
                var leaves = Leaves(failure).ToArray();
                Assert.NotEmpty(leaves);
                Assert.All(leaves, cause => Assert.True(ReferenceEquals(cause, foreign) ||
                    cause is TaskCanceledException canceled && ReferenceEquals(canceled.Task, held.Task),
                    "An unknown source occurrence cannot be whitelisted as expected cancellation."));
                rig.KeepExpected(failure);
            }
        }
        Assert.IsType<AggregateException>(failure); Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
        Assert.True(held.Task.IsCanceled); Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, foreign));
        Assert.Contains(Leaves(failure!), cause => cause is OperationCanceledException && !ReferenceEquals(cause, foreign));
        rig.PreserveStorage = true;
    });

    [Fact]
    public Task A_retained_callback_cannot_begin_a_late_validation_source_after_close() => Run(async rig =>
    {
        Action? stale = null; var refusal = new IOException("Controlled post-publication scope refusal.");
        void Scope(Action body)
        {
            if (stale is null) { stale = body; rig.Scope(body); throw refusal; }
            rig.Scope(body);
        }
        var actual = rig.Fence.ValidateWithinOriginalSourceAsync(Scope, rig.Retain, rig.Token).AsTask(); rig.Retain(actual);
        var failure = await Record.ExceptionAsync(() => actual); Assert.IsType<AggregateException>(failure);
        var leaves = Leaves(failure!).ToArray(); Assert.NotEmpty(leaves);
        Assert.All(leaves, cause => Assert.Same(refusal, cause)); rig.KeepExpected(refusal);
        Assert.NotNull(stale); var count = rig.RawSnapshot().Length;
        var late = Assert.Throws<InvalidOperationException>((Action)(() => stale!())); rig.KeepExpected(late);
        Assert.Equal(count, rig.RawSnapshot().Length);
        rig.PreserveStorage = true;
    });

    [Fact]
    public Task Legacy_fence_disposal_retires_the_same_claim_before_owner_completion() => Run(async rig =>
    {
        Assert.True(await rig.Fence.ValidateAsync(rig.Token));
        await rig.Fence.DisposeAsync();
        await rig.Fence.DisposeAsync();
        Assert.True((await rig.Broker.CompleteExecutionAsync(rig.Capability,
            new(HomePermissionRequestState.Succeeded, "TEST_FENCE_CLOSED", "Actual legacy fence closed; no owner mutation occurred.", []), rig.Token)).Succeeded);
    }, useLegacyCapture: true);

    [Fact]
    public Task Legacy_gate_release_failure_is_retained_for_duplicate_disposers() => Run(async rig =>
    {
        Assert.True(await rig.Fence.ValidateAsync(rig.Token));
        var firstClose = rig.Fence.DisposeAsync().AsTask();
        var first = await Record.ExceptionAsync(() => firstClose);
        Assert.IsType<AggregateException>(first);
        Assert.Contains(Leaves(first!), cause => ReferenceEquals(cause, rig.LegacyStoreGateReleaseFailure));
        rig.KeepExpected(first!);

        var duplicateClose = rig.Fence.DisposeAsync().AsTask();
        Assert.Same(firstClose, duplicateClose);
        var duplicate = await Record.ExceptionAsync(() => duplicateClose);
        Assert.IsType<AggregateException>(duplicate);
        Assert.Contains(Leaves(duplicate!), cause => ReferenceEquals(cause, rig.LegacyStoreGateReleaseFailure));
        rig.KeepExpected(duplicate!);
        Assert.True((await rig.State.ReadAsync(rig.Token)).IsSuccess);

        var scopedClose = await Record.ExceptionAsync(() => rig.Fence.DisposeWithinOriginalSourceAsync(rig.Scope, rig.Retain).AsTask());
        Assert.IsType<AggregateException>(scopedClose);
        rig.KeepExpected(scopedClose!);
        rig.PreserveStorage = true;
    }, useLegacyCapture: true, failLegacyStoreGateRelease: true);

    [Fact]
    public async Task Legacy_lease_release_failure_settles_duplicate_disposers_with_the_same_retained_outcome()
    {
        var releaseEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowReleaseFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFailure = new IOException("Controlled post-native-close gate release callback failure.");
        // Rig.Join must retain and settle both raw disposers even if an assertion below fails.
        Action<SemaphoreSlim> releaseStoreGate = gate =>
        {
            gate.Release();
            releaseEntered.TrySetResult();
            allowReleaseFailure.Task.GetAwaiter().GetResult();
            throw releaseFailure;
        };

        await Run(async rig =>
        {
            rig.KeepExpected(releaseFailure);
            var actor = await rig.Profiles.GetCurrentAsync(rig.Token)
                ?? throw new InvalidOperationException("No actual local actor for lease-failure control.");
            var lease = await rig.State.AcquireLocalOperationLeaseAsync(rig.Profiles, actor, rig.Token)
                ?? throw new InvalidOperationException("No actual local Home operation lease.");

            var first = Task.Run(async () => await lease.DisposeAsync());
            rig.Retain(first);
            Task? duplicate = null;
            try
            {
                await releaseEntered.Task.WaitAsync(rig.Token);
                var duplicateStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                duplicate = Task.Run(async () =>
                {
                    var duplicateClose = lease.DisposeAsync();
                    duplicateStarted.TrySetResult();
                    await duplicateClose;
                });
                rig.Retain(duplicate);
                await duplicateStarted.Task.WaitAsync(rig.Token);
                Assert.False(duplicate.IsCompleted);
            }
            finally { allowReleaseFailure.TrySetResult(); }

            var firstFailure = await Record.ExceptionAsync(() => first.WaitAsync(rig.Token));
            var duplicateFailure = await Record.ExceptionAsync(() => duplicate!.WaitAsync(rig.Token));
            Assert.IsType<AggregateException>(firstFailure);
            Assert.IsType<AggregateException>(duplicateFailure);
            Assert.Contains(Leaves(firstFailure!), cause => ReferenceEquals(cause, releaseFailure));
            Assert.Contains(Leaves(duplicateFailure!), cause => ReferenceEquals(cause, releaseFailure));
            rig.KeepExpected(firstFailure!); rig.KeepExpected(duplicateFailure!);

            Assert.True((await rig.State.ReadAsync(rig.Token)).IsSuccess);
            Assert.True((await rig.Broker.CompleteExecutionAsync(rig.Capability,
                new(HomePermissionRequestState.Succeeded, "TEST_LEASE_CLOSE_SETTLED", "The original lease outcome was retained.", []), rig.Token)).Succeeded);
        }, releaseLocalLeaseStoreGate: releaseStoreGate);
    }

    [Fact]
    public async Task Original_scoped_gate_release_failure_settles_duplicate_closers()
    {
        var releaseEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowReleaseFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFailure = new IOException("Controlled original-scoped post-native-close gate release failure.");
        Action<SemaphoreSlim> releaseStoreGate = gate =>
        {
            gate.Release();
            releaseEntered.TrySetResult();
            allowReleaseFailure.Task.GetAwaiter().GetResult();
            throw releaseFailure;
        };

        await Run(async rig =>
        {
            var validation = rig.Fence.ValidateWithinOriginalSourceAsync(rig.Scope, rig.Retain, rig.Token).AsTask();
            rig.Retain(validation);
            Assert.True(await validation.WaitAsync(rig.Token));

            var close = rig.Fence.DisposeWithinOriginalSourceAsync(rig.Scope, rig.Retain).AsTask();
            rig.Retain(close);
            Task? duplicate = null;
            try
            {
                await releaseEntered.Task.WaitAsync(rig.Token);
                Assert.False(close.IsCompleted);
                duplicate = rig.Fence.DisposeWithinOriginalSourceAsync(rig.Scope, rig.Retain).AsTask();
                rig.Retain(duplicate);
                Assert.Same(close, duplicate);
                Assert.False(close.IsCompleted);
            }
            finally { allowReleaseFailure.TrySetResult(); }

            var failure = await Record.ExceptionAsync(() => close.WaitAsync(rig.Token));
            Assert.IsType<AggregateException>(failure);
            Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, releaseFailure));
            rig.KeepExpected(failure!);
            rig.PreserveStorage = true;
        }, releaseLocalLeaseStoreGate: releaseStoreGate);
    }

    [Fact]
    public async Task Ambiguous_native_close_fault_keeps_store_gate_and_settles_duplicate_disposers()
    {
        var closeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settleClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeFailure = new IOException("Controlled ambiguous native process-lock close outcome.");
        var storeGateReleaseAttempts = 0;
        Action<SemaphoreSlim> releaseStoreGate = gate =>
        {
            Interlocked.Increment(ref storeGateReleaseAttempts);
            gate.Release();
        };
        async ValueTask DisposeProcessLock(FileStream processLock)
        {
            await processLock.DisposeAsync(); // The real handle is closed before this injected ambiguous result.
            closeStarted.TrySetResult();
            await settleClose.Task;
            throw closeFailure;
        }

        await Run(async rig =>
        {
            rig.PreserveStorage = true; // The simulated unknown close must not be repaired by a new wrapper.
            rig.KeepExpected(closeFailure);
            var actor = await rig.Profiles.GetCurrentAsync(rig.Token)
                ?? throw new InvalidOperationException("No actual local actor for close-outcome control.");
            var lease = await rig.State.AcquireLocalOperationLeaseAsync(rig.Profiles, actor, rig.Token)
                ?? throw new InvalidOperationException("No actual local Home operation lease.");

            var first = Task.Run(async () => await lease.DisposeAsync());
            rig.Retain(first);
            Task? duplicate = null;
            try
            {
                await closeStarted.Task.WaitAsync(rig.Token);
                var duplicateStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                duplicate = Task.Run(async () =>
                {
                    var duplicateClose = lease.DisposeAsync();
                    duplicateStarted.TrySetResult();
                    await duplicateClose;
                });
                rig.Retain(duplicate);
                await duplicateStarted.Task.WaitAsync(rig.Token);
                Assert.False(duplicate.IsCompleted);
            }
            finally { settleClose.TrySetResult(); }

            var firstFailure = await Record.ExceptionAsync(() => first.WaitAsync(rig.Token));
            var duplicateFailure = await Record.ExceptionAsync(() => duplicate!.WaitAsync(rig.Token));
            Assert.IsType<AggregateException>(firstFailure);
            Assert.IsType<AggregateException>(duplicateFailure);
            Assert.Contains(Leaves(firstFailure!), cause => ReferenceEquals(cause, closeFailure));
            Assert.Contains(Leaves(duplicateFailure!), cause => ReferenceEquals(cause, closeFailure));
            Assert.Equal(0, Volatile.Read(ref storeGateReleaseAttempts));
            rig.KeepExpected(firstFailure!); rig.KeepExpected(duplicateFailure!);
        }, releaseLocalLeaseStoreGate: releaseStoreGate, disposeLocalOperationProcessLock: DisposeProcessLock);
    }

    [Fact]
    public async Task Original_scoped_ambiguous_native_close_fault_keeps_store_gate_and_duplicate_close_task()
    {
        var closeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settleClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeFailure = new IOException("Controlled original-scoped ambiguous native close outcome.");
        var storeGateReleaseAttempts = 0;
        Action<SemaphoreSlim> releaseStoreGate = gate =>
        {
            Interlocked.Increment(ref storeGateReleaseAttempts);
            gate.Release();
        };
        async ValueTask DisposeProcessLock(FileStream processLock)
        {
            await processLock.DisposeAsync();
            closeStarted.TrySetResult();
            await settleClose.Task;
            throw closeFailure;
        }

        await Run(async rig =>
        {
            rig.PreserveStorage = true;
            rig.KeepExpected(closeFailure);
            var validation = rig.Fence.ValidateWithinOriginalSourceAsync(rig.Scope, rig.Retain, rig.Token).AsTask();
            rig.Retain(validation);
            Assert.True(await validation.WaitAsync(rig.Token));

            var close = rig.Fence.DisposeWithinOriginalSourceAsync(rig.Scope, rig.Retain).AsTask();
            rig.Retain(close);
            try
            {
                await closeStarted.Task.WaitAsync(rig.Token);
                Assert.False(close.IsCompleted);
                var duplicate = rig.Fence.DisposeWithinOriginalSourceAsync(rig.Scope, rig.Retain).AsTask();
                rig.Retain(duplicate);
                Assert.Same(close, duplicate);
            }
            finally { settleClose.TrySetResult(); }

            var failure = await Record.ExceptionAsync(() => close.WaitAsync(rig.Token));
            Assert.IsType<AggregateException>(failure);
            Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, closeFailure));
            Assert.Equal(0, Volatile.Read(ref storeGateReleaseAttempts));
            rig.KeepExpected(failure!);
        }, releaseLocalLeaseStoreGate: releaseStoreGate, disposeLocalOperationProcessLock: DisposeProcessLock);
    }

    private static IEnumerable<Exception> Leaves(Exception cause) => cause is AggregateException group
        ? group.InnerExceptions.SelectMany(Leaves) : [cause];
    private static async Task Run(Func<Rig, Task> body, bool useLegacyCapture = false,
        bool failLegacyStoreGateRelease = false, Action<SemaphoreSlim>? releaseLocalLeaseStoreGate = null,
        Func<FileStream, ValueTask>? disposeLocalOperationProcessLock = null)
    {
        var rig = await Rig.Create(useLegacyCapture, failLegacyStoreGateRelease, releaseLocalLeaseStoreGate,
            disposeLocalOperationProcessLock);
        List<Exception> failures = [];
        try { await body(rig); } catch (Exception cause) { failures.Add(cause); }
        await rig.Join(failures);
        if (failures.Count != 0) throw new AggregateException("Actual Home fence controls or independent original cleanup failed; storage retained.", failures);
    }

    private sealed class Rig
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "home-original-fence-" + Guid.NewGuid().ToString("N"));
        private readonly CancellationTokenSource _time = new(TimeSpan.FromSeconds(30));
        internal CancellationToken Token => _time.Token;
        internal readonly Principal Principal = new(); internal string? OriginalPrincipal;
        internal FileHomeCoreStateStore State = null!; internal HomeLocalProfileIdentity Profiles = null!;
        internal HomeDenStoreEvidenceProvider Den = null!; internal HomeResourceOperationBroker Broker = null!;
        internal HomeResourceExecutionCapability Capability = null!; internal HomeClaimedResourceCommitFence Fence = null!;
        internal readonly List<Task> Raw = []; private readonly HashSet<Exception> _expected = new(ReferenceEqualityComparer.Instance);
        internal readonly HashSet<Task> ExpectedCanceled = new(ReferenceEqualityComparer.Instance);
        internal readonly TaskCompletionSource HeldRetained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task? Held; internal bool PreserveStorage;
        internal IOException? LegacyStoreGateReleaseFailure;
        internal void Scope(Action body) => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { body(); return true; });
        internal void Retain(Task raw)
        {
            lock (Raw) if (!Raw.Any(value => ReferenceEquals(value, raw))) Raw.Add(raw);
            if (ReferenceEquals(raw, Held)) HeldRetained.TrySetResult();
        }
        internal Task[] RawSnapshot() { lock (Raw) return Raw.ToArray(); }
        internal void KeepExpected(Exception cause) { foreach (var leaf in Leaves(cause)) _expected.Add(leaf); }
        internal async Task WaitForHeldOrTerminal(Task actual)
        {
            var first = await Task.WhenAny(HeldRetained.Task, actual).WaitAsync(Token);
            if (ReferenceEquals(first, actual)) { await actual; throw new InvalidOperationException("The actual source ended before retaining its held principal."); }
            await HeldRetained.Task;
        }
        internal static async Task<Rig> Create(bool useLegacyCapture = false, bool failLegacyStoreGateRelease = false,
            Action<SemaphoreSlim>? releaseLocalLeaseStoreGate = null,
            Func<FileStream, ValueTask>? disposeLocalOperationProcessLock = null)
        {
            var rig = new Rig(); Directory.CreateDirectory(rig.Root);
            try
            {
                rig.OriginalPrincipal = await rig.Principal.Actual.GetPrincipalAsync(rig.Token);
                var statePath = Path.Combine(rig.Root, "home.json");
                if (releaseLocalLeaseStoreGate is not null)
                {
                    rig.State = disposeLocalOperationProcessLock is null
                        ? new(statePath, releaseLocalLeaseStoreGate)
                        : new(statePath, releaseLocalLeaseStoreGate, disposeLocalOperationProcessLock);
                }
                else if (failLegacyStoreGateRelease)
                {
                    var failure = new IOException("Controlled legacy store-gate release failure.");
                    rig.LegacyStoreGateReleaseFailure = failure;
                    rig.State = new(statePath, gate => { gate.Release(); throw failure; });
                }
                else rig.State = new(statePath);
                rig.Profiles = new(rig.State, rig.Principal);
                var actor = await rig.Profiles.GetCurrentAsync(rig.Token) ?? throw new InvalidOperationException("No actual local actor.");
                rig.Den = await HomeDenStoreEvidenceProvider.CreateAsync(Path.Combine(rig.Root, "den"), rig.Profiles, rig.Token);
                const string action = "test.den.original-fence";
                var permissions = new HomePermissionTrustService(rig.State, (app, command) => app == "den" && command == action
                    ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.Elevated, false, false, RequiresPerActionApproval: true) : null);
                var localOwnership = new HomeLocalStoreOwnership(rig.State, rig.Profiles, new HomeLocalStoreEvidenceRegistry([rig.Den]), permissions);
                await localOwnership.BindNewEmptyWithinOriginalSourceAsync(actor, "den", rig.Den.Store.Manifest.DenId, rig.Scope, rig.Retain, rig.Token);
                var ownership = new HomeResourceStoreOwnershipAuthority(localOwnership, rig.Profiles);
                var resource = new ResourceScope("test.den.original-fence", rig.Den.Store.Manifest.DenId, "1", ResourceAccess.Write);
                var resources = new ResourceAuthorizationService(rig.Profiles, [new ControlledAcl(actor, resource)]);
                rig.Broker = new(resources, permissions);
                var arguments = JsonSerializer.SerializeToElement(new { operation = "held fence control only" });
                var review = rig.Broker.PrepareReviewForActor(actor, "den", action, [resource], arguments,
                    "Actual test Home WRITE fence only; no Den or Files mutation.", null, "original-fence-test");
                var submitted = await rig.Broker.AuthorizePreparedReviewAsync(review, rig.Token);
                Assert.Equal(HomePermissionRequestState.PendingApproval, submitted.Request!.State);
                Assert.True((await permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept, cancellationToken: rig.Token)).Succeeded);
                rig.Capability = await rig.Broker.BeginExecutionCapabilityAsync(review.RequestId, arguments, rig.Token)
                    ?? throw new InvalidOperationException("No actual approved capability.");
                Assert.Equal(HomeResourceClaimDisposition.Claimed, (await rig.Broker.ClaimExecutionObservedAsync(rig.Capability,
                    "den", action, [resource], arguments, rig.Token)).Disposition);
                var state = await rig.State.ReadAsync(rig.Token); Assert.True(state.IsSuccess);
                var profile = Assert.Single(state.State!.Records, value => value.RecordType == "home.local-profile");
                rig.Fence = useLegacyCapture
                    ? await HomeClaimedResourceCommitFence.CaptureAsync(rig.Broker, rig.State, rig.Profiles, ownership,
                        "den", rig.Den.Store.Manifest.DenId, rig.Capability, actor, [profile], () => true, rig.Token)
                    : await HomeClaimedResourceCommitFence.CaptureWithinOriginalSourceAsync(rig.Broker, rig.State, rig.Profiles, ownership,
                        "den", rig.Den.Store.Manifest.DenId, rig.Capability, actor, [profile], () => true, rig.Scope, rig.Retain, rig.Token);
                if (rig.Fence is null) throw new InvalidOperationException("No actual claimed-resource fence.");
                return rig;
            }
            catch (Exception acquisitionFailure)
            {
                rig.PreserveStorage = true; List<Exception> errors = []; await rig.Join(errors);
                if (errors.Count != 0) throw new AggregateException("Actual fixture acquisition/cleanup failed.", new[] { acquisitionFailure }.Concat(errors)); throw;
            }
        }
        internal async Task Join(List<Exception> errors)
        {
            if (Fence is not null)
            {
                try { await Fence.DisposeWithinOriginalSourceAsync(Scope, Retain); }
                catch (Exception cause) { if (!IsExpected(cause)) errors.Add(cause); else PreserveStorage = true; }
            }
            foreach (var raw in RawSnapshot())
                try { await raw; }
                catch (Exception cause)
                {
                    if (ExpectedCanceled.Contains(raw) && raw.IsCanceled) continue;
                    if (!IsExpected(raw.Exception ?? cause)) errors.Add(raw.Exception ?? cause); else PreserveStorage = true;
                }
            if (Den is not null) try { await Den.DisposeAsync(); } catch (Exception cause) { errors.Add(cause); }
            _time.Dispose();
            if (errors.Count == 0 && !PreserveStorage) Directory.Delete(Root, true);
        }
        private bool IsExpected(Exception cause)
        { var leaves = Leaves(cause).ToArray(); return leaves.Length != 0 && leaves.All(_expected.Contains); }
    }
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        internal readonly OperatingSystemPrincipalSource Actual = new(); internal Func<ValueTask<string?>>? Next;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token)
        { var next = Next; Next = null; return next is null ? Actual.GetPrincipalAsync(token) : next(); }
    }
    private sealed class ControlledAcl(AuthenticatedResourceActor original, ResourceScope resource) : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => resource.Kind;
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token) =>
            ValueTask.FromResult(new ResourceAccessDecision(actor == original && action == "test.den.original-fence" && scope == resource,
                "CONTROLLED_ACTUAL_HOME_FENCE", actor.ActorId, scope.Revision, actor.OrganisationId));
    }
}
