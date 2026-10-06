using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace Haven.Infrastructure.Tests;

// Actual Home profile/store/process lock; controlled trusted principal and lifetime scope.
// No Files/kernel/approval/process permission or whole-host proof is issued by these checks.
public sealed class HomeOriginalScopedLeaseAcquisitionTests
{
    [Fact] public Task Held_prepublication_principal_then_restored_context_callback_uses_same_physical_guard() => Run(async rig =>
    {
        var prior = ExecutionContext.Capture(); Assert.NotNull(prior); var owner = new object();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously); int calls = 0;
        rig.Principal.Read = token =>
        {
            if (++calls == 1) { reached.TrySetResult(); return new(held.Task); }
            ExecutionContext.Run(prior!, ignored => Assert.Throws<InvalidOperationException>(() => CloudflareOriginalExecutionGuard.DemandExternalJoin(owner)), null);
            return ValueTask.FromResult<string?>(Principal.Original);
        };
        void Scope(Action body) => CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () => { body(); return true; });
        var actual = rig.Own(rig.Store.AcquireLocalOperationLeaseAsync(rig.Profiles, rig.Actor, Scope, rig.Raw.Add, default).AsTask());
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(actual.IsCompleted); Assert.Contains(held.Task, rig.Raw);
            held.TrySetResult(Principal.Original); rig.Lease = await actual.WaitAsync(TimeSpan.FromSeconds(10)); Assert.NotNull(rig.Lease);
            Assert.Equal(2, calls); Assert.True(actual.IsCompletedSuccessfully); Assert.Contains(held.Task, rig.Raw);
        }
        finally { held.TrySetResult(Principal.Original); rig.Principal.Read = null; rig.Lease ??= await actual; }
    });
    [Fact] public Task Late_actual_process_lock_is_captured_and_closed_before_post_scope_fault_returns() => Run(async rig =>
    {
        var cause = new IOException("scope after actual process-lock acquisition factory"); int calls = 0;
        void Scope(Action body) { body(); if (++calls == 2) throw cause; }
        var actual = rig.Own(rig.Store.AcquireLocalOperationLeaseAsync(rig.Profiles, rig.Actor, Scope, rig.Raw.Add, default).AsTask());
        rig.Expected.Add(actual); var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.Contains(cause, Leaves(error)); Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
        var sameRawLockTask = Assert.Single(rig.Raw.OfType<Task<FileStream>>()); var sameLock = await sameRawLockTask;
        Assert.True(sameRawLockTask.IsCompletedSuccessfully); Assert.False(sameLock.CanRead); Assert.False(sameLock.CanWrite);
        rig.Lease = await rig.Own(rig.Store.AcquireLocalOperationLeaseAsync(rig.Profiles, rig.Actor, default).AsTask());
        Assert.NotNull(rig.Lease);
    });
    [Fact] public Task Prepublication_raw_faulted_OCE_siblings_survive_and_store_gate_is_released() => Run(async rig =>
    {
        var held = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Principal.Read = token => { reached.TrySetResult(); return new(held.Task); };
        var actual = rig.Own(rig.Store.AcquireLocalOperationLeaseAsync(rig.Profiles, rig.Actor, body => body(), rig.Raw.Add, default).AsTask());
        var first = new OperationCanceledException("faulted original profile acquisition"); var second = new IOException("actual sibling");
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(actual.IsCompleted);
            Assert.Contains(held.Task, rig.Raw);
        }
        finally { rig.Expected.Add(actual); held.TrySetException([first, second]); rig.Principal.Read = null; }
        var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.Contains(first, Leaves(error)); Assert.Contains(second, Leaves(error)); Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
        var sameRawLockTask = Assert.Single(rig.Raw.OfType<Task<FileStream>>()); var sameLock = await sameRawLockTask; Assert.False(sameLock.CanRead);
        rig.Lease = await rig.Own(rig.Store.AcquireLocalOperationLeaseAsync(rig.Profiles, rig.Actor, default).AsTask()); Assert.NotNull(rig.Lease);
    });
    private static IEnumerable<Exception> Leaves(Exception cause) => cause is AggregateException group ? group.InnerExceptions.SelectMany(Leaves) : [cause];
    private static async Task Run(Func<Rig, Task> body)
    {
        var rig = await Rig.Create(); var errors = new List<Exception>();
        try { await body(rig); } catch (Exception cause) { errors.Add(cause); }
        foreach (var actual in rig.Originals) try { await actual; } catch (Exception cause) { if (!rig.Expected.Contains(actual)) errors.Add((Exception?)actual.Exception ?? cause); }
        if (rig.Lease is { } lease) try { await lease.DisposeAsync(); } catch (Exception cause) { errors.Add(cause); }
        try { Directory.Delete(rig.Root, true); } catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("Actual scoped acquisition/drain control failed.", errors);
        // Required exact original causes are asserted; unknown extra failures are not waived.
    }
    private sealed class Rig
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "haven-scoped-acquire-" + Guid.NewGuid().ToString("N"));
        internal readonly Principal Principal = new(); internal FileHomeCoreStateStore Store = null!;
        internal HomeLocalProfileIdentity Profiles = null!; internal AuthenticatedResourceActor Actor = null!; internal IHomeLocalOperationLease? Lease;
        internal readonly List<Task> Raw = [], Originals = []; internal readonly HashSet<Task> Expected = new(ReferenceEqualityComparer.Instance);
        internal T Own<T>(T actual) where T : Task { Originals.Add(actual); return actual; }
        internal static async Task<Rig> Create()
        {
            var rig = new Rig(); Directory.CreateDirectory(rig.Root); rig.Store = new(Path.Combine(rig.Root, "home.json"));
            rig.Profiles = new(rig.Store, rig.Principal); rig.Actor = await rig.Profiles.GetCurrentAsync(default) ?? throw new InvalidOperationException(); return rig;
        }
    }
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        internal const string Original = "synthetic-original-acquisition-principal";
        internal Func<CancellationToken, ValueTask<string?>>? Read;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token) => Read?.Invoke(token) ?? ValueTask.FromResult<string?>(Original);
    }
}
