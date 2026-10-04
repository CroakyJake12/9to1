using System.Collections.Frozen;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
namespace HavenOS.Home.Core;

// WIP recovery source: not selected, compiled or an installed grant.
public sealed record HomeNativeFilesRequest(string Operation, Guid? OriginalPage = null,
    Guid? SelectedItem = null, string Search = "", int Offset = 0);
public sealed record HomeNativeFilesItem(Guid ItemId, Guid? ParentId, string Name, string Kind,
    Guid? MetadataRevision, long? SizeBytes, string? ContentType, DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt, string Availability, bool IsShared, string? ContentHash);
public sealed record HomeNativeFilesPage(Guid OriginalPage, Guid StoreId, string StoreRevision,
    Guid? ParentId, string Title, IReadOnlyList<HomeNativeFilesItem> Items, bool HasMore, int? NextOffset = null);
public sealed record HomeNativeFilesReply(string State, string Code, string Message,
    HomeNativeFilesPage? Page = null, string? PermissionRequestId = null);

// This Home-only object is issued from the accepted private Session context; never from wire IDs.
public sealed class HomeNativeFilesOriginalConnection
{
    private readonly HomeNativeCoreApiSessions _issuer;
    private readonly object _originalSessionContext;
    private readonly IReadOnlySet<string> _declaredServices;
    public AuthenticatedResourceActor OriginalActor { get; }
    public string InstalledAppId { get; }
    public Guid InstalledApplicationId { get; }
    public CancellationToken OriginalLifetime { get; }
    public string OriginalSessionId { get; }
    internal HomeNativeFilesOriginalConnection(HomeNativeCoreApiSessions issuer,
        object originalPrivateSessionContext, AuthenticatedResourceActor originalActor,
        HomeNativeInstalledPeer originalInstalledPeer, string originalSessionId, CancellationToken originalLifetime)
    {
        _issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
        _originalSessionContext = originalPrivateSessionContext ?? throw new ArgumentNullException(nameof(originalPrivateSessionContext));
        OriginalActor = originalActor ?? throw new ArgumentNullException(nameof(originalActor));
        ArgumentNullException.ThrowIfNull(originalInstalledPeer);
        if (!originalLifetime.CanBeCanceled || originalLifetime.IsCancellationRequested ||
            string.IsNullOrWhiteSpace(originalSessionId))
            throw new UnauthorizedAccessException("Retain the actual original Files session and lifetime.");
        OriginalSessionId = originalSessionId;
        InstalledAppId = originalInstalledPeer.AppId;
        InstalledApplicationId = originalInstalledPeer.InstalledApplicationId;
        _declaredServices = originalInstalledPeer.AllowedServiceIds.ToFrozenSet(StringComparer.Ordinal);
        OriginalLifetime = originalLifetime;
    }
    public bool IsOriginalIssuer(HomeNativeCoreApiSessions expectedIssuer) => ReferenceEquals(_issuer, expectedIssuer);
    public bool Declares(string serviceId) => _declaredServices.Contains(serviceId);
    internal bool IsOriginalContext(object expectedContext) => ReferenceEquals(_originalSessionContext, expectedContext);
}
public interface IHomeNativeFilesDomainOwner
{
    Task<HomeNativeFilesReply> InvokeOriginalAsync(HomeNativeFilesOriginalConnection originalConnection,
        HomeNativeFilesRequest originalRequest, CancellationToken cancellationToken);
    Task DemandOriginalReplyCurrentAsync(HomeNativeFilesOriginalConnection originalConnection,
        HomeNativeFilesReply originalReply, CancellationToken cancellationToken);
    Task CloseOriginalConnectionAsync(HomeNativeFilesOriginalConnection originalConnection);
}
public sealed class HomeNativeFilesActionPolicies : IHomeActionPolicySource
{
    public const string TargetAppId = "files";
    public const string RequiredInstalledServiceId = "files.native";
    public const string ReadAction = "files.browser.read";
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == TargetAppId && actionId == ReadAction ? new(HomePermissionRisk.Routine, true, false) : null;
}
public sealed class HomeNativeFilesCoreService(IHomeNativeFilesDomainOwner originalOwner) : IHomeCoreService
{
    private HomeServiceLifecycleState _state = HomeServiceLifecycleState.Starting;
    public HomeServiceDescriptor Descriptor => new(HomeNativeFilesActionPolicies.RequiredInstalledServiceId,
        new HomeContractVersion(1, 0, 0), _state, _state == HomeServiceLifecycleState.Ready);
    public IReadOnlyList<string> Dependencies { get; } = Array.AsReadOnly(new[] { "home.state", "permissions.trust" });
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(originalOwner);
        _state = HomeServiceLifecycleState.Ready;
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _state = HomeServiceLifecycleState.Stopped;
        return Task.CompletedTask;
    }
}
