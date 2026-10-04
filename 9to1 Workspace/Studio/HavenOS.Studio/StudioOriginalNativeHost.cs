using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using HavenOS.Images;
using Microsoft.Extensions.DependencyInjection;

namespace HavenOS.AIStudio;

/// <summary>Explicit trusted composition inputs only. Core.Read compatibility does not issue Den/model/write authority.
/// The default native entrypoint has no factory and presents Unavailable.</summary>
public sealed class StudioOriginalNativeHost
{
    public IServiceProvider Services { get; }
    public IAsyncDisposable OriginalProviderLifetime { get; }
    public StudioDenLifetime Den { get; }
    public IPictureSharedRasterDecoder RasterDecoder { get; }
    public HomeNativeWindowsAppConnection OriginalConnection { get; }

    public StudioOriginalNativeHost(IServiceProvider services, IAsyncDisposable originalProviderLifetime,
        StudioDenLifetime den, IPictureSharedRasterDecoder rasterDecoder,
        HomeNativeWindowsAppConnection originalConnection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(originalProviderLifetime);
        ArgumentNullException.ThrowIfNull(den);
        ArgumentNullException.ThrowIfNull(rasterDecoder);
        ArgumentNullException.ThrowIfNull(originalConnection);
        if (!ReferenceEquals(services, originalProviderLifetime) ||
            !ReferenceEquals(services.GetRequiredService<StudioDenLifetime>(), den))
            throw new UnauthorizedAccessException("The original Studio provider/Den ownership tuple is absent.");
        // Required actual domain producers are resolved; no fallback actor, store, receipt or policy is created.
        _ = services.GetRequiredService<IAuthenticatedResourceActorSource>();
        _ = services.GetRequiredService<HomeCoreRuntime>();
        _ = services.GetRequiredService<HomeLocalProfileIdentity>();
        _ = services.GetRequiredService<HomePermissionTrustService>();
        _ = services.GetRequiredService<HomeLocalStoreOwnership>();
        _ = services.GetRequiredService<IResourceStoreOwnershipReceiptAuthority>();
        Services = services; OriginalProviderLifetime = originalProviderLifetime; Den = den;
        RasterDecoder = rasterDecoder; OriginalConnection = originalConnection;
    }
}
