using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;

namespace HavenOS.Apps.Dev;

public sealed record DeveloperRunTargetChoice(DeveloperProjectReference Project, DeveloperRunConfiguration Configuration,
    string DefinitionSha256, string Summary, string? UnavailableReason)
{
    public string SelectionKey => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
        new { Project, Configuration.Id, Configuration.Revision, DefinitionSha256 }))).ToLowerInvariant();
}

/// <summary>One actual configured console provider over the existing Workspace command owner.
/// Selection is metadata only; Task/Files/Home execution admission remains mandatory.</summary>
public static class DeveloperRunConfigurationSelection
{
    public const string ProviderId = nameof(ToolRuntimeKind.Workspace);
    public static DeveloperRunConfiguration Capture(DeveloperRunConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configuration.Arguments); ArgumentNullException.ThrowIfNull(configuration.PreLaunchTaskIds);
        return configuration with { Arguments = Array.AsReadOnly(configuration.Arguments.ToArray()),
            PreLaunchTaskIds = Array.AsReadOnly(configuration.PreLaunchTaskIds.ToArray()) };
    }

    /// <summary>Lowercase SHA-256 over exact UTF-8 default System.Text.Json serialization of the
    /// complete typed declaration. No command/path/case/whitespace/argument normalization.</summary>
    public static string Digest(DeveloperRunConfiguration configuration) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(configuration))).ToLowerInvariant();

    public static string? Unavailable(DeveloperResolvedProject project, DeveloperRunConfiguration configuration)
    {
        if (configuration.Revision < 1 || string.IsNullOrWhiteSpace(configuration.Id) || configuration.Id.Length > 128 || configuration.Id.Any(char.IsControl))
            return "The run configuration needs a stable bounded ID and positive revision.";
        if (!project.Project.RunConfigurationIds.Contains(configuration.Id, StringComparer.Ordinal) ||
            configuration.TargetProjectId != project.Project.ProjectId.ToString("D") ||
            configuration.WorkingDirectoryRootId != project.Root.RootId.ToString("D") || configuration.EnvironmentId != project.Root.EnvironmentId)
            return "This declared target does not match the selected project, root and environment.";
        if (configuration.ProviderId != ProviderId || configuration.TargetKind != "console")
            return "This owner supports declared Workspace console targets only. Other target providers require their real launch adapter.";
        if (configuration.EnvironmentId != "local") return "Remote target execution is not available from this local command owner.";
        if (configuration.Arguments.Count != 0) return "This provider requires an explicit Command; it does not reinterpret executable argument vectors.";
        if (configuration.PreLaunchTaskIds.Count != 0) return "This target has prelaunch dependencies. A single command cannot bypass their pipeline owner.";
        if (configuration.PreviewProviderId is not null) return "This target requests a preview owner that is not admitted by the console command provider.";
        if (string.IsNullOrWhiteSpace(configuration.Command) || configuration.Command.Length > 32_000 || configuration.Command.Contains('\0'))
            return "Declare a bounded explicit Command before running this console target. Older declarations without Command remain unavailable.";
        if (configuration.TimeoutSeconds is < 1 or > 900) return "The actual command owner supports timeouts from 1 to 900 seconds.";
        return null;
    }

    public static DeveloperOperationResult<IReadOnlyList<DeveloperRunTargetChoice>> Observe(DeveloperResolvedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var declarations = project.Workspace.RunConfigurations; var ids = project.Project.RunConfigurationIds;
        if (declarations is null || ids is null || ids.Count > 200 || ids.Any(string.IsNullOrWhiteSpace) ||
            ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || declarations.Any(configuration => configuration is null || string.IsNullOrWhiteSpace(configuration.Id)) ||
            declarations.Select(configuration => configuration.Id).Distinct(StringComparer.Ordinal).Count() != declarations.Count)
            return Invalid("Run configuration metadata is incomplete, ambiguous or exceeds this picker's 200-target bound.");
        var choices = new List<DeveloperRunTargetChoice>();
        foreach (var id in ids)
        {
            var declaration = declarations.SingleOrDefault(configuration => configuration.Id == id);
            if (declaration is null) return Invalid("A declared project RunConfigurationID has no saved configuration. Repair the project declaration.");
            DeveloperRunConfiguration captured;
            try { captured = Capture(declaration); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            { return Invalid("A run configuration changed during snapshotting or omitted its argument/dependency lists."); }
            var summary = $"Target {captured.Id}, revision {captured.Revision}; provider {captured.ProviderId}; kind {captured.TargetKind}.\n" +
                $"Project {captured.TargetProjectId}; root {captured.WorkingDirectoryRootId}; environment {captured.EnvironmentId}.\n" +
                $"Command: {captured.Command ?? "(not declared)"}\nArguments: {JsonSerializer.Serialize(captured.Arguments)}\n" +
                $"Prelaunch tasks: {JsonSerializer.Serialize(captured.PreLaunchTaskIds)}; preview: {captured.PreviewProviderId ?? "none"}; timeout: {captured.TimeoutSeconds ?? 120}s.";
            choices.Add(new(project.Reference, captured, Digest(captured), summary, Unavailable(project, captured)));
        }
        return DeveloperOperationResult<IReadOnlyList<DeveloperRunTargetChoice>>.Success(Array.AsReadOnly(choices.ToArray()));
    }
    private static DeveloperOperationResult<IReadOnlyList<DeveloperRunTargetChoice>> Invalid(string message) =>
        DeveloperOperationResult<IReadOnlyList<DeveloperRunTargetChoice>>.Failure(DeveloperOperationErrorCode.InvalidStoredData, message, "run-configurations");
}
