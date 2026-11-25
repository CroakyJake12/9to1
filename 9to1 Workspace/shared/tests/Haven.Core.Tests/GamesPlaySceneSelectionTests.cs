using Haven.Core.Games;

namespace Haven.Core.Tests;

/// <summary>atlas:games / APP-GAMES-001: scene/graph selection shares IDs across hierarchy, viewport, components and assets.
/// Proves shared identity across views, single-selection semantics, multi-select toggle and stale-edit no-mutation.</summary>
public sealed class GamesPlaySceneSelectionTests
{
    private static readonly Guid Entity = Guid.NewGuid();
    private static readonly Guid Component = Guid.NewGuid();
    private static readonly Guid Asset = Guid.NewGuid();

    [Fact]
    public void SelectReplacesPriorSelectionWithSingleIdentity()
    {
        var context = GamesSelectionContext.Empty().Select(GamesSelectableKind.Entity, Entity, 1);
        Assert.Equal(2, context.Revision);
        Assert.Single(context.Selected);
        Assert.True(context.IsSelected(GamesSelectableKind.Entity, Entity));

        // Selecting a different identity replaces the prior selection (single-selection primary view).
        var replaced = context.Select(GamesSelectableKind.Asset, Asset, context.Revision);
        Assert.False(replaced.IsSelected(GamesSelectableKind.Entity, Entity));
        Assert.True(replaced.IsSelected(GamesSelectableKind.Asset, Asset));
    }

    [Fact]
    public void ViewsShareTheSameIdentityAcrossKinds()
    {
        // Hierarchy selects the entity; components view adds its component; both reference shared IDs.
        var context = GamesSelectionContext.Empty().Select(GamesSelectableKind.Entity, Entity, 1);
        context = context.Add(new GamesSelectionItem(GamesSelectableKind.Component, Component), context.Revision);

        Assert.True(context.IsSelected(GamesSelectableKind.Entity, Entity));
        Assert.True(context.IsSelected(GamesSelectableKind.Component, Component));
        // The same entity ID is recognized regardless of which view asks.
        Assert.True(context.IsSelected(GamesSelectableKind.Entity, Entity));
    }

    [Fact]
    public void ToggleAddsThenRemovesMembership()
    {
        var context = GamesSelectionContext.Empty();
        var item = new GamesSelectionItem(GamesSelectableKind.Asset, Asset);
        var added = context.Toggle(item, 1);
        Assert.True(added.IsSelected(GamesSelectableKind.Asset, Asset));

        var removed = added.Toggle(item, added.Revision);
        Assert.False(removed.IsSelected(GamesSelectableKind.Asset, Asset));
    }

    [Fact]
    public void AddIsIdempotentForAlreadySelectedIdentity()
    {
        var context = GamesSelectionContext.Empty().Add(new GamesSelectionItem(GamesSelectableKind.Entity, Entity), 1);
        // Re-adding the same identity does not change the selection or advance revision.
        Assert.Same(context, context.Add(new GamesSelectionItem(GamesSelectableKind.Entity, Entity), context.Revision));
    }

    [Fact]
    public void DuplicateSelectionIsRejected()
    {
        // Constructing a context with duplicate (kind,id) pairs is invalid.
        Assert.Throws<InvalidOperationException>(() => new GamesSelectionContext(1,
            [new GamesSelectionItem(GamesSelectableKind.Entity, Entity), new GamesSelectionItem(GamesSelectableKind.Entity, Entity)]).Capture());
    }

    [Fact]
    public void EmptyIdentityIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => new GamesSelectionItem(GamesSelectableKind.Entity, Guid.Empty).Capture());
    }

    [Fact]
    public void StaleEditNeverMutatesContext()
    {
        var context = GamesSelectionContext.Empty().Select(GamesSelectableKind.Entity, Entity, 1);
        Assert.Throws<InvalidOperationException>(() => context.Clear(99));
        Assert.Equal(2, context.Revision);
        Assert.Single(context.Selected);
    }

    [Fact]
    public void ClearEmptiesTheSelection()
    {
        var context = GamesSelectionContext.Empty().Select(GamesSelectableKind.Entity, Entity, 1);
        var cleared = context.Clear(context.Revision);
        Assert.Empty(cleared.Selected);
        Assert.False(cleared.IsSelected(GamesSelectableKind.Entity, Entity));
    }
}
