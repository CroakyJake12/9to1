#if !ANDROID
using Haven.Application;
using Haven.Infrastructure.Native.Windows;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Actual cold Root inventory/index/Spaces composition. The limited
/// Home child must independently connect to the signed enrolled SCM Root before
/// startup; constructors and registry rows never create installed authority.</summary>
internal sealed class OriginalWindowsHomeRootClientRegistration : IAsyncDisposable
{
    internal NativeWindowsHomeRootClient OriginalClient { get; }
    internal HomeInstalledApplicationRegistry OriginalRegistry { get; }
    internal OriginalAssistantSpacesRootPackageObservationOwner OriginalSpaces { get; }
    internal IReadOnlyDictionary<Type, object> OriginalServices { get; }
    private readonly object _gate = new();
    private readonly CloudflareOriginalTaskLedger _closing = new();
    private Task? _close;

    internal OriginalWindowsHomeRootClientRegistration(HomeNativeWindowsOwnerComponents actualHome,
        NativeWindowsHomeRootClient actualRootClient)
    {
        OriginalClient = actualRootClient ?? throw new ArgumentNullException(nameof(actualRootClient));
        OriginalRegistry = new(actualHome.StateStore, actualHome.Profiles, [actualRootClient]);
        actualRootClient.BindOriginalHomeInventory(actualHome.StateStore, actualHome.Profiles, OriginalRegistry);
        OriginalSpaces = new(OriginalRegistry, actualRootClient, actualHome.Profiles, actualHome.StateStore);
        _closing.BindOriginalOwner(this);
        OriginalServices = new Dictionary<Type, object>
        {
            [typeof(NativeWindowsHomeRootClient)] = actualRootClient,
            [typeof(HomeInstalledApplicationRegistry)] = OriginalRegistry,
            [typeof(IInstalledApplicationRegistry)] = OriginalRegistry,
            [typeof(IInstalledApplicationOriginalActorRegistry)] = OriginalRegistry,
            [typeof(IInstalledApplicationOriginalScopedActorRegistry)] = OriginalRegistry,
            [typeof(IEnumerable<IInstalledApplicationObservationProvider>)] = new IInstalledApplicationObservationProvider[] { actualRootClient },
            [typeof(IOriginalAssistantSpacesPackageObservationOwner)] = OriginalSpaces,
            [typeof(IHomeNativeSessionHostVerifier)] = actualRootClient,
            [typeof(OriginalWindowsHomeRootClientRegistration)] = this
        };
    }
    internal bool HasOriginalComposition(HomeNativeWindowsComposition sameHome) =>
        OriginalClient.HasOriginalHomeInventory(sameHome.StateStore, sameHome.Profiles, OriginalRegistry) &&
        ReferenceEquals(sameHome.Services.GetService(typeof(IInstalledApplicationRegistry)), OriginalRegistry) &&
        ReferenceEquals(sameHome.Services.GetService(typeof(IHomeNativeInstalledPeerVerifier)), OriginalClient) &&
        ReferenceEquals(sameHome.Services.GetService(typeof(IOriginalAssistantSpacesPackageObservationOwner)), OriginalSpaces);

    internal Task? OriginalClose { get { lock (_gate) return _close; } }
    internal void DemandExternalOriginalJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        OriginalSpaces.DemandExternalOriginalJoin(); OriginalClient.DemandExternalOriginalJoin();
    }
    internal Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalJoin(); TaskCompletionSource? begin = null; Task actual;
        lock (_gate)
        {
            if (_close is null) { begin = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Close(begin.Task); }
            actual = _close;
        }
        begin?.SetResult(); return actual;
        async Task Close(Task start)
        {
            await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            await Join(OriginalSpaces.CloseAndDrainAsync).ConfigureAwait(false);
            await Join(OriginalClient.CloseAndDrainOriginalAsync).ConfigureAwait(false);
            await _closing.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (_closing.OriginalErrors.Count != 0)
                throw new AggregateException("Actual Root/Spaces original retirement remains failed and retained.", _closing.OriginalErrors);
        }
        async Task Join(Func<Task> close)
        {
            Task? raw = null;
            try { _closing.Invoke(() => { raw = close(); _ = _closing.Track(raw); return true; }); }
            catch (Exception cause) { _closing.Retain(cause); }
            if (raw is not null)
                try { await _closing.AwaitAsync(raw).ConfigureAwait(false); } catch (Exception cause) { _closing.Capture(raw, cause); }
        }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
}
#endif
