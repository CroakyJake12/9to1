using Haven.Core;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Services;

/// <summary>
/// Resource identity for an Open-in-Build native content address. It names the EXACT existing
/// canonical project (workspace/project/root with their current revisions) plus the canonical
/// Task/Run/context the workbench borrows when the address is a project-open request. It is
/// identity only: it grants no project, task, permission or readiness authority.
/// </summary>
internal sealed record BuildBrowseAppContentAddress(
    DeveloperProjectReference? ProjectReference,
    Guid TaskId,
    Guid ExecutionId,
    Guid ContextId)
{
    internal const string Authority = "dev";

    /// <summary>The plain Build landing (no specific project) opens the saved-project discovery entry.</summary>
    internal static readonly BuildBrowseAppContentAddress Landing = new(null, Guid.Empty, Guid.Empty, Guid.Empty);

    internal static BuildBrowseAppContentAddress Parse(AppContentAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Namespace != AppContentNamespace.NativeApp || address.Authority != Authority)
            throw new UnauthorizedAccessException("Select the registered native Build (Dev) app.");

        var segments = address.ResourceSegments;
        if (segments.Count == 0) return Landing;
        if (segments.Count != 16 || segments[0] != "projects")
            throw new FormatException("A Build location requires its canonical project reference.");

        Guid RequireGuid(string segment, string what) => Guid.TryParseExact(segment, "D", out var id) && id != Guid.Empty
            ? id : throw new FormatException($"A Build location requires a canonical {what}.");
        long RequireLong(string segment, string what) => long.TryParse(segment, out var value) && value >= 1
            ? value : throw new FormatException($"A Build location requires {what}.");

        string Check(string expected, int index, string what) =>
            string.Equals(segments[index], expected, StringComparison.Ordinal)
                ? segments[index] : throw new FormatException($"A Build project-open address requires {what}.");

        Check("workspace", 2, "the workspace marker");
        var workspaceId = RequireGuid(segments[3], "workspace identity");
        Check("roots", 4, "the root marker");
        var rootId = RequireGuid(segments[5], "root identity");
        Check("wsrv", 6, "the workspace-revision marker");
        var workspaceRevision = RequireLong(segments[7], "workspace revision");
        Check("prv", 8, "the project-revision marker");
        var projectRevision = RequireLong(segments[9], "project revision");
        Check("task", 10, "the Task marker");
        var taskId = RequireGuid(segments[11], "Task identity");
        Check("run", 12, "the run marker");
        var executionId = RequireGuid(segments[13], "run identity");
        Check("ctx", 14, "the context marker");
        var contextId = RequireGuid(segments[15], "context identity");

        var projectId = RequireGuid(segments[1], "project identity");
        var reference = new DeveloperProjectReference(workspaceId, workspaceRevision, projectId, projectRevision, rootId);
        return new(reference, taskId, executionId, contextId);
    }

    internal static AppContentAddress For(DeveloperProjectReference reference, Guid taskId, Guid executionId, Guid contextId) =>
        AppContentAddress.Parse(
            $"9to1://{Authority}/projects/{reference.ProjectId:D}/workspace/{reference.WorkspaceId:D}/roots/{reference.RootId:D}" +
            $"/wsrv/{reference.WorkspaceRevision}/prv/{reference.ProjectRevision}/task/{taskId:D}/run/{executionId:D}/ctx/{contextId:D}");
}
