using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

// Actual local Home/Den owners; no CAKE login, installed producer or browser authorization evidence.
public sealed class HomeOriginalScopedEmptyBindingTests
{
    [Fact]
    public async Task Real_new_empty_binding_retains_held_profile_source_and_checks_same_locked_profile_before_commit()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Principals.RequireScope = true; fixture.Principals.HoldNext = true;
        var actual = fixture.Retain(fixture.Ownership.BindNewEmptyWithinOriginalSourceAsync(fixture.Actor,
            "den", fixture.Provider.Store.Manifest.DenId, fixture.Scope, fixture.Retain, fixture.Token));
        try
        {
            await fixture.Principals.Entered.Task.WaitAsync(fixture.Token);
            Assert.False(actual.IsCompleted);
            Assert.Contains(fixture.Originals, raw => ReferenceEquals(raw, fixture.Principals.HeldOriginal));
            fixture.Principals.Release.TrySetResult();
            var bound = await actual;
            Assert.Equal(fixture.Actor.ProfileId, bound.ProfileId);
            Assert.Equal(fixture.Provider.Store.Manifest.DenId, bound.StoreId);
            Assert.Equal(1, fixture.State.BindingWrites);
            Assert.Contains(HomeStateCommitPhase.Admission, fixture.State.BindingChecks);
            Assert.Contains(HomeStateCommitPhase.Publication, fixture.State.BindingChecks);
            Assert.True(fixture.State.UsedOriginalScopedGuard);
            Assert.NotEmpty(fixture.Originals);
        }
        finally { fixture.Principals.Release.TrySetResult(); }
    }

    [Fact]
    public async Task Real_existing_unbound_Den_is_refused_even_when_empty_and_scoped_without_creating_ownership()
    {
        await using var fixture = await Fixture.CreateAsync(reopenExisting: true);
        fixture.Principals.RequireScope = true;
        var actual = fixture.Retain(fixture.Ownership.BindNewEmptyWithinOriginalSourceAsync(fixture.Actor,
            "den", fixture.Provider.Store.Manifest.DenId, fixture.Scope, fixture.Retain, fixture.Token));
        var refusal = await Record.ExceptionAsync(() => actual);
        Assert.IsType<UnauthorizedAccessException>(refusal);
        fixture.Expect(actual, refusal!);
        Assert.Equal(0, fixture.State.BindingWrites);
        Assert.Empty(fixture.State.BindingChecks);
        var read = await fixture.State.ReadAsync(fixture.Token);
        Assert.True(read.IsSuccess);
        Assert.DoesNotContain(read.State!.Records, record => record.RecordType == "home.local-store-ownership");
        Assert.True((await fixture.Provider.Store.ObserveOwnershipAsync(fixture.Token)).IsEmpty);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        [ThreadStatic] private static int _scopeDepth;
        private readonly string _root = Path.Combine(Path.GetTempPath(), "home-scoped-empty-" + Guid.NewGuid().ToString("N"));
        private readonly CancellationTokenSource _controls = new(TimeSpan.FromSeconds(30));
        internal CancellationToken Token => _controls.Token;
        private readonly List<(Task Task, Exception Cause)> _expected = [];
        internal readonly List<Task> Originals = [];
        internal readonly Principals Principals = new(() => _scopeDepth != 0);
        internal TrackingState State = null!;
        internal HomeLocalProfileIdentity Profiles = null!;
        internal AuthenticatedResourceActor Actor = null!;
        internal HomeDenStoreEvidenceProvider Provider = null!;
        internal HomeLocalStoreOwnership Ownership = null!;
        internal void Scope(Action callback) { _scopeDepth++; try { callback(); } finally { _scopeDepth--; } }
        internal void Retain(Task task) { lock (Originals) if (!Originals.Any(raw => ReferenceEquals(raw, task))) Originals.Add(task); }
        internal Task<T> Retain<T>(Task<T> task) { Retain((Task)task); return task; }
        internal void Expect(Task task, Exception cause) => _expected.Add((task, cause));
        internal static async Task<Fixture> CreateAsync(bool reopenExisting = false)
        {
            var fixture = new Fixture();
            try
            {
                Directory.CreateDirectory(fixture._root);
                fixture.State = new(new FileHomeCoreStateStore(Path.Combine(fixture._root, "home.json")));
                fixture.Profiles = new(fixture.State, fixture.Principals);
                fixture.Actor = await fixture.Profiles.GetCurrentAsync(fixture.Token)
                    ?? throw new InvalidOperationException("The actual OS profile was unavailable.");
                var actors = new ScopedActors(fixture);
                fixture.Provider = await HomeDenStoreEvidenceProvider.CreateAsync(Path.Combine(fixture._root, "den"),
                    actors, fixture.Token);
                if (reopenExisting)
                {
                    await fixture.Provider.DisposeAsync();
                    fixture.Provider = await HomeDenStoreEvidenceProvider.OpenAsync(Path.Combine(fixture._root, "den"),
                        actors, fixture.Token);
                }
                var permissions = new HavenOS.Home.PermissionsTrustNotifications.HomePermissionTrustService(fixture.State, (_, _) => null);
                fixture.Ownership = new(fixture.State, fixture.Profiles,
                    new HomeLocalStoreEvidenceRegistry([new Evidence(fixture.Provider)]), permissions);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            Principals.Release.TrySetResult(); var errors = new List<Exception>(); Task[] originals;
            lock (Originals) originals = Originals.ToArray();
            foreach (var actual in originals)
                try { await actual; }
                catch (Exception cause)
                {
                    if (!_expected.Any(expected => ReferenceEquals(expected.Task, actual) && ReferenceEquals(expected.Cause, cause)
                        && actual.Exception is { InnerExceptions.Count: 1 } failure && ReferenceEquals(failure.InnerExceptions[0], cause)))
                        errors.Add(actual.Exception ?? cause);
                }
            if (Provider is not null) try { await Provider.DisposeAsync(); } catch (Exception cause) { errors.Add(cause); }
            if (errors.Count != 0) throw new AggregateException("Actual Home/Den cleanup failed; storage was retained.", errors);
            _controls.Dispose();
            Directory.Delete(_root, true);
        }
    }

    private sealed class ScopedActors(Fixture owner) : IAuthenticatedResourceActorSource
    {
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) =>
            owner.Profiles.GetCurrentAsync(owner.Scope, owner.Retain, token);
    }
    private sealed class Evidence(HomeDenStoreEvidenceProvider actual) : IHomeOriginalScopedLocalStoreEvidenceProvider
    {
        public string ResourceKind => actual.ResourceKind;
        public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string id, CancellationToken token) => actual.ReadAsync(id, token);
        public async ValueTask<HomeLocalStoreEvidence?> ReadWithinOriginalSourceAsync(string id, Action<Action> scope,
            Action<Task> retain, CancellationToken token)
        {
            Task<HomeLocalStoreEvidence?>? source = null;
            scope(() => { source = actual.ReadAsync(id, token).AsTask(); retain(source); });
            return await (source ?? throw new InvalidOperationException("The real evidence source was not invoked."));
        }
    }
    private sealed class Principals(Func<bool> hasScope) : ITrustedHostPrincipalSource
    {
        private readonly OperatingSystemPrincipalSource _actual = new();
        internal bool RequireScope, HoldNext;
        internal Task<string?>? HeldOriginal;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token)
        {
            if (RequireScope && !hasScope()) throw new InvalidOperationException("A real principal source escaped its original physical scope.");
            var hold = HoldNext; HoldNext = false;
            var source = Read(token, hold);
            if (hold) { HeldOriginal = source; Entered.TrySetResult(); }
            return new(source);
        }
        private async Task<string?> Read(CancellationToken token, bool hold)
        { var principal = await _actual.GetPrincipalAsync(token); if (hold) await Release.Task; return principal; }
    }
    private sealed class TrackingState(IHomeCoreStateStore actual) : IHomeCoreStateStore
    {
        internal int BindingWrites;
        internal bool UsedOriginalScopedGuard;
        internal readonly List<HomeStateCommitPhase> BindingChecks = [];
        public Task<HomeStateReadResult> ReadAsync(CancellationToken token = default) => actual.ReadAsync(token);
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expected, CancellationToken token = default) => actual.WriteAsync(record, expected, token);
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long expected, AuthenticatedResourceActor actor,
            IHomeStateCommitActorGuard guard, CancellationToken token = default)
        {
            if (record.RecordType != "home.local-store-ownership") return actual.WriteGuardedAsync(record, expected, actor, guard, token);
            BindingWrites++; UsedOriginalScopedGuard = guard is IHomeOriginalScopedStateCommitActorGuard;
            return actual.WriteGuardedAsync(record, expected, actor, new Guard(guard, BindingChecks), token);
        }
        private sealed class Guard(IHomeStateCommitActorGuard actual, List<HomeStateCommitPhase> checks) : IHomeStateCommitActorGuard
        {
            public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor actor, HomeStateCommitPhase phase,
                CancellationToken token) { checks.Add(phase); return actual.CheckAsync(state, actor, phase, token); }
        }
    }
}
