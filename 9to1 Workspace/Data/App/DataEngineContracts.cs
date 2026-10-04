namespace HavenOS.Apps.Data;

public sealed record DataCellAddress(string Sheet, int Row, int Column);
public sealed record DataCellSnapshot(DataCellAddress Address, string Value, string Formula = "");
public sealed record DataRangeRequest(string Sheet, int StartRow, int StartColumn, int RowCount, int ColumnCount);
public sealed record DataRangeSnapshot(
    string Sheet,
    int StartRow,
    int StartColumn,
    IReadOnlyList<IReadOnlyList<string>> Values,
    IReadOnlyList<bool>? RowVisibility = null);
public sealed record DataSheetSummary(string Name, int Index);
public sealed record DataWorkbookHandle(string Id, string Path, bool ReadOnly);
public sealed record DataNamedRangeSummary(string Name, DataRangeRequest Range);
public sealed record DataListValidationState(DataRangeRequest Range, bool Enabled, IReadOnlyList<string> Values, bool AllowBlank);
public sealed record DataSortResult(DataRangeRequest Range, int KeyColumnOffset, bool Ascending, bool ContainsHeader, DataRangeSnapshot Snapshot);
public sealed record DataTableSnapshot(string Name, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);
public sealed record DataPublishedTable(string Name, IReadOnlyList<string> Columns, int RowCount, DataRangeRequest SourceRange);
public sealed record DataQueryResult(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, bool Truncated);
public sealed record DataMaterializedQueryResult(string Sheet, DataRangeRequest Range, int DataRowCount);

public interface IDataSpreadsheetEngine : IAsyncDisposable
{
    Task<DataWorkbookHandle> OpenAsync(string path, bool readOnly, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DataSheetSummary>> ListSheetsAsync(string workbookId, CancellationToken cancellationToken = default);
    Task<DataRangeSnapshot> ReadRangeAsync(string workbookId, DataRangeRequest range, CancellationToken cancellationToken = default);
    Task<DataCellSnapshot> SetCellAsync(string workbookId, DataCellAddress address, string? value, string? formula = null, CancellationToken cancellationToken = default);
    Task<DataRangeSnapshot> CreateSheetWithValuesAsync(string workbookId, string sheetName, IReadOnlyList<IReadOnlyList<string>> values, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DataNamedRangeSummary>> ListNamedRangesAsync(string workbookId, CancellationToken cancellationToken = default);
    Task<DataNamedRangeSummary> CreateNamedRangeAsync(string workbookId, string name, DataRangeRequest range, CancellationToken cancellationToken = default);
    Task DeleteNamedRangeAsync(string workbookId, string name, CancellationToken cancellationToken = default);
    Task<DataListValidationState> GetListValidationAsync(string workbookId, DataRangeRequest range, CancellationToken cancellationToken = default);
    Task<DataListValidationState> ApplyListValidationAsync(string workbookId, DataRangeRequest range, IReadOnlyList<string> values, bool allowBlank = true, CancellationToken cancellationToken = default);
    Task<DataListValidationState> ClearValidationAsync(string workbookId, DataRangeRequest range, CancellationToken cancellationToken = default);

    // Sort is a first-slice optional capability so alternate/test spreadsheet engines
    // do not gain a fake implementation merely to satisfy the base contract. Engines
    // that support it (currently CalcSpreadsheetEngine) override this member.
    Task<DataSortResult> SortRangeAsync(
        string workbookId,
        DataRangeRequest range,
        int keyColumnOffset,
        bool ascending = true,
        bool containsHeader = true,
        CancellationToken cancellationToken = default) =>
        Task.FromException<DataSortResult>(new NotSupportedException("This spreadsheet engine does not support range sorting."));

    // Filtering is likewise optional at the engine boundary. The first Calc slice
    // supports one literal text-equality condition over rows and explicit clearing.
    Task<DataRangeSnapshot> FilterEqualsAsync(
        string workbookId,
        DataRangeRequest range,
        int keyColumnOffset,
        string value,
        bool containsHeader = true,
        CancellationToken cancellationToken = default) =>
        Task.FromException<DataRangeSnapshot>(new NotSupportedException("This spreadsheet engine does not support range filtering."));

    Task<DataRangeSnapshot> ClearFilterAsync(
        string workbookId,
        DataRangeRequest range,
        bool containsHeader = true,
        CancellationToken cancellationToken = default) =>
        Task.FromException<DataRangeSnapshot>(new NotSupportedException("This spreadsheet engine does not support clearing range filters."));

    Task InsertRowsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default);
    Task DeleteRowsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default);
    Task InsertColumnsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default);
    Task DeleteColumnsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default);
    Task RecalculateAsync(string workbookId, CancellationToken cancellationToken = default);
    Task SaveAsync(string workbookId, string destinationPath, CancellationToken cancellationToken = default);
    Task CloseAsync(string workbookId, CancellationToken cancellationToken = default);
}

public interface IDataDatabaseEngine : IAsyncDisposable
{
    Task OpenAsync(string databasePath, CancellationToken cancellationToken = default);
    Task ReplaceTableAsync(DataTableSnapshot table, CancellationToken cancellationToken = default);
    Task<DataQueryResult> ExecuteReadOnlyAsync(string sql, int maxRows = 200, CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}

public sealed class DataAppService(IDataSpreadsheetEngine spreadsheet, IDataDatabaseEngine database)
{
    public IDataSpreadsheetEngine Spreadsheet { get; } = spreadsheet ?? throw new ArgumentNullException(nameof(spreadsheet));
    public IDataDatabaseEngine Database { get; } = database ?? throw new ArgumentNullException(nameof(database));
}
