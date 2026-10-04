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
    bool IsFirstParty);

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
    bool? ToolchainsReady = null);

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
    private readonly IReadOnlyList<IDeveloperProjectTemplateProvider> _providers =
        providers?.ToArray() ?? throw new ArgumentNullException(nameof(providers));

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
            var matches = templates
                .Where(template => Matches(template, query))
                .OrderBy(static template => template.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            var options = new List<DeveloperProjectTemplateOption>(matches.Length);
            foreach (var template in matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var provider = _providers.First(candidate => StringComparer.Ordinal.Equals(candidate.ProviderId, template.ProviderId));
                var readiness = await provider.GetReadinessAsync(template, cancellationToken).ConfigureAwait(false);
                if (readiness.Any(static item => item is null || string.IsNullOrWhiteSpace(item.RequirementId)) ||
                    template.Requirements.Any(requirement => !readiness.Any(item =>
                        StringComparer.Ordinal.Equals(item.RequirementId, requirement.RequirementId))))
                    throw new InvalidOperationException("A project-template provider omitted a required toolchain readiness result.");
                options.Add(new DeveloperProjectTemplateOption(template, readiness));
            }

            var filtered = query.ToolchainsReady is null
                ? options
                : options.Where(option => option.CanCreate == query.ToolchainsReady.Value).ToList();
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
        if (templates.Any(template => template is null || string.IsNullOrWhiteSpace(template.TemplateId) ||
                                      string.IsNullOrWhiteSpace(template.ProjectTypeId) ||
                                      !StringComparer.Ordinal.Equals(template.ProviderId, provider.ProviderId)))
            throw new InvalidOperationException("A project-template provider returned an invalid or misattributed template.");
        return templates;
    }

    private static bool Matches(DeveloperProjectTemplate template, DeveloperProjectTemplateQuery query) =>
        (string.IsNullOrWhiteSpace(query.SearchText) || Contains(template.DisplayName, query.SearchText) ||
         Contains(template.TemplateId, query.SearchText) || Contains(template.ProjectTypeId, query.SearchText)) &&
        (string.IsNullOrWhiteSpace(query.Language) || ContainsAny(template.Languages, query.Language)) &&
        (string.IsNullOrWhiteSpace(query.Framework) || ContainsAny(template.Frameworks, query.Framework)) &&
        (string.IsNullOrWhiteSpace(query.Platform) || ContainsAny(template.Platforms, query.Platform)) &&
        (string.IsNullOrWhiteSpace(query.ProductSurface) || ContainsAny(template.ProductSurfaces, query.ProductSurface));

    private static bool ContainsAny(IEnumerable<string> values, string expected) => values.Any(value => Contains(value, expected));
    private static bool Contains(string value, string expected) => value.Contains(expected.Trim(), StringComparison.OrdinalIgnoreCase);
}
