using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.Tests;

public sealed class HavenBoardFreeformContractTests
{
    [Fact]
    public void Freeform_frame_can_be_added_replaced_and_removed()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();

        var positioned = HavenBoardReducer.Apply(
            snapshot,
            new SetFreeformCardFrameCommand("card-1", 120, -40, 320, 180, 2));
        var first = Assert.Single(positioned.Freeform!.Items);
        Assert.Equal(new HavenBoardFreeformItem("card-1", 120, -40, 320, 180, 2), first);

        var replaced = HavenBoardReducer.Apply(
            positioned,
            new SetFreeformCardFrameCommand("card-1", 240, 60, 360, 200, 4));
        Assert.Equal(new HavenBoardFreeformItem("card-1", 240, 60, 360, 200, 4), Assert.Single(replaced.Freeform!.Items));

        var removed = HavenBoardReducer.Apply(replaced, new RemoveFreeformCardFrameCommand("card-1"));
        Assert.Empty(removed.Freeform!.Items);
    }

    [Fact]
    public void Freeform_layout_rejects_missing_cards_duplicate_frames_and_non_finite_or_unsafe_geometry()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();

        Assert.Throws<InvalidOperationException>(() => HavenBoardReducer.Apply(
            snapshot,
            new SetFreeformCardFrameCommand("missing", 0, 0)));
        Assert.Throws<InvalidOperationException>(() => HavenBoardReducer.Apply(
            snapshot,
            new SetFreeformCardFrameCommand("card-1", double.NaN, 0)));
        Assert.Throws<InvalidOperationException>(() => HavenBoardReducer.Apply(
            snapshot,
            new SetFreeformCardFrameCommand("card-1", 0, double.PositiveInfinity)));
        Assert.Throws<InvalidOperationException>(() => HavenBoardReducer.Apply(
            snapshot,
            new SetFreeformCardFrameCommand("card-1", 0, 0, 20, 20)));
        Assert.Throws<InvalidOperationException>(() => HavenBoardReducer.Apply(
            snapshot,
            new SetFreeformCardFrameCommand(
                "card-1",
                HavenBoardReducer.FreeformCoordinateLimit + 1,
                0)));

        var duplicate = snapshot with
        {
            Freeform = new HavenBoardFreeformLayout(
            [
                new HavenBoardFreeformItem("card-1", 0, 0),
                new HavenBoardFreeformItem("card-1", 300, 0)
            ])
        };
        Assert.Throws<InvalidOperationException>(() => HavenBoardReducer.Validate(duplicate));
    }

    [Fact]
    public void Lane_moves_and_hierarchy_changes_preserve_freeform_geometry()
    {
        var positioned = HavenBoardReducer.Apply(
            HavenBoardSnapshot.CreateDefault(),
            new SetFreeformCardFrameCommand("card-3", -120, 220, 300, 170, 5));
        var nested = HavenBoardReducer.Apply(positioned, new SetCardParentCommand("card-3", "card-1"));
        var moved = HavenBoardReducer.Apply(nested, new MoveCardCommand("done", 0, "doing", 1));

        Assert.Equal(
            new HavenBoardFreeformItem("card-3", -120, 220, 300, 170, 5),
            Assert.Single(moved.Freeform!.Items));
        Assert.Equal("card-1", FindCard(moved, "card-3").ParentCardId);
    }

    [Fact]
    public async Task Freeform_layout_round_trips_through_local_store()
    {
        var root = Path.Combine(Path.GetTempPath(), "cakeos-boards-freeform-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var snapshot = HavenBoardReducer.Apply(
                HavenBoardSnapshot.CreateDefault(),
                new SetFreeformCardFrameCommand("card-2", 512, 384, 340, 190, 7));

            await store.SaveAsync(snapshot);
            var loaded = await store.LoadAsync("board-main");

            Assert.NotNull(loaded);
            HavenBoardReducer.Validate(loaded);
            Assert.NotNull(snapshot.Freeform);
            Assert.NotNull(loaded.Freeform);
            Assert.Equal(snapshot.Freeform.Items.ToArray(), loaded.Freeform.Items.ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static HavenBoardCard FindCard(HavenBoardSnapshot snapshot, string cardId) =>
        snapshot.Groups.SelectMany(group => group.Cards).Single(card => card.Id == cardId);
}
