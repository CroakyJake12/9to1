using System.Runtime.ExceptionServices;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Actual Windows OS profile, File state, process lease, Core and pipe listener.
/// No synthetic installed verifier or installed/privileged effect is supplied.</summary>
public sealed class HomeNativeWindowsCompositionTests
{
    private sealed class WindowsFactAttribute : FactAttribute
    { public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Requires the actual Windows kernel."; } }

    [WindowsFact]
    public Task Original_producer_starts_real_core_with_one_profile_permission_graph_and_denies_copied_callers() =>
        WithOriginalAsync(async rig =>
        {
            rig.Compose(new OperatingSystemPrincipalSource());
            var original = rig.Composition ?? throw new InvalidOperationException("The original Home producer was not acquired.");
            var start = rig.Start = original.StartOriginalAsync();
            Assert.Same(start, original.StartOriginalAsync());
            await start.WaitAsync(rig.Token);
            Assert.Same(rig.Start, original.OriginalStartTask);
            Assert.Same(original.StateStore, original.Services.GetService(typeof(IHomeCoreStateStore)));
            Assert.Same(original.Profiles, original.Services.GetService(typeof(IAuthenticatedResourceActorSource)));
            Assert.Same(original.Sessions, original.Services.GetService(typeof(IHomeCoreAuthorization)));
            Assert.Same(original.Runtime, original.Services.GetService(typeof(HomeCoreRuntime)));
            Assert.Same(original.Broker, original.Services.GetService(typeof(HomeResourceOperationBroker)));
            Assert.Same(original.Ownership, original.Services.GetService(typeof(IResourceStoreOwnershipReceiptAuthority)));
            Assert.True(HomeLocalReadComposition.IsBound(original.StateStore, original.Profiles, original.Ownership));
            Assert.False(original.InstalledPeerAdmissionConfigured);
            foreach (var id in new[] { "home.core", "home.state", "permissions.trust", "productivity.engine" })
            {
                var service = Assert.Single(original.Runtime.Current.Services, row => row.ServiceId == id);
                Assert.True(service.IsAvailable);
                Assert.Equal(HomeServiceLifecycleState.Ready, service.State);
                Assert.Equal(HomeCoreServiceCatalog.CurrentContractVersion, service.ContractVersion);
            }
            Assert.False(Assert.Single(original.Runtime.Current.Services, row => row.ServiceId == "packages").IsAvailable);
            var actor = await original.Profiles.GetCurrentAsync(rig.Token);
            Assert.NotNull(actor);
            Assert.Null(actor.AccountId);
            Assert.Null(actor.OrganisationId);
            Assert.True((await original.StateStore.ReadAsync(rig.Token)).IsSuccess);
            rig.Read = original.Api.GetServicesAsync(new("copied-observation", "test", "public-record"), rig.Token);
            var refused = await rig.Read;
            Assert.False(refused.Succeeded);
            Assert.Null(refused.Value);
            var close = rig.Close = original.CloseAndDrainAsync();
            Assert.Same(close, original.CloseAndDrainAsync());
            await close.WaitAsync(rig.Token);
            Assert.Same(close, original.OriginalCloseTask);
            Assert.True(close.IsCompletedSuccessfully);
            Assert.True((await original.StateStore.ReadAsync(rig.Token)).IsSuccess);
        });

    [WindowsFact]
    public Task Close_publishes_same_task_before_original_profile_cancellation_and_waits_held_finally() =>
        WithOriginalAsync(async rig =>
        {
            var source = rig.HeldPrincipal = new(new OperatingSystemPrincipalSource());
            rig.Compose(source);
            var original = rig.Composition ?? throw new InvalidOperationException("The original Home producer was not acquired.");
            Task? reentered = null;
            source.OnCancellation = () => reentered = original.CloseAndDrainAsync();
            var start = rig.Start = original.StartOriginalAsync();
            await source.Entered.Task.WaitAsync(rig.Token);
            var close = rig.Close = original.CloseAndDrainAsync();
            await source.FinallyEntered.Task.WaitAsync(rig.Token);
            Assert.Same(close, reentered);
            Assert.Same(close, original.OriginalCloseTask);
            Assert.False(start.IsCompleted);
            Assert.False(close.IsCompleted);
            Assert.False(rig.Token.IsCancellationRequested);
            source.ReleaseFinally.TrySetResult();
            var startFailure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
            var closeFailure = await Record.ExceptionAsync(() => close);
            Assert.Same(startFailure, closeFailure);
            rig.ObserveExpected(startFailure);
            Assert.True(start.IsCompleted);
            Assert.True(close.IsCompleted);
            Assert.Same(close, original.CloseAndDrainAsync());
        });

    private static async Task WithOriginalAsync(Func<OriginalRig, Task> body)
    {
        var rig = new OriginalRig();
        Exception? primary = null;
        List<Exception> cleanup = [];
        try { await body(rig); }
        catch (Exception error) { primary = error; }
        finally
        {
            try { await rig.CloseOriginalsAsync(); }
            catch (Exception error) { Add(cleanup, error); }
        }
        if (primary is not null) Add(cleanup, primary);
        if (cleanup.Count == 1) ExceptionDispatchInfo.Capture(cleanup[0]).Throw();
        if (cleanup.Count > 1) throw new AggregateException("Original Windows producer assertion and cleanup failed.", cleanup);
    }

    private sealed class OriginalRig
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "home-original-windows-producer-" + Guid.NewGuid().ToString("N"));
        private readonly HashSet<Exception> _observed = new(ReferenceEqualityComparer.Instance);
        private CancellationTokenSource? _bound;
        internal HomeNativeWindowsComposition? Composition;
        internal Task? Start;
        internal Task<HomeCoreOperationResult<IReadOnlyList<HomeServiceDescriptor>>>? Read;
        internal Task? Close;
        internal HeldPrincipal? HeldPrincipal;
        // The owning project genuinely uses xUnit 2.9.3, which has no v3 TestContext.
        // This bound controls test observation only; the Home owner has its own process token.
        internal CancellationToken Token => (_bound ??= new CancellationTokenSource(TimeSpan.FromSeconds(30))).Token;

        internal void Compose(ITrustedHostPrincipalSource actual)
        {
            var token = Token;
            token.ThrowIfCancellationRequested();
            Composition = new(new FileHomeCoreStateStore(Path.Combine(_root, "Home", "home-core-state.json")),
                actual, new OriginalPaths(_root), new("home.producer.test." + Guid.NewGuid().ToString("N")));
        }
        internal void ObserveExpected(Exception error) => _observed.Add(error);
        internal async Task CloseOriginalsAsync()
        {
            List<Exception> failures = [];
            HeldPrincipal?.ReleaseFinally.TrySetResult();
            try { if (Composition is not null) Close = Composition.CloseAndDrainAsync(); }
            catch (Exception error) { Add(failures, error); }
            if (Start is not null) await Settle(Start);
            if (Read is not null) await Settle(Read);
            if (Close is not null) await Settle(Close);
            try { if (Composition is not null) await Composition.DisposeAsync(); }
            catch (Exception error) { if (!_observed.Contains(error)) Add(failures, error); }
            try { _bound?.Dispose(); } catch (Exception error) { Add(failures, error); }
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
            catch (Exception error) { Add(failures, error); }
            if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException("Original Windows producer cleanup failed.", failures);
            async Task Settle(Task original)
            {
                try { await original; }
                catch (Exception error) { if (!_observed.Contains(error)) Add(failures, error); }
            }
        }
    }

    private sealed class HeldPrincipal(ITrustedHostPrincipalSource actual) : ITrustedHostPrincipalSource
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource FinallyEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseFinally = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action? OnCancellation;
        public async ValueTask<string?> GetPrincipalAsync(CancellationToken token)
        {
            var observed = await actual.GetPrincipalAsync(token);
            using var reentry = token.Register(() => OnCancellation?.Invoke());
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); return observed; }
            finally
            {
                FinallyEntered.TrySetResult();
                await ReleaseFinally.Task;
            }
        }
    }

    private sealed class OriginalPaths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "database");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy");
    }

    private static void Add(List<Exception> failures, Exception error)
    {
        if (!failures.Any(original => ReferenceEquals(original, error))) failures.Add(error);
    }
}
