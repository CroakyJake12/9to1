using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace Haven.Infrastructure.Tests;

// Actual FileHomeCoreStateStore/profile/held lease; controlled trusted-principal callback
// is synthetic. No Files/kernel/native effect or policy approval is inferred from these checks.
public sealed class HomeOriginalScopedCommitCheckTests
{
    [Fact] public Task Actual_second_principal_factory_after_await_uses_same_physical_scope_and_raw_Task() => Run(async rig =>
    {
        var prior = ExecutionContext.Capture(); Assert.NotNull(prior); var owner = new object();
        var held = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var callbacks = 0;
        rig.Principal.Source = () =>
        {
            callbacks++;
            if (callbacks == 1) { reached.TrySetResult(); return new(held.Task); }
            ExecutionContext.Run(prior!, _ => Assert.Throws<InvalidOperationException>(() => CloudflareOriginalExecutionGuard.DemandExternalJoin(owner)), null);
            return ValueTask.FromResult<string?>(Principal.Value);
        };
        var raw = new List<Task>();
        void Scope(Action action) => CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () => { action(); return true; });
        var actual = rig.Own(rig.Scoped.IsCurrentAsync(Scope, raw.Add, default).AsTask());
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(actual.IsCompleted); Assert.Contains(held.Task, raw);
            Assert.Equal(1, callbacks);
        }
        finally { held.TrySetResult(Principal.Value); }
        Assert.True(await actual); Assert.Equal(2, callbacks); Assert.True(raw.Count >= 5);
        Assert.Contains(raw, task => ReferenceEquals(task, held.Task)); Assert.All(raw, task => Assert.True(task.IsCompletedSuccessfully));
    });
    [Fact] public Task Post_scope_fault_still_joins_exact_held_principal_and_faulted_OCE_direct_sibling() => Run(async rig =>
    {
        var held = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); bool acquired = false, thrown = false;
        rig.Principal.Source = () => { acquired = true; entered.TrySetResult(); return new(held.Task); };
        var raw = new List<Task>(); var scopeCause = new InvalidOperationException("scope failed after actual principal task acquisition");
        void Scope(Action action) { action(); if (acquired && !thrown) { thrown = true; throw scopeCause; } }
        var actual = rig.Own(rig.Scoped.IsCurrentAsync(Scope, raw.Add, default).AsTask());
        var first = new OperationCanceledException("the actual principal task is faulted"); var second = new IOException("independent raw sibling");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(actual.IsCompleted); Assert.Contains(held.Task, raw);
            Assert.True(thrown);
        }
        finally { held.TrySetException([first, second]); rig.Expected.Add(actual); }
        var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.Contains(scopeCause, Leaves(error)); Assert.Contains(first, Leaves(error)); Assert.Contains(second, Leaves(error));
        Assert.True(held.Task.IsFaulted); Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
    });
    [Fact] public Task Genuine_canceled_raw_principal_stays_canceled_and_retained() => Run(async rig =>
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); var rawPrincipal = Task.FromCanceled<string?>(cancel.Token);
        rig.Principal.Source = () => new(rawPrincipal); var raw = new List<Task>();
        var actual = rig.Own(rig.Scoped.IsCurrentAsync(action => action(), raw.Add, default).AsTask()); rig.Expected.Add(actual);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual);
        Assert.True(rawPrincipal.IsCanceled); Assert.True(actual.IsCanceled); Assert.False(actual.IsFaulted); Assert.Contains(rawPrincipal, raw);
    });
    [Fact] public Task Actual_changed_principal_returns_false_without_changing_held_actor_rules() => Run(async rig =>
    {
        rig.Principal.Source = () => ValueTask.FromResult<string?>("a different synthetic operating-system principal"); var raw = new List<Task>();
        var actual = rig.Own(rig.Scoped.IsCurrentAsync(action => action(), raw.Add, default).AsTask());
        Assert.False(await actual); Assert.True(actual.IsCompletedSuccessfully); Assert.NotEmpty(raw);
    });
    private static IEnumerable<Exception> Leaves(Exception cause) => cause is AggregateException group ? group.InnerExceptions.SelectMany(Leaves) : [cause];
    private static async Task Run(Func<Rig, Task> body)
    {
        var rig = await Rig.Create(); var errors = new List<Exception>();
        try { await body(rig); } catch (Exception cause) { errors.Add(cause); }
        foreach (var actual in rig.Originals) try { await actual; } catch (Exception cause) { if (!rig.Expected.Contains(actual)) errors.Add((Exception?)actual.Exception ?? cause); }
        try { await rig.Lease.DisposeAsync(); } catch (Exception cause) { errors.Add(cause); }
        try { Directory.Delete(rig.Root, true); } catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("Actual scoped Home check or independent lease cleanup failed.", errors);
        // Fault negatives require exact causes/status, without asserting absence of other unknown causes.
    }
    private sealed class Rig
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "haven-scoped-home-check-" + Guid.NewGuid().ToString("N"));
        internal readonly Principal Principal = new(); internal IHomeLocalOperationLease Lease = null!;
        internal IHomeOriginalScopedLocalOperationLease Scoped => Assert.IsAssignableFrom<IHomeOriginalScopedLocalOperationLease>(Lease);
        internal readonly List<Task> Originals = []; internal readonly HashSet<Task> Expected = new(ReferenceEqualityComparer.Instance);
        internal T Own<T>(T actual) where T : Task { Originals.Add(actual); return actual; }
        internal static async Task<Rig> Create()
        {
            var rig = new Rig(); Directory.CreateDirectory(rig.Root); var store = new FileHomeCoreStateStore(Path.Combine(rig.Root, "home.json"));
            var profile = new HomeLocalProfileIdentity(store, rig.Principal); var actor = await profile.GetCurrentAsync(default); Assert.NotNull(actor);
            rig.Lease = await store.AcquireLocalOperationLeaseAsync(profile, actor!, default) ?? throw new InvalidOperationException("Actual Home lease failed.");
            return rig;
        }
    }
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        internal const string Value = "synthetic-trusted-scoped-principal";
        internal Func<ValueTask<string?>>? Source;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Source?.Invoke() ?? ValueTask.FromResult<string?>(Value); }
    }
}
