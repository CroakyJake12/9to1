using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;

namespace HavenOS.Apps.Data.RecordsUI;

/// <summary>The Data-owned record renderer, shared by Data and reference blocks. It owns no
/// copied record store and never disposes the caller's session. Failed edits retain their inputs.</summary>
public sealed class DataRecordViewControl : UserControl, ICuiActionDispatcher, IAsyncDisposable
{
    private readonly DataRecordSession _session;
    private readonly CuiViewModel _model = new();
    private readonly CuiControlLoader _loader;
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private Guid? _filterField;
    private readonly List<Task> _operations = [];
    private readonly Dictionary<(Guid Record, Guid Field), string> _drafts = new();
    private int _draftCount;
    public bool HasUnsavedDrafts => Volatile.Read(ref _draftCount) != 0;
    public void DiscardDrafts() { Dispatcher.UIThread.VerifyAccess(); _drafts.Clear(); UpdateDraftState(); Render(); }
    private Guid _selectedView;
    private bool _closed, _busy, _readOnly;
    public bool IsReadOnly
    {
        get => _readOnly;
        set { Dispatcher.UIThread.VerifyAccess(); if (_readOnly == value) return; _readOnly = value; Render(); }
    }
    private void UpdateDraftState()
    {
        Volatile.Write(ref _draftCount, _drafts.Count);
        _model.Set("HasDrafts", HasUnsavedDrafts);
    }
    public Guid SelectedViewID => _selectedView;
    public Task WhenIdleAsync() => Task.WhenAll(_operations.Append(_loader.WhenActionsIdleAsync()).ToArray());
    public event EventHandler? SelectedViewChanged;

    public DataRecordViewControl(DataRecordSession session, Guid? initialView = null, CuiThemePreferenceContext? theme = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _selectedView = initialView ?? session.Snapshot.RecordViews.FirstOrDefault()?.ViewID ?? Guid.Empty;
        var registry = new CuiControlRegistry();
        registry.RegisterObjectRenderer("data.record-body", _ => new ScrollViewer { Content = _rows,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
        _loader = new CuiControlLoader(registry);
        _loader.SetSurface("data");
        if (theme is not null) _loader.SetThemePreferenceContext(theme);
        _loader.SetBindingContext(_model); _loader.SetActionDispatcher(this);
        using var stream = typeof(DataRecordViewControl).Assembly.GetManifestResourceStream("HavenOS.Data.RecordViews.cui")
            ?? throw new InvalidDataException("The Data record-view document is unavailable.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser(); var document = parser.Parse(reader.ReadToEnd(), "DataRecordViews.cui");
        var loaded = _loader.TryLoad(document);
        if (loaded.Root is null || parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error) ||
            loaded.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Data record-view document is invalid.");
        _loader.WireBindings(loaded.Root); Content = loaded.Root;
        _model.Set("FilterText", ""); _model.Set("Idle", true);
        _session.Changed += SessionChanged; Render();
    }
    public void SelectView(Guid viewID)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_closed) return;
        if (!_session.Snapshot.RecordViews.Any(v => v.ViewID == viewID)) throw new KeyNotFoundException("ViewNotFound");
        _selectedView = viewID; Render(); SelectedViewChanged?.Invoke(this, EventArgs.Empty);
    }
    private void SessionChanged(object? sender, EventArgs args) => Dispatcher.UIThread.Post(() =>
    {
        if (_closed || _busy) return;
        try { Render(); }
        catch (OperationCanceledException) { _rows.Children.Clear(); _model.Set("Idle", false); Status("This Data session has ended."); }
    });
    private void Render()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_closed) return;
        UpdateDraftState();
        _model.Set("Idle", !_busy && !_readOnly);
        var workbook = _session.Snapshot;
        _rows.Children.Clear();
        _model.Set("Views", workbook.RecordViews.Select(v => new Dictionary<string, object?>
        { ["ID"] = v.ViewID, ["Name"] = v.Name, ["Selected"] = v.ViewID == _selectedView }).ToArray());
        var selected = workbook.RecordViews.SingleOrDefault(v => v.ViewID == _selectedView);
        if (selected is null) { Status("Choose an existing Data view."); return; }
        var table = workbook.Tables.Single(t => t.Id == selected.TableID);
        string Name(Guid id) => table.RelationalSchema?.Fields.SingleOrDefault(f => f.FieldID == id)?.Name ?? "Field " + id.ToString("N")[..6];
        if (_filterField is null || !selected.FieldIDs.Contains(_filterField.Value)) _filterField = selected.FieldIDs.FirstOrDefault();
        _model.Set("Fields", selected.FieldIDs.Select(id => new Dictionary<string, object?>
        { ["ID"] = id, ["Name"] = Name(id) }).ToArray());
        _model.Set("FieldName", _filterField is { } selectedFilterField ? Name(selectedFilterField) : "Choose field");
        _model.Set("CanUndo", _session.CanUndo); _model.Set("CanRedo", _session.CanRedo);
        var projection = _session.Project(selected.ViewID);
        if (selected.Kind == DataRecordViewKind.Kanban)
        {
            var columns = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
            var categories = table.RelationalSchema?.Fields.SingleOrDefault(f => f.FieldID == selected.GroupFieldID)?.Categories ?? [];
            foreach (var group in categories.Concat(projection.Rows.Select(r => r.Group)).Append("").Distinct(StringComparer.Ordinal))
            {
                var column = new StackPanel { Width = 250, Spacing = 8 };
                column.Children.Add(TextLabel(group.Length == 0 ? "Unassigned" : group, bold: true));
                foreach (var row in projection.Rows.Where(r => r.Group == group))
                {
                    var card = BuildRecordEditor(table, selected, row, Name);
                    var move = new ComboBox { ItemsSource = categories.Append("").Distinct().ToArray(), SelectedItem = row.Group, IsEnabled = !_readOnly };
                    move.SelectionChanged += (_, _) => { if (!_busy && move.SelectedItem is string target && target != row.Group) Run(() => _session.MoveCardAsync(selected.ViewID, row.RecordID, target)); };
                    CuiPropertyPresentation.Attach(move);
                    card.Children.Add(move);
                    var frame = new Border { Child = card, BorderThickness = new Thickness(1), Padding = new Thickness(8), CornerRadius = new CornerRadius(8) };
                    frame.Bind(Border.BorderBrushProperty, _rows.GetResourceObservable("CuiControlBorderBrush"));
                    frame.Bind(Border.BackgroundProperty, _rows.GetResourceObservable("CuiPanelBrush"));
                    column.Children.Add(frame);
                }
                columns.Children.Add(column);
            }
            _rows.Children.Add(columns);
        }
        else if (selected.Kind == DataRecordViewKind.Timeline)
        {
            var dated = projection.Rows.Where(r => r.Start is not null && r.TimelineIssue is null).ToArray();
            var origin = dated.Length == 0 ? DateTimeOffset.UtcNow.Date : dated.Min(r => r.Start!.Value).Date;
            _rows.Children.Add(TextLabel("Timeline · daily scale · UTC · " + origin.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            foreach (var row in projection.Rows)
            {
                if (row.Start is { } start && row.TimelineIssue is null)
                {
                    var duration = Math.Max(1, ((row.End ?? start) - start).TotalDays + 1);
                    _rows.Children.Add(new Border { Height = 12, Width = Math.Min(2000, duration * 24),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                        Margin = new Thickness(Math.Clamp((start.Date - origin).TotalDays * 24, 0, 4000), 0, 0, 0),
                        Background = this.TryFindResource("CuiAccentBrush", out var accent) ? accent as Avalonia.Media.IBrush : Avalonia.Media.Brushes.Transparent });
                }
                else _rows.Children.Add(TextLabel(row.TimelineIssue ?? "Undated"));
                _rows.Children.Add(BuildRecordEditor(table, selected, row, Name));
            }
        }
        else
        {
            var header = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
            foreach (var field in selected.FieldIDs) header.Children.Add(TextLabel(Name(field), bold: true, width: 150));
            _rows.Children.Add(header);
            foreach (var row in projection.Rows) _rows.Children.Add(BuildRecordEditor(table, selected, row, Name));
        }
        Status(projection.TotalRows == 0 ? "No records match this view. Filters do not delete records." :
            $"{projection.Rows.Count} of {projection.TotalRows} records · revision {workbook.Version}" + (projection.HasMore ? " · refine filters to show more" : ""));
    }
    private StackPanel BuildRecordEditor(DataTableDefinition table, DataRecordViewDefinition view, DataRecordViewRow row, Func<Guid, string> name)
    {
        var panel = new StackPanel { Orientation = view.Kind == DataRecordViewKind.Kanban ? Avalonia.Layout.Orientation.Vertical : Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        var inputs = new Dictionary<Guid, TextBox>();
        foreach (var field in view.FieldIDs)
        {
            var cell = row.Cells[field];
            var input = new TextBox { Text = _drafts.GetValueOrDefault((row.RecordID, field), cell?.Value ?? ""), PlaceholderText = name(field), MinWidth = 130,
                IsReadOnly = _readOnly || cell?.Kind == DataCellKind.Formula || !string.IsNullOrWhiteSpace(cell?.Formula) };
            Avalonia.Automation.AutomationProperties.SetName(input, name(field));
            if (view.Kind == DataRecordViewKind.Table) input.Width = 150;
            else panel.Children.Add(TextLabel(name(field)));
            if (!input.IsReadOnly) input.PropertyChanged += (_, change) =>
            {
                if (change.Property != TextBox.TextProperty) return;
                var value = input.Text ?? "";
                if (value == (cell?.Value ?? "")) _drafts.Remove((row.RecordID, field));
                else _drafts[(row.RecordID, field)] = value;
                UpdateDraftState();
            };
            CuiPropertyPresentation.Attach(input);
            inputs.Add(field, input); panel.Children.Add(input);
        }
        panel.Children.Add(Button("Save record", async () =>
        {
            var parsed = DataRecordCreationDraft.Capture(table, inputs.Where(i => !i.Value.IsReadOnly)
                .Select(i => new DataRecordDraftField(i.Key, true, i.Value.Text ?? "")).ToArray());
            if (!parsed.Success) throw new InvalidDataException("Invalid field value: " + string.Join(", ", parsed.Issues.Select(i => i.Code)));
            var submitted = inputs.ToDictionary(i => i.Key, i => i.Value.Text ?? "");
            await _session.UpdateRecordAsync(table.Id, row.RecordID, parsed.Values!);
            foreach (var value in submitted)
                if (_drafts.GetValueOrDefault((row.RecordID, value.Key)) == value.Value) _drafts.Remove((row.RecordID, value.Key));
        }));
        return panel;
    }
    private TextBlock TextLabel(string text, bool bold = false, double width = double.NaN)
    {
        var label = new TextBlock { Text = text, Width = width,
            FontWeight = bold ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal };
        // Native record cells live inside the authored Data scope. Bind to that
        // scope, rather than inherit an unrelated host's default text colour.
        label.Bind(TextBlock.ForegroundProperty, _rows.GetResourceObservable("CuiTextBrush"));
        return label;
    }

    private void Status(string text) => _model.Set("Status", text);
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (_closed || _busy) return ValueTask.CompletedTask;
        if (command == "DiscardDrafts") { DiscardDrafts(); return ValueTask.CompletedTask; }
        if (command == "SelectView" && parameter is Guid view) { SelectView(view); return ValueTask.CompletedTask; }
        if (_readOnly) return ValueTask.CompletedTask;
        if (command == "SelectField" && parameter is Guid field) { _filterField = field; Render(); return ValueTask.CompletedTask; }
        return new(Run(async () =>
        {
            var snapshot = _session.Snapshot; var selected = snapshot.RecordViews.Single(v => v.ViewID == _selectedView);
            var table = snapshot.Tables.Single(t => t.Id == selected.TableID);
            switch (command)
            {
                case "AddRecord":
                    var title = table.RelationalSchema?.Fields.FirstOrDefault(f => f.Type == DataFieldType.Text);
                    var values = new Dictionary<Guid, DataScalarRecordValue>();
                    if (title is not null) values[title.FieldID] = new(DataCellKind.Text, System.Text.Json.JsonSerializer.SerializeToElement("New record"));
                    await _session.CreateRecordAsync(table.Id, values, token); break;
                case "Undo": await _session.UndoAsync(token); break;
                case "Redo": await _session.RedoAsync(token); break;
                case "Filter": case "SortAscending": case "SortDescending":
                    if (_filterField is not { } selectedField) throw new InvalidOperationException("Select a field.");
                    _model.TryGetValue("FilterText", out var text); var value = text?.ToString() ?? "";
                    var updated = command == "Filter" ? selected with { Filters = value.Length == 0 ? [] : [new(selectedField, DataRecordFilterOperator.Contains, value)] }
                        : selected with { Sorts = [new(selectedField, command == "SortDescending")] };
                    await _session.SetViewsAsync(snapshot.RecordViews.Select(v => v.ViewID == updated.ViewID ? updated : v).ToArray(), token); break;
                default: throw new InvalidOperationException("Unknown Data record-view action.");
            }
        }));
    }
    private Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text, IsEnabled = !_readOnly }; CuiPropertyPresentation.Attach(button); button.Click += (_, _) => Run(action); return button;
    }
    private Task Run(Func<Task> action)
    {
        if (_closed || _busy || _readOnly) return Task.CompletedTask;
        var task = RunCore(action); _operations.Add(task); _operations.RemoveAll(t => t.IsCompletedSuccessfully); return task;
    }
    private async Task RunCore(Func<Task> action)
    {
        _busy = true; _model.Set("Idle", false); _rows.IsEnabled = false;
        try { await action(); if (!_closed) Render(); }
        catch (Exception failure) { if (!_closed) Status(failure.Message); }
        finally { _busy = false; _model.Set("Idle", !_readOnly); _rows.IsEnabled = true; }
    }
    public async ValueTask DisposeAsync()
    {
        if (HasUnsavedDrafts) throw new InvalidOperationException("Save or explicitly discard the edited Data record fields before closing.");
        _closed = true; _session.Changed -= SessionChanged;
        await Task.WhenAll(_operations.ToArray());
        if (Dispatcher.UIThread.CheckAccess()) _loader.Dispose();
        else await Dispatcher.UIThread.InvokeAsync(() => _loader.Dispose()).GetTask();
    }
    private sealed record FieldChoice(Guid ID, string Name) { public override string ToString() => Name; }
}
