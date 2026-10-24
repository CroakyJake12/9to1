using Avalonia.Automation;
using Avalonia.Controls;
using Haven.Application;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenPage = Haven.UI.Components.Page;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Spaces;

internal static class SpaceLayoutEditorAdapter
{
    public static NodeEditorDocument ToEditor(SpaceLayoutDocument? document)
    {
        document ??= new SpaceLayoutDocument([], []);
        if (document.GraphId == Guid.Empty) document = document with { GraphId = Guid.NewGuid() };
        return new NodeEditorDocument(
            document.Nodes.Select(node => new NodeEditorNode(node.Id, node.Category, node.Title)
            {
                TypeId = node.TypeId,
                SchemaVersion = node.SchemaVersion,
                Subtitle = node.Subtitle,
                X = node.X,
                Y = node.Y,
                Width = node.Width,
                Height = node.Height,
                Ports = node.Ports.Select(port => new NodeEditorPort(
                    port.Id,
                    port.Label,
                    port.Direction == SpaceLayoutPortDirection.Input ? NodeEditorPortDirection.Input : NodeEditorPortDirection.Output,
                    port.DataType,
                    port.AllowsMultipleConnections)).ToArray(),
                Metadata = node.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            }).ToArray(),
            document.Edges.Select(edge => new NodeEditorEdge(edge.Id, edge.FromNodeId, edge.FromPortId, edge.ToNodeId, edge.ToPortId)
            {
                Label = edge.Label,
                Branch = edge.Branch,
                Metadata = edge.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            }).ToArray())
        {
            GraphId = document.GraphId,
            SchemaVersion = document.SchemaVersion,
            Revision = document.Revision,
            State = (NodeEditorRevisionState)document.State,
            ConnectionProfileId = document.ConnectionProfileId,
            Groups = document.Groups.Select(group => new NodeEditorGroup(group.Id, group.Title, group.NodeIds.ToArray(), group.Color)).ToArray(),
            Comments = document.Comments.Select(comment => new NodeEditorComment(comment.Id, comment.Text,
                comment.X, comment.Y, comment.Width, comment.Height)).ToArray()
        };
    }

    public static SpaceLayoutDocument FromEditor(NodeEditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new SpaceLayoutDocument(
            document.Nodes.Select(node => new SpaceLayoutNode(node.Id, node.Category, node.Title)
            {
                TypeId = node.TypeId,
                SchemaVersion = node.SchemaVersion,
                Subtitle = node.Subtitle,
                X = node.X,
                Y = node.Y,
                Width = node.Width,
                Height = node.Height,
                Ports = node.Ports.Select(port => new SpaceLayoutPort(
                    port.Id,
                    port.Label,
                    port.Direction == NodeEditorPortDirection.Input ? SpaceLayoutPortDirection.Input : SpaceLayoutPortDirection.Output,
                    port.DataType,
                    port.AllowsMultipleConnections)).ToArray(),
                Metadata = node.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            }).ToArray(),
            document.Edges.Select(edge => new SpaceLayoutEdge(edge.Id, edge.FromNodeId, edge.FromPortId, edge.ToNodeId, edge.ToPortId)
            {
                Label = edge.Label,
                Branch = edge.Branch,
                Metadata = edge.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            }).ToArray())
        {
            GraphId = document.GraphId,
            SchemaVersion = document.SchemaVersion,
            Revision = document.Revision,
            State = (SpaceLayoutRevisionState)document.State,
            ConnectionProfileId = document.ConnectionProfileId,
            Groups = document.Groups.Select(group => new SpaceLayoutGroup(group.Id, group.Title, group.NodeIds.ToArray(), group.Color)).ToArray(),
            Comments = document.Comments.Select(comment => new SpaceLayoutComment(comment.Id, comment.Text,
                comment.X, comment.Y, comment.Width, comment.Height)).ToArray()
        };
    }
}

internal sealed class SpaceLayoutEditorPage : UserControl, IDisposable
{
    private static readonly IReadOnlyList<NodeEditorPort> LogicPorts =
    [
        new("in", "In", NodeEditorPortDirection.Input, "flow", false),
        new("out", "Out", NodeEditorPortDirection.Output, "flow", true)
    ];

    private readonly SpaceRegistry _registry;
    private SpaceDefinition _space;
    private bool _disposed;
    private bool _saving;
    private long _editEpoch;

    public SpaceLayoutEditorPage(SpaceRegistry registry, SpaceDefinition space)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _space = space ?? throw new ArgumentNullException(nameof(space));

        var root = new HavenPage { Name = "Spaces.Layout.Root", Layout = HavenLayout.Grid, Rows = "Auto 1fr Auto" };
        root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(18)));
        root.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        root.SetValue(HavenProperties.Background, "Surface");

        var header = new Container { Name = "Spaces.Layout.Header", Layout = HavenLayout.Horizontal };
        header.SetValue(HavenProperties.Row, 0);
        header.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        header.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        var title = new HavenText($"{space.Name} · layout and additional logic") { Level = TextLevel.H1 };
        title.SetValue(HavenProperties.Width, HavenLength.Fr(1));
        AddLogic = ActionButton("Spaces.Layout.AddLogic", "plus", "Add logic");
        AddCondition = ActionButton("Spaces.Layout.AddCondition", "git-branch", "Add condition");
        DeleteSelection = ActionButton("Spaces.Layout.Delete", "trash", "Delete selected");
        ResetView = ActionButton("Spaces.Layout.ResetView", "maximize", "Reset view");
        Save = ActionButton("Spaces.Layout.Save", "save", "Save layout", ButtonVariant.Primary);
        header.Add(title);
        header.Add(AddLogic);
        header.Add(AddCondition);
        header.Add(DeleteSelection);
        header.Add(ResetView);
        header.Add(Save);
        root.Add(header);

        Editor = new NodeEditor { Name = "Spaces.Layout.NodeEditor" };
        Editor.Accessibility.AccessibleName = $"{space.Name} layout and additional logic editor";
        Editor.SetValue(HavenProperties.Row, 1);
        Editor.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Editor.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Editor.SetValue(HavenProperties.MinHeight, HavenLength.Px(420));
        Editor.SetValue(HavenProperties.Background, "SurfaceRaised");
        Editor.SetValue(HavenProperties.BorderColor, "Border");
        Editor.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        Editor.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        Editor.Document = SpaceLayoutEditorAdapter.ToEditor(space.LayoutDocument);
        root.Add(Editor);

        Status = new HavenText(Editor.Document.Nodes.Count == 0 && Editor.Document.Edges.Count == 0 &&
                               Editor.Document.Groups.Count == 0 && Editor.Document.Comments.Count == 0
            ? "Add logic nodes and connect ports; saving does not activate or execute the graph."
            : LayoutSummary("Loaded", Editor.Document))
        { Name = "Spaces.Layout.Status", Level = TextLevel.Caption };
        Status.SetValue(HavenProperties.Row, 2);
        Status.SetValue(HavenProperties.Foreground, "TextSecondary");
        root.Add(Status);

        Scene = new HavenSceneControl { Root = root };
        AutomationProperties.SetAutomationId(this, "HavenSpaceLayoutEditorPage");
        AutomationProperties.SetName(this, $"{space.Name} layout editor");
        AutomationProperties.SetAutomationId(Scene, "HavenSpaceLayoutEditorScene");
        Content = Scene;

        AddLogic.Invoked += OnAddLogic;
        AddCondition.Invoked += OnAddCondition;
        DeleteSelection.Invoked += OnDeleteSelection;
        ResetView.Invoked += OnResetView;
        Save.Invoked += OnSave;
        Editor.DocumentChanged += OnDocumentChanged;
    }

    internal HavenSceneControl Scene { get; }
    internal NodeEditor Editor { get; }
    internal HavenText Status { get; }
    internal HavenButton AddLogic { get; }
    internal HavenButton AddCondition { get; }
    internal HavenButton DeleteSelection { get; }
    internal HavenButton ResetView { get; }
    internal HavenButton Save { get; }

    private void OnAddLogic(object? sender, EventArgs e) => AddNode(new NodeEditorTemplate(
        "AdditionalLogic",
        "Additional logic",
        "Describe an extra rule or processing step for this Space.",
        LogicPorts));

    private void OnAddCondition(object? sender, EventArgs e) => AddNode(new NodeEditorTemplate(
        "Condition",
        "Condition",
        "Route the Space flow when a condition is true or false.",
        [
            new("in", "In", NodeEditorPortDirection.Input, "flow", false),
            new("true", "True", NodeEditorPortDirection.Output, "flow", true),
            new("false", "False", NodeEditorPortDirection.Output, "flow", true)
        ]));

    private void AddNode(NodeEditorTemplate template)
    {
        if (_disposed) return;
        if (Editor.Document.Nodes.Count >= SpaceLayoutDocument.MaximumNodes)
        {
            Status.Content = $"This layout has reached its {SpaceLayoutDocument.MaximumNodes}-node limit. Remove a node before adding another.";
            return;
        }
        var offset = Editor.Document.Nodes.Count * 28d;
        Editor.AddNode(template, 60d + offset, 70d + offset);
    }

    private void OnDeleteSelection(object? sender, EventArgs e)
    {
        if (!_disposed) Editor.DeleteSelection();
    }

    private void OnResetView(object? sender, EventArgs e)
    {
        if (!_disposed) Editor.ResetViewport();
    }

    private void OnDocumentChanged(NodeEditorDocument document)
    {
        _editEpoch++;
        Status.Content = LayoutSummary("Unsaved changes", document);
    }

    private async void OnSave(object? sender, EventArgs e)
    {
        if (_saving || _disposed) return;
        if (!CanSaveLayout(_space.LayoutDocument?.State))
        {
            Status.Content = "The active layout is preserved. Draft/Active publishing is not available here, so these edits remain unsaved.";
            return;
        }
        var diagnostics = Editor.ValidateDocument();
        if (diagnostics.Count > 0)
        {
            Status.Content = $"Cannot save: {diagnostics[0].Message}";
            return;
        }

        var submittedEditEpoch = _editEpoch;
        _saving = true;
        Save.SetValue(HavenProperties.Enabled, false);
        Save.SetState(HavenElementState.Disabled, true);
        Status.Content = "Saving layout…";
        try
        {
            _space = await _registry.SetLayoutAsync(_space.Id, SpaceLayoutEditorAdapter.FromEditor(Editor.Document),
                CancellationToken.None, _space.Revision);
            if (_disposed) return;
            Status.Content = _editEpoch == submittedEditEpoch
                ? LayoutSummary("Saved", Editor.Document)
                : "Earlier layout saved; newer editor changes remain unsaved.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {
            if (_disposed) return;
            Status.Content = exception is SpaceRevisionConflictException
                ? $"Layout could not be saved: {exception.Message} Your local edits remain unsaved; reopen the layout to review the latest saved version."
                : $"Layout could not be saved: {exception.Message}";
        }
        finally
        {
            _saving = false;
            if (!_disposed)
            {
                Save.SetValue(HavenProperties.Enabled, true);
                Save.SetState(HavenElementState.Disabled, false);
            }
        }
    }

    private static HavenButton ActionButton(string name, string icon, string content, ButtonVariant variant = ButtonVariant.Tertiary)
    {
        var button = new HavenButton { Name = name, IconKey = icon, Content = content, Variant = variant };
        button.Accessibility.AccessibleName = content;
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(38));
        return button;
    }

    internal static string LayoutSummary(string prefix, NodeEditorDocument document)
    {
        var summary = $"{prefix} {document.Nodes.Count} nodes, {document.Edges.Count} connections, {document.Groups.Count} groups and {document.Comments.Count} comments.";
        var untypedWarning = document.Nodes.Any(node => string.IsNullOrWhiteSpace(node.TypeId))
            ? " Untyped nodes are not executable."
            : string.Empty;
        return summary + untypedWarning + " Layout only; this editor does not activate or execute graphs.";
    }

    internal static bool CanSaveLayout(SpaceLayoutRevisionState? stored) =>
        stored != SpaceLayoutRevisionState.Active;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AddLogic.Invoked -= OnAddLogic;
        AddCondition.Invoked -= OnAddCondition;
        DeleteSelection.Invoked -= OnDeleteSelection;
        ResetView.Invoked -= OnResetView;
        Save.Invoked -= OnSave;
        Editor.DocumentChanged -= OnDocumentChanged;
    }
}
