using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace HavenOS.Apps.Dev;

/// <summary>A source file supplied by the same provider that registered a project template.</summary>
public sealed record DeveloperProjectTemplateFile(string RelativePath, string Text);

/// <summary>Optional content contract for providers that can prepare real multi-file projects.</summary>
public interface IDeveloperProjectTemplateContentProvider : IDeveloperProjectTemplateProvider
{
    Task<IReadOnlyList<DeveloperProjectTemplateFile>> GetFilesAsync(
        DeveloperProjectTemplate template, CancellationToken cancellationToken);
}

public sealed record DeveloperMaterializedProjectFile(
    string RelativePath, string Text, long SizeBytes, string ContentSha256);

/// <summary>
/// A validated, deterministic template payload ready for the existing project-setup owner to
/// review and capture. It is data only: preparing it does not create Files items or grant access.
/// </summary>
public sealed record DeveloperProjectTemplateMaterialization(
    string ProjectName, string TemplateId, string ProviderId,
    ImmutableArray<DeveloperMaterializedProjectFile> Files, long TotalBytes);

/// <summary>Converts provider-owned template content into a bounded payload for the genuine
/// original Files/Home setup workflow. Permission and publication remain with that workflow.</summary>
public sealed class DeveloperProjectTemplateMaterializer(
    IEnumerable<IDeveloperProjectTemplateContentProvider> providers)
{
    public const int MaximumFiles = 64;
    public const int MaximumFileBytes = 4 * 1024 * 1024;
    public const int MaximumTotalBytes = 16 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ConditionalWeakTable<DeveloperProjectTemplateMaterialization, object> _issued = new();
    private readonly object _issuedGate = new();
    private readonly IReadOnlyList<IDeveloperProjectTemplateContentProvider> _providers = ValidateProviders(providers);

    public async Task<DeveloperOperationResult<DeveloperProjectTemplateMaterialization>> PrepareAsync(
        DeveloperProjectTemplateOption option, string projectName, CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid();
        if (option?.Template is null || !SafeName(projectName) ||
            string.IsNullOrWhiteSpace(option.Template.TemplateId) ||
            string.IsNullOrWhiteSpace(option.Template.ProjectTypeId) ||
            string.IsNullOrWhiteSpace(option.Template.ProviderId))
            return Failure(DeveloperOperationErrorCode.InvalidInput,
                "Choose a valid template and a project name of up to 120 characters.", requestId);

        var template = option.Template;
        var matchingProviders = _providers.Where(provider =>
            StringComparer.Ordinal.Equals(provider.ProviderId, template.ProviderId)).ToArray();
        if (matchingProviders.Length != 1)
            return Failure(DeveloperOperationErrorCode.CapabilityUnavailable,
                "The template does not have exactly one matching content provider.", requestId);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = matchingProviders[0];
            var registeredTemplates = await provider.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);
            var registeredMatches = registeredTemplates.Where(candidate => candidate is not null &&
                StringComparer.Ordinal.Equals(candidate.TemplateId, template.TemplateId)).ToArray();
            if (registeredMatches.Length != 1 ||
                !StringComparer.Ordinal.Equals(registeredMatches[0].ProjectTypeId, template.ProjectTypeId))
                return Failure(DeveloperOperationErrorCode.InvalidInput,
                    "The selected template is no longer registered by its owning provider.", requestId);

            var registeredTemplate = registeredMatches[0];
            var currentReadiness = await provider.GetReadinessAsync(registeredTemplate, cancellationToken).ConfigureAwait(false);
            if (!HasReadyRequirements(registeredTemplate, currentReadiness))
                return Failure(DeveloperOperationErrorCode.ToolchainMissing,
                    "Every required template toolchain must be ready at preparation time.", requestId);

            var suppliedFiles = await provider.GetFilesAsync(registeredTemplate, cancellationToken).ConfigureAwait(false);
            if (suppliedFiles is null || suppliedFiles.Count is 0 or > MaximumFiles)
                return Failure(DeveloperOperationErrorCode.InvalidStoredData,
                    "The template must provide between one and 64 source files.", requestId);

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var materialized = new List<DeveloperMaterializedProjectFile>(suppliedFiles.Count);
            long totalBytes = 0;
            foreach (var file in suppliedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file is null || !SafeRelativePath(file.RelativePath) || file.Text is null || !paths.Add(file.RelativePath))
                    return Failure(DeveloperOperationErrorCode.InvalidStoredData,
                        "The template contains an invalid or duplicate relative file path.", requestId);

                byte[] bytes;
                try { bytes = StrictUtf8.GetBytes(file.Text); }
                catch (EncoderFallbackException)
                {
                    return Failure(DeveloperOperationErrorCode.InvalidStoredData,
                        "The template contains text that cannot be encoded as valid UTF-8.", requestId);
                }
                if (bytes.Length > MaximumFileBytes)
                    return Failure(DeveloperOperationErrorCode.InvalidStoredData,
                        "A template file exceeds the 4 MiB per-file limit.", requestId);
                totalBytes = checked(totalBytes + bytes.Length);
                if (totalBytes > MaximumTotalBytes)
                    return Failure(DeveloperOperationErrorCode.InvalidStoredData,
                        "The template exceeds the 16 MiB total source limit.", requestId);
                materialized.Add(new(file.RelativePath, file.Text, bytes.LongLength,
                    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
            }

            if (HasFileDirectoryConflict(paths))
                return Failure(DeveloperOperationErrorCode.InvalidStoredData,
                    "A template file path conflicts with a directory needed by another file.", requestId);

            var result = new DeveloperProjectTemplateMaterialization(projectName, registeredTemplate.TemplateId,
                registeredTemplate.ProviderId, materialized.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToImmutableArray(), totalBytes);
            lock (_issuedGate) _issued.Add(result, new object());
            return DeveloperOperationResult<DeveloperProjectTemplateMaterialization>.Success(result, requestId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return DeveloperOperationResult<DeveloperProjectTemplateMaterialization>.Failure(
                DeveloperOperationErrorCode.CapabilityUnavailable,
                "The project-template content provider could not prepare this template.",
                "project-template", retryable: true, requestId: requestId);
        }
    }

    /// <summary>Recognizes only the exact immutable materialization returned by this instance.
    /// A copied or caller-constructed payload cannot be presented to the original Files setup as
    /// a provider result.</summary>
    public bool IsIssued(DeveloperProjectTemplateMaterialization sameMaterialization)
    {
        ArgumentNullException.ThrowIfNull(sameMaterialization);
        lock (_issuedGate) return _issued.TryGetValue(sameMaterialization, out _);
    }

    private static bool HasReadyRequirements(DeveloperProjectTemplate template,
        IReadOnlyList<DeveloperToolchainReadiness> readiness)
    {
        var requirements = template.Requirements;
        if (requirements is null || readiness is null || requirements.Any(requirement => requirement is null ||
                string.IsNullOrWhiteSpace(requirement.RequirementId)) || readiness.Any(item => item is null ||
                string.IsNullOrWhiteSpace(item.RequirementId)))
            return false;
        if (readiness.Select(item => item.RequirementId).Distinct(StringComparer.Ordinal).Count() != readiness.Count ||
            requirements.Select(item => item.RequirementId).Distinct(StringComparer.Ordinal).Count() != requirements.Count ||
            readiness.Any(item => requirements.All(requirement => !StringComparer.Ordinal.Equals(requirement.RequirementId, item.RequirementId))) ||
            requirements.Any(requirement => readiness.All(item => !StringComparer.Ordinal.Equals(item.RequirementId, requirement.RequirementId))))
            return false;
        return requirements.All(requirement => !requirement.Required || readiness.Any(item =>
            StringComparer.Ordinal.Equals(item.RequirementId, requirement.RequirementId) && item.State == DeveloperToolchainState.Ready));
    }

    private static IReadOnlyList<IDeveloperProjectTemplateContentProvider> ValidateProviders(
        IEnumerable<IDeveloperProjectTemplateContentProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var snapshot = providers.ToArray();
        if (snapshot.Any(provider => provider is null || string.IsNullOrWhiteSpace(provider.ProviderId)) ||
            snapshot.Select(provider => provider.ProviderId).Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
            throw new ArgumentException("Every template content provider must have a distinct, stable identity.", nameof(providers));
        return snapshot;
    }

    private static bool HasFileDirectoryConflict(HashSet<string> paths)
    {
        foreach (var path in paths)
        {
            for (var slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
                if (paths.Contains(path[..slash])) return true;
        }
        return false;
    }

    private static bool SafeName(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 120 &&
        value == value.Trim() && value is not ("." or "..") && !value.EndsWith('.') && !value.EndsWith(' ') &&
        value.All(character => !char.IsControl(character) && character is not ('/' or '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*' or '\0'));

    private static bool SafeRelativePath(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 2048 &&
        !Path.IsPathFullyQualified(value) && !value.Contains('\\') &&
        value.Split('/').All(SafeName);

    private static DeveloperOperationResult<DeveloperProjectTemplateMaterialization> Failure(
        DeveloperOperationErrorCode code, string message, Guid requestId) =>
        DeveloperOperationResult<DeveloperProjectTemplateMaterialization>.Failure(code, message,
            "project-template", requestId: requestId);
}
