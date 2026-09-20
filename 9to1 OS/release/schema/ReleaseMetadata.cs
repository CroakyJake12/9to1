namespace HavenOS.Release;

using CakeOS.Platform;

/// <summary>
/// Stable identity for the one shared application registry product (Worker 3 boundary).
/// Release metadata references this identity but never owns enumeration.
/// </summary>
public static class ReleaseRegistryContract
{
    public const string RegistryId = "cakeos.shared-app-registry";
    public const int RegistrySchemaVersion = 1;

    public static RegistryIdentity Identity { get; } = new(RegistryId, RegistrySchemaVersion);
}

/// <summary>
/// Product type enumeration matching Worker 3's ProductType.
/// </summary>
public enum ReleaseProductType
{
    App = 0,
    SystemSurface = 1,
    BackgroundComponent = 2,
    Provider = 3,
    Service = 4
}

/// <summary>
/// Provenance state for a release component.
/// </summary>
public enum ProvenanceState
{
    Unknown = 0,
    Partial = 1,
    Blocked = 2,
    Ready = 3
}

/// <summary>
/// Image inclusion state for a release component.
/// </summary>
public enum ImageInclusionState
{
    NotApplicable = 0,
    Excluded = 1,
    Pending = 2,
    Included = 3
}

/// <summary>
/// Approved VM state for a release component.
/// </summary>
public enum ApprovedVmState
{
    NotTested = 0,
    Failed = 1,
    Partial = 2,
    Verified = 3
}

/// <summary>
/// Offline requirement level.
/// </summary>
public enum OfflineRequirement
{
    NotRequired = 0,
    Preferred = 1,
    Required = 2
}

/// <summary>
/// Source repository metadata.
/// </summary>
public sealed record SourceRepository(
    string Url,
    string Revision,
    string? Branch = null,
    string? Tag = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Url))
            throw new ArgumentException("Source repository URL is required.", nameof(Url));
        if (string.IsNullOrWhiteSpace(Revision))
            throw new ArgumentException("Source revision is required.", nameof(Revision));
    }
}

/// <summary>
/// Donor metadata (upstream project).
/// </summary>
public sealed record DonorMetadata(
    string Name,
    string RepositoryUrl,
    string Revision,
    string Licence,
    string? LicenceUrl = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Donor name is required.", nameof(Name));
        if (string.IsNullOrWhiteSpace(RepositoryUrl))
            throw new ArgumentException("Donor repository URL is required.", nameof(RepositoryUrl));
        if (string.IsNullOrWhiteSpace(Revision))
            throw new ArgumentException("Donor revision is required.", nameof(Revision));
        if (string.IsNullOrWhiteSpace(Licence))
            throw new ArgumentException("Donor licence is required.", nameof(Licence));
    }
}

/// <summary>
/// Package metadata.
/// </summary>
public sealed record PackageMetadata(
    string Name,
    string Version,
    string HashSha256,
    string? Architecture = "amd64",
    string? PackageFormat = "deb")
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Package name is required.", nameof(Name));
        if (string.IsNullOrWhiteSpace(Version))
            throw new ArgumentException("Package version is required.", nameof(Version));
        if (string.IsNullOrWhiteSpace(HashSha256) || HashSha256.Length != 64)
            throw new ArgumentException("Package SHA-256 hash is required (64 hex chars).", nameof(HashSha256));
    }
}

/// <summary>
/// Entrypoint metadata.
/// </summary>
public sealed record EntrypointMetadata(
    string BinaryPath,
    string? Arguments = null,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Environment = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BinaryPath))
            throw new ArgumentException("Entrypoint binary path is required.", nameof(BinaryPath));
    }
}

/// <summary>
/// Service metadata (systemd, socket activation, etc.).
/// </summary>
public sealed record ServiceMetadata(
    string? ServiceName = null,
    string? SocketName = null,
    string? DbusName = null,
    bool SocketActivation = false,
    IReadOnlyCollection<string>? RequiredServices = null)
{
    public void Validate()
    {
        if (!string.IsNullOrWhiteSpace(ServiceName) && ServiceName.Contains(' '))
            throw new ArgumentException("Service name must not contain spaces.", nameof(ServiceName));
    }
}

/// <summary>
/// Route metadata.
/// </summary>
public sealed record RouteMetadata(
    string Route,
    string Handler,
    IReadOnlyCollection<string>? Methods = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Route) || !Route.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("Route must be non-empty absolute path.", nameof(Route));
        if (string.IsNullOrWhiteSpace(Handler))
            throw new ArgumentException("Route handler is required.", nameof(Handler));
    }
}

/// <summary>
/// Capability metadata.
/// </summary>
public sealed record CapabilityMetadata(
    string CapabilityId,
    string DisplayName,
    string Description,
    bool IsRequired = true)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CapabilityId))
            throw new ArgumentException("Capability ID is required.", nameof(CapabilityId));
    }
}

/// <summary>
/// Dependency metadata.
/// </summary>
public sealed record DependencyMetadata(
    string DependencyId,
    DependencyKind Kind,
    string? VersionConstraint = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DependencyId))
            throw new ArgumentException("Dependency ID is required.", nameof(DependencyId));
    }
}

public enum DependencyKind
{
    RequiredProduct = 0,
    OptionalProduct = 1,
    RequiredProvider = 2,
    OptionalProvider = 3,
    RequiredPackage = 4,
    OptionalPackage = 5
}

/// <summary>
/// Persistence requirements.
/// </summary>
public sealed record PersistenceMetadata(
    bool RequiresSettings,
    bool RequiresCache,
    bool RequiresState,
    bool RequiresData,
    IReadOnlyCollection<string>? CustomPaths = null)
{
    public void Validate() { }
}

/// <summary>
/// Permission metadata.
/// </summary>
public sealed record PermissionMetadata(
    IReadOnlyCollection<string> RequiredGrants,
    IReadOnlyCollection<string> OptionalGrants)
{
    public void Validate() { }
}

/// <summary>
/// Provider metadata.
/// </summary>
public sealed record ProviderMetadata(
    string ProviderId,
    string TransportType,
    string? LegacyUnqualifiedProvider = null,
    IReadOnlyCollection<string>? Models = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProviderId))
            throw new ArgumentException("Provider ID is required.", nameof(ProviderId));
        if (string.IsNullOrWhiteSpace(TransportType))
            throw new ArgumentException("Transport type is required.", nameof(TransportType));
    }
}

/// <summary>
/// Smoke/acceptance test metadata.
/// </summary>
public sealed record SmokeTestMetadata(
    string Command,
    string? ExpectedOutputPattern = null,
    int TimeoutSeconds = 60,
    IReadOnlyCollection<string>? RequiredArtifacts = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Command))
            throw new ArgumentException("Smoke test command is required.", nameof(Command));
    }
}

/// <summary>
/// Smoke test evidence (derived from REAL test execution).
/// </summary>
public sealed record SmokeTestEvidence(
    bool Executed,
    int ExitCode,
    string Stdout,
    string Stderr,
    DateTimeOffset ExecutedAt,
    string? EvidenceArtifactPath = null)
{
    public bool Passed => ExitCode == 0;
}

/// <summary>
/// Convergence state for a single product.
/// </summary>
public sealed record ProductConvergenceState(
    string ProductId,
    ReleaseProductType ProductType,
    ProvenanceState ProvenanceState,
    ImageInclusionState ImageInclusionState,
    ApprovedVmState ApprovedVmState,
    bool OfflineRequirementMet,
    bool SmokeTestPassed,
    string? BlockingReason = null)
{
    public bool IsConverged => ProvenanceState == ProvenanceState.Ready
        && ImageInclusionState == ImageInclusionState.Included
        && ApprovedVmState == ApprovedVmState.Verified
        && OfflineRequirementMet
        && SmokeTestPassed;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProductId))
            throw new ArgumentException("Product ID is required.", nameof(ProductId));
    }
}

/// <summary>
/// Complete release metadata for a single product/component.
/// References Worker 3's AppRegistryContract.Identity but adds release-specific fields.
/// </summary>
public sealed record ReleaseComponentMetadata(
    string ProductId,
    ReleaseProductType ProductType,
    RegistryIdentity RegistryIdentity,
    SourceRepository SourceRepository,
    DonorMetadata Donor,
    PackageMetadata Package,
    EntrypointMetadata Entrypoint,
    ServiceMetadata Service,
    IReadOnlyCollection<RouteMetadata> Routes,
    IReadOnlyCollection<CapabilityMetadata> Capabilities,
    IReadOnlyCollection<DependencyMetadata> Dependencies,
    PersistenceMetadata Persistence,
    PermissionMetadata Permissions,
    IReadOnlyCollection<ProviderMetadata> Providers,
    OfflineRequirement OfflineRequirement,
    SmokeTestMetadata SmokeTest,
    SmokeTestEvidence? SmokeEvidence,
    ProvenanceState ProvenanceState,
    ImageInclusionState ImageInclusionState,
    ApprovedVmState ApprovedVmState)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProductId))
            throw new ArgumentException("Product ID is required.", nameof(ProductId));

        if (RegistryIdentity.RegistryId != ReleaseRegistryContract.RegistryId
            || RegistryIdentity.RegistrySchemaVersion != ReleaseRegistryContract.RegistrySchemaVersion)
        {
            throw new ArgumentException($"Registry identity must match Worker 3 registry: {ReleaseRegistryContract.RegistryId} v{ReleaseRegistryContract.RegistrySchemaVersion}", nameof(RegistryIdentity));
        }

        SourceRepository.Validate();
        Donor.Validate();
        Package.Validate();
        Entrypoint.Validate();
        Service.Validate();
        foreach (var route in Routes) route.Validate();
        foreach (var cap in Capabilities) cap.Validate();
        foreach (var dep in Dependencies) dep.Validate();
        Persistence.Validate();
        Permissions.Validate();
        foreach (var prov in Providers) prov.Validate();
        SmokeTest.Validate();
        SmokeEvidence?.Validate();
    }
}

/// <summary>
/// Release manifest containing all components for a release.
/// </summary>
public sealed record ReleaseManifest(
    string ReleaseId,
    string Version,
    DateTimeOffset CreatedAt,
    RegistryIdentity RegistryIdentity,
    IReadOnlyCollection<ReleaseComponentMetadata> Components)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ReleaseId))
            throw new ArgumentException("Release ID is required.", nameof(ReleaseId));
        if (string.IsNullOrWhiteSpace(Version))
            throw new ArgumentException("Release version is required.", nameof(Version));
        if (RegistryIdentity.RegistryId != ReleaseRegistryContract.RegistryId
            || RegistryIdentity.RegistrySchemaVersion != ReleaseRegistryContract.RegistrySchemaVersion)
        {
            throw new ArgumentException($"Registry identity must match Worker 3 registry: {ReleaseRegistryContract.RegistryId} v{ReleaseRegistryContract.RegistrySchemaVersion}", nameof(RegistryIdentity));
        }
        foreach (var component in Components)
        {
            component.Validate();
        }
    }
}

/// <summary>
/// Convergence matrix/dashboard derived from REAL evidence.
/// </summary>
public sealed record ConvergenceMatrix(
    string ReleaseId,
    DateTimeOffset GeneratedAt,
    IReadOnlyCollection<ProductConvergenceState> ProductStates,
    int TotalProducts,
    int ConvergedProducts,
    int PartialProducts,
    int BlockedProducts,
    int UnknownProducts)
{
    public double ConvergencePercentage => TotalProducts > 0
        ? (double)ConvergedProducts / TotalProducts * 100.0
        : 0.0;

    public static ConvergenceMatrix FromManifest(ReleaseManifest manifest)
    {
        var states = manifest.Components.Select(c => new ProductConvergenceState(
            ProductId: c.ProductId,
            ProductType: c.ProductType,
            ProvenanceState: c.ProvenanceState,
            ImageInclusionState: c.ImageInclusionState,
            ApprovedVmState: c.ApprovedVmState,
            OfflineRequirementMet: c.OfflineRequirement == OfflineRequirement.NotRequired || c.SmokeEvidence?.Passed == true,
            SmokeTestPassed: c.SmokeEvidence?.Passed ?? false,
            BlockingReason: c.ProvenanceState == ProvenanceState.Blocked ? "Provenance incomplete" : null
        )).ToArray();

        return new ConvergenceMatrix(
            ReleaseId: manifest.ReleaseId,
            GeneratedAt: DateTimeOffset.UtcNow,
            ProductStates: states,
            TotalProducts: states.Length,
            ConvergedProducts: states.Count(s => s.IsConverged),
            PartialProducts: states.Count(s => s.ProvenanceState == ProvenanceState.Partial || s.ImageInclusionState == ImageInclusionState.Pending),
            BlockedProducts: states.Count(s => s.ProvenanceState == ProvenanceState.Blocked || s.ApprovedVmState == ApprovedVmState.Failed),
            UnknownProducts: states.Count(s => s.ProvenanceState == ProvenanceState.Unknown)
        );
    }
}

public static class ReleaseMetadataExtensions
{
    public static ProductConvergenceState ToConvergenceState(this ReleaseComponentMetadata metadata)
    {
        return new ProductConvergenceState(
            ProductId: metadata.ProductId,
            ProductType: metadata.ProductType,
            ProvenanceState: metadata.ProvenanceState,
            ImageInclusionState: metadata.ImageInclusionState,
            ApprovedVmState: metadata.ApprovedVmState,
            OfflineRequirementMet: metadata.OfflineRequirement == OfflineRequirement.NotRequired || metadata.SmokeEvidence?.Passed == true,
            SmokeTestPassed: metadata.SmokeEvidence?.Passed ?? false,
            BlockingReason: metadata.ProvenanceState == ProvenanceState.Blocked ? "Provenance incomplete" : null
        );
    }
}