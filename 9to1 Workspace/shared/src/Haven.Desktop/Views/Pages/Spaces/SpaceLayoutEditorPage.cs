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
        document ??= SpaceLayoutDocument.Empty;
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
            GraphId = document.GraphId == Guid.Empty ? Guid.NewGuid() : document.GraphId,
            SchemaVersion = document.SchemaVersion,
            Revision = document.Revision,
            State = document.State == SpaceLayoutRevisionState.Active ? NodeEditorRevisionState.Active : NodeEditorRevisionState.Draft,
            ConnectionProfileId = document.ConnectionProfileId,
            Groups = document.Groups.Select(group => new NodeEditorGroup(group.Id, group.Title, group.NodeIds.ToArray(), group.Color)).ToArray(),
            Comments = document.Comments.Select(comment => new NodeEditorComment(comment.Id, comment.Text, comment.X, comment.Y, comment.Width, comment.Height)).ToArray()
        };
    }

    public static SpaceLayoutDocument FromEditor(NodeEditorDocument document, SpaceLayoutDocument? previous = null)
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
            State = document.State == NodeEditorRevisionState.Active ? SpaceLayoutRevisionState.Active : SpaceLayoutRevisionState.Draft,
            ConnectionProfileId = document.ConnectionProfileId,
            Groups = document.Groups.Select(group => new SpaceLayoutGroup(group.Id, group.Title, group.NodeIds.ToArray(), group.Color)).ToArray(),
            Comments = document.Comments.Select(comment => new SpaceLayoutComment(comment.Id, comment.Text, comment.X, comment.Y, comment.Width, comment.Height)).ToArray()
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
    private readonly Action<SpaceLayoutDocument>? _onDraftChanged;
    private SpaceDefinition _space;
    private bool _disposed;
    private bool _saving;
    private bool _suppressDraftChange;

    public SpaceLayoutEditorPage(SpaceRegistry registry, SpaceDefinition space)
        : this(registry, space, null) { }

    public SpaceLayoutEditorPage(SpaceDefinition space, Action<SpaceLayoutDocument> onDraftChanged)
        : this(null, space, onDraftChanged ?? throw new ArgumentNullException(nameof(onDraftChanged))) { }

    private SpaceLayoutEditorPage(SpaceRegistry? registry, SpaceDefinition space, Action<SpaceLayoutDocument>? onDraftChanged)
    {
        if (registry is null && onDraftChanged is null) throw new ArgumentNullException(nameof(registry));
        _registry = registry!;
        _onDraftChanged = onDraftChanged;
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
        AddTrigger = ActionButton("Spaces.Layout.AddTrigger", "zap", "Trigger");
        AddCondition = ActionButton("Spaces.Layout.AddCondition", "git-branch", "Add condition");
        AddEvent = ActionButton("Spaces.Layout.AddEvent", "bell", "Event");
        AddApp = ActionButton("Spaces.Layout.AddApp", "grid", "App");
        AddFiles = ActionButton("Spaces.Layout.AddFiles", "folder", "Files");
        DeleteSelection = ActionButton("Spaces.Layout.Delete", "trash", "Delete selected");
        ResetView = ActionButton("Spaces.Layout.ResetView", "maximize", "Reset view");
        Save = ActionButton("Spaces.Layout.Save", "save", onDraftChanged is null ? "Save layout" : "Validate graph", ButtonVariant.Primary);
        header.Add(title);
        header.Add(AddLogic);
        header.Add(AddTrigger);
        header.Add(AddCondition);
        header.Add(AddEvent);
        header.Add(AddApp);
        header.Add(AddFiles);
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

        Status = new HavenText(Editor.Document.Nodes.Count == 0
            ? "Add logic nodes, connect their ports, then save."
            : $"Loaded {Editor.Document.Nodes.Count} nodes and {Editor.Document.Edges.Count} connections.")
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
        AddTrigger.Invoked += OnAddTrigger;
        AddCondition.Invoked += OnAddCondition;
        AddEvent.Invoked += OnAddEvent;
        AddApp.Invoked += OnAddApp;
        AddFiles.Invoked += OnAddFiles;
        DeleteSelection.Invoked += OnDeleteSelection;
        ResetView.Invoked += OnResetView;
        Save.Invoked += OnSave;
        Editor.DocumentChanged += OnDocumentChanged;
    }

    internal HavenSceneControl Scene { get; }
    internal NodeEditor Editor { get; }
    internal HavenText Status { get; }
    internal HavenButton AddLogic { get; }
    internal HavenButton AddTrigger { get; }
    internal HavenButton AddCondition { get; }
    internal HavenButton AddEvent { get; }
    internal HavenButton AddApp { get; }
    internal HavenButton AddFiles { get; }
    internal HavenButton DeleteSelection { get; }
    internal HavenButton ResetView { get; }
    internal HavenButton Save { get; }

    public void ReloadDraft(SpaceDefinition space)
    {
        ArgumentNullException.ThrowIfNull(space);
        if (_disposed) throw new ObjectDisposedException(nameof(SpaceLayoutEditorPage));
        _space = space;
        _suppressDraftChange = true;
        try
        {
            Editor.Document = SpaceLayoutEditorAdapter.ToEditor(space.LayoutDocument);
            Editor.ClearSelection();
            Editor.ResetViewport();
            Status.Content = $"Loaded {Editor.Document.Nodes.Count} nodes and {Editor.Document.Edges.Count} connections.";
        }
        finally { _suppressDraftChange = false; }
    }

    private void OnAddLogic(object? sender, EventArgs e) => AddNode(new NodeEditorTemplate(
        "Logic",
        "Logic placeholder",
        "Structural only; an installed Logic schema is required before activation.",
        LogicPorts));

    private void OnAddTrigger(object? sender, EventArgs e) => AddNode(new NodeEditorTemplate(
        "Trigger", "Trigger placeholder", "Structural only; a registered trigger schema is required before activation.",
        [new("out", "Out", NodeEditorPortDirection.Output, "flow", true)]));

    private void OnAddCondition(object? sender, EventArgs e) => AddNode(new NodeEditorTemplate(
        "Condition",
        "Condition placeholder",
        "Structural only; a registered condition schema is required before activation.",
        [
            new("in", "In", NodeEditorPortDirection.Input, "flow", false),
            new("true", "True", NodeEditorPortDirection.Output, "flow", true),
            new("false", "False", NodeEditorPortDirection.Output, "flow", true)
        ]));

    private void OnAddEvent(object? sender, EventArgs e) => AddStructuralCategory("Event");
    private void OnAddApp(object? sender, EventArgs e) => AddStructuralCategory("App");
    private void OnAddFiles(object? sender, EventArgs e) => AddStructuralCategory("Files");

    private void AddStructuralCategory(string category) => AddNode(new NodeEditorTemplate(category,
        category + " placeholder", $"Structural only; a registered {category} schema is required before activation.", LogicPorts));

    private void AddNode(NodeEditorTemplate template)
    {
        var offset = Editor.Document.Nodes.Count * 28d;
        Editor.AddNode(template, 60d + offset, 70d + offset);
    }

    private void OnDeleteSelection(object? sender, EventArgs e) => Editor.DeleteSelection();
    private void OnResetView(object? sender, EventArgs e) => Editor.ResetViewport();

    private void OnDocumentChanged(NodeEditorDocument document)
    {
        Status.Content = $"Unsaved changes · {document.Nodes.Count} nodes · {document.Edges.Count} connections";
        if (!_suppressDraftChange && _onDraftChanged is not null)
        {
            try { _onDraftChanged(SpaceLayoutEditorAdapter.FromEditor(document, _space.LayoutDocument)); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            { Status.Content = $"Draft graph update rejected: {exception.Message}"; }
        }
    }

    private async void OnSave(object? sender, EventArgs e)
    {
        if (_saving || _disposed) return;
        var diagnostics = Editor.ValidateDocument();
        if (diagnostics.Count > 0)
        {
            Status.Content = $"Cannot save: {diagnostics[0].Message}";
            return;
        }

        _saving = true;
        Save.SetValue(HavenProperties.Enabled, false);
        Save.SetState(HavenElementState.Disabled, true);
        Status.Content = _onDraftChanged is null ? "Saving layout…" : "Validating graph…";
        try
        {
            if (_onDraftChanged is not null)
            {
                _onDraftChanged(SpaceLayoutEditorAdapter.FromEditor(Editor.Document, _space.LayoutDocument));
                Status.Content = $"Graph is structurally valid and remains in the unsaved Space draft: {Editor.Document.Nodes.Count} nodes, {Editor.Document.Edges.Count} connections.";
            }
            else
            {
                _space = await _registry.SetLayoutAsync(_space.Id, SpaceLayoutEditorAdapter.FromEditor(Editor.Document, _space.LayoutDocument), CancellationToken.None);
                Status.Content = $"Saved {_space.LayoutDocument?.Nodes.Count ?? 0} nodes and {_space.LayoutDocument?.Edges.Count ?? 0} connections.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status.Content = $"Layout could not be saved: {exception.Message}";
        }
        finally
        {
            _saving = false;
            Save.SetValue(HavenProperties.Enabled, true);
            Save.SetState(HavenElementState.Disabled, false);
        }
    }

    private static HavenButton ActionButton(string name, string icon, string content, ButtonVariant variant = ButtonVariant.Tertiary)
    {
        var button = new HavenButton { Name = name, IconKey = icon, Content = content, Variant = variant };
        button.Accessibility.AccessibleName = content;
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(38));
        return button;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AddLogic.Invoked -= OnAddLogic;
        AddTrigger.Invoked -= OnAddTrigger;
        AddCondition.Invoked -= OnAddCondition;
        AddEvent.Invoked -= OnAddEvent;
        AddApp.Invoked -= OnAddApp;
        AddFiles.Invoked -= OnAddFiles;
        DeleteSelection.Invoked -= OnDeleteSelection;
        ResetView.Invoked -= OnResetView;
        Save.Invoked -= OnSave;
        Editor.DocumentChanged -= OnDocumentChanged;
    }
}
