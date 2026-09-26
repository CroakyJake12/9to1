using System.Text.Json;

namespace HavenOS.Home.Core;

public sealed record HomeCoreOperationResult<T>(bool Succeeded, string Code, string Message, T? Value = default,
    bool Recoverable = true, long? Revision = null);

public sealed record HomeFeatureNavigationRequest(string RouteId, string? EntityType = null, string? EntityId = null,
    string? Action = null, string? DeepLink = null, HomeModelPickerNavigationTarget? ModelPickerTarget = null);

public sealed record HomeFeatureNavigationResult(bool Succeeded, string Code, string Message,
    HomeFeatureNavigationRequest Request, bool Recoverable = true, HomeFeatureViewState? ViewState = null);

/// <summary>Platform-neutral route state; native/web shells map the stable view ID to their renderer.</summary>
public sealed record HomeFeatureViewState(string RouteId, string ViewId, long Revision, JsonElement State);

/// <summary>Feature-owned route adapter. The Home shell owns dispatch; feature modules own destination behavior.</summary>
public interface IHomeFeatureRouteHandler
{
    string RouteId { get; }
    Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IHomeFeatureNavigationHost
{
    IReadOnlyCollection<string> AvailableRoutes { get; }
    event Action<HomeFeatureNavigationResult>? NavigationCompleted;
    HomeCoreOperationResult<bool> Register(IHomeFeatureRouteHandler handler);
    Task<HomeFeatureNavigationResult> NavigateAsync(HomeFeatureNavigationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable feature IDs used by CUI actions, deep links and external Home callers.</summary>
public static class HomeFeatureRouteIds
{
    public const string Dashboard = "home.dashboard";
    public const string Apps = "home.apps";
    public const string Library = "home.library";
    public const string Events = "home.events";
    public const string Discover = "home.discover";
    public const string Mesh = "home.mesh";
    public const string Settings = "home.settings";
    public const string Permissions = "home.permissions";
    public const string Notifications = "home.notifications";
    public const string ModelPicker = "home.model-picker";
    public const string Spaces = "app.spaces";
    public const string Automations = "app.automations";
}

public sealed record HomeModelPickerNavigationTarget(string Scope, string? ScopeId, string Category,
    string? RouteId = null, string? AppId = null, string? AgentId = null);

public sealed class HomeFeatureNavigationHost : IHomeFeatureNavigationHost
{
    private readonly Dictionary<string, IHomeFeatureRouteHandler> _handlers = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public HomeFeatureNavigationHost(IEnumerable<IHomeFeatureRouteHandler>? handlers = null)
    {
        foreach (var handler in handlers ?? []) Register(handler);
    }

    public event Action<HomeFeatureNavigationResult>? NavigationCompleted;

    public IReadOnlyCollection<string> AvailableRoutes
    {
        get { lock (_sync) return _handlers.Keys.Order(StringComparer.Ordinal).ToArray(); }
    }

    public HomeCoreOperationResult<bool> Register(IHomeFeatureRouteHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!IsValidRouteId(handler.RouteId))
            return new(false, "HomeFeatureRouteInvalid", "The feature route ID is invalid.", false, false);
        lock (_sync)
        {
            if (!_handlers.TryAdd(handler.RouteId, handler))
                return new(false, "HomeFeatureRouteConflict", $"Route '{handler.RouteId}' is already registered.", false, false);
        }
        return new(true, "Succeeded", "Feature route registered.", true);
    }

    public async Task<HomeFeatureNavigationResult> NavigateAsync(HomeFeatureNavigationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValidRouteId(request.RouteId))
            return new(false, "HomeFeatureRouteInvalid", "The requested feature route ID is invalid.", request, false);
        IHomeFeatureRouteHandler? handler;
        lock (_sync) _handlers.TryGetValue(request.RouteId, out handler);
        if (handler is null)
            return new(false, "HomeServiceUnavailable", $"Home destination '{request.RouteId}' is unavailable.", request,
                true);
        try
        {
            var result = await handler.OpenAsync(request, cancellationToken).ConfigureAwait(false);
            result ??= new(false, "HomeServiceUnavailable", "The Home destination returned no result.", request, true);
            foreach (var observer in NavigationCompleted?.GetInvocationList().Cast<Action<HomeFeatureNavigationResult>>() ?? [])
            {
                try { observer(result); }
                catch { /* A native view observer cannot invalidate a completed domain route operation. */ }
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(false, "HomeServiceUnavailable",
                $"Home destination '{request.RouteId}' failed ({exception.GetType().Name}).", request, true);
        }
    }

    private static bool IsValidRouteId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
}

public enum HomeAppsAction
{
    Refresh,
    Launch,
    SelectChannel,
    SelectVersion,
    Install,
    Update,
    Repair,
    Rollback,
    Uninstall,
}

public sealed record HomeAppsCommand(HomeAppsAction Action, string? PackageId = null, string? VersionOrChannel = null,
    long? ExpectedRevision = null, string? IdempotencyKey = null);

public sealed record HomeApplicationRecord(string AppId, string DisplayName, string Version,
    IReadOnlyList<string>? Channels = null, string? SelectedChannel = null, string? SelectedVersion = null,
    string? IconReference = null, string? Description = null, string? CompatibilityState = null,
    string? UpdateState = null, string? RepairState = null);

public sealed record HomeAppsSnapshot(long Revision, IReadOnlyList<HomeApplicationRecord> Applications,
    IReadOnlyList<HomeUpdate> Updates, IReadOnlyList<HomeCoreFailure> Diagnostics);

/// <summary>Home-owned app/package operations route to the single package service used by Home UI and APIs.</summary>
public interface IHomeAppsFeatureProvider
{
    Task<HomeAppsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<HomeAppsSnapshot>> ExecuteAsync(HomeAppsCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record HomeDiscoverQuery(string Text, IReadOnlyList<string>? ProviderIds = null, int Offset = 0,
    int Limit = 50, string? Cursor = null);

public sealed record HomeDiscoverCostMetadata(decimal? Amount = null, string? Currency = null, string? Unit = null,
    bool? IsFree = null, string? BillingCadence = null);

public sealed record HomeDiscoverPrivacyMetadata(bool? SendsDataExternally = null, string? DataHandling = null,
    string? Retention = null, IReadOnlyList<string>? Disclosures = null);

public sealed record HomeDiscoverEvidence(string EvidenceId, string Kind, string? Uri, string? Summary,
    DateTimeOffset? ObservedAtUtc = null);

public sealed record HomeDiscoverVoiceVariant(string VariantId, string DisplayName, string? Locale,
    IReadOnlySet<string>? Capabilities = null);

public sealed record HomeDiscoverCandidate(string ProviderId, string CandidateId, string DisplayName,
    string Capability, string Version, string? InstallationState = null, string? Category = null,
    IReadOnlySet<string>? Capabilities = null,
    IReadOnlyDictionary<string, string?>? TechnicalMetadata = null,
    HomeDiscoverPrivacyMetadata? Privacy = null, HomeDiscoverCostMetadata? Cost = null,
    IReadOnlyList<HomeDiscoverEvidence>? Evidence = null, IReadOnlyList<HomeDiscoverVoiceVariant>? VoiceVariants = null);

public sealed record HomeDiscoverPage(IReadOnlyList<HomeDiscoverCandidate> Candidates, string? NextCursor,
    bool HasMore, long Revision);

public sealed record HomeDiscoverCommand(string ProviderId, string CandidateId, string Action,
    string? IdempotencyKey = null);

public interface IHomeDiscoverFeatureProvider
{
    Task<HomeCoreOperationResult<HomeDiscoverPage>> SearchAsync(HomeDiscoverQuery query,
        CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<HomeDiscoverCandidate>> ExecuteAsync(HomeDiscoverCommand command,
        CancellationToken cancellationToken = default);
}

public enum HomeTrustLevel { OneTime, TrustThirtyDays, AlwaysTrust, TemporaryTrust, Blocked }
public enum HomePermissionRisk { Ordinary, Elevated, Destructive }

public sealed record HomePermissionGrant(string GrantId, string CallerId, string TargetApp, IReadOnlySet<string> Actions,
    IReadOnlySet<string> Scopes, HomeTrustLevel TrustLevel, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc, long Revision);

public sealed record HomePermissionRequest(string RequestId, HomeCallerIdentity Caller, string TargetApp,
    IReadOnlyList<string> Actions, IReadOnlyList<string> Scopes, HomePermissionRisk Risk,
    IReadOnlyList<string>? AffectedObjectTypes = null, int? AffectedObjectCount = null,
    bool? ImpactKnown = null, DateTimeOffset RequestedAtUtc = default);

public sealed record HomePermissionDecision(string RequestId, string Decision, HomeTrustLevel? TrustLevel = null,
    TimeSpan? TemporaryDuration = null, int? TemporaryActionCount = null, string? TemporaryExpiryBehavior = null,
    long ExpectedRevision = 0);

public sealed record HomePermissionTrustSnapshot(long Revision, IReadOnlyList<HomePermissionGrant> Grants,
    IReadOnlyList<HomePermissionRequest> PendingRequests, IReadOnlySet<string> BlockedCallerIds);

public sealed record HomePermissionTrustCommand(string Action, string? GrantId = null, string? CallerId = null,
    IReadOnlySet<string>? Actions = null, IReadOnlySet<string>? Scopes = null, long ExpectedRevision = 0);

public interface IHomePermissionTrustFeatureProvider
{
    Task<HomePermissionTrustSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<bool>> AuthorizeAsync(HomePermissionRequest request,
        CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<HomePermissionGrant?>> DecideAsync(HomePermissionDecision decision,
        CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<HomePermissionGrant>> UpdateGrantAsync(HomePermissionTrustCommand command,
        CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<bool>> RevokeGrantAsync(string grantId, long expectedRevision,
        CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<bool>> BlockCallerAsync(string callerId, long expectedRevision,
        CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<bool>> UnblockCallerAsync(string callerId, long expectedRevision,
        CancellationToken cancellationToken = default);
}

public sealed record HomeNotificationAction(string ActionId, string TargetApp, string ActionName,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record HomeNotificationRecord(string NotificationId, string SourceId, string Category,
    string Severity, string Title, string Body, DateTimeOffset CreatedAtUtc, bool IsRead, bool IsDismissed,
    string? RelatedEntityType, string? RelatedEntityId, IReadOnlyList<HomeNotificationAction> Actions,
    long Revision);

public sealed record HomeNotificationQuery(string? SourceId = null, string? Category = null,
    bool? IsRead = null, bool? IsDismissed = null, DateTimeOffset? FromUtc = null, DateTimeOffset? ToUtc = null,
    int Offset = 0, int Limit = 50);

public sealed record HomeNotificationPage(IReadOnlyList<HomeNotificationRecord> Items, int Offset, int Limit,
    bool HasMore, long Revision);

public sealed record HomeNotificationPreferences(string SourceId, IReadOnlySet<string> EnabledCategories,
    bool ShowSystemNotifications, long Revision);

public sealed record HomeNotificationDeepLinkRequest(string NotificationId, string ActionId);

public interface IHomeNotificationFeatureProvider
{
    Task<HomeCoreOperationResult<HomeNotificationPage>> GetSnapshotAsync(HomeNotificationQuery query,
        CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<HomeNotificationRecord>> PublishAsync(HomeNotificationRecord notification,
        CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<HomeNotificationRecord>> MarkReadAsync(string notificationId, long expectedRevision,
        CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<HomeNotificationRecord>> DismissAsync(string notificationId, long expectedRevision,
        CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<int>> ClearAsync(HomeNotificationQuery query, long expectedRevision,
        CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<HomeNotificationPreferences>> UpdatePreferencesAsync(
        HomeNotificationPreferences preferences, long expectedRevision, CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<HomeFeatureNavigationRequest>> ResolveDeepLinkAsync(
        HomeNotificationDeepLinkRequest request, CancellationToken cancellationToken = default);
}

public sealed record HomeModelRouteCandidate(string ProviderId, string ModelId, string ArtifactRevision,
    bool Enabled, int Order);

public sealed record HomeModelRouteContract(string RouteId, long Version, string Scope, string Category,
    string? AppId, string? OverrideIdentity, IReadOnlyList<HomeModelRouteCandidate> Candidates,
    JsonElement Policy, string? ScopeId = null);

public sealed record HomeModelPickerSnapshot(long Revision, string Scope, string Category,
    IReadOnlyList<HomeModelRouteContract> Routes);

public sealed record HomeModelRouteEdit(HomeModelRouteContract Route, long ExpectedRevision);

public sealed record HomeModelRoutePreview(string RouteId, long RouteRevision, string? SelectedIdentity,
    string ResolutionState, IReadOnlyList<string> Trace, string? FailureCode = null);

public sealed record HomeModelRoutePreviewRequest(string RouteId, string Capability, string? AppId,
    string? AgentId, JsonElement Context);

public interface IHomeModelPickerFeatureProvider
{
    Task<HomeCoreOperationResult<HomeModelCataloguePage>> GetCatalogueAsync(string? query = null,
        CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> GetSnapshotAsync(string scope, string category,
        CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteAsync(HomeModelRouteEdit edit,
        CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<HomeModelRoutePreview>> PreviewResolutionAsync(HomeModelRoutePreviewRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record HomeModelPickerCatalogueEntry(string ProviderId, string ModelId, string? ArtifactRevision,
    string DisplayName, string ProviderName, bool? IsLocal, IReadOnlySet<string>? Capabilities,
    int? ContextWindow, string? LifecycleState, string? PrivacyResidency, string? Alias);

public sealed record HomeModelCataloguePage(IReadOnlyList<HomeModelPickerCatalogueEntry> Items, int Offset,
    int Limit, bool HasMore, long Revision);

public sealed record HomeSearchRecord(string SourceApp, string EntityId, string EntityType, string Title,
    string DisplayMetadata, string? SearchText, string? VectorReference, DateTimeOffset ModifiedAtUtc,
    IReadOnlySet<string> AccessScopes, long Revision, HomeDataScope Scope, HomeRecordAuthority Authority);

public sealed record HomeSearchQuery(string Text, string? EntityType = null, string? SourceApp = null,
    IReadOnlySet<string>? RequiredScopes = null, int Offset = 0, int Limit = 50, string? Cursor = null);

public sealed record HomeSearchPage(IReadOnlyList<HomeSearchRecord> Items, string? NextCursor, bool HasMore,
    long Revision);

/// <summary>Permission is rechecked at query time, not trusted from indexed access metadata.</summary>
public interface IHomeSearchIndexFeatureProvider
{
    Task<HomeCoreOperationResult<HomeSearchPage>> SearchAsync(HomeSearchQuery query, HomeCallerIdentity caller,
        CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<HomeSearchRecord>> UpsertAsync(HomeSearchRecord record, long expectedRevision,
        CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<bool>> InvalidateAsync(string sourceApp, string entityId, long expectedRevision,
        CancellationToken cancellationToken = default);
}
