using System.Text.Json;
using Haven.Core.Shelf;

namespace Haven.Application.Shelf;

public sealed record ShelfSnapshot(ShelfLibrary Library, IReadOnlyList<Guid> ArchivedItemIds,
    IReadOnlyList<Guid> ArchivedCollectionIds)
{
    public ShelfOwnedMutationReceipt? LastOwnedMutation { get; init; }
    public static ShelfSnapshot Empty { get; } = new(ShelfLibrary.Empty, [], []);
}

public sealed record ShelfOperationResult(bool Success, string? ErrorCode, string? Message, ShelfSnapshot? Snapshot);

/// <summary>
/// Canonical persisted Shelf organisation. Both UI and authorised automation use these operations;
/// target opening remains the responsibility of the owning application/platform service.
/// Register one instance per user profile so all surfaces share its revision gate.
/// </summary>
public sealed class ShelfLibraryService(IVersionedSettingsStore settings) : IResourceStoreIdentitySource
{
    private const string Key = "shelf.library.v1";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IVersionedSettingsStore _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token) =>
        _settings is IResourceStoreIdentitySource identities ? identities.GetStoreIdentityAsync(token)
        : ValueTask.FromException<ResourceStoreIdentity>(new UnauthorizedAccessException("Actual Shelf settings identity unavailable."));

    public Task<ShelfOperationResult> AddItemAsync(long revision, ShelfLaunchItem item, ISettingsCommitAdmission admission, ShelfOwnedMutationReceipt receipt, CancellationToken token)
    {
        var captured = Capture(item);
        return MutateAsync(revision, state => state with { Library = ShelfLibraryPolicy.AddOrRefreshTarget(state.Library, captured) }, token, admission, receipt);
    }

    public Task<ShelfOperationResult> CreateCollectionAsync(long revision, ShelfCollection collection,
        ISettingsCommitAdmission admission, ShelfOwnedMutationReceipt receipt, CancellationToken token)
    {
        var captured = Capture(collection);
        return MutateAsync(revision, state => state with { Library = ShelfLibraryPolicy.AddCollection(state.Library, captured) }, token, admission, receipt);
    }
    public Task<ShelfOperationResult> AddMembershipAsync(long revision, Guid collectionID, Guid itemID, int order,
        ISettingsCommitAdmission admission, ShelfOwnedMutationReceipt receipt, CancellationToken token)
        => MutateAsync(revision, state => {
            EnsureActive(state, itemID, collectionID);
            return state with { Library = ShelfLibraryPolicy.AddMembership(state.Library, new(collectionID, itemID, order)) };
        }, token, admission, receipt);

    public async Task<ShelfSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await LoadAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public Task<ShelfOperationResult> AddItemAsync(long revision, ShelfLaunchItem item, CancellationToken token = default) =>
        AddCapturedItemAsync(revision, Capture(item), token);

    public Task<ShelfOperationResult> CreateCollectionAsync(long revision, ShelfCollection collection, CancellationToken token = default) =>
        CreateCapturedCollectionAsync(revision, Capture(collection), token);

    private Task<ShelfOperationResult> AddCapturedItemAsync(long revision, ShelfLaunchItem item, CancellationToken token) =>
        MutateAsync(revision, state => state with { Library = ShelfLibraryPolicy.AddOrRefreshTarget(state.Library, item) }, token);

    private Task<ShelfOperationResult> CreateCapturedCollectionAsync(long revision, ShelfCollection collection, CancellationToken token) =>
        MutateAsync(revision, state => state with { Library = ShelfLibraryPolicy.AddCollection(state.Library, collection) }, token);

    // Records may still contain caller-owned lists. Capture before waiting for the storage gate.
    private static T Capture<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    public Task<ShelfOperationResult> AddMembershipAsync(long revision, Guid collectionId, Guid itemId, int order = 0, CancellationToken token = default) =>
        MutateAsync(revision, state =>
        {
            EnsureActive(state, itemId, collectionId);
            return state with { Library = ShelfLibraryPolicy.AddMembership(state.Library, new(collectionId, itemId, order)) };
        }, token);

    /// <summary>Reorders visible members only, retaining archived entries in their previous slots.
    /// Captures the proposed order before waiting; storage compare-exchange rejects concurrent edits.</summary>
    public Task<ShelfOperationResult> ReorderCollectionAsync(long revision, Guid collectionId,
        IReadOnlyList<Guid> orderedActiveItemIds, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(orderedActiveItemIds);
        var captured = CaptureOrder(orderedActiveItemIds);
        return MutateAsync(revision, state =>
        {
            if (state.ArchivedCollectionIds.Contains(collectionId))
                throw new InvalidOperationException("Restore the archived collection before reordering it.");
            var current = ShelfLibraryPolicy.ResolveCollection(state.Library, collectionId);
            var active = current.Where(item => !state.ArchivedItemIds.Contains(item.Id)).Select(item => item.Id).ToHashSet();
            if (captured.Length != active.Count || captured.Distinct().Count() != captured.Length || !active.SetEquals(captured))
                throw new ArgumentException("Supply each current visible collection member exactly once; refresh before reordering.");
            var next = 0;
            var completeOrder = current.Select(item => state.ArchivedItemIds.Contains(item.Id) ? item.Id : captured[next++]).ToArray();
            return state with { Library = ShelfLibraryPolicy.ReorderCollection(state.Library, collectionId, completeOrder) };
        }, token);
    }

    private static Guid[] CaptureOrder(IReadOnlyList<Guid> supplied)
    {
        var expected = supplied.Count;
        if (expected < 0) throw new ArgumentException("Invalid collection order count.", nameof(supplied));
        var captured = new List<Guid>();
        foreach (var id in supplied)
        {
            if (captured.Count == expected) throw new ArgumentException("Collection order enumeration exceeds its declared count.", nameof(supplied));
            captured.Add(id);
        }
        if (captured.Count != expected) throw new ArgumentException("Collection order enumeration differs from its declared count.", nameof(supplied));
        return captured.ToArray();
    }

    public Task<ShelfOperationResult> RemoveMembershipAsync(long revision, Guid collectionId, Guid itemId, CancellationToken token = default) =>
        MutateAsync(revision, state => state with { Library = ShelfLibraryPolicy.RemoveMembership(state.Library, collectionId, itemId) }, token);

    public Task<ShelfOperationResult> EditItemAsync(long revision, Guid id, string name, IReadOnlyList<string> tags,
        bool favourite, int order, ShelfLaunchBehaviour behaviour, ISettingsCommitAdmission admission,
        ShelfOwnedMutationReceipt receipt, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(admission); ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(tags);
        if (tags.Count > 256) throw new ArgumentException("At most 256 tags are supported.", nameof(tags));
        var captured = tags.Take(257).ToArray();
        if (captured.Length != tags.Count || captured.Length > 256)
            throw new ArgumentException("Tag enumeration differs from its declared bounded count.", nameof(tags));
        return EditCapturedItemAsync(revision, id, name, captured, favourite, order, behaviour, token, admission, receipt);
    }

    public Task<ShelfOperationResult> EditItemAsync(long revision, Guid id, string name, IReadOnlyList<string> tags,
        bool favourite, int order, ShelfLaunchBehaviour behaviour, CancellationToken token = default) =>
        EditCapturedItemAsync(revision, id, name, tags.ToArray(), favourite, order, behaviour, token);

    private Task<ShelfOperationResult> EditCapturedItemAsync(long revision, Guid id, string name, IReadOnlyList<string> tags,
        bool favourite, int order, ShelfLaunchBehaviour behaviour, CancellationToken token,
        ISettingsCommitAdmission? admission = null, ShelfOwnedMutationReceipt? receipt = null) =>
        MutateAsync(revision, state =>
        {
            if (!state.Library.Items.Any(item => item.Id == id)) throw new KeyNotFoundException("Launch item not found.");
            return state with { Library = state.Library with { Items = state.Library.Items.Select(item => item.Id == id
                ? item with { Name = name.Trim(), Tags = tags.ToArray(), IsFavourite = favourite, Order = order, Behaviour = behaviour }
                : item).ToArray() } };
        }, token, admission, receipt);

    public Task<ShelfOperationResult> EditCollectionAsync(long revision, ShelfCollection collection, CancellationToken token = default) =>
        EditCapturedCollectionAsync(revision, Capture(collection), token);

    private Task<ShelfOperationResult> EditCapturedCollectionAsync(long revision, ShelfCollection collection, CancellationToken token) =>
        MutateAsync(revision, state =>
        {
            var existing = state.Library.Collections.FirstOrDefault(item => item.Id == collection.Id)
                ?? throw new KeyNotFoundException("Collection not found.");
            if (existing.Kind != collection.Kind) throw new ArgumentException("Create a new collection to change its kind.");
            return state with { Library = state.Library with { Collections = state.Library.Collections.Select(item => item.Id == collection.Id
                ? collection : item).ToArray() } };
        }, token);

    /// <summary>Deletion is recoverable and preserves collection memberships.</summary>
    public Task<ShelfOperationResult> SetItemArchivedAsync(long revision, Guid id, bool archived, CancellationToken token = default) =>
        MutateAsync(revision, state =>
        {
            if (!state.Library.Items.Any(item => item.Id == id)) throw new KeyNotFoundException("Launch item not found.");
            return state with { ArchivedItemIds = ChangeArchive(state.ArchivedItemIds, id, archived) };
        }, token);

    public Task<ShelfOperationResult> SetCollectionArchivedAsync(long revision, Guid id, bool archived, CancellationToken token = default) =>
        MutateAsync(revision, state =>
        {
            if (!state.Library.Collections.Any(item => item.Id == id)) throw new KeyNotFoundException("Collection not found.");
            return state with { ArchivedCollectionIds = ChangeArchive(state.ArchivedCollectionIds, id, archived) };
        }, token);

    public static IReadOnlyList<ShelfLaunchItem> Search(ShelfSnapshot state, string query, Guid? collectionId = null)
    {
        Validate(state);
        var matches = ShelfLibraryPolicy.Search(state.Library, query);
        if (collectionId is not { } id)
            return matches.Where(item => !state.ArchivedItemIds.Contains(item.Id)).ToArray();
        if (state.ArchivedCollectionIds.Contains(id)) return [];
        var matchingIds = matches.Select(item => item.Id).ToHashSet();
        return ShelfLibraryPolicy.ResolveCollection(state.Library, id)
            .Where(item => matchingIds.Contains(item.Id) && !state.ArchivedItemIds.Contains(item.Id)).ToArray();
    }

    public Task<ShelfOperationResult> ImportAsync(long revision, ShelfSnapshot imported, CancellationToken token = default)
    {
        var snapshot = Capture(imported);
        return MutateAsync(revision, _ => { Validate(snapshot); return snapshot; }, token);
    }

    private async Task<ShelfOperationResult> MutateAsync(long revision, Func<ShelfSnapshot, ShelfSnapshot> mutation, CancellationToken token, ISettingsCommitAdmission? admission = null, ShelfOwnedMutationReceipt? receipt = null)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        var writeStarted = false;
        try
        {
            if (_settings is not IVersionedSettingsCompareExchange atomic)
                return new(false, "AtomicStoreUnavailable", "Shelf requires atomic storage.", null);
            var stored = await _settings.ExportAsync(token).ConfigureAwait(false);
            stored.Settings.TryGetValue(Key, out var expectedJson);
            var state = expectedJson is null ? ShelfSnapshot.Empty : JsonSerializer.Deserialize<ShelfSnapshot>(expectedJson)
                ?? throw new InvalidDataException("Stored shelf library is null.");
            Validate(state);
            if (state.Library.Revision != revision)
                return new(false, "RevisionConflict", "Shelf changed; refresh and review before retrying.", state);
            var updated = mutation(state);
            updated = updated with { Library = updated.Library with { Revision = checked(revision + 1) } };
            if (receipt is not null)
            {
                if (receipt.SchemaVersion != 1 || receipt.OperationID == Guid.Empty || receipt.StoreID != stored.StoreIdentity?.StoreId
                    || receipt.ExpectedLibraryRevision != revision || receipt.PayloadSHA256.Length != 64)
                    throw new ArgumentException("Exact actual Shelf mutation receipt required.");
                updated = updated with { LastOwnedMutation = receipt };
            }
            Validate(updated);
            if (admission is not null)
            {
                if (_settings is not IVersionedSettingsGuardedCompareExchange guarded) return new(false, "GuardedStoreUnavailable", "Shelf requires final owner admission.", null);
                writeStarted = true;
                var committed = await guarded.CompareExchangeGuardedAsync(Key, expectedJson, JsonSerializer.Serialize(updated),
                    new Dictionary<string, string?>(), admission, token).ConfigureAwait(false);
                if (!committed.Exchanged) return new(false, "CommitDenied", "Shelf changed or owner admission was revoked.", null);
            }
            else if (!(await atomic.CompareExchangeAsync(Key, expectedJson, JsonSerializer.Serialize(updated), token).ConfigureAwait(false)).Exchanged)
                return new(false, "RevisionConflict", "Shelf changed; refresh before retrying.", null);
            return new(true, null, null, updated);
        }
        catch (Exception) when (writeStarted && receipt is not null)
        { return new(false, "CompletionUnknown", "Actual Shelf write completion is unknown; observe its exact durable receipt without replay.", null); }
        catch (KeyNotFoundException exception) { return new(false, "NotFound", exception.Message, null); }
        catch (ArgumentException exception) { return new(false, "InvalidArgument", exception.Message, null); }
        catch (InvalidOperationException exception) { return new(false, "InvalidOperation", exception.Message, null); }
        catch (JsonException) { return new(false, "InvalidData", "Stored Shelf data is incompatible or corrupt; prior state was preserved.", null); }
        catch (InvalidDataException exception) { return new(false, "InvalidData", exception.Message, null); }
        catch (IOException) { return new(false, "StorageUnavailable", "Shelf could not be saved. Retry after checking storage.", null); }
        finally { _gate.Release(); }
    }

    private async Task<ShelfSnapshot> LoadAsync(CancellationToken token)
    {
        var state = await _settings.GetAsync<ShelfSnapshot>(Key, token).ConfigureAwait(false) ?? ShelfSnapshot.Empty;
        Validate(state);
        return state;
    }

    private static void Validate(ShelfSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.Library);
        var errors = state.Library.Validate();
        if (errors.Count > 0) throw new InvalidDataException(string.Join(' ', errors));
        if (state.ArchivedItemIds is null || state.ArchivedCollectionIds is null
            || state.ArchivedItemIds.Distinct().Count() != state.ArchivedItemIds.Count
            || state.ArchivedCollectionIds.Distinct().Count() != state.ArchivedCollectionIds.Count
            || state.ArchivedItemIds.Any(id => !state.Library.Items.Any(item => item.Id == id))
            || state.ArchivedCollectionIds.Any(id => !state.Library.Collections.Any(collection => collection.Id == id)))
            throw new InvalidDataException("Shelf archive references are invalid.");
    }

    private static Guid[] ChangeArchive(IReadOnlyList<Guid> ids, Guid id, bool archived) =>
        archived ? ids.Append(id).Distinct().ToArray() : ids.Where(existing => existing != id).ToArray();

    private static void EnsureActive(ShelfSnapshot state, Guid itemId, Guid collectionId)
    {
        if (state.ArchivedItemIds.Contains(itemId) || state.ArchivedCollectionIds.Contains(collectionId))
            throw new InvalidOperationException("Restore archived items and collections before editing membership.");
    }
}
