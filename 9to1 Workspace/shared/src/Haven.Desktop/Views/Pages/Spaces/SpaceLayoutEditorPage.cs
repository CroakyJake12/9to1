using Avalonia.Automation;
using Avalonia.Controls;
using Haven.Application;
using Haven.Core;
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
    private readonly List<(HavenButton Button, Guid NodeId, string Key, string Value, EventHandler Handler)> _configurationOptionButtons = [];
    private readonly Container _configurationInspector;
    private readonly CancellationTokenSource _pageLifetime = new();
    private SpaceDefinition _space;
    private bool _disposed;
    private bool _saving;
    private bool _previewing;
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

        var root = new HavenPage { Name = "Spaces.Layout.Root", Layout = HavenLayout.Grid, Rows = "Auto Auto Auto Auto 1fr Auto" };
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

        var previewPanel = new Container { Name = "Spaces.Layout.SamplePreview", Layout = HavenLayout.Vertical };
        previewPanel.SetValue(HavenProperties.Row, 2);
        previewPanel.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        previewPanel.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        previewPanel.Accessibility.AccessibleName = "User-message graph sample preview";
        previewPanel.Add(new HavenText("Sample preview · synthetic input only · no model, Files, app or external action is called.")
            { Level = TextLevel.Caption });
        _previewSampleInput = new HavenInput { Name = "Spaces.Layout.SamplePreview.Input", Placeholder = "Synthetic sample user message" };
        _previewSampleInput.Accessibility.AccessibleName = "Synthetic sample user message; this is not sent or saved";
        _previewSampleInput.Text = "The sample contains 12, 34.5 and -6e2.";
        previewPanel.Add(_previewSampleInput);
        _previewButton = ActionButton("Spaces.Layout.SamplePreview.Run", "play", "Preview sample", ButtonVariant.Secondary);
        _previewButton.Accessibility.Description = "Run the current draft graph against the synthetic sample input only. No model or external service is called.";
        _previewButton.Invoked += OnPreviewSample;
        previewPanel.Add(_previewButton);
        _previewOutput = new HavenText("No sample preview has run.") { Name = "Spaces.Layout.SamplePreview.Output", Level = TextLevel.Paragraph };
        previewPanel.Add(_previewOutput);
        _previewTrace = new HavenText(string.Empty) { Name = "Spaces.Layout.SamplePreview.Trace", Level = TextLevel.Caption };
        previewPanel.Add(_previewTrace);
        root.Add(previewPanel);

        _configurationInspector = new Container { Name = "Spaces.Layout.NodeConfiguration", Layout = HavenLayout.Wrap };
        _configurationInspector.SetValue(HavenProperties.Row, 3);
        _configurationInspector.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _configurationInspector.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        _configurationInspector.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        _configurationInspector.Accessibility.AccessibleName = "Selected graph node configuration";
        root.Add(_configurationInspector);

        Editor = new NodeEditor { Name = "Spaces.Layout.NodeEditor" };
        Editor.Accessibility.AccessibleName = $"{space.Name} layout and additional logic editor";
        Editor.SetValue(HavenProperties.Row, 4);
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
        Status.SetValue(HavenProperties.Row, 5);
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
    private HavenInput _previewSampleInput { get; }
    private HavenButton _previewButton { get; }
    private HavenText _previewOutput { get; }
    private HavenText _previewTrace { get; }
    internal IReadOnlyList<(HavenButton Button, string TypeId)> UserMessagePalette =>
        _userMessagePalette.Select(item => (item.Button, item.TypeId)).ToArray();
    internal HavenInput PreviewSampleInput => _previewSampleInput;
    internal HavenText PreviewOutput => _previewOutput;
    internal HavenText PreviewTrace => _previewTrace;
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
        foreach (var (button, _, _, _, handler) in _configurationOptionButtons) button.Invoked -= handler;
        _configurationOptionButtons.Clear();
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
            var label = new HavenText(labelText) { Level = TextLevel.Caption };
            _configurationInspector.Add(label);
            if (property.Value.TryGetProperty("enum", out var enumValues) && enumValues.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var currentValue = node.Metadata.GetValueOrDefault(key, string.Empty);
                foreach (var enumValue in enumValues.EnumerateArray())
                {
                    var value = enumValue.ValueKind == System.Text.Json.JsonValueKind.String
                        ? enumValue.GetString() ?? string.Empty : enumValue.ToString();
                    var selected = string.Equals(value, currentValue, StringComparison.Ordinal);
                    var button = ActionButton($"Spaces.Layout.NodeConfiguration.{node.Id:D}.{key}.{value}",
                        selected ? "check" : "circle", value, selected ? ButtonVariant.Secondary : ButtonVariant.Tertiary);
                    button.SetState(HavenElementState.Selected, selected);
                    button.Accessibility.AccessibleName = $"{key}: {value}";
                    button.Accessibility.Description = selected ? "Current registered value." : "Select this registered value.";
                    EventHandler handler = (_, _) =>
                    {
                        Editor.UpdateNode(node.Id, current =>
                        {
                            var metadata = new Dictionary<string, string>(current.Metadata, StringComparer.Ordinal) { [key] = value };
                            return current with { Metadata = metadata };
                        });
                        foreach (var option in _configurationOptionButtons.Where(option => option.NodeId == node.Id && option.Key == key))
                        {
                            var isSelected = string.Equals(option.Value, value, StringComparison.Ordinal);
                            option.Button.Variant = isSelected ? ButtonVariant.Secondary : ButtonVariant.Tertiary;
                            option.Button.SetState(HavenElementState.Selected, isSelected);
                            option.Button.Accessibility.Description = isSelected ? "Current registered value." : "Select this registered value.";
                        }
                    };
                    button.Invoked += handler;
                    _configurationOptionButtons.Add((button, node.Id, key, value, handler));
                    _configurationInspector.Add(button);
                }
                continue;
            }
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
    internal IReadOnlyList<(HavenButton Button, string Key, string Value)> NodeConfigurationOptions =>
        _configurationOptionButtons.Select(option => (option.Button, option.Key, option.Value)).ToArray();

    private void OnDeleteSelection(object? sender, EventArgs e) => Editor.DeleteSelection();
    private void OnResetView(object? sender, EventArgs e) => Editor.ResetViewport();

    private async void OnPreviewSample(object? sender, EventArgs e) => await PreviewSampleAsync().ConfigureAwait(true);

    internal async Task PreviewSampleAsync()
    {
        if (_disposed || _previewing) return;
        var sample = _previewSampleInput.Text;
        if (string.IsNullOrEmpty(sample))
        {
            _previewOutput.Content = "Enter a synthetic sample message first.";
            _previewTrace.Content = string.Empty;
            return;
        }

        var document = Editor.Document;
        var draftRevision = document.Revision;
        var graphId = document.GraphId;
        var spaceId = _space.Id;
        var spaceRevision = _space.Revision;
        _previewing = true;
        _previewButton.SetValue(HavenProperties.Enabled, false);
        _previewButton.SetState(HavenElementState.Disabled, true);
        _previewOutput.Content = "Running the current draft against the synthetic sample…";
        _previewTrace.Content = string.Empty;
        try
        {
            var layout = SpaceLayoutEditorAdapter.FromEditor(document, _space.LayoutDocument);
            var graph = SpaceUserMessageGraph.Compile(spaceId, spaceRevision, layout);
            var message = new ChatMessage(Guid.NewGuid(), Guid.NewGuid(), MessageRole.User, sample,
                AgentName: null, ModelName: null, MetadataJson: null, CreatedAt: DateTimeOffset.UtcNow);
            var result = await graph.ExecuteAsync(message, _pageLifetime.Token).ConfigureAwait(true);
            if (_disposed) return;
            if (Editor.Document.Revision != draftRevision || Editor.Document.GraphId != graphId ||
                _space.Id != spaceId || _space.Revision != spaceRevision || !string.Equals(_previewSampleInput.Text, sample, StringComparison.Ordinal))
            {
                _previewOutput.Content = "Preview result discarded because the draft or sample changed while it ran. Run the sample again.";
                _previewTrace.Content = string.Empty;
                return;
            }

            _previewOutput.Content = $"Synthetic sample derived output · {result.OmittedTextElements} Unicode text element(s) omitted:\n" +
                LimitPreviewDisplay(result.DerivedText);
            _previewTrace.Content = LimitPreviewDisplay("Sample-only node trace:\n" + string.Join("\n", result.Trace.Select(item =>
            {
                var value = result.TypedNodeOutputs.GetValueOrDefault(item.NodeId);
                var typed = value?.JsonValue is { } json ? " · value=" + json.GetRawText() : string.Empty;
                var flags = item.MoreResultsOmitted ? " · more results omitted" : string.Empty;
                if (item.HasNumericUncertainty) flags += " · numeric uncertainty";
                return $"{item.TypeId}: {item.State}; {item.InputTextElements} → {item.OutputTextElements} text elements; {item.OutputDataType}; SHA-256 {item.OutputSha256 ?? "unavailable"}{flags}{typed}";
            })));
        }
        catch (OperationCanceledException) when (_pageLifetime.IsCancellationRequested)
        {
            if (!_disposed) _previewOutput.Content = "Sample preview cancelled because the editor closed.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or OperationCanceledException)
        {
            if (!_disposed)
            {
                _previewOutput.Content = "Sample preview unavailable: " + exception.Message;
                _previewTrace.Content = string.Empty;
            }
        }
        finally
        {
            _previewing = false;
            if (!_disposed)
            {
                _previewButton.SetValue(HavenProperties.Enabled, true);
                _previewButton.SetState(HavenElementState.Disabled, false);
            }
        }
    }

    private static string LimitPreviewDisplay(string value) => value.Length <= 8192
        ? value : value[..8192] + "… [display truncated; graph output was not changed]";

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
        _pageLifetime.Cancel();
        foreach (var (input, handler) in _configurationInputs) input.TextChanged -= handler;
        _configurationInputs.Clear();
        foreach (var (button, _, _, _, handler) in _configurationOptionButtons) button.Invoked -= handler;
        _configurationOptionButtons.Clear();
        foreach (var (button, _, handler) in _userMessagePalette) button.Invoked -= handler;
        _previewButton.Invoked -= OnPreviewSample;
        DeleteSelection.Invoked -= OnDeleteSelection;
        ResetView.Invoked -= OnResetView;
        Save.Invoked -= OnSave;
        Editor.DocumentChanged -= OnDocumentChanged;
        Editor.SelectionChanged -= OnSelectionChanged;
        _pageLifetime.Dispose();
    }
}
