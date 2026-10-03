using System.Text.Json;
using System.Text.Json.Serialization;
using Dulche.Runtime.Agents;
using HavenOS.Home.Core;
using NineToOne.Dulche.Den;

namespace HavenOS.AIStudio;

/// <summary>Canonical authoring fields; these declarations do not resolve a registry,
/// grant a capability or prove an Agent executable. Presentation lives on the same
/// Den record and is preserved by draft edits.</summary>
public sealed record DenAgentBuilderConfiguration
{
    public AgentModelPolicy ModelPolicy { get; init; } = new(true);
    public AgentBudgetLimits Budget { get; init; } = new();
    public IReadOnlyList<string> ToolIds { get; init; } = [];
    public IReadOnlyList<string> SkillIds { get; init; } = [];
    public IReadOnlyList<string> PluginIds { get; init; } = [];
    public IReadOnlyList<string> McpCapabilityIds { get; init; } = [];
    public IReadOnlyList<string> AllowedPermissions { get; init; } = [];
    public IReadOnlyList<AgentAvailabilityBinding>? AvailabilityBindings { get; init; }
    public IReadOnlyList<AgentKnowledgeReference> KnowledgeReferences { get; init; } = [];
    public IReadOnlyList<AgentQuickActionDefinition> QuickActions { get; init; } = [];
    public AgentMemoryPolicy? MemoryPolicy { get; init; }
    public AgentCapabilityPolicy? CapabilityPolicy { get; init; }
    public AgentDelegationPolicy? DelegationPolicy { get; init; }
    public DenAgentGraphReference? GraphReference { get; init; }
    public AgentSharingMetadata? SharingMetadata { get; init; }
    public AgentDefinitionLifecycle? LifecycleState { get; init; }
}

/// <summary>One actual current Home-bound Den authoring context. The production factory
/// comes from StudioDenLifetime; a DTO, Guid, display name or Enabled flag cannot bind it.
/// After first use this instance refuses any selected-store or actor replacement.</summary>
public enum CanonicalAgentCommitKind { CreatedDraft, UpdatedDraft }

/// <summary>Known local mutation stage only. This reference grants no read or execution access
/// and carries no Agent definition/private content. It cannot bind another actor or Den.</summary>
public sealed record CanonicalAgentCommitReceipt(Guid AgentId, long DefinitionRevision, CanonicalAgentCommitKind Kind);

/// <summary>A canonical Save returned successfully but its subsequent current-session observation
/// failed. Creation MUST NOT be retried as though nothing was saved.</summary>
public sealed class CanonicalAgentCommittedObservationException(CanonicalAgentCommitReceipt receipt, Exception cause)
    : Exception("The Agent draft was saved, but its post-save observation could not finish. Recover the saved Agent; do not repeat the save or creation.", cause)
{
    public CanonicalAgentCommitReceipt Receipt { get; } = receipt;
}

public sealed class DenCanonicalAgentBuilderAdapter : ICanonicalAgentBuilderAdapter
{
    private static readonly JsonSerializerOptions ConfigurationJson = new(DenJson.Options)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly Func<CancellationToken, Task<HomePersonalDenSession>> _openCurrent;
    private readonly string _namespaceId;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HomePersonalDenSession? _basis;

    public DenCanonicalAgentBuilderAdapter(Func<CancellationToken, Task<HomePersonalDenSession>> openCurrent,
        string namespaceId = "personal")
    {
        ArgumentNullException.ThrowIfNull(openCurrent);
        if (namespaceId != "personal") throw new ArgumentException("This actual Home host binds the personal namespace only.", nameof(namespaceId));
        _openCurrent = openCurrent; _namespaceId = namespaceId;
    }

    public Task<StudioResult<CanonicalAgentReference>> CreateAsync(CanonicalAgentDefinition definition,
        CancellationToken cancellationToken) => Guard(async ct =>
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.AgentId != Guid.Empty || definition.DefinitionRevision != 0 || !definition.IsDraft)
            throw new DenException(DenErrorCode.InvalidRecord, "Create requires a fresh canonical draft with no supplied identity or revision.");
        ValidateText(definition); var config = DecodeConfiguration(definition.ConfigurationJson);
        var session = await CurrentAsync(ct).ConfigureAwait(false);
        var record = Apply(new AgentDefinitionRecord { Id = Guid.NewGuid().ToString("D"), NamespaceId = _namespaceId,
            DisplayName = definition.Name, Version = "1", Enabled = false }, definition, config, ct);
        var saved = await session.Den.SaveAsync(record, 0, Guid.NewGuid().ToString("N"), ct).ConfigureAwait(false);
        return await ObserveCommittedAsync(saved, CanonicalAgentCommitKind.CreatedDraft,
            () => new CanonicalAgentReference(Guid.ParseExact(saved.Id, "D"), saved.Revision, session.Actor.ActorId), ct).ConfigureAwait(false);
    }, cancellationToken, checkCancellationAfterAction: false);

    public Task<StudioResult<CanonicalAgentDefinition>> OpenAsync(Guid agentId,
        CancellationToken cancellationToken) => Guard(async ct =>
    {
        var (_, record) = await ReadAsync(agentId, null, ct).ConfigureAwait(false);
        var result = Project(record, ct); await CurrentAsync(ct).ConfigureAwait(false); return result;
    }, cancellationToken);

    public Task<StudioResult<CanonicalAgentDefinition>> UpdateDraftAsync(Guid agentId, long expectedRevision,
        CanonicalAgentDefinition definition, CancellationToken cancellationToken) => Guard(async ct =>
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.AgentId != agentId || definition.DefinitionRevision != expectedRevision || !definition.IsDraft)
            throw new DenException(DenErrorCode.InvalidRecord, "A draft edit must retain its exact original identity and expected revision.");
        ValidateText(definition); var config = DecodeConfiguration(definition.ConfigurationJson);
        var (session, record) = await ReadAsync(agentId, expectedRevision, ct).ConfigureAwait(false);
        if (record.Enabled) throw new DenException(DenErrorCode.CapabilityUnavailable,
            "Editing an active Agent requires its canonical runtime draft/version operation; this editor will not silently disable it.");
        await CurrentAsync(ct).ConfigureAwait(false);
        var saved = await session.Den.SaveAsync(Apply(record, definition, config, ct), expectedRevision,
            Guid.NewGuid().ToString("N"), ct).ConfigureAwait(false);
        return await ObserveCommittedAsync(saved, CanonicalAgentCommitKind.UpdatedDraft,
            () => Project(saved, ct), ct).ConfigureAwait(false);
    }, cancellationToken, checkCancellationAfterAction: false);

    public Task<StudioResult<ValidationReport>> ValidateAsync(Guid agentId, long? revision,
        CancellationToken cancellationToken) => Guard(async ct =>
    {
        var (_, record) = await ReadAsync(agentId, revision, ct).ConfigureAwait(false);
        _ = Project(record, ct); await CurrentAsync(ct).ConfigureAwait(false);
        // A valid authoring schema is not registry/model/execution validation.
        return new ValidationReport(false,
            [new("CanonicalRuntimeValidationUnavailable", "The current registry and trusted runtime validator are required before this draft can execute.", "agent", "error")],
            "CanonicalDenAuthoringSchemaOnly; registry/model/execution unverified", DateTimeOffset.UtcNow);
    }, cancellationToken);

    public Task<StudioResult<StudioRun>> PreviewAsync(Guid agentId, string input, AgentPreviewOptions options,
        CancellationToken cancellationToken) => Guard<StudioRun>(async ct =>
    {
        _ = await ReadAsync(agentId, null, ct).ConfigureAwait(false);
        throw new DenException(DenErrorCode.CapabilityUnavailable,
            "Test preview requires the current trusted canonical runtime; authoring permissions do not grant Execute.");
    }, cancellationToken);
    public Task<StudioResult<CanonicalAgentReference>> ActivateAsync(Guid agentId, long revision,
        CancellationToken cancellationToken) => Guard<CanonicalAgentReference>(async ct =>
    {
        _ = await ReadAsync(agentId, revision, ct).ConfigureAwait(false);
        throw new DenException(DenErrorCode.CapabilityUnavailable,
            "Activation requires current registry validation and trusted execution authority; this action did not enable the Agent.");
    }, cancellationToken);
    public Task<StudioResult<Unit>> ShareAsync(Guid agentId, string principalId, string role,
        CancellationToken cancellationToken) => Guard<Unit>(async ct =>
    {
        _ = await ReadAsync(agentId, null, ct).ConfigureAwait(false);
        throw new DenException(DenErrorCode.CapabilityUnavailable,
            "Sharing requires the canonical namespace administration authority; this action did not change its ACLs.");
    }, cancellationToken);

    private async Task<(HomePersonalDenSession Session, AgentDefinitionRecord Record)> ReadAsync(Guid agentId,
        long? revision, CancellationToken ct)
    {
        if (agentId == Guid.Empty) throw new DenException(DenErrorCode.InvalidRecord, "A canonical UUID Agent identity is required.");
        var session = await CurrentAsync(ct).ConfigureAwait(false);
        var record = await session.Den.GetAsync<AgentDefinitionRecord>(_namespaceId, agentId.ToString("D"), ct).ConfigureAwait(false)
            ?? throw new DenException(DenErrorCode.NotFound, "The Agent does not exist in this bound canonical namespace.");
        if (record.Id != agentId.ToString("D") || record.NamespaceId != _namespaceId || (revision is not null && record.Revision != revision))
            throw new DenException(DenErrorCode.Conflict, "The Agent identity, namespace or expected definition revision changed.");
        await CurrentAsync(ct).ConfigureAwait(false); return (session, record);
    }
    private async Task<HomePersonalDenSession> CurrentAsync(CancellationToken ct)
    {
        var current = await _openCurrent(ct).ConfigureAwait(false); ct.ThrowIfCancellationRequested();
        if (_basis is null) _basis = current;
        else if (current.Actor != _basis.Actor || current.DenId != _basis.DenId || !ReferenceEquals(current.Den.Store, _basis.Den.Store))
            throw new DenException(DenErrorCode.Forbidden, "The bound Den or current Home actor changed; reopen this editor.");
        return current;
    }
    private static AgentDefinitionRecord Apply(AgentDefinitionRecord record, CanonicalAgentDefinition definition,
        DenAgentBuilderConfiguration config, CancellationToken ct) => DenAgentAuthoringFields.Capture(record with
    {
        DisplayName = definition.Name, Description = definition.Description, Instructions = definition.Instructions,
        ModelPolicyJson = JsonSerializer.Serialize(config.ModelPolicy, ConfigurationJson),
        BudgetJson = JsonSerializer.Serialize(config.Budget, ConfigurationJson),
        ToolIds = config.ToolIds.ToArray(), SkillIds = config.SkillIds.ToArray(), PluginIds = config.PluginIds.ToArray(),
        McpCapabilityIds = config.McpCapabilityIds.ToArray(), AllowedPermissions = config.AllowedPermissions.ToArray(),
        AvailabilityBindings = config.AvailabilityBindings, KnowledgeReferences = config.KnowledgeReferences,
        QuickActions = config.QuickActions, MemoryPolicy = config.MemoryPolicy,
        CapabilityPolicyJson = config.CapabilityPolicy is null ? null : JsonSerializer.Serialize(config.CapabilityPolicy, ConfigurationJson),
        DelegationPolicyJson = config.DelegationPolicy is null ? null : JsonSerializer.Serialize(config.DelegationPolicy, ConfigurationJson),
        GraphReference = config.GraphReference, SharingMetadata = config.SharingMetadata, LifecycleState = config.LifecycleState
    }, cancellationToken: ct);
    private static CanonicalAgentDefinition Project(AgentDefinitionRecord record, CancellationToken ct)
    {
        record = DenAgentAuthoringFields.Capture(record, cancellationToken: ct);
        var config = new DenAgentBuilderConfiguration
        {
            ModelPolicy = record.ModelPolicyJson is null ? new(true) : JsonSerializer.Deserialize<AgentModelPolicy>(record.ModelPolicyJson, ConfigurationJson)
                ?? throw new DenException(DenErrorCode.InvalidRecord, "The canonical model policy is missing."),
            Budget = record.BudgetJson is null ? new() : JsonSerializer.Deserialize<AgentBudgetLimits>(record.BudgetJson, ConfigurationJson)
                ?? throw new DenException(DenErrorCode.InvalidRecord, "The canonical budget is missing."),
            ToolIds = record.ToolIds, SkillIds = record.SkillIds, PluginIds = record.PluginIds,
            McpCapabilityIds = record.McpCapabilityIds, AllowedPermissions = record.AllowedPermissions,
            AvailabilityBindings = record.AvailabilityBindings, KnowledgeReferences = record.KnowledgeReferences,
            QuickActions = record.QuickActions, MemoryPolicy = record.MemoryPolicy,
            CapabilityPolicy = record.CapabilityPolicyJson is null ? null : JsonSerializer.Deserialize<AgentCapabilityPolicy>(record.CapabilityPolicyJson, ConfigurationJson)
                ?? throw new DenException(DenErrorCode.InvalidRecord, "The canonical capability policy is missing."),
            DelegationPolicy = record.DelegationPolicyJson is null ? null : JsonSerializer.Deserialize<AgentDelegationPolicy>(record.DelegationPolicyJson, ConfigurationJson)
                ?? throw new DenException(DenErrorCode.InvalidRecord, "The canonical delegation policy is missing."),
            GraphReference = record.GraphReference, SharingMetadata = record.SharingMetadata, LifecycleState = record.LifecycleState
        };
        ValidateConfiguration(config, ct);
        return new(Guid.ParseExact(record.Id, "D"), record.DisplayName, record.Description ?? "", record.Instructions ?? "",
            JsonSerializer.Serialize(config, ConfigurationJson), record.Revision, !record.Enabled);
    }
    /// <summary>Structural typed authoring JSON only; decoding grants no identity or execution permission.</summary>
    public static DenAgentBuilderConfiguration DecodeConfiguration(string source)
    {
        if (source is null || source.Length > 32768) throw new DenException(DenErrorCode.InvalidRecord, "Agent configuration must be at most 32768 characters.");
        var config = JsonSerializer.Deserialize<DenAgentBuilderConfiguration>(source, ConfigurationJson)
            ?? throw new DenException(DenErrorCode.InvalidRecord, "The canonical configuration is missing.");
        ValidateConfiguration(config); return config;
    }
    /// <summary>Uses the existing Den set converter and omits optional null fields.
    /// These serialized declarations remain unresolved until the current runtime validates them.</summary>
    public static string EncodeConfiguration(DenAgentBuilderConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config); ValidateConfiguration(config);
        var source = JsonSerializer.Serialize(config, ConfigurationJson);
        if (source.Length > 32768) throw new DenException(DenErrorCode.InvalidRecord, "Agent configuration must be at most 32768 characters.");
        return source;
    }
    private static void ValidateConfiguration(DenAgentBuilderConfiguration config, CancellationToken cancellationToken = default)
    {
        if (config.ModelPolicy is null || config.Budget is null || config.Budget.Validate("agent") is not null)
            throw new DenException(DenErrorCode.InvalidRecord, "The model policy and nonnegative canonical budget are required.");
        foreach (var id in new[] { config.ModelPolicy.ModelId, config.ModelPolicy.ProviderId })
            if (id is not null && !IsExactId(id))
                throw new DenException(DenErrorCode.InvalidRecord, "Model and provider declarations require exact IDs of at most 256 characters.");
        if (config.ModelPolicy.RequiredCapabilities is { } capabilities &&
            (capabilities.Count > 128 || capabilities.Any(id => !IsExactId(id))))
            throw new DenException(DenErrorCode.InvalidRecord, "Required model capabilities require at most 128 exact IDs.");
        foreach (var ids in new[] { config.ToolIds, config.SkillIds, config.PluginIds, config.McpCapabilityIds, config.AllowedPermissions })
            if (ids is null || ids.Count > 128 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count ||
                ids.Any(id => !IsExactId(id)))
                throw new DenException(DenErrorCode.InvalidRecord, "Dependency and permission declarations require unique exact IDs, at most 128 per category.");
        if (config.CapabilityPolicy is { } capabilitiesPolicy)
            ValidatePolicyIds(capabilitiesPolicy.AllowedCapabilities, capabilitiesPolicy.DeniedCapabilities);
        if (config.DelegationPolicy is { } delegation)
        {
            ValidatePolicyIds(delegation.AllowedAgentIds, delegation.AllowedAgentClasses);
            if (delegation.MaximumConcurrency < 1 || delegation.MaximumDepth < 0 ||
                (delegation.AllowSubagents && delegation.MaximumDepth == 0) || delegation.Budget is null ||
                delegation.Budget.Validate("agent-delegation") is not null)
                throw new DenException(DenErrorCode.InvalidRecord, "Delegation declarations require consistent nonnegative limits and a canonical budget.");
        }
        // This unpersisted metadata probe binds no Agent identity and invokes no host service.
        // Actual creation/edit Capture below uses the original canonical ID/revision/Enabled.
        _ = DenAgentAuthoringFields.Capture(new AgentDefinitionRecord
        {
            Id = "unpersisted-configuration-check", NamespaceId = "personal", DisplayName = "Metadata validation", Version = "1",
            Enabled = config.LifecycleState == AgentDefinitionLifecycle.Active,
            AvailabilityBindings = config.AvailabilityBindings, KnowledgeReferences = config.KnowledgeReferences,
            QuickActions = config.QuickActions, MemoryPolicy = config.MemoryPolicy,
            CapabilityPolicyJson = config.CapabilityPolicy is null ? null : JsonSerializer.Serialize(config.CapabilityPolicy, ConfigurationJson),
            DelegationPolicyJson = config.DelegationPolicy is null ? null : JsonSerializer.Serialize(config.DelegationPolicy, ConfigurationJson),
            GraphReference = config.GraphReference, SharingMetadata = config.SharingMetadata, LifecycleState = config.LifecycleState
        }, cancellationToken: cancellationToken);
    }
    private static void ValidatePolicyIds(params IReadOnlySet<string>[] sets)
    {
        if (sets.Any(set => set is null || set.Count > 128 || set.Any(id => !IsExactId(id))))
            throw new DenException(DenErrorCode.InvalidRecord, "Policy declarations require exact IDs, at most 128 per set.");
    }
    private static bool IsExactId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 256 && id == id.Trim() && !id.Any(char.IsControl);
    private static void ValidateText(CanonicalAgentDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Length > 256 || definition.Description is null ||
            definition.Description.Length > 8192 || definition.Instructions is null || definition.Instructions.Length > 65536)
            throw new DenException(DenErrorCode.InvalidRecord, "Agent name, purpose and instructions exceed their canonical authoring limits.");
    }
    private async Task<T> ObserveCommittedAsync<T>(AgentDefinitionRecord saved, CanonicalAgentCommitKind kind,
        Func<T> project, CancellationToken ct)
    {
        var receipt = new CanonicalAgentCommitReceipt(Guid.ParseExact(saved.Id, "D"), saved.Revision, kind);
        try
        {
            await CurrentAsync(ct).ConfigureAwait(false); ct.ThrowIfCancellationRequested();
            return project();
        }
        catch (Exception cause) { throw new CanonicalAgentCommittedObservationException(receipt, cause); }
    }
    private async Task<StudioResult<T>> Guard<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct,
        bool checkCancellationAfterAction = true)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { var result = await action(ct).ConfigureAwait(false); if (checkCancellationAfterAction) ct.ThrowIfCancellationRequested(); return StudioResult<T>.Success(result); }
        catch (DenException e) { return StudioResult<T>.Failure(e.Code.ToString(), e.Message, retryable: e.Retryable, recoverable: e.Recoverable); }
        catch (UnauthorizedAccessException) { return StudioResult<T>.Failure("Forbidden", "A current verified Home Den session is required."); }
        catch (JsonException) { return StudioResult<T>.Failure("InvalidRecord", "Agent configuration does not match the canonical typed schema."); }
        finally { _gate.Release(); }
    }
}
