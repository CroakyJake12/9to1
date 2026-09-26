using System.Collections.ObjectModel;

namespace HavenOS.Apps.Dev;

/// <summary>A stable identifier paired with a selectable local or remote workspace root.</summary>
public sealed record DeveloperWorkspaceRoot(Guid RootId, string Location, string EnvironmentId = "local");

/// <summary>A stable project identity. Paths describe source locations but do not define the project ID.</summary>
public sealed record DeveloperProject(
    Guid ProjectId,
    string ProjectTypeId,
    string Name,
    IReadOnlyList<Guid> RootIds,
    string Language,
    string? Framework,
    string BuildSystem,
    IReadOnlyList<string> TargetPlatforms,
    IReadOnlyList<string> ToolchainRequirementIds,
    IReadOnlyList<string> RunConfigurationIds,
    IReadOnlyList<string> DebugConfigurationIds,
    string? PreviewProviderId,
    long Revision = 1);

/// <summary>Editor state holds canonical resource IDs, never a path as the durable file identity.</summary>
public sealed record DeveloperOpenEditor(
    Guid FileId,
    Guid? ProjectId,
    string CanonicalResourceId,
    string GroupId,
    int Order,
    bool IsPinned,
    bool IsPreview);

public sealed record DeveloperRunConfiguration(
    string Id,
    string ProviderId,
    string TargetProjectId,
    string TargetKind,
    IReadOnlyList<string> Arguments,
    string WorkingDirectoryRootId,
    string EnvironmentId,
    IReadOnlyList<string> PreLaunchTaskIds,
    string? PreviewProviderId,
    long Revision = 1);

public sealed record DeveloperDebugConfiguration(
    string Id,
    string ProviderId,
    string ProjectId,
    string RunConfigurationId,
    string? AdapterId,
    IReadOnlyDictionary<string, string> SafeSettings);

public sealed record DeveloperToolchain(
    string ToolchainId,
    string Language,
    string Provider,
    string Executable,
    string? Version,
    IReadOnlyList<string> TargetArchitectures,
    string? Debugger,
    string? LanguageServer,
    string? PackageManager,
    string EnvironmentId,
    DeveloperToolchainState State,
    string Provenance);

public enum DeveloperToolchainState
{
    Ready,
    MissingCompilerOrRuntime,
    VersionMismatch,
    MissingDebugger,
    MissingLanguageServer,
    MissingPackageManager,
    RegistryUnavailable,
    AuthenticationRequired,
    RestoreIncomplete,
    CapabilityUnavailable
}

public sealed record DeveloperEnvironment(
    string EnvironmentId,
    string AdapterId,
    string DisplayName,
    string Kind,
    string State,
    IReadOnlyDictionary<string, string> NonSecretProperties);

public sealed record DeveloperSourceControlBinding(
    string BindingId,
    Guid RootId,
    string ProviderId,
    string CanonicalRepositoryId);

public sealed record DeveloperExtensionState(
    string ExtensionId,
    string Version,
    bool Enabled,
    IReadOnlyList<string> GrantedCapabilities,
    string HostApiVersion);

public sealed record DeveloperWorkspaceSettings(
    string? SelectedProjectId,
    string? SelectedEnvironmentId,
    string DefaultAiReviewMode,
    IReadOnlyDictionary<string, string> LayoutByProjectId,
    IReadOnlyDictionary<string, string> NonSecretPreferences);

/// <summary>
/// Versioned canonical Dev workspace state. All identities are stable IDs; root locations are attributes.
/// Secret values are deliberately excluded and must be represented by credential references owned elsewhere.
/// </summary>
public sealed record DeveloperWorkspace(
    Guid WorkspaceId,
    IReadOnlyList<DeveloperWorkspaceRoot> Roots,
    IReadOnlyList<DeveloperProject> Projects,
    IReadOnlyList<DeveloperOpenEditor> OpenEditors,
    IReadOnlyList<DeveloperTaskDefinition> Tasks,
    IReadOnlyList<DeveloperRunConfiguration> RunConfigurations,
    IReadOnlyList<DeveloperDebugConfiguration> DebugConfigurations,
    IReadOnlyList<DeveloperToolchain> Toolchains,
    IReadOnlyList<DeveloperEnvironment> Environments,
    IReadOnlyList<DeveloperSourceControlBinding> SourceControlBindings,
    IReadOnlyList<DeveloperExtensionState> ExtensionState,
    DeveloperWorkspaceSettings Settings,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    long Revision)
{
    public static DeveloperWorkspace Create(IReadOnlyList<DeveloperWorkspaceRoot> roots, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var timestamp = now ?? DateTimeOffset.UtcNow;
        return new DeveloperWorkspace(
            Guid.NewGuid(),
            Array.AsReadOnly(roots.ToArray()),
            Array.Empty<DeveloperProject>(),
            Array.Empty<DeveloperOpenEditor>(),
            Array.Empty<DeveloperTaskDefinition>(),
            Array.Empty<DeveloperRunConfiguration>(),
            Array.Empty<DeveloperDebugConfiguration>(),
            Array.Empty<DeveloperToolchain>(),
            Array.Empty<DeveloperEnvironment>(),
            Array.Empty<DeveloperSourceControlBinding>(),
            Array.Empty<DeveloperExtensionState>(),
            new DeveloperWorkspaceSettings(null, null, "review-first",
                new ReadOnlyDictionary<string, string>(new Dictionary<string, string>()),
                new ReadOnlyDictionary<string, string>(new Dictionary<string, string>())),
            timestamp, timestamp, 1);
    }

    public string? Validate()
    {
        if (WorkspaceId == Guid.Empty) return "WorkspaceID must be a stable non-empty identifier.";
        if (Revision < 1) return "Workspace revision must be positive.";
        if (Roots is null || Roots.Count == 0) return "A workspace must have at least one root.";
        if (Projects is null || OpenEditors is null || Tasks is null || RunConfigurations is null ||
            DebugConfigurations is null || Toolchains is null || Environments is null ||
            SourceControlBindings is null || ExtensionState is null || Settings is null)
            return "Workspace collections and settings must be present; unknown and empty states are distinct.";
        if (Roots.Any(root => root is null || root.RootId == Guid.Empty || string.IsNullOrWhiteSpace(root.Location) ||
                              !Path.IsPathRooted(root.Location) || string.IsNullOrWhiteSpace(root.EnvironmentId)))
            return "Every workspace root must have an ID, an absolute location, and an environment ID.";
        if (Roots.Select(root => root.RootId).Distinct().Count() != Roots.Count)
            return "Workspace root IDs must be unique.";
        if (Projects.Any(project => project is null || project.ProjectId == Guid.Empty ||
                                    string.IsNullOrWhiteSpace(project.ProjectTypeId) || string.IsNullOrWhiteSpace(project.Name) ||
                                    project.RootIds is null || project.RootIds.Count == 0 ||
                                    project.RootIds.Any(id => Roots.All(root => root.RootId != id)) || project.Revision < 1))
            return "Every project must have a valid ID, type, name, workspace root, and revision.";
        if (Projects.Select(project => project.ProjectId).Distinct().Count() != Projects.Count)
            return "Project IDs must be unique within the workspace.";
        if (OpenEditors.Any(editor => editor is null || editor.FileId == Guid.Empty ||
                                      string.IsNullOrWhiteSpace(editor.CanonicalResourceId) ||
                                      string.IsNullOrWhiteSpace(editor.GroupId) || editor.Order < 0))
            return "Open editor state must reference canonical file identities and valid editor groups.";
        if (OpenEditors.Select(editor => editor.FileId).Distinct().Count() != OpenEditors.Count)
            return "An editor file identity may occur only once in the saved open-editor list.";
        if (OpenEditors.Any(editor => editor.ProjectId is { } id && Projects.All(project => project.ProjectId != id)))
            return "Open editor project references must resolve to a workspace project.";
        return null;
    }
}

public enum DeveloperOperationErrorCode
{
    WorkspaceNotFound,
    ProjectNotFound,
    FileNotFound,
    ToolchainMissing,
    ToolchainVersionMismatch,
    LanguageServiceUnavailable,
    BuildFailed,
    TestFailed,
    DebuggerUnavailable,
    RunConfigurationInvalid,
    PackageRestoreFailed,
    PackageAuthenticationRequired,
    ExtensionHostFailed,
    PreviewBuildFailed,
    VmProviderUnavailable,
    EnvironmentUnavailable,
    RevisionConflict,
    PermissionDenied,
    CapabilityUnavailable,
    UnsupportedSchemaVersion,
    InvalidInput,
    InvalidStoredData,
    StorageUnavailable
}

public sealed record DeveloperOperationError(
    DeveloperOperationErrorCode Code,
    string Message,
    string TargetId,
    bool Recoverable,
    bool Retryable);

/// <summary>Structured app/API result; RequestId identifies this attempt even when it fails.</summary>
public sealed record DeveloperOperationResult<T>(
    Guid RequestId,
    T? Value,
    DeveloperOperationError? Error)
{
    public bool Succeeded => Error is null;

    public static DeveloperOperationResult<T> Success(T value, Guid? requestId = null) =>
        new(requestId ?? Guid.NewGuid(), value, null);

    public static DeveloperOperationResult<T> Failure(
        DeveloperOperationErrorCode code, string message, string targetId,
        bool recoverable = true, bool retryable = false, Guid? requestId = null) =>
        new(requestId ?? Guid.NewGuid(), default,
            new DeveloperOperationError(code, message, targetId, recoverable, retryable));
}

public sealed record DeveloperTaskDefinition(
    string TaskId,
    string Label,
    string Source,
    string EnvironmentId,
    string WorkingDirectoryRootId,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<DeveloperBuildStage> Stages,
    bool RequiresWorkspaceTrust,
    long Revision = 1);

public sealed record DeveloperBuildStage(
    string StageId,
    DeveloperBuildStageKind Kind,
    string Label,
    IReadOnlyList<string> DependsOn,
    string? Command,
    IReadOnlyList<string> Arguments,
    TimeSpan? Timeout);

public enum DeveloperBuildStageKind { Restore, Generate, Compile, Link, Package, Test, Sign, Custom }
public enum DeveloperBuildStageState { Pending, Running, Succeeded, Failed, Cancelled, Blocked }

public sealed record DeveloperBuildStageResult(
    string StageId,
    DeveloperBuildStageState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int? ExitCode,
    string? DiagnosticSummary,
    string? LogReference);

public sealed record DeveloperRunSession(
    Guid RunId,
    string RunConfigurationId,
    string EnvironmentId,
    string State,
    DateTimeOffset StartedAt,
    DateTimeOffset? StoppedAt,
    string? StructuredOutputReference);

public sealed record DeveloperTestIdentity(string ProviderId, string StableId, string DisplayName, string? ParentId);
public sealed record DeveloperTestResult(
    Guid ResultId,
    DeveloperTestIdentity Test,
    string State,
    TimeSpan Duration,
    string? FailureMessage,
    string? SourceResourceId,
    string? OutputReference,
    DateTimeOffset CompletedAt);
