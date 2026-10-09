using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using NineToOne.Dulche.Den;

namespace HavenOS.Apps.Assistants.Migration;

public sealed partial class LegacyAgentMigrationController
{
    internal sealed record OriginalMemoryLineage(Guid AgentId, string SourceSha256, string SourceJson, string ReceiptSha256);

    // Read the SAME maintained migration receipt and canonical classification format.
    // Later ordinary configuration edits may keep the origin, but the caller must bind
    // and validate their exact current definition revision separately on every read.
    internal static OriginalMemoryLineage? ReadOriginalMemoryLineage(AgentDefinitionRecord current,
        Guid actualStoreId, string actualProfileId)
    {
        var migration = ReadMigration(current);
        var original = ReadMetadata<AgentDefinitionRecord>(current, OriginalDefinitionKey);
        var configured = ReadMetadata<AssistantCanonicalMembershipSource.DefinitionMetadata>(current, DefinitionKey);
        if (migration is not { Schema: 1, State: LegacyAgentMigrationState.Staged, Kind: ConfiguredIdentityKind.Assistant } ||
            original is null || configured is not { Schema: 1, Kind: ConfiguredIdentityKind.Assistant } ||
            migration.OperationId == Guid.Empty || configured.CreationOperation != migration.OperationId ||
            migration.SourceStoreId != actualStoreId || migration.ProfileId != actualProfileId ||
            migration.LegacyAgentId == Guid.Empty || migration.LegacyAgentId.ToString("D") != current.Id ||
            current.NamespaceId != Namespace || original.NamespaceId != current.NamespaceId || original.Id != current.Id ||
            migration.StagedRevision < 1 || original.Revision != migration.StagedRevision || current.Revision <= original.Revision ||
            original.ExtensionData?.ContainsKey(DefinitionKey) == true ||
            Serialize(ReadMigration(original)) != Serialize(migration) ||
            string.IsNullOrWhiteSpace(migration.OriginalDefinitionJson) ||
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(migration.OriginalDefinitionJson))) != migration.SourceDefinitionSha256)
            return null;
        using var saved = JsonDocument.Parse(migration.OriginalDefinitionJson);
        if (!saved.RootElement.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
            id.GetString() != current.Id) return null;
        return new(migration.LegacyAgentId, migration.SourceDefinitionSha256, migration.OriginalDefinitionJson,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(new { Migration = migration, Original = original })))));
    }
}
