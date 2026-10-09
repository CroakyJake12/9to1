#if !ANDROID
using Haven.Application;
using Haven.Infrastructure.Native.Windows;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
namespace Haven.Desktop.Services;

/// <summary>Actual configured installer/platform composition only. The host passes its
/// preexisting enrolled artifact/root owners and protected canonical device store. No
/// implementation/path/DI declaration issues installed authority. Constructors do no IO.</summary>
internal sealed class OriginalWindowsHomePackageActivationRegistration : IAsyncDisposable
{
    internal NativeWindowsHomePackageActivationOwner OriginalActivation { get; }
    internal HomeInstalledApplicationRegistry OriginalRegistry { get; }
    internal OriginalAssistantSpacesCanonicalPackageObservationOwner OriginalSpaces { get; }
    internal IReadOnlyDictionary<Type, object> OriginalServices { get; }
    private readonly object _gate = new(); private Task? _close;
    internal OriginalWindowsHomePackageActivationRegistration(HomeNativeWindowsOwnerComponents actualHome,
        HomePackageOriginalDeviceOwner actualDevice, FileHomeCoreStateStore actualDeviceRegistry,
        string actualDeviceRegistryFile, IHomePackageOriginalArtifactProvider enrolledArtifactOwner,
        IHomePackageOriginalRootMutationPort enrolledRootOwner,
        Func<IHomePackageOriginalRootMutation?> actualCurrentRetainedRoot)
    {
        OriginalActivation = new(actualDevice, actualDeviceRegistry, actualDeviceRegistryFile, enrolledArtifactOwner,
            enrolledRootOwner, actualHome.Profiles, actualCurrentRetainedRoot, "spaces");
        OriginalRegistry = new(actualHome.StateStore, actualHome.Profiles, [OriginalActivation]);
        OriginalSpaces = new(OriginalRegistry, OriginalActivation, actualHome.Profiles, actualHome.StateStore);
        OriginalServices = new Dictionary<Type, object>
        {
            [typeof(NativeWindowsHomePackageActivationOwner)] = OriginalActivation,
            [typeof(HomeInstalledApplicationRegistry)] = OriginalRegistry,
            [typeof(IInstalledApplicationRegistry)] = OriginalRegistry,
            [typeof(IInstalledApplicationOriginalActorRegistry)] = OriginalRegistry,
            [typeof(IInstalledApplicationOriginalScopedActorRegistry)] = OriginalRegistry,
            [typeof(IEnumerable<IInstalledApplicationObservationProvider>)] = new IInstalledApplicationObservationProvider[] { OriginalActivation },
            [typeof(IOriginalAssistantSpacesPackageObservationOwner)] = OriginalSpaces,
            [typeof(OriginalWindowsHomePackageActivationRegistration)] = this,
        };
    }
    internal Task? OriginalClose { get { lock (_gate) return _close; } }
    internal void DemandExternalOriginalJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        OriginalSpaces.DemandExternalOriginalJoin(); OriginalActivation.DemandExternalOriginalJoin();
    }
    internal Task CloseAndDrainAsync()
    {
        DemandExternalOriginalJoin();
        TaskCompletionSource? start = null; Task actual;
        lock (_gate)
        {
            if (_close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Close(start.Task); }
            actual = _close;
        }
        start?.SetResult(); return actual;
    }
    private async Task Close(Task start)
    {
        await start.ConfigureAwait(false); var errors = new List<Exception>();
        Task? spaces = null, activation = null;
        try { spaces = OriginalSpaces.CloseAndDrainAsync(); await spaces.ConfigureAwait(false); } catch (Exception cause) { errors.Add(spaces?.Exception ?? cause); }
        // Even failed borrower close is joined independently; original resources remain
        // rooted and errors retained. Borrowed DeviceOwner/root channel are not disposed here.
        try { activation = OriginalActivation.CloseAndDrainAsync(); await activation.ConfigureAwait(false); } catch (Exception cause) { errors.Add(activation?.Exception ?? cause); }
        if (errors.Count != 0) throw new AggregateException("Actual installer/dependency source retirement failed.", errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
#endif
