using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Exact one-record creation proposal, including the real applied defaults and storage impact.
/// Capturing freezes scalar values and grants no execution or persistence capability.</summary>
public sealed class DataRecordCreateIntent
{
    public const string TargetAppID = "data";
    public const string ActionID = "data.records.create";
    private readonly IReadOnlyDictionary<Guid, DataScalarRecordValue> _values;
    private readonly JsonElement _arguments;
    private readonly DataFormulaRecalculationReport _calculation;
    private DataRecordCreateIntent(Guid storeID, DataWorkbook workbook, Guid tableID, Guid recordID,
        IReadOnlyDictionary<Guid, DataScalarRecordValue> values, Guid operationID, DateTimeOffset calculationAt)
    {
        ArgumentNullException.ThrowIfNull(workbook); ArgumentNullException.ThrowIfNull(values);
        if (storeID == Guid.Empty || workbook.Id == Guid.Empty || workbook.Version < 1 || workbook.RevisionId == Guid.Empty || operationID == Guid.Empty)
            throw new ArgumentException("An exact persisted record creation target is required.");
        var captured = DataRecordCreationValues.Capture(values).OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key, pair => pair.Value);
        var prepared = DataRecordCreationProjection.Prepare(workbook, tableID, recordID, workbook.Version, workbook.RevisionId, captured, calculationAt);
        if (!prepared.Success) throw new InvalidDataException("RecordCreationPreviewFailed: " + prepared.Issues[0].Code);
        _calculation = prepared.Calculation! with { Issues = Array.AsReadOnly(prepared.Calculation!.Issues.ToArray()) };
        var before = workbook.Tables.Single(table => table.Id == tableID);
        var after = prepared.Workbook!.Tables.Single(table => table.Id == tableID);
        var row = after.Records.Single(record => record.RecordID == recordID);
        var sheet = prepared.Workbook!.Sheets.Single(sheet => sheet.Id == after.SheetId);
        var formulaEffects = workbook.Sheets.OrderBy(item => item.Id).SelectMany(original => original.Cells
            .Where(cell => !string.IsNullOrWhiteSpace(cell.Formula)).OrderBy(cell => cell.Row).ThenBy(cell => cell.Column)
            .Select(cell => new { original, cell, updated = prepared.Workbook!.Sheets.Single(item => item.Id == original.Id).GetCell(cell.Row, cell.Column) })
            .Where(item => item.updated is not null && (item.cell.Value != item.updated.Value || item.cell.Kind != item.updated.Kind
                || !item.cell.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal).SequenceEqual(item.updated.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))))
            .Select(item => new {
                sheetID = item.original.Id, row = item.cell.Row, column = item.cell.Column,
                before = new { item.cell.Kind, item.cell.Value, metadata = item.cell.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray() },
                after = new { item.updated!.Kind, item.updated.Value, metadata = item.updated.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray() }
            })).ToArray();
        var changedExistingRecords = workbook.Tables.SelectMany(existing => existing.Records
            .Where(existingRecord => formulaEffects.Any(effect => effect.sheetID == existing.SheetId && effect.row == existingRecord.SheetRow
                && existing.Fields.Any(field => field.SheetColumn == effect.column)))
            .Select(existingRecord => existingRecord.RecordID)).Distinct().Order().ToArray();
        _values = new ReadOnlyDictionary<Guid, DataScalarRecordValue>(captured);
        StoreID = storeID; WorkbookID = workbook.Id; Version = workbook.Version; RevisionID = workbook.RevisionId;
        TableID = tableID; RecordID = recordID; OperationID = operationID; CalculationAt = calculationAt;
        _arguments = JsonSerializer.SerializeToElement(new
        {
            operation = "record.create", operationID, storeID, workbookID = workbook.Id, version = workbook.Version, revisionID = workbook.RevisionId,
            tableID, tableName = before.Name, recordID, beforeSchema = before.RelationalSchema,
            recordsBefore = before.Records.Count, recordsCreated = 1, existingRecordsChanged = changedExistingRecords.Length, existingRecordsMoved = 0,
            calculationAt, calculation = prepared.Calculation, formulaEffects, changedExistingRecords,
            storage = new { sheetID = after.SheetId, row = row.SheetRow, beforeRange = before.Range, afterRange = after.Range },
            suppliedValues = captured.Select(pair => new { fieldID = pair.Key, kind = pair.Value.Kind.ToString(), value = pair.Value.Value }).ToArray(),
            actualValues = after.Fields.OrderBy(field => field.SheetColumn).Select(field => new
            {
                fieldID = field.FieldID, value = sheet.GetCell(row.SheetRow, field.SheetColumn)?.Value,
                kind = sheet.GetCell(row.SheetRow, field.SheetColumn)?.Kind.ToString(),
                defaultApplied = !captured.ContainsKey(field.FieldID) && before.RelationalSchema?.Fields.Single(item => item.FieldID == field.FieldID).DefaultValue is { Length: > 0 }
            }).ToArray(),
            relationshipsValidated = workbook.Relationships.Where(relation => relation.SourceTableID == tableID || relation.TargetTableID == tableID).ToArray()
        });
        if (System.Text.Encoding.UTF8.GetByteCount(_arguments.GetRawText()) > 1024 * 1024)
            throw new ArgumentException("The record creation preview exceeds 1 MiB.");
        Scopes = Array.AsReadOnly(new[] { new ResourceScope(DataRecordUpdateIntent.ResourceKind,
            $"{storeID:D}/{workbook.Id:D}", workbook.RevisionId.ToString("D"), ResourceAccess.Write) });
    }
    public Guid StoreID { get; }
    public Guid WorkbookID { get; }
    public int Version { get; }
    public Guid RevisionID { get; }
    public Guid TableID { get; }
    public Guid RecordID { get; }
    public Guid OperationID { get; }
    public DateTimeOffset CalculationAt { get; }
    public DataFormulaRecalculationReport Calculation => _calculation with { Issues = Array.AsReadOnly(_calculation.Issues.ToArray()) };
    public DataRecordCreateIntent Revalidate(DataWorkbook workbook) => new(StoreID, workbook, TableID, RecordID, _values, OperationID, CalculationAt);
    public IReadOnlyDictionary<Guid, DataScalarRecordValue> Values => _values;
    public JsonElement Arguments => _arguments.Clone();
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public string PayloadSHA256 => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_arguments.GetRawText())));
    public static DataRecordCreateIntent Capture(Guid storeID, DataWorkbook workbook, Guid tableID, Guid recordID,
        IReadOnlyDictionary<Guid, DataScalarRecordValue> values, Guid? operationID = null) =>
        new(storeID, workbook, tableID, recordID, values, operationID ?? Guid.NewGuid(), TimeProvider.System.GetLocalNow());
}
