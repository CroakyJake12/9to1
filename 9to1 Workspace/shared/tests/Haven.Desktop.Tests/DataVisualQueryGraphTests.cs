using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Data;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class DataVisualQueryGraphTests
{
    [AvaloniaFact]
    public void Relationship_draft_rejects_missing_foreign_key_and_never_autosaves_or_calls_owner()
    {
        var workbook = RelationshipWorkbook(); var before = System.Text.Json.JsonSerializer.Serialize(workbook);
        var designer = new RelationshipReviewRecorder(workbook);
        using var panel = new DataRelationshipDesignPanel(() => workbook, _ => throw new InvalidOperationException("Unexpected persistence"), designer, () => true);
        panel.DescendantsAndSelf().OfType<Toggle>().Single(item => item.Name?.EndsWith(workbook.Tables[0].Fields[0].FieldID.ToString("N"), StringComparison.Ordinal) == true).IsChecked = false;
        InvokeRelationshipButton(panel, "Data.Relationship.Review");
        Assert.Equal(0, designer.Reviews); Assert.Equal(0, designer.Commits);
        Assert.Contains("Draft retained", panel.DescendantsAndSelf().OfType<Text>().Single(item => item.Name == "Data.Relationship.Status").Content);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(workbook));
    }

    [AvaloniaFact]
    public void Relationship_draft_change_invalidates_exact_review_and_stale_workbook_retains_visible_name()
    {
        var workbook = RelationshipWorkbook(); var designer = new RelationshipReviewRecorder(workbook);
        using var panel = new DataRelationshipDesignPanel(() => workbook, _ => throw new InvalidOperationException("Unexpected persistence"), designer, () => true);
        var name = panel.DescendantsAndSelf().OfType<Input>().Single(item => item.Name == "Data.Relationship.Name");
        name.Text = "Self reference"; InvokeRelationshipButton(panel, "Data.Relationship.Review");
        Assert.Equal(1, designer.Reviews);
        name.Text = "Changed after review"; InvokeRelationshipButton(panel, "Data.Relationship.Apply");
        Assert.Equal(0, designer.Commits);
        workbook.Version++; workbook.RevisionId = Guid.NewGuid(); panel.Refresh();
        Assert.Equal("Changed after review", panel.DescendantsAndSelf().OfType<Input>().Single(item => item.Name == "Data.Relationship.Name").Text);
        Assert.Contains("draft is retained", panel.DescendantsAndSelf().OfType<Text>().Single(item => item.Name == "Data.Relationship.Status").Content);
        InvokeRelationshipButton(panel, "Data.Relationship.Review"); Assert.Equal(1, designer.Reviews);
        InvokeRelationshipButton(panel, "Data.Relationship.Reload");
        Assert.Equal("New relationship", panel.DescendantsAndSelf().OfType<Input>().Single(item => item.Name == "Data.Relationship.Name").Text);
    }

    [AvaloniaFact]
    public async Task Relationship_review_callback_cannot_replace_a_switched_workbook_draft()
    {
        var first = RelationshipWorkbook(); var second = RelationshipWorkbook(); var current = first;
        var designer = new HeldRelationshipReview(first);
        using var panel = new DataRelationshipDesignPanel(() => current, _ => throw new InvalidOperationException("Unexpected persistence"), designer, () => true);
        panel.DescendantsAndSelf().OfType<Input>().Single(item => item.Name == "Data.Relationship.Name").Text = "First draft";
        InvokeRelationshipButton(panel, "Data.Relationship.Review");
        Assert.NotNull(designer.Review);
        current = second; panel.Refresh();
        designer.Release();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!panel.GetValue(HavenProperties.Enabled) && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(panel.GetValue(HavenProperties.Enabled));
        InvokeRelationshipButton(panel, "Data.Relationship.Apply"); Assert.Equal(0, designer.Commits);
        Assert.Equal("New relationship", panel.DescendantsAndSelf().OfType<Input>().Single(item => item.Name == "Data.Relationship.Name").Text);
        current = first; panel.Refresh();
        Assert.Equal("First draft", panel.DescendantsAndSelf().OfType<Input>().Single(item => item.Name == "Data.Relationship.Name").Text);
        InvokeRelationshipButton(panel, "Data.Relationship.Apply"); Assert.Equal(0, designer.Commits);
    }
    private sealed class HeldRelationshipReview(DataWorkbook workbook) : IDataRelationshipDesigner
    {
        private readonly TaskCompletionSource<DataRelationshipReview> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DataRelationshipReview? Review { get; private set; }
        public int Commits { get; private set; }
        public Task<DataRelationshipReview> ReviewAsync(Guid workbookID, int expectedVersion, Guid expectedRevision,
            DataRelationshipMutationKind kind, DataRelationshipDefinition relationship, long? expectedRelationshipRevision,
            CancellationToken cancellationToken = default)
        {
            Review = new("held-ui-boundary-review", DataRelationshipUpdateIntent.Capture(Guid.NewGuid(), workbook, kind, relationship, expectedRelationshipRevision));
            return _completion.Task;
        }
        public void Release() => _completion.SetResult(Review!);
        public Task<DataRelationshipDesignerCommit> CommitAsync(DataRelationshipReview review, CancellationToken cancellationToken = default)
        { Commits++; return Task.FromResult(new DataRelationshipDesignerCommit(false, "ApprovalRequired", null, false)); }
    }

    private static DataWorkbook RelationshipWorkbook()
    {
        var workbook = DataWorkbook.Create("Relationships"); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "ID"); sheet.SetCell(1, 0, "1");
        var table = new DataTableDefinition { SheetId = sheet.Id, Name = "Employees", Range = new() { EndRow = 1, EndColumn = 0 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table); workbook.Normalize();
        var key = new DataKeyDefinition(Guid.NewGuid(), "Employee ID", DataKeyKind.Primary, [table.Fields[0].FieldID]);
        workbook = DataTableDesign.SetSchema(workbook, table.Id, 0, Guid.Empty, null,
            [new(table.Fields[0].FieldID, "ID", DataFieldType.Integer, false)], [key]).Workbook!;
        workbook.Version = 1; workbook.RevisionId = Guid.NewGuid(); return workbook;
    }
    private static void InvokeRelationshipButton(Container panel, string name)
    {
        var button = panel.DescendantsAndSelf().OfType<Button>().Single(item => item.Name == name);
        button.KeyDown(new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None));
        button.KeyUp(new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None));
    }
    private sealed class RelationshipReviewRecorder(DataWorkbook workbook) : IDataRelationshipDesigner
    {
        public int Reviews { get; private set; }
        public int Commits { get; private set; }
        public Task<DataRelationshipReview> ReviewAsync(Guid workbookID, int expectedVersion, Guid expectedRevision,
            DataRelationshipMutationKind kind, DataRelationshipDefinition relationship, long? expectedRelationshipRevision,
            CancellationToken cancellationToken = default)
        {
            Reviews++;
            return Task.FromResult(new DataRelationshipReview("actual-ui-boundary-review", DataRelationshipUpdateIntent.Capture(
                Guid.NewGuid(), workbook, kind, relationship, expectedRelationshipRevision)));
        }
        public Task<DataRelationshipDesignerCommit> CommitAsync(DataRelationshipReview review, CancellationToken cancellationToken = default)
        { Commits++; return Task.FromResult(new DataRelationshipDesignerCommit(false, "ApprovalRequired", null, false)); }
    }

    [AvaloniaFact]
    public void Relational_editor_projects_real_self_reference_without_runtime_activation_or_workbook_mutation()
    {
        var workbook = DataWorkbook.Create("Employees"); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "ID"); sheet.SetCell(0, 1, "Manager");
        sheet.SetCell(1, 0, "1"); sheet.SetCell(1, 1, "2"); sheet.SetCell(2, 0, "2"); sheet.SetCell(2, 1, "1");
        var table = new DataTableDefinition { SheetId = sheet.Id, Name = "Employees", Range = new() { EndRow = 2, EndColumn = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table); workbook.Normalize();
        var key = new DataKeyDefinition(Guid.NewGuid(), "Employee ID", DataKeyKind.Primary, [table.Fields[0].FieldID]);
        workbook = DataTableDesign.SetSchema(workbook, table.Id, 0, Guid.Empty, null,
            [new(table.Fields[0].FieldID, "ID", DataFieldType.Integer, false),
             new(table.Fields[1].FieldID, "Manager", DataFieldType.Integer, false)], [key]).Workbook!;
        var relation = new DataRelationshipDefinition(Guid.NewGuid(), "Manager", table.Id, [table.Fields[1].FieldID],
            table.Id, key.KeyID, DataRelationshipCardinality.OneToMany, false, 0);
        workbook = DataTableDesign.SetRelationship(workbook, 0, Guid.Empty, null, relation).Workbook!;
        var before = System.Text.Json.JsonSerializer.Serialize(workbook);
        var snapshot = DataRelationshipEditorAdapter.Capture(workbook);
        var editor = new NodeEditor { TopologyPolicy = NodeEditorTopologyPolicy.RelationalAuthoring, Document = snapshot.Document };
        Assert.Empty(editor.ValidateAgainst(snapshot.Schema));
        var node = Assert.Single(editor.Document.Nodes); Assert.Equal(table.Id, node.Id);
        var edge = Assert.Single(editor.Document.Edges); Assert.Equal(relation.RelationshipID, edge.Id);
        Assert.Equal(table.Fields[1].FieldID.ToString("D"), edge.FromPortId);
        Assert.Equal(key.KeyID.ToString("D"), edge.ToPortId);
        Assert.False(editor.TryActivate(editor.Document.Revision, out _));
        editor.SelectNode(node.Id); editor.MoveSelectionBy(35, 40);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(workbook));
        var reopened = System.Text.Json.JsonSerializer.Deserialize<DataWorkbook>(before)!;
        Assert.Equal(snapshot.Document, DataRelationshipEditorAdapter.Capture(reopened).Document,
            new RelationshipDocumentIdentityComparer());
    }

    private sealed class RelationshipDocumentIdentityComparer : IEqualityComparer<NodeEditorDocument>
    {
        public bool Equals(NodeEditorDocument? x, NodeEditorDocument? y) =>
            System.Text.Json.JsonSerializer.Serialize(x) == System.Text.Json.JsonSerializer.Serialize(y);
        public int GetHashCode(NodeEditorDocument obj) => obj.GraphId.GetHashCode();
    }

    [Fact]
    public void Visual_query_graph_has_stable_pipeline_ids_and_persists_stage_layout()
    {
        var query = DataQuery.Create("People query"); query.Visual.Source = "People"; query.Visual.Columns = "A, B"; query.Visual.Filter = "B > 10"; query.Visual.OrderBy = "B DESC"; query.Visual.Limit = 50; var first = DataVisualQueryGraphAdapter.ToEditor(query); Assert.Equal(7, first.Nodes.Count); Assert.Equal(6, first.Edges.Count);
        var filter = first.Nodes.Single(node => DataVisualQueryGraphAdapter.Stage(node) == "filter"); var moved = first with { Nodes = first.Nodes.Select(node => node.Id == filter.Id ? node with { X = node.X + 77, Y = node.Y + 31 } : node).ToArray() }; DataVisualQueryGraphAdapter.PersistLayout(query, moved); var second = DataVisualQueryGraphAdapter.ToEditor(query); var restored = second.Nodes.Single(node => node.Id == filter.Id);
        Assert.Equal(filter.Id, restored.Id); Assert.Equal(filter.X + 77, restored.X); Assert.Equal(filter.Y + 31, restored.Y); Assert.True(DataVisualQueryGraphAdapter.IsCanonicalStructure(query, second)); var editor = new NodeEditor { Document = second }; Assert.Empty(editor.ValidateDocument());
    }

    [AvaloniaFact]
    public void Data_scene_uses_shared_node_editor_and_retained_spreadsheet_for_visual_sql_results()
    {
        var workbook = DataWorkbook.Create("Visual SQL"); workbook.Sheets[0].Name = "People"; var query = workbook.Queries[0]; query.Visual.Source = "People"; query.Visual.Columns = "Name, Score"; query.Visual.Filter = "Score > 10"; var result = new DataQueryResult(["Name", "Score"], [["Ada", "42"]], false, "test");
        using var scene = new DataHavenScene(); var dirty = false; var controller = new DataVisualQueryGraphController(scene, () => query, () => result, () => dirty = true); scene.SetWorkbook(workbook, 0, 1, 0, 0, 0, 0, 0, 0, result); var editor = controller.Editor; Assert.Equal(7, editor.Document.Nodes.Count); Assert.Contains(editor.Document.Nodes, node => node.Title == "Filter" && node.Subtitle == "Score > 10");
        var resultHost = scene.Root.DescendantsAndSelf().OfType<Container>().Single(element => element.Name == "Data.Query.ResultGrid"); var grid = Assert.Single(resultHost.Children.OfType<DataSpreadsheetSurface>()); grid.SelectCell(0, 0, raiseChanged: false); Assert.Equal("Name", grid.Copy()); Assert.False(grid.TextInput("mutate"));
        var filter = editor.Document.Nodes.Single(node => DataVisualQueryGraphAdapter.Stage(node) == "filter"); editor.SelectNode(filter.Id); editor.MoveSelectionBy(40, 20); Assert.True(dirty); Assert.True(query.Metadata.ContainsKey("visualGraph.layout.filter.x")); scene.Root.ValidateUniqueNames();
    }
}
