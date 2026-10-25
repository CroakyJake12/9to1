using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HavenOS.Apps.Dev;

/// <summary>Immutable declaration review only. No command is run and no Files/Home permission is
/// issued. The canonical store must compare the exact workspace and previous declaration again.</summary>
public sealed class DeveloperRunConfigurationEditReview
{
    internal DeveloperRunConfigurationEditReview(DeveloperProjectReference project, string workspaceSha256,
        string? beforeSha256, DeveloperRunConfiguration after)
    { Project = project; WorkspaceSha256 = workspaceSha256; BeforeDefinitionSha256 = beforeSha256; Configuration = after; }
    public DeveloperProjectReference Project { get; }
    public string WorkspaceSha256 { get; }
    public string? BeforeDefinitionSha256 { get; }
    public DeveloperRunConfiguration Configuration { get; }
    public string AfterDefinitionSha256 => DeveloperRunConfigurationSelection.Digest(Configuration);
    public string Summary => $"{(BeforeDefinitionSha256 is null ? "Create" : "Update")} console target {Configuration.Id}; revision {Configuration.Revision}.\n" +
        $"Project {Project.ProjectId:D}; root {Configuration.WorkingDirectoryRootId}; environment {Configuration.EnvironmentId}.\n" +
        $"Command: {Configuration.Command}\nTimeout: {Configuration.TimeoutSeconds ?? 120}s.\nDefinition SHA-256: {AfterDefinitionSha256}\n" +
        "Save changes project metadata only. Compatible protected drafts and clean layouts migrate atomically; source files and live drafts are unchanged. Run still requires a separate Home approval.";
}

public static class DeveloperRunConfigurationEdit
{
    public static string WorkspaceDigest(DeveloperWorkspace workspace) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(workspace))).ToLowerInvariant();

    public static DeveloperOperationResult<DeveloperRunConfigurationEditReview> Prepare(DeveloperResolvedProject project,
        string id, string command, int timeoutSeconds, string? expectedBeforeDefinitionSha256 = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Root.EnvironmentId != "local" || project.Reference.ProjectRevision == long.MaxValue ||
            project.Reference.WorkspaceRevision == long.MaxValue || string.IsNullOrWhiteSpace(id) || id.Length > 128 ||
            id.Any(char.IsControl) || id.Trim() != id || string.IsNullOrWhiteSpace(command) || command.Length > 32_000 || command.Contains('\0') ||
            timeoutSeconds is < 1 or > 900)
            return Invalid("Choose a bounded stable target ID, explicit local command and timeout from 1 to 900 seconds.");
        try { _ = new UTF8Encoding(false, true).GetByteCount(command); }
        catch (EncoderFallbackException) { return Invalid("The exact command must contain valid Unicode; it was not normalized."); }
        var observed = DeveloperRunConfigurationSelection.Observe(project);
        if (!observed.Succeeded) return Invalid(observed.Error!.Message);
        var existing = project.Workspace.RunConfigurations.SingleOrDefault(value => value.Id == id);
        DeveloperRunConfiguration after;
        if (expectedBeforeDefinitionSha256 is null)
        {
            if (existing is not null) return Invalid("That target ID already exists. Select its current declaration to edit it; it will not be adopted or overwritten.");
            if (project.Project.RunConfigurationIds.Count >= 200) return Invalid("The project's bounded target catalogue is full; no target was removed.");
            after = new(id, DeveloperRunConfigurationSelection.ProviderId, project.Project.ProjectId.ToString("D"), "console", [],
                project.Root.RootId.ToString("D"), "local", [], null) { Command = command, TimeoutSeconds = timeoutSeconds };
        }
        else
        {
            if (existing is null || existing.Revision == long.MaxValue ||
                DeveloperRunConfigurationSelection.Digest(existing) != expectedBeforeDefinitionSha256 ||
                !project.Project.RunConfigurationIds.Contains(id, StringComparer.Ordinal))
                return Invalid("The exact previous target declaration changed or belongs to another project. Select it again.");
            after = DeveloperRunConfigurationSelection.Capture(existing) with { Command = command,
                TimeoutSeconds = existing.TimeoutSeconds is null && timeoutSeconds == 120 ? null : timeoutSeconds,
                Revision = checked(existing.Revision + 1) };
            if (DeveloperRunConfigurationSelection.Digest(after with { Revision = existing.Revision }) == expectedBeforeDefinitionSha256)
                return Invalid("The target declaration is unchanged; no metadata or recovery migration is needed.");
        }
        var ids = project.Project.RunConfigurationIds.Contains(id, StringComparer.Ordinal)
            ? project.Project.RunConfigurationIds.ToArray() : project.Project.RunConfigurationIds.Append(id).ToArray();
        var declared = project with { Project = project.Project with { RunConfigurationIds = Array.AsReadOnly(ids) } };
        if (DeveloperRunConfigurationSelection.Unavailable(declared, after) is { } reason) return Invalid(reason);
        return DeveloperOperationResult<DeveloperRunConfigurationEditReview>.Success(new(project.Reference,
            WorkspaceDigest(project.Workspace), expectedBeforeDefinitionSha256, DeveloperRunConfigurationSelection.Capture(after)));
    }
    public static bool IsCurrent(DeveloperResolvedProject project, DeveloperRunConfigurationEditReview reviewed) =>
        reviewed.Project == project.Reference && WorkspaceDigest(project.Workspace) == reviewed.WorkspaceSha256 &&
        Prepare(project, reviewed.Configuration.Id, reviewed.Configuration.Command!, reviewed.Configuration.TimeoutSeconds ?? 120,
            reviewed.BeforeDefinitionSha256) is { Succeeded: true, Value: { } current } &&
        current.AfterDefinitionSha256 == reviewed.AfterDefinitionSha256;
    private static DeveloperOperationResult<DeveloperRunConfigurationEditReview> Invalid(string detail) =>
        DeveloperOperationResult<DeveloperRunConfigurationEditReview>.Failure(DeveloperOperationErrorCode.InvalidInput, detail, "run-configurations");
}
