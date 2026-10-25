using Avalonia.Automation;
using Avalonia.Controls;
using Haven.Application;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenInput = Haven.UI.Components.Input;
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
    private readonly SpaceRegistry _registry;
    private readonly Action<SpaceLayoutDocument>? _onDraftChanged;
    private readonly List<(HavenButton Button, string TypeId, EventHandler Handler)> _userMessagePalette = [];
    private readonly List<(HavenInput Input, EventHandler Handler)> _configurationInputs = [];
    private readonly Container _configurationInspector;
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

        var root = new HavenPage { Name = "Spaces.Layout.Root", Layout = HavenLayout.Grid, Rows = "Auto Auto Auto 1fr Auto" };
        root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(18)));
        root.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        root.SetValue(HavenProperties.Background, "Surface");

        var header = new Container { Name = "Spaces.Layout.Header", Layout = HavenLayout.Horizontal };
        header.SetValue(HavenProperties.Row, 0);
        header.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        header.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        var title = new HavenText($"{space.Name} · user-message logic") { Level = TextLevel.H1 };
        title.SetValue(HavenProperties.Width, HavenLength.Fr(1));
        DeleteSelection = ActionButton("Spaces.Layout.Delete", "trash", "Delete selected");
        ResetView = ActionButton("Spaces.Layout.ResetView", "maximize", "Reset view");
        Save = ActionButton("Spaces.Layout.Save", "save", onDraftChanged is null ? "Save layout" : "Validate graph", ButtonVariant.Primary);
        header.Add(title);
        header.Add(DeleteSelection);
        header.Add(ResetView);
        header.Add(Save);
        root.Add(header);

        var palette = new Container { Name = "Spaces.Layout.UserMessagePalette", Layout = HavenLayout.Wrap };
        palette.SetValue(HavenProperties.Row, 1);
        palette.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        palette.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        palette.Accessibility.AccessibleName = "Registered user-message nodes";
        var profile = SpaceUserMessageGraph.Profile();
        foreach (var schema in SpaceUserMessageGraph.Types().Where(type => profile.AllowedNodeTypes.Contains(type.TypeId)))
        {
            var prototype = SpaceUserMessageGraph.CreateNode(schema.TypeId);
            var button = ActionButton("Spaces.Layout.Add." + schema.TypeId, "plus", "Add " + prototype.Title);
            var typeId = schema.TypeId;
            EventHandler handler = (_, _) => AddRegisteredUserMessageNode(typeId);
            button.Invoked += handler;
            _userMessagePalette.Add((button, typeId, handler));
            palette.Add(button);
        }
        root.Add(palette);

        _configurationInspector = new Container { Name = "Spaces.Layout.NodeConfiguration", Layout = HavenLayout.Wrap };
        _configurationInspector.SetValue(HavenProperties.Row, 2);
        _configurationInspector.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _configurationInspector.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        _configurationInspector.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        _configurationInspector.Accessibility.AccessibleName = "Selected graph node configuration";
        root.Add(_configurationInspector);

        Editor = new NodeEditor { Name = "Spaces.Layout.NodeEditor" };
        Editor.Accessibility.AccessibleName = $"{space.Name} layout and additional logic editor";
        Editor.SetValue(HavenProperties.Row, 3);
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
            ? "Add registered nodes, connect their typed ports, then validate the graph."
            : $"Loaded {Editor.Document.Nodes.Count} nodes and {Editor.Document.Edges.Count} connections.")
        { Name = "Spaces.Layout.Status", Level = TextLevel.Caption };
        Status.SetValue(HavenProperties.Row, 4);
        Status.SetValue(HavenProperties.Foreground, "TextSecondary");
        root.Add(Status);

        Scene = new HavenSceneControl { Root = root };
        AutomationProperties.SetAutomationId(this, "HavenSpaceLayoutEditorPage");
        AutomationProperties.SetName(this, $"{space.Name} layout editor");
        AutomationProperties.SetAutomationId(Scene, "HavenSpaceLayoutEditorScene");
        Content = Scene;

        DeleteSelection.Invoked += OnDeleteSelection;
        ResetView.Invoked += OnResetView;
        Save.Invoked += OnSave;
        Editor.DocumentChanged += OnDocumentChanged;
        Editor.SelectionChanged += OnSelectionChanged;
        OnSelectionChanged(Editor.SelectedNodeIds);
    }

    internal HavenSceneControl Scene { get; }
    internal NodeEditor Editor { get; }
    internal HavenText Status { get; }
    internal IReadOnlyList<(HavenButton Button, string TypeId)> UserMessagePalette =>
        _userMessagePalette.Select(item => (item.Button, item.TypeId)).ToArray();
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

    private void AddRegisteredUserMessageNode(string typeId)
    {
        var canonical = SpaceUserMessageGraph.CreateNode(typeId);
        var node = SpaceLayoutEditorAdapter.ToEditor(new SpaceLayoutDocument([canonical], [])).Nodes.Single();
        var offset = Editor.Document.Nodes.Count * 28d;
        Editor.AddNode(node, 60d + offset, 70d + offset);
    }

    private void OnSelectionChanged(IReadOnlyCollection<Guid> selectedIds)
    {
        foreach (var (input, handler) in _configurationInputs) input.TextChanged -= handler;
        _configurationInputs.Clear();
        foreach (var child in _configurationInspector.Children.ToArray()) _configurationInspector.Remove(child);
        var node = selectedIds.Count == 1
            ? Editor.Document.Nodes.SingleOrDefault(candidate => candidate.Id == selectedIds.Single())
            : null;
        var profile = SpaceUserMessageGraph.Profile();
        var schema = node is null || !profile.AllowedNodeTypes.Contains(node.TypeId)
            ? null
            : SpaceUserMessageGraph.Types().SingleOrDefault(type => type.TypeId == node.TypeId && type.Version == node.SchemaVersion);
        if (node is null || schema is null || !schema.ConfigurationSchema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != System.Text.Json.JsonValueKind.Object || properties.EnumerateObject().Count() == 0)
        {
            _configurationInspector.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
            return;
        }

        _configurationInspector.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        var heading = new HavenText($"{node.Title} settings") { Level = TextLevel.H3 };
        _configurationInspector.Add(heading);
        var required = schema.ConfigurationSchema.TryGetProperty("required", out var requiredArray) &&
            requiredArray.ValueKind == System.Text.Json.JsonValueKind.Array
            ? requiredArray.EnumerateArray().Where(value => value.ValueKind == System.Text.Json.JsonValueKind.String)
                .Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties.EnumerateObject())
        {
            var key = property.Name;
            var type = property.Value.TryGetProperty("type", out var propertyType) && propertyType.ValueKind == System.Text.Json.JsonValueKind.String
                ? propertyType.GetString() : "value";
            var labelText = key + (required.Contains(key) ? " (required)" : "");
            if (property.Value.TryGetProperty("enum", out var enumValues) && enumValues.ValueKind == System.Text.Json.JsonValueKind.Array)
                labelText += " · " + string.Join(" / ", enumValues.EnumerateArray().Select(value => value.ToString()));
            var label = new HavenText(labelText) { Level = TextLevel.Caption };
            _configurationInspector.Add(label);
            var input = new HavenInput { Name = $"Spaces.Layout.NodeConfiguration.{node.Id:D}.{key}", Placeholder = type };
            input.Accessibility.AccessibleName = labelText;
            input.Text = node.Metadata.GetValueOrDefault(key, string.Empty);
            EventHandler handler = (_, _) => Editor.UpdateNode(node.Id, current =>
            {
                var metadata = new Dictionary<string, string>(current.Metadata, StringComparer.Ordinal);
                if (input.Text.Length == 0 && !required.Contains(key)) metadata.Remove(key);
                else metadata[key] = input.Text;
                return current with { Metadata = metadata };
            });
            input.TextChanged += handler;
            _configurationInputs.Add((input, handler));
            _configurationInspector.Add(input);
        }
    }

    internal IReadOnlyDictionary<string, HavenInput> NodeConfigurationInputs => _configurationInputs
        .ToDictionary(pair => pair.Input.Name ?? string.Empty, pair => pair.Input, StringComparer.Ordinal);

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
                var layout = SpaceLayoutEditorAdapter.FromEditor(Editor.Document, _space.LayoutDocument);
                _ = SpaceUserMessageGraph.Compile(_space.Id, _space.Revision, layout);
                _onDraftChanged(layout);
                Status.Content = $"Registered user-message graph is valid and remains in the unsaved Space draft: {Editor.Document.Nodes.Count} nodes, {Editor.Document.Edges.Count} connections.";
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
        foreach (var (input, handler) in _configurationInputs) input.TextChanged -= handler;
        _configurationInputs.Clear();
        foreach (var (button, _, handler) in _userMessagePalette) button.Invoked -= handler;
        DeleteSelection.Invoked -= OnDeleteSelection;
        ResetView.Invoked -= OnResetView;
        Save.Invoked -= OnSave;
        Editor.DocumentChanged -= OnDocumentChanged;
        Editor.SelectionChanged -= OnSelectionChanged;
    }
}
