using Haven.Core.Shelf;

namespace Haven.Application.Shelf;

/// <summary>Deterministic Shelf operations over canonical references and user organisation.</summary>
public static class ShelfLibraryPolicy
{
    public static ShelfLibrary AddOrRefreshTarget(ShelfLibrary library, ShelfLaunchItem candidate)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(candidate);
        EnsureValid(library);
        if (!ShelfLibrary.IsValidTarget(candidate.Target) || string.IsNullOrWhiteSpace(candidate.Name))
            throw new ArgumentException("A Shelf item requires a name and valid canonical target.", nameof(candidate));

        var existing = library.Items.FirstOrDefault(item => SameTarget(item.Target, candidate.Target));
        if (existing is null && library.Items.Any(item => item.Id == candidate.Id))
            throw new InvalidOperationException("A different Shelf target already uses this stable launch item ID.");
        var items = existing is null
            ? library.Items.Append(candidate).ToArray()
            : library.Items.Select(item => item.Id == existing.Id
                ? item with { Name = candidate.Name, Target = candidate.Target, IconReference = candidate.IconReference ?? item.IconReference }
                : item).ToArray();
        return library with { Revision = checked(library.Revision + 1), Items = items };
    }

    public static ShelfLibrary AddCollection(ShelfLibrary library, ShelfCollection collection)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(collection);
        EnsureValid(library);
        if (collection.Id == Guid.Empty || string.IsNullOrWhiteSpace(collection.Name)
            || (collection.Kind == ShelfCollectionKind.Smart) != (collection.Criteria is not null))
            throw new ArgumentException("Collection identity, name and criteria do not match its type.", nameof(collection));
        if (library.Collections.Any(item => item.Id == collection.Id))
            throw new InvalidOperationException("A collection with this stable ID already exists.");
        return library with { Revision = checked(library.Revision + 1), Collections = library.Collections.Append(collection).ToArray() };
    }

    public static ShelfLibrary AddMembership(ShelfLibrary library, ShelfCollectionMembership membership)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(membership);
        EnsureValid(library);
        var collection = library.Collections.FirstOrDefault(item => item.Id == membership.CollectionId);
        if (collection is null || collection.Kind != ShelfCollectionKind.Manual)
            throw new InvalidOperationException("Only an existing manual collection can receive explicit membership.");
        if (!library.Items.Any(item => item.Id == membership.LaunchItemId))
            throw new KeyNotFoundException("The Shelf launch item does not exist.");
        if (library.Memberships.Any(item => item.CollectionId == membership.CollectionId && item.LaunchItemId == membership.LaunchItemId))
            return library;
        return library with { Revision = checked(library.Revision + 1), Memberships = library.Memberships.Append(membership).ToArray() };
    }

    public static ShelfLibrary RemoveMembership(ShelfLibrary library, Guid collectionId, Guid launchItemId)
    {
        ArgumentNullException.ThrowIfNull(library);
        EnsureValid(library);
        var memberships = library.Memberships.Where(item => item.CollectionId != collectionId || item.LaunchItemId != launchItemId).ToArray();
        return memberships.Length == library.Memberships.Count
            ? library
            : library with { Revision = checked(library.Revision + 1), Memberships = memberships };
    }

    public static ShelfLibrary SetFavourite(ShelfLibrary library, Guid launchItemId, bool favourite)
    {
        ArgumentNullException.ThrowIfNull(library);
        EnsureValid(library);
        var found = false;
        var items = library.Items.Select(item =>
        {
            if (item.Id != launchItemId) return item;
            found = true;
            return item with { IsFavourite = favourite };
        }).ToArray();
        if (!found) throw new KeyNotFoundException("The Shelf launch item does not exist.");
        return library with { Revision = checked(library.Revision + 1), Items = items };
    }

    public static IReadOnlyList<ShelfLaunchItem> Search(ShelfLibrary library, string query)
    {
        ArgumentNullException.ThrowIfNull(library);
        EnsureValid(library);
        var terms = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return library.Items.Where(item => terms.All(term => SearchText(item).Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.Order).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<ShelfLaunchItem> ResolveCollection(ShelfLibrary library, Guid collectionId)
    {
        ArgumentNullException.ThrowIfNull(library);
        EnsureValid(library);
        var collection = library.Collections.FirstOrDefault(item => item.Id == collectionId)
            ?? throw new KeyNotFoundException("The Shelf collection does not exist.");
        if (collection.Kind == ShelfCollectionKind.Smart)
            return library.Items.Where(item => Matches(item, collection.Criteria!)).OrderBy(item => item.Order).ToArray();

        var order = library.Memberships.Where(item => item.CollectionId == collectionId)
            .ToDictionary(item => item.LaunchItemId, item => item.Order);
        return library.Items.Where(item => order.ContainsKey(item.Id))
            .OrderBy(item => order[item.Id]).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool Matches(ShelfLaunchItem item, ShelfSmartCollectionCriteria criteria)
    {
        if (criteria.TargetKinds is { Count: > 0 } kinds && !kinds.Contains(item.Target.Kind)) return false;
        if (criteria.FavouritesOnly == true && !item.IsFavourite) return false;
        var tags = (item.Tags ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (criteria.OfflineCapableOnly == true && !tags.Contains("offline")) return false;
        return (criteria.RequiredTags ?? []).All(tags.Contains);
    }

    private static bool SameTarget(ShelfTargetReference first, ShelfTargetReference second) =>
        first.Kind == second.Kind &&
        string.Equals(first.CanonicalId, second.CanonicalId, StringComparison.Ordinal) &&
        string.Equals(first.OwnerApp, second.OwnerApp, StringComparison.Ordinal) &&
        string.Equals(first.RouteId, second.RouteId, StringComparison.Ordinal) &&
        string.Equals(first.EntityType, second.EntityType, StringComparison.Ordinal) &&
        string.Equals(first.EntityId, second.EntityId, StringComparison.Ordinal) &&
        string.Equals(first.PlatformId, second.PlatformId, StringComparison.Ordinal) &&
        string.Equals(first.LaunchActivity, second.LaunchActivity, StringComparison.Ordinal) &&
        string.Equals(first.Location, second.Location, StringComparison.Ordinal) &&
        string.Equals(first.Uri, second.Uri, StringComparison.Ordinal) &&
        string.Equals(first.ProviderId, second.ProviderId, StringComparison.Ordinal);

    private static string SearchText(ShelfLaunchItem item) => string.Join(' ', item.Name, item.Target.CanonicalId,
        item.Target.OwnerApp, item.Target.RouteId, item.Target.EntityType, item.Target.EntityId,
        item.Target.Location, item.Target.Uri, string.Join(' ', item.Tags ?? []));

    private static void EnsureValid(ShelfLibrary library)
    {
        var errors = library.Validate();
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
    }
}
