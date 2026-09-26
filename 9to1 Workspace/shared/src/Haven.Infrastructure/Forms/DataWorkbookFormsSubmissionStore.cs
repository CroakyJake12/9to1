using System.Globalization;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Stores Forms responses in the same durable workbook that the Data app opens and exports.</summary>
public sealed class DataWorkbookFormsSubmissionStore : IFormsSubmissionStore
{
    private static readonly string[] Headers = ["Response ID", "Form ID", "Form Title", "Submitted At (UTC)", "Values (JSON)"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    // All instances share one read-modify-write sequence for this stable workbook.
    private static readonly SemaphoreSlim WorkbookGate = new(1, 1);
    private readonly IDataWorkbookRepository _workbooks;

    public DataWorkbookFormsSubmissionStore(IDataWorkbookRepository workbooks) =>
        _workbooks = workbooks ?? throw new ArgumentNullException(nameof(workbooks));

    public Guid DataWorkbookId => DataWorkbookAppLinks.FormsResponses;

    public async Task<IReadOnlyList<FormsSubmission>> GetLatestAsync(CancellationToken cancellationToken)
    {
        await WorkbookGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var workbook = await LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
            var sheet = GetResponseSheet(workbook);
            var columns = ReadColumnIndexes(sheet);
            return FormsSubmissionLogic.Normalise(ReadRows(sheet, columns).Select(item => item.Submission));
        }
        finally
        {
            WorkbookGate.Release();
        }
    }

    public async Task SaveAsync(FormsSubmission submission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var normalized = FormsSubmissionLogic.Normalise(submission);
        if (string.IsNullOrWhiteSpace(normalized.Id) || string.IsNullOrWhiteSpace(normalized.FormId))
            throw new ArgumentException("A form submission needs a stable response id and form id.", nameof(submission));

        await WorkbookGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var workbook = await LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
            var sheet = GetResponseSheet(workbook);
            var columns = ReadColumnIndexes(sheet);
            var existing = ReadRows(sheet, columns);
            var priorResponses = existing.Where(item => item.Submission.Id.Equals(normalized.Id, StringComparison.Ordinal)).ToArray();
            if (priorResponses.Length > 0)
            {
                if (priorResponses.All(item => AreEquivalent(item.Submission, normalized))) return;
                throw new FormsSubmissionConflictException(normalized.Id);
            }

            var submissions = FormsSubmissionLogic.Normalise(existing
                .Select(item => item.Submission)
                .Append(normalized));

            // Keep each response on its existing row so Data edits, extra columns and unrelated
            // cells in the workbook survive updates. New responses append after all used rows.
            var existingById = existing.GroupBy(item => item.Submission.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Row).First(), StringComparer.Ordinal);
            var retainedIds = submissions.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var removed in existing.Where(item => !retainedIds.Contains(item.Submission.Id)))
                foreach (var header in Headers) sheet.SetCell(removed.Row, columns[header], string.Empty);
            var occupiedRows = sheet.Cells.Where(cell => cell.Row > 0).Select(cell => cell.Row).ToHashSet();
            var nextRow = occupiedRows.Count == 0 ? 1 : occupiedRows.Max() + 1;
            var responseRows = new List<int>(submissions.Count);
            foreach (var response in submissions)
            {
                int row;
                IReadOnlyDictionary<int, string>? extras = null;
                if (existingById.TryGetValue(response.Id, out var prior))
                {
                    row = prior.Row;
                    extras = prior.ExtraCells;
                }
                else
                {
                    while (occupiedRows.Contains(nextRow)) nextRow++;
                    row = nextRow++;
                    occupiedRows.Add(row);
                }
                responseRows.Add(row);
                WriteCell(sheet, row, columns[Headers[0]], response.Id);
                WriteCell(sheet, row, columns[Headers[1]], response.FormId);
                WriteCell(sheet, row, columns[Headers[2]], response.FormTitle);
                WriteCell(sheet, row, columns[Headers[3]], response.SubmittedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                WriteCell(sheet, row, columns[Headers[4]], JsonSerializer.Serialize(response.Values, JsonOptions));
                if (extras is not null)
                    foreach (var (column, value) in extras) WriteCell(sheet, row, column, value);
            }

            EnsureResponseTable(workbook, sheet, columns, responseRows.Count == 0 ? 0 : responseRows.Max());
            workbook.Metadata["haven.app"] = "forms";
            workbook.Metadata["haven.purpose"] = "Locally stored Forms responses";
            await _workbooks.SaveAsync(workbook, "Forms response submitted", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            WorkbookGate.Release();
        }
    }

    private async Task<DataWorkbook> LoadOrCreateAsync(CancellationToken cancellationToken)
    {
        var workbook = await _workbooks.LoadAsync(DataWorkbookId, cancellationToken).ConfigureAwait(false);
        if (workbook is not null)
        {
            var originalSheetCount = workbook.Sheets.Count;
            var sheet = GetResponseSheet(workbook);
            var priorHeaderCount = sheet.Cells.Count(cell => cell.Row == 0);
            var existingColumns = EnsureHeaders(sheet);
            var rows = ReadRows(sheet, existingColumns);
            var priorTable = workbook.Tables.FirstOrDefault(item => item.Name.Equals("FormsResponses", StringComparison.OrdinalIgnoreCase));
            (Guid SheetId, int StartColumn, int EndRow, int EndColumn, bool HasHeaders)? tableState = priorTable is null ? null : (priorTable.SheetId, priorTable.Range.StartColumn,
                priorTable.Range.EndRow, priorTable.Range.EndColumn, priorTable.HasHeaders);
            EnsureResponseTable(workbook, sheet, existingColumns, rows.Count == 0 ? 0 : rows.Max(item => item.Row));
            var currentTable = workbook.Tables.First(item => item.Name.Equals("FormsResponses", StringComparison.OrdinalIgnoreCase));
            var changed = originalSheetCount != workbook.Sheets.Count || priorHeaderCount != sheet.Cells.Count(cell => cell.Row == 0)
                || tableState != (currentTable.SheetId, currentTable.Range.StartColumn, currentTable.Range.EndRow,
                    currentTable.Range.EndColumn, currentTable.HasHeaders);
            if (changed)
                await _workbooks.SaveAsync(workbook, "Forms response workbook schema prepared", cancellationToken).ConfigureAwait(false);
            return workbook;
        }

        workbook = DataWorkbook.Create("Forms responses");
        workbook.Id = DataWorkbookId;
        workbook.Sheets[0].Name = "Responses";
        workbook.Queries[0].Visual.Source = "Responses";
        workbook.Queries[0].Sql = "SELECT * FROM \"Responses\";";
        workbook.Metadata["haven.app"] = "forms";
        workbook.Metadata["haven.purpose"] = "Locally stored Forms responses";
        var responseSheet = GetResponseSheet(workbook);
        var columns = EnsureHeaders(responseSheet);
        EnsureResponseTable(workbook, responseSheet, columns, 0);
        await _workbooks.SaveAsync(workbook, "Forms response workbook created", cancellationToken).ConfigureAwait(false);
        return workbook;
    }

    private static DataSheet GetResponseSheet(DataWorkbook workbook) =>
        workbook.Sheets.FirstOrDefault(sheet => sheet.Name.Equals("Responses", StringComparison.OrdinalIgnoreCase))
        ?? workbook.Sheets.FirstOrDefault()
        ?? throw new InvalidDataException("The Forms response workbook has no sheet.");

    private static Dictionary<string, int> EnsureHeaders(DataSheet sheet)
    {
        var columns = ReadHeaderMap(sheet);
        var nextColumn = sheet.Cells.Where(cell => cell.Row == 0).Select(cell => cell.Column).DefaultIfEmpty(-1).Max() + 1;
        foreach (var header in Headers)
        {
            if (columns.ContainsKey(header)) continue;
            if (sheet.Cells.Any(cell => cell.Row == 0 && cell.Column == nextColumn)) nextColumn++;
            sheet.SetCell(0, nextColumn, header, null, DataCellKind.Text);
            columns[header] = nextColumn++;
        }
        return columns;
    }

    private static Dictionary<string, int> ReadColumnIndexes(DataSheet sheet)
    {
        var columns = ReadHeaderMap(sheet);
        var missing = Headers.Where(header => !columns.ContainsKey(header)).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException("The Forms response workbook is missing columns: " + string.Join(", ", missing));
        return columns;
    }

    private static Dictionary<string, int> ReadHeaderMap(DataSheet sheet) => sheet.Cells
        .Where(cell => cell.Row == 0 && !string.IsNullOrWhiteSpace(cell.Value))
        .GroupBy(cell => cell.Value.Trim(), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First().Column, StringComparer.OrdinalIgnoreCase);

    private static List<StoredResponse> ReadRows(DataSheet sheet, IReadOnlyDictionary<string, int> columns)
    {
        var result = new List<StoredResponse>();
        var firstColumn = columns[Headers[0]];
        foreach (var row in sheet.Cells.Where(cell => cell.Column == firstColumn && cell.Row > 0).Select(cell => cell.Row).Distinct().Order())
        {
            var id = ReadCell(sheet, row, columns[Headers[0]]);
            var formId = ReadCell(sheet, row, columns[Headers[1]]);
            var title = ReadCell(sheet, row, columns[Headers[2]]);
            var submittedAt = ReadCell(sheet, row, columns[Headers[3]]);
            var valuesJson = ReadCell(sheet, row, columns[Headers[4]]);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(formId)
                || !DateTimeOffset.TryParse(submittedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedAt))
                continue;
            Dictionary<string, string>? values;
            try { values = JsonSerializer.Deserialize<Dictionary<string, string>>(valuesJson, JsonOptions); }
            catch (JsonException) { continue; }
            if (values is null) continue;
            var submission = FormsSubmissionLogic.Normalise(new FormsSubmission(id, formId, title, values, parsedAt));
            var knownColumns = columns.Values.ToHashSet();
            var extras = sheet.Cells.Where(cell => cell.Row == row && !knownColumns.Contains(cell.Column))
                .ToDictionary(cell => cell.Column, cell => cell.Value);
            result.Add(new StoredResponse(row, submission, extras));
        }
        return result;
    }

    private static void EnsureResponseTable(DataWorkbook workbook, DataSheet sheet, IReadOnlyDictionary<string, int> columns, int lastResponseRow)
    {
        var firstColumn = columns.Values.Min();
        var lastColumn = columns.Values.Max();
        var table = workbook.Tables.FirstOrDefault(item => item.Name.Equals("FormsResponses", StringComparison.OrdinalIgnoreCase));
        if (table is null)
        {
            table = new DataTableDefinition { Name = "FormsResponses", SheetId = sheet.Id };
            workbook.Tables.Add(table);
        }
        table.SheetId = sheet.Id;
        table.HasHeaders = true;
        table.Range = new DataCellRange
        {
            StartRow = 0,
            StartColumn = firstColumn,
            EndRow = lastResponseRow,
            EndColumn = lastColumn
        };
    }

    private static string ReadCell(DataSheet sheet, int row, int column) => sheet.GetCell(row, column)?.Value ?? string.Empty;
    private static void WriteCell(DataSheet sheet, int row, int column, string value) => sheet.SetCell(row, column, value, null, DataCellKind.Text);

    private static bool AreEquivalent(FormsSubmission stored, FormsSubmission retry) =>
        stored.Id.Equals(retry.Id, StringComparison.Ordinal)
        && stored.FormId.Equals(retry.FormId, StringComparison.Ordinal)
        && stored.FormTitle.Equals(retry.FormTitle, StringComparison.Ordinal)
        && stored.SubmittedAt.Equals(retry.SubmittedAt)
        && stored.Values.Count == retry.Values.Count
        && stored.Values.All(pair => retry.Values.TryGetValue(pair.Key, out var value)
            && pair.Value.Equals(value, StringComparison.Ordinal));

    private sealed record StoredResponse(int Row, FormsSubmission Submission, IReadOnlyDictionary<int, string> ExtraCells);
}
