namespace HavenOS.Apps.Data;

public sealed class CalcSpreadsheetEngine : IDataSpreadsheetEngine
{
    private const int MaximumMaterializedRows = 1000;
    private const int MaximumMaterializedColumns = 256;
    private const int MaximumStructuralMutationCount = 100;
    private const int MaximumNamedRangeNameLength = 64;
    private const int MaximumValidationValues = 50;
    private const int MaximumValidationValueLength = 64;
    private const int MaximumFilterValueLength = 256;

    private readonly JsonLineWorkerClient _worker;

    public CalcSpreadsheetEngine(string workerScriptPath, string pythonExecutable = "python3")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerScriptPath);
        _worker = new JsonLineWorkerClient(
            pythonExecutable,
            [workerScriptPath],
            new Dictionary<string, string?>
            {
                ["PYTHONUNBUFFERED"] = "1"
            });
    }

    public Task<DataWorkbookHandle> OpenAsync(string path, bool readOnly, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _worker.CallAsync<DataWorkbookHandle>("open", new { path, readOnly }, cancellationToken);
    }

    public Task<IReadOnlyList<DataSheetSummary>> ListSheetsAsync(string workbookId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        return _worker.CallAsync<IReadOnlyList<DataSheetSummary>>("listSheets", new { workbookId }, cancellationToken);
    }

    public Task<DataRangeSnapshot> ReadRangeAsync(string workbookId, DataRangeRequest range, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ValidateBoundedRange(range, "Read range");
        return _worker.CallAsync<DataRangeSnapshot>("readRange", new { workbookId, range }, cancellationToken);
    }

    public Task<DataCellSnapshot> SetCellAsync(string workbookId, DataCellAddress address, string? value, string? formula = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ArgumentNullException.ThrowIfNull(address);
        return _worker.CallAsync<DataCellSnapshot>("setCell", new { workbookId, address, value = value ?? string.Empty, formula = formula ?? string.Empty }, cancellationToken);
    }

    public Task<DataRangeSnapshot> CreateSheetWithValuesAsync(
        string workbookId,
        string sheetName,
        IReadOnlyList<IReadOnlyList<string>> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ValidatePortableSheetName(sheetName);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count is < 1 or > MaximumMaterializedRows)
            throw new ArgumentOutOfRangeException(nameof(values), $"Materialized sheets must contain 1-{MaximumMaterializedRows} rows in this slice.");
        var columns = values[0].Count;
        if (columns is < 1 or > MaximumMaterializedColumns)
            throw new ArgumentOutOfRangeException(nameof(values), $"Materialized sheets must contain 1-{MaximumMaterializedColumns} columns.");
        if (values.Any(row => row.Count != columns))
            throw new ArgumentException("Every materialized row must contain the same number of columns.", nameof(values));

        return _worker.CallAsync<DataRangeSnapshot>(
            "createSheetWithValues",
            new { workbookId, sheetName = sheetName.Trim(), values },
            cancellationToken);
    }

    public Task<IReadOnlyList<DataNamedRangeSummary>> ListNamedRangesAsync(string workbookId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        return _worker.CallAsync<IReadOnlyList<DataNamedRangeSummary>>("listNamedRanges", new { workbookId }, cancellationToken);
    }

    public Task<DataNamedRangeSummary> CreateNamedRangeAsync(
        string workbookId,
        string name,
        DataRangeRequest range,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ValidateNamedRangeName(name);
        ValidateBoundedRange(range, "Named range");
        return _worker.CallAsync<DataNamedRangeSummary>(
            "createNamedRange",
            new { workbookId, name = name.Trim(), range },
            cancellationToken);
    }

    public async Task DeleteNamedRangeAsync(string workbookId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ValidateNamedRangeName(name);
        _ = await _worker.CallAsync<WorkerAck>(
            "deleteNamedRange",
            new { workbookId, name = name.Trim() },
            cancellationToken).ConfigureAwait(false);
    }

    public Task<DataListValidationState> GetListValidationAsync(
        string workbookId,
        DataRangeRequest range,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ValidateBoundedRange(range, "Validation range");
        return _worker.CallAsync<DataListValidationState>(
            "getListValidation",
            new { workbookId, range },
            cancellationToken);
    }

    public Task<DataListValidationState> ApplyListValidationAsync(
        string workbookId,
        DataRangeRequest range,
        IReadOnlyList<string> values,
        bool allowBlank = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ValidateBoundedRange(range, "Validation range");
        var validatedValues = ValidateValidationValues(values);
        return _worker.CallAsync<DataListValidationState>(
            "applyListValidation",
            new { workbookId, range, values = validatedValues, allowBlank },
            cancellationToken);
    }

    public Task<DataListValidationState> ClearValidationAsync(
        string workbookId,
        DataRangeRequest range,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ValidateBoundedRange(range, "Validation range");
        return _worker.CallAsync<DataListValidationState>(
            "clearValidation",
            new { workbookId, range },
            cancellationToken);
    }

    public async Task<DataSortResult> SortRangeAsync(
        string workbookId,
        DataRangeRequest range,
        int keyColumnOffset,
        bool ascending = true,
        bool containsHeader = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ValidateBoundedRange(range, "Sort range");
        if (keyColumnOffset < 0 || keyColumnOffset >= range.ColumnCount)
            throw new ArgumentOutOfRangeException(nameof(keyColumnOffset), "Sort key must identify a column inside the requested range.");
        if (containsHeader && range.RowCount < 2)
            throw new ArgumentException("A sort range marked as containing a header must include at least one data row.", nameof(range));

        var snapshot = await _worker.CallAsync<DataRangeSnapshot>(
            "sortRange",
            new { workbookId, range, keyColumnOffset, ascending, containsHeader },
            cancellationToken).ConfigureAwait(false);
        return new DataSortResult(range, keyColumnOffset, ascending, containsHeader, snapshot);
    }

    public Task<DataRangeSnapshot> FilterEqualsAsync(
        string workbookId,
        DataRangeRequest range,
        int keyColumnOffset,
        string value,
        bool containsHeader = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ValidateBoundedRange(range, "Filter range");
        if (keyColumnOffset < 0 || keyColumnOffset >= range.ColumnCount)
            throw new ArgumentOutOfRangeException(nameof(keyColumnOffset), "Filter key must identify a column inside the requested range.");
        if (containsHeader && range.RowCount < 2)
            throw new ArgumentException("A filter range marked as containing a header must include at least one data row.", nameof(range));
        var validatedValue = ValidateFilterValue(value);
        return _worker.CallAsync<DataRangeSnapshot>(
            "filterEquals",
            new { workbookId, range, keyColumnOffset, value = validatedValue, containsHeader },
            cancellationToken);
    }

    public Task<DataRangeSnapshot> ClearFilterAsync(
        string workbookId,
        DataRangeRequest range,
        bool containsHeader = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ValidateBoundedRange(range, "Filter range");
        if (containsHeader && range.RowCount < 2)
            throw new ArgumentException("A filter range marked as containing a header must include at least one data row.", nameof(range));
        return _worker.CallAsync<DataRangeSnapshot>(
            "clearFilter",
            new { workbookId, range, containsHeader },
            cancellationToken);
    }

    public Task InsertRowsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default) =>
        MutateStructureAsync("insertRows", workbookId, sheet, index, count, cancellationToken);

    public Task DeleteRowsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default) =>
        MutateStructureAsync("deleteRows", workbookId, sheet, index, count, cancellationToken);

    public Task InsertColumnsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default) =>
        MutateStructureAsync("insertColumns", workbookId, sheet, index, count, cancellationToken);

    public Task DeleteColumnsAsync(string workbookId, string sheet, int index, int count, CancellationToken cancellationToken = default) =>
        MutateStructureAsync("deleteColumns", workbookId, sheet, index, count, cancellationToken);

    public async Task RecalculateAsync(string workbookId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        _ = await _worker.CallAsync<WorkerAck>("recalculate", new { workbookId }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(string workbookId, string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        _ = await _worker.CallAsync<WorkerAck>("save", new { workbookId, destinationPath }, cancellationToken).ConfigureAwait(false);
    }

    public async Task CloseAsync(string workbookId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        _ = await _worker.CallAsync<WorkerAck>("close", new { workbookId }, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _worker.DisposeAsync();

    private async Task MutateStructureAsync(
        string method,
        string workbookId,
        string sheet,
        int index,
        int count,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sheet);
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (count is < 1 or > MaximumStructuralMutationCount)
            throw new ArgumentOutOfRangeException(nameof(count), $"Structural mutations must affect 1-{MaximumStructuralMutationCount} rows or columns at a time.");

        _ = await _worker.CallAsync<WorkerAck>(
            method,
            new { workbookId, sheet, index, count },
            cancellationToken).ConfigureAwait(false);
    }

    private static string[] ValidateValidationValues(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count is < 1 or > MaximumValidationValues)
            throw new ArgumentOutOfRangeException(nameof(values), $"List validation requires 1-{MaximumValidationValues} literal values.");

        var result = new string[values.Count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index] ?? throw new ArgumentException("Validation values cannot be null.", nameof(values));
            if (value.Length is < 1 or > MaximumValidationValueLength)
                throw new ArgumentException($"Validation values must contain 1-{MaximumValidationValueLength} characters.", nameof(values));
            if (value.Any(char.IsControl) || value.Contains(';') || value.Contains('"'))
                throw new ArgumentException("First-slice validation values cannot contain control characters, semicolons or double quotes.", nameof(values));
            if (!seen.Add(value))
                throw new ArgumentException("Duplicate validation values are not allowed in the first slice.", nameof(values));
            result[index] = value;
        }
        return result;
    }

    private static string ValidateFilterValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is < 1 or > MaximumFilterValueLength)
            throw new ArgumentException($"First-slice filter values must contain 1-{MaximumFilterValueLength} characters.", nameof(value));
        if (value.Any(char.IsControl))
            throw new ArgumentException("First-slice filter values cannot contain control characters.", nameof(value));
        return value;
    }

    private static void ValidateBoundedRange(DataRangeRequest range, string label)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentException.ThrowIfNullOrWhiteSpace(range.Sheet);
        if (range.StartRow < 0 || range.StartColumn < 0)
            throw new ArgumentOutOfRangeException(nameof(range), $"{label} coordinates cannot be negative.");
        if (range.RowCount is < 1 or > MaximumMaterializedRows)
            throw new ArgumentOutOfRangeException(nameof(range), $"{label} can contain 1-{MaximumMaterializedRows} rows in this slice.");
        if (range.ColumnCount is < 1 or > MaximumMaterializedColumns)
            throw new ArgumentOutOfRangeException(nameof(range), $"{label} can contain 1-{MaximumMaterializedColumns} columns in this slice.");
    }

    private static void ValidateNamedRangeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        if (trimmed.Length > MaximumNamedRangeNameLength)
            throw new ArgumentException($"Named range names must be at most {MaximumNamedRangeNameLength} characters in this slice.", nameof(name));
        if (!(char.IsLetter(trimmed[0]) || trimmed[0] == '_') ||
            trimmed.Skip(1).Any(character => !(char.IsLetterOrDigit(character) || character is '_' or '.')))
            throw new ArgumentException("Named range names must start with a letter or underscore and then contain only letters, digits, underscores or periods.", nameof(name));
    }

    private static void ValidatePortableSheetName(string sheetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
        var trimmed = sheetName.Trim();
        if (trimmed.Length > 31)
            throw new ArgumentException("Materialized sheet names must be at most 31 characters for ODS/XLSX portability.", nameof(sheetName));
        if (trimmed.Any(character => char.IsControl(character) || "[]:*?/\\".Contains(character)))
            throw new ArgumentException("Materialized sheet names contain a character that is unsafe for ODS/XLSX portability.", nameof(sheetName));
        if (trimmed.StartsWith('\'') || trimmed.EndsWith('\''))
            throw new ArgumentException("Materialized sheet names cannot begin or end with an apostrophe.", nameof(sheetName));
    }

    private sealed record WorkerAck(bool Ok);
}
