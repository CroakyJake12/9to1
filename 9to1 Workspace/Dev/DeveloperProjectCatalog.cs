namespace HavenOS.Apps.Dev;

public sealed record DeveloperProjectTemplate(
    string TemplateId,
    string DisplayName,
    string ProjectTypeId,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Frameworks,
    IReadOnlyList<string> Platforms,
    IReadOnlyList<string> ProductSurfaces,
    IReadOnlyList<DeveloperToolchainRequirement> Requirements,
    string ProviderId,
    bool IsFirstParty,
    IReadOnlyList<string>? TargetRuntimes = null,
    string? OrganisationTemplateId = null);

public sealed record DeveloperToolchainRequirement(
    string RequirementId,
    string Language,
    string Component,
    string? MinimumVersion,
    string? MaximumVersionExclusive,
    bool Required);

public sealed record DeveloperToolchainReadiness(
    string RequirementId,
    DeveloperToolchainState State,
    string? RequiredVersion,
    string? InstalledVersion,
    string? InstallActionId,
    string? Message);

public sealed record DeveloperProjectTemplateQuery(
    string? SearchText = null,
    string? Language = null,
    string? Framework = null,
    string? Platform = null,
    string? ProductSurface = null,
    bool? ToolchainsReady = null,
    string? ProjectType = null,
    string? TargetRuntime = null,
    string? ProviderId = null,
    string? OrganisationTemplateId = null)
{
    public bool Matches(DeveloperProjectTemplateOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        var template = option.Template;
        return (string.IsNullOrWhiteSpace(SearchText) || Contains(template.DisplayName, SearchText) ||
                Contains(template.TemplateId, SearchText) || Contains(template.ProjectTypeId, SearchText) ||
                Contains(template.ProviderId, SearchText) || ContainsAny(template.Languages, SearchText) ||
                ContainsAny(template.Frameworks, SearchText) || ContainsAny(template.Platforms, SearchText) ||
                ContainsAny(template.ProductSurfaces, SearchText) || ContainsAny(template.TargetRuntimes, SearchText) ||
                Contains(template.OrganisationTemplateId, SearchText)) &&
            MatchesIdentity(ProjectType, template.ProjectTypeId) &&
            MatchesIdentity(ProviderId, template.ProviderId) &&
            MatchesIdentity(OrganisationTemplateId, template.OrganisationTemplateId) &&
            MatchesAny(Language, template.Languages) && MatchesAny(Framework, template.Frameworks) &&
            MatchesAny(Platform, template.Platforms) && MatchesAny(ProductSurface, template.ProductSurfaces) &&
            MatchesAny(TargetRuntime, template.TargetRuntimes) &&
            (ToolchainsReady is null || option.CanCreate == ToolchainsReady.Value);
    }

    private static bool MatchesIdentity(string? expected, string? actual) => string.IsNullOrWhiteSpace(expected) ||
        StringComparer.Ordinal.Equals(actual, expected.Trim());

    private static bool MatchesAny(string? expected, IEnumerable<string>? actual) =>
        string.IsNullOrWhiteSpace(expected) || actual?.Any(value =>
            StringComparer.OrdinalIgnoreCase.Equals(value, expected.Trim())) == true;

    private static bool ContainsAny(IEnumerable<string>? values, string expected) =>
        values?.Any(value => Contains(value, expected)) == true;

    private static bool Contains(string? actual, string expected) => !string.IsNullOrWhiteSpace(actual) &&
        actual.Contains(expected.Trim(), StringComparison.OrdinalIgnoreCase);
}

public sealed record DeveloperProjectTemplateOption(
    DeveloperProjectTemplate Template,
    IReadOnlyList<DeveloperToolchainReadiness> Readiness)
{
    public bool CanCreate => Template.Requirements.All(requirement => !requirement.Required ||
        Readiness.Any(readiness => StringComparer.Ordinal.Equals(readiness.RequirementId, requirement.RequirementId) &&
                                   readiness.State == DeveloperToolchainState.Ready));
}

/// <summary>Provider seam keeps the catalogue extensible without binding Dev core to a fixed language list.</summary>
public interface IDeveloperProjectTemplateProvider
{
    string ProviderId { get; }
    Task<IReadOnlyList<DeveloperProjectTemplate>> GetTemplatesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<DeveloperToolchainReadiness>> GetReadinessAsync(
        DeveloperProjectTemplate template, CancellationToken cancellationToken);
}

public sealed class DeveloperProjectCatalog(IEnumerable<IDeveloperProjectTemplateProvider> providers)
{
    private readonly IReadOnlyList<IDeveloperProjectTemplateProvider> _providers = ValidateProviders(providers);

    public async Task<DeveloperOperationResult<IReadOnlyList<DeveloperProjectTemplateOption>>> SearchAsync(
        DeveloperProjectTemplateQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null)
            return DeveloperOperationResult<IReadOnlyList<DeveloperProjectTemplateOption>>.Failure(
                DeveloperOperationErrorCode.InvalidInput, "A project-template query is required.", "catalog");

        var requestId = Guid.NewGuid();
        try
        {
            var tasks = _providers.Select(provider => LoadProviderAsync(provider, cancellationToken)).ToArray();
            var templates = (await Task.WhenAll(tasks).ConfigureAwait(false)).SelectMany(static list => list);
            var metadataQuery = query with { ToolchainsReady = null };
            var matches = templates
                .Where(template => metadataQuery.Matches(new DeveloperProjectTemplateOption(template, [])))
                .OrderBy(static template => template.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            var options = new List<DeveloperProjectTemplateOption>(matches.Length);
            foreach (var template in matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var provider = _providers.First(candidate => StringComparer.Ordinal.Equals(candidate.ProviderId, template.ProviderId));
                var readiness = await provider.GetReadinessAsync(template, cancellationToken).ConfigureAwait(false);
                var requiredIds = template.Requirements.Select(requirement => requirement.RequirementId).ToHashSet(StringComparer.Ordinal);
                if (readiness is null || readiness.Any(static item => item is null || string.IsNullOrWhiteSpace(item.RequirementId)) ||
                    readiness.Select(item => item.RequirementId).Distinct(StringComparer.Ordinal).Count() != readiness.Count ||
                    readiness.Any(item => !requiredIds.Contains(item.RequirementId)) ||
                    template.Requirements.Any(requirement => !readiness.Any(item =>
                        StringComparer.Ordinal.Equals(item.RequirementId, requirement.RequirementId))))
                    throw new InvalidOperationException("A project-template provider omitted a required toolchain readiness result.");
                options.Add(new DeveloperProjectTemplateOption(template, readiness));
            }

            var filtered = options.Where(query.Matches).ToArray();
            return DeveloperOperationResult<IReadOnlyList<DeveloperProjectTemplateOption>>.Success(filtered, requestId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return DeveloperOperationResult<IReadOnlyList<DeveloperProjectTemplateOption>>.Failure(
                DeveloperOperationErrorCode.CapabilityUnavailable,
                "A project-template provider could not complete the catalogue query.", "catalog", retryable: true, requestId: requestId);
        }
    }

    private static async Task<IReadOnlyList<DeveloperProjectTemplate>> LoadProviderAsync(
        IDeveloperProjectTemplateProvider provider, CancellationToken cancellationToken)
    {
        var templates = await provider.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);
        if (templates is null || templates.Any(template => template is null || string.IsNullOrWhiteSpace(template.TemplateId) ||
                                       string.IsNullOrWhiteSpace(template.DisplayName) ||
                                       string.IsNullOrWhiteSpace(template.ProjectTypeId) ||
                                       string.IsNullOrWhiteSpace(template.ProviderId) ||
                                       !StringComparer.Ordinal.Equals(template.ProviderId, provider.ProviderId) ||
                                       template.TargetRuntimes?.Any(string.IsNullOrWhiteSpace) == true ||
                                       template.OrganisationTemplateId is { } org && (string.IsNullOrWhiteSpace(org) || org != org.Trim()) ||
                                       InvalidMetadata(template.Languages) || InvalidMetadata(template.Frameworks) ||
                                       InvalidMetadata(template.Platforms) || InvalidMetadata(template.ProductSurfaces) ||
                                       template.Requirements is null || template.Requirements.Any(requirement => requirement is null ||
                                           string.IsNullOrWhiteSpace(requirement.RequirementId)) ||
                                       template.Requirements.Select(requirement => requirement.RequirementId)
                                           .Distinct(StringComparer.Ordinal).Count() != template.Requirements.Count))
            throw new InvalidOperationException("A project-template provider returned an invalid or misattributed template.");
        if (templates.Select(template => template.TemplateId).Distinct(StringComparer.Ordinal).Count() != templates.Count)
            throw new InvalidOperationException("A project-template provider returned duplicate template identities.");
        return templates;
    }

    private static bool InvalidMetadata(IReadOnlyList<string>? values) =>
        values is null || values.Any(string.IsNullOrWhiteSpace);

    private static IReadOnlyList<IDeveloperProjectTemplateProvider> ValidateProviders(
        IEnumerable<IDeveloperProjectTemplateProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var snapshot = providers.ToArray();
        if (snapshot.Any(provider => provider is null || string.IsNullOrWhiteSpace(provider.ProviderId)) ||
            snapshot.Select(provider => provider.ProviderId).Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
            throw new ArgumentException("Project-template providers require distinct, stable identities.", nameof(providers));
        return snapshot;
    }
}
