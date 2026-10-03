using System.Collections.Frozen;
using System.Text.Json;
using Haven.Core;
using NineToOne.Dulche.Den;

namespace Dulche.Runtime.Agents;

/// <summary>The shared Guid definition is a transient projection of the same current Den record.
/// Neither projection is independently saved or editable.</summary>
public sealed record DenAgentProjection(DenAgentReference Reference, PersistentAgentSnapshot Execution,
    AgentDefinition SharedDefinition, AgentDefinitionRecord? AuthoringDefinition = null);

/// <summary>Bound by the trusted host to one existing caller-owned Den session and explicit namespace.
/// All reads use the canonical store and its current ACL. Invocation and tool authorization remain
/// with the existing Home permission broker; caller/context strings confer no authority here.</summary>
public sealed class DenPersistentAgentCatalog(DulcheDen den, string namespaceId) : IPersistentAgentCatalog
{
    public async ValueTask<AgentResult<PersistentAgentSnapshot>> GetAsync(string agentId,
        CancellationToken cancellationToken = default)
    {
        var projection = await ReadProjectionAsync(agentId, null, cancellationToken).ConfigureAwait(false);
        return projection.Error is { } error ? AgentResult<PersistentAgentSnapshot>.Failure(error) :
            AgentResult<PersistentAgentSnapshot>.Success(projection.Value!.Execution);
    }

    public async ValueTask<AgentResult<PersistentAgentSnapshot>> GetRevisionAsync(string agentId,
        long definitionRevision, CancellationToken cancellationToken = default)
    {
        if (definitionRevision < 1)
            return Failure<PersistentAgentSnapshot>(AgentFailureCode.DefinitionRevisionUnavailable,
                "A current canonical definition revision is required.", agentId);
        var projection = await ReadProjectionAsync(agentId, definitionRevision, cancellationToken).ConfigureAwait(false);
        return projection.Error is { } error ? AgentResult<PersistentAgentSnapshot>.Failure(error) :
            AgentResult<PersistentAgentSnapshot>.Success(projection.Value!.Execution);
    }

    public async ValueTask<AgentResult<IReadOnlyList<PersistentAgentSnapshot>>> ResolveForAsync(
        AgentInvocationContext context, CancellationToken cancellationToken = default)
    {
        // This comparison can only narrow a read; equality is not authenticated admission.
        if (context.CallerId != den.PrincipalId)
            return Failure<IReadOnlyList<PersistentAgentSnapshot>>(AgentFailureCode.PermissionDenied,
                "The requested caller does not match the bound Den principal.", namespaceId);
        try
        {
            await RequireNamespaceAsync(cancellationToken).ConfigureAwait(false);
            var records = await den.ListAsync<AgentDefinitionRecord>(namespaceId, cancellationToken).ConfigureAwait(false);
            var resolved = new List<PersistentAgentSnapshot>();
            foreach (var record in records)
            {
                var projection = await ReadProjectionAsync(record.Id, record.Revision, cancellationToken).ConfigureAwait(false);
                if (projection.Error is { } error)
                    return AgentResult<IReadOnlyList<PersistentAgentSnapshot>>.Failure(error);
                if (projection.Value!.Execution.Enabled && AvailableIn(projection.Value.AuthoringDefinition!, context))
                    resolved.Add(projection.Value.Execution);
            }
            await RequireNamespaceAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return AgentResult<IReadOnlyList<PersistentAgentSnapshot>>.Success(resolved.AsReadOnly());
        }
        catch (DenException error) { return DenFailure<IReadOnlyList<PersistentAgentSnapshot>>(error, namespaceId); }
    }

    public async ValueTask<AgentResult<DenAgentProjection>> GetProjectionAsync(DenAgentReference expected,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authority = await RequireNamespaceAsync(cancellationToken).ConfigureAwait(false);
            if (expected.DenId != authority.DenId || expected.NamespaceId != namespaceId)
                return Failure<DenAgentProjection>(AgentFailureCode.AgentUnavailableInScope,
                    "The Agent reference belongs to another Den or namespace.", expected.AgentId);
            return await ReadProjectionAsync(expected.AgentId, expected.DefinitionRevision, cancellationToken).ConfigureAwait(false);
        }
        catch (DenException error) { return DenFailure<DenAgentProjection>(error, expected.AgentId); }
    }

    public ValueTask<AgentResult<DenAgentProjection>> GetCurrentProjectionAsync(string agentId,
        CancellationToken cancellationToken = default) => ReadProjectionAsync(agentId, null, cancellationToken);

    /// <summary>Visibility only narrows the SAME host/issuer-admitted invocation. Matching IDs
    /// cannot grant Execute or tool permission. The original host rechecks authority separately.</summary>
    public async ValueTask<AgentResult<DenAgentProjection>> GetInvocationProjectionAsync(DenAgentReference expected,
        AgentInvocationContext context, CancellationToken cancellationToken = default)
    {
        if (context.CallerId != den.PrincipalId)
            return Failure<DenAgentProjection>(AgentFailureCode.PermissionDenied,
                "The invocation caller differs from the bound current Den principal.", expected.AgentId);
        var result = await GetProjectionAsync(expected, cancellationToken).ConfigureAwait(false);
        if (result.Error is not null) return result;
        var record = result.Value!.AuthoringDefinition!;
        if (!AvailableIn(record, context))
            return Failure<DenAgentProjection>(AgentFailureCode.AgentUnavailableInScope,
                "The same canonical Agent is unavailable in this original invocation scope.", expected.AgentId);
        if (record.KnowledgeReferences.Count != 0 || record.GraphReference is not null ||
            record.MemoryPolicy is { } memory &&
                (memory.ReadFrequency != MemoryFrequency.Never || memory.WriteFrequency != MemoryFrequency.Never))
            return Failure<DenAgentProjection>(AgentFailureCode.CapabilityUnavailable,
                "BLOCKED: owning knowledge/graph/active memory execution routes are not integrated; saved references grant no access.", expected.AgentId);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static bool AvailableIn(AgentDefinitionRecord record, AgentInvocationContext context) =>
        record.AvailabilityBindings is null || record.AvailabilityBindings.Any(binding => binding.Scope switch
        {
            AgentAvailabilityScope.Global => true,
            AgentAvailabilityScope.Surface => binding.TargetId == context.SurfaceId,
            AgentAvailabilityScope.Space => context.SpaceId is not null && binding.TargetId == context.SpaceId,
            AgentAvailabilityScope.ProjectOrEntity => context.ProjectOrEntityId is not null && binding.TargetId == context.ProjectOrEntityId,
            _ => false
        });

    private async ValueTask<AgentResult<DenAgentProjection>> ReadProjectionAsync(string agentId,
        long? expectedRevision, CancellationToken cancellationToken)
    {
        try
        {
            var authority = await RequireNamespaceAsync(cancellationToken).ConfigureAwait(false);
            var record = await den.GetAsync<AgentDefinitionRecord>(namespaceId, agentId, cancellationToken).ConfigureAwait(false);
            if (record is null)
                return Failure<DenAgentProjection>(AgentFailureCode.AgentNotFound, "The canonical Agent was not found.", agentId);
            if (expectedRevision is { } expected && record.Revision != expected)
                return Failure<DenAgentProjection>(AgentFailureCode.DefinitionRevisionUnavailable,
                    "The canonical Agent changed; its current revision must be admitted again.", agentId);
            if (!await CanExecuteAsync(agentId, cancellationToken).ConfigureAwait(false))
                return Failure<DenAgentProjection>(AgentFailureCode.PermissionDenied,
                    "The bound principal cannot execute this canonical Agent.", agentId);
            if (!Guid.TryParseExact(record.Id, "D", out var sharedId) || sharedId == Guid.Empty ||
                record.Id != sharedId.ToString("D"))
                return Failure<DenAgentProjection>(AgentFailureCode.CapabilityUnavailable,
                    "This Den Agent ID is not supported by the shared Guid runtime. No replacement ID was created.", agentId);
            record = DenAgentAuthoringFields.Capture(record, cancellationToken: cancellationToken);
            var model = ReadPolicy<AgentModelPolicy>(record.ModelPolicyJson) ?? new(Inherit: true);
            var budget = ReadPolicy<AgentBudgetLimits>(record.BudgetJson) ?? new();
            if (budget.Validate(agentId) is { } invalidBudget)
                return AgentResult<DenAgentProjection>.Failure(invalidBudget);
            if (string.IsNullOrWhiteSpace(record.DisplayName) || record.DisplayName.Length > 256 ||
                record.AllowedPermissions is null || record.ToolIds is null || record.SkillIds is null ||
                record.PluginIds is null || record.McpCapabilityIds is null ||
                record.AllowedPermissions.Any(string.IsNullOrWhiteSpace) ||
                record.ToolIds.Concat(record.SkillIds).Concat(record.PluginIds).Concat(record.McpCapabilityIds).Any(string.IsNullOrWhiteSpace))
                return Failure<DenAgentProjection>(AgentFailureCode.InvalidInvocationContext,
                    "The canonical Agent requires a bounded name and nonempty capability/dependency IDs.", agentId);
            if (model.RequiredCapabilities?.Any(string.IsNullOrWhiteSpace) == true)
                return Failure<DenAgentProjection>(AgentFailureCode.InvalidInvocationContext,
                    "Required model capabilities cannot be empty.", agentId);
            model = model with { RequiredCapabilities = model.RequiredCapabilities?.ToFrozenSet(StringComparer.Ordinal) };
            var originalCapabilities = record.AllowedPermissions.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            var declaredPolicy = ReadDeclaration<AgentCapabilityPolicy>(record.CapabilityPolicyJson);
            if (declaredPolicy is not null && AgentSavedPolicyValidation.Validate(declaredPolicy, agentId) is { } invalidPolicy)
                return AgentResult<DenAgentProjection>.Failure(invalidPolicy);
            var denied = (declaredPolicy?.DeniedCapabilities ?? Array.Empty<string>().ToFrozenSet())
                .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            var declaredAllowed = declaredPolicy?.AllowedCapabilities.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            var capabilities = originalCapabilities.Where(value => !denied.Contains(value) &&
                (declaredAllowed is null || declaredAllowed.Contains(value)))
                .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            var dependencies = record.ToolIds.Concat(record.SkillIds).Concat(record.PluginIds)
                .Concat(record.McpCapabilityIds).ToFrozenSet(StringComparer.Ordinal);
            // Saved declarations only narrow the original permission upper bound and cannot
            // remove its original consequential approval requirement or replace Home authority.
            var policy = new AgentCapabilityPolicy(capabilities, denied, RequireApprovalForConsequentialActions: true);
            var delegation = ReadDeclaration<AgentDelegationPolicy>(record.DelegationPolicyJson) ??
                new AgentDelegationPolicy(Array.Empty<string>().ToFrozenSet(StringComparer.Ordinal),
                    Array.Empty<string>().ToFrozenSet(StringComparer.Ordinal), false, 1, 0, budget);
            if (AgentSavedPolicyValidation.Validate(delegation, agentId) is { } invalidDelegation)
                return AgentResult<DenAgentProjection>.Failure(invalidDelegation);
            delegation = delegation with { AllowedAgentIds = delegation.AllowedAgentIds.ToFrozenSet(StringComparer.Ordinal),
                AllowedAgentClasses = delegation.AllowedAgentClasses.ToFrozenSet(StringComparer.Ordinal) };
            var execution = new PersistentAgentSnapshot(record.Id, record.Revision, record.DisplayName, record.Enabled,
                null, model, policy, delegation, namespaceId, dependencies);
            var shared = new AgentDefinition(sharedId, record.DisplayName, record.Description ?? string.Empty, record.Instructions ?? string.Empty,
                "agent", model.ModelId ?? "default", null, "manual",
                JsonSerializer.Serialize(new { capabilities = capabilities.Order(StringComparer.Ordinal).ToArray() }),
                false, record.Enabled, record.UpdatedAtUtc);
            var current = await den.GetAsync<AgentDefinitionRecord>(namespaceId, agentId, cancellationToken).ConfigureAwait(false);
            var currentAuthority = await RequireNamespaceAsync(cancellationToken).ConfigureAwait(false);
            if (current is null || current.Revision != record.Revision || currentAuthority.DenId != authority.DenId ||
                currentAuthority.ManifestRevision != authority.ManifestRevision)
                return Failure<DenAgentProjection>(AgentFailureCode.RevisionConflict,
                    "The canonical Agent or namespace changed during projection.", agentId);
            if (!await CanExecuteAsync(agentId, cancellationToken).ConfigureAwait(false))
                return Failure<DenAgentProjection>(AgentFailureCode.PermissionDenied,
                    "Execution permission changed during projection.", agentId);
            cancellationToken.ThrowIfCancellationRequested();
            return AgentResult<DenAgentProjection>.Success(new(new(authority.DenId, namespaceId, record.Id, record.Revision), execution, shared, record));
        }
        catch (DenException error) { return DenFailure<DenAgentProjection>(error, agentId); }
        catch (JsonException) { return Failure<DenAgentProjection>(AgentFailureCode.InvalidInvocationContext, "The saved Agent model, budget or execution policy is invalid.", agentId); }
    }

    private async ValueTask<DenAuthoritySnapshot> RequireNamespaceAsync(CancellationToken cancellationToken)
    {
        var current = await den.Store.ReadAuthoritySnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(namespaceId) || !current.Namespaces.Any(item => item.Id == namespaceId))
            throw new DenException(DenErrorCode.Forbidden, "The explicitly bound Den namespace is unavailable.");
        return current;
    }

    private ValueTask<bool> CanExecuteAsync(string agentId, CancellationToken cancellationToken) =>
        den.AccessPolicy.IsAllowedAsync(den.PrincipalId, namespaceId, agentId, DenPermission.Execute, cancellationToken);

    private static T? ReadPolicy<T>(string? json) where T : class =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json, DenJson.Options)
            ?? throw new JsonException("A saved Agent policy cannot be null.");

    private static T? ReadDeclaration<T>(string? json) where T : class => json is null ? null :
        JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(DenJson.Options)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        }) ?? throw new JsonException("A saved execution declaration cannot be null.");

    private static AgentResult<T> DenFailure<T>(DenException error, string target) => Failure<T>(error.Code switch
    {
        DenErrorCode.Forbidden => AgentFailureCode.PermissionDenied,
        DenErrorCode.NotFound => AgentFailureCode.AgentNotFound,
        DenErrorCode.Conflict => AgentFailureCode.RevisionConflict,
        DenErrorCode.InvalidRecord => AgentFailureCode.InvalidInvocationContext,
        _ => AgentFailureCode.StateStoreUnavailable
    }, error.Message, target);

    private static AgentResult<T> Failure<T>(AgentFailureCode code, string message, string target) =>
        AgentResult<T>.Failure(new(code, message, target));
}
