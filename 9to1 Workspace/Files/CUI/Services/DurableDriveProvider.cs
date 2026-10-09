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
        public Guid? StoreId { get; init; }
        public DateTimeOffset? StoreCreatedAtUtc { get; init; }
        public Guid? CreationSessionId { get; init; }
        public string? StoreOwnerPrincipalId { get; init; }
        public FilesLocationId? StoreLocationId { get; init; }
        public IReadOnlyList<FilesArtifactReference> Artifacts { get; init; } = [];
        public IReadOnlyList<FilesRevision> Revisions { get; init; } = [];
        public IReadOnlyDictionary<string, string?> RevisionContentReferences { get; init; } = new Dictionary<string, string?>();
        public IReadOnlyList<FilesUploadedContent> UploadedContents { get; init; } = [];
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<FilesBrowserDownloadRegistration>? BrowserDownloads { get; init; }
    }
    private readonly VersionedJsonStateStore<State> _store;
    private readonly string _owner;
    private readonly Guid _creationSessionId = Guid.NewGuid();
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
        _store = new(statePath, 1, () => new State([], [], [])
        {
            StoreId = Guid.NewGuid(), StoreCreatedAtUtc = DateTimeOffset.UtcNow,
            CreationSessionId = _creationSessionId, StoreOwnerPrincipalId = _owner, StoreLocationId = locationId
        });
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

    public Task<FilesResult<FilesOperation>> MutateAsync(FilesOperation operation, string? newName, CancellationToken cancellationToken) =>
        MutateCoreAsync(operation, newName, null, [], null, cancellationToken);

    /// <summary>Trusted original Dev import only. No other structural mutation is exposed through
    /// this additive port. The caller owns the actual held Home entry and once-created step IDs.</summary>
    public Task<FilesResult<FilesOperation>> CreateOriginalDeveloperFolderAsync(FilesOperation operation, string newName,
        Guid originalStoreId, IReadOnlyList<FilesItemRevisionPrecondition> originalParents,
        FilesCommitAuthorityGuard originalAuthority, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation); ArgumentNullException.ThrowIfNull(originalParents);
        ArgumentNullException.ThrowIfNull(originalAuthority);
        var captured = originalParents.ToArray();
        if (originalStoreId == Guid.Empty || operation.Operation != "CreateFolder" || operation.State != FilesOperationState.Pending ||
            operation.ActorId != _owner || originalAuthority.ActorId != _owner || operation.ItemId.Value == Guid.Empty ||
            captured.Length is 0 or > 65 || captured.Any(value => value is null || value.ItemId.Value == Guid.Empty || value.ExpectedRevision is null) ||
            captured.Select(value => value.ItemId).Distinct().Count() != captured.Length ||
            operation.DestinationParentId is not { } parent || !captured.Any(value => value.ItemId == parent))
            return Task.FromResult(Fail<FilesOperation>(FilesErrorCode.PermissionDenied,
                "Retain the original owned store, parent revisions, once-created folder/step IDs and held commit authority.", operation.Operation, operation.ItemId));
        return MutateCoreAsync(operation, newName, originalStoreId, captured, originalAuthority, cancellationToken);
    }

    /// <summary>Owning native browser commit after its actual Home claim. The exact displayed
    /// store revision and parent/item revisions are checked inside the metadata transaction;
    /// the supplied guard holds the same claimed Home fence through durable publication.</summary>
    public Task<FilesResult<FilesOperation>> CommitNativeBrowserStructureAsync(FilesOperation operation,
        string newName, Guid originalStoreId, string originalStoreRevision,
        IReadOnlyList<FilesItemRevisionPrecondition> originalParents, FilesCommitAuthorityGuard originalAuthority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation); ArgumentNullException.ThrowIfNull(originalParents);
        ArgumentNullException.ThrowIfNull(originalAuthority);
        var parents = originalParents.ToArray();
        if (originalStoreId == Guid.Empty || string.IsNullOrWhiteSpace(originalStoreRevision)
            || operation.State != FilesOperationState.Pending || operation.Id.Value == Guid.Empty
            || operation.ItemId.Value == Guid.Empty || operation.ActorId != _owner || originalAuthority.ActorId != _owner
            || operation.Operation is not ("CreateFolder" or "Rename") || parents.Length > 1
            || parents.Any(parent => parent is null || parent.ItemId.Value == Guid.Empty || parent.ExpectedRevision is null)
            || operation.Operation == "CreateFolder" && (operation.BaseRevisionId is not null
                || operation.DestinationParentId is not { } destination || parents.Length != 1 || parents[0].ItemId != destination)
            || operation.Operation == "Rename" && operation.BaseRevisionId is null)
            return Task.FromResult(Fail<FilesOperation>(FilesErrorCode.PermissionDenied,
                "Retain the exact native browser operation, original store/parents and held Home commit guard.", operation.Operation, operation.ItemId));
        return MutateCoreAsync(operation, newName, originalStoreId, parents, originalAuthority,
            cancellationToken, originalStoreRevision);
    }

    private async Task<FilesResult<FilesOperation>> MutateCoreAsync(FilesOperation operation, string? newName,
        Guid? originalStoreId, IReadOnlyList<FilesItemRevisionPrecondition> originalParents,
        FilesCommitAuthorityGuard? originalAuthority, CancellationToken cancellationToken,
        string? expectedStoreRevision = null)
    {
        FilesResult<FilesOperation>? result = null;
        try
        {
        Task<State> actual;
        try { actual = _store.UpdateAsync(state =>
        {
            if (originalStoreId is { } store && state.StoreId != store) throw new OriginalFilesStoreChangedException();
            if (expectedStoreRevision is not null && (state.StoreOwnerPrincipalId != _owner ||
                state.StoreLocationId != Location.Id || expectedStoreRevision != Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(state)))))
            { result = Fail<FilesOperation>(FilesErrorCode.RevisionConflict, "The displayed Files store changed before publication.", operation.Operation, operation.ItemId); return state; }
            foreach (var expected in originalParents)
            {
                var actual = state.Items.SingleOrDefault(value => value.Metadata.Id == expected.ItemId);
                if (actual is null || !IsVisible(state, actual) || actual.Metadata.Kind != HostedItemKind.Folder ||
                    actual.Metadata.CurrentRevisionId != expected.ExpectedRevision)
                { result = Fail<FilesOperation>(FilesErrorCode.RevisionConflict, "An original project parent changed before folder publication.", operation.Operation, operation.ItemId); return state; }
            }
            var prior = state.Operations.FirstOrDefault(item => item.Id == operation.Id);
            if (originalStoreId is not null && prior is not null)
            { result = Fail<FilesOperation>(FilesErrorCode.InvalidState, "The original setup step is already recorded; inspect its outcome without another folder admission.", operation.Operation, operation.ItemId); return state; }
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
        }, originalAuthority is null ? null : originalAuthority.ValidateAsync, cancellationToken); }
        catch (OperationCanceledException original) when (originalAuthority is not null)
        { throw new AggregateException("The original folder commit source returned no canceled original Task; outcome may be unknown.", original); }
        try { await actual.ConfigureAwait(false); }
        catch when (originalAuthority is not null && actual.IsFaulted &&
            actual.Exception!.InnerExceptions.Count != 1) { throw actual.Exception!; }
        catch (Exception) when (originalAuthority is not null && actual.IsFaulted &&
            actual.Exception!.InnerException is not (OriginalFilesStoreChangedException or FilesCommitAuthorityChangedException))
        { throw actual.Exception!; }
        }
        catch (OriginalFilesStoreChangedException)
        { return Fail<FilesOperation>(FilesErrorCode.RevisionConflict, "The original Dev destination store changed.", operation.Operation, operation.ItemId); }
        catch (FilesCommitAuthorityChangedException)
        { return Fail<FilesOperation>(FilesErrorCode.PermissionDenied, "The actual held Dev import authority changed before folder publication.", operation.Operation, operation.ItemId); }
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
