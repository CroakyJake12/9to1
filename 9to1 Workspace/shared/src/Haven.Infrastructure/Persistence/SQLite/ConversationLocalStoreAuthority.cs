using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>Evidence from the canonical SQL root and its existing conversation tables. Observation
/// never binds/imports ownership. Message content is hashed locally, never exposed in Home metadata.</summary>
public sealed class ConversationLocalStoreEvidenceProvider(SqliteDatabase database) : IHomeLocalStoreEvidenceProvider
{
    public string ResourceKind => "conversation";
    public async ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken ct)
    {
        var identity = await database.GetStoreIdentityAsync(ct).ConfigureAwait(false);
        if (identity.StoreId.ToString("D") != storeId || identity.SchemaVersion != 1) return null;
        await using var connection = await database.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: true);
        var current = await SqliteDatabase.ReadStoreIdentityAsync(connection, false, ct, transaction).ConfigureAwait(false);
        if (current.StoreId != identity.StoreId) return null;
        var tables = new List<string>();
        await using (var names = connection.CreateCommand())
        {
            names.Transaction = transaction;
            names.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
            await using var reader = await names.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var name = reader.GetString(0);
                if (name.StartsWith("conversation", StringComparison.Ordinal) || name.StartsWith("message", StringComparison.Ordinal) ||
                    name is "response_usage" or "shared_sessions") tables.Add(name);
            }
        }
        if (tables.Count > 64) throw new InvalidDataException("Too many conversation tables for ownership observation.");
        if (!tables.Contains("conversations", StringComparer.Ordinal) || !tables.Contains("messages", StringComparer.Ordinal)) return null;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(storeId));
        long totalBytes = 0; var rowCount = 0;
        foreach (var table in tables)
        {
            hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(table));
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"", StringComparison.Ordinal) + "\" ORDER BY rowid;";
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (reader.FieldCount > 128) throw new InvalidDataException("Conversation table exceeds the supported observation width.");
            hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray()));
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (++rowCount > 100000) throw new InvalidDataException("Conversation ownership observation exceeds its bounded snapshot.");
                long rawRowBytes = 0;
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    if (reader.IsDBNull(i)) continue;
                    var type = reader.GetFieldType(i);
                    var length = type == typeof(string) ? reader.GetChars(i, 0, null, 0, 0) :
                        type == typeof(byte[]) ? reader.GetBytes(i, 0, null, 0, 0) : 8;
                    if (length > 512 * 1024) throw new InvalidDataException("Conversation value exceeds bounded ownership observation.");
                    rawRowBytes += type == typeof(string) ? length * 2 : length;
                    if (rawRowBytes > 1024 * 1024) throw new InvalidDataException("Conversation row exceeds bounded ownership observation.");
                }
                var values = Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i)).ToArray();
                var bytes = JsonSerializer.SerializeToUtf8Bytes(values);
                try
                {
                    totalBytes += bytes.Length;
                    if (totalBytes > 64 * 1024 * 1024) throw new InvalidDataException("Conversation ownership observation exceeds its bounded snapshot.");
                    hash.AppendData(bytes);
                }
                finally { Array.Clear(bytes); }
            }
        }
        return new(ResourceKind, storeId, Convert.ToHexString(hash.GetHashAndReset()), identity.NewlyCreated, rowCount == 0, true);
    }
}

/// <summary>Captures current personal conversation ownership outside SQL. The returned admission
/// checks only raw Home receipts and the supplied owning Space guard under the SQL transaction.</summary>
public sealed class ConversationLocalStoreAuthority(IAuthenticatedResourceActorSource actors,
    IResourceStoreOwnershipAuthority ownership)
{
    public async ValueTask<IConversationSpaceCommitAdmission?> CaptureAsync(AuthenticatedResourceActor expectedActor,
        Guid storeId, IReadOnlyList<ConversationSpaceChange> changes, IConversationSpaceCommitAdmission owningSpaceAdmission,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expectedActor); ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(owningSpaceAdmission);
        var captured = Array.AsReadOnly(changes.ToArray());
        if (storeId == Guid.Empty || captured.Count is < 1 or > 1000 ||
            expectedActor.AccountId is not null || expectedActor.OrganisationId is not null ||
            ownership is not IResourceStoreOwnershipReceiptAuthority receipts ||
            await actors.GetCurrentAsync(ct).ConfigureAwait(false) != expectedActor) return null;
        var binding = await ownership.GetVerifiedAsync("conversation", storeId.ToString("D"), ct).ConfigureAwait(false);
        if (binding?.Receipt is null || binding.ResourceKind != "conversation" || binding.StoreId != storeId.ToString("D") ||
            binding.ProfileId != expectedActor.ProfileId || !await receipts.IsCurrentAsync(binding, expectedActor, ct).ConfigureAwait(false)) return null;
        return new Admission(storeId, captured, expectedActor, binding, receipts, owningSpaceAdmission);
    }
    private sealed class Admission(Guid storeId, IReadOnlyList<ConversationSpaceChange> changes,
        AuthenticatedResourceActor actor, VerifiedResourceStoreOwnership binding,
        IResourceStoreOwnershipReceiptAuthority receipts, IConversationSpaceCommitAdmission space) : IConversationSpaceCommitAdmission
    {
        public async ValueTask<bool> CheckAsync(ConversationSpaceCommitContext context, CancellationToken ct) =>
            context.StoreIdentity.SchemaVersion == 1 && context.StoreIdentity.StoreId == storeId && context.Changes.SequenceEqual(changes) &&
            await receipts.IsCurrentAsync(binding, actor, ct).ConfigureAwait(false) &&
            await space.CheckAsync(context, ct).ConfigureAwait(false) &&
            await receipts.IsCurrentAsync(binding, actor, ct).ConfigureAwait(false);
    }
}
