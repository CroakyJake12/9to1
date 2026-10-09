using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeDenOriginalScopedEvidenceTests
{
    [Fact]
    public async Task Accepted_actual_actor_and_Den_evidence_are_joined_before_provider_store_close()
    {
        await using var rig = await Rig.CreateAsync(); rig.Actors.Hold = true;
        var actual = rig.Provider.ReadWithinOriginalSourceAsync(rig.Provider.Store.Manifest.DenId,
            rig.Scope, rig.Retain, rig.Token).AsTask(); rig.Retain(actual);
        Task? close = null;
        try
        {
            var first = await Task.WhenAny(rig.Actors.Retained.Task, actual);
            if (ReferenceEquals(first, actual)) await actual;
            Assert.Same(rig.Actors.Retained.Task, first);
            Assert.Contains(rig.Raw, raw => ReferenceEquals(raw, rig.Actors.Held));
            close = rig.Provider.CloseAndDrainOriginalAsync(); rig.Retain(close);
            Assert.False(close.IsCompleted);
            Assert.Throws<ObjectDisposedException>(() =>
            {
                _ = rig.Provider.ReadWithinOriginalSourceAsync(rig.Provider.Store.Manifest.DenId, rig.Scope, rig.Retain, rig.Token);
            });
            rig.Actors.Release.TrySetResult();
            var observation = await actual;
            Assert.NotNull(observation); Assert.True(observation!.NewlyCreated); Assert.True(observation.IsEmpty);
            Assert.Equal(rig.Provider.Store.Manifest.DenId, observation.StoreId);
            Assert.NotEmpty(observation.Revision);
            await close; Assert.Same(close, rig.Provider.OriginalClose);
        }
        finally
        {
            rig.Actors.Release.TrySetResult();
            List<Exception> cleanupFailures = [];
            try { await actual; } catch (Exception cause) { cleanupFailures.Add(actual.Exception ?? cause); }
            if (close is not null)
                try { await close; } catch (Exception cause) { cleanupFailures.Add(close.Exception ?? cause); }
            if (cleanupFailures.Count != 0) throw new AggregateException("Actual admitted evidence and provider close failed independently.", cleanupFailures);
        }
    }

    [Fact]
    public async Task Restored_context_callback_cannot_join_its_actual_provider_original()
    {
        await using var rig = await Rig.CreateAsync();
        var outside = ExecutionContext.Capture()!; var refusals = 0;
        void Scope(Action body)
        {
            ExecutionContext.Run(outside.CreateCopy(), ignored =>
            {
                Assert.Throws<InvalidOperationException>(() => { _ = rig.Provider.CloseAndDrainOriginalAsync(); });
                Interlocked.Increment(ref refusals); body();
            }, null);
        }
        var actual = rig.Provider.ReadWithinOriginalSourceAsync(rig.Provider.Store.Manifest.DenId, Scope, rig.Retain, rig.Token).AsTask();
        rig.Retain(actual);
        var observation = await actual;
        Assert.NotNull(observation); Assert.True(refusals > 0);
        Assert.Null(rig.Provider.OriginalClose);
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "home-den-original-evidence-" + Guid.NewGuid().ToString("N"));
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(30));
        internal CancellationToken Token => _timeout.Token;
        internal HomeDenStoreEvidenceProvider Provider = null!;
        internal Actors Actors = null!;
        internal readonly List<Task> Raw = [];
        internal void Scope(Action body) => body();
        internal void Retain(Task actual) { lock (Raw) if (!Raw.Any(value => ReferenceEquals(value, actual))) Raw.Add(actual); }
        internal static async Task<Rig> CreateAsync()
        {
            var rig = new Rig();
            try
            {
                Directory.CreateDirectory(rig._root);
                var store = new FileHomeCoreStateStore(Path.Combine(rig._root, "home.json"));
                var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
                rig.Actors = new(profiles);
                rig.Provider = await HomeDenStoreEvidenceProvider.CreateAsync(Path.Combine(rig._root, "den"), rig.Actors, rig.Token);
                return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            Actors?.Release.TrySetResult(); List<Exception> errors = []; Task[] originals;
            lock (Raw) originals = Raw.ToArray();
            foreach (var raw in originals) try { await raw; } catch (Exception cause) { errors.Add(raw.Exception ?? cause); }
            if (Provider is not null) try { await Provider.CloseAndDrainOriginalAsync(); } catch (Exception cause) { errors.Add(cause); }
            if (errors.Count != 0) throw new AggregateException("Actual Den evidence sources failed; fixture storage retained at " + _root, errors);
            _timeout.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
    private sealed class Actors(HomeLocalProfileIdentity actual) : IOriginalScopedResourceActorSource
    {
        internal bool Hold;
        internal Task<AuthenticatedResourceActor?>? Held;
        internal readonly TaskCompletionSource Retained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) => actual.GetCurrentAsync(token);
        public Task<AuthenticatedResourceActor?> GetCurrentWithinOriginalSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            var hold = Hold; Hold = false;
            Task<AuthenticatedResourceActor?>? original = null;
            scope(() =>
            {
                original = Read(); if (hold) Held = original;
                retain(original); if (hold) Retained.TrySetResult();
            });
            return original ?? throw new InvalidOperationException("The actual scoped actor did not return its original.");
            async Task<AuthenticatedResourceActor?> Read()
            {
                var current = await actual.GetCurrentAsync(scope, retain, token);
                if (hold) await Release.Task; return current;
            }
        }
    }
}
