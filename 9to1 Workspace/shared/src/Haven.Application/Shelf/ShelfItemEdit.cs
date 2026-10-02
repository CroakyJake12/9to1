using Haven.Core.Shelf;
namespace Haven.Application.Shelf;
/// <summary>Local organisation fields only; this proposal does not change or open a target.
/// The owning review detaches Tags before any asynchronous work. This object grants no access.</summary>
public sealed record ShelfItemEdit(Guid ItemID, string Name, IReadOnlyList<string> Tags,
    bool IsFavourite, int Order, ShelfLaunchBehaviour Behaviour)
{
    public const int MaximumNameCharacters = 4096;
    public const int MaximumTags = 256;
    public const int MaximumTagCharacters = 4096;
}
