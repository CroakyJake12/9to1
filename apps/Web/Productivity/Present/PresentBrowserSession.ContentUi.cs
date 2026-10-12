using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;

namespace NineToOne.Web.Productivity.Present;

public sealed partial class PresentBrowserSession
{
    private static readonly string[] ChartTypes = Enum.GetNames<PresentChartType>();
    private (PresentEditor Editor, Guid Slide, Guid Object)? _contentSelection;
    private int _tableRow, _chartSeries;
    private PresentEditor? _contentProjectionEditor;
    private PresentElement? _contentProjectionElement;
    private long _contentProjectionGeneration = -1;
    private PresentTable? _contentTable;
    private PresentChart? _contentChart;
    private int _contentEditDepth;
    private PresentElement? SelectedTable => Editor?.SelectedElements is [{ Kind: PresentElementKind.Table } table] ? table : null;
    private PresentElement? SelectedChart => Editor?.SelectedElements is [{ Kind: PresentElementKind.Chart } chart] ? chart : null;

    private void RefreshContentUi()
    {
        // These are private, read-only projections for one UI refresh. Commands
        // still decode a fresh owner value before making any mutation.
        _contentProjectionGeneration = -1;
        EnsureContentProjection();
        var selected = SelectedTable ?? SelectedChart;
        var current = Editor is { } editor && selected is not null ? (editor, editor.Selection.SlideId, selected.Id) : ((PresentEditor, Guid, Guid)?)null;
        if (_contentSelection != current) { _contentSelection = current; _tableRow = 0; _chartSeries = 0; }
        if (_contentTable is { } table) _tableRow = Math.Clamp(_tableRow, 0, table.Rows - 1);
        if (_contentChart is { } chart) _chartSeries = Math.Clamp(_chartSeries, 0, chart.Series.Count - 1);
    }

    private void EnsureContentProjection()
    {
        var element = SelectedTable ?? SelectedChart;
        if (ReferenceEquals(_contentProjectionEditor, Editor) && ReferenceEquals(_contentProjectionElement, element)
            && _contentProjectionGeneration == _generation) return;
        _contentProjectionEditor = Editor;
        _contentProjectionElement = element;
        _contentProjectionGeneration = _generation;
        _contentTable = element?.Kind == PresentElementKind.Table ? element.ReadTable() : null;
        _contentChart = element?.Kind == PresentElementKind.Chart ? element.ReadChart() : null;
    }

    private PresentTable? ContentTable { get { EnsureContentProjection(); return _contentTable; } }
    private PresentChart? ContentChart { get { EnsureContentProjection(); return _contentChart; } }

    // This is the capability tail of the existing session admission/lifecycle
    // gate. It never supplies document authority independently of that gate.
    private bool IsContentActionAvailable(string command) => command switch
    {
        "AddTable" or "AddChart" => Editor is not null && !IsReadOnly,
        "EditTable" => SelectedTable is { Locked: false } && !IsReadOnly,
        "EditChart" => SelectedChart is { Locked: false } && !IsReadOnly,
        "InsertTableRow" => SelectedTable is { Locked: false } && !IsReadOnly && ContentTable!.Rows < 100,
        "DeleteTableRow" => SelectedTable is { Locked: false } && !IsReadOnly && ContentTable!.Rows > 1,
        "InsertTableColumn" => SelectedTable is { Locked: false } && !IsReadOnly && ContentTable!.Columns < 100,
        "DeleteTableColumn" => SelectedTable is { Locked: false } && !IsReadOnly && ContentTable!.Columns > 1,
        "AddChartCategory" => SelectedChart is { Locked: false } && !IsReadOnly && ContentChart!.Categories.Count < 100,
        "RemoveChartCategory" => SelectedChart is { Locked: false } && !IsReadOnly && ContentChart!.Categories.Count > 1,
        "AddChartSeries" => SelectedChart is { Locked: false } && !IsReadOnly && ContentChart!.Series.Count < 32,
        "RemoveChartSeries" => SelectedChart is { Locked: false } && !IsReadOnly && ContentChart!.Series.Count > 1,
        "TextVerticalTop" or "TextVerticalMiddle" or "TextVerticalBottom" => SelectedTextElement is { Locked: false } && !IsReadOnly,
        _ => false
    };

    private async ValueTask<bool> TryExecuteContentUiAsync(string command, object? parameter, CancellationToken token)
    {
        switch (command)
        {
            case "AddTable":
                await EditAsync(owner => { owner.AddTable(owner.Selection.SlideId, 3, 3); return true; }, token);
                _showInspector = true; Notify(); return true;
            case "AddChart":
                var type = PresentChartType.Column;
                if (parameter is not null && (parameter is not string name || !Enum.TryParse(name, out type) || !Enum.IsDefined(type)))
                { Failure<bool>("InvalidArgument", "Choose a supported chart type."); Notify(); return true; }
                await EditAsync(owner => { owner.AddChart(owner.Selection.SlideId, type); return true; }, token);
                _showInspector = true; Notify(); return true;
            case "InsertTableRow" or "DeleteTableRow" or "InsertTableColumn" or "DeleteTableColumn":
                var tableId = SelectedTable!.Id;
                await EditAsync(owner => command switch
                {
                    "InsertTableRow" => owner.InsertTableRow(owner.Selection.SlideId, tableId, _tableRow + 1),
                    "DeleteTableRow" => owner.DeleteTableRow(owner.Selection.SlideId, tableId, _tableRow),
                    "InsertTableColumn" => owner.InsertTableColumn(owner.Selection.SlideId, tableId, SelectedTable!.ReadTable().Columns),
                    _ => owner.DeleteTableColumn(owner.Selection.SlideId, tableId, SelectedTable!.ReadTable().Columns - 1)
                }, token);
                return true;
            case "AddChartCategory" or "RemoveChartCategory" or "AddChartSeries" or "RemoveChartSeries":
                var chartId = SelectedChart!.Id;
                await EditAsync(owner =>
                {
                    var chart = SelectedChart!.ReadChart();
                    switch (command)
                    {
                        case "AddChartCategory": chart.Categories.Add($"Category {chart.Categories.Count + 1}"); break;
                        case "RemoveChartCategory": chart.Categories.RemoveAt(chart.Categories.Count - 1); break;
                        case "AddChartSeries": chart.Series.Add(new() { Name = $"Series {chart.Series.Count + 1}" }); break;
                        case "RemoveChartSeries": chart.Series.RemoveAt(_chartSeries); break;
                    }
                    return owner.SetChartData(owner.Selection.SlideId, chartId, chart.Categories, chart.Series);
                }, token);
                return true;
            case "TextVerticalTop" or "TextVerticalMiddle" or "TextVerticalBottom":
                await EditAsync(owner => UpdateTextStyle(owner, style => style.VerticalAlignment = command switch
                {
                    "TextVerticalTop" => PresentTextVerticalAlignment.Top,
                    "TextVerticalMiddle" => PresentTextVerticalAlignment.Middle,
                    _ => PresentTextVerticalAlignment.Bottom
                }), token);
                return true;
            default: return false;
        }
    }

    private bool TryGetContentUiValue(string path, out object? value)
    {
        if (path is not ("HasTableSelection" or "HasChartSelection" or "CanEditTable" or "CanEditChart"
            or "CanInsertTableRow" or "CanDeleteTableRow" or "CanInsertTableColumn" or "CanDeleteTableColumn"
            or "CanAddChartCategory" or "CanRemoveChartCategory" or "CanAddChartSeries" or "CanRemoveChartSeries"
            or "TableRows" or "TableRowIndex" or "TableSummary" or "TableCells" or "ChartTypes" or "ChartTypeIndex"
            or "ChartSeriesNames" or "ChartSeriesIndex" or "ChartSeriesName" or "ChartShowLegend" or "ChartDataRows"))
        { value = null; return false; }
        var table = ContentTable;
        var chart = ContentChart;
        value = path switch
        {
            "HasTableSelection" => table is not null,
            "HasChartSelection" => chart is not null,
            "CanEditTable" => IsActionAvailable("EditTable") == true,
            "CanEditChart" => IsActionAvailable("EditChart") == true,
            "CanInsertTableRow" => IsActionAvailable("InsertTableRow") == true,
            "CanDeleteTableRow" => IsActionAvailable("DeleteTableRow") == true,
            "CanInsertTableColumn" => IsActionAvailable("InsertTableColumn") == true,
            "CanDeleteTableColumn" => IsActionAvailable("DeleteTableColumn") == true,
            "CanAddChartCategory" => IsActionAvailable("AddChartCategory") == true,
            "CanRemoveChartCategory" => IsActionAvailable("RemoveChartCategory") == true,
            "CanAddChartSeries" => IsActionAvailable("AddChartSeries") == true,
            "CanRemoveChartSeries" => IsActionAvailable("RemoveChartSeries") == true,
            "TableRows" => Enumerable.Range(1, table?.Rows ?? 0).Select(row => $"Row {row}").ToArray(),
            "TableRowIndex" => _tableRow,
            "TableSummary" => table is null ? "" : $"{table.Rows} rows × {table.Columns} columns",
            "TableCells" => TableCellBindings(),
            "ChartTypes" => ChartTypes,
            "ChartTypeIndex" => (int)(chart?.Type ?? PresentChartType.Column),
            "ChartSeriesNames" => chart?.Series.Select(series => series.Name).ToArray() ?? [],
            "ChartSeriesIndex" => _chartSeries,
            "ChartSeriesName" => chart?.Series[Math.Clamp(_chartSeries, 0, chart.Series.Count - 1)].Name ?? "",
            "ChartShowLegend" => chart?.ShowLegend ?? false,
            "ChartDataRows" => ChartDataBindings(),
            _ => MissingUiValue
        };
        return !ReferenceEquals(value, MissingUiValue);
    }

    private bool? TrySetContentUiValue(string path, object? value)
    {
        if (path is not ("TableRowIndex" or "ChartTypeIndex" or "ChartSeriesIndex" or "ChartSeriesName" or "ChartShowLegend")) return null;
        var element = path == "TableRowIndex" ? SelectedTable : SelectedChart;
        if (element is null || IsActionAvailable(path == "TableRowIndex" ? "EditTable" : "EditChart") != true) return false;
        var editor = Editor!;
        if (path == "ChartShowLegend" && value is bool show)
            return CommitContentEdit(editor, () => editor.SetChartStyle(editor.Selection.SlideId, element.Id, element.ReadChart().Style, show));
        if (path == "ChartSeriesName" && value is string name && !string.IsNullOrWhiteSpace(name))
        {
            var chart = element.ReadChart();
            if (chart.Series[_chartSeries].Name == name.Trim()) return true;
            chart.Series[_chartSeries].Name = name;
            return CommitContentEdit(editor, () => editor.SetChartData(editor.Selection.SlideId, element.Id, chart.Categories, chart.Series));
        }
        if (!Number(value, out var number) || number != Math.Truncate(number)) return false;
        var index = (int)number;
        switch (path)
        {
            case "TableRowIndex" when index >= 0 && index < element.ReadTable().Rows: _tableRow = index; Notify(); return true;
            case "ChartSeriesIndex" when index >= 0 && index < element.ReadChart().Series.Count: _chartSeries = index; Notify(); return true;
            case "ChartTypeIndex" when index >= 0 && index < ChartTypes.Length:
                return CommitContentEdit(editor, () => editor.SetChartType(editor.Selection.SlideId, element.Id, (PresentChartType)index));
            default: return false;
        }
    }

    private bool CommitContentEdit(PresentEditor editor, Func<bool> edit)
    {
        // Owner change events still advance the generation and dirty state.
        // Publish the new field contexts only once the whole synchronous edit
        // and the durable revision restoration have completed.
        ++_contentEditDepth;
        try
        {
            editor.CommitLiveTextEdit();
            edit();
            return true;
        }
        finally
        {
            editor.Document.Version = DurableRevision;
            --_contentEditDepth;
            Notify();
        }
    }

    private PresentContentField[] TableCellBindings()
    {
        if (SelectedTable is not { } element || Editor is not { } editor) return [];
        var generation = _generation; var slideId = editor.Selection.SlideId; var row = _tableRow; var table = ContentTable!;
        if (row >= table.Rows) return [];
        return Enumerable.Range(0, table.Columns).Select(column => new PresentContentField(
            $"{element.Id}:{row}:{column}", $"Column {column + 1}", table.GetCell(row, column).Text,
            next =>
            {
                if (generation != _generation || row != _tableRow || !CurrentContentTarget(editor, slideId, element.Id, "EditTable") || next is not string text) return false;
                var current = SelectedTable!.ReadTable();
                if (row >= current.Rows || column >= current.Columns) return false;
                return CommitContentEdit(editor, () => editor.SetTableCellText(slideId, element.Id, row, column, text));
            })).ToArray();
    }

    private PresentChartDataRow[] ChartDataBindings()
    {
        if (SelectedChart is not { } element || Editor is not { } editor) return [];
        var generation = _generation; var slideId = editor.Selection.SlideId; var series = _chartSeries; var chart = ContentChart!;
        if (series >= chart.Series.Count) return [];
        return Enumerable.Range(0, chart.Categories.Count).Select(category => new PresentChartDataRow(
            $"{element.Id}:{series}:{category}", category + 1, chart.Categories[category], chart.Series[series].Values[category],
            (field, next) =>
            {
                if (generation != _generation || series != _chartSeries || !CurrentContentTarget(editor, slideId, element.Id, "EditChart")) return false;
                var current = SelectedChart!.ReadChart();
                if (category >= current.Categories.Count || series >= current.Series.Count) return false;
                if (field == "Category" && next is string label && !string.IsNullOrWhiteSpace(label))
                {
                    if (current.Categories[category] == label.Trim()) return true;
                    current.Categories[category] = label;
                }
                else if (field == "Value" && Number(next, out var number))
                {
                    if (current.Series[series].Values[category] == number) return true;
                    current.Series[series].Values[category] = number;
                }
                else return false;
                return CommitContentEdit(editor, () => editor.SetChartData(slideId, element.Id, current.Categories, current.Series));
            })).ToArray();
    }

    private bool CurrentContentTarget(PresentEditor editor, Guid slide, Guid element, string capability) =>
        ReferenceEquals(editor, Editor) && editor.Selection.SlideId == slide && editor.SelectedElements is [var selected]
        && selected.Id == element && IsActionAvailable(capability) == true;
}

public sealed record PresentContentField(string Id, string Label, string Text, Func<object?, bool> Write) : ICuiWritableBindingContext
{
    public bool TryGetValue(string path, out object? value)
    { value = path switch { "Id" => Id, "Label" => Label, "Text" => Text, _ => null }; return path is "Id" or "Label" or "Text"; }
    public bool TrySetValue(string path, object? value) => path == "Text" && Write(value);
}

public sealed record PresentChartDataRow(string Id, int Number, string Category, double Value, Func<string, object?, bool> Write) : ICuiWritableBindingContext
{
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch { "Id" => Id, "Category" => Category, "Value" => Value,
            "CategoryLabel" => $"Category {Number}", "ValueLabel" => $"Value {Number}", _ => null };
        return path is "Id" or "Category" or "Value" or "CategoryLabel" or "ValueLabel";
    }
    public bool TrySetValue(string path, object? value) => path is "Category" or "Value" && Write(path, value);
}
