using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Data.Sqlite;

namespace HavenOS.Apps.Assistants.Migration;

/// <summary>Legacy content adapter over the SAME protected canonical SQLite owner.
/// Construction does no I/O; ordinary reads never initialize/adopt a store or create identity.
/// Native custody and the separately verified Home legacy import receipt are both required.</summary>
public sealed partial class LegacySavedAgentSqliteSource : IHomeOriginalScopedLocalStoreEvidenceProvider,
    IResourceStoreIdentitySource, IAsyncDisposable
{
    public const string LegacyResourceKind = "legacy.saved-agents";
    private readonly CanonicalSqliteOriginalStoreOwner _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly MigrationOriginals _setupOriginals = new();
    public LegacySavedAgentSqliteSource(CanonicalSqliteOriginalStoreOwner actualStore, HomeLocalProfileIdentity actualProfiles)
    {
        ArgumentNullException.ThrowIfNull(actualStore); ArgumentNullException.ThrowIfNull(actualProfiles);
        if (!actualStore.HasOriginalProfiles(actualProfiles))
            throw new ArgumentException("Migration requires the SAME original Home profile and configured SQLite owner.");
        _store = actualStore; _profiles = actualProfiles;
    }
    public string ResourceKind => LegacyResourceKind;
    public bool IsOriginalSource(CanonicalSqliteOriginalStoreOwner store, HomeLocalProfileIdentity profiles) =>
        ReferenceEquals(store, _store) && ReferenceEquals(profiles, _profiles) && _store.HasOriginalProfiles(profiles);

    // Compatibility setup entry validates identity created by the actual normal database
    // initializer. It performs no INSERT, schema change, permission change or Home binding.
    public Task<ResourceStoreIdentity> PrepareIdentityAsync(CancellationToken token = default) =>
        GetStoreIdentityAsync(token).AsTask();
    public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token) => new(
        _setupOriginals.Admit(() => GetIdentityAsync(new(_setupOriginals.Run, _setupOriginals.Retain), token)));
    private async Task<ResourceStoreIdentity> GetIdentityAsync(Callbacks callbacks, CancellationToken token)
    {
        var actor = await CurrentActorAsync(callbacks, token).ConfigureAwait(false);
        return await callbacks.Read(() => _store.GetStoreIdentityWithinOriginalSourceAsync(actor, callbacks.Run, callbacks.Retain, token)).ConfigureAwait(false);
    }
    public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken token) => new(
        _setupOriginals.Admit(() => ReadEvidenceAsync(storeId, new(_setupOriginals.Run, _setupOriginals.Retain), token)));
    public ValueTask<HomeLocalStoreEvidence?> ReadWithinOriginalSourceAsync(string storeId,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token) =>
        new(ReadEvidenceAsync(storeId, new(originalSynchronousScope, retainOriginalTask), token));
    private async Task<HomeLocalStoreEvidence?> ReadEvidenceAsync(string storeId, Callbacks callbacks, CancellationToken token)
    {
        var actor = await CurrentActorAsync(callbacks, token).ConfigureAwait(false);
        return await WithLeaseAsync<HomeLocalStoreEvidence?>(actor, false, callbacks, async lease =>
        {
            if (lease.Actual.OriginalIdentity.StoreId.ToString("D") != storeId) return null;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long rows = 0;
            foreach (var table in new[] { "agents", "agent_runs", "knowledge_record_details", "knowledge_records" })
            {
                if (!await HasTableAsync(lease, table, token).ConfigureAwait(false)) continue;
                if ((table is "knowledge_record_details" or "knowledge_records") &&
                    !await HasColumnAsync(lease, "knowledge_record_details", "agent_id", token).ConfigureAwait(false)) continue;
                await WithCommandAsync(lease, async command =>
                {
                    command.CommandText = table switch
                    {
                        "knowledge_record_details" => "SELECT * FROM knowledge_record_details WHERE agent_id IN (SELECT id FROM agents) ORDER BY id;",
                        "knowledge_records" => "SELECT r.* FROM knowledge_records r JOIN knowledge_record_details d ON d.id=r.id WHERE d.agent_id IN (SELECT id FROM agents) ORDER BY r.id;",
                        _ => $"SELECT * FROM {table} ORDER BY id;"
                    };
                    return await WithReaderAsync(lease, command, async reader =>
                    {
                        hash.AppendData(Encoding.UTF8.GetBytes(table));
                        while (await lease.Read(() => reader.ReadAsync(token)).ConfigureAwait(false))
                        { hash.AppendData(Encoding.UTF8.GetBytes(lease.Invoke(() => ReadRawRow(reader)))); hash.AppendData([0]); rows++; }
                        return true;
                    }, token).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            await lease.RevalidateAsync(token).ConfigureAwait(false);
            return new(LegacyResourceKind, storeId, Convert.ToHexString(hash.GetHashAndReset()),
                NewlyCreated: false, IsEmpty: rows == 0, AccessibleToCurrentOsPrincipal: true);
        }, token).ConfigureAwait(false);
    }
    internal async Task<T> ReadOwnedAsync<T>(AuthenticatedResourceActor actor,
        IResourceStoreOwnershipReceiptAuthority ownership, bool reserveWriter,
        Func<OwnedLease, Task<T>> operation, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        if (ownership is not IResourceStoreOriginalScopedOwnershipAuthority scoped)
            throw new InvalidOperationException("Migration requires the actual original-scoped Home ownership authority.");
        var callbacks = new Callbacks(scope, retain);
        return await WithLeaseAsync(actor, reserveWriter, callbacks, async lease =>
        {
            var identity = lease.Actual.OriginalIdentity;
            var binding = await callbacks.Read(() => scoped.GetVerifiedWithinOriginalSourceAsync(
                LegacyResourceKind, identity.StoreId.ToString("D"), callbacks.Run, callbacks.Retain, token).AsTask()).ConfigureAwait(false);
            if (binding is null || binding.Receipt is null || binding.ResourceKind != LegacyResourceKind ||
                binding.StoreId != identity.StoreId.ToString("D") || binding.ProfileId != actor.ProfileId)
                throw new UnauthorizedAccessException("Review and import this existing Saved Agent store through Home before migration.");
            var owned = new OwnedLease(lease, identity, actor, scoped, binding);
            await owned.DemandCurrentAsync(token).ConfigureAwait(false);
            var result = await operation(owned).ConfigureAwait(false);
            await owned.DemandCurrentAsync(token).ConfigureAwait(false);
            return result;
        }, token).ConfigureAwait(false);
    }
    internal sealed record Snapshot(ResourceStoreIdentity Store, AgentDefinition Definition,
        string RawDefinitionJson, string DefinitionSha256, LegacyAgentPreservationSummary Preserved);
    internal sealed class OwnedLease(Lease lease, ResourceStoreIdentity identity, AuthenticatedResourceActor actor,
        IResourceStoreOriginalScopedOwnershipAuthority ownership, VerifiedResourceStoreOwnership binding)
    {
        internal ResourceStoreIdentity Identity => identity;
        internal async Task DemandCurrentAsync(CancellationToken token)
        {
            await lease.RevalidateAsync(token).ConfigureAwait(false);
            if (!await lease.Callbacks.Read(() => ownership.IsCurrentWithinOriginalSourceAsync(binding, actor,
                    lease.Callbacks.Run, lease.Callbacks.Retain, token).AsTask()).ConfigureAwait(false))
                throw new UnauthorizedAccessException("Saved Agent source ownership changed. Reopen migration.");
            await lease.RevalidateAsync(token).ConfigureAwait(false);
        }
        internal async Task<IReadOnlyList<AgentDefinition>> ListAsync(string? cursor, int maximum, CancellationToken token)
        {
            return await WithCommandAsync<IReadOnlyList<AgentDefinition>>(lease, async command =>
            {
                command.CommandText = "SELECT * FROM agents WHERE id > $cursor ORDER BY id LIMIT $limit;";
                command.Parameters.AddWithValue("$cursor", cursor ?? ""); command.Parameters.AddWithValue("$limit", maximum);
                return await WithReaderAsync<IReadOnlyList<AgentDefinition>>(lease, command, async reader =>
                {
                    var rows = new List<AgentDefinition>();
                    while (await lease.Read(() => reader.ReadAsync(token)).ConfigureAwait(false)) rows.Add(lease.Invoke(() => ReadDefinition(reader)));
                    return rows.AsReadOnly();
                }, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        internal async Task<Snapshot?> ReadAsync(Guid id, CancellationToken token)
        {
            AgentDefinition? definition = null; string? raw = null;
            await WithCommandAsync(lease, async command =>
            {
                command.CommandText = "SELECT * FROM agents WHERE id = $id;";
                command.Parameters.AddWithValue("$id", id.ToString("D"));
                return await WithReaderAsync(lease, command, async reader =>
                {
                    if (await lease.Read(() => reader.ReadAsync(token)).ConfigureAwait(false)) { lease.Invoke(() => { definition = ReadDefinition(reader); raw = ReadRawRow(reader); return 0; }); }
                    return true;
                }, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
            if (definition is null) return null;
            var runs = await HasTableAsync(lease, "agent_runs", token).ConfigureAwait(false);
            var memories = await HasTableAsync(lease, "knowledge_record_details", token).ConfigureAwait(false) &&
                await HasColumnAsync(lease, "knowledge_record_details", "agent_id", token).ConfigureAwait(false);
            var runCount = runs ? await CountAsync("agent_runs", id, token).ConfigureAwait(false) : 0;
            var memoryCount = memories ? await CountAsync("knowledge_record_details", id, token).ConfigureAwait(false) : 0;
            return new(identity, definition, raw!, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw!))),
                new(runCount, memoryCount, runs, memories));
        }
        private async Task<long> CountAsync(string table, Guid id, CancellationToken token)
        {
            return await WithCommandAsync(lease, async command =>
            {
                command.CommandText = $"SELECT count(*) FROM {table} WHERE agent_id=$id;";
                command.Parameters.AddWithValue("$id", id.ToString("D"));
                return Convert.ToInt64(await lease.Read(() => command.ExecuteScalarAsync(token)).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }).ConfigureAwait(false);
        }
        internal async Task<LegacyAgentLinkPage> LinksAsync(Guid id, string kind, string? cursor, int maximum, CancellationToken token)
        {
            if (kind is not ("runs" or "memories" or "references" or "conversations"))
                throw new ArgumentException("Choose runs, memories, references or conversations.", nameof(kind));
            var table = kind == "memories" ? "knowledge_record_details" : "agent_runs";
            if (!await HasTableAsync(lease, table, token).ConfigureAwait(false) ||
                !await HasColumnAsync(lease, table, "agent_id", token).ConfigureAwait(false)) return new([], null);
            var memoryContentAvailable = kind == "memories" && await HasTableAsync(lease, "knowledge_records", token).ConfigureAwait(false);
            return await WithCommandAsync(lease, async command =>
            {
                command.CommandText = kind switch
                {
                    "memories" when memoryContentAvailable => "SELECT d.id,d.agent_id,r.title FROM knowledge_record_details d JOIN knowledge_records r ON r.id=d.id WHERE d.agent_id=$id AND d.id>$cursor ORDER BY d.id LIMIT $limit;",
                    "memories" => "SELECT d.id,d.agent_id,NULL FROM knowledge_record_details d WHERE d.agent_id=$id AND d.id>$cursor ORDER BY d.id LIMIT $limit;",
                    "references" => "SELECT id,resource_reference,task FROM agent_runs WHERE agent_id=$id AND id>$cursor AND resource_reference IS NOT NULL AND resource_reference<>'' ORDER BY id LIMIT $limit;",
                    "conversations" => "SELECT id,activity_json,task FROM agent_runs WHERE agent_id=$id AND id>$cursor ORDER BY id LIMIT $limit;",
                    _ => "SELECT id,activity_json,task FROM agent_runs WHERE agent_id=$id AND id>$cursor ORDER BY id LIMIT $limit;"
                };
                command.Parameters.AddWithValue("$id", id.ToString("D"));
                command.Parameters.AddWithValue("$cursor", cursor ?? ""); command.Parameters.AddWithValue("$limit", maximum + 1);
                return await WithReaderAsync(lease, command, async reader =>
                {
                    var rows = new List<LegacyAgentLink>();
                    while (await lease.Read(() => reader.ReadAsync(token)).ConfigureAwait(false))
                    {
                        var link = lease.Invoke(() => new LegacyAgentLink(kind, reader.GetString(0),
                            reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
                        var related = link.RelatedId;
                        if (kind is "runs" or "conversations") related = ReadConversationObservation(related);
                        rows.Add(link with { RelatedId = related });
                    }
                    var next = rows.Count > maximum ? rows[maximum - 1].Id : null;
                    return new LegacyAgentLinkPage(Array.AsReadOnly(rows.Take(maximum).ToArray()), next);
                }, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
    }

    private async Task<T> WithLeaseAsync<T>(AuthenticatedResourceActor actor, bool reserveWriter,
        Callbacks callbacks, Func<Lease, Task<T>> operation, CancellationToken token)
    {
        CanonicalSqliteOriginalStoreLease? actual = null; T result = default!; var failures = new List<Exception>();
        try
        {
            await callbacks.Read(() => _store.AcquireOriginalProtectedReadWithinSourceAsync(
                actor, reserveWriter, callbacks.Run, callbacks.Retain, token), value => actual = value).ConfigureAwait(false);
            var lease = new Lease(actual!, callbacks);
            await lease.RevalidateAsync(token).ConfigureAwait(false);
            result = await operation(lease).ConfigureAwait(false);
            await lease.RevalidateAsync(token).ConfigureAwait(false);
        }
        catch (Exception failure) { failures.Add(failure); }
        if (actual is not null)
            try { await callbacks.Read(actual.CloseAndDrainAsync).ConfigureAwait(false); }
            catch (Exception failure) { failures.Add(failure); }
        MigrationOriginals.ThrowCombined(failures); return result;
    }
    private async Task<AuthenticatedResourceActor> CurrentActorAsync(Callbacks callbacks, CancellationToken token)
    {
        var actor = await callbacks.Read(() => _profiles.GetCurrentAsync(callbacks.Run, callbacks.Retain, token).AsTask()).ConfigureAwait(false);
        if (actor is null || actor.AccountId is not null || actor.OrganisationId is not null)
            throw new UnauthorizedAccessException("Saved Agent migration requires the current personal Home profile.");
        return actor;
    }
    internal sealed class Callbacks(Action<Action> scope, Action<Task> retain)
    {
        internal void Run(Action body)
        {
            var active = true; var used = false; var thread = Environment.CurrentManagedThreadId;
            try
            {
                scope(() =>
                {
                    if (!active || used || thread != Environment.CurrentManagedThreadId)
                        throw new InvalidOperationException("The original migration source callback is inactive, repeated or on a foreign thread.");
                    used = true; body();
                });
                if (!used) throw new InvalidOperationException("The original migration source callback was not invoked.");
            }
            finally { active = false; }
        }
        internal void Retain(Task actual) => retain(actual);
        internal async Task<T> Read<T>(Func<Task<T>> factory, Action<T>? capture = null)
        {
            Task<T>? actual = null; T result = default!; var failures = new List<Exception>();
            try { Run(() => { actual = factory(); Retain(actual); }); }
            catch (Exception failure) { failures.Add(failure); }
            if (actual is not null)
                try { result = await actual.ConfigureAwait(false); capture?.Invoke(result); }
                catch (Exception failure) { failures.Add(failure); }
            MigrationOriginals.ThrowCombined(failures);
            return actual is null ? throw new InvalidOperationException("The actual source callback was not invoked.") : result;
        }
        internal async Task Read(Func<Task> factory)
        {
            Task? actual = null; var failures = new List<Exception>();
            try { Run(() => { actual = factory(); Retain(actual); }); }
            catch (Exception failure) { failures.Add(failure); }
            if (actual is not null) try { await actual.ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
            MigrationOriginals.ThrowCombined(failures);
            if (actual is null) throw new InvalidOperationException("The actual source callback was not invoked.");
        }
    }
    internal sealed class Lease(CanonicalSqliteOriginalStoreLease actual, Callbacks callbacks)
    {
        internal CanonicalSqliteOriginalStoreLease Actual => actual;
        internal Callbacks Callbacks => callbacks;
        internal T Invoke<T>(Func<T> source) => actual.InvokeOriginalSource(source);
        internal Task RevalidateAsync(CancellationToken token) => callbacks.Read(() =>
            actual.RevalidateWithinSourceAsync(callbacks.Run, callbacks.Retain, token));
        internal async Task<T> Read<T>(Func<Task<T>> factory, Action<T>? capture = null)
        {
            Task<T>? raw = null; T result = default!; var failures = new List<Exception>();
            try { result = await callbacks.Read(() => actual.ReadOriginalSourceAsync(() => raw = factory())).ConfigureAwait(false); }
            catch (Exception failure) { failures.Add(failure); }
            // A reader may arrive after a caller postguard failed. Capture its original result
            // before rethrowing so its owning finally can settle the actual resource.
            if (raw is { IsCompletedSuccessfully: true }) { result = raw.Result; capture?.Invoke(result); }
            MigrationOriginals.ThrowCombined(failures); return result;
        }
        internal Task CloseResourceAsync(IAsyncDisposable resource) =>
            callbacks.Read(() => actual.CloseOriginalResourceAsync(resource));
    }

    private static async Task<bool> HasTableAsync(Lease lease, string name, CancellationToken token)
    {
        return await WithCommandAsync(lease, async command =>
        {
            command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$name;";
            command.Parameters.AddWithValue("$name", name);
            return Convert.ToInt64(await lease.Read(() => command.ExecuteScalarAsync(token)).ConfigureAwait(false), CultureInfo.InvariantCulture) == 1;
        }).ConfigureAwait(false);
    }
    private static async Task<bool> HasColumnAsync(Lease lease, string table, string column, CancellationToken token)
    {
        return await WithCommandAsync(lease, async command =>
        {
            command.CommandText = "SELECT count(*) FROM pragma_table_info($table) WHERE name=$column;";
            command.Parameters.AddWithValue("$table", table); command.Parameters.AddWithValue("$column", column);
            return Convert.ToInt64(await lease.Read(() => command.ExecuteScalarAsync(token)).ConfigureAwait(false), CultureInfo.InvariantCulture) == 1;
        }).ConfigureAwait(false);
    }
    private static async Task<T> WithCommandAsync<T>(Lease lease, Func<SqliteCommand, Task<T>> body)
    {
        SqliteCommand? command = null; T result = default!; var failures = new List<Exception>();
        try
        {
            lease.Invoke(() => { command = lease.Actual.Connection.CreateCommand(); return command; });
            lease.Invoke(() => { command!.Transaction = lease.Actual.Transaction; return 0; });
            Task<T>? original = null;
            try { lease.Invoke(() => { original = body(command!); lease.Callbacks.Retain(original); return 0; }); }
            catch (Exception failure) { failures.Add(failure); }
            if (original is not null) try { result = await original.ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        }
        catch (Exception failure) { failures.Add(failure); }
        if (command is not null) try { await lease.CloseResourceAsync(command).ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        MigrationOriginals.ThrowCombined(failures); return result;
    }
    private static async Task<T> WithReaderAsync<T>(Lease lease, SqliteCommand command,
        Func<SqliteDataReader, Task<T>> body, CancellationToken token)
    {
        SqliteDataReader? reader = null; T result = default!; var failures = new List<Exception>();
        try
        {
            await lease.Read(() => command.ExecuteReaderAsync(token), value => reader = value).ConfigureAwait(false);
            result = await body(reader!).ConfigureAwait(false);
        }
        catch (Exception failure) { failures.Add(failure); }
        if (reader is not null) try { await lease.CloseResourceAsync(reader).ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        MigrationOriginals.ThrowCombined(failures); return result;
    }
    public void RequestRetirement() => _setupOriginals.RequestRetirement();
    public Task? OriginalClose => _setupOriginals.OriginalClose;
    public Task CloseAndDrainAsync() => _setupOriginals.CloseAndDrainAsync();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private static AgentDefinition ReadDefinition(SqliteDataReader reader)
    {
        string Text(string key) => reader.GetString(reader.GetOrdinal(key));
        var fallback = reader.GetOrdinal("fallback_model");
        return new(Guid.Parse(Text("id")), Text("name"), Text("description"), Text("instructions"), Text("icon_key"),
            Text("preferred_model"), reader.IsDBNull(fallback) ? null : reader.GetString(fallback), Text("detection_rules"),
            Text("permissions_json"), reader.GetInt64(reader.GetOrdinal("is_built_in")) != 0,
            reader.GetInt64(reader.GetOrdinal("is_enabled")) != 0,
            DateTimeOffset.Parse(Text("updated_at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }
    private static string ReadRawRow(SqliteDataReader reader)
    {
        var columns = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < reader.FieldCount; index++)
            columns.Add(reader.GetName(index), reader.IsDBNull(index) ? null : reader.GetValue(index));
        return JsonSerializer.Serialize(columns);
    }
    private static string? ReadConversationObservation(string? activityJson)
    {
        if (activityJson is null) return null;
        try
        {
            using var json = JsonDocument.Parse(activityJson);
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("CanonicalBindingVersion", out var version) || !version.TryGetInt32(out var number) || number != 1 ||
                !json.RootElement.TryGetProperty("CanonicalTask", out var binding) || binding.ValueKind != JsonValueKind.Object) return null;
            var observation = binding.Deserialize<AgentRunCanonicalBinding>();
            return observation is not null && observation.TaskId != Guid.Empty && observation.ContextId != Guid.Empty &&
                observation.ExecutionId != Guid.Empty && observation.PersistenceRevision > 0 && Enum.IsDefined(observation.State)
                ? observation.ContextId.ToString("D") : null;
        }
        catch (JsonException) { return null; } // Unknown legacy activity remains in its owning original row.
    }
}
