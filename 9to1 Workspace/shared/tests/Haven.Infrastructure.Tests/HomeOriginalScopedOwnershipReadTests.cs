using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace Haven.Infrastructure.Tests;

// Actual Home store/profile/binding/receipt; store evidence and trusted-principal callbacks
// are controlled sources. No Files/native/Execute grant is inferred.
public sealed class HomeOriginalScopedOwnershipReadTests
{
    [Fact] public Task Actual_binding_receipt_and_post_await_principal_use_same_parent_scope_and_raw_tasks() => Run(async rig =>
    {
        await rig.Prepare(); var prior = ExecutionContext.Capture(); Assert.NotNull(prior);
        var held = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
        rig.Principal.Read = () =>
        {
            if (++calls == 1) return new(held.Task);
            ExecutionContext.Run(prior!, state => Assert.Throws<InvalidOperationException>(() => CloudflareOriginalExecutionGuard.DemandExternalJoin(rig.Parent)), null);
            return ValueTask.FromResult<string?>(Principal.Value);
        };
        void Retain(Task raw) { rig.OwnRaw(raw); if (ReferenceEquals(raw, held.Task)) enrolled.TrySetResult(); }
        var actual = rig.Own(rig.Authority!.GetVerifiedWithinOriginalSourceAsync("controlled", "actual-store", rig.Scope, Retain, rig.Token).AsTask());
        try
        {
            await enrolled.Task.WaitAsync(TimeSpan.FromSeconds(10), rig.Token); Assert.False(actual.IsCompleted); Assert.Contains(held.Task, rig.Originals);
        }
        finally { held.TrySetResult(Principal.Value); }
        var result = await actual; Assert.NotNull(result); Assert.NotNull(result!.Receipt);
        Assert.Equal(rig.Actor!.ProfileId, result.ProfileId); Assert.True(calls > 2);
        var current = rig.Own(rig.Authority.IsCurrentWithinOriginalSourceAsync(result, rig.Actor, rig.Scope, rig.OwnRaw, rig.Token).AsTask());
        Assert.True(await current); Assert.All(rig.Originals, raw => Assert.True(raw.IsCompletedSuccessfully));
    });

    [Fact] public Task Missing_scoped_evidence_refuses_without_legacy_evidence_fallback() => Run(async rig =>
    {
        rig.UseLegacy = true; await rig.Prepare(); rig.Evidence.Reads = 0;
        var actual = rig.Own(rig.Authority!.GetVerifiedWithinOriginalSourceAsync("controlled", "actual-store", rig.Scope, rig.OwnRaw, rig.Token).AsTask());
        var error = await Assert.ThrowsAsync<AggregateException>(() => actual); rig.Expect(error);
        Assert.Contains(Leaves(error), cause => cause is InvalidOperationException && cause.Message.StartsWith("HOME_OWNERSHIP_SCOPE_REQUIRED", StringComparison.Ordinal));
        Assert.Equal(0, rig.Evidence.Reads); Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
    });

    [Fact] public Task Post_acquisition_scope_fault_joins_same_held_evidence_and_all_faulted_raw_causes() => Run(async rig =>
    {
        await rig.Prepare(); var held = new TaskCompletionSource<HomeLocalStoreEvidence?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Evidence.Raw = held.Task; var scopeCause = new IOException("original scope failed after real evidence task capture"); bool threw = false;
        void Retain(Task raw) { rig.OwnRaw(raw); if (ReferenceEquals(raw, held.Task)) enrolled.TrySetResult(); }
        void Scope(Action body) { rig.Scope(body); if (!threw && rig.Originals.Any(raw => ReferenceEquals(raw, held.Task))) { threw = true; throw scopeCause; } }
        var actual = rig.Own(rig.Authority!.GetVerifiedWithinOriginalSourceAsync("controlled", "actual-store", Scope, Retain, rig.Token).AsTask());
        var first = new OperationCanceledException("faulted evidence OCE"); var second = new IOException("independent evidence sibling");
        try { await enrolled.Task.WaitAsync(TimeSpan.FromSeconds(10), rig.Token); Assert.False(actual.IsCompleted); Assert.Contains(held.Task, rig.Originals); }
        finally { held.TrySetException([first, second]); }
        var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.Contains(scopeCause, Leaves(error)); Assert.Contains(first, Leaves(error)); Assert.Contains(second, Leaves(error));
        rig.Expect(error); Assert.True(held.Task.IsFaulted); Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
    });

    private static IEnumerable<Exception> Leaves(Exception cause) => cause is AggregateException group ? group.InnerExceptions.SelectMany(Leaves) : [cause];
    private static async Task Run(Func<Rig, Task> body)
    {
        var rig = new Rig(); Exception? primary = null; var failures = new List<Exception>();
        try { rig.Root = Directory.CreateTempSubdirectory("scoped-ownership-").FullName; await body(rig); }
        catch (Exception cause) { primary = cause; }
        foreach (var actual in rig.Originals.Distinct<Task>(ReferenceEqualityComparer.Instance))
            try { await actual; } catch (Exception cause) { foreach (var leaf in Leaves((Exception?)actual.Exception ?? cause)) if (!rig.Expected.Contains(leaf)) failures.Add(leaf); }
        try { if (rig.Root is not null) Directory.Delete(rig.Root, true); } catch (Exception cause) { failures.Add(cause); }
        if (primary is not null) failures.Insert(0, primary);
        if (failures.Count != 0) throw new AggregateException("Actual scoped Home ownership/body and independent raw cleanup failed.", failures);
    }
    private sealed class Rig
    {
        internal string? Root; internal bool UseLegacy; internal readonly Principal Principal = new(); internal readonly Evidence Evidence = new();
        internal AuthenticatedResourceActor? Actor; internal HomeResourceStoreOwnershipAuthority? Authority;
        internal readonly object Parent = new(); internal readonly List<Task> Originals = [];
        internal readonly HashSet<Exception> Expected = new(ReferenceEqualityComparer.Instance);
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal void Scope(Action body) => CloudflareOriginalExecutionGuard.InvokeOriginal(Parent, () => { body(); return true; });
        internal void OwnRaw(Task raw) { if (!Originals.Any(value => ReferenceEquals(value, raw))) Originals.Add(raw); }
        internal T Own<T>(T raw) where T : Task { OwnRaw(raw); return raw; }
        internal void Expect(Exception error) { foreach (var leaf in Leaves(error)) Expected.Add(leaf); }
        internal async Task Prepare()
        {
            var store = new FileHomeCoreStateStore(Path.Combine(Root!, "home.json")); var profiles = new HomeLocalProfileIdentity(store, Principal);
            Actor = await Own(profiles.GetCurrentAsync(Token).AsTask()); Assert.NotNull(Actor);
            var permissions = new HomePermissionTrustService(store, (_, _) => null);
            IHomeLocalStoreEvidenceProvider provider = UseLegacy ? new LegacyEvidence(Evidence) : Evidence;
            var ownership = new HomeLocalStoreOwnership(store, profiles, new HomeLocalStoreEvidenceRegistry([provider]), permissions);
            var binding = await Own(ownership.BindNewEmptyAsync("controlled", "actual-store", Token)); Assert.NotNull(binding);
            Authority = new(ownership, profiles);
        }
    }
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        internal const string Value = "synthetic-trusted-ownership-principal"; internal Func<ValueTask<string?>>? Read;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Read?.Invoke() ?? ValueTask.FromResult<string?>(Value); }
    }
    private sealed class Evidence : IHomeOriginalScopedLocalStoreEvidenceProvider
    {
        public string ResourceKind => "controlled"; internal int Reads; internal Task<HomeLocalStoreEvidence?>? Raw;
        private static HomeLocalStoreEvidence Value => new("controlled", "actual-store", "revision1", true, true, true);
        public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken token) { token.ThrowIfCancellationRequested(); Reads++; return ValueTask.FromResult<HomeLocalStoreEvidence?>(Value); }
        public ValueTask<HomeLocalStoreEvidence?> ReadWithinOriginalSourceAsync(string storeId, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            Task<HomeLocalStoreEvidence?>? raw = null;
            scope(() => { token.ThrowIfCancellationRequested(); Reads++; raw = Raw ?? Task.FromResult<HomeLocalStoreEvidence?>(Value); retain(raw); });
            return new(raw ?? throw new InvalidOperationException("Controlled evidence factory was not invoked."));
        }
    }
    private sealed class LegacyEvidence(Evidence evidence) : IHomeLocalStoreEvidenceProvider
    { public string ResourceKind => evidence.ResourceKind; public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken token) => evidence.ReadAsync(storeId, token); }
}
