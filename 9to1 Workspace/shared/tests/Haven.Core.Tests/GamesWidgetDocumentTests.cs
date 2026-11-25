using System.Text.Json;
using Haven.Core.Games;
using Xunit;

namespace Haven.Core.Tests;

public class GamesWidgetDocumentTests
{
    [Fact]
    public void Constructs_menu_with_title_and_play_button()
    {
        var doc = GamesWidgetDocument.Create("menu", new[]
        {
            new GamesWidgetNode("title", "Title", GamesWidgetKind.Text, "Play", "title", "top", null, null),
            new GamesWidgetNode("play", "Play", GamesWidgetKind.Button, "Play", "button", "center", "on_play", "title")
        });
        Assert.Equal(1, doc.Revision);
        Assert.Equal(2, doc.Nodes.Count);
    }

    [Fact]
    public void Mutates_property_and_reorders()
    {
        var doc = GamesWidgetDocument.Create("menu", new[]
        {
            new GamesWidgetNode("title", "Title", GamesWidgetKind.Text, "Play", "title", "top", null, null),
            new GamesWidgetNode("play", "Play", GamesWidgetKind.Button, "Play", "button", "center", "on_play", "title")
        });
        var updated = doc.Update("play", "Start", "button", "center", "on_start", 1);
        Assert.Equal(2, updated.Revision);
        Assert.Equal("Start", updated.Nodes.First(n => n.Id == "play").Text);
        var reordered = updated.Reorder(new[] { "play", "title" }, 2);
        Assert.Equal("play", reordered.Nodes[0].Id);
        Assert.Equal("title", reordered.Nodes[1].Id);
    }

    [Fact]
    public void Rejects_duplicates_cycles_invalid_event_and_stale_edit()
    {
        Assert.Throws<ArgumentException>(() => GamesWidgetDocument.Create("menu", new[]
        {
            new GamesWidgetNode("a", "A", GamesWidgetKind.Panel, null, null, null, null, null),
            new GamesWidgetNode("a", "B", GamesWidgetKind.Panel, null, null, null, null, null)
        }));
        Assert.Throws<ArgumentException>(() => GamesWidgetDocument.Create("menu", new[]
        {
            new GamesWidgetNode("a", "A", GamesWidgetKind.Panel, null, null, null, null, "b"),
            new GamesWidgetNode("b", "B", GamesWidgetKind.Panel, null, null, null, null, "a")
        }));
        Assert.Throws<ArgumentException>(() => GamesWidgetDocument.Create("menu", new[]
        {
            new GamesWidgetNode("a", "A", GamesWidgetKind.Button, null, null, null, "bad-event", null)
        }));
        var doc = GamesWidgetDocument.Create("menu", new[]
        {
            new GamesWidgetNode("a", "A", GamesWidgetKind.Panel, null, null, null, null, null)
        });
        Assert.Throws<InvalidOperationException>(() => doc.Add(new GamesWidgetNode("b", "B", GamesWidgetKind.Panel, null, null, null, null, null), 99));
    }

    [Fact]
    public void The_public_document_snapshot_cannot_bypass_validation_or_edit_an_unknown_widget()
    {
        var node = new GamesWidgetNode("a", "A", GamesWidgetKind.Panel, null, null, null, null, null);
        var supplied = new List<GamesWidgetNode> { node };
        var doc = new GamesWidgetDocument("menu", 1, supplied);
        supplied.Clear();
        Assert.Equal(node, Assert.Single(doc.Nodes));
        Assert.Throws<ArgumentException>(() => doc.Reparent("absent", null, 1));
        Assert.Throws<ArgumentException>(() => doc.Update("absent", null, null, null, null, 1));
        Assert.Throws<ArgumentException>(() => new GamesWidgetDocument("menu", 1, new[] { node, node }));
        Assert.Throws<ArgumentException>(() => new GamesWidgetDocument("menu", 1,
            new[] { node with { Kind = (GamesWidgetKind)99 } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GamesWidgetDocument("menu", 0, new[] { node }));
    }

    [Fact]
    public void Json_round_trip_supported()
    {
        var doc = GamesWidgetDocument.Create("menu", new[]
        {
            new GamesWidgetNode("title", "Title", GamesWidgetKind.Text, "Play", "title", "top", null, null)
        });
        var json = JsonSerializer.Serialize(doc);
        var back = JsonSerializer.Deserialize<GamesWidgetDocument>(json);
        Assert.NotNull(back);
        Assert.Equal(doc.ScreenId, back!.ScreenId);
        Assert.Equal(doc.Revision, back.Revision);
        Assert.Equal(doc.Nodes.Count, back.Nodes.Count);
        Assert.Equal(doc.Nodes[0].Id, back.Nodes[0].Id);
    }
}
