namespace HavenOS.Apps.Data.Cui;

public sealed record DataCuiSelection(int Row, int Column, string Address);

public sealed record DataCuiSortState(int KeyColumn, bool Ascending);

public sealed record DataCuiFilterState(bool Enabled, int KeyColumn, string Value);

public sealed record DataCuiQueryPanelState(
    bool IsOpen,
    string DatabaseName,
    string Sql,
    IReadOnlyList<DataPublishedTable> PublishedTables,
    DataQueryExecution? LastExecution);

public sealed record DataCuiWorkspaceSnapshot(
    DataGridSessionSnapshot Workbook,
    DataCuiSelection Selection,
    string FormulaBarText,
    DataCuiSortState Sort,
    DataCuiFilterState Filter,
    DataCuiQueryPanelState Query,
    bool IsDirty,
    string Status);

/// <summary>
/// CUI presentation controller over the existing Calc/DuckDB sessions. It owns no worker or
/// renderer types and updates visible state only after an engine operation has completed.
/// </summary>
public sealed class DataCuiWorkspaceController : IAsyncDisposable
{
    private static readonly DataWorkbookActionCapability[] ReadActions =
    [
        new(DataSelectCellAction.Id, "Select cell", false, "workbook.read"),
    ];

    private static readonly DataWorkbookActionCapability[] WriteActions =
    [
        new(DataEditCellAction.Id, "Edit cell", true, "workbook.write"),
        new(DataSetFormulaAction.Id, "Set formula", true, "workbook.write"),
        new(DataSortVisibleRangeAction.Id, "Sort visible rows", true, "workbook.write"),
        new(DataFilterVisibleRangeAction.Id, "Filter visible rows", true, "workbook.write"),
        new(DataClearVisibleFilterAction.Id, "Clear visible filter", true, "workbook.write"),
        new(DataSaveWorkbookAsAction.Id, "Save workbook as", true, "files.write"),
    ];

    private readonly DataGridSession _grid;
    private readonly DataQuerySession _query;
    private readonly IDataWorkbookActionAuthorizer? _actionAuthorizer;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateLock = new();
    private DataCuiWorkspaceSnapshot? _state;
    private bool _disposed;

    public DataCuiWorkspaceController(
        DataGridSession grid,
        DataQuerySession query,
        DataCuiSurfaceDefinition surface,
        IDataWorkbookActionAuthorizer? actionAuthorizer = null)
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _query = query ?? throw new ArgumentNullException(nameof(query));
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _actionAuthorizer = actionAuthorizer;
    }

    public DataCuiSurfaceDefinition Surface { get; }

    public DataCuiWorkspaceSnapshot Current
    {
        get
        {
            lock (_stateLock)
                return _state ?? throw new InvalidOperationException("No workbook is open in the Data CUI workspace.");
        }
    }

    public async Task<DataCuiWorkspaceSnapshot> OpenAsync(
        string workbookPath,
        bool readOnly = false,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var workbook = await _grid.OpenAsync(workbookPath, readOnly, cancellationToken).ConfigureAwait(false);
            var state = new DataCuiWorkspaceSnapshot(
                workbook,
                Selection(0, 0),
                workbook.Grid.Values[0][0],
                new DataCuiSortState(0, true),
                new DataCuiFilterState(false, 0, string.Empty),
                new DataCuiQueryPanelState(false, string.Empty, string.Empty, [], null),
                false,
                $"Opened {Path.GetFileName(workbook.Workbook.Path)}.");
            return SetState(state);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public DataCuiWorkspaceSnapshot SelectCell(int row, int column)
    {
        ThrowIfDisposed();
        ValidateCell(row, column);
        lock (_stateLock)
        {
            var state = _state ?? throw new InvalidOperationException("No workbook is open in the Data CUI workspace.");
            var selected = Selection(row, column);
            _state = state with
            {
                Selection = selected,
                FormulaBarText = state.Workbook.Grid.Values[row][column],
                Status = $"Selected {selected.Address}.",
            };
            return _state;
        }
    }

    public async Task<DataCuiWorkspaceSnapshot> CommitFormulaBarAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return await MutateGridAsync(async (state, token) =>
        {
            var formula = text.StartsWith('=') ? text : null;
            var value = formula is null ? text : string.Empty;
            var workbook = await _grid.EditCellAsync(
                state.Selection.Row,
                state.Selection.Column,
                value,
                formula,
                token).ConfigureAwait(false);
            return state with
            {
                Workbook = workbook,
                FormulaBarText = text,
                IsDirty = true,
                Status = formula is null
                    ? $"Updated {state.Selection.Address}."
                    : $"Recalculated {state.Selection.Address} from {text}.",
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DataCuiWorkspaceSnapshot> SelectSheetAsync(
        int sheetIndex,
        CancellationToken cancellationToken = default)
    {
        return await MutateGridAsync(async (state, token) =>
        {
            var workbook = await _grid.SelectSheetAsync(sheetIndex, token).ConfigureAwait(false);
            return state with
            {
                Workbook = workbook,
                Selection = Selection(0, 0),
                FormulaBarText = workbook.Grid.Values[0][0],
                Filter = new DataCuiFilterState(false, 0, string.Empty),
                Status = $"Selected sheet {workbook.ActiveSheet.Name}.",
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DataCuiWorkspaceSnapshot> SortVisibleAsync(
        int keyColumn,
        bool ascending,
        CancellationToken cancellationToken = default)
    {
        ValidateColumn(keyColumn);
        return await MutateGridAsync(async (state, token) =>
        {
            var workbook = await _grid.SortRangeAsync(
                0, 0, DataGridSession.VisibleRows, DataGridSession.VisibleColumns,
                keyColumn, ascending, containsHeader: true, token).ConfigureAwait(false);
            return state with
            {
                Workbook = workbook,
                Sort = new DataCuiSortState(keyColumn, ascending),
                IsDirty = true,
                Status = $"Sorted visible rows by column {ColumnName(keyColumn)} {(ascending ? "ascending" : "descending")}.",
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DataCuiWorkspaceSnapshot> FilterVisibleAsync(
        int keyColumn,
        string value,
        CancellationToken cancellationToken = default)
    {
        ValidateColumn(keyColumn);
        return await MutateGridAsync(async (state, token) =>
        {
            var workbook = await _grid.FilterEqualsAsync(
                0, 0, DataGridSession.VisibleRows, DataGridSession.VisibleColumns,
                keyColumn, value, containsHeader: true, token).ConfigureAwait(false);
            return state with
            {
                Workbook = workbook,
                Filter = new DataCuiFilterState(true, keyColumn, value),
                IsDirty = true,
                Status = $"Filtered column {ColumnName(keyColumn)} to literal value '{value}'.",
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DataCuiWorkspaceSnapshot> ClearFilterAsync(CancellationToken cancellationToken = default)
    {
        return await MutateGridAsync(async (state, token) =>
        {
            var workbook = await _grid.ClearFilterAsync(
                0, 0, DataGridSession.VisibleRows, DataGridSession.VisibleColumns,
                containsHeader: true, token).ConfigureAwait(false);
            return state with
            {
                Workbook = workbook,
                Filter = new DataCuiFilterState(false, state.Filter.KeyColumn, string.Empty),
                IsDirty = true,
                Status = "Cleared the visible-range filter.",
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DataCuiWorkspaceSnapshot> SaveAsAsync(
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        return await MutateGridAsync(async (state, token) =>
        {
            await _grid.SaveAsAsync(destinationPath, token).ConfigureAwait(false);
            return state with
            {
                IsDirty = false,
                Status = $"Saved {Path.GetFileName(destinationPath)}.",
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DataCuiWorkspaceSnapshot> OpenQueryPanelAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        return await MutateGridAsync(async (state, token) =>
        {
            var query = await _query.OpenAsync(databasePath, token).ConfigureAwait(false);
            return state with
            {
                Query = new DataCuiQueryPanelState(true, Path.GetFileName(query.DatabasePath), string.Empty, query.PublishedTables, null),
                Status = $"Opened query database {Path.GetFileName(databasePath)}.",
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DataCuiWorkspaceSnapshot> PublishVisibleRangeAsync(
        string tableName,
        bool firstRowIsHeaders = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        return await MutateGridAsync(async (state, token) =>
        {
            EnsureQueryOpen(state);
            _ = await _query.PublishRangeAsync(
                state.Workbook.Workbook.Id,
                new DataRangeRequest(state.Workbook.ActiveSheet.Name, 0, 0, DataGridSession.VisibleRows, DataGridSession.VisibleColumns),
                tableName,
                firstRowIsHeaders,
                token).ConfigureAwait(false);
            var query = _query.Snapshot();
            return state with
            {
                Query = state.Query with { PublishedTables = query.PublishedTables },
                Status = $"Published the visible range as {tableName}.",
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DataCuiWorkspaceSnapshot> ExecuteReadOnlyQueryAsync(
        string sql,
        int maxRows = 200,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        return await MutateGridAsync(async (state, token) =>
        {
            EnsureQueryOpen(state);
            var execution = await _query.ExecuteAsync(sql, maxRows, token).ConfigureAwait(false);
            return state with
            {
                Query = state.Query with { Sql = execution.Sql, LastExecution = execution },
                Status = $"Query returned {execution.Result.Rows.Count} row(s){(execution.Result.Truncated ? " (truncated)" : string.Empty)}.",
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    public DataWorkbookAiContext CreateAiContext()
    {
        var state = Current;
        var selection = state.Selection;
        var query = state.Query;
        var actions = new List<DataWorkbookActionCapability>(ReadActions);
        if (!state.Workbook.Workbook.ReadOnly)
            actions.AddRange(WriteActions);
        if (query.IsOpen)
            actions.Add(new DataWorkbookActionCapability(DataRunReadOnlyQueryAction.Id, "Run read-only query", false, "database.query.read"));

        DataWorkbookQueryContext? queryContext = query.IsOpen
            ? new DataWorkbookQueryContext(
                true,
                query.DatabaseName,
                query.Sql,
                query.PublishedTables.Select(table => table.Name).ToArray(),
                query.LastExecution?.Result.Columns.ToArray() ?? [],
                query.LastExecution?.Result.Rows.Count ?? 0,
                query.LastExecution?.Result.Truncated ?? false)
            : null;

        return new DataWorkbookAiContext(
            Path.GetFileName(state.Workbook.Workbook.Path),
            state.Workbook.Workbook.ReadOnly,
            state.Workbook.ActiveSheet.Name,
            state.Workbook.Sheets.Select(sheet => sheet.Name).ToArray(),
            new DataWorkbookSelectionContext(
                selection.Row,
                selection.Column,
                selection.Address,
                state.Workbook.Grid.Values[selection.Row][selection.Column],
                state.FormulaBarText),
            CopyRange(state.Workbook.Grid),
            queryContext,
            actions);
    }

    public async Task<DataWorkbookActionResult> ExecuteAiActionAsync(
        DataWorkbookActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InvocationId);
        ArgumentNullException.ThrowIfNull(request.Action);

        if (_actionAuthorizer is null)
        {
            return new DataWorkbookActionResult(
                request.InvocationId,
                request.Action.ActionId,
                DataWorkbookActionOutcome.Denied,
                "The host has not connected the central action authorizer.",
                CreateAiContext());
        }

        var authorization = await _actionAuthorizer.AuthorizeAsync(request, cancellationToken).ConfigureAwait(false);
        if (!authorization.Allowed)
        {
            return new DataWorkbookActionResult(
                request.InvocationId,
                request.Action.ActionId,
                DataWorkbookActionOutcome.Denied,
                authorization.Reason,
                CreateAiContext());
        }

        try
        {
            switch (request.Action)
            {
                case DataSelectCellAction select:
                    SelectCell(select.Row, select.Column);
                    break;
                case DataEditCellAction edit:
                    SelectCell(edit.Row, edit.Column);
                    await CommitFormulaBarAsync(edit.Value, cancellationToken).ConfigureAwait(false);
                    break;
                case DataSetFormulaAction formula:
                    if (!formula.Formula.StartsWith('='))
                        throw new ArgumentException("A typed formula action must start with '='.", nameof(request));
                    SelectCell(formula.Row, formula.Column);
                    await CommitFormulaBarAsync(formula.Formula, cancellationToken).ConfigureAwait(false);
                    break;
                case DataSortVisibleRangeAction sort:
                    await SortVisibleAsync(sort.KeyColumn, sort.Ascending, cancellationToken).ConfigureAwait(false);
                    break;
                case DataFilterVisibleRangeAction filter:
                    await FilterVisibleAsync(filter.KeyColumn, filter.Value, cancellationToken).ConfigureAwait(false);
                    break;
                case DataClearVisibleFilterAction:
                    await ClearFilterAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case DataRunReadOnlyQueryAction query:
                    await ExecuteReadOnlyQueryAsync(query.Sql, query.MaxRows, cancellationToken).ConfigureAwait(false);
                    break;
                case DataSaveWorkbookAsAction save:
                    await SaveAsAsync(save.DestinationPath, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported Data workbook action '{request.Action.ActionId}'.");
            }

            return new DataWorkbookActionResult(
                request.InvocationId,
                request.Action.ActionId,
                DataWorkbookActionOutcome.Succeeded,
                Current.Status,
                CreateAiContext());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new DataWorkbookActionResult(
                request.InvocationId,
                request.Action.ActionId,
                DataWorkbookActionOutcome.Failed,
                exception.Message,
                CreateAiContext());
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _query.DisposeAsync().ConfigureAwait(false);
        await _grid.DisposeAsync().ConfigureAwait(false);
        _operationGate.Dispose();
    }

    private async Task<DataCuiWorkspaceSnapshot> MutateGridAsync(
        Func<DataCuiWorkspaceSnapshot, CancellationToken, Task<DataCuiWorkspaceSnapshot>> operation,
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var current = Current;
            var updated = await operation(current, cancellationToken).ConfigureAwait(false);
            return SetState(updated);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private DataCuiWorkspaceSnapshot SetState(DataCuiWorkspaceSnapshot state)
    {
        lock (_stateLock)
            return _state = state;
    }

    private static DataRangeSnapshot CopyRange(DataRangeSnapshot source) => new(
        source.Sheet,
        source.StartRow,
        source.StartColumn,
        source.Values.Select(row => (IReadOnlyList<string>)row.ToArray()).ToArray(),
        source.RowVisibility?.ToArray());

    private static void EnsureQueryOpen(DataCuiWorkspaceSnapshot state)
    {
        if (!state.Query.IsOpen)
            throw new InvalidOperationException("Open a DuckDB database before using the query panel.");
    }

    private static DataCuiSelection Selection(int row, int column) =>
        new(row, column, $"{ColumnName(column)}{row + 1}");

    private static string ColumnName(int column)
    {
        ValidateColumn(column);
        var value = column + 1;
        var result = string.Empty;
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }
        return result;
    }

    private static void ValidateCell(int row, int column)
    {
        if (row < 0 || row >= DataGridSession.VisibleRows)
            throw new ArgumentOutOfRangeException(nameof(row));
        ValidateColumn(column);
    }

    private static void ValidateColumn(int column)
    {
        if (column < 0 || column >= DataGridSession.VisibleColumns)
            throw new ArgumentOutOfRangeException(nameof(column));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
