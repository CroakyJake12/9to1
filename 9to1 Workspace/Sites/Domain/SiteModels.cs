using System.Text.Json;

namespace HavenOS.Apps.Sites.Domain;

public static class SiteProjectFormat
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record SiteSourceBinding(
    Guid FilesDirectoryId,
    Guid? StackProjectId,
    Guid? StackDomainId,
    string SourceRevision,
    string FrameworkId,
    string RelativeProjectPath);

public sealed record SiteProject(
    Guid SiteId,
    Guid ProjectId,
    string Name,
    SiteSourceBinding Source,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<SitePage> Pages,
    IReadOnlyList<SiteRoute> Routes,
    IReadOnlyList<SiteComponent> Components,
    IReadOnlyList<SiteReusableComponent> ReusableComponents,
    SiteDesignSystem DesignSystem,
    IReadOnlyList<SiteContentCollection> ContentCollections,
    IReadOnlyList<SiteAssetReference> Assets,
    IReadOnlyList<SiteEnvironment> Environments,
    IReadOnlyList<Guid> DomainBindingIds,
    IReadOnlyList<SiteRedirect> Redirects,
    IReadOnlyDictionary<string, JsonElement> Settings,
    int SchemaVersion = SiteProjectFormat.CurrentSchemaVersion);

public sealed record SitePage(
    Guid PageId,
    string Name,
    string? SourcePath,
    IReadOnlyList<Guid> RootComponentIds,
    string? SeoTitle,
    string? MetaDescription,
    long Revision);

public sealed record SiteRoute(
    Guid RouteId,
    Guid PageId,
    string Pattern,
    SiteRouteKind Kind,
    Guid? CollectionId = null,
    string? RecordSlugField = null,
    bool IsPublished = false);

public enum SiteRouteKind { Static, Parameterised, NotFound, Redirect }

public sealed record SiteComponent(
    Guid ComponentId,
    string ComponentType,
    IReadOnlyDictionary<string, JsonElement> Properties,
    IReadOnlyList<Guid> ChildIds,
    IReadOnlyDictionary<string, IReadOnlyList<Guid>> Slots,
    SiteLayout Layout,
    IReadOnlyDictionary<string, string> DesignTokens,
    IReadOnlyDictionary<string, JsonElement> Bindings,
    IReadOnlyDictionary<string, JsonElement> Interactions,
    IReadOnlyDictionary<string, JsonElement> Accessibility,
    SiteSourceMapping? SourceMapping,
    string? ReusableDefinitionId,
    IReadOnlyDictionary<string, JsonElement> Overrides,
    long Revision);

public sealed record SiteSourceMapping(string RelativePath, string? Symbol, int? StartLine, int? EndLine, bool IsGenerated);

public sealed record SiteLayout(
    SiteLayoutMode Mode,
    IReadOnlyDictionary<string, JsonElement> Properties,
    IReadOnlyDictionary<string, SiteLayoutOverride> BreakpointOverrides);

public enum SiteLayoutMode { Flow, Stack, Grid, Freeform }

public sealed record SiteLayoutOverride(IReadOnlyDictionary<string, JsonElement> ChangedProperties);

public sealed record SiteReusableComponent(
    Guid DefinitionId,
    string Name,
    Guid RootComponentId,
    IReadOnlyDictionary<string, JsonElement> DeclaredProperties,
    IReadOnlyDictionary<string, IReadOnlyList<Guid>> DeclaredSlots,
    long Revision);

public sealed record SiteDesignSystem(
    IReadOnlyDictionary<string, string> ColourTokens,
    IReadOnlyDictionary<string, string> TypographyTokens,
    IReadOnlyDictionary<string, string> SpacingTokens,
    IReadOnlyDictionary<string, string> RadiusTokens,
    IReadOnlyDictionary<string, string> ShadowTokens,
    IReadOnlyDictionary<string, int> Breakpoints,
    string ThemeMode,
    long Revision);

public sealed record SiteContentCollection(
    Guid CollectionId,
    string Name,
    int SchemaVersion,
    IReadOnlyList<SiteContentField> Fields,
    IReadOnlyList<SiteContentRelation> Relations,
    long Revision);

public sealed record SiteContentField(Guid FieldId, string Name, SiteContentFieldType Type, bool Required, bool IsUnique);
public enum SiteContentFieldType { Text, RichText, Slug, Number, Boolean, DateTime, FileReference, Enum, RecordReference, List, Structured }
public sealed record SiteContentRelation(Guid RelationId, Guid TargetCollectionId, SiteRelationCardinality Cardinality);
public enum SiteRelationCardinality { OneToOne, OneToMany, ManyToMany }

public sealed record SiteContentRecord(
    Guid RecordId,
    Guid CollectionId,
    IReadOnlyDictionary<Guid, JsonElement> Values,
    SiteContentState State,
    long Revision,
    DateTimeOffset UpdatedAt);

public enum SiteContentState { Draft, Published, Archived }

public sealed record SiteAssetReference(
    Guid AssetReferenceId,
    Guid? FilesFileId,
    string SourcePath,
    string? AltText,
    string? Caption,
    string? Origin,
    IReadOnlyDictionary<string, JsonElement> Transformations,
    long Revision);

public sealed record SiteEnvironment(Guid EnvironmentId, string Name, SiteEnvironmentKind Kind, bool IsProduction, bool IsEnabled);
public enum SiteEnvironmentKind { Development, Preview, Staging, Production, ProviderDefined }

public sealed record SiteRedirect(Guid RedirectId, string SourcePath, string Destination, int StatusCode, DateTimeOffset? ExpiresAt);

public sealed record SiteDeployment(
    Guid DeploymentId,
    Guid SiteId,
    Guid ProjectId,
    Guid? StackDomainId,
    string SourceRevision,
    string ConfigurationRevision,
    Guid EnvironmentId,
    string ProviderId,
    string ArtifactId,
    Uri? PublicUrl,
    SiteDeploymentState State,
    IReadOnlyList<SiteDeploymentStage> Stages,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Initiator,
    string? FailureCode,
    string? FailureMessage,
    Guid? RolledBackFromDeploymentId = null);

public enum SiteDeploymentState { Queued, Building, Validating, Packaging, Deploying, Verifying, Succeeded, Failed, Cancelled, RollingBack }
public enum SiteDeploymentStageKind { Restore, Build, Validate, Package, Deploy, Verify, Route }
public enum SiteStageState { Pending, Running, Succeeded, Failed, Skipped, Cancelled }
public sealed record SiteDeploymentStage(SiteDeploymentStageKind Kind, SiteStageState State, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, string? Code, string? Message);

public sealed record DomainBinding(
    Guid DomainBindingId,
    Guid SiteId,
    Guid EnvironmentId,
    string HostnameAscii,
    string DisplayHostname,
    bool IsCanonical,
    SiteDomainVerificationState OwnershipState,
    SiteNameVerificationState NameSafetyState,
    SiteTlsState TlsState,
    IReadOnlyList<SiteDnsRequirement> RequiredRecords,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public enum SiteDomainVerificationState { Pending, Verified, Misconfigured, Conflict, Expired, Failed }
public enum SiteNameVerificationState { Pending, Passed, PendingReview, Rejected, Failed, Unavailable }
public enum SiteTlsState { Unknown, Pending, Ready, Misconfigured, Failed }
public sealed record SiteDnsRequirement(string Type, string Name, string Value, string Purpose, bool IsRequired);

public sealed record PublicNameVerification(
    Guid VerificationId,
    Guid SiteId,
    string Subject,
    string NormalizedSubject,
    string RulesVersion,
    string? ModelVersion,
    SiteNameVerificationState State,
    IReadOnlyList<string> ReasonCategories,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ReviewedAt,
    string? ReviewReference);

public sealed record DomainOwnershipChallenge(
    Guid ChallengeId,
    Guid DomainBindingId,
    Guid SiteId,
    string HostnameAscii,
    string RecordName,
    string TokenHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? VerifiedAt,
    int Generation);

public sealed record SiteWorkspaceSnapshot(
    int SchemaVersion,
    IReadOnlyList<SiteProject> Projects,
    IReadOnlyList<SiteDeployment> Deployments,
    IReadOnlyList<DomainBinding> Domains,
    IReadOnlyList<PublicNameVerification> NameVerifications,
    IReadOnlyList<DomainOwnershipChallenge> DomainChallenges,
    IReadOnlyList<SiteSlugReservation> SlugReservations);

public sealed record SiteSlugReservation(string Slug, Guid SiteId, SiteNameVerificationState NameSafetyState, DateTimeOffset ReservedAt, DateTimeOffset? ReleasedAt);

public sealed record SiteApiError(string Code, string Message, string Target, bool Recoverable, TimeSpan? RetryAfter = null, string? Detail = null);

public sealed record SiteApiResult<T>(T? Value, SiteApiError? Error)
{
    public bool IsSuccess => Error is null;
    public static SiteApiResult<T> Success(T value) => new(value, null);
    public static SiteApiResult<T> Failure(SiteApiError error) => new(default, error);
}

public sealed class SiteOperationException(SiteApiError error) : Exception(error.Message)
{
    public SiteApiError Error { get; } = error;
}
