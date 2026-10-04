using System.Text;
using System.Text.Json;

namespace HavenOS.AIStudio;

public interface IStudioRuntimeAdapter
{
    Task<StudioResult<StudioRun>> RunHarnessAsync(StudioProject project, string input, string? runProfileId, CancellationToken cancellationToken);
    Task<StudioResult<StudioRun>> RunPlaygroundAsync(PlaygroundConfiguration configuration, string input, CancellationToken cancellationToken);
    Task<StudioResult<InstallReceipt>> InstallToolAsync(StudioProject project, CancellationToken cancellationToken);
}

public interface ICanonicalAgentBuilderAdapter
{
    Task<StudioResult<CanonicalAgentReference>> CreateAsync(CanonicalAgentDefinition definition, CancellationToken cancellationToken);
    Task<StudioResult<CanonicalAgentDefinition>> OpenAsync(Guid agentId, CancellationToken cancellationToken);
    Task<StudioResult<CanonicalAgentDefinition>> UpdateDraftAsync(Guid agentId, long expectedRevision, CanonicalAgentDefinition definition, CancellationToken cancellationToken);
    Task<StudioResult<ValidationReport>> ValidateAsync(Guid agentId, long? revision, CancellationToken cancellationToken);
    Task<StudioResult<StudioRun>> PreviewAsync(Guid agentId, string input, AgentPreviewOptions options, CancellationToken cancellationToken);
    Task<StudioResult<CanonicalAgentReference>> ActivateAsync(Guid agentId, long revision, CancellationToken cancellationToken);
    Task<StudioResult<Unit>> ShareAsync(Guid agentId, string principalId, string role, CancellationToken cancellationToken);
}

public interface IStudioPermissionSimulationAdapter
{
    Task<StudioResult<PermissionSimulation>> EvaluateAsync(string requestContextJson, CancellationToken cancellationToken);
}

public interface IStudioContextInspectorAdapter
{
    Task<StudioResult<ContextReport>> InspectAsync(Guid runOrRequestId, CancellationToken cancellationToken);
}

public interface IStudioReplayAdapter
{
    Task<StudioResult<StudioRun>> OpenAsync(Guid runId, CancellationToken cancellationToken);
    Task<StudioResult<StudioRun>> RestartFromAsync(Guid runId, string nodeId, string overridesJson, CancellationToken cancellationToken);
}

public sealed record Unit;
public sealed record CanonicalAgentReference(Guid AgentId, long DefinitionRevision, string Owner);
public sealed record CanonicalAgentDefinition(Guid AgentId, string Name, string Description, string Instructions,
    string ConfigurationJson, long DefinitionRevision, bool IsDraft);
public sealed record AgentPreviewOptions(string? RunProfileId, bool TestScoped, IReadOnlyDictionary<string, bool> PermissionSimulation);
public sealed record StudioRun(Guid RunId, string TargetId, string Status, string Output,
    string EffectiveModel, string EffectiveProvider, string EffectiveContextJson, string ActivityJson,
    string ActionGraphJson, string PermissionsJson, long? InputTokens, long? OutputTokens,
    TimeSpan? Duration, string? StructuredErrorCode, string? StructuredErrorMessage,
    int? DefinitionVersion, string? RunProfileId, decimal? CostAmount = null, string? CostCurrency = null);
public sealed record InstallReceipt(string PackageId, string PackageVersion, string Status);
public sealed record PlaygroundConfiguration(string? ModelId, Guid? AgentId, IReadOnlyList<string> SkillIds,
    IReadOnlyList<string> PluginIds, IReadOnlyDictionary<string, string> ContextReferences, string? RunProfileId,
    string Instructions);
public sealed record ValidationIssue(string Code, string Message, string Path, string Severity);
public sealed record ValidationReport(bool IsValid, IReadOnlyList<ValidationIssue> Issues,
    string ValidationScope, DateTimeOffset ValidatedAt);

/// <summary>
/// First-party AI Studio API. Project operations are fully local and durable; execution,
/// installation and canonical Agent operations require registered Dulche/Den adapters.
/// </summary>
public sealed class AIStudioApi(IStudioProjectStore store, IStudioRuntimeAdapter runtime,
    ICanonicalAgentBuilderAdapter agents, IStudioResourceStore resources,
    IStudioPermissionSimulationAdapter permissionSimulator, IStudioContextInspectorAdapter contextInspector,
    IStudioReplayAdapter replay)
{
    internal const string RuntimeUnavailableMessage = "The canonical Dulche runtime adapter is not registered. This action was not simulated or executed.";

    public async Task<StudioResult<ProjectPage>> ListProjectsAsync(int pageSize = 50, string? cursor = null,
        bool includeDeleted = false, string? search = null, StudioProjectType? type = null, bool pinnedOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > 200) return StudioResult<ProjectPage>.Failure("InvalidPageSize", "Page size must be between 1 and 200.");
        if (!TryDecodeCursor(cursor, out var offset)) return StudioResult<ProjectPage>.Failure("InvalidCursor", "The project cursor is invalid.");
        var all = await SafeReadAsync(includeDeleted, cancellationToken).ConfigureAwait(false);
        if (!all.IsSuccess) return StudioResult<ProjectPage>.Failure(all.Error!.Code, all.Error.Message, all.Error.TargetId, all.Error.Retryable, all.Error.Recoverable);
        var filtered = all.Value!
            .Where(item => type is null || item.ProjectType == type)
            .Where(item => !pinnedOnly || item.IsPinned)
            .Where(item => string.IsNullOrWhiteSpace(search) || item.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (offset > filtered.Length) return StudioResult<ProjectPage>.Failure("InvalidCursor", "The project cursor is outside this result set.");
        var page = filtered.Skip(offset).Take(pageSize).ToArray();
        var next = offset + page.Length < filtered.Length ? EncodeCursor(offset + page.Length) : null;
        return StudioResult<ProjectPage>.Success(new ProjectPage(page, next));
    }

    public async Task<StudioResult<StudioProject>> CreateProjectAsync(StudioProjectType type, string templateId,
        string name, string ownerScope = "user", CancellationToken cancellationToken = default)
    {
        var template = StudioTemplateCatalog.Find(templateId);
        if (template is null) return StudioResult<StudioProject>.Failure("TemplateNotFound", "The selected template does not exist.", templateId);
        if (template.ProjectType != type) return StudioResult<StudioProject>.Failure("TemplateTypeMismatch", "The selected template does not create this project type.", templateId);
        if (type == StudioProjectType.Tool && template.ToolType is null)
            return StudioResult<StudioProject>.Failure("InvalidDefinition", "A Tool project requires a type-specific Skill, Plugin or MCP template.", templateId);
        if (string.IsNullOrWhiteSpace(name)) return StudioResult<StudioProject>.Failure("InvalidDefinition", "A project name is required.");
        if (string.IsNullOrWhiteSpace(ownerScope)) return StudioResult<StudioProject>.Failure("InvalidDefinition", "An owner/scope is required.");

        var now = DateTimeOffset.UtcNow;
        var project = new StudioProject(Guid.NewGuid(), type, name.Trim(), 1, now, now, ownerScope.Trim(),
            template.TemplateId, template.Version, template.DefinitionJson, template.Dependencies,
            new Dictionary<string, string>(), [], null, new Dictionary<string, string>(), false);
        var write = await SafeWriteAsync(project, cancellationToken).ConfigureAwait(false);
        return write.IsSuccess ? StudioResult<StudioProject>.Success(project) : Failure<StudioProject>(write.Error!);
    }

    public Task<StudioResult<StudioProject>> CreateHarnessFromTemplateAsync(string templateId, string name,
        string ownerScope = "user", CancellationToken cancellationToken = default) =>
        CreateProjectAsync(StudioProjectType.Harness, templateId, name, ownerScope, cancellationToken);

    public Task<StudioResult<StudioProject>> CreateToolAsync(StudioToolType type, string templateId, string name,
        string ownerScope = "user", CancellationToken cancellationToken = default)
    {
        var template = StudioTemplateCatalog.Find(templateId);
        if (template?.ToolType != type) return Task.FromResult(StudioResult<StudioProject>.Failure(
            "TemplateTypeMismatch", "Tool type and type-specific template must match.", templateId));
        return CreateProjectAsync(StudioProjectType.Tool, templateId, name, ownerScope, cancellationToken);
    }

    public async Task<StudioResult<StudioProject>> OpenProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var read = await SafeGetAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess) return Failure<StudioProject>(read.Error!);
        return read.Value is null || read.Value.IsDeleted
            ? StudioResult<StudioProject>.Failure("ProjectNotFound", "The project does not exist or is in the recovery area.", projectId.ToString())
            : StudioResult<StudioProject>.Success(read.Value);
    }

    public async Task<StudioResult<StudioProject>> SaveProjectAsync(Guid projectId, int expectedVersion,
        string definitionJson, IReadOnlyList<StudioDependency>? dependencies = null,
        IReadOnlyDictionary<string, string>? permissionMetadata = null, string? note = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseJson(definitionJson, out var normalized))
            return StudioResult<StudioProject>.Failure("InvalidDefinition", "Definition must be valid JSON.", projectId.ToString());
        var current = await GetForMutationAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess) return Failure<StudioProject>(current.Error!);
        if (current.Value!.Version != expectedVersion)
            return StudioResult<StudioProject>.Failure("RevisionConflict", $"The project is version {current.Value.Version}; version {expectedVersion} was submitted.", projectId.ToString(), recoverable: true);
        var now = DateTimeOffset.UtcNow;
        var revisions = current.Value.Revisions.Append(new ProjectRevision(current.Value.Version, now,
            current.Value.DefinitionJson, note)).ToArray();
        var updated = current.Value with { Version = current.Value.Version + 1, ModifiedAt = now,
            DefinitionJson = normalized!, Dependencies = dependencies ?? current.Value.Dependencies,
            PermissionMetadata = permissionMetadata ?? current.Value.PermissionMetadata, Revisions = revisions };
        var write = await SafeWriteAsync(updated, cancellationToken).ConfigureAwait(false);
        return write.IsSuccess ? StudioResult<StudioProject>.Success(updated) : Failure<StudioProject>(write.Error!);
    }

    public Task<StudioResult<StudioProject>> RenameProjectAsync(Guid projectId, int expectedVersion, string name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return Task.FromResult(StudioResult<StudioProject>.Failure("InvalidDefinition", "A project name is required.", projectId.ToString()));
        return MutateAsync(projectId, expectedVersion, current => current with { Name = name.Trim() }, cancellationToken);
    }

    public Task<StudioResult<StudioProject>> SetProjectPinnedAsync(Guid projectId, int expectedVersion, bool isPinned,
        CancellationToken cancellationToken = default) =>
        MutateAsync(projectId, expectedVersion, current => current with { IsPinned = isPinned }, cancellationToken);

    public async Task<StudioResult<StudioProject>> SetProjectFileAsync(Guid projectId, int expectedVersion,
        string relativePath, string content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Length > 240 ||
            relativePath.Contains(':') || relativePath.Contains('\0'))
            return StudioResult<StudioProject>.Failure("InvalidProjectPath", "Project file path must be a short, relative path.", projectId.ToString());
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or "..") || parts[0].Equals(".git", StringComparison.OrdinalIgnoreCase))
            return StudioResult<StudioProject>.Failure("InvalidProjectPath", "Project file path cannot escape its project or address repository metadata.", projectId.ToString());
        if (Encoding.UTF8.GetByteCount(content) > 1_048_576)
            return StudioResult<StudioProject>.Failure("ProjectFileTooLarge", "Project files are limited to 1 MiB per file.", projectId.ToString());
        var current = await GetForMutationAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess) return Failure<StudioProject>(current.Error!);
        if (current.Value!.Version != expectedVersion) return StudioResult<StudioProject>.Failure("RevisionConflict", "Project version changed.", projectId.ToString(), recoverable: true);
        var files = new Dictionary<string, string>(current.Value.ProjectFiles ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        files[string.Join('/', parts)] = content;
        var now = DateTimeOffset.UtcNow;
        var updated = current.Value with { Version = current.Value.Version + 1, ModifiedAt = now, ProjectFiles = files,
            Revisions = current.Value.Revisions.Append(new ProjectRevision(current.Value.Version, now, current.Value.DefinitionJson,
                "Project file updated: " + string.Join('/', parts))).ToArray() };
        var write = await SafeWriteAsync(updated, cancellationToken).ConfigureAwait(false);
        return write.IsSuccess ? StudioResult<StudioProject>.Success(updated) : Failure<StudioProject>(write.Error!);
    }

    public async Task<StudioResult<StudioProject>> DuplicateProjectAsync(Guid projectId, string name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return StudioResult<StudioProject>.Failure("InvalidDefinition", "A name is required for the copy.", projectId.ToString());
        var source = await OpenProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (!source.IsSuccess) return Failure<StudioProject>(source.Error!);
        var now = DateTimeOffset.UtcNow;
        var copy = source.Value! with { ProjectId = Guid.NewGuid(), Name = name.Trim(), Version = 1,
            CreatedAt = now, ModifiedAt = now, Revisions = [], DeletedAt = null };
        var write = await SafeWriteAsync(copy, cancellationToken).ConfigureAwait(false);
        return write.IsSuccess ? StudioResult<StudioProject>.Success(copy) : Failure<StudioProject>(write.Error!);
    }

    public async Task<StudioResult<IReadOnlyList<ProjectRevision>>> ProjectHistoryAsync(Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await SafeGetAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (!project.IsSuccess) return Failure<IReadOnlyList<ProjectRevision>>(project.Error!);
        return project.Value is null
            ? StudioResult<IReadOnlyList<ProjectRevision>>.Failure("ProjectNotFound", "The project was not found.", projectId.ToString())
            : StudioResult<IReadOnlyList<ProjectRevision>>.Success(project.Value.Revisions);
    }

    public Task<StudioResult<StudioProject>> DeleteProjectAsync(Guid projectId, int expectedVersion,
        CancellationToken cancellationToken = default) =>
        MutateAsync(projectId, expectedVersion, current => current with { DeletedAt = DateTimeOffset.UtcNow }, cancellationToken);

    public Task<StudioResult<StudioProject>> RecoverProjectAsync(Guid projectId,
        CancellationToken cancellationToken = default) => MutateDeletedAsync(projectId, current => current with
        { DeletedAt = null, ModifiedAt = DateTimeOffset.UtcNow, Version = current.Version + 1 }, cancellationToken);

    public async Task<StudioResult<ValidationReport>> ValidateProjectAsync(Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var opened = await OpenProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess) return Failure<ValidationReport>(opened.Error!);
        var project = opened.Value!;
        var issues = new List<ValidationIssue>();
        if (project.ProjectType == StudioProjectType.Harness)
        {
            var harness = project.ReadDefinition<HarnessDefinition>();
            if (harness is null) issues.Add(new("InvalidDefinition", "Harness configuration could not be read.", "$", "error"));
            else
            {
                if (string.IsNullOrWhiteSpace(harness.Prompt)) issues.Add(new("InvalidDefinition", "A Harness requires instructions.", "$.prompt", "error"));
                if (string.IsNullOrWhiteSpace(harness.ModelPolicy)) issues.Add(new("InvalidDefinition", "Select a model policy.", "$.modelPolicy", "error"));
                if (harness.MaxSteps is < 1 or > 1000) issues.Add(new("InvalidDefinition", "MaxSteps must be between 1 and 1000.", "$.maxSteps", "error"));
                if (harness.MaxSubagents is < 0 or > 64) issues.Add(new("InvalidDefinition", "MaxSubagents must be between 0 and 64.", "$.maxSubagents", "error"));
                if (!TryParseJson(harness.OutputSchemaJson, out _)) issues.Add(new("InvalidDefinition", "Output schema must be valid JSON.", "$.outputSchemaJson", "error"));
                if (!TryParseJson(harness.GraphJson, out _)) issues.Add(new("InvalidDefinition", "Graph document must be valid JSON.", "$.graphJson", "error"));
                if (harness.PersistentAgentIds.Any(id => id == Guid.Empty)) issues.Add(new("DependencyMissing", "Persistent Agent references require stable IDs.", "$.persistentAgentIds", "error"));
            }
        }
        else
        {
            var tool = project.ReadDefinition<ToolDefinition>();
            if (tool is null) issues.Add(new("InvalidDefinition", "Tool configuration could not be read.", "$", "error"));
            else
            {
                if (tool.Type is not (StudioToolType.Skill or StudioToolType.Plugin or StudioToolType.Mcp))
                    issues.Add(new("InvalidDefinition", "Tool type must be Skill, Plugin or MCP.", "$.type", "error"));
                if (string.IsNullOrWhiteSpace(tool.Version)) issues.Add(new("InvalidDefinition", "Tool version is required.", "$.version", "error"));
                if (!TryParseJson(tool.ManifestJson, out var normalizedManifest)) issues.Add(new("InvalidDefinition", "Tool manifest must be valid JSON.", "$.manifestJson", "error"));
                else
                {
                    using var manifest = JsonDocument.Parse(normalizedManifest!);
                    var schemaVersion = manifest!.RootElement.TryGetProperty("schemaVersion", out var schema) && schema.TryGetInt32(out var version) ? version : 0;
                    if (schemaVersion != 1) issues.Add(new("SchemaIncompatible", "Manifest schemaVersion must be 1.", "$.manifestJson.schemaVersion", "error"));
                    if (tool.Type == StudioToolType.Skill && string.IsNullOrWhiteSpace(tool.Instructions))
                        issues.Add(new("InvalidDefinition", "Skill instructions are required.", "$.instructions", "error"));
                    if (tool.Type == StudioToolType.Plugin && !manifest.RootElement.TryGetProperty("actions", out _))
                        issues.Add(new("InvalidDefinition", "Plugin manifest must declare actions.", "$.manifestJson.actions", "error"));
                    if (tool.Type == StudioToolType.Mcp && (!manifest.RootElement.TryGetProperty("role", out _) || !manifest.RootElement.TryGetProperty("capabilities", out _)))
                        issues.Add(new("InvalidDefinition", "MCP manifest must declare role and capabilities.", "$.manifestJson", "error"));
                }
            }
        }
        var report = new ValidationReport(issues.All(issue => issue.Severity != "error"), issues,
            "AI Studio structural checks only; canonical Dulche/Home validators are not registered.", DateTimeOffset.UtcNow);
        return StudioResult<ValidationReport>.Success(report);
    }

    public async Task<StudioResult<StudioRun>> TestProjectAsync(Guid projectId, string? runProfileId = null,
        CancellationToken cancellationToken = default)
    {
        var project = await OpenProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (!project.IsSuccess) return Failure<StudioRun>(project.Error!);
        var report = await ValidateProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (!report.IsSuccess || !report.Value!.IsValid)
            return StudioResult<StudioRun>.Failure("ValidationFailed", "The project failed structural validation.", projectId.ToString(), recoverable: true);
        var harness = project.Value!.ReadDefinition<HarnessDefinition>();
        if (harness is null) return StudioResult<StudioRun>.Failure("UnsupportedProjectType", "Only Harness projects can be run.", projectId.ToString());
        return await runtime.RunHarnessAsync(project.Value, "Run the configured Harness test.", runProfileId, cancellationToken).ConfigureAwait(false);
    }

    public Task<StudioResult<InstallReceipt>> InstallToolAsync(Guid projectId, int expectedVersion,
        CancellationToken cancellationToken = default) => InstallToolCoreAsync(projectId, expectedVersion, cancellationToken);

    private async Task<StudioResult<InstallReceipt>> InstallToolCoreAsync(Guid projectId, int expectedVersion, CancellationToken cancellationToken)
    {
        var project = await OpenProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (!project.IsSuccess) return Failure<InstallReceipt>(project.Error!);
        if (project.Value!.ProjectType != StudioProjectType.Tool)
            return StudioResult<InstallReceipt>.Failure("InvalidDefinition", "Only Tool projects can be installed.", projectId.ToString());
        if (project.Value.Version != expectedVersion)
            return StudioResult<InstallReceipt>.Failure("RevisionConflict", "The Tool changed after confirmation.", projectId.ToString(), recoverable: true);
        var validation = await ValidateProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (!validation.IsSuccess || !validation.Value!.IsValid)
            return StudioResult<InstallReceipt>.Failure("ValidationFailed", "The Tool failed structural validation.", projectId.ToString(), recoverable: true);
        return await runtime.InstallToolAsync(project.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StudioResult<StudioRun>> RunPlaygroundAsync(PlaygroundConfiguration config, string input,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input)) return StudioResult<StudioRun>.Failure("InvalidDefinition", "Playground input is required.");
        return await runtime.RunPlaygroundAsync(config, input, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StudioResult<StudioResource>> CreateResourceAsync(StudioResourceKind kind, string name,
        string definitionJson, string ownerScope = "user", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ownerScope))
            return StudioResult<StudioResource>.Failure("InvalidDefinition", "Resource name and owner/scope are required.");
        if (!TryParseJson(definitionJson, out var definition))
            return StudioResult<StudioResource>.Failure("InvalidDefinition", "Resource definition must be valid JSON.");
        var now = DateTimeOffset.UtcNow;
        var resource = new StudioResource(Guid.NewGuid(), kind, name.Trim(), 1, now, now, ownerScope.Trim(), definition!, []);
        var validation = ValidateResource(resource);
        if (!validation.IsSuccess) return Failure<StudioResource>(validation.Error!);
        try { await resources.WriteResourceAsync(resource, cancellationToken).ConfigureAwait(false); }
        catch (InvalidDataException ex) { return StudioResult<StudioResource>.Failure("StoreSchemaUnsupported", ex.Message, recoverable: true); }
        catch (IOException ex) { return StudioResult<StudioResource>.Failure("PersistenceUnavailable", ex.Message, retryable: true); }
        return StudioResult<StudioResource>.Success(resource);
    }

    public Task<StudioResult<StudioResource>> CreateEvaluationSuiteAsync(string name, EvaluationSuite suite,
        string ownerScope = "user", CancellationToken cancellationToken = default) =>
        CreateResourceAsync(StudioResourceKind.EvaluationSuite, name, JsonSerializer.Serialize(suite, StudioJson.Options), ownerScope, cancellationToken);

    public Task<StudioResult<StudioResource>> CreateTestSuiteAsync(string name, TestSuite suite,
        string ownerScope = "user", CancellationToken cancellationToken = default) =>
        CreateResourceAsync(StudioResourceKind.TestSuite, name, JsonSerializer.Serialize(suite, StudioJson.Options), ownerScope, cancellationToken);

    public Task<StudioResult<StudioResource>> CreateSchemaAsync(string name, string schemaJson,
        string ownerScope = "user", CancellationToken cancellationToken = default) =>
        CreateResourceAsync(StudioResourceKind.Schema, name, schemaJson, ownerScope, cancellationToken);

    public Task<StudioResult<StudioResource>> CreateModelRouterAsync(string name, string policyJson,
        string ownerScope = "user", CancellationToken cancellationToken = default) =>
        CreateResourceAsync(StudioResourceKind.ModelRouter, name, policyJson, ownerScope, cancellationToken);

    public Task<StudioResult<StudioResource>> CreateGenerativeUiAsync(string name, string definitionJson,
        string ownerScope = "user", CancellationToken cancellationToken = default) =>
        CreateResourceAsync(StudioResourceKind.GenerativeUi, name, definitionJson, ownerScope, cancellationToken);

    public Task<StudioResult<StudioResource>> CreateRunProfileAsync(string name, string profileJson,
        string ownerScope = "user", CancellationToken cancellationToken = default) =>
        CreateResourceAsync(StudioResourceKind.RunProfile, name, profileJson, ownerScope, cancellationToken);

    public async Task<StudioResult<IReadOnlyList<StudioResource>>> ListResourcesAsync(StudioResourceKind? kind = null,
        CancellationToken cancellationToken = default)
    {
        try { return StudioResult<IReadOnlyList<StudioResource>>.Success(await resources.ListAsync(kind, cancellationToken).ConfigureAwait(false)); }
        catch (InvalidDataException ex) { return StudioResult<IReadOnlyList<StudioResource>>.Failure("StoreSchemaUnsupported", ex.Message, recoverable: true); }
        catch (IOException ex) { return StudioResult<IReadOnlyList<StudioResource>>.Failure("PersistenceUnavailable", ex.Message, retryable: true); }
    }

    public async Task<StudioResult<StudioResource>> OpenResourceAsync(Guid id, StudioResourceKind? expectedKind = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var resource = await resources.GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (resource is null || resource.DeletedAt is not null) return StudioResult<StudioResource>.Failure("ResourceNotFound", "The resource does not exist.", id.ToString());
            if (expectedKind is not null && resource.Kind != expectedKind) return StudioResult<StudioResource>.Failure("ResourceTypeMismatch", "The resource has a different type.", id.ToString());
            return StudioResult<StudioResource>.Success(resource);
        }
        catch (InvalidDataException ex) { return StudioResult<StudioResource>.Failure("StoreSchemaUnsupported", ex.Message, id.ToString(), recoverable: true); }
        catch (IOException ex) { return StudioResult<StudioResource>.Failure("PersistenceUnavailable", ex.Message, id.ToString(), retryable: true); }
    }

    public async Task<StudioResult<StudioResource>> UpdateResourceAsync(Guid id, int expectedVersion, string definitionJson,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseJson(definitionJson, out var definition)) return StudioResult<StudioResource>.Failure("InvalidDefinition", "Resource definition must be valid JSON.", id.ToString());
        var opened = await OpenResourceAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess) return Failure<StudioResource>(opened.Error!);
        var current = opened.Value!;
        if (current.Version != expectedVersion) return StudioResult<StudioResource>.Failure("RevisionConflict", "The resource changed since it was opened.", id.ToString(), recoverable: true);
        var updated = current with { Version = current.Version + 1, ModifiedAt = DateTimeOffset.UtcNow,
            DefinitionJson = definition!, Revisions = current.Revisions.Append(new ProjectRevision(current.Version,
                DateTimeOffset.UtcNow, current.DefinitionJson)).ToArray() };
        var validation = ValidateResource(updated);
        if (!validation.IsSuccess) return Failure<StudioResource>(validation.Error!);
        try { await resources.WriteResourceAsync(updated, cancellationToken).ConfigureAwait(false); }
        catch (IOException ex) { return StudioResult<StudioResource>.Failure("PersistenceUnavailable", ex.Message, id.ToString(), retryable: true); }
        return StudioResult<StudioResource>.Success(updated);
    }

    public async Task<StudioResult<IReadOnlyList<ProjectRevision>>> ResourceHistoryAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        var opened = await OpenResourceAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false);
        return opened.IsSuccess
            ? StudioResult<IReadOnlyList<ProjectRevision>>.Success(opened.Value!.Revisions)
            : Failure<IReadOnlyList<ProjectRevision>>(opened.Error!);
    }

    public async Task<StudioResult<StudioResource>> RenameResourceAsync(Guid id, int expectedVersion, string name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return StudioResult<StudioResource>.Failure("InvalidDefinition", "A resource name is required.", id.ToString());
        var opened = await OpenResourceAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess) return Failure<StudioResource>(opened.Error!);
        var current = opened.Value!;
        if (current.Version != expectedVersion) return StudioResult<StudioResource>.Failure("RevisionConflict", "The resource changed since it was opened.", id.ToString(), recoverable: true);
        var now = DateTimeOffset.UtcNow;
        var renamed = current with { Name = name.Trim(), Version = current.Version + 1, ModifiedAt = now,
            Revisions = current.Revisions.Append(new ProjectRevision(current.Version, now, current.DefinitionJson, "Renamed resource")).ToArray() };
        try { await resources.WriteResourceAsync(renamed, cancellationToken).ConfigureAwait(false); }
        catch (IOException ex) { return StudioResult<StudioResource>.Failure("PersistenceUnavailable", ex.Message, id.ToString(), retryable: true); }
        return StudioResult<StudioResource>.Success(renamed);
    }

    public async Task<StudioResult<StudioResource>> SaveResourceAsync(Guid id, int expectedVersion, string name,
        string definitionJson, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return StudioResult<StudioResource>.Failure("InvalidDefinition", "A resource name is required.", id.ToString());
        if (!TryParseJson(definitionJson, out var definition)) return StudioResult<StudioResource>.Failure("InvalidDefinition", "Resource definition must be valid JSON.", id.ToString());
        var opened = await OpenResourceAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess) return Failure<StudioResource>(opened.Error!);
        var current = opened.Value!;
        if (current.Version != expectedVersion) return StudioResult<StudioResource>.Failure("RevisionConflict", "The resource changed since it was opened.", id.ToString(), recoverable: true);
        var now = DateTimeOffset.UtcNow;
        var updated = current with { Name = name.Trim(), Version = current.Version + 1, ModifiedAt = now,
            DefinitionJson = definition!, Revisions = current.Revisions.Append(new ProjectRevision(current.Version,
                now, current.DefinitionJson, "Edited resource")).ToArray() };
        var validation = ValidateResource(updated);
        if (!validation.IsSuccess) return Failure<StudioResource>(validation.Error!);
        try { await resources.WriteResourceAsync(updated, cancellationToken).ConfigureAwait(false); }
        catch (IOException ex) { return StudioResult<StudioResource>.Failure("PersistenceUnavailable", ex.Message, id.ToString(), retryable: true); }
        return StudioResult<StudioResource>.Success(updated);
    }

    public async Task<StudioResult<StudioResource>> DeleteResourceAsync(Guid id, int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var opened = await OpenResourceAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess) return Failure<StudioResource>(opened.Error!);
        var current = opened.Value!;
        if (current.Version != expectedVersion) return StudioResult<StudioResource>.Failure("RevisionConflict", "The resource changed since it was opened.", id.ToString(), recoverable: true);
        var now = DateTimeOffset.UtcNow;
        var deleted = current with { Version = current.Version + 1, ModifiedAt = now, DeletedAt = now,
            Revisions = current.Revisions.Append(new ProjectRevision(current.Version, now, current.DefinitionJson, "Moved to recovery")).ToArray() };
        try { await resources.WriteResourceAsync(deleted, cancellationToken).ConfigureAwait(false); }
        catch (IOException ex) { return StudioResult<StudioResource>.Failure("PersistenceUnavailable", ex.Message, id.ToString(), retryable: true); }
        return StudioResult<StudioResource>.Success(deleted);
    }

    public async Task<StudioResult<StudioResource>> RecoverResourceAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await resources.GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (deleted is null || deleted.DeletedAt is null)
                return StudioResult<StudioResource>.Failure("ResourceNotFound", "The resource is not in the recovery area.", id.ToString());
            var now = DateTimeOffset.UtcNow;
            var recovered = deleted with { Version = deleted.Version + 1, ModifiedAt = now, DeletedAt = null,
                Revisions = deleted.Revisions.Append(new ProjectRevision(deleted.Version, now, deleted.DefinitionJson, "Recovered resource")).ToArray() };
            await resources.WriteResourceAsync(recovered, cancellationToken).ConfigureAwait(false);
            return StudioResult<StudioResource>.Success(recovered);
        }
        catch (InvalidDataException ex) { return StudioResult<StudioResource>.Failure("StoreSchemaUnsupported", ex.Message, id.ToString(), recoverable: true); }
        catch (IOException ex) { return StudioResult<StudioResource>.Failure("PersistenceUnavailable", ex.Message, id.ToString(), retryable: true); }
    }

    public async Task<StudioResult<DependencyReport>> InspectDependenciesAsync(Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var root = await OpenProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (!root.IsSuccess) return Failure<DependencyReport>(root.Error!);
        var projects = await SafeReadAsync(includeDeleted: false, cancellationToken).ConfigureAwait(false);
        if (!projects.IsSuccess) return Failure<DependencyReport>(projects.Error!);
        var all = projects.Value!;
        var nodes = new List<DependencyNode> { new(root.Value!.ProjectType.ToString(), root.Value.ProjectId.ToString(), root.Value.Version.ToString(), "available", root.Value.TemplateId) };
        var edges = new List<DependencyEdge>();
        var cycles = new List<string>();
        var active = new HashSet<string>(StringComparer.Ordinal);
        var complete = new HashSet<string>(StringComparer.Ordinal);
        Visit(root.Value);
        return StudioResult<DependencyReport>.Success(new DependencyReport(nodes, edges, cycles.Distinct().ToArray(), DateTimeOffset.UtcNow));

        void Visit(StudioProject project)
        {
            var fromId = project.ProjectId.ToString();
            if (!active.Add(fromId)) { cycles.Add(fromId); return; }
            if (!complete.Contains(fromId))
            {
                foreach (var dependency in project.Dependencies)
                {
                    var target = all.FirstOrDefault(item => item.ProjectId.ToString().Equals(dependency.StableId, StringComparison.OrdinalIgnoreCase));
                    edges.Add(new DependencyEdge(fromId, dependency.StableId, dependency.Kind));
                    if (target is null)
                    {
                        nodes.Add(new DependencyNode(dependency.Kind, dependency.StableId, dependency.VersionPolicy, "missing", null));
                        continue;
                    }
                    nodes.Add(new DependencyNode(target.ProjectType.ToString(), dependency.StableId, target.Version.ToString(), "available", target.TemplateId));
                    if (active.Contains(dependency.StableId)) cycles.Add(fromId + " -> " + dependency.StableId);
                    else Visit(target);
                }
                complete.Add(fromId);
            }
            active.Remove(fromId);
        }
    }

    public async Task<StudioResult<ContextReport>> InspectContextAsync(Guid runOrRequestId, CancellationToken cancellationToken = default) =>
        await contextInspector.InspectAsync(runOrRequestId, cancellationToken).ConfigureAwait(false);

    public Task<StudioResult<PermissionSimulation>> SimulatePermissionsAsync(string requestContextJson,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseJson(requestContextJson, out _)) return Task.FromResult(StudioResult<PermissionSimulation>.Failure("InvalidDefinition", "Permission simulation context must be valid JSON."));
        return permissionSimulator.EvaluateAsync(requestContextJson, cancellationToken);
    }

    public async Task<StudioResult<StudioRun>> OpenReplayAsync(Guid runId, CancellationToken cancellationToken = default) =>
        await replay.OpenAsync(runId, cancellationToken).ConfigureAwait(false);

    public async Task<StudioResult<StudioRun>> RestartReplayAsync(Guid runId, string nodeId, string overridesJson,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nodeId)) return StudioResult<StudioRun>.Failure("ReplayPointUnavailable", "A replay node ID is required.", runId.ToString());
        if (!TryParseJson(overridesJson, out var overrides)) return StudioResult<StudioRun>.Failure("InvalidDefinition", "Replay overrides must be valid JSON.", runId.ToString());
        return await replay.RestartFromAsync(runId, nodeId, overrides!, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StudioResult<EvaluationRun>> RunEvaluationAsync(Guid suiteResourceId, int repetitions = 1,
        CancellationToken cancellationToken = default)
    {
        if (repetitions is < 1 or > 20) return StudioResult<EvaluationRun>.Failure("InvalidRepetitionCount", "Repetitions must be between 1 and 20.", suiteResourceId.ToString());
        var resource = await OpenResourceAsync(suiteResourceId, StudioResourceKind.EvaluationSuite, cancellationToken).ConfigureAwait(false);
        if (!resource.IsSuccess) return Failure<EvaluationRun>(resource.Error!);
        var suite = JsonSerializer.Deserialize<EvaluationSuite>(resource.Value!.DefinitionJson, StudioJson.Options);
        if (suite is null || suite.Cases.Count == 0) return StudioResult<EvaluationRun>.Failure("InvalidDefinition", "An Evaluation Suite requires at least one case.", suiteResourceId.ToString());
        var start = DateTimeOffset.UtcNow;
        var caseResults = new List<EvaluationCaseResult>();
        var dependencies = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var repetition = 1; repetition <= repetitions; repetition++)
        foreach (var testCase in suite.Cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var run = await RunTargetAsync(suite.TargetKind, suite.TargetId, suite.TargetRevision, testCase.Input,
                testCase.ContextJson, suite.RunProfileId, cancellationToken).ConfigureAwait(false);
            if (!run.IsSuccess)
            {
                caseResults.Add(new EvaluationCaseResult(testCase.CaseId, repetition, "Failed", [], null, null, run.Error!.Code, null));
                continue;
            }
            dependencies[run.Value!.EffectiveModel] = run.Value.DefinitionVersion?.ToString() ?? suite.TargetRevision.ToString();
            var facts = GradeCase(testCase, suite.Graders, run.Value);
            var deterministicPass = facts.All(item => item.StartsWith("PASS", StringComparison.Ordinal));
            var deterministicFailure = facts.Any(item => item.StartsWith("FAIL", StringComparison.Ordinal));
            var hasSubjectiveGrader = suite.Graders.Any(item => !item.IsDeterministic);
            var pendingJudgement = hasSubjectiveGrader ? "Model/human/custom grading requires a registered grader and is not inferred." : null;
            var status = deterministicFailure ? "Failed" : !deterministicPass || hasSubjectiveGrader ? "NeedsReview" : "Passed";
            caseResults.Add(new EvaluationCaseResult(testCase.CaseId, repetition, status, facts, pendingJudgement,
                suite.Graders.Any(item => item.IsDeterministic) && deterministicPass ? 1d : deterministicFailure ? 0d : null,
                null, run.Value.RunId));
        }
        string? baselineComparison = null;
        if (suite.BaselineRunId is { } baselineId)
        {
            var prior = await resources.ListEvaluationRunsAsync(suite.EvalSuiteId, cancellationToken).ConfigureAwait(false);
            var baseline = prior.FirstOrDefault(item => item.RunId == baselineId);
            baselineComparison = baseline is null
                ? JsonSerializer.Serialize(new { baselineId, status = "BaselineNotFound" }, StudioJson.Options)
                : JsonSerializer.Serialize(new { baselineId, baseline.TargetRevision, currentRevision = suite.TargetRevision,
                    changes = caseResults.Select(current => new { current.CaseId, current.Status,
                        previousStatus = baseline.Results.FirstOrDefault(old => old.CaseId == current.CaseId)?.Status }).ToArray() }, StudioJson.Options);
        }
        var record = new EvaluationRun(Guid.NewGuid(), suite.EvalSuiteId, suite.TargetRevision, suite.RunProfileId,
            start, DateTimeOffset.UtcNow, caseResults, dependencies, baselineComparison);
        try { await resources.WriteEvaluationRunAsync(record, cancellationToken).ConfigureAwait(false); }
        catch (IOException ex) { return StudioResult<EvaluationRun>.Failure("PersistenceUnavailable", ex.Message, suiteResourceId.ToString(), retryable: true); }
        return StudioResult<EvaluationRun>.Success(record);
    }

    public async Task<StudioResult<TestRun>> RunTestSuiteAsync(Guid suiteResourceId,
        CancellationToken cancellationToken = default)
    {
        var resource = await OpenResourceAsync(suiteResourceId, StudioResourceKind.TestSuite, cancellationToken).ConfigureAwait(false);
        if (!resource.IsSuccess) return Failure<TestRun>(resource.Error!);
        var suite = JsonSerializer.Deserialize<TestSuite>(resource.Value!.DefinitionJson, StudioJson.Options);
        if (suite is null || suite.Cases.Count == 0) return StudioResult<TestRun>.Failure("InvalidDefinition", "A Test Suite requires at least one case.", suiteResourceId.ToString());
        var started = DateTimeOffset.UtcNow;
        var results = new List<StudioTestCaseResult>();
        foreach (var testCase in suite.Cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var run = await RunTargetAsync(suite.TargetKind, suite.TargetId, testCase.TargetRevision, testCase.Input,
                testCase.ContextJson, suite.RunProfileId, cancellationToken).ConfigureAwait(false);
            if (!run.IsSuccess)
            {
                results.Add(new StudioTestCaseResult(testCase.CaseId, "Failed", [], null, run.Error!.Code,
                    JsonSerializer.Serialize(new { testCase, run.Error.Code, run.Error.Message }, StudioJson.Options)));
                continue;
            }
            var assertions = testCase.Assertions.Select(assertion => EvaluateAssertion(assertion, run.Value!)).ToArray();
            var status = assertions.All(assertion => assertion.Passed) ? "Passed" : "Failed";
            results.Add(new StudioTestCaseResult(testCase.CaseId, status, assertions, run.Value!.RunId, null,
                status == "Failed" ? JsonSerializer.Serialize(new { testCase, run.Value.EffectiveModel, run.Value.RunProfileId, run.Value.ActionGraphJson }, StudioJson.Options) : null));
        }
        var record = new TestRun(Guid.NewGuid(), suite.TestSuiteId, suite.TargetRevision, suite.RunProfileId,
            started, DateTimeOffset.UtcNow, results);
        try { await resources.WriteTestRunAsync(record, cancellationToken).ConfigureAwait(false); }
        catch (IOException ex) { return StudioResult<TestRun>.Failure("PersistenceUnavailable", ex.Message, suiteResourceId.ToString(), retryable: true); }
        return StudioResult<TestRun>.Success(record);
    }

    public async Task<StudioResult<IReadOnlyList<EvaluationRun>>> ListEvaluationRunsAsync(Guid suiteId, CancellationToken cancellationToken = default)
    {
        try { return StudioResult<IReadOnlyList<EvaluationRun>>.Success(await resources.ListEvaluationRunsAsync(suiteId, cancellationToken).ConfigureAwait(false)); }
        catch (IOException ex) { return StudioResult<IReadOnlyList<EvaluationRun>>.Failure("PersistenceUnavailable", ex.Message, suiteId.ToString(), retryable: true); }
    }

    public async Task<StudioResult<IReadOnlyList<TestRun>>> ListTestRunsAsync(Guid suiteId, CancellationToken cancellationToken = default)
    {
        try { return StudioResult<IReadOnlyList<TestRun>>.Success(await resources.ListTestRunsAsync(suiteId, cancellationToken).ConfigureAwait(false)); }
        catch (IOException ex) { return StudioResult<IReadOnlyList<TestRun>>.Failure("PersistenceUnavailable", ex.Message, suiteId.ToString(), retryable: true); }
    }

    public async Task<StudioResult<ComparisonResult>> CompareAsync(StudioRun left, StudioRun right,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var changed = new List<string>();
        if (!left.Output.Equals(right.Output, StringComparison.Ordinal)) changed.Add("output");
        if (!left.EffectiveModel.Equals(right.EffectiveModel, StringComparison.Ordinal)) changed.Add("effectiveModel");
        if (!left.EffectiveProvider.Equals(right.EffectiveProvider, StringComparison.Ordinal)) changed.Add("effectiveProvider");
        if (!left.PermissionsJson.Equals(right.PermissionsJson, StringComparison.Ordinal)) changed.Add("permissions");
        if (!left.ActionGraphJson.Equals(right.ActionGraphJson, StringComparison.Ordinal)) changed.Add("actionGraph");
        return StudioResult<ComparisonResult>.Success(new ComparisonResult(left.RunId.ToString(), right.RunId.ToString(),
            left.Output.Equals(right.Output, StringComparison.Ordinal), changed, left.Output, right.Output));
    }

    public async Task<StudioResult<DocumentationArtifact>> GenerateDocumentationAsync(Guid projectId, string format = "markdown",
        CancellationToken cancellationToken = default)
    {
        if (!format.Equals("markdown", StringComparison.OrdinalIgnoreCase)) return StudioResult<DocumentationArtifact>.Failure("UnsupportedFormat", "Markdown documentation is the currently supported format.", projectId.ToString());
        var project = await OpenProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (!project.IsSuccess) return Failure<DocumentationArtifact>(project.Error!);
        var value = project.Value!;
        using var definition = JsonDocument.Parse(value.DefinitionJson);
        var content = $"# {value.Name}\n\n- Project ID: `{value.ProjectId}`\n- Type: {value.ProjectType}\n- Version: {value.Version}\n- Owner/scope: {value.OwnerScope}\n- Template: `{value.TemplateId}` v{value.TemplateVersion}\n- Updated: {value.ModifiedAt:O}\n\n## Dependencies\n" +
            (value.Dependencies.Count == 0 ? "None declared.\n" : string.Join("\n", value.Dependencies.Select(item => $"- {item.Kind}: `{item.StableId}` ({item.VersionPolicy})")) + "\n") +
            "\n## Permissions\n" + (value.PermissionMetadata.Count == 0 ? "None declared.\n" : string.Join("\n", value.PermissionMetadata.Select(item => $"- {item.Key}: {item.Value}")) + "\n") +
            "\n## Definition\n\n```json\n" + JsonSerializer.Serialize(definition.RootElement, new JsonSerializerOptions(StudioJson.Options) { WriteIndented = true }) + "\n```\n";
        return StudioResult<DocumentationArtifact>.Success(new DocumentationArtifact(value.ProjectId.ToString(), format,
            content, value.Version.ToString(), DateTimeOffset.UtcNow));
    }

    private async Task<StudioResult<StudioRun>> RunTargetAsync(string targetKind, string targetId, int revision,
        string input, string contextJson, string? runProfileId, CancellationToken cancellationToken)
    {
        var context = TryParseJson(contextJson, out _) ? JsonSerializer.Deserialize<Dictionary<string, string>>(contextJson, StudioJson.Options) ?? new() : new();
        if (targetKind.Equals("Harness", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(targetId, out var projectId))
        {
            var project = await OpenProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
            if (!project.IsSuccess) return Failure<StudioRun>(project.Error!);
            if (project.Value!.Version != revision) return StudioResult<StudioRun>.Failure("RevisionConflict", "The test targets a different Harness revision.", targetId, recoverable: true);
            return await runtime.RunHarnessAsync(project.Value, input, runProfileId, cancellationToken).ConfigureAwait(false);
        }
        if (targetKind.Equals("Agent", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(targetId, out var agentId))
            return await agents.PreviewAsync(agentId, input, new AgentPreviewOptions(runProfileId, true,
                new Dictionary<string, bool>()), cancellationToken).ConfigureAwait(false);
        return StudioResult<StudioRun>.Failure("EvaluationTargetUnavailable", $"Target type '{targetKind}' is not runnable through registered canonical adapters.", targetId, retryable: true);
    }

    private static IReadOnlyList<string> GradeCase(EvaluationCase testCase, IReadOnlyList<EvaluationGrader> graders, StudioRun run)
    {
        var facts = new List<string>();
        if (testCase.ExpectedOutputJson is not null && !graders.Any(item => item.Type == EvaluationGraderType.ExactOutput))
        {
            var expected = NormalizeJson(testCase.ExpectedOutputJson);
            facts.Add(expected is not null && expected == NormalizeJson(run.Output) ? "PASS exact output matches" : "FAIL exact output differs");
        }
        foreach (var grader in graders.Where(item => item.IsDeterministic))
        {
            try
            {
                using var configuration = JsonDocument.Parse(grader.ConfigurationJson);
                var config = configuration.RootElement;
                switch (grader.Type)
                {
                    case EvaluationGraderType.ExactOutput:
                        var expectedOutput = testCase.ExpectedOutputJson ?? (config.TryGetProperty("expectedOutputJson", out var expectedElement) ? expectedElement.ToString() : null);
                        facts.Add(expectedOutput is not null && NormalizeJson(expectedOutput) == NormalizeJson(run.Output) ? "PASS exact output matches" : "FAIL exact output differs or is unspecified");
                        break;
                    case EvaluationGraderType.OutputSchema:
                        var schemaJson = testCase.ExpectedOutputSchemaJson ?? (config.TryGetProperty("schemaJson", out var schemaElement) ? schemaElement.ToString() : null);
                        facts.Add(schemaJson is not null && IsValidJsonSchema(run.Output, schemaJson) ? "PASS output matches the declared JSON schema" : "FAIL output does not satisfy the declared JSON schema");
                        break;
                    case EvaluationGraderType.RequiredActions:
                        var required = testCase.RequiredActions.Count > 0 ? testCase.RequiredActions : ReadStringArray(config, "requiredActions");
                        if (required.Count == 0) facts.Add("FAIL RequiredActions grader has no required actions");
                        facts.AddRange(required.Select(action => ContainsAction(run.ActivityJson, action) ? $"PASS required action {action}" : $"FAIL required action {action} missing"));
                        break;
                    case EvaluationGraderType.ForbiddenActions:
                        var forbidden = testCase.ForbiddenActions.Count > 0 ? testCase.ForbiddenActions : ReadStringArray(config, "forbiddenActions");
                        if (forbidden.Count == 0) facts.Add("FAIL ForbiddenActions grader has no forbidden actions");
                        facts.AddRange(forbidden.Select(action => !ContainsAction(run.ActivityJson, action) ? $"PASS forbidden action {action} absent" : $"FAIL forbidden action {action} occurred"));
                        break;
                    case EvaluationGraderType.CostLimit:
                        var maxCost = testCase.MaximumCost ?? (config.TryGetProperty("maximumCost", out var cost) && cost.TryGetDecimal(out var costValue) ? costValue : null);
                        facts.Add(maxCost is not null && run.CostAmount is { } amount ? (amount <= maxCost ? "PASS cost limit" : "FAIL cost limit exceeded") : "FAIL cost or maximum cost is unknown");
                        break;
                    case EvaluationGraderType.LatencyLimit:
                        var maxLatency = testCase.MaximumLatency ?? (config.TryGetProperty("maximumLatencyMilliseconds", out var latency) && latency.TryGetDouble(out var milliseconds) ? TimeSpan.FromMilliseconds(milliseconds) : null);
                        facts.Add(maxLatency is not null && run.Duration is { } duration ? (duration <= maxLatency ? "PASS latency limit" : "FAIL latency limit exceeded") : "FAIL latency or maximum latency is unknown");
                        break;
                }
            }
            catch (JsonException) { facts.Add($"FAIL grader {grader.Type} configuration is invalid JSON"); }
        }
        if (testCase.RequiredActions.Count > 0 && !graders.Any(item => item.Type == EvaluationGraderType.RequiredActions))
            facts.AddRange(testCase.RequiredActions.Select(action => ContainsAction(run.ActivityJson, action) ? $"PASS required action {action}" : $"FAIL required action {action} missing"));
        if (testCase.ForbiddenActions.Count > 0 && !graders.Any(item => item.Type == EvaluationGraderType.ForbiddenActions))
            facts.AddRange(testCase.ForbiddenActions.Select(action => !ContainsAction(run.ActivityJson, action) ? $"PASS forbidden action {action} absent" : $"FAIL forbidden action {action} occurred"));
        if (testCase.MaximumLatency is { } maximumLatency && !graders.Any(item => item.Type == EvaluationGraderType.LatencyLimit))
            facts.Add(run.Duration is { } duration ? (duration <= maximumLatency ? "PASS latency limit" : "FAIL latency limit exceeded") : "FAIL latency is unknown");
        if (testCase.MaximumCost is { } maximumCost && !graders.Any(item => item.Type == EvaluationGraderType.CostLimit))
            facts.Add(run.CostAmount is { } cost ? (cost <= maximumCost ? "PASS cost limit" : "FAIL cost limit exceeded") : "FAIL cost is unknown");
        if (testCase.ExpectedOutputSchemaJson is not null && !graders.Any(item => item.Type == EvaluationGraderType.OutputSchema))
            facts.Add(IsValidJsonSchema(run.Output, testCase.ExpectedOutputSchemaJson) ? "PASS output matches the declared JSON schema" : "FAIL output does not satisfy the declared JSON schema");
        if (facts.Count == 0 && !graders.Any(item => !item.IsDeterministic)) facts.Add("FAIL no deterministic grading assertion configured");
        if (graders.Any(item => !item.IsDeterministic)) facts.Add("UNRESOLVED model, human or custom grading is pending its authorized grader");
        return facts;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray()
            : [];

    private static bool IsValidJsonSchema(string instanceJson, string schemaJson)
    {
        try
        {
            using var instance = JsonDocument.Parse(instanceJson);
            using var schema = JsonDocument.Parse(schemaJson);
            return ValidateSchemaValue(instance.RootElement, schema.RootElement);
        }
        catch (JsonException) { return false; }
    }

    private static bool ValidateSchemaValue(JsonElement value, JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String) return false;
        var matches = typeElement.GetString() switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => false
        };
        if (!matches || value.ValueKind != JsonValueKind.Object) return matches;
        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array &&
            required.EnumerateArray().Any(name => name.ValueKind != JsonValueKind.String || !value.TryGetProperty(name.GetString()!, out _))) return false;
        if (!schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object) return true;
        foreach (var property in properties.EnumerateObject())
            if (value.TryGetProperty(property.Name, out var actual) && !ValidateSchemaValue(actual, property.Value)) return false;
        return true;
    }

    private static TestAssertionResult EvaluateAssertion(TestAssertion assertion, StudioRun run)
    {
        var passed = assertion.Type switch
        {
            "outputEquals" => NormalizeJson(run.Output) == NormalizeJson(assertion.ExpectedJson),
            "outputContains" => run.Output.Contains(assertion.ExpectedJson.Trim('"'), StringComparison.Ordinal),
            "actionCalled" => ContainsAction(run.ActivityJson, assertion.ExpectedJson.Trim('"')),
            "actionNotCalled" => !ContainsAction(run.ActivityJson, assertion.ExpectedJson.Trim('"')),
            "outputSchemaValid" => IsValidJsonSchema(run.Output, assertion.ExpectedJson),
            "runStatus" => run.Status.Equals(assertion.ExpectedJson.Trim('"'), StringComparison.OrdinalIgnoreCase),
            "structuredErrorCode" => string.Equals(run.StructuredErrorCode, assertion.ExpectedJson.Trim('"'), StringComparison.Ordinal),
            "permissionOutcome" => run.PermissionsJson.Contains(assertion.ExpectedJson.Trim('"'), StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        var actual = assertion.Type.StartsWith("action", StringComparison.Ordinal) ? run.ActivityJson :
            assertion.Type == "permissionOutcome" ? run.PermissionsJson : JsonSerializer.Serialize(run.Output);
        return new TestAssertionResult(assertion.AssertionId, passed,
            passed ? "Assertion passed." : $"Assertion '{assertion.Type}' failed or is unsupported.", actual);
    }

    private static StudioResult<StudioResource> ValidateResource(StudioResource resource)
    {
        try
        {
            using var document = JsonDocument.Parse(resource.DefinitionJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return StudioResult<StudioResource>.Failure("InvalidDefinition", "Resource definition must be a JSON object.", resource.ResourceId.ToString());
            if (resource.Kind == StudioResourceKind.EvaluationSuite)
            {
                var suite = JsonSerializer.Deserialize<EvaluationSuite>(resource.DefinitionJson, StudioJson.Options);
                if (suite is null || suite.EvalSuiteId == Guid.Empty || string.IsNullOrWhiteSpace(suite.TargetId) || suite.Cases.Count == 0)
                    return StudioResult<StudioResource>.Failure("InvalidDefinition", "Evaluation Suite requires a stable identity, target and at least one case.", resource.ResourceId.ToString());
                foreach (var testCase in suite.Cases)
                    if (string.IsNullOrWhiteSpace(testCase.SourceProvenance) || testCase.TargetRevision != suite.TargetRevision)
                        return StudioResult<StudioResource>.Failure("InvalidDefinition", "Evaluation cases require source provenance and the exact suite target revision.", testCase.CaseId.ToString());
            }
            else if (resource.Kind == StudioResourceKind.TestSuite)
            {
                var suite = JsonSerializer.Deserialize<TestSuite>(resource.DefinitionJson, StudioJson.Options);
                if (suite is null || suite.TestSuiteId == Guid.Empty || string.IsNullOrWhiteSpace(suite.TargetId) || suite.Cases.Count == 0)
                    return StudioResult<StudioResource>.Failure("InvalidDefinition", "Test Suite requires a stable identity, target and at least one case.", resource.ResourceId.ToString());
                foreach (var testCase in suite.Cases)
                    if (string.IsNullOrWhiteSpace(testCase.SourceProvenance) || testCase.Assertions.Count == 0)
                        return StudioResult<StudioResource>.Failure("InvalidDefinition", "Test cases require source provenance and at least one deterministic assertion.", testCase.CaseId.ToString());
            }
            else if (resource.Kind == StudioResourceKind.Schema && !root.TryGetProperty("SchemaID", out _) && !root.TryGetProperty("schemaId", out _))
                return StudioResult<StudioResource>.Failure("InvalidDefinition", "Reusable schemas require a stable SchemaID.", resource.ResourceId.ToString());
            else if (resource.Kind == StudioResourceKind.ModelRouter && !root.TryGetProperty("routes", out _) && !root.TryGetProperty("Rules", out _))
                return StudioResult<StudioResource>.Failure("RouterInvalid", "Model Router requires route rules.", resource.ResourceId.ToString());
            else if (resource.Kind == StudioResourceKind.GenerativeUi && (!root.TryGetProperty("components", out _) || !root.TryGetProperty("actions", out _)))
                return StudioResult<StudioResource>.Failure("InvalidDefinition", "Generative UI definition requires an explicit component and action allow-list.", resource.ResourceId.ToString());
            else if (resource.Kind == StudioResourceKind.RunProfile && !root.TryGetProperty("revision", out _))
                return StudioResult<StudioResource>.Failure("InvalidDefinition", "Run Profile requires a revision.", resource.ResourceId.ToString());
            return StudioResult<StudioResource>.Success(resource);
        }
        catch (JsonException ex) { return StudioResult<StudioResource>.Failure("InvalidDefinition", ex.Message, resource.ResourceId.ToString()); }
    }

    private static string? NormalizeJson(string json)
    {
        try { using var document = JsonDocument.Parse(json); return JsonSerializer.Serialize(document.RootElement); }
        catch (JsonException) { return null; }
    }

    private static bool ContainsAction(string activityJson, string action)
    {
        try { using var activity = JsonDocument.Parse(activityJson); return activity.RootElement.ToString().Contains(action, StringComparison.OrdinalIgnoreCase); }
        catch (JsonException) { return activityJson.Contains(action, StringComparison.OrdinalIgnoreCase); }
    }

    public Task<StudioResult<CanonicalAgentReference>> CreateAgentAsync(CanonicalAgentDefinition definition,
        CancellationToken cancellationToken = default) => agents.CreateAsync(definition, cancellationToken);
    public Task<StudioResult<CanonicalAgentDefinition>> OpenAgentAsync(Guid id, CancellationToken cancellationToken = default) => agents.OpenAsync(id, cancellationToken);
    public Task<StudioResult<CanonicalAgentDefinition>> UpdateAgentDraftAsync(Guid id, long expectedRevision,
        CanonicalAgentDefinition definition, CancellationToken cancellationToken = default) => agents.UpdateDraftAsync(id, expectedRevision, definition, cancellationToken);
    public Task<StudioResult<ValidationReport>> ValidateAgentAsync(Guid id, long? revision = null, CancellationToken cancellationToken = default) => agents.ValidateAsync(id, revision, cancellationToken);
    public Task<StudioResult<StudioRun>> PreviewAgentAsync(Guid id, string input, AgentPreviewOptions options,
        CancellationToken cancellationToken = default) => agents.PreviewAsync(id, input, options with { TestScoped = true }, cancellationToken);
    public Task<StudioResult<CanonicalAgentReference>> ActivateAgentAsync(Guid id, long revision,
        CancellationToken cancellationToken = default) => agents.ActivateAsync(id, revision, cancellationToken);
    public Task<StudioResult<Unit>> ShareAgentAsync(Guid id, string principalId, string role,
        CancellationToken cancellationToken = default) => agents.ShareAsync(id, principalId, role, cancellationToken);

    public StudioResult<StudioProject> ExportProject(Guid projectId, string format)
    {
        if (!format.Equals("json", StringComparison.OrdinalIgnoreCase))
            return StudioResult<StudioProject>.Failure("UnsupportedFormat", "Only the lossless JSON project bundle is supported.");
        return StudioResult<StudioProject>.Failure("AsyncReadRequired", "Use ExportProjectAsync to read the canonical project store.");
    }

    public async Task<StudioResult<byte[]>> ExportProjectAsync(Guid projectId, string format = "json", CancellationToken cancellationToken = default)
    {
        if (!format.Equals("json", StringComparison.OrdinalIgnoreCase)) return StudioResult<byte[]>.Failure("UnsupportedFormat", "Only the lossless JSON project bundle is supported.");
        var project = await OpenProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        return project.IsSuccess
            ? StudioResult<byte[]>.Success(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(project.Value, StudioJson.Options)))
            : Failure<byte[]>(project.Error!);
    }

    public async Task<StudioResult<StudioProject>> ImportProjectAsync(ReadOnlyMemory<byte> content, string conflictPolicy = "new-id",
        CancellationToken cancellationToken = default)
    {
        if (conflictPolicy != "new-id") return StudioResult<StudioProject>.Failure("UnsupportedConflictPolicy", "Only new-id conflict handling is supported.");
        StudioProject? imported;
        try { imported = JsonSerializer.Deserialize<StudioProject>(content.Span, StudioJson.Options); }
        catch (JsonException) { return StudioResult<StudioProject>.Failure("InvalidDefinition", "Import is not a valid AI Studio project bundle."); }
        if (imported is null || imported.ProjectId == Guid.Empty || string.IsNullOrWhiteSpace(imported.Name))
            return StudioResult<StudioProject>.Failure("InvalidDefinition", "Import is missing project identity or name.");
        var definition = imported with { ProjectId = Guid.NewGuid(), Version = 1, CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow, Revisions = [], DeletedAt = null };
        var write = await SafeWriteAsync(definition, cancellationToken).ConfigureAwait(false);
        return write.IsSuccess ? StudioResult<StudioProject>.Success(definition) : Failure<StudioProject>(write.Error!);
    }

    private async Task<StudioResult<StudioProject>> MutateAsync(Guid id, int expectedVersion,
        Func<StudioProject, StudioProject> mutation, CancellationToken cancellationToken)
    {
        var current = await GetForMutationAsync(id, cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess) return Failure<StudioProject>(current.Error!);
        if (current.Value!.Version != expectedVersion) return StudioResult<StudioProject>.Failure("RevisionConflict", "Project version changed.", id.ToString(), recoverable: true);
        var now = DateTimeOffset.UtcNow;
        var updated = mutation(current.Value) with { Version = current.Value.Version + 1, ModifiedAt = now,
            Revisions = current.Value.Revisions.Append(new ProjectRevision(current.Value.Version, now, current.Value.DefinitionJson)).ToArray() };
        var write = await SafeWriteAsync(updated, cancellationToken).ConfigureAwait(false);
        return write.IsSuccess ? StudioResult<StudioProject>.Success(updated) : Failure<StudioProject>(write.Error!);
    }

    private async Task<StudioResult<StudioProject>> MutateDeletedAsync(Guid id, Func<StudioProject, StudioProject> mutation,
        CancellationToken cancellationToken)
    {
        var result = await SafeGetAsync(id, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess) return Failure<StudioProject>(result.Error!);
        if (result.Value is null || !result.Value.IsDeleted) return StudioResult<StudioProject>.Failure("ProjectNotFound", "Project is not in the recovery area.", id.ToString());
        var updated = mutation(result.Value);
        var write = await SafeWriteAsync(updated, cancellationToken).ConfigureAwait(false);
        return write.IsSuccess ? StudioResult<StudioProject>.Success(updated) : Failure<StudioProject>(write.Error!);
    }

    private async Task<StudioResult<StudioProject>> GetForMutationAsync(Guid id, CancellationToken cancellationToken)
    {
        var read = await SafeGetAsync(id, cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess) return Failure<StudioProject>(read.Error!);
        return read.Value is null || read.Value.IsDeleted
            ? StudioResult<StudioProject>.Failure("ProjectNotFound", "Project not found or deleted.", id.ToString())
            : StudioResult<StudioProject>.Success(read.Value);
    }

    private async Task<StudioResult<IReadOnlyList<StudioProject>>> SafeReadAsync(bool includeDeleted, CancellationToken cancellationToken)
    {
        try { return StudioResult<IReadOnlyList<StudioProject>>.Success(await store.ListAsync(includeDeleted, cancellationToken).ConfigureAwait(false)); }
        catch (OperationCanceledException) { return StudioResult<IReadOnlyList<StudioProject>>.Failure("Cancelled", "The request was cancelled.", retryable: true); }
        catch (InvalidDataException ex) { return StudioResult<IReadOnlyList<StudioProject>>.Failure("StoreSchemaUnsupported", ex.Message, recoverable: true); }
        catch (IOException ex) { return StudioResult<IReadOnlyList<StudioProject>>.Failure("PersistenceUnavailable", ex.Message, retryable: true); }
    }

    private async Task<StudioResult<StudioProject?>> SafeGetAsync(Guid id, CancellationToken cancellationToken)
    {
        try { return StudioResult<StudioProject?>.Success(await store.GetAsync(id, cancellationToken).ConfigureAwait(false)); }
        catch (OperationCanceledException) { return StudioResult<StudioProject?>.Failure("Cancelled", "The request was cancelled.", id.ToString(), retryable: true); }
        catch (InvalidDataException ex) { return StudioResult<StudioProject?>.Failure("StoreSchemaUnsupported", ex.Message, id.ToString(), recoverable: true); }
        catch (IOException ex) { return StudioResult<StudioProject?>.Failure("PersistenceUnavailable", ex.Message, id.ToString(), retryable: true); }
    }

    private async Task<StudioResult<Unit>> SafeWriteAsync(StudioProject project, CancellationToken cancellationToken)
    {
        try { await store.WriteAsync(project, cancellationToken).ConfigureAwait(false); return StudioResult<Unit>.Success(new Unit()); }
        catch (OperationCanceledException) { return StudioResult<Unit>.Failure("Cancelled", "The request was cancelled.", project.ProjectId.ToString(), retryable: true); }
        catch (InvalidDataException ex) { return StudioResult<Unit>.Failure("StoreSchemaUnsupported", ex.Message, project.ProjectId.ToString(), recoverable: true); }
        catch (IOException ex) { return StudioResult<Unit>.Failure("PersistenceUnavailable", ex.Message, project.ProjectId.ToString(), retryable: true); }
    }

    private static StudioResult<T> Failure<T>(StudioError error) => new(default, error);
    private static bool TryParseJson(string? json, out string? normalized)
    {
        normalized = null;
        try { using var document = JsonDocument.Parse(json ?? string.Empty); normalized = JsonSerializer.Serialize(document.RootElement, StudioJson.Options); return true; }
        catch (JsonException) { return false; }
    }
    private static bool TryDecodeCursor(string? cursor, out int offset)
    {
        offset = 0;
        if (string.IsNullOrEmpty(cursor)) return true;
        try { return int.TryParse(Encoding.UTF8.GetString(Convert.FromBase64String(cursor)), out offset) && offset >= 0; }
        catch (FormatException) { return false; }
    }
    private static string EncodeCursor(int offset) => Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.ToString(System.Globalization.CultureInfo.InvariantCulture)));
}

public sealed class UnavailableStudioRuntimeAdapter : IStudioRuntimeAdapter
{
    public Task<StudioResult<StudioRun>> RunHarnessAsync(StudioProject project, string input, string? runProfileId, CancellationToken cancellationToken) =>
        Task.FromResult(StudioResult<StudioRun>.Failure("RuntimeUnavailable", AIStudioApi.RuntimeUnavailableMessage, project.ProjectId.ToString(), retryable: true));
    public Task<StudioResult<StudioRun>> RunPlaygroundAsync(PlaygroundConfiguration configuration, string input, CancellationToken cancellationToken) =>
        Task.FromResult(StudioResult<StudioRun>.Failure("RuntimeUnavailable", AIStudioApi.RuntimeUnavailableMessage, retryable: true));
    public Task<StudioResult<InstallReceipt>> InstallToolAsync(StudioProject project, CancellationToken cancellationToken) =>
        Task.FromResult(StudioResult<InstallReceipt>.Failure("CapabilityUnavailable", "The canonical Dulche Skill/Plugin/MCP installer is not registered. Nothing was installed.", project.ProjectId.ToString(), retryable: true));
}

public sealed class UnavailableCanonicalAgentBuilderAdapter : ICanonicalAgentBuilderAdapter
{
    private static StudioError Error(string action) => new("CapabilityUnavailable",
        $"The canonical Den/Dulche Agent {action} API is not registered. No local duplicate Agent was created or changed.", Retryable: true);
    public Task<StudioResult<CanonicalAgentReference>> CreateAsync(CanonicalAgentDefinition definition, CancellationToken cancellationToken) => Task.FromResult(new StudioResult<CanonicalAgentReference>(default, Error("create")));
    public Task<StudioResult<CanonicalAgentDefinition>> OpenAsync(Guid agentId, CancellationToken cancellationToken) => Task.FromResult(new StudioResult<CanonicalAgentDefinition>(default, Error("read")));
    public Task<StudioResult<CanonicalAgentDefinition>> UpdateDraftAsync(Guid agentId, long expectedRevision, CanonicalAgentDefinition definition, CancellationToken cancellationToken) => Task.FromResult(new StudioResult<CanonicalAgentDefinition>(default, Error("draft update")));
    public Task<StudioResult<ValidationReport>> ValidateAsync(Guid agentId, long? revision, CancellationToken cancellationToken) => Task.FromResult(new StudioResult<ValidationReport>(default, Error("validation")));
    public Task<StudioResult<StudioRun>> PreviewAsync(Guid agentId, string input, AgentPreviewOptions options, CancellationToken cancellationToken) => Task.FromResult(new StudioResult<StudioRun>(default, Error("preview")));
    public Task<StudioResult<CanonicalAgentReference>> ActivateAsync(Guid agentId, long revision, CancellationToken cancellationToken) => Task.FromResult(new StudioResult<CanonicalAgentReference>(default, Error("activation")));
    public Task<StudioResult<Unit>> ShareAsync(Guid agentId, string principalId, string role, CancellationToken cancellationToken) => Task.FromResult(new StudioResult<Unit>(default, Error("sharing")));
}

public sealed class UnavailableStudioPermissionSimulationAdapter : IStudioPermissionSimulationAdapter
{
    public Task<StudioResult<PermissionSimulation>> EvaluateAsync(string requestContextJson, CancellationToken cancellationToken) =>
        Task.FromResult(StudioResult<PermissionSimulation>.Failure("CapabilityUnavailable",
            "Home permission resolution is not registered. No capabilities were granted or executed.", retryable: true));
}

public sealed class UnavailableStudioContextInspectorAdapter : IStudioContextInspectorAdapter
{
    public Task<StudioResult<ContextReport>> InspectAsync(Guid runOrRequestId, CancellationToken cancellationToken) =>
        Task.FromResult(StudioResult<ContextReport>.Failure("ContextUnavailable",
            "Dulche runtime context provenance is not registered. No context is inferred.", runOrRequestId.ToString(), retryable: true));
}

public sealed class UnavailableStudioReplayAdapter : IStudioReplayAdapter
{
    public Task<StudioResult<StudioRun>> OpenAsync(Guid runId, CancellationToken cancellationToken) =>
        Task.FromResult(StudioResult<StudioRun>.Failure("RunNotFound", "No canonical Dulche run history is registered.", runId.ToString(), retryable: true));
    public Task<StudioResult<StudioRun>> RestartFromAsync(Guid runId, string nodeId, string overridesJson, CancellationToken cancellationToken) =>
        Task.FromResult(StudioResult<StudioRun>.Failure("ReplayPointUnavailable",
            "Dulche restart/fork semantics are not registered. The original run was not changed.", runId.ToString(), retryable: true));
}
