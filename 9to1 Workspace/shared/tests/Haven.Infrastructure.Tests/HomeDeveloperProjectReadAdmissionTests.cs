using System.Runtime.ExceptionServices;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace Haven.Infrastructure.Tests;

// Real Home store/profile/approval/claim/audit; synthetic configured Files selector and principal.
// These controls neither enumerate source files nor claim installed application/kernel authority.
public sealed partial class HomeDeveloperProjectReadAdmissionTests
{
    [Fact] public Task A_public_selection_cannot_acquire_the_configured_original_read() => Run(async rig =>
    {
        var actual = rig.Own(rig.Reads.AcquireOriginalAsync(new Selection(), default));
        var error = await Assert.ThrowsAsync<AggregateException>(() => actual); rig.Expected.Add(actual); rig.ExpectedClose = true;
        Assert.Contains(Leaves(error), x => x is UnauthorizedAccessException); Assert.Equal(0, rig.Selections.Validations);
        Assert.Empty((await rig.Own(rig.Permissions.GetSnapshotAsync())).PendingRequests);
    });
    [Fact] public Task Pending_Home_read_approval_performs_no_manifest_or_finite_read() => Run(async rig =>
    {
        var actual = rig.Own(rig.Reads.AcquireOriginalAsync(rig.Selections.Original, default));
        try
        {
            var pending = await rig.Pending(); Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
            Assert.Equal(HomeDeveloperProjectReadAdmissionSource.ReadAction, pending.Scope.ActionName);
            Assert.False(actual.IsCompleted); Assert.Equal(0, rig.FiniteReads);
        }
        finally
        {
            rig.Expected.Add(actual); rig.ExpectedClose = true; rig.Reads.RequestOriginalReadRetirement();
            try { await actual; } catch { _ = actual.Exception; }
        }
    });
    [Fact] public Task Actual_Accept_admits_only_the_same_read_and_retains_the_exact_raw_task() => Run(async rig =>
    {
        var read = await rig.Accept(); await rig.Own(rig.Reads.ValidateOriginalAsync(rig.Selections.Original, read, default));
        var original = Task.FromResult("full original source string");
        var actual = read.RunOriginalRead(() => { rig.FiniteReads++; return original; }, default); _ = rig.Own(actual);
        Assert.Same(original, actual); Assert.Equal("full original source string", await actual); Assert.Equal(1, rig.FiniteReads);
        var close = rig.Own(rig.Reads.CloseAndDrainOriginalReadsAsync()); Assert.Same(close, rig.Reads.CloseAndDrainOriginalReadsAsync()); await close;
        Assert.True(close.IsCompletedSuccessfully); Assert.Throws<ObjectDisposedException>(() => { _ = rig.Reads.AcquireOriginalAsync(rig.Selections.Original, default); });
    });
    [Fact] public Task Held_raw_read_is_joined_and_faulted_OCE_siblings_survive_original_close() => Run(async rig =>
    {
        var read = await rig.Accept(); await rig.Own(read.RevalidateOriginalAsync(default));
        var raw = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new OperationCanceledException("actual raw READ Task is faulted"); var second = new IOException("independent source read cause");
        var actual = rig.Own(read.RunOriginalRead(() => raw.Task, default)); Task? close = null;
        try
        {
            rig.Reads.RequestOriginalReadRetirement(); close = rig.Own(rig.Reads.CloseAndDrainOriginalReadsAsync());
            Assert.Same(raw.Task, actual); Assert.False(close.IsCompleted);
        }
        finally
        {
            raw.TrySetException([first, second]); rig.Expected.Add(actual); rig.ExpectedClose = true;
            try { await actual; } catch { _ = actual.Exception; }
            if (close is not null) { var error = await Assert.ThrowsAsync<AggregateException>(() => close); rig.Expected.Add(close); Assert.Contains(first, Leaves(error)); Assert.Contains(second, Leaves(error)); }
        }
        Assert.True(actual.IsFaulted); Assert.NotNull(close); Assert.True(close.IsFaulted);
    });
    [Fact] public Task Restored_execution_context_source_callback_cannot_join_its_own_read_owner() => Run(async rig =>
    {
        var previous = ExecutionContext.Capture(); Assert.NotNull(previous); var read = await rig.Accept();
        var entered = 0;
        rig.Selections.OnValidate = () => ExecutionContext.Run(previous!, _ =>
        {
            entered++; Assert.Throws<InvalidOperationException>(() => { _ = rig.Reads.CloseAndDrainOriginalReadsAsync(); });
        }, null);
        await rig.Own(read.RevalidateOriginalAsync(default)); Assert.True(entered > 0);
        rig.Selections.OnValidate = null;
    });
    [Fact] public Task Real_Home_caller_revocation_blocks_the_next_finite_source_read() => Run(async rig =>
    {
        var read = await rig.Accept(); await rig.Own(read.RevalidateOriginalAsync(default));
        var actor = await rig.Own(rig.Profiles.GetCurrentAsync(default)); Assert.NotNull(actor);
        Assert.True((await rig.Own(rig.Permissions.BlockCallerAsync(actor!.ActorId))).Succeeded);
        var validation = rig.Own(read.RevalidateOriginalAsync(default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => validation); rig.Expected.Add(validation); rig.ExpectedClose = true;
        Assert.Throws<UnauthorizedAccessException>(() => read.RunOriginalRead(() => { rig.FiniteReads++; return Task.CompletedTask; }, default));
        Assert.Equal(0, rig.FiniteReads);
    });
    private static IEnumerable<Exception> Leaves(Exception value) => value is AggregateException group ? group.InnerExceptions.SelectMany(Leaves) : [value];
    private static async Task Run(Func<Rig, Task> body)
    {
        var rig = new Rig(); var failures = new List<Exception>();
        try { await body(rig); } catch (Exception cause) { failures.Add(cause); }
        try { await rig.Reads.CloseAndDrainOriginalReadsAsync(); } catch (Exception cause) { if (!rig.ExpectedClose) failures.Add(cause); }
        foreach (var original in rig.Originals)
            try { await original; } catch (Exception cause) { if (!rig.Expected.Contains(original)) failures.Add(cause); _ = original.Exception; }
        try { Directory.Delete(rig.Root, true); } catch (Exception cause) { failures.Add(cause); }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("Actual Home READ control or owned cleanup failed.", failures);
    }
    private sealed class Rig
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "haven-dev-read-" + Guid.NewGuid().ToString("N"));
        internal readonly FileHomeCoreStateStore Store; internal readonly HomeLocalProfileIdentity Profiles;
        internal readonly HomePermissionTrustService Permissions; internal readonly HomeDeveloperProjectReadAdmissionSource Reads;
        internal readonly Selections Selections = new(); internal readonly List<Task> Originals = [];
        internal readonly HashSet<Task> Expected = new(ReferenceEqualityComparer.Instance);
        internal bool ExpectedClose; internal int FiniteReads;
        internal Rig()
        {
            Directory.CreateDirectory(Root); Store = new(Path.Combine(Root, "home.json")); Profiles = new(Store, new Principal());
            var policy = new HomeDeveloperProjectReadActionPolicySource(); Permissions = new(Store, policy.TryGet);
            HomeDeveloperProjectReadAdmissionSource? actual = null;
            var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(Profiles, [new Resolver(() => actual!)]), Permissions);
            Reads = actual = new(Store, Profiles, broker, Permissions, () => Selections);
        }
        internal T Own<T>(T actual) where T : Task { Originals.Add(actual); return actual; }
        internal async Task<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest> Pending()
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < deadline)
            {
                var snapshot = await Own(Permissions.GetSnapshotAsync());
                var request = snapshot.PendingRequests.SingleOrDefault(); if (request is not null) return request;
                await Task.Delay(10);
            }
            throw new TimeoutException("Actual Home READ review was not published.");
        }
        internal async Task<IDeveloperProjectOriginalReadAdmission> Accept()
        {
            var original = Own(Reads.AcquireOriginalAsync(Selections.Original, default)); var request = await Pending();
            Assert.True((await Own(Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept))).Succeeded);
            return await original;
        }
    }
    private sealed class Principal : ITrustedHostPrincipalSource
    { public ValueTask<string?> GetPrincipalAsync(CancellationToken token) => ValueTask.FromResult<string?>("synthetic-device-local-dev-read-principal"); }
    private sealed class Selection : IDeveloperProjectOriginalReadSelection { }
    private sealed class Selections : IDeveloperProjectOriginalPhysicalReadSelectionSource
    {
        internal readonly Selection Original = new(); internal int Validations; internal Action? OnValidate;
        internal Task? OriginalValidation;
        private readonly ResourceScope _scope = new("dev.project.source", "synthetic-configured-selection", "original-config-revision", ResourceAccess.Read);
        public bool IsIssuedOriginal(IDeveloperProjectOriginalReadSelection same) => ReferenceEquals(same, Original);
        public void DemandExternalOriginalReadSelectionJoin() { }
        public bool IsIssuedOriginalPhysicalBinding(IDeveloperProjectOriginalReadSelection selection, IDeveloperProjectOriginalPhysicalSelection physical) => false;
        public IReadOnlyList<ResourceScope> GetOriginalReadScopes(IDeveloperProjectOriginalReadSelection same)
            => IsIssuedOriginal(same) ? [_scope] : throw new UnauthorizedAccessException();
        public Task RevalidateOriginalAsync(IDeveloperProjectOriginalReadSelection same, AuthenticatedResourceActor actor, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); if (!IsIssuedOriginal(same)) throw new UnauthorizedAccessException();
            Validations++; OnValidate?.Invoke(); return OriginalValidation ?? Task.CompletedTask;
        }
    }
    private sealed class Resolver(Func<HomeDeveloperProjectReadAdmissionSource> source) : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => "dev.project.source";
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token)
            => source().EvaluateAsync(actor, action, scope, token);
    }
}
