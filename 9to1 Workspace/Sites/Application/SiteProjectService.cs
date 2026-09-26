using System.Text.Json;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Infrastructure;

namespace HavenOS.Apps.Sites.Application;

public sealed record CreateSiteProjectRequest(
    Guid FilesDirectoryId,
    Guid? StackProjectId,
    Guid? StackDomainId,
    string SourceRevision,
    string Name,
    string FrameworkId,
    string RelativeProjectPath);

public sealed class SiteProjectService(FileSiteWorkspaceStore store)
{
    public async Task<SiteApiResult<SiteProject>> CreateProjectAsync(CreateSiteProjectRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateCreateRequest(request);
            var now = DateTimeOffset.UtcNow;
            var siteId = Guid.NewGuid();
            var project = new SiteProject(
                siteId,
                Guid.NewGuid(),
                request.Name.Trim(),
                new SiteSourceBinding(request.FilesDirectoryId, request.StackProjectId, request.StackDomainId, request.SourceRevision.Trim(), request.FrameworkId.Trim(), NormalizeRelativePath(request.RelativeProjectPath)),
                Revision: 1,
                CreatedAt: now,
                UpdatedAt: now,
                Pages: Array.Empty<SitePage>(),
                Routes: Array.Empty<SiteRoute>(),
                Components: Array.Empty<SiteComponent>(),
                ReusableComponents: Array.Empty<SiteReusableComponent>(),
                DesignSystem: EmptyDesignSystem(),
                ContentCollections: Array.Empty<SiteContentCollection>(),
                Assets: Array.Empty<SiteAssetReference>(),
                Environments: DefaultEnvironments(),
                DomainBindingIds: Array.Empty<Guid>(),
                Redirects: Array.Empty<SiteRedirect>(),
                Settings: new Dictionary<string, JsonElement>(StringComparer.Ordinal));

            await store.MutateAsync(state =>
            {
                if (state.Projects.Any(existing => existing.Source.FilesDirectoryId == project.Source.FilesDirectoryId &&
                    string.Equals(existing.Source.RelativeProjectPath, project.Source.RelativeProjectPath, StringComparison.OrdinalIgnoreCase)))
                    throw Conflict("A Sites project is already bound to this Files directory and relative path.", "source");
                return (state with { Projects = [.. state.Projects, project] }, project);
            }, cancellationToken).ConfigureAwait(false);
            return SiteApiResult<SiteProject>.Success(project);
        }
        catch (SiteOperationException ex)
        {
            return SiteApiResult<SiteProject>.Failure(ex.Error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SiteApiResult<SiteProject>.Failure(new SiteApiError("Cancelled", "The Sites project creation was cancelled before it committed.", "Sites.CreateProject", true));
        }
    }

    public async Task<SiteApiResult<SiteProject>> GetProjectAsync(Guid siteId, CancellationToken cancellationToken = default)
    {
        if (siteId == Guid.Empty)
            return SiteApiResult<SiteProject>.Failure(new SiteApiError("InvalidInput", "A valid SiteID is required.", "siteID", false));
        try
        {
            var project = await store.ReadAsync(state => state.Projects.SingleOrDefault(candidate => candidate.SiteId == siteId), cancellationToken).ConfigureAwait(false);
            return project is null
                ? SiteApiResult<SiteProject>.Failure(new SiteApiError("SiteNotFound", "The Sites project was not found.", siteId.ToString(), false))
                : SiteApiResult<SiteProject>.Success(project);
        }
        catch (SiteOperationException ex)
        {
            return SiteApiResult<SiteProject>.Failure(ex.Error);
        }
    }

    public async Task<SiteApiResult<SiteProject>> UpdateProjectAsync(Guid siteId, long expectedRevision, Func<SiteProject, SiteProject> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        try
        {
            var saved = await store.MutateAsync(state =>
            {
                var index = IndexOf(state.Projects, project => project.SiteId == siteId);
                if (index < 0) throw new SiteOperationException(new SiteApiError("SiteNotFound", "The Sites project was not found.", siteId.ToString(), false));
                var current = state.Projects[index];
                if (current.Revision != expectedRevision)
                    throw new SiteOperationException(new SiteApiError("RevisionConflict", "The Sites project changed since it was opened. Reload it before saving.", siteId.ToString(), true));
                var updated = update(current);
                if (updated.SiteId != current.SiteId || updated.ProjectId != current.ProjectId || updated.Source.FilesDirectoryId != current.Source.FilesDirectoryId)
                    throw new SiteOperationException(new SiteApiError("IdentityMutationBlocked", "Stable SiteID, ProjectID, and Files identity cannot be changed by a project update.", siteId.ToString(), false));
                updated = updated with { Revision = checked(current.Revision + 1), UpdatedAt = DateTimeOffset.UtcNow };
                var projects = state.Projects.ToArray();
                projects[index] = updated;
                return (state with { Projects = projects }, updated);
            }, cancellationToken).ConfigureAwait(false);
            return SiteApiResult<SiteProject>.Success(saved);
        }
        catch (SiteOperationException ex)
        {
            return SiteApiResult<SiteProject>.Failure(ex.Error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SiteApiResult<SiteProject>.Failure(new SiteApiError("Cancelled", "The project update was cancelled before it committed.", siteId.ToString(), true));
        }
    }

    private static void ValidateCreateRequest(CreateSiteProjectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.FilesDirectoryId == Guid.Empty)
            throw Input("A canonical Files directory identity is required.", "filesDirectoryId");
        if (request.StackDomainId.HasValue != request.StackProjectId.HasValue)
            throw Input("Stack ProjectID and DomainID must be supplied together.", "sourceBinding");
        if (string.IsNullOrWhiteSpace(request.SourceRevision))
            throw Input("The source revision must be explicit.", "sourceRevision");
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
            throw Input("A project name between 1 and 120 characters is required.", "name");
        if (string.IsNullOrWhiteSpace(request.FrameworkId) || request.FrameworkId.Trim().Length > 100)
            throw Input("A registered framework/provider identifier is required.", "frameworkId");
        _ = NormalizeRelativePath(request.RelativeProjectPath);
    }

    private static string NormalizeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Input("The Sites project path must be relative to its Files directory.", "relativeProjectPath");
        var path = value.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(path) || path.StartsWith("/", StringComparison.Ordinal) ||
            path.Split('/').Any(part => part is ".." or "." || part.Length == 0))
            throw Input("The Sites project path must remain inside its canonical Files directory.", "relativeProjectPath");
        return path;
    }

    private static SiteDesignSystem EmptyDesignSystem() => new(
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, int>(StringComparer.Ordinal),
        "system",
        1);

    private static IReadOnlyList<SiteEnvironment> DefaultEnvironments() =>
    [
        new(Guid.NewGuid(), "Development", SiteEnvironmentKind.Development, false, true),
        new(Guid.NewGuid(), "Preview", SiteEnvironmentKind.Preview, false, true),
        new(Guid.NewGuid(), "Production", SiteEnvironmentKind.Production, true, true)
    ];

    private static int IndexOf<T>(IReadOnlyList<T> values, Func<T, bool> predicate)
    {
        for (var index = 0; index < values.Count; index++) if (predicate(values[index])) return index;
        return -1;
    }

    private static SiteOperationException Conflict(string message, string target) =>
        new(new SiteApiError("Conflict", message, target, false));

    private static SiteOperationException Input(string message, string target) =>
        new(new SiteApiError("InvalidInput", message, target, false));
}
