using System.Text.Json;

namespace HavenOS.Home.Core;

public sealed record HomeModelRouteCandidate(string ProviderId, string ModelId, string? ArtifactRevision,
    bool Enabled, int Order, string? ReasoningLevelId = null);

public sealed record HomeModelRouteContract(string RouteId, long Version, string Scope, string Category,
    string? AppId, string? OverrideIdentity, IReadOnlyList<HomeModelRouteCandidate> Candidates,
    JsonElement Policy, string? ScopeId = null);

public sealed record HomeModelPickerSnapshot(long Revision, string Scope, string Category,
    IReadOnlyList<HomeModelRouteContract> Routes)
{
    public string? PendingApprovalRequestId { get; init; }
    public string? PendingAuditRequestId { get; init; }
}

public sealed record HomeModelRouteEdit(HomeModelRouteContract Route, long ExpectedRevision)
{
    public string? ApprovalRequestId { get; init; }
}

public sealed record HomeModelRoutePreview(string RouteId, long RouteRevision, string? SelectedIdentity,
    string ResolutionState, IReadOnlyList<string> Trace, string? FailureCode = null);

public sealed record HomeModelRoutePreviewRequest(string RouteId, string Capability, string? AppId,
    string? AgentId, JsonElement Context);

public interface IHomeModelPickerFeatureProvider
{
    Task<HomeCoreOperationResult<object>> RetryAuditAsync(string requestId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new HomeCoreOperationResult<object>(false, "AuditNotOwned", "This host does not own the original route audit."));
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
    int? ContextWindow, string? LifecycleState, string? PrivacyResidency, string? Alias,
    HomeModelPickerPresentation? Presentation = null,
    HomeModelProviderReportedMetadata? ReportedMetadata = null);

public sealed record HomeModelCataloguePage(IReadOnlyList<HomeModelPickerCatalogueEntry> Items, int Offset,
    int Limit, bool HasMore, long Revision, HomeModelFamilyReleaseContext? FamilyRelease = null);
