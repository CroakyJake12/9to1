using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;

namespace Haven.Desktop.Views.Pages.Data;

/// <summary>Presentation receipt for an actual returned local query preview, not a canonical SqlExecution or a new data store.</summary>
internal sealed record DataQueryResultsSnapshot(Guid WorkbookId, string WorkbookTitle, int WorkbookVersion,
    Guid WorkbookRevisionId, bool IncludedUnsavedChanges, Guid QueryId, string QueryName, string Sql,
    DateTimeOffset ExecutedAtUtc, TimeSpan Duration, DataQueryResult Result, string InputSnapshotSha256);

/// <summary>OS dialog host and typed paging adapter. All application UI is authored in DataQueryResults.cui.</summary>
internal sealed class DataQueryResultsCuiWindow : Window, ICuiBindingContext, ICuiRepeatItemBindingContext,
    ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private const int RowsPerPage = 20;
    private const int ColumnsPerPage = 8;
    private const int TextPageSize = 4096;
    private sealed record ResultColumn(int Index, string Label);
    private sealed record ResultCell(int Row, int Column, string Preview);
    private sealed record ResultRow(int Index, IReadOnlyList<ResultCell> Cells);
    private readonly DataQueryResultsSnapshot _snapshot;
    private readonly string _notice;
    private readonly Func<bool> _originalOwner;
    private readonly CuiControlLoader _loader = new();
    private readonly List<Task> _originalActions = [];
    private IReadOnlyList<ResultColumn> _columns = [];
    private IReadOnlyList<ResultRow> _rows = [];
    private int _rowOffset, _columnOffset, _sqlOffset, _cellOffset;
    private ResultCell? _selected;
    private Task? _presentation, _close;
    private bool _closing, _closed, _allowNativeClose;
    public new event PropertyChangedEventHandler? PropertyChanged;

    internal DataQueryResultsCuiWindow(DataQueryResultsSnapshot snapshot, string notice, Func<bool> originalOwner)
    {
        Dispatcher.UIThread.VerifyAccess();
        _snapshot = snapshot; _notice = Bounded(notice, 1024); _originalOwner = originalOwner;
        if (snapshot.Result.Rows.Count > 200 || snapshot.Result.Columns.Count > 2048
            || snapshot.Result.Rows.Any(row => row.Count != snapshot.Result.Columns.Count))
            throw new InvalidDataException("The local query preview returned unsupported result dimensions.");
        Title = "Data · Query results"; Width = 1180; Height = 820; MinWidth = 760; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Closing += OnClosing;
        RebuildViewport();
    }

    internal Task? OriginalPresentation => _presentation;
    internal Task? OriginalClose => _close;
    private bool Alive => !_closing && !_closed && _originalOwner();
    internal Task PresentOriginalAsync(Window owner)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_presentation is not null) return _presentation;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _presentation = PresentAfterPublicationAsync(start.Task, owner); start.SetResult(); return _presentation;
    }
    private async Task PresentAfterPublicationAsync(Task start, Window owner)
    {
        await start;
        if (!Alive) throw new ObjectDisposedException(nameof(DataQueryResultsCuiWindow));
        List<Exception> failures = [];
        try
        {
            using var stream = typeof(DataQueryResultsCuiWindow).Assembly.GetManifestResourceStream("Haven.Desktop.Data.QueryResults.cui")
                ?? throw new InvalidDataException("The Data query-results CUI resource is unavailable.");
            using var reader = new StreamReader(stream);
            var parser = new CuiRichParser(); var document = parser.Parse(reader.ReadToEnd(), "DataQueryResults.cui");
            if (parser.Diagnostics.Diagnostics.Any(row => row.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("The Data query-results CUI resource is invalid.");
            _loader.SetSurface("QueryResults", "data");
            _loader.SetBindingContext(this); _loader.SetActionDispatcher(this);
            var loaded = _loader.TryLoad(document);
            if (loaded.Root is null || loaded.Diagnostics.Any(row => row.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("The Data query-results surface could not be rendered.");
            _loader.WireBindings(loaded.Root); Content = loaded.Root;
            if (!Alive) throw new ObjectDisposedException(nameof(DataQueryResultsCuiWindow));
            // The existing Data Run task awaits this dialog: its original busy/close guard remains in force.
            var dialog = ShowDialog(owner);
            try { await dialog; } catch when (dialog.IsFaulted) { throw dialog.Exception!; }
        }
        catch (Exception error) { failures.Add(error); }
        try { await CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Data query-results presentation retained its failures.", failures);
    }

    public bool TryGetValue(string path, out object? value)
    {
        var result = _snapshot.Result;
        var cell = SelectedValue;
        value = path switch
        {
            "Source" => $"{Bounded(_snapshot.WorkbookTitle, 160)} · workbook {_snapshot.WorkbookId:D} · saved v{_snapshot.WorkbookVersion}",
            "Revision" => (_snapshot.WorkbookRevisionId == Guid.Empty ? "No saved revision identity is available" : $"Saved revision {_snapshot.WorkbookRevisionId:D}") + (_snapshot.IncludedUnsavedChanges ? " · includes unsaved edits present at execution" : " · no unsaved edits at execution"),
            "InputSnapshot" => $"Input snapshot SHA256 {_snapshot.InputSnapshotSha256}",
            "Target" => $"Local workbook snapshot · {Bounded(result.SourceDescription, 160)} · read-only",
            "Execution" => $"{Bounded(_snapshot.QueryName, 160)} · query {_snapshot.QueryId:D} · {_snapshot.ExecutedAtUtc.ToLocalTime():G} · {_snapshot.Duration.TotalMilliseconds:N0} ms",
            "Notice" => _notice,
            "ResultSummary" => $"{result.Rows.Count} returned rows · {result.Columns.Count} columns. " + (result.Truncated
                ? "The execution preview is truncated; the total matching row count is unknown."
                : "All rows in this returned result are available here."),
            "Viewport" => $"Rows {(_rows.Count == 0 ? 0 : _rowOffset + 1)}–{_rowOffset + _rows.Count} of {result.Rows.Count} returned · columns {(_columns.Count == 0 ? 0 : _columnOffset + 1)}–{_columnOffset + _columns.Count} of {result.Columns.Count}",
            "Columns" => _columns,
            "Rows" => _rows,
            "Empty" => result.Rows.Count == 0,
            "CanPreviousRows" => IsActionAvailable("data.results.rows.previous"),
            "CanNextRows" => IsActionAvailable("data.results.rows.next"),
            "CanPreviousColumns" => IsActionAvailable("data.results.columns.previous"),
            "CanNextColumns" => IsActionAvailable("data.results.columns.next"),
            "SqlText" => TextPage(_snapshot.Sql, _sqlOffset),
            "SqlRange" => TextRange(_snapshot.Sql, _sqlOffset),
            "CanPreviousSql" => IsActionAvailable("data.results.sql.previous"),
            "CanNextSql" => IsActionAvailable("data.results.sql.next"),
            "CellTitle" => _selected is { } selected ? $"Returned row {selected.Row + 1}, column {selected.Column + 1} · {Bounded(result.Columns[selected.Column], 160)}" : "Select a returned cell to inspect its value.",
            "CellText" => _selected is null ? "" : TextPage(cell, _cellOffset),
            "CellRange" => _selected is null ? "Cell previews are shortened; selection shows the actual returned value in text pages." : TextRange(cell, _cellOffset),
            "CanPreviousCell" => IsActionAvailable("data.results.cell.previous"),
            "CanNextCell" => IsActionAvailable("data.results.cell.next"),
            _ => null
        };
        return path is "Source" or "Revision" or "InputSnapshot" or "Target" or "Execution" or "Notice" or "ResultSummary" or "Viewport"
            or "Columns" or "Rows" or "Empty" or "CanPreviousRows" or "CanNextRows" or "CanPreviousColumns" or "CanNextColumns"
            or "SqlText" or "SqlRange" or "CanPreviousSql" or "CanNextSql" or "CellTitle" or "CellText" or "CellRange" or "CanPreviousCell" or "CanNextCell";
    }

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        if (!Alive) return false;
        if (item is ResultColumn column && _columns.Any(row => ReferenceEquals(row, column)))
        {
            value = path switch { "id" => column.Index.ToString(), "label" => column.Label, _ => null };
            return path is "id" or "label";
        }
        if (item is ResultRow rowItem && _rows.Any(row => ReferenceEquals(row, rowItem)))
        {
            value = path switch { "id" => rowItem.Index.ToString(), "number" => (rowItem.Index + 1).ToString(), "cells" => rowItem.Cells, _ => null };
            return path is "id" or "number" or "cells";
        }
        if (item is ResultCell cell && _rows.Any(row => row.Cells.Any(candidate => ReferenceEquals(candidate, cell))))
        {
            value = path switch { "id" => $"{cell.Row}:{cell.Column}", "preview" => cell.Preview,
                "name" => $"Inspect returned row {cell.Row + 1}, column {cell.Column + 1}", _ => null };
            return path is "id" or "preview" or "name";
        }
        return false;
    }
    public bool TrySetItemValue(object item, string path, object? value) => false;
    private string SelectedValue => _selected is { } selected ? _snapshot.Result.Rows[selected.Row][selected.Column] : "";
    public bool? IsActionAvailable(string command) => Alive && (command switch
    {
        "data.results.rows.previous" => _rowOffset > 0,
        "data.results.rows.next" => _rowOffset + RowsPerPage < _snapshot.Result.Rows.Count,
        "data.results.columns.previous" => _columnOffset > 0,
        "data.results.columns.next" => _columnOffset + ColumnsPerPage < _snapshot.Result.Columns.Count,
        "data.results.sql.previous" => _sqlOffset > 0,
        "data.results.sql.next" => TextEnd(_snapshot.Sql, _sqlOffset) < _snapshot.Sql.Length,
        "data.results.cell.previous" => _selected is not null && _cellOffset > 0,
        "data.results.cell.next" => _selected is not null && TextEnd(SelectedValue, _cellOffset) < SelectedValue.Length,
        "data.results.cell.select" or "data.results.close" => true,
        _ => false
    });
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess(); cancellationToken.ThrowIfCancellationRequested();
        if (IsActionAvailable(command) != true) return ValueTask.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _originalActions.RemoveAll(task => task.IsCompletedSuccessfully); _originalActions.Add(completion.Task);
        try
        {
            if (command == "data.results.cell.select")
            {
                if (parameter is not ResultCell cell || !_rows.Any(row => row.Cells.Any(candidate => ReferenceEquals(candidate, cell))))
                    throw new ArgumentException("Select a cell from the original displayed result page.");
                _selected = cell; _cellOffset = 0;
            }
            else
            {
                if (parameter is not null) throw new ArgumentException("This result action uses its original returned snapshot.");
                switch (command)
                {
                    case "data.results.rows.previous": _rowOffset = Math.Max(0, _rowOffset - RowsPerPage); RebuildViewport(); break;
                    case "data.results.rows.next": _rowOffset += RowsPerPage; RebuildViewport(); break;
                    case "data.results.columns.previous": _columnOffset = Math.Max(0, _columnOffset - ColumnsPerPage); RebuildViewport(); break;
                    case "data.results.columns.next": _columnOffset += ColumnsPerPage; RebuildViewport(); break;
                    case "data.results.sql.previous": _sqlOffset = PreviousTextOffset(_snapshot.Sql, _sqlOffset); break;
                    case "data.results.sql.next": _sqlOffset = TextEnd(_snapshot.Sql, _sqlOffset); break;
                    case "data.results.cell.previous": _cellOffset = PreviousTextOffset(SelectedValue, _cellOffset); break;
                    case "data.results.cell.next": _cellOffset = TextEnd(SelectedValue, _cellOffset); break;
                    case "data.results.close": _ = CloseAndDrainAsync(); break;
                }
            }
            if (Alive) PropertyChanged?.Invoke(this, new(null));
            completion.SetResult();
        }
        catch (Exception error) { completion.SetException(error); }
        return new(completion.Task);
    }

    private void RebuildViewport()
    {
        var result = _snapshot.Result;
        _columns = Enumerable.Range(_columnOffset, Math.Min(ColumnsPerPage, result.Columns.Count - _columnOffset))
            .Select(index => new ResultColumn(index, Bounded(result.Columns[index], 96))).ToArray();
        _rows = Enumerable.Range(_rowOffset, Math.Min(RowsPerPage, result.Rows.Count - _rowOffset))
            .Select(row => new ResultRow(row, _columns.Select(column => new ResultCell(row, column.Index,
                result.Rows[row][column.Index].Length == 0 ? "(empty)" : Bounded(result.Rows[row][column.Index], 160))).ToArray())).ToArray();
        _selected = null; _cellOffset = 0;
    }
    private static int TextEnd(string text, int offset)
    {
        var end = (int)Math.Min(text.Length, (long)offset + TextPageSize);
        if (end > offset && end < text.Length && char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end])) end--;
        return end;
    }
    private static int PreviousTextOffset(string text, int offset)
    {
        var previous = Math.Max(0, offset - TextPageSize);
        if (previous > 0 && char.IsLowSurrogate(text[previous]) && char.IsHighSurrogate(text[previous - 1])) previous--;
        return previous;
    }
    private static string TextPage(string text, int offset) => text[offset..TextEnd(text, offset)];
    private static string TextRange(string text, int offset) => text.Length == 0 ? "Empty returned text (0 UTF-16 code units)."
        : $"UTF-16 code units {offset + 1}–{TextEnd(text, offset)} of {text.Length}. Text pages preserve the returned value.";
    private static string Bounded(string text, int maximum)
    {
        if (text.Length <= maximum) return text;
        var end = maximum;
        if (char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end])) end--;
        return text[..end] + "…";
    }
    private void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_allowNativeClose) return;
        args.Cancel = true; _ = CloseAndDrainAsync(); // Coalesced task is retained before any await.
    }
    internal Task CloseAndDrainAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_close is not null) return _close;
        _closing = true;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _close = CloseAfterPublicationAsync(start.Task); start.SetResult(); return _close;
    }
    private async Task CloseAfterPublicationAsync(Task start)
    {
        await start;
        List<Exception> failures = [];
        try { _loader.Dispose(); } catch (Exception error) { failures.Add(error); }
        var originals = _originalActions.Append(_loader.WhenActionsIdleAsync()).ToArray();
        foreach (var actual in originals)
            try { await actual; } catch (Exception error) { failures.Add(actual.Exception ?? error); }
        try { Content = null; } catch (Exception error) { failures.Add(error); }
        try { Closing -= OnClosing; } catch (Exception error) { failures.Add(error); }
        _allowNativeClose = true; _closed = true;
        try { Close(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Data result-view cleanup retained its original faults.", failures);
    }
}
