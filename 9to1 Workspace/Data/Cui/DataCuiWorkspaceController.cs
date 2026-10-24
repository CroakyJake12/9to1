using System.Globalization;
using System.Text.Json.Serialization;

namespace HavenOS.Apps.Data.Cui;

public sealed record DataCuiSelection(int Row, int Column, string Address);

public sealed record DataCuiSortState(int KeyColumn, bool Ascending);

public sealed record DataCuiFilterState(bool Enabled, int KeyColumn, string Value);

public sealed record DataCuiQueryError(string Code, string Message, DateTimeOffset OccurredAtUtc);

public sealed class DataCuiQueryException : Exception
{
    public DataCuiQueryException(DataCuiQueryError diagnostic, Exception originalException,
        Exception? errorStatePublicationException = null)
        : base(errorStatePublicationException is null
            ? diagnostic.Message
            : $"{diagnostic.Message} The visible error state could not be updated.")
    {
        Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        OriginalException = originalException ?? throw new ArgumentNullException(nameof(originalException));
        ErrorStatePublicationException = errorStatePublicationException;
    }

    [JsonIgnore]
    public DataCuiQueryError Diagnostic { get; }
    public string Code => Diagnostic.Code;

    [JsonIgnore]
    public Exception OriginalException { get; }

    [JsonIgnore]
    public Exception? ErrorStatePublicationException { get; }
}

public sealed class DataCuiStaleActionException : InvalidOperationException
{
    public DataCuiStaleActionException(string message, string code = "StaleQuerySession") : base(message) => Code = code;
    public string Code { get; }
}

public sealed record DataCuiQueryPanelState(
    bool IsOpen,
    string DatabaseName,
    string Sql,
    IReadOnlyList<DataPublishedTable> PublishedTables,
    DataQueryExecution? LastExecution,
    string PublishTableName = "",
    string DatabaseTarget = "",
    string DatabasePathDraft = "",
    IReadOnlyList<DataQueryExecution>? RecentQueries = null,
    DataCuiQueryError? LastError = null,
    bool IsExecuting = false)
{
    public IReadOnlyList<string> ResultColumns => LastExecution?.Result.Columns ?? Array.Empty<string>();
    public IReadOnlyList<IReadOnlyList<string>> ResultRows => LastExecution?.Result.Rows ?? Array.Empty<IReadOnlyList<string>>();
    public bool ResultTruncated => LastExecution?.Result.Truncated ?? false;
    public bool CanCancel => IsOpen && IsExecuting;
    public string ResultStatus => IsExecuting
        ? "Query is running. Previous successful results remain available."
        : LastExecution is null
        ? "No query results."
        : LastExecution.Result.Truncated
            ? $"Showing {LastExecution.Result.Rows.Count} row(s); results are truncated."
            : $"Query returned {LastExecution.Result.Rows.Count} row(s).";
    public IReadOnlyList<string> HistoryColumns { get; } = ["Executed", "Target", "SQL", "Rows", "Truncated"];
    public IReadOnlyList<IReadOnlyList<string>> HistoryRows => (RecentQueries ?? Array.Empty<DataQueryExecution>())
        .Select(execution => (IReadOnlyList<string>)new[]
        {
            execution.ExecutedAtUtc.ToString("u", CultureInfo.InvariantCulture),
            DatabaseTarget,
            execution.Sql,
            execution.Result.Rows.Count.ToString(CultureInfo.InvariantCulture),
            execution.Result.Truncated ? "Yes" : "No",
        }).ToArray();
    public string? ErrorMessage => LastError is null ? null : $"{LastError.Code}: {LastError.Message}";
}

public sealed record DataCuiWorkspaceSnapshot(
    DataGridSessionSnapshot Workbook,
    DataCuiSelection Selection,
    string FormulaBarText,
    DataCuiSortState Sort,
    DataCuiFilterState Filter,
    DataCuiQueryPanelState Query,
    bool IsDirty,
    string Status)
{
    // Explicit CUI binding projections keep the language surface on the same
    // canonical workbook snapshot rather than maintaining a second grid model.
    public IReadOnlyList<DataSheetSummary> Sheets => Workbook.Sheets;
    public DataSheetSummary ActiveSheet => Workbook.ActiveSheet;
    public DataRangeSnapshot Grid => Workbook.Grid;
    public IReadOnlyList<string> Columns { get; } = ["A", "B", "C", "D", "E", "F", "G", "H"];
}

/// <summary>
/// CUI presentation controller over the existing Calc/DuckDB sessions. It owns no worker or
/// renderer types and updates visible state only after an engine operation has completed.
/// </summary>
public sealed class DataCuiWorkspaceController : IAsyncDisposable
{
    private sealed class ActiveQueryCancellation(
        Guid executionId,
        long generation,
        string target,
        CancellationTokenSource source)
    {
        private readonly object _sync = new();
        private bool _completed;

        public Guid ExecutionId { get; } = executionId;
        public long Generation { get; } = generation;
        public string Target { get; } = target;
        public CancellationToken Token { get; } = source.Token;

        public bool Cancel()
        {
            lock (_sync)
            {
                if (_completed) return false;
                source.Cancel();
                return true;
            }
        }

        public void Complete()
        {
            lock (_sync)
            {
                if (_completed) return;
                _completed = true;
                source.Dispose();
            }
        }
    }

    private sealed record DataWorkbookActionOrigin(
        long QuerySessionGeneration,
        DataCuiWorkspaceSnapshot Snapshot,
        DataRangeRequest VisibleRange,
        DataRangeSnapshot VisibleRangeSnapshot);

    private static readonly DataWorkbookActionCapability[] ReadActions =
    [
        new(DataSelectCellAction.Id, "Select cell", false, "workbook.read"),
        new(DataSelectSheetAction.Id, "Select worksheet", false, "workbook.read"),
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
    private long _querySessionGeneration;
    private ActiveQueryCancellation? _activeQueryCancellation;
    private Task? _disposeTask;
    private volatile bool _closing;
    private volatile bool _disposed;

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
            if (state.Workbook.Grid.RowVisibility is { } visibility && !visibility[row])
                throw new InvalidOperationException($"Cell {Selection(row, column).Address} is hidden by the active filter and cannot be selected.");
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
        var original = Current;
        return await CommitCellAsync(original.Selection.Row, original.Selection.Column, text,
            original.Workbook, cancellationToken).ConfigureAwait(false);
    }

    private Task<DataCuiWorkspaceSnapshot> CommitCellAsync(int row, int column, string text,
        DataGridSessionSnapshot originalWorkbook, CancellationToken cancellationToken)
    {
        ValidateCell(row, column);
        ArgumentNullException.ThrowIfNull(text);
        return MutateGridAsync((state, token) =>
        {
            if (state.Workbook.Workbook.Id != originalWorkbook.Workbook.Id ||
                state.Workbook.ActiveSheet.Name != originalWorkbook.ActiveSheet.Name)
                throw new InvalidOperationException("The original workbook sheet changed before this cell edit. Review and submit a new edit.");
            return CommitCellCoreAsync(state, row, column, text, token);
        }, cancellationToken);
    }

    private async Task<DataCuiWorkspaceSnapshot> CommitCellCoreAsync(DataCuiWorkspaceSnapshot state,
        int row, int column, string text, CancellationToken token)
    {
        var selection = Selection(row, column);
        var formula = text.StartsWith('=') ? text : null;
        var value = formula is null ? text : string.Empty;
        var workbook = await _grid.EditCellAsync(row, column, value, formula, token).ConfigureAwait(false);
        return state with
        {
            Workbook = workbook,
            Selection = selection,
            FormulaBarText = text,
            IsDirty = true,
            Status = formula is null
                ? $"Updated {selection.Address}."
                : $"Recalculated {selection.Address} from {text}.",
        };
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
                FormulaBarText = workbook.Grid.Values[state.Selection.Row][state.Selection.Column],
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
            var visibility = workbook.Grid.RowVisibility ??
                throw new InvalidDataException("The filtered workbook snapshot omitted row-visibility metadata.");
            if (visibility.Count != DataGridSession.VisibleRows || !visibility[0])
                throw new InvalidDataException("The filtered workbook snapshot has malformed visibility or hides its header row.");
            // A filtered-out cell cannot remain active: its formula bar and
            // contextual actions would otherwise point at an invisible row.
            var selection = state.Selection;
            if (!visibility[selection.Row])
            {
                var visibleRow = Enumerable.Range(1, visibility.Count - 1)
                    .FirstOrDefault(row => visibility[row], -1);
                if (visibleRow < 0)
                    visibleRow = 0; // DataGridSession guarantees the header remains visible.
                selection = Selection(visibleRow, selection.Column);
            }
            return state with
            {
                Workbook = workbook,
                Selection = selection,
                FormulaBarText = workbook.Grid.Values[selection.Row][selection.Column],
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
                FormulaBarText = workbook.Grid.Values[state.Selection.Row][state.Selection.Column],
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

    public Task<DataCuiWorkspaceSnapshot> OpenQueryPanelAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        var origin = CaptureActionOrigin();
        if (origin.Snapshot.Query.IsOpen)
            throw new InvalidOperationException("Close the active query connection before opening another database.");
        return OpenQueryPanelCoreAsync(databasePath, cancellationToken, origin);
    }

    private async Task<DataCuiWorkspaceSnapshot> OpenQueryPanelCoreAsync(
        string databasePath,
        CancellationToken cancellationToken,
        DataWorkbookActionOrigin? origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        return await MutateGridAsync(async (state, token) =>
        {
            var query = await _query.OpenAsync(databasePath, token).ConfigureAwait(false);
            return state with
            {
                Query = new DataCuiQueryPanelState(
                    true, Path.GetFileName(query.DatabasePath), string.Empty,
                    query.PublishedTables, null, state.Query.PublishTableName, query.DatabasePath,
                    query.DatabasePath, query.RecentQueries),
                Status = $"Opened query database {Path.GetFileName(databasePath)}.",
            };
        }, cancellationToken,
            validateCurrent: origin is null ? null : state => ValidateActionOrigin(state, origin, requireOpen: false),
            advanceQuerySessionGeneration: true).ConfigureAwait(false);
    }

    public Task<DataCuiWorkspaceSnapshot> CloseQueryPanelAsync(CancellationToken cancellationToken = default)
    {
        var origin = CaptureActionOrigin();
        return origin.Snapshot.Query.IsOpen
            ? CloseQueryPanelCoreAsync(cancellationToken, origin)
            : Task.FromResult(origin.Snapshot);
    }

    private async Task<DataCuiWorkspaceSnapshot> CloseQueryPanelCoreAsync(
        CancellationToken cancellationToken,
        DataWorkbookActionOrigin? origin)
    {
        return await MutateGridAsync(async (state, token) =>
        {
            await _query.CloseAsync(token).ConfigureAwait(false);
            return state with
            {
                Query = new DataCuiQueryPanelState(
                    false, string.Empty, string.Empty, [], null,
                    state.Query.PublishTableName, string.Empty, state.Query.DatabasePathDraft),
                Status = "Closed the query database.",
            };
        }, cancellationToken,
            validateCurrent: origin is null ? null : state => ValidateActionOrigin(state, origin, requireOpen: true),
            advanceQuerySessionGeneration: true).ConfigureAwait(false);
    }

    public Task<DataCuiWorkspaceSnapshot> PublishVisibleRangeAsync(
        string tableName,
        bool firstRowIsHeaders = true,
        CancellationToken cancellationToken = default)
        => PublishVisibleRangeCoreAsync(tableName, firstRowIsHeaders, cancellationToken, CaptureActionOrigin());

    private async Task<DataCuiWorkspaceSnapshot> PublishVisibleRangeCoreAsync(
        string tableName,
        bool firstRowIsHeaders,
        CancellationToken cancellationToken,
        DataWorkbookActionOrigin? origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        return await MutateGridAsync(async (state, token) =>
        {
            EnsureQueryOpen(state);
            var workbookId = origin?.Snapshot.Workbook.Workbook.Id ?? state.Workbook.Workbook.Id;
            var sourceRange = origin?.VisibleRange ?? new DataRangeRequest(
                state.Workbook.ActiveSheet.Name, 0, 0, DataGridSession.VisibleRows, DataGridSession.VisibleColumns);
            _ = await _query.PublishRangeAsync(
                workbookId,
                sourceRange,
                tableName,
                firstRowIsHeaders,
                token).ConfigureAwait(false);
            var query = _query.Snapshot();
            return state with
            {
                Query = state.Query with { PublishedTables = query.PublishedTables },
                Status = $"Published the visible range as {tableName}.",
            };
        }, cancellationToken,
            validateCurrent: origin is null ? null : state => ValidateActionOrigin(
                state, origin, requireOpen: true, requireSameVisibleRange: true)).ConfigureAwait(false);
    }

    public Task<DataCuiWorkspaceSnapshot> ExecuteReadOnlyQueryAsync(
        string sql,
        int maxRows = 200,
        CancellationToken cancellationToken = default)
        => ExecuteReadOnlyQueryWithErrorStateAsync(sql, maxRows, cancellationToken, CaptureActionOrigin());

    private async Task<DataCuiWorkspaceSnapshot> ExecuteReadOnlyQueryWithErrorStateAsync(
        string sql,
        int maxRows,
        CancellationToken cancellationToken,
        DataWorkbookActionOrigin origin)
    {
        try
        {
            return await ExecuteReadOnlyQueryCoreAsync(sql, maxRows, cancellationToken, origin).ConfigureAwait(false);
        }
        catch (DataCuiQueryException)
        {
            throw;
        }
        catch (OperationCanceledException error)
        {
            var diagnostic = new DataCuiQueryError(
                "SqlCancelled",
                "The query was cancelled. Previous successful results are retained.",
                DateTimeOffset.UtcNow);
            var stateFailure = await PublishQueryErrorIfCurrentAsync(origin, diagnostic).ConfigureAwait(false);
            throw new DataCuiQueryException(diagnostic, error, stateFailure);
        }
        catch (Exception exception)
        {
            var diagnostic = CreateSafeQueryDiagnostic(sql, maxRows, exception, origin.Snapshot.Query.IsOpen);
            var stateFailure = await PublishQueryErrorIfCurrentAsync(origin, diagnostic).ConfigureAwait(false);
            throw new DataCuiQueryException(diagnostic, exception, stateFailure);
        }
    }

    private async Task<DataCuiWorkspaceSnapshot> ExecuteReadOnlyQueryCoreAsync(
        string sql,
        int maxRows,
        CancellationToken cancellationToken,
        DataWorkbookActionOrigin? origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        return await MutateGridAsync(async (state, token) =>
        {
            EnsureQueryOpen(state);
            var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var active = new ActiveQueryCancellation(
                Guid.NewGuid(), origin?.QuerySessionGeneration ?? _querySessionGeneration,
                state.Query.DatabaseTarget, linkedCancellation);
            lock (_stateLock)
            {
                if (_activeQueryCancellation is not null)
                {
                    linkedCancellation.Dispose();
                    throw new InvalidOperationException("A read-only SQL query is already running in this connection.");
                }
                _activeQueryCancellation = active;
                if (_state is { } current && current.Query.IsOpen &&
                    current.Query.DatabaseTarget == active.Target && _querySessionGeneration == active.Generation)
                    _state = current with { Query = current.Query with { IsExecuting = true } };
            }

            DataQueryExecution execution;
            try { execution = await _query.ExecuteAsync(sql, maxRows, active.Token).ConfigureAwait(false); }
            finally
            {
                lock (_stateLock)
                {
                    if (ReferenceEquals(_activeQueryCancellation, active))
                        _activeQueryCancellation = null;
                    if (_state is { } current && current.Query.DatabaseTarget == active.Target &&
                        _querySessionGeneration == active.Generation)
                        _state = current with { Query = current.Query with { IsExecuting = false } };
                }
                active.Complete();
            }
            var query = _query.Snapshot();
            return state with
            {
                Query = state.Query with
                {
                    Sql = execution.Sql,
                    LastExecution = execution,
                    RecentQueries = query.RecentQueries,
                    LastError = null,
                },
                Status = $"Query returned {execution.Result.Rows.Count} row(s){(execution.Result.Truncated ? " (truncated)" : string.Empty)}.",
            };
        }, cancellationToken,
            validateCurrent: origin is null ? null : state => ValidateActionOrigin(state, origin, requireOpen: true)).ConfigureAwait(false);
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
        {
            actions.Add(new DataWorkbookActionCapability(DataRunReadOnlyQueryAction.Id, "Run read-only query", false, "database.query.read"));
            if (query.CanCancel)
                actions.Add(new DataWorkbookActionCapability(DataCancelReadOnlyQueryAction.Id, "Cancel read-only query", false, "database.query.read")
                { EffectOverride = DataWorkbookActionEffect.ConnectionLifecycle });
            actions.Add(new DataWorkbookActionCapability(DataCloseQueryPanelAction.Id, "Close query database", false, "database.query.read")
            { EffectOverride = DataWorkbookActionEffect.ConnectionLifecycle });
            actions.Add(new DataWorkbookActionCapability(DataPublishVisibleRangeAction.Id, "Publish visible range", false, "database.publish")
            { EffectOverride = DataWorkbookActionEffect.DatabaseMutation });
        }
        else
        {
            actions.Add(new DataWorkbookActionCapability(DataOpenQueryPanelAction.Id, "Open query database", false, "database.query.read")
            { EffectOverride = DataWorkbookActionEffect.ConnectionLifecycle });
        }

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
        ThrowIfDisposed();
        var origin = CaptureActionOrigin();
        var originalWorkbook = origin.Snapshot.Workbook;
        var authorizedAction = request.Action switch
        {
            DataOpenQueryPanelAction open => open,
            DataCloseQueryPanelAction close => close with
            {
                DatabaseTarget = origin.Snapshot.Query.DatabaseTarget,
                QuerySessionGeneration = origin.QuerySessionGeneration,
            },
            DataRunReadOnlyQueryAction run => run with
            {
                DatabaseTarget = origin.Snapshot.Query.DatabaseTarget,
                QuerySessionGeneration = origin.QuerySessionGeneration,
            },
            DataCancelReadOnlyQueryAction cancel => cancel with
            {
                DatabaseTarget = origin.Snapshot.Query.DatabaseTarget,
                QuerySessionGeneration = origin.QuerySessionGeneration,
                ExecutionId = CaptureActiveQueryExecutionId(origin),
            },
            DataPublishVisibleRangeAction publish => publish with
            {
                SourceWorkbookId = origin.Snapshot.Workbook.Workbook.Id,
                SourceSheet = origin.Snapshot.Workbook.ActiveSheet.Name,
                SourceRange = origin.VisibleRange,
                DatabaseTarget = origin.Snapshot.Query.DatabaseTarget,
                QuerySessionGeneration = origin.QuerySessionGeneration,
                SourceContentFingerprint = origin.VisibleRangeSnapshot.ContentFingerprint(),
            },
            _ => request.Action,
        };
        var authorizedRequest = request with { Action = authorizedAction };

        if (_actionAuthorizer is null)
        {
            return new DataWorkbookActionResult(
                request.InvocationId,
                request.Action.ActionId,
                DataWorkbookActionOutcome.Denied,
                "The host has not connected the central action authorizer.",
                CreateAiContext());
        }

        var authorization = await _actionAuthorizer.AuthorizeAsync(authorizedRequest, cancellationToken).ConfigureAwait(false);
        if (!authorization.Allowed)
        {
            return new DataWorkbookActionResult(
                request.InvocationId,
                request.Action.ActionId,
                DataWorkbookActionOutcome.Denied,
                authorization.Reason,
                CreateAiContext());
        }

        if (request.Action.RequiresPerActionApproval &&
            !string.Equals(authorization.ApprovedActionFingerprint, authorizedRequest.ActionFingerprint(), StringComparison.Ordinal))
        {
            return new DataWorkbookActionResult(
                request.InvocationId,
                request.Action.ActionId,
                DataWorkbookActionOutcome.Denied,
                "The authorizer did not approve this exact mutating action payload.",
                CreateAiContext());
        }

        try
        {
            var actionMessage = (string?)null;
            switch (authorizedRequest.Action)
            {
                case DataSelectCellAction select:
                    SelectCell(select.Row, select.Column);
                    break;
                case DataSelectSheetAction selectSheet:
                    await SelectSheetAsync(selectSheet.SheetIndex, cancellationToken).ConfigureAwait(false);
                    break;
                case DataOpenQueryPanelAction openQuery:
                    await OpenQueryPanelCoreAsync(openQuery.DatabasePath, cancellationToken, origin).ConfigureAwait(false);
                    break;
                case DataCloseQueryPanelAction:
                    await CloseQueryPanelCoreAsync(cancellationToken, origin).ConfigureAwait(false);
                    break;
                case DataPublishVisibleRangeAction publish:
                    await PublishVisibleRangeCoreAsync(publish.TableName, firstRowIsHeaders: true,
                        cancellationToken: cancellationToken, origin: origin).ConfigureAwait(false);
                    break;
                case DataEditCellAction edit:
                    await CommitCellAsync(edit.Row, edit.Column, edit.Value, originalWorkbook, cancellationToken).ConfigureAwait(false);
                    break;
                case DataSetFormulaAction formula:
                    if (!formula.Formula.StartsWith('='))
                        throw new ArgumentException("A typed formula action must start with '='.", nameof(request));
                    await CommitCellAsync(formula.Row, formula.Column, formula.Formula, originalWorkbook, cancellationToken).ConfigureAwait(false);
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
                    await ExecuteReadOnlyQueryWithErrorStateAsync(query.Sql, query.MaxRows, cancellationToken, origin).ConfigureAwait(false);
                    break;
                case DataCancelReadOnlyQueryAction cancel:
                    CancelReadOnlyQuery(cancel.DatabaseTarget, cancel.QuerySessionGeneration, cancel.ExecutionId);
                    actionMessage = "Cancellation requested for the active read-only query.";
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
                actionMessage ?? Current.Status,
                CreateAiContext());
        }
        catch (DataCuiQueryException exception) when (authorizedRequest.Action is DataRunReadOnlyQueryAction)
        {
            return new DataWorkbookActionResult(
                request.InvocationId,
                request.Action.ActionId,
                exception.Code == "SqlCancelled" ? DataWorkbookActionOutcome.Cancelled : DataWorkbookActionOutcome.Failed,
                exception.Diagnostic.Message,
                CreateAiContext(),
                exception.Code)
            {
                DiagnosticException = exception.OriginalException,
                ErrorStatePublicationException = exception.ErrorStatePublicationException,
            };
        }
        catch (DataCuiStaleActionException exception) when (authorizedRequest.Action is DataCancelReadOnlyQueryAction)
        {
            return new DataWorkbookActionResult(
                request.InvocationId,
                request.Action.ActionId,
                DataWorkbookActionOutcome.Failed,
                exception.Message,
                CreateAiContext(),
                exception.Code);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (authorizedRequest.Action is DataRunReadOnlyQueryAction failedQuery)
            {
                var diagnostic = CreateSafeQueryDiagnostic(
                    failedQuery.Sql, failedQuery.MaxRows, exception, origin.Snapshot.Query.IsOpen);
                var stateFailure = await PublishQueryErrorIfCurrentAsync(origin, diagnostic).ConfigureAwait(false);
                var message = stateFailure is null
                    ? diagnostic.Message
                    : $"{diagnostic.Message} The visible error state could not be updated.";
                return new DataWorkbookActionResult(
                    request.InvocationId,
                    request.Action.ActionId,
                    DataWorkbookActionOutcome.Failed,
                    message,
                    CreateAiContext(),
                    diagnostic.Code)
                {
                    DiagnosticException = exception,
                    ErrorStatePublicationException = stateFailure,
                };
            }
            return new DataWorkbookActionResult(
                request.InvocationId,
                request.Action.ActionId,
                DataWorkbookActionOutcome.Failed,
                exception.Message,
                CreateAiContext());
        }
    }

    private Guid? CaptureActiveQueryExecutionId(DataWorkbookActionOrigin origin)
    {
        lock (_stateLock)
        {
            if (_querySessionGeneration != origin.QuerySessionGeneration ||
                !string.Equals(_state?.Query.DatabaseTarget, origin.Snapshot.Query.DatabaseTarget, StringComparison.Ordinal) ||
                _activeQueryCancellation is not { } active ||
                active.Generation != origin.QuerySessionGeneration ||
                !string.Equals(active.Target, origin.Snapshot.Query.DatabaseTarget, StringComparison.Ordinal))
                return null;
            return active.ExecutionId;
        }
    }

    private void CancelReadOnlyQuery(string? target, long? generation, Guid? executionId)
    {
        ActiveQueryCancellation active;
        lock (_stateLock)
        {
            ThrowIfDisposed();
            var state = _state ?? throw new InvalidOperationException("No workbook is open in the Data CUI workspace.");
            if (generation != _querySessionGeneration || !state.Query.IsOpen ||
                !string.Equals(target, state.Query.DatabaseTarget, StringComparison.Ordinal))
                throw new DataCuiStaleActionException(
                    "The query cancellation action is stale because its original query session changed.",
                    "StaleQuerySession");
            if (executionId is null)
                throw new DataCuiStaleActionException(
                    "There was no active read-only query when cancellation was requested.",
                    "QueryExecutionNotRunning");
            if (_activeQueryCancellation is not { } current)
                throw new DataCuiStaleActionException(
                    "The original read-only query has already completed; no query was cancelled.",
                    "QueryExecutionCompleted");
            if (current.ExecutionId != executionId || current.Generation != generation ||
                !string.Equals(current.Target, target, StringComparison.Ordinal))
                throw new DataCuiStaleActionException(
                    "The original read-only query was replaced; no other query was cancelled.",
                    "StaleQueryExecution");
            active = current;
        }

        if (!active.Cancel())
            throw new DataCuiStaleActionException(
                "The original read-only query has already completed; no query was cancelled.",
                "QueryExecutionCompleted");
    }

    public ValueTask DisposeAsync()
    {
        lock (_stateLock)
        {
            if (_disposeTask is null)
            {
                _closing = true;
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = DisposeAfterPublicationAsync(start.Task);
                start.SetResult();
            }
            return new(_disposeTask);
        }
    }

    private async Task DisposeAfterPublicationAsync(Task start)
    {
        await start.ConfigureAwait(false);
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            var failures = new List<Exception>();
            try { await _query.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { failures.Add(error); }
            try { await _grid.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0)
                throw new AggregateException("Data retained its session cleanup failures.", failures);
        }
        finally
        {
            // Existing queued callers still acquire and release this gate before refusing.
            // No native wait handle is created; let the managed gate retire with this owner.
            _operationGate.Release();
        }
    }

    private async Task<DataCuiWorkspaceSnapshot> MutateGridAsync(
        Func<DataCuiWorkspaceSnapshot, CancellationToken, Task<DataCuiWorkspaceSnapshot>> operation,
        CancellationToken cancellationToken,
        Action<DataCuiWorkspaceSnapshot>? validateCurrent = null,
        bool advanceQuerySessionGeneration = false)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var current = Current;
            validateCurrent?.Invoke(current);
            var updated = await operation(current, cancellationToken).ConfigureAwait(false);
            return SetState(updated, advanceQuerySessionGeneration);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private DataCuiWorkspaceSnapshot SetState(DataCuiWorkspaceSnapshot state) =>
        SetState(state, advanceQuerySessionGeneration: false);

    private DataCuiWorkspaceSnapshot SetState(DataCuiWorkspaceSnapshot state, bool advanceQuerySessionGeneration)
    {
        lock (_stateLock)
        {
            if (advanceQuerySessionGeneration && _state is { } previous &&
                previous.Query.IsOpen != state.Query.IsOpen)
                _querySessionGeneration = checked(_querySessionGeneration + 1);
            return _state = state;
        }
    }

    private DataWorkbookActionOrigin CaptureActionOrigin()
    {
        lock (_stateLock)
        {
            var snapshot = _state ?? throw new InvalidOperationException("No workbook is open in the Data CUI workspace.");
            var grid = snapshot.Workbook.Grid;
            return new DataWorkbookActionOrigin(
                _querySessionGeneration,
                snapshot,
                new DataRangeRequest(grid.Sheet, grid.StartRow, grid.StartColumn, grid.Values.Count,
                    grid.Values.Count == 0 ? 0 : grid.Values[0].Count),
                CopyRange(grid));
        }
    }

    private void ValidateActionOrigin(
        DataCuiWorkspaceSnapshot current,
        DataWorkbookActionOrigin origin,
        bool requireOpen,
        bool requireSameVisibleRange = false)
    {
        if (_querySessionGeneration != origin.QuerySessionGeneration ||
            current.Query.IsOpen != origin.Snapshot.Query.IsOpen ||
            current.Query.IsOpen != requireOpen ||
            !string.Equals(current.Query.DatabaseTarget, origin.Snapshot.Query.DatabaseTarget, StringComparison.Ordinal))
            throw new DataCuiStaleActionException("The authorized SQL action is stale because its original query session changed.");

        if (!requireSameVisibleRange)
            return;

        var original = origin.Snapshot.Workbook;
        var active = current.Workbook;
        var grid = active.Grid;
        var range = origin.VisibleRange;
        if (!string.Equals(active.Workbook.Id, original.Workbook.Id, StringComparison.Ordinal) ||
            !string.Equals(active.ActiveSheet.Name, original.ActiveSheet.Name, StringComparison.Ordinal) ||
            !string.Equals(grid.Sheet, range.Sheet, StringComparison.Ordinal) ||
            grid.StartRow != range.StartRow || grid.StartColumn != range.StartColumn ||
            grid.Values.Count != range.RowCount ||
            (grid.Values.Count != 0 && grid.Values[0].Count != range.ColumnCount) ||
            !RangesEqual(grid, origin.VisibleRangeSnapshot))
            throw new DataCuiStaleActionException("The authorized database publication is stale because its workbook, sheet, or visible range changed.");
    }

    private static DataCuiQueryError CreateSafeQueryDiagnostic(
        string sql,
        int maxRows,
        Exception exception,
        bool hadOpenTarget)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return new DataCuiQueryError("SqlInputInvalid", "Enter a non-empty SQL statement.", DateTimeOffset.UtcNow);
        if (maxRows is < 1 or > 1000)
            return new DataCuiQueryError("SqlInputInvalid", "Choose a result limit from 1 to 1000 rows.", DateTimeOffset.UtcNow);
        if (exception is DataCuiStaleActionException)
        {
            if (!hadOpenTarget)
                return new DataCuiQueryError("ConnectionUnavailable", "Open a DuckDB target before running SQL.", DateTimeOffset.UtcNow);
            return new DataCuiQueryError("StaleQuerySession", "The query connection changed before execution. Select the intended target and retry.", DateTimeOffset.UtcNow);
        }

        // No provider exception-message contract has been reviewed as safe for display.
        // Keep its original exception task-private and expose a stable, non-sensitive fallback.
        return new DataCuiQueryError(
            "SqlExecutionFailed",
            "The query could not be completed. Check the SQL, active target, and permissions.",
            DateTimeOffset.UtcNow);
    }

    private async Task<Exception?> PublishQueryErrorIfCurrentAsync(
        DataWorkbookActionOrigin origin,
        DataCuiQueryError diagnostic)
    {
        try
        {
            await MutateGridAsync((state, _) => Task.FromResult(state with
            {
                Query = state.Query with { LastError = diagnostic },
            }), CancellationToken.None,
                validateCurrent: state => ValidateActionOrigin(state, origin, requireOpen: true)).ConfigureAwait(false);
            return null;
        }
        catch (DataCuiStaleActionException)
        {
            // Intentional stale refusal: never attach an old failure to a new session.
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch (Exception errorStateFailure)
        {
            // Surface the independent update failure to the originating task alongside
            // its primary diagnostic; never replace the original exception silently.
            return errorStateFailure;
        }
    }

    private static bool RangesEqual(DataRangeSnapshot left, DataRangeSnapshot right) =>
        string.Equals(left.Sheet, right.Sheet, StringComparison.Ordinal) &&
        left.StartRow == right.StartRow && left.StartColumn == right.StartColumn &&
        left.Values.Count == right.Values.Count &&
        left.Values.Zip(right.Values).All(pair => pair.First.SequenceEqual(pair.Second, StringComparer.Ordinal)) &&
        ((left.RowVisibility is null && right.RowVisibility is null) ||
         (left.RowVisibility is not null && right.RowVisibility is not null && left.RowVisibility.SequenceEqual(right.RowVisibility)));

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

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_closing || _disposed, this);
}
