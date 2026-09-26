using System.Collections.ObjectModel;
using System.Text.Json;

namespace HavenOS.AIStudio;

public enum StudioProjectType { Harness, Tool }
public enum StudioToolType { Skill, Plugin, Mcp }

public sealed record StudioDependency(string Kind, string StableId, string VersionPolicy = "latest-compatible");
public sealed record ProjectRevision(int Version, DateTimeOffset SavedAt, string SnapshotJson, string? Note = null);

public sealed record StudioProject(
    Guid ProjectId,
    StudioProjectType ProjectType,
    string Name,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    string OwnerScope,
    string TemplateId,
    int TemplateVersion,
    string DefinitionJson,
    IReadOnlyList<StudioDependency> Dependencies,
    IReadOnlyDictionary<string, string> PermissionMetadata,
    IReadOnlyList<ProjectRevision> Revisions,
    DateTimeOffset? DeletedAt = null)
{
    public bool IsDeleted => DeletedAt is not null;

    public T? ReadDefinition<T>() => JsonSerializer.Deserialize<T>(DefinitionJson, StudioJson.Options);
}

public sealed record HarnessDefinition(
    string Prompt,
    string ModelPolicy,
    IReadOnlyList<string> ToolIds,
    IReadOnlyList<Guid> PersistentAgentIds,
    IReadOnlyList<string> SubagentRoles,
    IReadOnlyList<string> SkillIds,
    IReadOnlyList<string> PluginIds,
    IReadOnlyList<string> McpCapabilityIds,
    IReadOnlyDictionary<string, string> ContextReferences,
    string OutputSchemaJson,
    string GraphJson,
    string? RunProfileId,
    bool RequireApprovalForExternalEffects,
    int MaxSteps,
    int MaxSubagents);

public sealed record ToolDefinition(
    StudioToolType Type,
    string Description,
    string Instructions,
    string ManifestJson,
    IReadOnlyList<StudioDependency> Dependencies,
    IReadOnlyDictionary<string, string> DeclaredPermissions,
    string Version,
    string TestConfigurationJson,
    string? CuiSurfaceJson = null);

public sealed record ProjectPage(IReadOnlyList<StudioProject> Items, string? NextCursor);

public sealed record StudioError(
    string Code,
    string Message,
    string? TargetId = null,
    bool Retryable = false,
    bool Recoverable = false);

public sealed record StudioResult<T>(T? Value, StudioError? Error)
{
    public bool IsSuccess => Error is null;
    public static StudioResult<T> Success(T value) => new(value, null);
    public static StudioResult<T> Failure(string code, string message, string? targetId = null,
        bool retryable = false, bool recoverable = false) =>
        new(default, new StudioError(code, message, targetId, retryable, recoverable));
}

public sealed record StudioOperation(string Name, string ArgumentsSchema, string ResultSchema,
    string RequiredPermission, string RiskLevel, bool Reversible, bool ExternalSideEffects);

public static class StudioJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}

public sealed record StudioStoreDocument(int SchemaVersion, IReadOnlyList<StudioProject> Projects);

public static class StudioApiCatalogue
{
    public static IReadOnlyList<StudioOperation> Operations { get; } = new ReadOnlyCollection<StudioOperation>([
        Op("ListProjects", "pageSize,cursor,includeDeleted", "ProjectPage", "studio.projects.read", "ordinary", true),
        Op("CreateProject", "type,templateId,name,ownerScope", "StudioProject", "studio.projects.create", "ordinary", true),
        Op("OpenProject", "projectId", "StudioProject", "studio.projects.read", "ordinary", true),
        Op("SaveProject", "projectId,expectedVersion,definition", "StudioProject", "studio.projects.edit", "ordinary", true),
        Op("DuplicateProject", "projectId,name", "StudioProject", "studio.projects.create", "ordinary", true),
        Op("RenameProject", "projectId,expectedVersion,name", "StudioProject", "studio.projects.edit", "ordinary", true),
        Op("ProjectHistory", "projectId", "ProjectRevision[]", "studio.projects.read", "ordinary", true),
        Op("DeleteProject", "projectId,expectedVersion", "deletedProject", "studio.projects.delete", "elevated", true),
        Op("RecoverProject", "projectId", "StudioProject", "studio.projects.recover", "ordinary", true),
        Op("ValidateProject", "projectId", "ValidationReport", "studio.projects.validate", "ordinary", true),
        Op("TestProject", "projectId,runProfileId", "StudioRun", "studio.runtime.execute", "elevated", false),
        Op("ExportProject", "projectId,format", "ExportArtifact", "studio.projects.export", "ordinary", true),
        Op("ImportProject", "content,conflictPolicy", "StudioProject", "studio.projects.import", "ordinary", true),
        Op("InspectDependencies", "projectId", "DependencyReport", "studio.dependencies.read", "ordinary", true),
        Op("CreateHarnessFromTemplate", "templateId,name,ownerScope", "StudioProject", "studio.projects.create", "ordinary", true),
        Op("CreateTool", "type,templateId,name,ownerScope", "StudioProject", "studio.projects.create", "ordinary", true),
        Op("ValidateTool", "projectId", "ValidationReport", "studio.projects.validate", "ordinary", true),
        Op("InstallTool", "projectId,expectedVersion", "InstallResult", "studio.tools.install", "elevated", false, true),
        Op("RunPlayground", "config,input,runProfileId", "StudioRun", "studio.runtime.execute", "elevated", false),
        Op("CreateEvaluation", "suite", "EvaluationSuite", "studio.evaluations.create", "ordinary", true),
        Op("RunEvaluation", "suiteId,target,runProfileId", "EvaluationRun", "studio.runtime.execute", "elevated", false),
        Op("CreateTestSuite", "suite", "TestSuite", "studio.tests.create", "ordinary", true),
        Op("RunTestSuite", "suiteId,target,runProfileId", "TestRun", "studio.runtime.execute", "elevated", false),
        Op("OpenReplay", "runId", "ReplayView", "studio.runs.read", "ordinary", true),
        Op("RestartReplay", "runId,nodeId,overrides", "StudioRun", "studio.runtime.execute", "elevated", false),
        Op("CreateSchema", "schema", "VersionedSchema", "studio.schemas.create", "ordinary", true),
        Op("CreateModelRouter", "policy", "ModelRouter", "studio.routers.create", "ordinary", true),
        Op("CreateGenerativeUi", "definition", "GenerativeUiDefinition", "studio.genui.create", "ordinary", true),
        Op("CreateRunProfile", "profile", "RunProfile", "studio.runprofiles.create", "ordinary", true),
        Op("InspectContext", "runOrRequestId", "ContextReport", "studio.context.read", "ordinary", true),
        Op("SimulatePermissions", "requestContext", "PermissionSimulation", "studio.permissions.simulate", "ordinary", true),
        Op("Compare", "left,right,options", "Comparison", "studio.compare", "ordinary", true),
        Op("GenerateDocumentation", "entityRef,format", "Documentation", "studio.documentation.generate", "ordinary", true)
    ]);

    private static StudioOperation Op(string name, string args, string result, string permission,
        string risk, bool reversible, bool external = false) =>
        new(name, args, result, permission, risk, reversible, external);
}
