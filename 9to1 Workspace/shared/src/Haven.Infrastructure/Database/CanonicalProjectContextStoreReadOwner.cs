using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>Bounded saved Tasks/Studio metadata behind the actual canonical SQLite Home store
/// receipt. Den ownership, configuration IDs and paths never substitute for that receipt.
/// Project selection/content/effect permission remains the separate Home/Files READ path.</summary>
public sealed class CanonicalProjectContextStoreReadOwner : ICanonicalProjectContextStoreReadSource,
    IHomeOriginalScopedLocalStoreEvidenceProvider, IResourceStoreIdentitySource
{
    public const string CanonicalResourceKind = "canonical.sqlite";
    private readonly CanonicalSqliteOriginalStoreOwner _store;
    private readonly ConversationRepository _conversations;
    private readonly ContainerRepository _containers;
    private readonly HomeResourceStoreOwnershipAuthority _ownership;
    private readonly CanonicalSqliteOriginalStoreEvidenceProvider _evidence;
    private readonly ConditionalWeakTable<ICanonicalProjectContextStoreObservation, Observation> _issued = new();
    private readonly ConditionalWeakTable<Task, OriginalReadMarker> _readCommands = new();
    private readonly ConditionalWeakTable<ICanonicalProjectContextStoreContinuation, Continuation> _continuations = new();
    private sealed class OriginalReadMarker { }
    public string ResourceKind => CanonicalResourceKind;
    public CanonicalSqliteOriginalStoreOwner OriginalStoreOwner => _store;
    public ConversationRepository OriginalConversations => _conversations;
    public ContainerRepository OriginalContainers => _containers;
    public HomeResourceStoreOwnershipAuthority OriginalOwnershipAuthority => _ownership;

    public CanonicalProjectContextStoreReadOwner(CanonicalSqliteOriginalStoreOwner actualStore,
        SqliteDatabase actualDatabase, ConversationRepository actualConversations,
        ContainerRepository actualContainers, HomeResourceStoreOwnershipAuthority actualOwnership)
    {
        ArgumentNullException.ThrowIfNull(actualStore); ArgumentNullException.ThrowIfNull(actualDatabase);
        ArgumentNullException.ThrowIfNull(actualConversations); ArgumentNullException.ThrowIfNull(actualContainers);
        ArgumentNullException.ThrowIfNull(actualOwnership);
        if (!actualStore.HasOriginalDatabase(actualDatabase) ||
            !actualConversations.HasOriginalSqliteFactory(actualDatabase) ||
            !actualContainers.HasOriginalSqliteFactory(actualDatabase))
            throw new InvalidOperationException("The SAME actual configured SQLite repository factory is required.");
        _store = actualStore; _conversations = actualConversations; _containers = actualContainers; _ownership = actualOwnership;
        _evidence = new(actualStore, CanonicalResourceKind);
    }
    public bool HasOriginalComposition(CanonicalSqliteOriginalStoreOwner sameStore,
        ConversationRepository sameConversations, ContainerRepository sameContainers,
        HomeResourceStoreOwnershipAuthority sameOwnership) => ReferenceEquals(_store, sameStore) &&
        ReferenceEquals(_conversations, sameConversations) && ReferenceEquals(_containers, sameContainers) &&
        ReferenceEquals(_ownership, sameOwnership);

    // One immutable logical identity; separate Home kinds remain separate grants.
    public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token) => _store.GetStoreIdentityAsync(token);
    public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken token) => _evidence.ReadAsync(storeId, token);
    public ValueTask<HomeLocalStoreEvidence?> ReadWithinOriginalSourceAsync(string storeId,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => _evidence.ReadWithinOriginalSourceAsync(storeId, scope, retain, token);
    private sealed class Observation(CanonicalProjectContextStoreReadOwner owner,
        AuthenticatedResourceActor actor, ResourceStoreIdentity identity, VerifiedResourceStoreOwnership ownership,
        IReadOnlyList<Conversation> conversations, IReadOnlyList<ContainerDefinition> containers,
        bool hasMore, int maximum, Guid? selectedId, bool includeStudio, PageQuery? pageQuery,
        Continuation? nextContinuation) : ICanonicalProjectContextStoreObservation
    {
        internal readonly CanonicalProjectContextStoreReadOwner Owner = owner;
        internal readonly int Maximum = maximum;
        internal readonly Guid? SelectedId = selectedId;
        internal readonly bool IncludeStudio = includeStudio;
        internal readonly PageQuery? Query = pageQuery;
        internal readonly string Snapshot = JsonSerializer.Serialize(new { conversations, containers, hasMore });
        public AuthenticatedResourceActor Actor { get; } = actor;
        public ResourceStoreIdentity OriginalStoreIdentity { get; } = identity;
        public VerifiedResourceStoreOwnership OriginalStoreOwnership { get; } = ownership;
        public IReadOnlyList<Conversation> Conversations { get; } = conversations;
        public IReadOnlyList<ContainerDefinition> Containers { get; } = containers;
        public bool HasMore { get; } = hasMore;
        public ICanonicalProjectContextStoreContinuation? NextContinuation { get; } = nextContinuation;
    }
    // Preserve the database's actual persisted TEXT ordering. Reformatting a parsed
    // DateTimeOffset or Guid would change historical precision/offset/identifier TEXT positions.
    private sealed record Position(string UpdatedAt, string ConversationId);
    private sealed record PageQuery(Position? After, string? Search);
    private sealed class Continuation(CanonicalProjectContextStoreReadOwner owner,
        AuthenticatedResourceActor actor, ResourceStoreIdentity identity,
        VerifiedResourceStoreOwnership ownership, int maximum, bool includeStudio, PageQuery query)
        : ICanonicalProjectContextStoreContinuation
    {
        internal readonly CanonicalProjectContextStoreReadOwner Owner = owner;
        internal readonly AuthenticatedResourceActor Actor = actor;
        internal readonly ResourceStoreIdentity Identity = identity;
        internal readonly VerifiedResourceStoreOwnership Ownership = ownership;
        internal readonly int Maximum = maximum;
        internal readonly bool IncludeStudio = includeStudio;
        internal readonly PageQuery Query = query;
    }
    public bool IsIssuedOriginalContinuation(ICanonicalProjectContextStoreContinuation continuation) =>
        continuation is Continuation actual && ReferenceEquals(actual.Owner, this) &&
        _continuations.TryGetValue(continuation, out var same) && ReferenceEquals(actual, same);

    public bool IsAcknowledgedOriginalReadRefusal(Task sameOriginalRead) =>
        _readCommands.TryGetValue(sameOriginalRead, out _) && _store.IsAcknowledgedOriginalCommandRefusal(sameOriginalRead);
    public bool IsIssuedOriginalObservation(ICanonicalProjectContextStoreObservation observation) =>
        observation is Observation actual && ReferenceEquals(actual.Owner, this) &&
        _issued.TryGetValue(observation, out var same) && ReferenceEquals(actual, same);

    public Task<ICanonicalProjectContextStoreObservation> ReadOriginalProjectContextsWithinSourceAsync(
        AuthenticatedResourceActor actualActor, int maximum, Action<Action> scope, Action<Task> retain,
        CancellationToken token, Guid? originalConversationId = null, bool includeStudioContexts = false)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, scope, retain);
        var original = _store.RetainOriginalReader(source, () => ReadCoreAsync(actualActor, maximum, source, token, originalConversationId, includeStudioContexts));
        _readCommands.Add(original, new()); return original;
    }
    public Task<ICanonicalProjectContextStoreObservation> ReadOriginalProjectContextPageWithinSourceAsync(
        AuthenticatedResourceActor actualActor, int maximum, Action<Action> scope, Action<Task> retain,
        CancellationToken token, ICanonicalProjectContextStoreContinuation? continuation = null,
        bool includeStudioContexts = false, string? searchText = null)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, scope, retain);
        var original = _store.RetainOriginalReader(source, async () =>
        {
            string? search = null, declined = null; Continuation? cursor = null;
            source.Run(() =>
            {
                search = string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim();
                if (search?.Length > 256) throw new ArgumentOutOfRangeException(nameof(searchText));
                if (continuation is null) return;
                if (!IsIssuedOriginalContinuation(continuation))
                {
                    declined = "Refresh the project catalogue: this source did not issue that continuation."; return;
                }
                cursor = (Continuation)continuation;
                if (cursor.Actor != actualActor || cursor.Maximum != maximum ||
                    cursor.IncludeStudio != includeStudioContexts || cursor.Query.Search != search)
                    declined = "Refresh the project catalogue after changing its actor, page size or search.";
            });
            // The caller's actual admission/publication callback must independently settle
            // before a semantic cursor refusal can be acknowledged. Its fault is not waived.
            if (declined is not null) await RefusePageAsync(source, declined).ConfigureAwait(false);
            var query = cursor?.Query ?? new PageQuery(null, search);
            return await ReadCoreAsync(actualActor, maximum, source, token, null,
                includeStudioContexts, query, cursor).ConfigureAwait(false);
        });
        _readCommands.Add(original, new()); return original;
    }
    private static async Task RefusePageAsync(CanonicalSqliteOriginalSourceScope source, string reason)
    {
        // Only this exact issued, independently healthy pre-metadata command may be
        // acknowledged. Foreign groups, callbacks and another Task alias stay faults.
        await source.JoinAllAsync().ConfigureAwait(false);
        var refusal = new InvalidOperationException(reason);
        source.RegisterOriginalPreEffectRefusal(refusal); throw refusal;
    }
    private async Task<ICanonicalProjectContextStoreObservation> ReadCoreAsync(AuthenticatedResourceActor actualActor,
        int maximum, CanonicalSqliteOriginalSourceScope source, CancellationToken token, Guid? originalConversationId, bool includeStudio,
        PageQuery? pageQuery = null, Continuation? originalCursor = null)
    {
        if (maximum is < 1 or > 64 || originalConversationId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(maximum), "Choose 1–64 actual current project contexts or one known canonical ID.");
        // Physical custody does not grant metadata: capture the actual identity, then
        // obtain the separately authorized canonical-store receipt BEFORE any title query.
        var identity = await source.Read(() => _store.GetStoreIdentityWithinOriginalSourceAsync(actualActor,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        var ownership = await source.Read(() => _ownership.GetVerifiedWithinOriginalSourceAsync(CanonicalResourceKind,
            identity.StoreId.ToString("D"), source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
        await DemandReceiptAsync(source, ownership, actualActor, identity, token).ConfigureAwait(false);
        if (originalCursor is not null && (originalCursor.Identity != identity || originalCursor.Ownership != ownership))
            await RefusePageAsync(source, "The actual canonical store identity or Home READ receipt changed. Refresh the catalogue.").ConfigureAwait(false);
        CanonicalSqliteOriginalStoreLease? lease = null;
        var errors = new List<Exception>(); Observation? observed = null;
        try
        {
            await source.ReadCapture(() => _store.AcquireOriginalProtectedReadWithinSourceAsync(actualActor,
                false, source.Run, source.Retain, token), value => lease = value).ConfigureAwait(false);
            if (lease!.OriginalIdentity != identity || !_store.IsIssuedOriginalLease(lease))
                throw new UnauthorizedAccessException("The actual protected SQLite issuer/identity changed.");
            await DemandReceiptAsync(source, ownership, actualActor, identity, token).ConfigureAwait(false);
            var window = await ReadConversationWindowAsync(lease, maximum + 1, originalConversationId, includeStudio, pageQuery, token).ConfigureAwait(false);
            var hasMore = window.Rows.Count > maximum;
            var selected = Array.AsReadOnly(window.Rows.Take(maximum).ToArray());
            var containers = await ReadContainersAsync(lease, selected.Select(value => value.ContainerId!.Value).Distinct().ToArray(), token).ConfigureAwait(false);
            await lease.RevalidateWithinSourceAsync(source.Run, source.Retain, token).ConfigureAwait(false);
            await DemandReceiptAsync(source, ownership, actualActor, identity, token).ConfigureAwait(false);
            Continuation? next = pageQuery is not null && hasMore
                ? new(this, actualActor, identity, ownership!, maximum, includeStudio,
                    new PageQuery(window.Positions[maximum - 1], pageQuery.Search)) : null;
            observed = new(this, actualActor, identity, ownership!, selected, containers, hasMore, maximum,
                originalConversationId, includeStudio, pageQuery, next);
        }
        catch (Exception cause) { errors.Add(cause); }
        if (lease is not null) try { await lease.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
        if (observed is null) throw new InvalidOperationException("No actual canonical store observation was captured.");
        // Issuance follows healthy original SQL/native close and the final actual Home check.
        var currentIdentity = await source.Read(() => _store.GetStoreIdentityWithinOriginalSourceAsync(actualActor,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        if (currentIdentity != identity) throw new UnauthorizedAccessException("The actual SQLite identity changed before metadata publication.");
        await DemandReceiptAsync(source, ownership, actualActor, identity, token).ConfigureAwait(false);
        await source.JoinAllAsync().ConfigureAwait(false);
        source.Run(() =>
        {
            _issued.Add(observed, observed);
            if (observed.NextContinuation is Continuation next) _continuations.Add(next, next);
        }); return observed;
    }
    public Task RevalidateOriginalObservationWithinSourceAsync(ICanonicalProjectContextStoreObservation observation,
        AuthenticatedResourceActor actualActor, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, scope, retain);
        var originalRead = _store.RetainOriginalReader(source, async () =>
        {
        if (!IsIssuedOriginalObservation(observation) || observation.Actor != actualActor)
            throw new UnauthorizedAccessException("The SAME configured store did not issue this actor's actual metadata observation.");
        var original = (Observation)observation;
        var fresh = (Observation)await ReadCoreAsync(actualActor, original.Maximum,
            source, token, original.SelectedId, original.IncludeStudio, original.Query).ConfigureAwait(false);
        if (fresh.OriginalStoreIdentity != original.OriginalStoreIdentity ||
            fresh.OriginalStoreOwnership != original.OriginalStoreOwnership || fresh.Snapshot != original.Snapshot)
            throw new UnauthorizedAccessException("The actual canonical store ownership/context metadata changed. Refresh the project catalogue.");
        return 0;
        });
        _readCommands.Add(originalRead, new()); return originalRead;
    }
    private async Task DemandReceiptAsync(CanonicalSqliteOriginalSourceScope source,
        VerifiedResourceStoreOwnership? binding, AuthenticatedResourceActor actor,
        ResourceStoreIdentity identity, CancellationToken token)
    {
        if (binding is null)
        {
            // The actual owning Home query returned no binding after independently joined
            // healthy identity/native sources. This is a privately issued setup refusal,
            // not a waiver for failed I/O, changed receipts, callbacks or caller exceptions.
            await source.JoinAllAsync().ConfigureAwait(false);
            var refusal = new InvalidOperationException("Import the actual canonical SQLite store through Home before reading project titles.");
            source.RegisterOriginalPreEffectRefusal(refusal); throw refusal;
        }
        if (binding.Receipt is null || binding.ResourceKind != CanonicalResourceKind ||
            binding.StoreId != identity.StoreId.ToString("D") || binding.ProfileId != actor.ProfileId ||
            !await source.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(binding, actor,
                source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Explicit current Home ownership import of the canonical SQLite store is required before project metadata READ.");
    }
    internal static async Task<IReadOnlyList<Conversation>> ReadConversationsAsync(CanonicalSqliteOriginalStoreLease lease,
        int limit, Guid? id, bool includeStudio, CancellationToken token) =>
        (await ReadConversationWindowAsync(lease, limit, id, includeStudio, null, token).ConfigureAwait(false)).Rows;
    private sealed record ConversationWindow(IReadOnlyList<Conversation> Rows, IReadOnlyList<Position> Positions);
    private static async Task<ConversationWindow> ReadConversationWindowAsync(CanonicalSqliteOriginalStoreLease lease,
        int limit, Guid? id, bool includeStudio, PageQuery? query, CancellationToken token)
    {
        var command = lease.CreateOriginalCommand(); SqliteDataReader? reader = null;
        var rows = new List<Conversation>(); var positions = new List<Position>(); var errors = new List<Exception>();
        try
        {
            lease.InvokeOriginalSource(() =>
            {
                command.CommandText = """
                    SELECT c.* FROM conversations c JOIN containers x ON x.id=c.container_id
                    WHERE c.is_temporary=0 AND c.is_archived=0 AND c.space_id IS NULL AND c.lesson_id IS NULL
                      AND x.mode=c.mode AND x.is_archived=0
                    """ + (id is not null || includeStudio
                        ? " AND ((c.mode=$mode AND c.kind=$kind) OR (c.mode=$studioMode AND c.kind=$studioKind))"
                        : " AND c.mode=$mode AND c.kind=$kind")
                    + (id is not null ? " AND c.id=$id" : "")
                    + (query?.Search is not null ? " AND (c.title LIKE $search ESCAPE '\\' OR x.name LIKE $search ESCAPE '\\')" : "")
                    + (query?.After is not null ? " AND (c.updated_at<$after OR (c.updated_at=$after AND c.id>$afterId))" : "")
                    + " ORDER BY c.updated_at DESC,c.id ASC LIMIT $limit;";
                command.Parameters.AddWithValue("$mode", (int)HavenMode.Tasks);
                command.Parameters.AddWithValue("$kind", (int)ConversationKind.Task);
                command.Parameters.AddWithValue("$limit", limit);
                if (id is not null) command.Parameters.AddWithValue("$id", id.Value.ToString("D"));
                if (id is not null || includeStudio)
                {
                    command.Parameters.AddWithValue("$studioMode", (int)HavenMode.Studio);
                    command.Parameters.AddWithValue("$studioKind", (int)ConversationKind.StudioChat);
                }
                if (query?.Search is string search)
                    command.Parameters.AddWithValue("$search", "%" + search.Replace("\\", "\\\\", StringComparison.Ordinal)
                        .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%");
                if (query?.After is Position after)
                {
                    command.Parameters.AddWithValue("$after", after.UpdatedAt);
                    command.Parameters.AddWithValue("$afterId", after.ConversationId);
                }
            });
            reader = await lease.ReadOriginalSourceAsync(() => command.ExecuteReaderAsync(token)).ConfigureAwait(false);
            while (await lease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false))
                lease.InvokeOriginalSource(() =>
                {
                    var row = ConversationRepository.MapOriginalDatabaseRow(reader); rows.Add(row);
                    positions.Add(new(reader.GetString(reader.GetOrdinal("updated_at")), reader.GetString(reader.GetOrdinal("id"))));
                });
        }
        catch (Exception cause) { errors.Add(cause); }
        if (reader is not null) try { await lease.CloseOriginalSourceAsync(reader).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        try { await lease.CloseOriginalSourceAsync(command).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
        return new(Array.AsReadOnly(rows.ToArray()), Array.AsReadOnly(positions.ToArray()));
    }
    internal static async Task<IReadOnlyList<ContainerDefinition>> ReadContainersAsync(CanonicalSqliteOriginalStoreLease lease,
        Guid[] ids, CancellationToken token)
    {
        if (ids.Length == 0) return [];
        var command = lease.CreateOriginalCommand(); SqliteDataReader? reader = null;
        var rows = new List<ContainerDefinition>(); var errors = new List<Exception>();
        try
        {
            lease.InvokeOriginalSource(() =>
            {
                var names = ids.Select((id, index) => "$id" + index).ToArray();
                command.CommandText = "SELECT * FROM containers WHERE mode IN ($mode,$studioMode) AND is_archived=0 AND id IN (" + string.Join(',', names) + ") ORDER BY id;";
                command.Parameters.AddWithValue("$mode", (int)HavenMode.Tasks);
                command.Parameters.AddWithValue("$studioMode", (int)HavenMode.Studio);
                for (var index = 0; index < ids.Length; index++) command.Parameters.AddWithValue(names[index], ids[index].ToString("D"));
            });
            reader = await lease.ReadOriginalSourceAsync(() => command.ExecuteReaderAsync(token)).ConfigureAwait(false);
            while (await lease.ReadOriginalSourceAsync(() => reader.ReadAsync(token)).ConfigureAwait(false))
                rows.Add(lease.InvokeOriginalSource(() => ContainerRepository.MapOriginalDatabaseRow(reader)));
        }
        catch (Exception cause) { errors.Add(cause); }
        if (reader is not null) try { await lease.CloseOriginalSourceAsync(reader).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        try { await lease.CloseOriginalSourceAsync(command).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors); return Array.AsReadOnly(rows.ToArray());
    }
    private static string Fingerprint(ResourceStoreIdentity identity) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity)));
}
