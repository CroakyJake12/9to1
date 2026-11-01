using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.Tests;

/// <summary>
/// Contract coverage for the page-level Snap dimension the current Boards reference shows alongside
/// Edit/View and the layout lock (V3-BOARDS-LAY-01, V3-BOARDS-FEA-03, V3-BOARDS-ACC-02). Snap decides
/// where a spatial manipulation commits; it never decides whether manipulation is allowed, and it is
/// persisted per page so a board reopens in the state the owner left it.
/// </summary>
public sealed class HavenRichBoardSnapTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Snap_is_off_until_the_owner_turns_it_on_and_survives_a_physical_reopen()
    {
        var created = HavenBoardDocumentCodec.CreateNew("Snap default");
        var notes = created.RichNotes!;
        var page = Assert.Single(Assert.Single(notes.Sections).Pages);
        Assert.False(page.SnapToGrid);

        Assert.True(HavenRichNotesOps.SetSnapToGrid(notes, page.Id, true));
        Assert.False(HavenRichNotesOps.SetSnapToGrid(notes, page.Id, true));
        HavenRichNotesValidator.Validate(notes);

        var reopened = HavenBoardDocumentCodec.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(created with { RichNotes = notes }, Json)).RichNotes!;
        Assert.True(Assert.Single(Assert.Single(reopened.Sections).Pages).SnapToGrid);
    }

    [Fact]
    public void A_legacy_page_that_omits_the_snap_field_loads_with_snap_off()
    {
        var created = HavenBoardDocumentCodec.CreateNew("Legacy snap");
        var encoded = JsonSerializer.SerializeToNode(created.RichNotes!, Json)!.AsObject();
        encoded["sections"]![0]!["pages"]![0]!.AsObject().Remove("snapToGrid");
        var reopened = JsonSerializer.Deserialize<HavenRichNotes>(encoded.ToJsonString(), Json)!;
        var page = Assert.Single(Assert.Single(reopened.Sections).Pages);
        Assert.False(page.SnapToGrid);
        HavenRichNotesValidator.Validate(reopened);
    }

    [Fact]
    public void Snap_on_commits_canvas_and_block_manipulation_to_the_grid_and_snap_off_leaves_it_free()
    {
        var created = HavenBoardDocumentCodec.CreateNew("Snap behaviour");
        var notes = created.RichNotes!;
        var page = Assert.Single(Assert.Single(notes.Sections).Pages);

        // Free first: an un-snapped manipulation must keep the exact position the pointer left.
        var box = HavenRichNotesOps.AddCanvasObject(notes, page.Id, "Text", "note", 40, 40);
        Assert.True(HavenRichNotesOps.MoveCanvasObject(notes, page.Id, box.Id, 137, 245));
        Assert.Equal((137d, 245d), (box.X, box.Y));
        Assert.True(HavenRichNotesOps.ResizeCanvasObject(notes, page.Id, box.Id, 261, 163));
        Assert.Equal((261d, 163d), (box.Width, box.Height));

        HavenRichNotesOps.SetPageLayoutMode(notes, page.Id, HavenRichPageLayoutModes.Unlocked);
        var block = Assert.Single(page.Blocks);
        HavenRichNotesOps.PlaceBlock(notes, page.Id, block.Id, 73, 129, 421, 209, 5);
        Assert.Equal((73d, 129d, 421d, 209d), (block.X, block.Y, block.Width, block.Height));

        // Now the same manipulations commit on the shared placement grid.
        Assert.True(HavenRichNotesOps.SetSnapToGrid(notes, page.Id, true));
        Assert.True(HavenRichNotesOps.MoveCanvasObject(notes, page.Id, box.Id, 137, 245));
        Assert.True(HavenRichNotesOps.ResizeCanvasObject(notes, page.Id, box.Id, 261, 163));
        HavenRichNotesOps.PlaceBlock(notes, page.Id, block.Id, 73, 129, 421, 209, 5);
        HavenRichNotesValidator.Validate(notes);

        const double grid = HavenRichNotesOps.PlacementGridUnits;
        Assert.Equal(0, box.X % grid);
        Assert.Equal(0, box.Y % grid);
        Assert.Equal(0, box.Width % grid);
        Assert.Equal(0, box.Height % grid);
        Assert.Equal(0, block.X % grid);
        Assert.Equal(0, block.Y % grid);
        Assert.Equal(0, block.Width % grid);
        Assert.Equal(0, block.Height % grid);

        // And turning it off again returns to free placement without disturbing stored geometry.
        Assert.True(HavenRichNotesOps.SetSnapToGrid(notes, page.Id, false));
        Assert.True(HavenRichNotesOps.MoveCanvasObject(notes, page.Id, box.Id, 139, 247));
        Assert.Equal((139d, 247d), (box.X, box.Y));

        var reopened = HavenBoardDocumentCodec.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(created with { RichNotes = notes }, Json)).RichNotes!;
        var reopenedPage = Assert.Single(Assert.Single(reopened.Sections).Pages);
        Assert.False(reopenedPage.SnapToGrid);
        var reopenedBox = Assert.Single(reopenedPage.Canvas);
        Assert.Equal((139d, 247d), (reopenedBox.X, reopenedBox.Y));
    }
}
