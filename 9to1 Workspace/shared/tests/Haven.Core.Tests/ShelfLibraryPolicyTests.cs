using Haven.Application.Shelf;
using Haven.Core.Shelf;

namespace Haven.Core.Tests;

public sealed class ShelfLibraryPolicyTests
{
    [Fact]
    public void One_canonical_item_can_belong_to_multiple_collections_without_duplication()
    {
        var item = Project("Unreal project", Guid.NewGuid());
        var first = new ShelfCollection(Guid.NewGuid(), "Development", ShelfCollectionKind.Manual);
        var second = new ShelfCollection(Guid.NewGuid(), "Coursework", ShelfCollectionKind.Manual);
        var library = ShelfLibrary.Empty with { Items = [item], Collections = [first, second] };

        library = ShelfLibraryPolicy.AddMembership(library, new(first.Id, item.Id, 2));
        library = ShelfLibraryPolicy.AddMembership(library, new(second.Id, item.Id, 1));

        Assert.Single(library.Items);
        Assert.Equal(2, library.Memberships.Count);
        Assert.Empty(library.Validate());
    }

    [Fact]
    public void Adding_the_same_canonical_target_refreshes_the_item_instead_of_copying_it()
    {
        var original = Project("Old name", Guid.NewGuid());
        var library = ShelfLibrary.Empty with { Items = [original] };
        var refreshed = Project("Current name", Guid.NewGuid()) with { Target = original.Target };

        var result = ShelfLibraryPolicy.AddOrRefreshTarget(library, refreshed);

        Assert.Single(result.Items);
        Assert.Equal(original.Id, result.Items[0].Id);
        Assert.Equal("Current name", result.Items[0].Name);
    }

    [Fact]
    public void Smart_collections_filter_existing_items_using_declared_typed_metadata()
    {
        var offlineGame = new ShelfLaunchItem(Guid.NewGuid(), "Game One",
            new(ShelfTargetKind.InstalledApplication, "game.one"), Tags: ["game", "offline"]);
        var onlineGame = new ShelfLaunchItem(Guid.NewGuid(), "Game Two",
            new(ShelfTargetKind.InstalledApplication, "game.two"), Tags: ["game"]);
        var smart = new ShelfCollection(Guid.NewGuid(), "Offline games", ShelfCollectionKind.Smart,
            Criteria: new([ShelfTargetKind.InstalledApplication], ["game"], OfflineCapableOnly: true));
        var library = ShelfLibrary.Empty with { Items = [offlineGame, onlineGame], Collections = [smart] };

        var results = ShelfLibraryPolicy.ResolveCollection(library, smart.Id);

        Assert.Equal([offlineGame], results);
    }

    [Fact]
    public void Search_is_deterministic_and_includes_tags_and_canonical_metadata()
    {
        var item = new ShelfLaunchItem(Guid.NewGuid(), "Blender",
            new(ShelfTargetKind.InstalledApplication, "org.blender.Blender"), Tags: ["3d", "offline"], Order: 1);
        var library = ShelfLibrary.Empty with { Items = [item] };

        Assert.Equal([item], ShelfLibraryPolicy.Search(library, "offline blender"));
        Assert.Empty(ShelfLibraryPolicy.Search(library, "offline photoshop"));
    }

    [Fact]
    public void Cross_device_unavailable_item_remains_present_as_a_canonical_reference()
    {
        var item = Project("Remote project", Guid.NewGuid());
        var library = ShelfLibrary.Empty with { Items = [item] };

        Assert.Empty(library.Validate());
        Assert.Equal(item, ShelfLibraryPolicy.Search(library, "remote").Single());
    }

    [Fact]
    public void Explicit_equivalence_fields_are_part_of_target_identity()
    {
        var first = new ShelfLaunchItem(Guid.NewGuid(), "Home Library",
            new(ShelfTargetKind.NineToOneRoute, "home.library", OwnerApp: "home", RouteId: "home.library",
                EntityType: "artifact", EntityId: "artifact-1"));
        var second = new ShelfLaunchItem(Guid.NewGuid(), "Other artifact",
            first.Target with { EntityId = "artifact-2" });
        var library = ShelfLibrary.Empty with { Items = [first] };

        var result = ShelfLibraryPolicy.AddOrRefreshTarget(library, second);

        Assert.Equal(2, result.Items.Count);
        Assert.Empty(result.Validate());
    }

    [Fact]
    public void Smart_collection_does_not_accept_manual_membership()
    {
        var item = Project("Project", Guid.NewGuid());
        var smart = new ShelfCollection(Guid.NewGuid(), "Projects", ShelfCollectionKind.Smart,
            Criteria: new([ShelfTargetKind.Project]));
        var library = ShelfLibrary.Empty with { Items = [item], Collections = [smart] };

        Assert.Throws<InvalidOperationException>(() =>
            ShelfLibraryPolicy.AddMembership(library, new(smart.Id, item.Id)));
    }

    private static ShelfLaunchItem Project(string name, Guid projectId) =>
        new(Guid.NewGuid(), name, new(ShelfTargetKind.Project, projectId.ToString("D")));
}
