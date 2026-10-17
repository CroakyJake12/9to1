namespace HavenOS.Files;

/// <summary>A recoverable deletion retains the original canonical identity, parent and revision.</summary>
public sealed record FilesTrashEntry(HostedItemMetadata Metadata, DateTimeOffset DeletedAt);

public sealed partial class DurableDriveProvider
{
    /// <summary>Owner metadata only. Ordinary Get/List never expose deleted items.</summary>
    public async Task<FilesPage<FilesTrashEntry>> ListTrashForOriginalStoreAsync(Guid expectedStoreId,
        string search, string? pageToken, int limit, CancellationToken cancellationToken = default)
    {
        if (search.Length > 256 || limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        var state = await _store.ReadExistingAsync(cancellationToken).ConfigureAwait(false);
        RequireOriginalReadStore(state, expectedStoreId);
        var items = state.Items.Where(entry => entry.Deleted && entry.DeletedAt is not null &&
                entry.Metadata.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Metadata.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Metadata.Id.Value).ToArray();
        var offset = ParseOffset(pageToken);
        if (offset > items.Length) throw new ArgumentException("Invalid Trash page token.", nameof(pageToken));
        var page = items.Skip(offset).Take(limit).Select(entry =>
        {
            RequireOriginalReadItem(entry.Metadata);
            return new FilesTrashEntry(entry.Metadata, entry.DeletedAt!.Value);
        }).ToArray();
        return new(page, offset + page.Length < items.Length
            ? (offset + page.Length).ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
    }

    public Task<FilesResult<HostedItemMetadata>> GetTrashedAsync(HostedItemId itemId,
        CancellationToken cancellationToken = default) => ReadTrashedAsync(null, itemId, cancellationToken);

    public Task<FilesResult<HostedItemMetadata>> GetTrashedForOriginalStoreAsync(Guid expectedStoreId,
        HostedItemId itemId, CancellationToken cancellationToken = default)
        => ReadTrashedAsync(expectedStoreId, itemId, cancellationToken);

    private async Task<FilesResult<HostedItemMetadata>> ReadTrashedAsync(Guid? expectedStoreId,
        HostedItemId itemId, CancellationToken cancellationToken)
    {
        var state = await _store.ReadExistingAsync(cancellationToken).ConfigureAwait(false);
        RequireOriginalReadStore(state, expectedStoreId ?? state.StoreId ?? Guid.Empty);
        var entry = state.Items.SingleOrDefault(value => value.Metadata.Id == itemId);
        if (entry is not { Deleted: true, DeletedAt: not null })
            return Fail<HostedItemMetadata>(FilesErrorCode.ItemNotFound, "The item is not in Trash.", "GetTrashItem", itemId);
        RequireOriginalReadItem(entry.Metadata);
        return FilesResult<HostedItemMetadata>.Success(entry.Metadata);
    }
}
