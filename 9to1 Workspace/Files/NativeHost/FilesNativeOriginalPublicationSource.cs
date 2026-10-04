using HavenOS.Home.Core;
namespace HavenOS.Files.NativeHost;

/// <summary>HOME-only trusted source. No DTO or app registration constructs this authority.
/// Implementations must retain the actual Home profile/configuration/receipt transaction AND
/// actual provider metadata read lease, paired to these SAME owner-supplied source objects.
/// Check callbacks run under those leases without reentering their owners. Missing support
/// returns null; a previous asynchronous current check never stands in for held authority.</summary>
public interface IFilesNativeOriginalPublicationSource
{
    ValueTask<IHomeNativeFilesPublicationGuard?> AcquireOriginalAsync(
        FilesNativeOriginalPublicationRead originalRead, CancellationToken cancellationToken);
}

/// <summary>Issued only by the registered owner from its private reply/source map.</summary>
public sealed class FilesNativeOriginalPublicationRead
{
    public HomeNativeFilesOriginalConnection OriginalConnection { get; }
    public HomeNativeFilesReply OriginalReply { get; }
    public NativeFilesWorkspace OriginalWorkspace { get; }
    public FilesNativeBrowserPage? OriginalPage { get; }
    public string OriginalStoreRevision { get; }
    public CancellationToken OriginalOwnerLifetime { get; }
    internal FilesNativeOriginalPublicationRead(HomeNativeFilesOriginalConnection connection,
        HomeNativeFilesReply reply, NativeFilesWorkspace workspace, FilesNativeBrowserPage? page,
        string storeRevision, CancellationToken lifetime)
    {
        OriginalConnection = connection;
        OriginalReply = reply;
        OriginalWorkspace = workspace;
        OriginalPage = page;
        OriginalStoreRevision = storeRevision;
        OriginalOwnerLifetime = lifetime;
    }
}
