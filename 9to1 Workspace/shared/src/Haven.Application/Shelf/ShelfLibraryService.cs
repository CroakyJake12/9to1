using System.Text.Json;
using Haven.Core.Shelf;

namespace Haven.Application.Shelf;

public sealed record ShelfSnapshot(ShelfLibrary Library, IReadOnlyList<Guid> ArchivedItemIds,
    IReadOnlyList<Guid> ArchivedCollectionIds)
{
    public static ShelfSnapshot Empty { get; } = new(ShelfLibrary.Empty, [], []);
}

public sealed record ShelfOperationResult(bool Success, string? ErrorCode, string? Message, ShelfSnapshot? Snapshot);

/// <summary>
/// Canonical persisted Shelf organisation. Both UI and authorised automation use these operations;
/// target opening remains the responsibility of the owning application/platform service.
/// Register one instance per user profile so all surfaces share its revision gate.
/// </summary>
public sealed class ShelfLibraryService(IVersionedSettingsStore settings)
{
    private const string Key = "shelf.library.v1";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IVersionedSettingsStore _settings = settings ?? throw new ArgumentNullException(nameof(settings));

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

    public Task<ShelfOperationResult> RemoveMembershipAsync(long revision, Guid collectionId, Guid itemId, CancellationToken token = default) =>
        MutateAsync(revision, state => state with { Library = ShelfLibraryPolicy.RemoveMembership(state.Library, collectionId, itemId) }, token);

    public Task<ShelfOperationResult> EditItemAsync(long revision, Guid id, string name, IReadOnlyList<string> tags,
        bool favourite, int order, ShelfLaunchBehaviour behaviour, CancellationToken token = default) =>
        EditCapturedItemAsync(revision, id, name, tags.ToArray(), favourite, order, behaviour, token);

    private Task<ShelfOperationResult> EditCapturedItemAsync(long revision, Guid id, string name, IReadOnlyList<string> tags,
        bool favourite, int order, ShelfLaunchBehaviour behaviour, CancellationToken token) =>
        MutateAsync(revision, state =>
        {
            if (!state.Library.Items.Any(item => item.Id == id)) throw new KeyNotFoundException("Launch item not found.");
            return state with { Library = state.Library with { Items = state.Library.Items.Select(item => item.Id == id
                ? item with { Name = name.Trim(), Tags = tags.ToArray(), IsFavourite = favourite, Order = order, Behaviour = behaviour }
                : item).ToArray() } };
        }, token);

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
        var visible = ShelfLibraryPolicy.Search(state.Library, query).Where(item => !state.ArchivedItemIds.Contains(item.Id));
        if (collectionId is { } id)
        {
            if (state.ArchivedCollectionIds.Contains(id)) return [];
            var members = ShelfLibraryPolicy.ResolveCollection(state.Library, id).Select(item => item.Id).ToHashSet();
            visible = visible.Where(item => members.Contains(item.Id));
        }
        return visible.ToArray();
    }

    public Task<ShelfOperationResult> ImportAsync(long revision, ShelfSnapshot imported, CancellationToken token = default)
    {
        var snapshot = Capture(imported);
        return MutateAsync(revision, _ => { Validate(snapshot); return snapshot; }, token);
    }

    private async Task<ShelfOperationResult> MutateAsync(long revision, Func<ShelfSnapshot, ShelfSnapshot> mutation, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
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
            Validate(updated);
            if (!(await atomic.CompareExchangeAsync(Key, expectedJson, JsonSerializer.Serialize(updated), token).ConfigureAwait(false)).Exchanged)
                return new(false, "RevisionConflict", "Shelf changed; refresh before retrying.", null);
            return new(true, null, null, updated);
        }
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
