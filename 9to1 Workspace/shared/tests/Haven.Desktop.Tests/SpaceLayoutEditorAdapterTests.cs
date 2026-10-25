using Haven.Application;
using Haven.Application.NodeGraph;
using Haven.Desktop.Views.Pages.Spaces;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class SpaceLayoutEditorAdapterTests
{
    [Fact]
    public void Space_layout_round_trips_through_the_canonical_node_editor_document()
    {
        var first = new SpaceLayoutNode(Guid.NewGuid(), "Context", "Registered context")
        {
            X = 20,
            Y = 40,
            Ports = [new SpaceLayoutPort("out", "Out", SpaceLayoutPortDirection.Output)],
            Metadata = new Dictionary<string, string> { ["source"] = "space" }
        };
        var second = new SpaceLayoutNode(Guid.NewGuid(), "AdditionalLogic", "Filter")
        {
            X = 320,
            Y = 40,
            Ports = [new SpaceLayoutPort("in", "In", SpaceLayoutPortDirection.Input)]
        };
        var edge = new SpaceLayoutEdge(Guid.NewGuid(), first.Id, "out", second.Id, "in") { Label = "context" };
        var source = new SpaceLayoutDocument([first, second], [edge]);

        var editor = SpaceLayoutEditorAdapter.ToEditor(source);
        var restored = SpaceLayoutEditorAdapter.FromEditor(editor);

        Assert.Equal(2, editor.Nodes.Count);
        Assert.Equal(NodeEditorPortDirection.Output, editor.Nodes[0].Ports[0].Direction);
        Assert.Equal(source.Nodes.Count, restored.Nodes.Count);
        Assert.Equal(first.Id, restored.Nodes[0].Id);
        Assert.Equal(first.Category, restored.Nodes[0].Category);
        Assert.Equal(first.Title, restored.Nodes[0].Title);
        Assert.Equal(first.Ports[0], restored.Nodes[0].Ports[0]);
        Assert.Equal("space", restored.Nodes[0].Metadata["source"]);
        Assert.Single(restored.Edges);
        Assert.Equal(edge.Id, restored.Edges[0].Id);
        Assert.Equal(edge.FromNodeId, restored.Edges[0].FromNodeId);
        Assert.Equal(edge.ToNodeId, restored.Edges[0].ToNodeId);
        Assert.Equal(edge.Label, restored.Edges[0].Label);
    }

    [Fact]
    public void User_message_palette_authors_the_registered_typed_graph_that_activation_accepts()
    {
        var now = DateTimeOffset.UtcNow;
        var space = new SpaceDefinition(Guid.NewGuid(), "Graph authoring", "", "space", SpaceKind.General,
            false, false, null, "", SpaceThinkingMode.Default, [], [], null, now, now);
        SpaceLayoutDocument? latestDraft = null;
        using var page = new SpaceLayoutEditorPage(space, draft => latestDraft = draft);

        var types = SpaceUserMessageGraph.Types();
        var profile = SpaceUserMessageGraph.Profile();
        Assert.Equal(types.Select(type => type.TypeId).ToArray(), page.UserMessagePalette.Select(entry => entry.TypeId).ToArray());
        Assert.All(page.UserMessagePalette, entry => Assert.Contains(entry.TypeId, profile.AllowedNodeTypes));
        foreach (var entry in page.UserMessagePalette) entry.Button.Invoke();

        var nodes = page.Editor.Document.Nodes;
        Assert.Equal(types.Count, nodes.Count);
        foreach (var type in types)
        {
            var authored = Assert.Single(nodes, node => node.TypeId == type.TypeId);
            var canonical = SpaceLayoutEditorAdapter.ToEditor(
                new SpaceLayoutDocument([SpaceUserMessageGraph.CreateNode(type.TypeId)], [])).Nodes.Single();
            Assert.Equal(type.Version, authored.SchemaVersion);
            Assert.Equal(canonical.Category, authored.Category);
            Assert.Equal(canonical.Title, authored.Title);
            Assert.Equal(type.Ports.Select(port => (port.Key, port.Direction, port.DataType, port.Multiple)),
                authored.Ports.Select(port => (port.Id, port.Direction == NodeEditorPortDirection.Input
                    ? GraphPortDirection.Input : GraphPortDirection.Output, port.DataType, port.AllowsMultipleConnections)));
            Assert.Equal(canonical.Metadata.Count, authored.Metadata.Count);
            foreach (var metadata in canonical.Metadata)
                Assert.Equal(metadata.Value, authored.Metadata[metadata.Key]);
        }

        var source = Assert.Single(nodes, node => node.TypeId == SpaceUserMessageGraph.SourceType);
        var truncate = Assert.Single(nodes, node => node.TypeId == SpaceUserMessageGraph.TruncateType);
        var output = Assert.Single(nodes, node => node.TypeId == SpaceUserMessageGraph.OutputType);
        page.Editor.SelectNode(truncate.Id);
        var maximumInput = Assert.Single(page.NodeConfigurationInputs,
            pair => pair.Key.EndsWith(".maximumTextElements", StringComparison.Ordinal)).Value;
        Assert.Equal("1000", maximumInput.Text);
        maximumInput.Text = "256";
        Assert.Equal("256", Assert.Single(page.Editor.Document.Nodes, node => node.Id == truncate.Id).Metadata["maximumTextElements"]);
        var extraNodes = nodes.Where(node => node.TypeId is not (SpaceUserMessageGraph.SourceType or
            SpaceUserMessageGraph.TruncateType or SpaceUserMessageGraph.OutputType)).ToArray();
        if (extraNodes.Length > 0)
        {
            page.Editor.SelectNode(extraNodes[0].Id);
            foreach (var extra in extraNodes.Skip(1)) page.Editor.SelectNode(extra.Id, additive: true);
            page.Editor.DeleteSelection();
        }
        Assert.True(page.Editor.Connect(source.Id, "out", truncate.Id, "in"));
        Assert.True(page.Editor.Connect(truncate.Id, "out", output.Id, "in"));
        Assert.NotNull(latestDraft);
        var compiled = SpaceUserMessageGraph.Compile(space.Id, space.Revision, latestDraft!);
        Assert.Equal(space.Id, compiled.SpaceId);
        Assert.Equal(space.Revision, compiled.SpaceRevision);
    }

    [Fact]
    public void User_message_palette_authors_an_executable_typed_numbers_chain()
    {
        var (page, space, latestDraft) = NewPage();
        using (page)
        {
            foreach (var type in new[] { SpaceUserMessageGraph.SourceType, SpaceUserMessageGraph.ExtractNumbersType,
                         SpaceUserMessageGraph.NumbersToTextType, SpaceUserMessageGraph.OutputType })
                Assert.Contains(page.UserMessagePalette, entry => entry.TypeId == type);
            Add(page, SpaceUserMessageGraph.SourceType);
            Add(page, SpaceUserMessageGraph.ExtractNumbersType);
            Add(page, SpaceUserMessageGraph.NumbersToTextType);
            Add(page, SpaceUserMessageGraph.OutputType);

            var nodes = page.Editor.Document.Nodes.ToDictionary(node => node.TypeId, StringComparer.Ordinal);
            Assert.True(page.Editor.Connect(nodes[SpaceUserMessageGraph.SourceType].Id, "out",
                nodes[SpaceUserMessageGraph.ExtractNumbersType].Id, "in"));
            Assert.True(page.Editor.Connect(nodes[SpaceUserMessageGraph.ExtractNumbersType].Id, "out",
                nodes[SpaceUserMessageGraph.NumbersToTextType].Id, "in"));
            Assert.True(page.Editor.Connect(nodes[SpaceUserMessageGraph.NumbersToTextType].Id, "out",
                nodes[SpaceUserMessageGraph.OutputType].Id, "in"));

            Assert.NotNull(latestDraft());
            Assert.Equal(space.Id, SpaceUserMessageGraph.Compile(space.Id, space.Revision, latestDraft()!).SpaceId);
        }
    }

    [Fact]
    public void User_message_palette_authors_and_configures_an_executable_phrase_search_chain()
    {
        var (page, space, latestDraft) = NewPage();
        using (page)
        {
            Add(page, SpaceUserMessageGraph.SourceType);
            Add(page, SpaceUserMessageGraph.SearchWordsType);
            Add(page, SpaceUserMessageGraph.MatchesToTextType);
            Add(page, SpaceUserMessageGraph.OutputType);

            var nodes = page.Editor.Document.Nodes.ToDictionary(node => node.TypeId, StringComparer.Ordinal);
            page.Editor.SelectNode(nodes[SpaceUserMessageGraph.SearchWordsType].Id);
            var query = Assert.Single(page.NodeConfigurationInputs,
                pair => pair.Key.EndsWith(".query", StringComparison.Ordinal)).Value;
            Assert.Equal("word", query.Text);
            query.Text = "permission scope";
            Assert.Equal("permission scope", Assert.Single(page.Editor.Document.Nodes,
                node => node.TypeId == SpaceUserMessageGraph.SearchWordsType).Metadata["query"]);
            Assert.True(page.Editor.Connect(nodes[SpaceUserMessageGraph.SourceType].Id, "out",
                nodes[SpaceUserMessageGraph.SearchWordsType].Id, "in"));
            Assert.True(page.Editor.Connect(nodes[SpaceUserMessageGraph.SearchWordsType].Id, "out",
                nodes[SpaceUserMessageGraph.MatchesToTextType].Id, "in"));
            Assert.True(page.Editor.Connect(nodes[SpaceUserMessageGraph.MatchesToTextType].Id, "out",
                nodes[SpaceUserMessageGraph.OutputType].Id, "in"));

            Assert.NotNull(latestDraft());
            Assert.Equal(space.Id, SpaceUserMessageGraph.Compile(space.Id, space.Revision, latestDraft()!).SpaceId);
        }
    }

    private static void Add(SpaceLayoutEditorPage page, string typeId) =>
        Assert.Single(page.UserMessagePalette, entry => entry.TypeId == typeId).Button.Invoke();

    private static (SpaceLayoutEditorPage Page, SpaceDefinition Space, Func<SpaceLayoutDocument?> LatestDraft) NewPage()
    {
        var now = DateTimeOffset.UtcNow;
        var space = new SpaceDefinition(Guid.NewGuid(), "Graph authoring", "", "space", SpaceKind.General,
            false, false, null, "", SpaceThinkingMode.Default, [], [], null, now, now);
        SpaceLayoutDocument? latestDraft = null;
        return (new SpaceLayoutEditorPage(space, draft => latestDraft = draft), space, () => latestDraft);
    }
}
