using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace HavenOS.Files;

/// <summary>Hosted hierarchy authority. Structural operations, journal and resumable events commit atomically.</summary>
public sealed partial class DurableDriveProvider : IFilesProvider, IFilesOwningAppRevisionSink
{
    public sealed record Entry(HostedItemMetadata Metadata, bool Deleted = false, DateTimeOffset? DeletedAt = null);
    public sealed record State(IReadOnlyList<Entry> Items, IReadOnlyList<FilesOperation> Operations, IReadOnlyList<FilesChangeEvent> Events)
    {
        public IReadOnlyList<FilesArtifactReference> Artifacts { get; init; } = [];
        public IReadOnlyList<FilesRevision> Revisions { get; init; } = [];
        public IReadOnlyDictionary<string, string?> RevisionContentReferences { get; init; } = new Dictionary<string, string?>();
        public IReadOnlyList<FilesUploadedContent> UploadedContents { get; init; } = [];
    }
    private readonly VersionedJsonStateStore<State> _store;
    private readonly string _owner;
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<bool>>> Subscriptions = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, Channel<bool>> _subscribers;

    public DurableDriveProvider(string statePath, FilesLocationId locationId, string ownerPrincipalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerPrincipalId);
        _owner = ownerPrincipalId;
        _subscribers = Subscriptions.GetOrAdd(Path.GetFullPath(statePath), static _ => new());
        Location = new(locationId, "9to1 Drive", FilesLocationKind.Drive,
            FilesProviderCapabilities.Read | FilesProviderCapabilities.Write | FilesProviderCapabilities.Rename |
            FilesProviderCapabilities.Move | FilesProviderCapabilities.Trash | FilesProviderCapabilities.Search |
            FilesProviderCapabilities.ChangeFeed, "9to1.drive");
        _store = new(statePath, 1, () => new State([], [], []));
    }

    public FilesLocation Location { get; }

    public async Task<FilesResult<HostedItemMetadata>> GetAsync(HostedItemId itemId, CancellationToken cancellationToken)
    {
        var state = await _store.ReadAsync(cancellationToken);
        var item = state.Items.FirstOrDefault(item => item.Metadata.Id == itemId);
        return item is null || !IsVisible(state, item) ? Fail<HostedItemMetadata>(FilesErrorCode.ItemNotFound, "Item is unavailable.", "Get", itemId) : FilesResult<HostedItemMetadata>.Success(item.Metadata);
    }

    public async Task<FilesPage<HostedItemMetadata>> ListAsync(HostedItemId? parentId, FilesSearchQuery? query, string? pageToken, CancellationToken cancellationToken)
    {
        var state = await _store.ReadAsync(cancellationToken);
        int offset = ParseOffset(pageToken);
        int limit = query?.Limit ?? 100;
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(query));
        var items = state.Items.Where(item => IsVisible(state, item) && item.Metadata.ParentId == parentId &&
            (query is null || item.Metadata.Name.Contains(query.Text, StringComparison.OrdinalIgnoreCase)))
            .Select(item => item.Metadata).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id.Value).ToArray();
        if (offset > items.Length) throw new ArgumentException("Invalid page token.", nameof(pageToken));
        var page = items.Skip(offset).Take(limit).ToArray();
        return new(page, offset + page.Length < items.Length ? (offset + page.Length).ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
    }

    public async Task<FilesResult<FilesOperation>> MutateAsync(FilesOperation operation, string? newName, CancellationToken cancellationToken)
    {
        FilesResult<FilesOperation>? result = null;
        await _store.UpdateAsync(state =>
        {
            var prior = state.Operations.FirstOrDefault(item => item.Id == operation.Id);
            if (prior is not null)
            {
                result = prior.ActorId == operation.ActorId && prior.ItemId == operation.ItemId && prior.Operation == operation.Operation &&
                    prior.DestinationParentId == operation.DestinationParentId && prior.BaseRevisionId == operation.BaseRevisionId && prior.Payload?.NewName == newName
                    ? FilesResult<FilesOperation>.Success(prior) : Fail<FilesOperation>(FilesErrorCode.InvalidState, "Operation ID was reused with different arguments.", operation.Operation, operation.ItemId);
                return state;
            }
            if (operation.ActorId != _owner)
            { result = Fail<FilesOperation>(FilesErrorCode.PermissionDenied, "Caller does not own this Drive.", operation.Operation, operation.ItemId); return state; }
            var existing = state.Items.FirstOrDefault(item => item.Metadata.Id == operation.ItemId);
            bool create = operation.Operation == "CreateFolder";
            if ((!create && existing is null) || (create && existing is not null))
            { result = Fail<FilesOperation>(FilesErrorCode.ItemNotFound, "Invalid target identity.", operation.Operation, operation.ItemId); return state; }
            if (!create && !IsVisible(state, existing!) && operation.Operation != "Restore")
            { result = Fail<FilesOperation>(FilesErrorCode.ItemNotFound, "Item is in Trash.", operation.Operation, operation.ItemId); return state; }
            if (!create && operation.BaseRevisionId != existing!.Metadata.CurrentRevisionId)
            { result = Fail<FilesOperation>(FilesErrorCode.RevisionConflict, "The item changed; refresh before retrying.", operation.Operation, operation.ItemId); return state; }
            var name = create || operation.Operation == "Rename" ? newName : existing!.Metadata.Name;
            if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(['/', '\\', '\0']) >= 0)
            { result = Fail<FilesOperation>(FilesErrorCode.InvalidName, "Name must be a single non-empty path component.", operation.Operation, operation.ItemId); return state; }
            var parent = create || operation.Operation == "Move" ? operation.DestinationParentId : existing!.Metadata.ParentId;
            if (parent is { } parentId)
            {
                var destination = state.Items.FirstOrDefault(item => item.Metadata.Id == parentId);
                if (destination is null || !IsVisible(state, destination) || destination.Metadata.Kind != HostedItemKind.Folder)
                { result = Fail<FilesOperation>(FilesErrorCode.DestinationUnavailable, "Destination folder is unavailable.", operation.Operation, operation.ItemId); return state; }
                var cursor = destination;
                var ancestry = new HashSet<HostedItemId>();
                while (cursor is not null)
                {
                    if (!ancestry.Add(cursor.Metadata.Id))
                    { result = Fail<FilesOperation>(FilesErrorCode.InvalidState, "Stored folder ancestry contains a cycle.", operation.Operation, operation.ItemId); return state; }
                    if (cursor.Metadata.Id == operation.ItemId)
                    { result = Fail<FilesOperation>(FilesErrorCode.InvalidState, "A folder cannot contain itself.", operation.Operation, operation.ItemId); return state; }
                    cursor = state.Items.FirstOrDefault(item => item.Metadata.Id == cursor.Metadata.ParentId);
                }
            }
            if (operation.Operation is not ("CreateFolder" or "Rename" or "Move" or "Delete" or "Restore"))
            { result = FilesProviderCapabilityExtensions.Unsupported<FilesOperation>(Location, operation.Operation); return state; }
            if (operation.Operation != "Delete" && state.Items.Any(item => !item.Deleted && item.Metadata.Id != operation.ItemId && item.Metadata.ParentId == parent && item.Metadata.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            { result = Fail<FilesOperation>(FilesErrorCode.NameConflict, "Destination already contains this name.", operation.Operation, operation.ItemId); return state; }
            var now = DateTimeOffset.UtcNow;
            var revision = new FilesRevisionId(Guid.NewGuid());
            var metadata = create ? new HostedItemMetadata(operation.ItemId, Location.Id, parent, name!, HostedItemKind.Folder, null, _owner, "personal", null, now, now, revision, SyncAvailability.Synced, false, null) :
                existing!.Metadata with { Name = name!, ParentId = parent, CurrentRevisionId = revision, ModifiedAt = now };
            var entry = new Entry(metadata, operation.Operation == "Delete", operation.Operation == "Delete" ? now : null);
            var committed = operation with { State = FilesOperationState.Committed, ResultRevisionId = revision, UpdatedAt = now,
                Payload = (operation.Payload ?? new FilesOperationPayload()) with { NewName = newName }, Sequence = state.Events.Count + 1 };
            var change = new FilesChangeEvent(Guid.NewGuid().ToString("N"), new(state.Events.Count + 1), operation.Id, operation.ItemId, operation.ActorId, operation.Operation, operation.BaseRevisionId, revision, now, metadata);
            result = FilesResult<FilesOperation>.Success(committed);
            return state with { Items = [.. state.Items.Where(item => item.Metadata.Id != operation.ItemId), entry], Operations = [.. state.Operations, committed], Events = [.. state.Events, change] };
        }, cancellationToken);
        if (result!.IsSuccess) foreach (var subscriber in _subscribers.Values) subscriber.Writer.TryWrite(true);
        return result;
    }

    public async Task<FilesPage<FilesChangeEvent>> GetChangesAsync(FilesChangeCursor? after, int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        var state = await _store.ReadAsync(cancellationToken);
        long cursor = after?.Revision ?? 0;
        if (cursor < 0 || cursor > state.Events.Count) throw new ArgumentException("Invalid change cursor.", nameof(after));
        var page = state.Events.Where(item => item.Cursor.Revision > cursor).Take(limit).ToArray();
        return new(page, page.Length > 0 && page[^1].Cursor.Revision < state.Events.Count ? page[^1].Cursor.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
    }

    public async IAsyncEnumerable<FilesChangeEvent> SubscribeAsync(FilesChangeCursor? after, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        _subscribers[id] = wake;
        try
        {
            var cursor = after;
            while (true)
            {
                var page = await GetChangesAsync(cursor, 500, cancellationToken);
                foreach (var item in page.Items) { cursor = item.Cursor; yield return item; }
                if (page.NextPageToken is not null) continue;
                await wake.Reader.ReadAsync(cancellationToken);
            }
        }
        finally { _subscribers.TryRemove(id, out _); }
    }

    private static bool IsVisible(State state, Entry entry)
    {
        var seen = new HashSet<HostedItemId>();
        Entry? cursor = entry;
        while (cursor is not null)
        {
            if (cursor.Deleted || !seen.Add(cursor.Metadata.Id)) return false;
            if (cursor.Metadata.ParentId is not { } parent) return true;
            cursor = state.Items.FirstOrDefault(item => item.Metadata.Id == parent);
            if (cursor is null) return false;
        }
        return true;
    }

    private static int ParseOffset(string? token) => token is null ? 0 : int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var offset) && offset >= 0 ? offset : throw new ArgumentException("Invalid page token.", nameof(token));
    private static FilesResult<T> Fail<T>(FilesErrorCode code, string message, string action, HostedItemId id) =>
        FilesResult<T>.Failure(new(code, message, "9to1.Files." + action, id.ToString(), code is FilesErrorCode.RevisionConflict or FilesErrorCode.NameConflict, false));
}
