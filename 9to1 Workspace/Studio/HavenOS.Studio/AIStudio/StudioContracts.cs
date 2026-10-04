using System.Collections.ObjectModel;
using System.Text.Json;

namespace HavenOS.AIStudio;

public enum StudioProjectType { Harness, Tool }
public enum StudioToolType { Skill, Plugin, Mcp }
public enum StudioResourceKind { EvaluationSuite, TestSuite, Schema, ModelRouter, GenerativeUi, RunProfile }

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
    DateTimeOffset? DeletedAt = null,
    IReadOnlyDictionary<string, string>? ProjectFiles = null,
    bool IsPinned = false)
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
    string RequiredPermission, string RiskLevel, bool Reversible, bool ExternalSideEffects,
    string AffectedObjects = "Target object", string AffectedScopes = "Current user scope");

public static class StudioJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}

public sealed record StudioStoreDocument(int SchemaVersion, IReadOnlyList<StudioProject> Projects);

public sealed record StudioResource(Guid ResourceId, StudioResourceKind Kind, string Name, int Version,
    DateTimeOffset CreatedAt, DateTimeOffset ModifiedAt, string OwnerScope, string DefinitionJson,
    IReadOnlyList<ProjectRevision> Revisions, DateTimeOffset? DeletedAt = null);

public sealed record EvaluationCase(Guid CaseId, string Name, string Input, string ContextJson,
    string? ExpectedOutputJson, string? ExpectedOutputSchemaJson, IReadOnlyList<string> RequiredActions,
    IReadOnlyList<string> ForbiddenActions, decimal? MaximumCost, TimeSpan? MaximumLatency,
    string? ScoringRubric, string SourceProvenance, int TargetRevision);

public enum EvaluationGraderType { ExactOutput, OutputSchema, RequiredActions, ForbiddenActions, CostLimit, LatencyLimit, RubricModel, HumanReview, CustomTool }
public sealed record EvaluationGrader(Guid GraderId, EvaluationGraderType Type, string ConfigurationJson, bool IsDeterministic);
public sealed record EvaluationSuite(Guid EvalSuiteId, string Name, string TargetId, string TargetKind,
    int TargetRevision, IReadOnlyList<EvaluationCase> Cases, IReadOnlyList<EvaluationGrader> Graders,
    string? RunProfileId, Guid? BaselineRunId, DateTimeOffset CreatedAt, int Version);
public sealed record EvaluationCaseResult(Guid CaseId, int Repetition, string Status, IReadOnlyList<string> DeterministicFacts,
    string? ModelJudgement, double? Score, string? StructuredErrorCode, Guid? RunId);
public sealed record EvaluationRun(Guid RunId, Guid EvalSuiteId, int TargetRevision, string? RunProfileId,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, IReadOnlyList<EvaluationCaseResult> Results,
    IReadOnlyDictionary<string, string> DependencyRevisions, string? BaselineComparisonJson);

public sealed record TestAssertion(string AssertionId, string Type, string Path, string ExpectedJson);
public sealed record StudioTestCase(Guid CaseId, string Name, string Input, string ContextJson,
    IReadOnlyList<TestAssertion> Assertions, int TargetRevision, string SourceProvenance);
public sealed record TestSuite(Guid TestSuiteId, string Name, string TargetId, string TargetKind,
    int TargetRevision, IReadOnlyList<StudioTestCase> Cases, string? RunProfileId, int Version);
public sealed record TestAssertionResult(string AssertionId, bool Passed, string Message, string? ActualJson);
public sealed record StudioTestCaseResult(Guid CaseId, string Status, IReadOnlyList<TestAssertionResult> Assertions,
    Guid? RunId, string? StructuredErrorCode, string? ReproductionJson);
public sealed record TestRun(Guid RunId, Guid TestSuiteId, int TargetRevision, string? RunProfileId,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, IReadOnlyList<StudioTestCaseResult> Results);

public sealed record DependencyNode(string Kind, string StableId, string Version, string State, string? SourceIdentity);
public sealed record DependencyEdge(string FromId, string ToId, string Relation);
public sealed record DependencyReport(IReadOnlyList<DependencyNode> Nodes, IReadOnlyList<DependencyEdge> Edges,
    IReadOnlyList<string> Cycles, DateTimeOffset InspectedAt);

public sealed record ContextItem(string Kind, string StableId, string Revision, string Provenance,
    string InclusionStatus, string? RetrievalReason, long? EstimatedTokens, string? Content);
public sealed record ContextReport(Guid RunOrRequestId, IReadOnlyList<ContextItem> Items,
    string InstructionSource, bool IsObservedRuntimeContext);

public enum SimulatedPermissionDecision { Allowed, Ask, Denied }
public sealed record PermissionSimulationDecision(string CapabilityId, SimulatedPermissionDecision Decision,
    IReadOnlyList<string> ControllingPolicies, string Reason);
public sealed record PermissionSimulation(IReadOnlyList<PermissionSimulationDecision> Decisions,
    string ControllingAuthority, DateTimeOffset EvaluatedAt, bool ExecutedActions);

public sealed record ComparisonResult(string LeftIdentity, string RightIdentity, bool OutputsEqual,
    IReadOnlyList<string> ChangedFields, string LeftOutput, string RightOutput);
public sealed record DocumentationArtifact(string EntityId, string Format, string Content, string SourceVersion,
    DateTimeOffset GeneratedAt);

public static class StudioApiCatalogue
{
    public static IReadOnlyList<StudioOperation> Operations { get; } = new ReadOnlyCollection<StudioOperation>([
        Op("ListProjects", "pageSize,cursor,includeDeleted", "ProjectPage", "studio.projects.read", "ordinary", true),
        Op("CreateResource", "kind,name,definition,ownerScope", "StudioResource", "studio.resources.create", "ordinary", true),
        Op("OpenResource", "resourceId,expectedKind", "StudioResource", "studio.resources.read", "ordinary", true),
        Op("RenameResource", "resourceId,expectedVersion,name", "StudioResource", "studio.resources.edit", "ordinary", true),
        Op("DeleteResource", "resourceId,expectedVersion", "recoverable StudioResource", "studio.resources.delete", "elevated", true),
        Op("RecoverResource", "resourceId", "StudioResource", "studio.resources.recover", "ordinary", true),
        Op("ResourceHistory", "resourceId", "ProjectRevision[]", "studio.resources.read", "ordinary", true),
        Op("CreateProject", "type,templateId,name,ownerScope", "StudioProject", "studio.projects.create", "ordinary", true),
        Op("OpenProject", "projectId", "StudioProject", "studio.projects.read", "ordinary", true),
        Op("SaveProject", "projectId,expectedVersion,definition", "StudioProject", "studio.projects.edit", "ordinary", true),
        Op("SetProjectFile", "projectId,expectedVersion,path,content", "StudioProject", "studio.projects.edit", "ordinary", true),
        Op("DuplicateProject", "projectId,name", "StudioProject", "studio.projects.create", "ordinary", true),
        Op("RenameProject", "projectId,expectedVersion,name", "StudioProject", "studio.projects.edit", "ordinary", true),
        Op("SetProjectPinned", "projectId,expectedVersion,isPinned", "StudioProject", "studio.projects.organize", "ordinary", true),
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
        Op("ListResources", "kind,cursor,pageSize", "StudioResource[]", "studio.resources.read", "ordinary", true),
        Op("UpdateResource", "resourceId,expectedVersion,definition", "StudioResource", "studio.resources.edit", "ordinary", true),
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
