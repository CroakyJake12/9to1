using Haven.Application.Shelf;
using Haven.Core.Shelf;

namespace Haven.Core.Tests;

public sealed class ShelfCollectionOrderingTests
{
    [Fact]
    public void Manual_order_is_collection_specific_and_preserves_canonical_targets_and_user_metadata()
    {
        var (library, first, second, items) = Fixture();
        var changed = ShelfLibraryPolicy.ReorderCollection(library, first.Id, [items[1].Id, items[0].Id]);
        Assert.Equal([items[1].Id, items[0].Id], ShelfLibraryPolicy.ResolveCollection(changed, first.Id).Select(item => item.Id));
        Assert.Equal([items[0].Id, items[1].Id], ShelfLibraryPolicy.ResolveCollection(changed, second.Id).Select(item => item.Id));
        Assert.Equal(library.Items, changed.Items); Assert.Equal(library.Collections, changed.Collections);
        Assert.Equal(library.Memberships.Where(item => item.CollectionId == second.Id), changed.Memberships.Where(item => item.CollectionId == second.Id));
        Assert.Equal([items[0].Id, items[1].Id], ShelfLibraryPolicy.ResolveCollection(library, first.Id).Select(item => item.Id));
        Assert.Empty(changed.Validate());
    }

    [Fact]
    public void Partial_duplicate_foreign_and_smart_orders_do_not_change_memberships()
    {
        var (library, first, _, items) = Fixture();
        Assert.Throws<ArgumentException>(() => ShelfLibraryPolicy.ReorderCollection(library, first.Id, [items[0].Id]));
        Assert.Throws<ArgumentException>(() => ShelfLibraryPolicy.ReorderCollection(library, first.Id, [items[0].Id, items[0].Id]));
        Assert.Throws<ArgumentException>(() => ShelfLibraryPolicy.ReorderCollection(library, first.Id, [items[0].Id, Guid.NewGuid()]));
        var smart = new ShelfCollection(Guid.NewGuid(), "All projects", ShelfCollectionKind.Smart, Criteria: new([ShelfTargetKind.Project]));
        Assert.Throws<InvalidOperationException>(() => ShelfLibraryPolicy.ReorderCollection(library with { Collections = library.Collections.Append(smart).ToArray() }, smart.Id, []));
        Assert.Equal([items[0].Id, items[1].Id], ShelfLibraryPolicy.ResolveCollection(library, first.Id).Select(item => item.Id));
    }

    private static (ShelfLibrary Library, ShelfCollection First, ShelfCollection Second, ShelfLaunchItem[] Items) Fixture()
    {
        var items = new[] {
            new ShelfLaunchItem(Guid.NewGuid(), "First project", new(ShelfTargetKind.Project, Guid.NewGuid().ToString("D")), Tags: ["course"], IsFavourite: true, Order: 0),
            new ShelfLaunchItem(Guid.NewGuid(), "Second project", new(ShelfTargetKind.Project, Guid.NewGuid().ToString("D"), Arguments: ["retained"]), Order: 10) };
        var first = new ShelfCollection(Guid.NewGuid(), "Work", ShelfCollectionKind.Manual);
        var second = new ShelfCollection(Guid.NewGuid(), "Study", ShelfCollectionKind.Manual);
        return (ShelfLibrary.Empty with { Items = items, Collections = [first, second], Memberships =
            [new(first.Id, items[0].Id, 10), new(first.Id, items[1].Id, 20), new(second.Id, items[0].Id, 5), new(second.Id, items[1].Id, 9)] }, first, second, items);
    }
}
