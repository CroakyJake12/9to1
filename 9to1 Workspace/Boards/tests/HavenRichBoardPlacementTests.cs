using System.Text.Json;
using System.Text.Json.Nodes;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.Tests;

public sealed class HavenRichBoardPlacementTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Legacy_rich_page_and_blocks_default_to_vertical_flow()
    {
        var notes = HavenRichNotes.Create("Legacy board");
        var encoded = JsonSerializer.SerializeToNode(notes, Json)!.AsObject();
        var page = encoded["sections"]![0]!["pages"]![0]!.AsObject();
        page.Remove("layoutMode");
        var block = page["blocks"]![0]!.AsObject();
        block.Remove("layout");
        block.Remove("x");
        block.Remove("y");
        block.Remove("width");
        block.Remove("height");
        block.Remove("zOrder");

        var reopened = JsonSerializer.Deserialize<HavenRichNotes>(encoded.ToJsonString(), Json)!;
        var reopenedPage = Assert.Single(Assert.Single(reopened.Sections).Pages);
        var reopenedBlock = Assert.Single(reopenedPage.Blocks);

        Assert.Equal(HavenRichPageLayoutModes.Locked, reopenedPage.LayoutMode);
        Assert.Equal(HavenRichBlockLayouts.Flow, reopenedBlock.Layout);
        Assert.Equal((0d, 0d, 320d, 120d, 0),
            (reopenedBlock.X, reopenedBlock.Y, reopenedBlock.Width, reopenedBlock.Height, reopenedBlock.ZOrder));
        HavenRichNotesValidator.Validate(reopened);
    }

    [Fact]
    public void Layout_mode_and_absolute_geometry_round_trip_without_mode_switch_conversion()
    {
        var created = HavenBoardDocumentCodec.CreateNew("Placement");
        var notes = created.RichNotes!;
        var page = Assert.Single(Assert.Single(notes.Sections).Pages);
        var block = Assert.Single(page.Blocks);

        HavenRichNotesOps.SetPageLayoutMode(notes, page.Id, HavenRichPageLayoutModes.Unlocked);
        HavenRichNotesOps.PlaceBlock(notes, page.Id, block.Id, 72, 128, 420, 208, 5);
        HavenRichNotesValidator.Validate(notes);
        var firstReopen = HavenBoardDocumentCodec.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(created with { RichNotes = notes }, Json));
        var firstReopenedNotes = firstReopen.RichNotes!;
        var reopenedPage = Assert.Single(Assert.Single(firstReopenedNotes.Sections).Pages);
        var reopenedBlock = Assert.Single(reopenedPage.Blocks);
        Assert.Equal(HavenRichPageLayoutModes.Unlocked, reopenedPage.LayoutMode);
        Assert.Equal(HavenRichBlockLayouts.Absolute, reopenedBlock.Layout);
        Assert.Equal((72d, 128d, 420d, 208d, 5),
            (reopenedBlock.X, reopenedBlock.Y, reopenedBlock.Width, reopenedBlock.Height, reopenedBlock.ZOrder));

        HavenRichNotesOps.SetPageLayoutMode(firstReopenedNotes, reopenedPage.Id, HavenRichPageLayoutModes.Locked);
        var lockedReopen = HavenBoardDocumentCodec.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(firstReopen, Json));
        var lockedNotes = lockedReopen.RichNotes!;
        var lockedPage = Assert.Single(Assert.Single(lockedNotes.Sections).Pages);
        var lockedBlock = Assert.Single(lockedPage.Blocks);
        Assert.Equal(HavenRichPageLayoutModes.Locked, lockedPage.LayoutMode);
        Assert.Equal(HavenRichBlockLayouts.Absolute, lockedBlock.Layout);
        Assert.Equal((72d, 128d, 420d, 208d, 5),
            (lockedBlock.X, lockedBlock.Y, lockedBlock.Width, lockedBlock.Height, lockedBlock.ZOrder));

        HavenRichNotesOps.SetBlockFlowLayout(lockedNotes, lockedPage.Id, lockedBlock.Id);
        Assert.Equal(HavenRichBlockLayouts.Flow, lockedBlock.Layout);
        Assert.Equal((72d, 128d, 420d, 208d, 5),
            (lockedBlock.X, lockedBlock.Y, lockedBlock.Width, lockedBlock.Height, lockedBlock.ZOrder));
    }

    [Fact]
    public void Free_placement_requires_unlocked_mode_and_valid_geometry()
    {
        var notes = HavenRichNotes.Create();
        var page = Assert.Single(Assert.Single(notes.Sections).Pages);
        var block = Assert.Single(page.Blocks);

        Assert.Throws<InvalidOperationException>(() =>
            HavenRichNotesOps.PlaceBlock(notes, page.Id, block.Id, 12, 24, 300, 160, 1));
        HavenRichNotesOps.SetPageLayoutMode(notes, page.Id, HavenRichPageLayoutModes.Unlocked);
        HavenRichNotesOps.SetCanvasLocked(notes, page.Id, locked: true);
        Assert.Throws<InvalidOperationException>(() =>
            HavenRichNotesOps.PlaceBlock(notes, page.Id, block.Id, 12, 24, 300, 160, 1));
        HavenRichNotesOps.SetCanvasLocked(notes, page.Id, locked: false);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HavenRichNotesOps.PlaceBlock(notes, page.Id, block.Id, double.NaN, 24, 300, 160, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HavenRichNotesOps.PlaceBlock(notes, page.Id, block.Id, 12, 24, 0, 160, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HavenRichNotesOps.SetPageLayoutMode(notes, page.Id, "Floating"));
        Assert.Equal(HavenRichBlockLayouts.Flow, block.Layout);
    }
}
