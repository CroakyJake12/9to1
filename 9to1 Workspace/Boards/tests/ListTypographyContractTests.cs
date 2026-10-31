using System.Text.Json;
using System.Text.Json.Nodes;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.Tests;

public sealed class ListTypographyContractTests
{
    [Theory]
    [InlineData(HavenRichBlockKind.Checklist)]
    [InlineData(HavenRichBlockKind.BulletList)]
    [InlineData(HavenRichBlockKind.NumberedList)]
    public async Task Row_format_survives_history_duplicate_and_physical_reopen(HavenRichBlockKind kind)
    {
        var root = Path.Combine(Path.GetTempPath(), "list-typography-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "rows.9to1board");
            await using (var session = await RichBoardSession.CreateNewAsync(store, "List typography"))
            {
                var pageId = session.Rich.Sections[0].Pages[0].Id;
                await session.MutateAsync(notes => notes.Sections[0].Pages[0].Blocks =
                    [new HavenRichBlock { Id = "list", Kind = kind, Items = [new HavenRichListItem { Id = "row", Text = "Formatted" }] }]);
                await session.MutateAsync(notes => Assert.True(HavenRichNotesOps.SetListItemFormatting(notes, pageId, "list", "row",
                    bold: true, italic: true, underline: true, strike: true, baseline: HavenRichBaseline.Superscript,
                    foreground: "#FF123456", fontFamily: "Georgia", fontSize: 24, background: "#FFFFFF00", lineSpacing: 2)));
                AssertFormat(session.Rich.Sections[0].Pages[0].Blocks.Single().Items.Single());
                Assert.True(await session.UndoAsync());
                Assert.Equal(0, session.Rich.Sections[0].Pages[0].Blocks.Single().Items.Single().FontSize);
                Assert.True(await session.RedoAsync());
                await session.MutateAsync(notes => HavenRichNotesOps.DuplicateBlock(notes, pageId, "list"));
                await session.SaveAsAsync(path);
            }
            Assert.True(File.Exists(path));
            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            Assert.Equal(2, reopened.Rich.Sections[0].Pages[0].Blocks.Count);
            Assert.All(reopened.Rich.Sections[0].Pages[0].Blocks, block =>
            {
                Assert.Equal(kind, block.Kind);
                AssertFormat(Assert.Single(block.Items));
            });
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Existing_schema5_without_additive_fields_reopens_with_inherited_values()
    {
        var root = Path.Combine(Path.GetTempPath(), "list-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "legacy.9to1board");
            await using (var session = await RichBoardSession.CreateNewAsync(store, "Old list"))
            {
                await session.MutateAsync(notes => notes.Sections[0].Pages[0].Blocks =
                    [new HavenRichBlock { Kind = HavenRichBlockKind.Checklist, Items = [new HavenRichListItem { Text = "Old row" }] }]);
                await session.SaveAsAsync(path);
            }
            var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            RemoveAddedFields(json);
            await File.WriteAllTextAsync(path, json.ToJsonString());
            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            var item = reopened.Rich.Sections[0].Pages[0].Blocks.Single().Items.Single();
            Assert.Equal("Old row", item.Text);
            Assert.Equal(string.Empty, item.FontFamily); Assert.Equal(0, item.FontSize);
            Assert.Equal(string.Empty, item.Background); Assert.Equal(0, item.LineSpacing);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(double.NaN, 1, "")]
    [InlineData(double.PositiveInfinity, 1, "")]
    [InlineData(-1, 1, "")]
    [InlineData(257, 1, "")]
    [InlineData(24, double.NaN, "")]
    [InlineData(24, 11, "")]
    [InlineData(24, 1, "not-a-colour")]
    public void Invalid_overrides_are_rejected_before_mutating_any_field(double size, double spacing, string background)
    {
        var notes = HavenRichNotes.Create(); var page = notes.Sections[0].Pages[0];
        var item = new HavenRichListItem { Id = "row" };
        page.Blocks = [new HavenRichBlock { Id = "list", Kind = HavenRichBlockKind.Checklist, Items = [item] }];
        var before = JsonSerializer.Serialize(notes);
        Assert.Throws<InvalidOperationException>(() => HavenRichNotesOps.SetListItemFormatting(notes, page.Id, "list", "row",
            bold: true, fontSize: size, background: background, lineSpacing: spacing));
        Assert.Equal(before, JsonSerializer.Serialize(notes));
        item.FontSize = size; item.Background = background; item.LineSpacing = spacing;
        Assert.Throws<InvalidOperationException>(() => HavenRichNotesValidator.Validate(notes));
    }

    private static void AssertFormat(HavenRichListItem item)
    {
        Assert.Equal("Georgia", item.FontFamily); Assert.Equal(24, item.FontSize);
        Assert.Equal("#FFFFFF00", item.Background); Assert.Equal(2, item.LineSpacing);
        Assert.True(item.Bold); Assert.True(item.Italic); Assert.True(item.Underline); Assert.True(item.StrikeThrough);
        Assert.Equal(HavenRichBaseline.Superscript, item.Baseline); Assert.Equal("#FF123456", item.Foreground);
    }

    private static void RemoveAddedFields(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            // Only list rows have checked/level: retain named-style and text-run fields.
            if (obj.ContainsKey("checked") && obj.ContainsKey("level"))
                foreach (var field in new[] { "fontFamily", "fontSize", "background", "lineSpacing" }) obj.Remove(field);
            foreach (var child in obj.Select(pair => pair.Value).OfType<JsonNode>().ToArray()) RemoveAddedFields(child);
        }
        else if (node is JsonArray array)
            foreach (var child in array.OfType<JsonNode>()) RemoveAddedFields(child);
    }
}
