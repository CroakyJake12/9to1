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

public sealed record Unit;
public sealed record CanonicalAgentReference(Guid AgentId, long DefinitionRevision, string Owner);
public sealed record CanonicalAgentDefinition(Guid AgentId, string Name, string Description, string Instructions,
    string ConfigurationJson, long DefinitionRevision, bool IsDraft);
public sealed record AgentPreviewOptions(string? RunProfileId, bool TestScoped, IReadOnlyDictionary<string, bool> PermissionSimulation);
public sealed record StudioRun(Guid RunId, string TargetId, string Status, string Output,
    string EffectiveModel, string EffectiveProvider, string EffectiveContextJson, string ActivityJson,
    string ActionGraphJson, string PermissionsJson, long? InputTokens, long? OutputTokens,
    TimeSpan? Duration, string? StructuredErrorCode, string? StructuredErrorMessage,
    int? DefinitionVersion, string? RunProfileId);
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
    ICanonicalAgentBuilderAdapter agents)
{
    internal const string RuntimeUnavailableMessage = "The canonical Dulche runtime adapter is not registered. This action was not simulated or executed.";

    public async Task<StudioResult<ProjectPage>> ListProjectsAsync(int pageSize = 50, string? cursor = null,
        bool includeDeleted = false, string? search = null, StudioProjectType? type = null,
        CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > 200) return StudioResult<ProjectPage>.Failure("InvalidPageSize", "Page size must be between 1 and 200.");
        if (!TryDecodeCursor(cursor, out var offset)) return StudioResult<ProjectPage>.Failure("InvalidCursor", "The project cursor is invalid.");
        var all = await SafeReadAsync(includeDeleted, cancellationToken).ConfigureAwait(false);
        if (!all.IsSuccess) return StudioResult<ProjectPage>.Failure(all.Error!.Code, all.Error.Message, all.Error.TargetId, all.Error.Retryable, all.Error.Recoverable);
        var filtered = all.Value!
            .Where(item => type is null || item.ProjectType == type)
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
            new Dictionary<string, string>(), [], null);
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
                if (!TryParseJson(tool.ManifestJson, out var manifest)) issues.Add(new("InvalidDefinition", "Tool manifest must be valid JSON.", "$.manifestJson", "error"));
                else
                {
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
        $"The canonical Den/Dulche Agent {action} API is not registered. No local duplicate Agent was created or changed.", retryable: true);
    public Task<StudioResult<CanonicalAgentReference>> CreateAsync(CanonicalAgentDefinition definition, CancellationToken cancellationToken) => Task.FromResult(new StudioResult<CanonicalAgentReference>(default, Error("create")));
    public Task<StudioResult<CanonicalAgentDefinition>> OpenAsync(Guid agentId, CancellationToken cancellationToken) => Task.FromResult(new StudioResult<CanonicalAgentDefinition>(default, Error("read")));
    public Task<StudioResult<CanonicalAgentDefinition>> UpdateDraftAsync(Guid agentId, long expectedRevision, CanonicalAgentDefinition definition, CancellationToken cancellationToken) => Task.FromResult(new StudioResult<CanonicalAgentDefinition>(default, Error("draft update")));
    public Task<StudioResult<ValidationReport>> ValidateAsync(Guid agentId, long? revision, CancellationToken cancellationToken) => Task.FromResult(new StudioResult<ValidationReport>(default, Error("validation")));
    public Task<StudioResult<StudioRun>> PreviewAsync(Guid agentId, string input, AgentPreviewOptions options, CancellationToken cancellationToken) => Task.FromResult(new StudioResult<StudioRun>(default, Error("preview")));
    public Task<StudioResult<CanonicalAgentReference>> ActivateAsync(Guid agentId, long revision, CancellationToken cancellationToken) => Task.FromResult(new StudioResult<CanonicalAgentReference>(default, Error("activation")));
    public Task<StudioResult<Unit>> ShareAsync(Guid agentId, string principalId, string role, CancellationToken cancellationToken) => Task.FromResult(new StudioResult<Unit>(default, Error("sharing")));
}
