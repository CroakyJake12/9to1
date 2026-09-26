using HavenOS.Apps.Sites.Domain;

namespace HavenOS.Apps.Sites.Hosting;

[Flags]
public enum SiteHostingCapabilities
{
    None = 0,
    StaticDeployment = 1,
    ServerFunctions = 2,
    PreviewDeployments = 4,
    AtomicPromotion = 8,
    Rollback = 16,
    CustomDomains = 32,
    DnsTxtRead = 64,
    DnsRecordWrite = 128,
    TlsStatus = 256,
}

public sealed record SiteHostingProviderDescriptor(
    string ProviderId,
    string DisplayName,
    SiteHostingCapabilities Capabilities,
    IReadOnlyList<string> SupportedFrameworkIds,
    bool IsConnected,
    string? UnavailableReason);

public sealed record SiteSourceRevisionRequest(Guid SiteId, Guid ProjectId, Guid? StackDomainId, string SourceRevision);
public sealed record SiteCanonicalSource(string SourceRevision, string ConfigurationRevision, string FrameworkId, string RootReference, bool IsWorkingTreeClean, bool IsCodeDefinedContentPreserved);
public sealed record SiteBuildRequest(Guid SiteId, Guid DeploymentId, SiteCanonicalSource Source, Guid EnvironmentId);
public sealed record SiteBuildArtifact(Guid ArtifactId, string SourceRevision, string ConfigurationRevision, string FrameworkId, string ContentHash, string ArtifactReference, long SizeBytes, bool SecretScanPassed);
public sealed record SiteBuildValidation(bool IsValid, IReadOnlyList<SiteApiError> Diagnostics);
public sealed record SitePipelineUpdate(SiteDeploymentStageKind Stage, SiteStageState State, string? Code = null, string? Message = null);
public sealed record SiteCandidateDeployment(Guid ProviderDeploymentId, Uri PreviewUrl, string SourceRevision, string ConfigurationRevision, string ArtifactId);
public sealed record SiteProviderVerification(bool IsReachable, bool ContentMatches, string SourceRevision, string ArtifactId, SiteTlsState TlsState, IReadOnlyList<SiteApiError> Diagnostics);
public sealed record SiteHostingState(Guid SiteId, Guid EnvironmentId, string ProviderId, Uri? ActiveUrl, string? ActiveSourceRevision, Guid? ActiveDeploymentId, SiteTlsState TlsState, string State, DateTimeOffset ObservedAt);
public sealed record SiteProviderDnsScope(string ProviderId, string AccountId, string ZoneId, IReadOnlySet<string> AuthorizedHostnames, string AuthorizationReference);
public sealed record SiteProviderDnsRecord(string RecordId, string Type, string Name, string Value, int? TtlSeconds, bool? Proxied);

public interface ISiteCanonicalSourceResolver
{
    Task<SiteCanonicalSource> ResolveAsync(SiteSourceRevisionRequest request, CancellationToken cancellationToken);
}

public interface ISiteBuildPipeline
{
    Task<SiteBuildArtifact> BuildAndPackageAsync(
        SiteBuildRequest request,
        Func<SitePipelineUpdate, CancellationToken, ValueTask> progress,
        CancellationToken cancellationToken);

    Task<SiteBuildValidation> ValidateArtifactAsync(SiteBuildArtifact artifact, CancellationToken cancellationToken);
}

public interface ISiteArtifactArchive
{
    Task<SiteBuildArtifact?> GetAsync(Guid artifactId, CancellationToken cancellationToken);
    Task SaveAsync(SiteBuildArtifact artifact, CancellationToken cancellationToken);
}

public interface ISiteHostingProvider
{
    SiteHostingProviderDescriptor Descriptor { get; }
    Task<SiteHostingState> GetStateAsync(Guid siteId, Guid environmentId, CancellationToken cancellationToken);
    Task<SiteCandidateDeployment> UploadCandidateAsync(SiteBuildArtifact artifact, Guid siteId, Guid environmentId, Guid deploymentId, CancellationToken cancellationToken);
    Task<SiteProviderVerification> VerifyCandidateAsync(SiteCandidateDeployment candidate, CancellationToken cancellationToken);
    Task<SiteHostingState> PromoteAsync(SiteCandidateDeployment candidate, Guid siteId, Guid environmentId, CancellationToken cancellationToken);
}

public interface ISiteHostingProviderRegistry
{
    IReadOnlyList<SiteHostingProviderDescriptor> ListProviders();
    ISiteHostingProvider? Find(string providerId);
}

public interface ISiteProductionSourcePolicy
{
    Task<SiteApiResult<bool>> IsApprovedProductionSourceAsync(SiteProject project, string sourceRevision, CancellationToken cancellationToken);
}

public sealed class SiteDeploymentException(SiteApiError error) : Exception(error.Message)
{
    public SiteApiError Error { get; } = error;
}
