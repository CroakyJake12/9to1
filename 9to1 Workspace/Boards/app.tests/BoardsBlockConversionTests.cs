using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsBlockConversionTests
{
    [Theory]
    [InlineData(HavenRichBlockKind.Paragraph, HavenRichBlockKind.Heading)]
    [InlineData(HavenRichBlockKind.Heading, HavenRichBlockKind.Paragraph)]
    public async Task Conversion_keeps_full_payload_identity_one_undo_and_reopen(
        HavenRichBlockKind from, HavenRichBlockKind to)
    {
        await WithBoardAsync(from, async (store, adapter, path, original) =>
        {
            var before = adapter.Snapshot;
            var target = Capture(adapter);
            Assert.True(adapter.GetBlockConversionCapability(target, to).CanApply);
            var result = await adapter.ApplyBlockConversionAsync(target, to, () => true);
            Assert.True(result.Applied);
            Assert.Equal("block-payload", result.SelectedBlockId);
            await adapter.SaveAsync();
            var saved = (await store.LoadDocumentAtPathAsync(path))!.RichNotes!.Sections.Single().Pages.Single().Blocks.Single();
            Assert.Equal(to, saved.Kind);
            saved.Kind = from; // Compare every other serialized field, including dormant payloads.
            Assert.Equal(original, JsonSerializer.Serialize(saved));

            Assert.True(await adapter.UndoAsync());
            Assert.Equal(before, adapter.Snapshot);
            Assert.False(adapter.CanUndo);
            Assert.True(await adapter.RedoAsync());
            var converted = adapter.Snapshot;
            var noChange = await adapter.ApplyBlockConversionAsync(Capture(adapter), to, () => true);
            Assert.False(noChange.Applied);
            Assert.Equal("NoChange", noChange.Code);
            Assert.Equal(converted, adapter.Snapshot);
            await adapter.SaveAsync();
            await using (var reopened = await ContractSessionAdapter.OpenAsync(store, path))
                Assert.Equal(converted, reopened.Snapshot);
            Assert.True(await adapter.UndoAsync());
            Assert.Equal(before, adapter.Snapshot);
            Assert.False(adapter.CanUndo); // The no-op did not add a second transaction.
        });
    }

    [Theory]
    [InlineData("forged")]
    [InlineData("read-only")]
    [InlineData("revoked-during-apply")]
    [InlineData("stale-view")]
    [InlineData("stale-revision")]
    [InlineData("cancelled")]
    public async Task Refused_conversion_never_changes_content_or_adds_history(string refusal)
    {
        await WithBoardAsync(HavenRichBlockKind.Paragraph, async (_, adapter, _, _) =>
        {
            var target = Capture(adapter);
            var original = adapter.Snapshot;
            if (refusal == "forged") target = target with { };
            if (refusal == "stale-view")
                adapter.Document.Sections.Single().Pages.Single().Blocks.Single().Text = "Newer pending text";
            if (refusal == "stale-revision")
                Assert.True((await adapter.ApplyBlockActionAsync(target, BoardBlockAction.Duplicate, () => true)).Applied);
            var before = adapter.Snapshot;
            var couldUndo = adapter.CanUndo;
            var checks = 0;
            bool Editable() => refusal != "read-only" && (refusal != "revoked-during-apply" || ++checks == 1);
            using var cancellation = new CancellationTokenSource();
            if (refusal == "cancelled") cancellation.Cancel();
            if (refusal == "cancelled")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    await adapter.ApplyBlockConversionAsync(target, HavenRichBlockKind.Heading, Editable, cancellation.Token));
            else
                Assert.False((await adapter.ApplyBlockConversionAsync(target, HavenRichBlockKind.Heading, Editable)).Applied);
            Assert.Equal(before, adapter.Snapshot);
            Assert.Equal(couldUndo, adapter.CanUndo);
            if (refusal == "stale-revision")
            {
                Assert.True(await adapter.UndoAsync());
                Assert.Equal(original, adapter.Snapshot);
                Assert.False(adapter.CanUndo);
            }
        });
    }

    [Theory]
    [InlineData(HavenRichBlockKind.Checklist)]
    [InlineData(HavenRichBlockKind.BulletList)]
    [InlineData(HavenRichBlockKind.NumberedList)]
    [InlineData(HavenRichBlockKind.Table)]
    [InlineData(HavenRichBlockKind.Image)]
    [InlineData(HavenRichBlockKind.Divider)]
    public async Task Unimplemented_lossless_mapping_refuses_without_blank_replacement(HavenRichBlockKind kind)
    {
        await WithBoardAsync(HavenRichBlockKind.Paragraph, async (_, adapter, _, _) =>
        {
            var before = adapter.Snapshot;
            var target = Capture(adapter);
            Assert.False(adapter.GetBlockConversionCapability(target, kind).CanApply);
            var result = await adapter.ApplyBlockConversionAsync(target, kind, () => true);
            Assert.False(result.Applied);
            Assert.Equal("UnsupportedConversion", result.Code);
            Assert.Equal(before, adapter.Snapshot);
            Assert.False(adapter.CanUndo);
        });
    }

    private static BoardBlockActionTarget Capture(ContractSessionAdapter adapter) =>
        adapter.CaptureBlockAction(adapter.Document.Sections.Single().Pages.Single().Id, "block-payload")!;

    private static async Task WithBoardAsync(HavenRichBlockKind kind,
        Func<JsonFileHavenBoardStore, ContractSessionAdapter, string, string, Task> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-conversion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "conversion.9to1board");
            string original;
            await using (var owner = await RichBoardSession.CreateNewAsync(store, "Conversion owner"))
            {
                await owner.MutateAsync(rich =>
                {
                    var page = rich.Sections.Single().Pages.Single();
                    page.Blocks.Clear(); page.LayoutMode = HavenRichPageLayoutModes.Unlocked;
                    var block = HavenRichBlock.Paragraph("AlphaBeta");
                    block.Id = "block-payload"; block.Kind = kind;
                    block.Layout = HavenRichBlockLayouts.Absolute; block.X = 23.5; block.Y = 47;
                    block.Width = 410; block.Height = 85; block.ZOrder = 3;
                    block.StyleId = "quote"; block.Alignment = HavenRichAlignment.Right;
                    block.LineSpacing = 1.7; block.SpaceBefore = 12; block.SpaceAfter = 9; block.IndentLevel = 2;
                    block.Runs = [new() { Text = "Alpha" }, new() { Text = "Beta", Bold = true, Italic = true,
                        Underline = true, StrikeThrough = true, Baseline = HavenRichBaseline.Superscript,
                        FontFamily = "Georgia", FontSize = 19, Foreground = "#FF112233", Background = "#FF445566" }];
                    block.Items = [new() { Id = "retained-item", Text = "retained", Checked = true, Level = 2 }];
                    block.Table = HavenRichTable.Create(1, 1); block.Table.Rows[0].Cells[0].Text = "retained cell";
                    block.Attachment = new() { Id = "retained-asset", DisplayName = "asset.bin", MediaType = "application/octet-stream",
                        DataBase64 = "AQID", SizeBytes = 3 };
                    page.Blocks.Add(block);
                });
                await owner.SaveAsAsync(path);
                original = JsonSerializer.Serialize(owner.Rich.Sections.Single().Pages.Single().Blocks.Single());
            }
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            await check(store, adapter, path, original);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
