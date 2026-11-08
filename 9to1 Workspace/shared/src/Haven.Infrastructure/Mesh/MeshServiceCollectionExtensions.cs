using CakeOS.Apps.Boards.Contract;
using Haven.Application;
using Haven.Application.Automations;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure;

public static class MeshServiceCollectionExtensions
{
    public static IServiceCollection AddHavenMesh(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IMeshStateStore, MeshStateStore>();
        services.AddSingleton<IMeshIdentitySecretStore, MeshIdentitySecretStore>();
        services.AddSingleton<IMeshCapabilitySource, MeshCapabilitySource>();
        services.AddSingleton<MeshResourceStore>();
        // Canonical data belongs to the feature that owns it. The generic journal stays the fallback for
        // Mesh-owned bookkeeping only, and a feature becomes reachable for synchronization solely by
        // registering an owner for the exact resource type it controls.
        services.AddSingleton<IMeshResourceMergeService>(provider => new MeshMergeRouter(
            provider.GetRequiredService<MeshResourceStore>(),
            provider.GetServices<IMeshResourceMergeOwner>().ToArray()));
        services.AddSingleton<IMeshInboundDeviceActionExecutor, MeshInboundDeviceActionExecutor>();
        services.AddSingleton<IMeshDiscoveryService, LanMeshDiscoveryService>();
        services.AddSingleton<IMeshInboundRuntimeExecutor, MeshInboundRuntimeExecutor>();
        services.AddSingleton<IMeshInboundTaskExecutor, MeshInboundTaskExecutor>();
        services.AddSingleton<IMeshFileTransferStore, MeshFileTransferStore>();
        services.AddSingleton<IMeshTransport, SecureLanMeshTransport>();
        services.AddSingleton<MeshCoordinator>();
        services.AddSingleton<MeshRemoteModelProvider>();
        services.AddSingleton<IModelProvider>(provider => provider.GetRequiredService<MeshRemoteModelProvider>());
        services.AddSingleton<MeshDeviceActionProvider>();
        services.AddSingleton<IDeviceActionProvider>(provider => provider.GetRequiredService<MeshDeviceActionProvider>());
        return services;
    }

    /// <summary>
    /// Opt-in: lets Mesh reach board data through the Boards store's own save path instead of its generic
    /// journal. Deliberately separate from AddHavenMesh, because only the host composition knows the real Boards
    /// storage root, and the store instance used here must be the same one Boards uses - two stores over one
    /// directory would mean two independent in-process write locks. The desktop composition has not called this
    /// yet, so Boards synchronization is proven at the adapter level and is not claimed as wired on screen.
    /// </summary>
    public static IServiceCollection AddBoardsMeshResourceOwner(this IServiceCollection services, string boardsRootDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(boardsRootDirectory);
        services.AddSingleton<BoardsMeshResourceOwner>(provider =>
            new BoardsMeshResourceOwner(provider.GetRequiredService<JsonFileHavenBoardStore>(), boardsRootDirectory));
        services.AddSingleton<IMeshResourceMergeOwner>(provider => provider.GetRequiredService<BoardsMeshResourceOwner>());
        return services;
    }
}
