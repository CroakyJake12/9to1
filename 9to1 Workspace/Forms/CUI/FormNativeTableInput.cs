using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Haven.Core.Forms;
using Haven.Application;

namespace HavenOS.Forms;

/// <summary>Edits one canonical table row at a time so authored row bounds do not multiply native
/// controls. Draft invalid cells stay visible; only the shared response engine accepts an answer.</summary>
internal sealed class FormNativeTableInput : StackPanel, IDisposable
{
    private readonly FormTableInputDefinition _definition;
    private readonly Action<JsonElement> _changed;
    private readonly Func<Guid, Func<bool>, CancellationToken, Task<IFormDataReferenceLookupSession?>>? _referenceLookup;
    private readonly List<(Guid ID, Dictionary<Guid, JsonElement> Cells)> _rows = [];
    private readonly List<Action> _detachCells = [];
    private readonly ComboBox _selector = new();
    private readonly StackPanel _cells = new() { Spacing = 6 };
    private readonly Button _add = new() { Content = "Add row" };
    private readonly Button _remove = new() { Content = "Remove row" };
    private bool _refreshing;
    private bool _disposed;

    public FormNativeTableInput(FormTableInputDefinition definition, JsonElement? answer, Action<JsonElement> changed)
        : this(definition, answer, changed, null) { }

    public FormNativeTableInput(FormTableInputDefinition definition, JsonElement? answer, Action<JsonElement> changed,
        Func<Guid, Func<bool>, CancellationToken, Task<IFormDataReferenceLookupSession?>>? referenceLookup)
    {
        FormTableInput.ValidateDefinition(definition);
        if (referenceLookup is null && definition.Columns.Any(column => column.Type == FormTableCellType.Reference))
            throw new NotSupportedException("CapabilityUnavailable: table references require a Data lookup provider.");
        _definition = definition; _changed = changed; _referenceLookup = referenceLookup; Spacing = 6;
        if (answer is { ValueKind: JsonValueKind.Object } value)
        {
            var response = value.Deserialize<FormTableInputResponse>()!;
            foreach (var row in response.Rows)
                _rows.Add((row.RowID, row.Cells.ToDictionary(cell => cell.Key, cell => cell.Value.Clone())));
        }
        else
            foreach (var id in definition.FixedRowIDs ?? []) _rows.Add((id, []));
        AutomationProperties.SetName(_selector, "Table row");
        Children.Add(_selector); Children.Add(_cells);
        var actions = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        actions.Children.Add(_add); actions.Children.Add(_remove); Children.Add(actions);
        _selector.SelectionChanged += SelectRow; _add.Click += AddRow; _remove.Click += RemoveRow;
        RefreshRows(_rows.Count == 0 ? -1 : 0);
    }

    private bool Editable => !_disposed && IsEffectivelyEnabled;
    private void SelectRow(object? sender, SelectionChangedEventArgs args)
    { if (!_refreshing && Editable) RefreshCells(); }
    private void AddRow(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (!Editable || !_definition.AllowAddedRows || _rows.Count >= _definition.MaximumRows) return;
        _rows.Add((Guid.NewGuid(), [])); RefreshRows(_rows.Count - 1); Publish();
    }
    private void RemoveRow(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        var index = _selector.SelectedIndex;
        if (!Editable || index < 0 || index >= _rows.Count
            || (_definition.FixedRowIDs?.Contains(_rows[index].ID) ?? false)) return;
        _rows.RemoveAt(index); RefreshRows(Math.Min(index, _rows.Count - 1)); Publish();
    }
    private void RefreshRows(int index)
    {
        _refreshing = true;
        _selector.ItemsSource = Enumerable.Range(1, _rows.Count).Select(number => $"Row {number}").ToArray();
        _selector.SelectedIndex = index; _refreshing = false; RefreshCells();
    }
    private void RefreshCells()
    {
        foreach (var detach in _detachCells) detach();
        _detachCells.Clear(); _cells.Children.Clear();
        var index = _selector.SelectedIndex;
        _add.IsEnabled = _definition.AllowAddedRows && _rows.Count < _definition.MaximumRows;
        _remove.IsEnabled = index >= 0 && index < _rows.Count && !(_definition.FixedRowIDs?.Contains(_rows[index].ID) ?? false);
        if (index < 0 || index >= _rows.Count) return;
        var row = _rows[index];
        foreach (var column in _definition.Columns)
        {
            row.Cells.TryGetValue(column.ColumnID, out var current);
            void Change(JsonElement value)
            {
                // Replaced controls cannot edit a newly selected row, even if retained by a caller.
                if (!Editable || _selector.SelectedIndex < 0 || _selector.SelectedIndex >= _rows.Count
                    || _rows[_selector.SelectedIndex].ID != row.ID) return;
                row.Cells[column.ColumnID] = value; Publish();
            }
            Control input;
            if (column.Type == FormTableCellType.Reference)
            {
                var reference = new FormNativeReferenceLookupInput(
                    (alive, token) => _referenceLookup!(column.ColumnID, alive, token), Change, current);
                _detachCells.Add(reference.Dispose); input = reference;
            }
            else if (column.Type == FormTableCellType.Number)
            {
                var number = new FormNativeNumberInput(current.ValueKind == JsonValueKind.Number ? current.GetDecimal() : null, Change,
                    current.ValueKind == JsonValueKind.String ? current.GetString() : null);
                _detachCells.Add(number.Dispose); input = number;
            }
            else if (column.Type == FormTableCellType.Boolean)
            {
                var check = new CheckBox { IsThreeState = true, IsChecked = current.ValueKind switch
                    { JsonValueKind.True => true, JsonValueKind.False => false, _ => null } };
                void Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => Change(JsonSerializer.SerializeToElement(check.IsChecked));
                check.IsCheckedChanged += Changed; _detachCells.Add(() => check.IsCheckedChanged -= Changed); input = check;
            }
            else if (column.Type == FormTableCellType.Choice)
            {
                var ids = column.ChoiceIDs!;
                var choice = new ComboBox { ItemsSource = ids, SelectedIndex = current.ValueKind == JsonValueKind.String
                    ? Array.IndexOf(ids, current.GetString()) : -1 };
                void Changed(object? sender, SelectionChangedEventArgs args) => Change(JsonSerializer.SerializeToElement(
                    choice.SelectedIndex >= 0 && choice.SelectedIndex < ids.Length ? ids[choice.SelectedIndex] : null));
                choice.SelectionChanged += Changed; _detachCells.Add(() => choice.SelectionChanged -= Changed); input = choice;
            }
            else
            {
                var text = new TextBox { Text = current.ValueKind == JsonValueKind.String ? current.GetString() : "",
                    PlaceholderText = column.Type == FormTableCellType.Date ? "yyyy-MM-dd" : null };
                void Changed(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
                {
                    if (args.Property == TextBox.TextProperty)
                        Change(JsonSerializer.SerializeToElement(column.Type == FormTableCellType.Date && string.IsNullOrEmpty(text.Text)
                            ? null : text.Text ?? ""));
                }
                text.PropertyChanged += Changed; _detachCells.Add(() => text.PropertyChanged -= Changed); input = text;
            }
            AutomationProperties.SetName(input, column.Label);
            _cells.Children.Add(new TextBlock { Text = column.Label + (column.Required ? " *" : "") });
            _cells.Children.Add(input);
        }
    }
    private void Publish() => _changed(JsonSerializer.SerializeToElement(new FormTableInputResponse(_definition.FieldID,
        _rows.Select(row => new FormTableResponseRow(row.ID, new Dictionary<Guid, JsonElement>(row.Cells))).ToArray())));
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _selector.SelectionChanged -= SelectRow; _add.Click -= AddRow; _remove.Click -= RemoveRow;
        foreach (var detach in _detachCells) detach();
        _detachCells.Clear(); IsEnabled = false;
    }
}
