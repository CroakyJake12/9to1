namespace HavenOS.Apps.Dev;

/// <summary>A metadata observation of one exact saved project/root/repository choice. Opening
/// still resolves the current reference through the original project and source admission owners.</summary>
public sealed record DeveloperSavedProjectChoice(string SelectionKey, string Title, string Summary,
    string RootSummary, DeveloperProjectReference Reference);

public static class DeveloperSavedProjectChoices
{
    public static DeveloperOperationResult<IReadOnlyList<DeveloperSavedProjectChoice>> Build(
        IReadOnlyList<DeveloperWorkspace> workspaces, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(workspaces); token.ThrowIfCancellationRequested();
        var rows = new List<DeveloperSavedProjectChoice>(); var seen = new HashSet<Guid>();
        foreach (var workspace in workspaces)
        {
            token.ThrowIfCancellationRequested();
            if (workspace is null || workspace.Validate() is not null || !seen.Add(workspace.WorkspaceId))
                return Invalid("Saved workspace identities or project/root metadata are invalid or ambiguous.");
            var roots = workspace.Roots.ToDictionary(root => root.RootId);
            var bindings = workspace.SourceControlBindings.ToArray();
            if (bindings.Any(binding => binding is null || !roots.ContainsKey(binding.RootId) ||
                string.IsNullOrWhiteSpace(binding.BindingId) || string.IsNullOrWhiteSpace(binding.ProviderId) || string.IsNullOrWhiteSpace(binding.CanonicalRepositoryId)) ||
                bindings.GroupBy(binding => (binding.RootId, binding.BindingId)).Any(group => group.Count() != 1))
                return Invalid("Saved source-control bindings are incomplete or ambiguous. Repair the saved workspace before opening a repository choice.");
            foreach (var project in workspace.Projects)
            {
                if (project.RootIds.Distinct().Count() != project.RootIds.Count)
                    return Invalid("A saved project repeats the same root identity.");
                foreach (var rootId in project.RootIds)
                {
                    token.ThrowIfCancellationRequested(); var root = roots[rootId];
                    var repositories = bindings.Where(binding => binding.RootId == rootId).Cast<DeveloperSourceControlBinding?>().ToArray();
                    if (repositories.Length == 0) repositories = [null];
                    foreach (var repository in repositories)
                    {
                        var reference = new DeveloperProjectReference(workspace.WorkspaceId, workspace.Revision,
                            project.ProjectId, project.Revision, rootId, repository?.BindingId);
                        var key = FormattableString.Invariant($"manual/{workspace.WorkspaceId:D}/{workspace.Revision}/{project.ProjectId:D}/{project.Revision}/{rootId:D}/") +
                            (repository is null ? "none" : "repository/" + Uri.EscapeDataString(repository.BindingId));
                        rows.Add(new(key, project.Name,
                            $"Workspace {workspace.WorkspaceId:D} / revision {workspace.Revision}; project {project.ProjectId:D} / revision {project.Revision}\n{root.Location}",
                            $"Root {rootId:D}; environment {root.EnvironmentId}; " + (repository is null ? "no repository binding"
                                : $"{repository.ProviderId}: {repository.CanonicalRepositoryId} (binding {repository.BindingId})"), reference));
                    }
                }
            }
        }
        token.ThrowIfCancellationRequested();
        return DeveloperOperationResult<IReadOnlyList<DeveloperSavedProjectChoice>>.Success(Array.AsReadOnly(rows.ToArray()));
    }

    private static DeveloperOperationResult<IReadOnlyList<DeveloperSavedProjectChoice>> Invalid(string message) =>
        DeveloperOperationResult<IReadOnlyList<DeveloperSavedProjectChoice>>.Failure(DeveloperOperationErrorCode.InvalidStoredData, message, "saved-projects");
}
