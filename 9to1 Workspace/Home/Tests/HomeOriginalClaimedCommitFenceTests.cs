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

    private static IEnumerable<Exception> Leaves(Exception cause) => cause is AggregateException group
        ? group.InnerExceptions.SelectMany(Leaves) : [cause];
    private static async Task Run(Func<Rig, Task> body)
    {
        var rig = await Rig.Create(); List<Exception> failures = [];
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
        internal static async Task<Rig> Create()
        {
            var rig = new Rig(); Directory.CreateDirectory(rig.Root);
            try
            {
                rig.OriginalPrincipal = await rig.Principal.Actual.GetPrincipalAsync(rig.Token);
                rig.State = new(Path.Combine(rig.Root, "home.json")); rig.Profiles = new(rig.State, rig.Principal);
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
                Assert.True((await permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept, rig.Token)).Succeeded);
                rig.Capability = await rig.Broker.BeginExecutionCapabilityAsync(review.RequestId, arguments, rig.Token)
                    ?? throw new InvalidOperationException("No actual approved capability.");
                Assert.Equal(HomeResourceClaimDisposition.Claimed, (await rig.Broker.ClaimExecutionObservedAsync(rig.Capability,
                    "den", action, [resource], arguments, rig.Token)).Disposition);
                var state = await rig.State.ReadAsync(rig.Token); Assert.True(state.IsSuccess);
                var profile = Assert.Single(state.State!.Records, value => value.RecordType == "home.local-profile");
                rig.Fence = await HomeClaimedResourceCommitFence.CaptureWithinOriginalSourceAsync(rig.Broker, rig.State, rig.Profiles, ownership,
                    "den", rig.Den.Store.Manifest.DenId, rig.Capability, actor, [profile], () => true, rig.Scope, rig.Retain, rig.Token)
                    ?? throw new InvalidOperationException("No actual original fence.");
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
