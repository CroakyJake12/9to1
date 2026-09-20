namespace HavenOS.Apps.Data;

public sealed record DataGridSessionSnapshot(
    DataWorkbookHandle Workbook,
    IReadOnlyList<DataSheetSummary> Sheets,
    DataSheetSummary ActiveSheet,
    DataRangeSnapshot Grid);

/// <summary>
/// First HUI-facing spreadsheet session. It deliberately exposes a fixed 10 x 8
/// viewport and Haven-owned records rather than UNO objects or LibreOffice UI.
/// </summary>
public sealed class DataGridSession : IAsyncDisposable
{
    public const int VisibleRows = 10;
    public const int VisibleColumns = 8;
    private const int MaximumFilterValueLength = 256;

    private readonly IDataSpreadsheetEngine _spreadsheet;
    private DataWorkbookHandle? _workbook;
    private IReadOnlyList<DataSheetSummary> _sheets = [];
    private int _activeSheetIndex;
    private bool _disposed;

    public DataGridSession(IDataSpreadsheetEngine spreadsheet)
    {
        _spreadsheet = spreadsheet ?? throw new ArgumentNullException(nameof(spreadsheet));
    }

    public bool IsOpen => _workbook is not null;
    public bool IsReadOnly => _workbook?.ReadOnly ?? false;

    public async Task<DataGridSessionSnapshot> OpenAsync(
        string path,
        bool readOnly = false,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_workbook is not null)
            throw new InvalidOperationException("A workbook is already open in this Data grid session.");

        var opened = await _spreadsheet.OpenAsync(path, readOnly, cancellationToken).ConfigureAwait(false);
        try
        {
            var sheets = await _spreadsheet.ListSheetsAsync(opened.Id, cancellationToken).ConfigureAwait(false);
            if (sheets.Count == 0)
                throw new InvalidDataException("The Calc workbook contains no worksheets.");

            _workbook = opened;
            _sheets = sheets.ToArray();
            _activeSheetIndex = 0;
            return await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _workbook = null;
            _sheets = [];
            _activeSheetIndex = 0;
            try { await _spreadsheet.CloseAsync(opened.Id, CancellationToken.None).ConfigureAwait(false); }
            catch { }
            throw;
        }
    }

    public async Task<DataGridSessionSnapshot> SelectSheetAsync(
        int sheetIndex,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureOpen();
        if (sheetIndex < 0 || sheetIndex >= _sheets.Count)
            throw new ArgumentOutOfRangeException(nameof(sheetIndex));

        _activeSheetIndex = sheetIndex;
        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DataGridSessionSnapshot> EditCellAsync(
        int row,
        int column,
        string? value,
        string? formula = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var workbook = EnsureEditable();
        ValidateVisibleCell(row, column);

        var address = new DataCellAddress(_sheets[_activeSheetIndex].Name, row, column);
        _ = await _spreadsheet.SetCellAsync(workbook.Id, address, value, formula, cancellationToken).ConfigureAwait(false);
        await _spreadsheet.RecalculateAsync(workbook.Id, cancellationToken).ConfigureAwait(false);
        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<DataNamedRangeSummary>> ListNamedRangesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var workbook = EnsureOpen();
        return _spreadsheet.ListNamedRangesAsync(workbook.Id, cancellationToken);
    }

    public Task<DataNamedRangeSummary> CreateNamedRangeAsync(
        string name,
        int startRow,
        int startColumn,
        int rowCount,
        int columnCount,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var workbook = EnsureEditable();
        var range = VisibleRange(startRow, startColumn, rowCount, columnCount);
        return _spreadsheet.CreateNamedRangeAsync(workbook.Id, name, range, cancellationToken);
    }

    public async Task<IReadOnlyList<DataNamedRangeSummary>> DeleteNamedRangeAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var workbook = EnsureEditable();
        await _spreadsheet.DeleteNamedRangeAsync(workbook.Id, name, cancellationToken).ConfigureAwait(false);
        return await _spreadsheet.ListNamedRangesAsync(workbook.Id, cancellationToken).ConfigureAwait(false);
    }

    public Task<DataListValidationState> GetListValidationAsync(
        int startRow,
        int startColumn,
        int rowCount,
        int columnCount,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var workbook = EnsureOpen();
        var range = VisibleRange(startRow, startColumn, rowCount, columnCount);
        return _spreadsheet.GetListValidationAsync(workbook.Id, range, cancellationToken);
    }

    public Task<DataListValidationState> ApplyListValidationAsync(
        int startRow,
        int startColumn,
        int rowCount,
        int columnCount,
        IReadOnlyList<string> values,
        bool allowBlank = true,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var workbook = EnsureEditable();
        var range = VisibleRange(startRow, startColumn, rowCount, columnCount);
        return _spreadsheet.ApplyListValidationAsync(workbook.Id, range, values, allowBlank, cancellationToken);
    }

    public Task<DataListValidationState> ClearValidationAsync(
        int startRow,
        int startColumn,
        int rowCount,
        int columnCount,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var workbook = EnsureEditable();
        var range = VisibleRange(startRow, startColumn, rowCount, columnCount);
        return _spreadsheet.ClearValidationAsync(workbook.Id, range, cancellationToken);
    }

    public async Task<DataGridSessionSnapshot> SortRangeAsync(
        int startRow,
        int startColumn,
        int rowCount,
        int columnCount,
        int keyColumnOffset,
        bool ascending = true,
        bool containsHeader = true,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var workbook = EnsureEditable();
        var range = VisibleRange(startRow, startColumn, rowCount, columnCount);
        if (keyColumnOffset < 0 || keyColumnOffset >= columnCount)
            throw new ArgumentOutOfRangeException(nameof(keyColumnOffset), "Sort key must identify a column inside the requested visible range.");
        if (containsHeader && rowCount < 2)
            throw new ArgumentException("A sort range marked as containing a header must include at least one data row.", nameof(rowCount));

        _ = await _spreadsheet.SortRangeAsync(
            workbook.Id,
            range,
            keyColumnOffset,
            ascending,
            containsHeader,
            cancellationToken).ConfigureAwait(false);
        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DataGridSessionSnapshot> FilterEqualsAsync(
        int startRow,
        int startColumn,
        int rowCount,
        int columnCount,
        int keyColumnOffset,
        string value,
        bool containsHeader = true,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var workbook = EnsureEditable();
        var range = VisibleRange(startRow, startColumn, rowCount, columnCount);
        if (keyColumnOffset < 0 || keyColumnOffset >= columnCount)
            throw new ArgumentOutOfRangeException(nameof(keyColumnOffset), "Filter key must identify a column inside the requested visible range.");
        if (containsHeader && rowCount < 2)
            throw new ArgumentException("A filter range marked as containing a header must include at least one data row.", nameof(rowCount));
        ValidateFilterValue(value);

        _ = await _spreadsheet.FilterEqualsAsync(
            workbook.Id,
            range,
            keyColumnOffset,
            value,
            containsHeader,
            cancellationToken).ConfigureAwait(false);
        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DataGridSessionSnapshot> ClearFilterAsync(
        int startRow,
        int startColumn,
        int rowCount,
        int columnCount,
        bool containsHeader = true,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var workbook = EnsureEditable();
        var range = VisibleRange(startRow, startColumn, rowCount, columnCount);
        if (containsHeader && rowCount < 2)
            throw new ArgumentException("A filter range marked as containing a header must include at least one data row.", nameof(rowCount));

        _ = await _spreadsheet.ClearFilterAsync(
            workbook.Id,
            range,
            containsHeader,
            cancellationToken).ConfigureAwait(false);
        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<DataGridSessionSnapshot> InsertRowsAsync(int index, int count = 1, CancellationToken cancellationToken = default) =>
        MutateStructureAsync(rows: true, insert: true, index, count, cancellationToken);

    public Task<DataGridSessionSnapshot> DeleteRowsAsync(int index, int count = 1, CancellationToken cancellationToken = default) =>
        MutateStructureAsync(rows: true, insert: false, index, count, cancellationToken);

    public Task<DataGridSessionSnapshot> InsertColumnsAsync(int index, int count = 1, CancellationToken cancellationToken = default) =>
        MutateStructureAsync(rows: false, insert: true, index, count, cancellationToken);

    public Task<DataGridSessionSnapshot> DeleteColumnsAsync(int index, int count = 1, CancellationToken cancellationToken = default) =>
        MutateStructureAsync(rows: false, insert: false, index, count, cancellationToken);

    public async Task<DataGridSessionSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var workbook = EnsureOpen();
        var activeSheet = _sheets[_activeSheetIndex];
        var grid = await _spreadsheet.ReadRangeAsync(
            workbook.Id,
            new DataRangeRequest(activeSheet.Name, 0, 0, VisibleRows, VisibleColumns),
            cancellationToken).ConfigureAwait(false);

        if (grid.Values.Count != VisibleRows || grid.Values.Any(row => row.Count != VisibleColumns))
            throw new InvalidDataException($"Spreadsheet engine returned an invalid first-slice grid; expected {VisibleRows} x {VisibleColumns}.");
        if (grid.RowVisibility is not null && grid.RowVisibility.Count != VisibleRows)
            throw new InvalidDataException($"Spreadsheet engine returned invalid row-visibility metadata; expected {VisibleRows} rows.");

        return new DataGridSessionSnapshot(workbook, _sheets, activeSheet, grid);
    }

    public Task SaveAsAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var workbook = EnsureEditable();
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        return _spreadsheet.SaveAsync(workbook.Id, destinationPath, cancellationToken);
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_workbook is null)
            return;

        var id = _workbook.Id;
        await _spreadsheet.CloseAsync(id, cancellationToken).ConfigureAwait(false);
        _workbook = null;
        _sheets = [];
        _activeSheetIndex = 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_workbook is not null)
        {
            var id = _workbook.Id;
            _workbook = null;
            _sheets = [];
            _activeSheetIndex = 0;
            try { await _spreadsheet.CloseAsync(id, CancellationToken.None).ConfigureAwait(false); }
            catch { }
        }
    }

    private async Task<DataGridSessionSnapshot> MutateStructureAsync(
        bool rows,
        bool insert,
        int index,
        int count,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var workbook = EnsureEditable();
        var visibleCount = rows ? VisibleRows : VisibleColumns;
        if (index < 0 || index >= visibleCount)
            throw new ArgumentOutOfRangeException(nameof(index), $"First-slice structural edits must start inside the visible {(rows ? "row" : "column")} range 0-{visibleCount - 1}.");
        if (count < 1 || count > visibleCount)
            throw new ArgumentOutOfRangeException(nameof(count), $"First-slice structural edits can affect 1-{visibleCount} {(rows ? "rows" : "columns")} at a time.");

        var sheet = _sheets[_activeSheetIndex].Name;
        if (rows)
        {
            if (insert)
                await _spreadsheet.InsertRowsAsync(workbook.Id, sheet, index, count, cancellationToken).ConfigureAwait(false);
            else
                await _spreadsheet.DeleteRowsAsync(workbook.Id, sheet, index, count, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (insert)
                await _spreadsheet.InsertColumnsAsync(workbook.Id, sheet, index, count, cancellationToken).ConfigureAwait(false);
            else
                await _spreadsheet.DeleteColumnsAsync(workbook.Id, sheet, index, count, cancellationToken).ConfigureAwait(false);
        }

        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    private DataWorkbookHandle EnsureOpen() =>
        _workbook ?? throw new InvalidOperationException("No workbook is open in this Data grid session.");

    private DataWorkbookHandle EnsureEditable()
    {
        var workbook = EnsureOpen();
        if (workbook.ReadOnly)
            throw new InvalidOperationException("The workbook is open read-only.");
        return workbook;
    }

    private DataRangeRequest VisibleRange(int startRow, int startColumn, int rowCount, int columnCount)
    {
        ValidateVisibleRange(startRow, startColumn, rowCount, columnCount);
        return new DataRangeRequest(_sheets[_activeSheetIndex].Name, startRow, startColumn, rowCount, columnCount);
    }

    private static void ValidateVisibleCell(int row, int column)
    {
        if (row < 0 || row >= VisibleRows)
            throw new ArgumentOutOfRangeException(nameof(row), $"First-slice rows must be between 0 and {VisibleRows - 1}.");
        if (column < 0 || column >= VisibleColumns)
            throw new ArgumentOutOfRangeException(nameof(column), $"First-slice columns must be between 0 and {VisibleColumns - 1}.");
    }

    private static void ValidateVisibleRange(int startRow, int startColumn, int rowCount, int columnCount)
    {
        if (startRow < 0 || startColumn < 0 || rowCount < 1 || columnCount < 1 ||
            startRow + rowCount > VisibleRows || startColumn + columnCount > VisibleColumns)
            throw new ArgumentOutOfRangeException(nameof(rowCount), $"First-slice range operations must fit wholly inside the {VisibleRows} x {VisibleColumns} viewport.");
    }

    private static void ValidateFilterValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is < 1 or > MaximumFilterValueLength)
            throw new ArgumentException($"First-slice filter values must contain 1-{MaximumFilterValueLength} characters.", nameof(value));
        if (value.Any(char.IsControl))
            throw new ArgumentException("First-slice filter values cannot contain control characters.", nameof(value));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
