using System.Text.Json;
using System.Text.Json.Serialization;

namespace Haven.Core;

public enum ExtensionPackageType { Plugin = 0, Skill = 1, PluginAndSkills = 2 }
public enum ExtensionSourceType { GitHubRepository = 0, LocalRepository = 1 }
public enum ExtensionUpdateMode { Manual = 0, Notify = 1, Automatic = 2 }
public enum ExtensionInstallState
{
    Available = 0,
    Installing = 1,
    Installed = 2,
    UpdateAvailable = 3,
    Disabled = 4,
    Failed = 5,
    Incompatible = 6,
    Deprecated = 7,
    Enabled = 8,
    Updating = 9,
    Quarantined = 10
}

public enum ExtensionDependencyType { Required = 0, Optional = 1 }

public sealed record ExtensionDependency(string PackageId, string VersionRange, ExtensionDependencyType Type = ExtensionDependencyType.Required);

[Flags]
public enum ExtensionPermission
{
    None = 0,
    ReadHavenData = 1 << 0,
    ModifyHavenData = 1 << 1,
    ProjectRead = 1 << 2,
    ProjectWrite = 1 << 3,
    FileSystemRead = 1 << 4,
    FileSystemWrite = 1 << 5,
    ProcessExecution = 1 << 6,
    NetworkAccess = 1 << 7,
    ConnectorAccess = 1 << 8,
    HavenApiAccess = 1 << 9
}

public sealed record ExtensionSource(
    Guid Id,
    ExtensionSourceType Type,
    string DisplayName,
    string RepositoryUri,
    string? Branch,
    bool IsPrivate,
    string? ConnectedAccountId,
    ExtensionUpdateMode UpdateMode,
    bool IsEnabled,
    DateTimeOffset? LastRefreshedAt,
    string? SafeLastError);

public sealed record ExtensionCapabilityManifest(
    string Id,
    string DisplayName,
    string Description,
    string EntryPoint,
    IReadOnlyList<string> SemanticActions,
    ExtensionPermission RequiredPermissions,
    string InputSchemaJson = "{\"type\":\"object\"}",
    string? OutputSchemaJson = null,
    string RiskClassification = "consequential",
    string? CancellationSemantics = null,
    string? ExternalSideEffectClassification = null,
    string? CredentialReferenceKey = null,
    string? ConnectionScope = null)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record ExtensionSkillManifest(
    string Id,
    string DisplayName,
    string Description,
    string InstructionPath,
    bool EnabledByDefault,
    string? WorkflowJson = null,
    string? ContextRulesJson = null,
    IReadOnlyList<string>? ConflictKeys = null,
    IReadOnlyList<string>? RequiredCapabilityIds = null,
    IReadOnlyList<string>? ResourcePaths = null)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record ExtensionPluginSurfaceManifest(string Id, string CUiDefinitionPath, string HostContractVersion);

public sealed record ExtensionPackageManifest(
    string PackageId,
    string PackagePath,
    string DisplayName,
    ExtensionPackageType PackageType,
    string Version,
    string HavenVersionRange,
    string Description,
    string Author,
    string Publisher,
    string? Homepage,
    string? License,
    ExtensionPermission RequestedPermissions,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<ExtensionCapabilityManifest> Capabilities,
    IReadOnlyList<ExtensionSkillManifest> Skills,
    string? UpdateManifestPath,
    bool Deprecated = false,
    string? Provenance = null,
    string? MinimumHavenVersion = null,
    IReadOnlyList<ExtensionDependency>? DependencyDefinitions = null,
    IReadOnlyList<string>? ResourcePaths = null,
    IReadOnlyList<ExtensionPluginSurfaceManifest>? Surfaces = null,
    string? IntegrityAlgorithm = null,
    string? DeclaredContentHash = null,
    string? Signature = null,
    string? SignatureKeyId = null,
    string? UpdateMetadataJson = null)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record InstalledExtensionPackage(
    Guid Id,
    Guid SourceId,
    ExtensionPackageManifest Manifest,
    string InstallPath,
    ExtensionPermission GrantedPermissions,
    ExtensionInstallState State,
    bool IsEnabled,
    bool HasLocalModifications,
    string ContentHash,
    DateTimeOffset InstalledAt,
    DateTimeOffset UpdatedAt,
    string? AvailableVersion = null,
    string? SafeLastError = null,
    string EnablementScope = "device",
    IReadOnlyDictionary<string, string>? RetainedPackageData = null,
    IReadOnlyList<string>? EnabledScopes = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? SkillEnablementScopes = null);

public sealed record ExtensionManifestDocument(int SchemaVersion, IReadOnlyList<ExtensionPackageManifest> Packages)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record DiscoveredExtensionPackage(
    Guid SourceId,
    ExtensionPackageManifest Manifest,
    string MaterializedRepositoryPath,
    string ContentHash,
    ExtensionInstallState State,
    string? SafeError = null);

public sealed record SkillResolutionRequest(
    IReadOnlyList<string> RequestedSkillIds,
    string Scope,
    IReadOnlyList<string> AvailableCapabilityIds);

public sealed record ResolvedSkill(
    string SkillId,
    string PackageId,
    string PackageVersion,
    string Name,
    string Instructions,
    string? WorkflowJson,
    string? ContextRulesJson,
    IReadOnlyDictionary<string, string> Resources,
    IReadOnlyList<string> CapabilityIds);

public sealed record ExtensionSkillCatalogEntry(
    string SkillId,
    string PackageId,
    string PackageVersion,
    string Name,
    string Description,
    ExtensionInstallState PackageState,
    bool IsAvailable,
    IReadOnlyList<string> EnabledScopes);

public sealed record SkillResolutionResult(
    bool Succeeded,
    IReadOnlyList<ResolvedSkill> Skills,
    IReadOnlyList<string> Provenance,
    string? ErrorCode = null,
    string? ErrorMessage = null);
