#if !ANDROID
using Haven.Application;
using Haven.Infrastructure.Native.Linux;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>One original Linux Home/store/profile/index/Root graph. Registration
/// grants nothing; the actual Root-controlled child independently connects first.</summary>
internal sealed class DesktopOriginalLinuxHomeRootClientRegistration : IAsyncDisposable
{
    internal LinuxHomeRootClient OriginalClient { get; }
    internal HomeInstalledApplicationRegistry OriginalRegistry { get; }
    internal IReadOnlyDictionary<Type, object> OriginalServices { get; }
    internal DesktopOriginalLinuxHomeRootClientRegistration(HomeNativeWindowsOwnerComponents home, LinuxHomeRootClient client)
    {
        OriginalClient = client; OriginalRegistry = new(home.StateStore, home.Profiles, [client]);
        client.BindOriginalHomeInventory(home.StateStore, home.Profiles, OriginalRegistry);
        OriginalServices = new Dictionary<Type, object> {
            [typeof(LinuxHomeRootClient)] = client, [typeof(HomeInstalledApplicationRegistry)] = OriginalRegistry,
            [typeof(IInstalledApplicationRegistry)] = OriginalRegistry, [typeof(IInstalledApplicationOriginalActorRegistry)] = OriginalRegistry,
            [typeof(IInstalledApplicationOriginalScopedActorRegistry)] = OriginalRegistry,
            [typeof(IEnumerable<IInstalledApplicationObservationProvider>)] = new IInstalledApplicationObservationProvider[] { client },
            [typeof(IHomeNativeSessionHostVerifier)] = client,
            [typeof(DesktopOriginalLinuxHomeRootClientRegistration)] = this
        };
    }
    internal bool HasOriginalComposition(HomeNativeWindowsComposition home) =>
        OriginalClient.HasOriginalHomeInventory(home.StateStore, home.Profiles, OriginalRegistry) &&
        ReferenceEquals(home.Services.GetService(typeof(IInstalledApplicationRegistry)), OriginalRegistry) &&
        ReferenceEquals(home.Services.GetService(typeof(IHomeNativeInstalledPeerVerifier)), OriginalClient);
    internal Task CloseAndDrainAsync() => OriginalClient.CloseAndDrainAsync();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
#endif
