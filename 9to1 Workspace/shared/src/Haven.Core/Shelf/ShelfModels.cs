namespace Haven.Core.Shelf;

public enum ShelfTargetKind { InstalledApplication, NineToOneRoute, Project, File, Folder, WebAddress, Shortcut, AndroidApplication }
public enum ShelfLaunchBehaviour { Open, OpenWith, Reveal, Launch }
public enum ShelfCollectionKind { Manual, Smart }
public enum ShelfPresentation { List, Grid, Cards }

/// <summary>Target-owned canonical identity; Shelf never derives cross-platform equivalences.</summary>
public sealed record ShelfTargetReference(
    ShelfTargetKind Kind,
    string CanonicalId,
    string? OwnerApp = null,
    string? RouteId = null,
    string? EntityType = null,
    string? EntityId = null,
    string? PlatformId = null,
    string? LaunchActivity = null,
    string? Location = null,
    bool? IsDirectory = null,
    string? Uri = null,
    string? ProviderId = null,
    IReadOnlyList<string>? Arguments = null,
    string? WorkingDirectory = null);

public sealed record ShelfLaunchItem(Guid Id, string Name, ShelfTargetReference Target,
    string? IconReference = null, IReadOnlyList<string>? Tags = null, bool IsFavourite = false,
    int Order = 0, ShelfLaunchBehaviour Behaviour = ShelfLaunchBehaviour.Open);

public sealed record ShelfSmartCollectionCriteria(IReadOnlyList<ShelfTargetKind>? TargetKinds = null,
    IReadOnlyList<string>? RequiredTags = null, bool? FavouritesOnly = null, bool? OfflineCapableOnly = null);

public sealed record ShelfCollection(Guid Id, string Name, ShelfCollectionKind Kind,
    ShelfPresentation Presentation = ShelfPresentation.Grid, ShelfSmartCollectionCriteria? Criteria = null, int Order = 0);

/// <summary>Separate membership enables one item to appear in multiple collections without duplication.</summary>
public sealed record ShelfCollectionMembership(Guid CollectionId, Guid LaunchItemId, int Order = 0);

public sealed record ShelfLibrary(int SchemaVersion, long Revision, IReadOnlyList<ShelfLaunchItem> Items,
    IReadOnlyList<ShelfCollection> Collections, IReadOnlyList<ShelfCollectionMembership> Memberships)
{
    public const int CurrentSchemaVersion = 1;
    public static ShelfLibrary Empty { get; } = new(CurrentSchemaVersion, 0, [], [], []);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (SchemaVersion != CurrentSchemaVersion) errors.Add($"Unsupported Shelf schema version {SchemaVersion}.");
        if (Revision < 0) errors.Add("Shelf revision cannot be negative.");
        if (Items is null || Collections is null || Memberships is null)
        {
            errors.Add("Shelf item, collection and membership lists are required.");
            return errors;
        }
        if (Items.Any(item => item is null) || Collections.Any(collection => collection is null)
            || Memberships.Any(membership => membership is null))
        {
            errors.Add("Shelf lists cannot contain null entries.");
            return errors;
        }
        if (Items.Any(item => item is null || item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.Name) || !IsValidTarget(item.Target)))
            errors.Add("Shelf contains a launch item with a missing ID, name or valid canonical target.");
        if (Items.Select(item => item.Id).Distinct().Count() != Items.Count)
            errors.Add("Shelf contains duplicate launch item IDs.");
        if (Collections.Any(collection => collection is null || collection.Id == Guid.Empty || string.IsNullOrWhiteSpace(collection.Name)
            || (collection.Kind == ShelfCollectionKind.Smart) != (collection.Criteria is not null)))
            errors.Add("Shelf contains an invalid collection identity or smart-collection criteria.");
        if (Collections.Select(collection => collection.Id).Distinct().Count() != Collections.Count)
            errors.Add("Shelf contains duplicate collection IDs.");
        var itemIds = Items.Select(item => item.Id).ToHashSet();
        var collectionIds = Collections.Select(collection => collection.Id).ToHashSet();
        if (Memberships.Any(membership => !collectionIds.Contains(membership.CollectionId) || !itemIds.Contains(membership.LaunchItemId)
            || Collections.First(collection => collection.Id == membership.CollectionId).Kind != ShelfCollectionKind.Manual))
            errors.Add("Shelf membership references a missing item or collection, or assigns an item to a smart collection.");
        if (Memberships.Distinct().Count() != Memberships.Count)
            errors.Add("Shelf contains duplicate collection memberships.");
        return errors;
    }

    public static bool IsValidTarget(ShelfTargetReference? target)
    {
        if (target is null || string.IsNullOrWhiteSpace(target.CanonicalId)) return false;
        return target.Kind switch
        {
            ShelfTargetKind.InstalledApplication => true,
            ShelfTargetKind.NineToOneRoute => !string.IsNullOrWhiteSpace(target.OwnerApp) && !string.IsNullOrWhiteSpace(target.RouteId),
            ShelfTargetKind.Project => Guid.TryParse(target.CanonicalId, out var id) && id != Guid.Empty,
            ShelfTargetKind.File => !string.IsNullOrWhiteSpace(target.Location) && target.IsDirectory == false,
            ShelfTargetKind.Folder => !string.IsNullOrWhiteSpace(target.Location) && target.IsDirectory == true,
            ShelfTargetKind.WebAddress => Uri.TryCreate(target.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https",
            ShelfTargetKind.Shortcut => !string.IsNullOrWhiteSpace(target.ProviderId),
            ShelfTargetKind.AndroidApplication => !string.IsNullOrWhiteSpace(target.PlatformId) && !string.IsNullOrWhiteSpace(target.LaunchActivity),
            _ => false
        };
    }
}
