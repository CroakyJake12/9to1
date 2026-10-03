using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NineToOne.Dulche.Den;

/// <summary>Explicit structural input limits, not service allowances, permission or a hard
/// allocation/CPU boundary. Serialization/library allocation precedes its final byte check.</summary>
public sealed record AgentAuthoringValidationLimits(int MaxEntriesPerList = 256,
    int MaxReferenceCharacters = 2048, int MaxInstructionCharacters = 65_536,
    int MaxJsonBytes = 262_144, int MaxJsonDepth = 32, int MaxJsonNodes = 16_384,
    int MaxDefinitionBytes = 2_097_152)
{
    public void Validate()
    {
        if (MaxEntriesPerList is < 1 or > 4096 || MaxReferenceCharacters is < 16 or > 16_384 ||
            MaxInstructionCharacters is < 1 or > 1_048_576 || MaxJsonBytes is < 16 or > 1_048_576 ||
            MaxJsonDepth is < 1 or > 64 || MaxJsonNodes is < 1 or > 1_000_000 ||
            MaxDefinitionBytes is < 256 or > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(AgentAuthoringValidationLimits));
    }
}

/// <summary>Captures the same canonical record and structurally validates its authoring
/// declarations. Registry/model/graph/context/memory/ACL/current actor and execution authority
/// are separate owning checks. A successful capture is never activation or permission.</summary>
public static class DenAgentAuthoringFields
{
    public static AgentDefinitionRecord Capture(AgentDefinitionRecord record,
        AgentAuthoringValidationLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record); limits ??= new(); limits.Validate();
        var validator = new Validator(limits, cancellationToken); validator.Check(record);
        var options = new JsonSerializerOptions(DenJson.Options)
        { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, MaxDepth = limits.MaxJsonDepth };
        // Clone the entire same record, including existing presentation/lists/extension JSON,
        // so the returned definition cannot follow later mutable input or JsonDocument lifetime.
        AgentDefinitionRecord detached;
        try
        {
            if (record.ExtensionData is { Count: > 0 } extension)
            {
                // Derive actual canonical member names, including inherited/optional members
                // and the maintained Den discriminator, using the same owning serializer.
                // Extension data may retain unknown fields; it must never overwrite a member.
                var memberOptions = new JsonSerializerOptions(options)
                { DefaultIgnoreCondition = JsonIgnoreCondition.Never };
                var canonical = JsonSerializer.SerializeToElement<DenRecord>(
                    record with { ExtensionData = null }, memberOptions);
                var names = canonical.EnumerateObject().Select(property => property.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var name in extension.Keys)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (names.Contains(name)) Refuse("AgentExtensionDataCanonicalMemberCollision");
                }
            }
            var bytes = JsonSerializer.SerializeToUtf8Bytes(record, options);
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes.Length > limits.MaxDefinitionBytes) Refuse("AgentDefinitionBytesExceeded");
            detached = JsonSerializer.Deserialize<AgentDefinitionRecord>(bytes, options)
                ?? throw new DenException(DenErrorCode.InvalidRecord, "AgentDefinitionMissing");
        }
        catch (JsonException) { throw new DenException(DenErrorCode.InvalidRecord, "AgentDefinitionJsonInvalidOrExceeded"); }
        validator.Check(detached);
        if (detached.Id != record.Id || detached.NamespaceId != record.NamespaceId || detached.Revision != record.Revision)
            Refuse("AgentDefinitionIdentityChangedDuringCapture");
        cancellationToken.ThrowIfCancellationRequested(); return detached;
    }
    [DoesNotReturn]
    private static void Refuse(string diagnostic) => throw new DenException(DenErrorCode.InvalidRecord, diagnostic);

    private sealed class Validator(AgentAuthoringValidationLimits limits, CancellationToken token)
    {
        public void Check(AgentDefinitionRecord record)
        {
            token.ThrowIfCancellationRequested(); Text(record.Id); Text(record.NamespaceId);
            if (record.Revision < 1) Refuse("AgentDefinitionRevisionInvalid");
            if (record.LifecycleState is { } lifecycle)
            {
                Defined(lifecycle);
                if ((lifecycle == AgentDefinitionLifecycle.Active) != record.Enabled)
                    Refuse("AgentLifecycleEnabledMismatch");
            }
            if (record.AvailabilityBindings is { } availability)
                Each(availability, item =>
                {
                    if (item is null) Refuse("AgentAvailabilityMissing");
                    Identifier(item!.BindingId); Defined(item.Scope);
                    if (item.Scope == AgentAvailabilityScope.Global)
                    { if (item.TargetId is not null) Refuse("AgentGlobalTargetInvalid"); }
                    else Text(item.TargetId);
                }, item => item.BindingId);
            Each(record.KnowledgeReferences, Knowledge, item => item.ReferenceId);
            Each(record.QuickActions, action =>
            {
                if (action is null) Refuse("AgentQuickActionMissing");
                Identifier(action!.QuickActionId); Text(action.Label); Optional(action.IconReference); Optional(action.Description);
                Defined(action.ConfirmationPolicy);
                if ((action.Instruction is not null) == (action.GraphEntry is not null))
                    Refuse("AgentQuickActionEntryAmbiguousOrMissing");
                if (action.Instruction is not null) Text(action.Instruction, instruction: true);
                if (action.GraphEntry is not null) Graph(action.GraphEntry);
                if (action.InputSchema is { } schema) Json(schema, schema: true);
                if (action.ContextRequirements is { } context) Each(context, Knowledge, item => item.ReferenceId);
            }, item => item.QuickActionId);
            if (record.MemoryPolicy is { } memory)
            {
                Text(memory.NamespaceId); Text(memory.ScopeId); Defined(memory.ScopeKind);
                Defined(memory.ReadFrequency); Defined(memory.WriteFrequency);
                if (memory.MaximumRetention < TimeSpan.Zero) Refuse("AgentMemoryRetentionInvalid");
            }
            PolicyJson(record.CapabilityPolicyJson); PolicyJson(record.DelegationPolicyJson);
            if (record.GraphReference is { } graph) Graph(graph);
            if (record.SharingMetadata is { } sharing)
            {
                Text(sharing.ScopeId); Optional(sharing.OrganisationId); Optional(sharing.TeamId);
                Each(sharing.PrincipalIds, item => Text(item), item => item);
            }
            token.ThrowIfCancellationRequested();
        }
        private void Knowledge(AgentKnowledgeReference item)
        {
            if (item is null) Refuse("AgentKnowledgeReferenceMissing");
            Identifier(item!.ReferenceId); Text(item.OwningApp); Text(item.CanonicalId);
            Text(item.Revision); Text(item.AccessScope); Optional(item.DisplayName); Optional(item.ContentHash);
        }
        private void Graph(DenAgentGraphReference graph)
        {
            Text(graph.DenId); Text(graph.NamespaceId); Text(graph.WorkflowId); Optional(graph.EntryId);
            if (graph.DefinitionRevision < 1) Refuse("AgentGraphRevisionInvalid");
        }
        private void Identifier(Guid id) { token.ThrowIfCancellationRequested(); if (id == Guid.Empty) Refuse("AgentReferenceIdMissing"); }
        private void Defined<T>(T value) where T : struct, Enum
        { token.ThrowIfCancellationRequested(); if (!Enum.IsDefined(value)) Refuse("AgentDeclarationEnumUnknown"); }
        private void Text(string? value, bool instruction = false)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(value) || value.Length > (instruction ? limits.MaxInstructionCharacters : limits.MaxReferenceCharacters) ||
                value.Any(character => char.IsControl(character) && !(instruction && character is '\r' or '\n' or '\t')))
                Refuse("AgentDeclarationTextInvalidOrExceeded");
        }
        private void Optional(string? value) { if (value is not null) Text(value); }
        private void Each<T, TKey>(IReadOnlyList<T>? list, Action<T> validate, Func<T, TKey> key) where TKey : notnull
        {
            token.ThrowIfCancellationRequested();
            if (list is null || list.Count > limits.MaxEntriesPerList) Refuse("AgentDeclarationListMissingOrExceeded");
            var keys = new HashSet<TKey>();
            foreach (var item in list!)
            {
                token.ThrowIfCancellationRequested(); validate(item);
                if (!keys.Add(key(item))) Refuse("AgentDeclarationIdentityDuplicate");
            }
        }
        private void PolicyJson(string? text)
        {
            if (text is null) return;
            token.ThrowIfCancellationRequested();
            if (Encoding.UTF8.GetByteCount(text) > limits.MaxJsonBytes) Refuse("AgentDeclarationJsonBytesExceeded");
            try
            {
                using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = limits.MaxJsonDepth });
                Json(document.RootElement, schema: false);
            }
            catch (JsonException) { Refuse("AgentDeclarationJsonInvalid"); }
            // Exact maintained Runtime policy typing/unknown members/current intersections remain
            // the Runtime owner's validation. Structural JSON alone cannot resolve a capability.
        }
        private void Json(JsonElement element, bool schema)
        {
            token.ThrowIfCancellationRequested();
            if (element.ValueKind != JsonValueKind.Object && !(schema && element.ValueKind is JsonValueKind.True or JsonValueKind.False))
                Refuse("AgentDeclarationJsonRootInvalid");
            if (Encoding.UTF8.GetByteCount(element.GetRawText()) > limits.MaxJsonBytes) Refuse("AgentDeclarationJsonBytesExceeded");
            var nodes = 0; Visit(element, 0);
            void Visit(JsonElement current, int depth)
            {
                token.ThrowIfCancellationRequested();
                if (++nodes > limits.MaxJsonNodes || depth > limits.MaxJsonDepth) Refuse("AgentDeclarationJsonTreeExceeded");
                if (current.ValueKind == JsonValueKind.Object)
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in current.EnumerateObject())
                    {
                        if (!names.Add(property.Name)) Refuse("AgentDeclarationJsonMemberDuplicate");
                        Visit(property.Value, depth + 1);
                    }
                }
                else if (current.ValueKind == JsonValueKind.Array)
                    foreach (var child in current.EnumerateArray()) Visit(child, depth + 1);
            }
        }
    }
}
