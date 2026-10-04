using System.Runtime.ExceptionServices;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

// Real FileHome/Den state and the existing manual Home permission flow.
// Fault injection observes operation provenance; it does not issue installed authority.
public sealed class HomeDenOriginalObservationTests
{
    [Fact]
    public Task Factory_refusal_is_bound_to_its_exact_task_and_not_a_replayed_body_cause() =>
        RunOriginalAsync(async rig =>
        {
            var original = rig.Retain(rig.Factory.OpenAsync());
            var refusal = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => original);
            rig.Expect(original, refusal);
            Assert.True(rig.Factory.TryObserveOriginalPreEffectRefusal(original, refusal));
            var foreign = Task.FromException<HomePersonalDenSession>(refusal);
            _ = foreign.Exception;
            Assert.False(rig.Factory.TryObserveOriginalPreEffectRefusal(foreign, refusal));

            rig.Actors.NextFailure = refusal;
            var replay = rig.Retain(rig.Factory.OpenAsync());
            var replayed = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => replay);
            rig.Expect(replay, replayed);
            Assert.Same(refusal, replayed);
            Assert.NotSame(original, replay);
            Assert.False(rig.Factory.TryObserveOriginalPreEffectRefusal(replay, replayed));
            Assert.True(rig.Factory.TryObserveOriginalPreEffectRefusal(original, refusal));
            Assert.Equal(0, rig.Home.BindingWrites);
        });

    [Fact]
    public Task Import_refusal_does_not_mark_a_later_profile_read_replaying_the_same_exception() =>
        RunOriginalAsync(async rig =>
        {
            var original = rig.Retain(rig.Ownership.CompleteImportAsync("not-issued"));
            var refusal = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => original);
            rig.Expect(original, refusal);
            Assert.True(rig.Ownership.TryObserveOriginalPreEffectRefusal(original, refusal));

            var review = await rig.Ownership.RequestImportAsync("den", rig.Provider.Store.Manifest.DenId,
                "observation-only-test");
            Assert.Equal(HomePermissionRequestState.PendingApproval, review.State);
            Assert.True((await rig.Permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            rig.Home.NextReadFailure = refusal;
            var replay = rig.Retain(rig.Ownership.CompleteImportAsync(review.RequestId));
            var replayed = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => replay);
            rig.Expect(replay, replayed);
            Assert.Same(refusal, replayed);
            Assert.False(rig.Ownership.TryObserveOriginalPreEffectRefusal(replay, replayed));
            var foreign = Task.FromException<HomeLocalStoreBinding>(refusal);
            _ = foreign.Exception;
            Assert.False(rig.Ownership.TryObserveOriginalPreEffectRefusal(foreign, refusal));
            Assert.Equal(0, rig.Home.BindingWrites);
            Assert.True((await rig.Permissions.GetAuthorizationAsync(review.RequestId)).IsAllowed);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Audit_recovery_requires_both_exact_canonical_tasks_and_the_actual_completion(bool afterCommit) =>
        RunOriginalAsync(async rig =>
        {
            var actor = await rig.Profiles.GetCurrentAsync(default);
            Assert.NotNull(actor);
            var review = await rig.Ownership.RequestImportAsync("den", rig.Provider.Store.Manifest.DenId,
                "observation-only-audit-test");
            Assert.True((await rig.Permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            rig.Home.FailNextAudit = true;
            var original = rig.Retain(rig.Ownership.CompleteImportAsync(review.RequestId));
            var pending = await Assert.ThrowsAsync<HomeStoreImportAuditPendingException>(() => original);
            rig.Expect(original, pending);
            Assert.False(rig.Ownership.TryObserveOriginalPreEffectRefusal(original, pending));
            Assert.Equal(1, rig.Home.BindingWrites);

            var foreignSuccess = Task.FromResult(pending.Binding);
            Assert.False(rig.Ownership.TryObserveOriginalImportAuditRecovery(original, pending,
                foreignSuccess, actor!, out _));
            var retry = rig.Retain(rig.Ownership.RetryImportAuditAsync(review.RequestId));
            var binding = await retry;
            Assert.Same(pending.Binding, binding);
            Assert.True(rig.Ownership.TryObserveOriginalImportAuditRecovery(original, pending,
                retry, actor!, out var observation));
            Assert.NotNull(observation);
            Assert.Same(binding, observation!.Binding);
            Assert.Equal(actor, observation.Actor);
            Assert.Equal(review.RequestId, observation.RequestId);
            Assert.False(rig.Ownership.TryObserveOriginalImportAuditRecovery(original, pending,
                foreignSuccess, actor!, out _));
            var foreignPending = Task.FromException<HomeLocalStoreBinding>(pending);
            _ = foreignPending.Exception;
            Assert.False(rig.Ownership.TryObserveOriginalImportAuditRecovery(
                foreignPending, pending, retry, actor!, out _));
            Assert.False(rig.Ownership.TryObserveOriginalImportAuditRecovery(original, pending,
                retry, actor! with { AuthenticationRevision = "foreign-revision" }, out _));
            Assert.Equal(1, rig.Home.BindingWrites);
            Assert.Equal(HomePermissionRequestState.Succeeded,
                (await rig.Permissions.GetAuthorizationAsync(review.RequestId)).State);
        }, afterCommit);

    private static async Task RunOriginalAsync(Func<Rig, Task> body, bool afterCommit = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "home-den-observation-" + Guid.NewGuid().ToString("N"));
        Rig? rig = null;
        var errors = new List<Exception>();
        try
        {
            Directory.CreateDirectory(root);
            rig = new Rig(root, afterCommit);
            await rig.InitializeOriginalAsync();
            await body(rig);
        }
        catch (Exception error) { Add(error); }
        finally
        {
            if (rig is not null)
            {
                rig.Actors.NextFailure = null;
                rig.Home.NextReadFailure = null;
                rig.Home.FailNextAudit = false;
                foreach (var original in rig.Originals)
                {
                    try { await original; }
                    catch (Exception error)
                    {
                        if (!rig.IsExpectedOriginal(original, error)) Add(error);
                    }
                }
                if (rig.OriginalProvider is { } provider)
                {
                    try { await provider.DisposeAsync(); }
                    catch (Exception error) { Add(error); }
                }
            }
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch (Exception error) { Add(error); }
        }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Original Den observation and independent cleanup failed.", errors);
        void Add(Exception error)
        {
            if (!errors.Any(prior => ReferenceEquals(prior, error))) errors.Add(error);
        }
    }

    private sealed class Rig
    {
        private readonly string _root;
        private readonly List<(Task Original, Exception Cause)> _expected = [];
        public List<Task> Originals { get; } = [];
        public FaultStore Home { get; }
        public HomeLocalProfileIdentity Profiles { get; }
        public ReplayActors Actors { get; }
        public HomePermissionTrustService Permissions { get; }
        public HomeDenStoreEvidenceProvider? OriginalProvider { get; private set; }
        public HomeDenStoreEvidenceProvider Provider => OriginalProvider ??
            throw new InvalidOperationException("The actual original Den provider has not been acquired.");
        public HomeLocalStoreOwnership Ownership { get; private set; } = null!;
        public HomePersonalDenFactory Factory { get; private set; } = null!;

        public Rig(string root, bool afterCommit)
        {
            _root = root;
            Home = new FaultStore(new FileHomeCoreStateStore(Path.Combine(root, "home.json")), afterCommit);
            Profiles = new HomeLocalProfileIdentity(Home, new OperatingSystemPrincipalSource());
            Actors = new ReplayActors(Profiles);
            Permissions = new HomePermissionTrustService(Home, (target, action) =>
                target == "9to1.home.local-profile" && action == "home.profile.importStore"
                    ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, true) : null);
        }

        public async Task InitializeOriginalAsync()
        {
            OriginalProvider = await HomeDenStoreEvidenceProvider.CreateAsync(Path.Combine(_root, "den"), Profiles);
            Ownership = new HomeLocalStoreOwnership(Home, Profiles,
                new HomeLocalStoreEvidenceRegistry([Provider]), Permissions);
            Factory = new HomePersonalDenFactory(Provider,
                new HomeResourceStoreOwnershipAuthority(Ownership, Profiles), Actors);
        }

        public Task<T> Retain<T>(Task<T> original) { Originals.Add(original); return original; }
        public void Expect(Task original, Exception cause) => _expected.Add((original, cause));
        public bool IsExpectedOriginal(Task original, Exception cause) =>
            _expected.Any(value => ReferenceEquals(value.Original, original) && ReferenceEquals(value.Cause, cause));
    }

    private sealed class ReplayActors(IAuthenticatedResourceActorSource original) : IAuthenticatedResourceActorSource
    {
        public Exception? NextFailure;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct)
        {
            if (NextFailure is { } error) { NextFailure = null; throw error; }
            return original.GetCurrentAsync(ct);
        }
    }

    private sealed class FaultStore(IHomeCoreStateStore original, bool afterCommit) : IHomeCoreStateStore
    {
        public Exception? NextReadFailure;
        public bool FailNextAudit;
        public int BindingWrites;
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default)
        {
            if (NextReadFailure is { } error) { NextReadFailure = null; throw error; }
            return original.ReadAsync(ct);
        }
        public async Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expected,
            CancellationToken ct = default)
        {
            if (FailNextAudit && record.RecordType == "home.permissions-trust" &&
                record.Payload.GetRawText().Contains("HOME_STORE_IMPORTED", StringComparison.Ordinal))
            {
                FailNextAudit = false;
                if (afterCommit) _ = await original.WriteAsync(record, expected, ct);
                throw new IOException("Injected original audit acknowledgement failure.");
            }
            return await original.WriteAsync(record, expected, ct);
        }
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long expected,
            AuthenticatedResourceActor actor, IHomeStateCommitActorGuard guard, CancellationToken ct = default)
        {
            if (record.RecordType == "home.local-store-ownership") BindingWrites++;
            return original.WriteGuardedAsync(record, expected, actor, guard, ct);
        }
    }
}
