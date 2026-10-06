using System.Collections.Concurrent;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class HomeOriginalScopedCallbackPhaseTests
{
    // Exercise the actual gate, late process-lock and held-read helper overloads.
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public Task Callback_retained_beyond_synchronous_scope_cannot_start_a_late_Home_factory(int kind) => Run(async rig =>
    {
        Action? saved = null; int calls = 0; int target = kind == 1 ? 2 : 1;
        void Scope(Action body) { if (++calls == target) saved = body; else body(); }
        Task actual;
        if (kind == 2)
        {
            rig.Lease = await rig.Own(rig.Store.AcquireLocalOperationLeaseAsync(rig.Profiles, rig.Actor, default).AsTask()); Assert.NotNull(rig.Lease);
            var scoped = Assert.IsAssignableFrom<IHomeOriginalScopedLocalOperationLease>(rig.Lease);
            actual = rig.Own(scoped.IsCurrentAsync(Scope, rig.Raw.Add, default).AsTask());
        }
        else actual = rig.Own(rig.Store.AcquireLocalOperationLeaseAsync(rig.Profiles, rig.Actor, Scope, rig.Raw.Add, default).AsTask());
        rig.Expected.Add(actual); await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.True(actual.IsFaulted); Assert.NotNull(saved); var rawCount = rig.Raw.Count; var principalCount = rig.Principal.Reads;
        Assert.Throws<InvalidOperationException>((Action)(() => saved!()));
        Assert.Equal(rawCount, rig.Raw.Count); Assert.Equal(principalCount, rig.Principal.Reads); Assert.Empty(rig.Raw.OfType<Task<FileStream>>());
        if (kind != 2)
        {
            rig.Lease = await rig.Own(rig.Store.AcquireLocalOperationLeaseAsync(rig.Profiles, rig.Actor, default).AsTask());
            Assert.NotNull(rig.Lease);
        }
    });
    [Fact] public Task Concurrent_foreign_threads_cannot_claim_a_synchronous_Home_source_callback() => Run(async rig =>
    {
        var failures = new ConcurrentBag<Exception>();
        void Scope(Action body)
        {
            void Attempt() { try { body(); } catch (Exception cause) { failures.Add(cause); } }
            var first = new Thread(Attempt) { IsBackground = true }; var second = new Thread(Attempt) { IsBackground = true };
            first.Start(); second.Start();
            Assert.True(first.Join(TimeSpan.FromSeconds(10))); Assert.True(second.Join(TimeSpan.FromSeconds(10)));
        }
        var actual = rig.Own(rig.Store.AcquireLocalOperationLeaseAsync(rig.Profiles, rig.Actor, Scope, rig.Raw.Add, default).AsTask());
        rig.Expected.Add(actual); await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.True(actual.IsFaulted); Assert.Empty(rig.Raw); Assert.Equal(2, failures.Count);
        Assert.All(failures, cause => Assert.IsType<InvalidOperationException>(cause));
        rig.Lease = await rig.Own(rig.Store.AcquireLocalOperationLeaseAsync(rig.Profiles, rig.Actor, default).AsTask()); Assert.NotNull(rig.Lease);
    });
    private static async Task Run(Func<Rig, Task> body)
    {
        var rig = await Rig.Create(); var errors = new List<Exception>();
        try { await body(rig); } catch (Exception cause) { errors.Add(cause); }
        foreach (var actual in rig.Originals) try { await actual; } catch (Exception cause) { if (!rig.Expected.Contains(actual)) errors.Add((Exception?)actual.Exception ?? cause); }
        if (rig.Lease is { } lease) try { await lease.DisposeAsync(); } catch (Exception cause) { errors.Add(cause); }
        try { Directory.Delete(rig.Root, true); } catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("Actual Home synchronous callback-phase control/drain failed.", errors);
    }
    private sealed class Rig
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "haven-home-scope-phase-" + Guid.NewGuid().ToString("N"));
        internal readonly Principal Principal = new(); internal FileHomeCoreStateStore Store = null!; internal HomeLocalProfileIdentity Profiles = null!;
        internal AuthenticatedResourceActor Actor = null!; internal IHomeLocalOperationLease? Lease;
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
        internal int Reads;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token)
        { Interlocked.Increment(ref Reads); token.ThrowIfCancellationRequested(); return ValueTask.FromResult<string?>("synthetic-phase-control-principal"); }
    }
}
