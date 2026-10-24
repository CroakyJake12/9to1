using System.Security.Cryptography;
using System.Text.Json;

namespace HavenOS.Files;

public sealed partial class DurableDriveProvider
{
    private const int MaximumSearchCursorLength = 128;
    private const int VersionPageSize = 100;

    /// <summary>Returns immutable history for a currently visible canonical item. The continuation is
    /// bound to the item and complete retained revision set so a concurrent revision cannot skip history.</summary>
    public async Task<FilesPage<FilesRevision>> GetVersionsAsync(HostedItemId itemId, string? pageToken,
        CancellationToken cancellationToken = default)
    {
        if (itemId.Value == Guid.Empty) throw new ArgumentException("Select a canonical Files item.", nameof(itemId));
        if (!Location.Capabilities.HasFlag(FilesProviderCapabilities.Versions))
            throw new NotSupportedException("This Files provider does not support version history.");
        var state = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        var item = state.Items.SingleOrDefault(entry => entry.Metadata.Id == itemId);
        if (item is null || !IsVisible(state, item))
            throw new UnauthorizedAccessException("The selected canonical Files item is unavailable.");
        var revisions = state.Revisions.Where(revision => revision.ItemId == itemId)
            .OrderByDescending(revision => revision.IsCurrent).ThenByDescending(revision => revision.CreatedAt)
            .ThenByDescending(revision => revision.Id.Value).ToArray();
        return CreateVersionPage(state.StoreId ?? Guid.Empty, item, revisions, pageToken);
    }

    /// <summary>Searches canonical visible names in this provider without reducing the query to one folder.
    /// Content/metadata indexing is intentionally not implied by this name-only provider operation.</summary>
    public async Task<FilesPage<HostedItemMetadata>> SearchAsync(FilesSearchQuery query, string? pageToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Text is null || query.Text.Length > 256 || query.Limit is < 1 or > 500)
            throw new ArgumentException("Search requires text up to 256 characters and a page size from 1 to 500.", nameof(query));
        if (!Location.Capabilities.HasFlag(FilesProviderCapabilities.Search))
            throw new NotSupportedException("This Files provider does not support search.");
        if (query.LocationId is { } location && location != Location.Id)
            throw new ArgumentException("Search location does not belong to this Files provider.", nameof(query));

        var state = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        var entriesById = new Dictionary<HostedItemId, Entry>(state.Items.Count);
        foreach (var entry in state.Items)
            if (!entriesById.TryAdd(entry.Metadata.Id, entry))
                throw new InvalidDataException("Files item identity is duplicated; search refuses an ambiguous hierarchy.");
        if (query.ParentId is { } parentId)
        {
            if (!entriesById.TryGetValue(parentId, out var parent) || parent.Metadata.Kind != HostedItemKind.Folder || !IsVisible(state, parent))
                throw new ArgumentException("Search scope is not a current visible Files folder.", nameof(query));
        }

        bool InScope(Entry entry)
        {
            if (query.ParentId is not { } scope) return true;
            var seen = new HashSet<HostedItemId>();
            var current = entry;
            while (current.Metadata.ParentId is { } parent)
            {
                if (parent == scope) return true;
                if (!seen.Add(current.Metadata.Id)) return false;
                if (!entriesById.TryGetValue(parent, out var ancestor)
                    || ancestor.Deleted || ancestor.Metadata.Kind != HostedItemKind.Folder) return false;
                current = ancestor;
            }
            return false;
        }

        var matches = state.Items.Where(entry => IsVisible(state, entry) && InScope(entry)
                && entry.Metadata.Name.Contains(query.Text, StringComparison.OrdinalIgnoreCase))
            .Select(entry => ProjectSharingMetadata(state, entry.Metadata))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id.Value).ToArray();
        return CreateSearchPage(query, pageToken, matches);
    }

    private static FilesPage<HostedItemMetadata> CreateSearchPage(FilesSearchQuery query, string? pageToken,
        IReadOnlyList<HostedItemMetadata> matches)
    {
        var fingerprint = SearchFingerprint(query, matches);
        var offset = 0;
        if (pageToken is not null)
        {
            if (pageToken.Length > MaximumSearchCursorLength)
                throw new ArgumentException("The Files search cursor is invalid or obsolete; restart the search.", nameof(pageToken));
            var parts = pageToken.Split(':');
            if (parts.Length != 3 || parts[0] != "files-search-v1"
                || !int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out offset)
                || offset < 0 || offset >= matches.Length || offset % query.Limit != 0
                || !string.Equals(parts[2], fingerprint, StringComparison.Ordinal))
                throw new ArgumentException("The Files search cursor is invalid, stale, or belongs to another query.", nameof(pageToken));
        }

        var page = matches.Skip(offset).Take(query.Limit).ToArray();
        var next = offset + page.Length < matches.Length
            ? $"files-search-v1:{offset + page.Length}:{fingerprint}" : null;
        return new(page, next);
    }

    private static string SearchFingerprint(FilesSearchQuery query, IReadOnlyList<HostedItemMetadata> matches)
    {
        var canonical = new
        {
            query.Text,
            Location = query.LocationId?.Value,
            Parent = query.ParentId?.Value,
            query.Limit,
            Matches = matches.Select(item => new
            {
                Id = item.Id.Value,
                Parent = item.ParentId?.Value,
                item.Name,
                item.Kind,
                Revision = item.CurrentRevisionId?.Value
            }).ToArray()
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical)));
    }

    private static FilesPage<FilesRevision> CreateVersionPage(Guid storeId, Entry item,
        IReadOnlyList<FilesRevision> revisions, string? pageToken)
    {
        if (storeId == Guid.Empty) throw new InvalidDataException("Files version history has no canonical store identity.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            StoreId = storeId,
            ItemId = item.Metadata.Id.Value,
            CurrentRevision = item.Metadata.CurrentRevisionId?.Value,
            Revisions = revisions.Select(revision => new
            {
                Id = revision.Id.Value,
                revision.ParentRevisionId,
                revision.CreatedAt,
                revision.ActorId,
                revision.Source,
                revision.ContentHash,
                revision.SizeBytes,
                revision.OwningAppId,
                revision.OwningAppRevisionId,
                revision.IsCurrent
            }).ToArray()
        })));
        var offset = 0;
        if (pageToken is not null)
        {
            if (pageToken.Length > 128)
                throw new ArgumentException("The version-history cursor is invalid, stale, or belongs to another item.", nameof(pageToken));
            var parts = pageToken.Split(':');
            if (parts.Length != 3 || parts[0] != "files-versions-v1"
                || !int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out offset)
                || offset < 0 || offset >= revisions.Count || offset % VersionPageSize != 0
                || !string.Equals(parts[2], fingerprint, StringComparison.Ordinal))
                throw new ArgumentException("The version-history cursor is invalid, stale, or belongs to another item.", nameof(pageToken));
        }
        var page = revisions.Skip(offset).Take(VersionPageSize).ToArray();
        var next = offset + page.Length < revisions.Count
            ? $"files-versions-v1:{offset + page.Length}:{fingerprint}" : null;
        return new(page, next);
    }
}
