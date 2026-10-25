using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;

namespace HavenOS.Apps.Dev;

public sealed record DeveloperGitCommand(DeveloperProjectReference Project, DeveloperGitOperation Operation,
    string Command, string DefinitionSha256)
{
    public const int TimeoutSeconds = 120;

    /// <summary>Pure command intent. No process, trust, issuer, configuration or Task is created.</summary>
    public static DeveloperOperationResult<DeveloperGitCommand> Prepare(DeveloperResolvedProject project,
        DeveloperProjectGitInspectionKind operation) => Prepare(project, operation switch
        {
            DeveloperProjectGitInspectionKind.Status => DeveloperGitOperation.Status,
            DeveloperProjectGitInspectionKind.Diff => DeveloperGitOperation.Diff,
            DeveloperProjectGitInspectionKind.Branches => DeveloperGitOperation.Branches,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        });

    public static DeveloperOperationResult<DeveloperGitCommand> Prepare(DeveloperResolvedProject project,
        DeveloperGitOperation operation, string? branchName = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        if (project.Repository is not { ProviderId: "git" } repository || repository.RootId != project.Root.RootId ||
            project.Reference.RootId != project.Root.RootId || project.Reference.RepositoryBindingId != repository.BindingId)
            return DeveloperOperationResult<DeveloperGitCommand>.Failure(DeveloperOperationErrorCode.CapabilityUnavailable,
                "This project root has no current Git repository binding.", project.Reference.RootId.ToString("D"));
        string[] arguments = operation switch
        {
            DeveloperGitOperation.Status => ["status", "--porcelain=v1", "--branch"],
            DeveloperGitOperation.Diff => ["diff", "--no-ext-diff", "--no-textconv", "--"],
            DeveloperGitOperation.Branches => ["branch", "--list", "--no-color"],
            DeveloperGitOperation.CreateBranch => ["branch", "--", ValidateBranch(branchName)],
            DeveloperGitOperation.SwitchBranch => ["switch", "--no-guess", "--", ValidateBranch(branchName)],
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        var inspection = operation is DeveloperGitOperation.Status or DeveloperGitOperation.Diff or DeveloperGitOperation.Branches;
        var options = inspection ? " '--no-optional-locks' '-c' 'core.fsmonitor=false'" : "";
        var command = (OperatingSystem.IsWindows() ? "& " : "") + "'git' '--no-pager'" + options + " '-C' " + Quote(project.Root.Location) + " " + string.Join(" ", arguments.Select(Quote));
        // General JSON over the exact typed reference/root/repository, operation, literal command
        // and bounded timeout. Paths/IDs/command bytes are never normalised for this comparison.
        var digest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { project.Reference, project.Root, project.Repository, operation, command, timeoutSeconds = TimeoutSeconds }))).ToLowerInvariant();
        return DeveloperOperationResult<DeveloperGitCommand>.Success(new(project.Reference, operation, command, digest));
    }

    public static string ValidateBranch(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch) || branch.Length > 256 || branch.StartsWith('-') ||
            branch.Contains("..", StringComparison.Ordinal) || branch.Any(value => char.IsControl(value) || char.IsWhiteSpace(value) || "~^:?*[\\".Contains(value)))
            throw new ArgumentException("A literal Git branch name is required.", nameof(branch));
        return branch;
    }
    private static string Quote(string value) => "'" + value.Replace("'", OperatingSystem.IsWindows() ? "''" : "'\"'\"'", StringComparison.Ordinal) + "'";
}
