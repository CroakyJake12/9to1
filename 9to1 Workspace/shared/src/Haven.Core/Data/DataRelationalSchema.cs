using System.Globalization;
using System.Text.Json;

namespace Haven.Core;

public enum DataFieldType { Text, Integer, Decimal, Currency, Boolean, Date, DateTime, Duration, Uuid, Category }
public enum DataKeyKind { Primary, Unique }
public enum DataRelationshipCardinality { OneToMany, OneToOne }
public sealed record DataFieldDefinition(Guid FieldID, string Name, DataFieldType Type, bool Nullable = true,
    string? DefaultValue = null, IReadOnlyList<string>? Categories = null);
public sealed record DataKeyDefinition(Guid KeyID, string Name, DataKeyKind Kind, IReadOnlyList<Guid> FieldIDs);
public sealed record DataRelationshipDefinition(Guid RelationshipID, string Name, Guid SourceTableID,
    IReadOnlyList<Guid> SourceFieldIDs, Guid TargetTableID, Guid TargetKeyID,
    DataRelationshipCardinality Cardinality, bool Optional, long Revision);
public sealed record DataTableSchema(int SchemaVersion, long Revision, IReadOnlyList<DataFieldDefinition> Fields,
    IReadOnlyList<DataKeyDefinition> Keys);
public sealed record DataSchemaIssue(string Code, Guid TableID, Guid? FieldID = null,
    Guid? RecordID = null, Guid? ConstraintID = null);

/// <summary>Relational constraints over the existing canonical table records and sheet cells.
/// It owns no copied row values, query engine or external database capability.</summary>
public static class DataRelationalSchema
{
    public static IReadOnlyList<DataSchemaIssue> Inspect(DataWorkbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        var issues = new List<DataSchemaIssue>();
        DataTableIdentity.ValidateWorkbook(workbook);
        if (workbook.Relationships is null) return [new("InvalidRelationshipSchema", Guid.Empty)];
        var ids = workbook.Tables.SelectMany(table => table.Fields.Select(field => field.FieldID)
            .Concat(table.Records.Select(record => record.RecordID)).Append(table.Id)).ToHashSet();
        foreach (var table in workbook.Tables.Where(table => table.RelationalSchema is not null))
        {
            var schema = table.RelationalSchema!;
            if (schema.SchemaVersion != 1 || schema.Revision < 1 || schema.Fields is null || schema.Keys is null
                || table.RecordIdentityVersion != 1 || schema.Fields.Count != table.Fields.Count)
            { issues.Add(new("InvalidSchema", table.Id)); continue; }
            var fieldIds = table.Fields.Select(field => field.FieldID).ToHashSet();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in schema.Fields)
            {
                if (field is null || !fieldIds.Remove(field.FieldID) || !Enum.IsDefined(field.Type)
                    || string.IsNullOrWhiteSpace(field.Name) || field.Name.Length > 256 || !names.Add(field.Name))
                { issues.Add(new("InvalidField", table.Id, field?.FieldID)); continue; }
                if (field.Type == DataFieldType.Category && (field.Categories is null || field.Categories.Count == 0
                    || field.Categories.Count > 4096 || field.Categories.Any(string.IsNullOrEmpty)
                    || field.Categories.Distinct(StringComparer.Ordinal).Count() != field.Categories.Count))
                    issues.Add(new("InvalidCategories", table.Id, field.FieldID));
                if (field.DefaultValue is not null && !TryCanonical(field, field.DefaultValue, out _))
                    issues.Add(new("InvalidDefault", table.Id, field.FieldID));
            }
            var keyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (schema.Keys.Count(key => key?.Kind == DataKeyKind.Primary) > 1) issues.Add(new("MultiplePrimaryKeys", table.Id));
            foreach (var key in schema.Keys)
            {
                if (key is null || key.KeyID == Guid.Empty || !ids.Add(key.KeyID) || !Enum.IsDefined(key.Kind)
                    || string.IsNullOrWhiteSpace(key.Name) || key.Name.Length > 256 || !keyNames.Add(key.Name)
                    || key.FieldIDs is null || key.FieldIDs.Count == 0 || key.FieldIDs.Count > table.Fields.Count
                    || key.FieldIDs.Distinct().Count() != key.FieldIDs.Count
                    || key.FieldIDs.Any(id => schema.Fields.All(field => field?.FieldID != id)))
                { issues.Add(new("InvalidKey", table.Id, ConstraintID: key?.KeyID)); continue; }
            }
        }
        if (issues.Any(issue => issue.Code is "InvalidSchema" or "InvalidField" or "InvalidKey" or "InvalidCategories" or "InvalidDefault" or "MultiplePrimaryKeys")) return issues;
        var contexts = workbook.Tables.Where(table => table.RelationalSchema is not null)
            .ToDictionary(table => table.Id, table => new TableContext(workbook, table));
        foreach (var table in workbook.Tables.Where(table => table.RelationalSchema is not null))
        {
            var schema = table.RelationalSchema!;
            foreach (var field in schema.Fields)
            {
                foreach (var record in table.Records)
                {
                    if (issues.Count >= 256) return issues;
                    var cell = contexts[table.Id].Cell(record, field.FieldID);
                    if (cell?.Kind == DataCellKind.Formula)
                        issues.Add(new("FormulaProjectionRequired", table.Id, field.FieldID, record.RecordID));
                    else if (!Compatible(field, cell) || !TryCanonical(field, cell?.Value, out _))
                        issues.Add(new("InvalidValue", table.Id, field.FieldID, record.RecordID));
                }
            }
            foreach (var key in schema.Keys)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var record in table.Records)
                {
                    if (issues.Count >= 256) return issues;
                    if (!Tuple(contexts, table, record, key.FieldIDs, out var tuple, out var empty)) continue;
                    if (empty)
                    {
                        if (key.Kind == DataKeyKind.Primary) issues.Add(new("MissingPrimaryKey", table.Id, RecordID: record.RecordID, ConstraintID: key.KeyID));
                        continue; // Nullable unique keys follow SQL's distinct-null semantics.
                    }
                    if (!seen.Add(tuple)) issues.Add(new("DuplicateKey", table.Id, RecordID: record.RecordID, ConstraintID: key.KeyID));
                }
            }
        }
        foreach (var relation in workbook.Relationships)
        {
            var source = workbook.Tables.SingleOrDefault(table => table.Id == relation?.SourceTableID);
            var target = workbook.Tables.SingleOrDefault(table => table.Id == relation?.TargetTableID);
            var key = target?.RelationalSchema?.Keys.SingleOrDefault(key => key.KeyID == relation?.TargetKeyID);
            if (relation is null || relation.RelationshipID == Guid.Empty || !ids.Add(relation.RelationshipID)
                || relation.Revision < 1 || string.IsNullOrWhiteSpace(relation.Name) || relation.Name.Length > 256
                || !Enum.IsDefined(relation.Cardinality) || source?.RelationalSchema is null || key is null
                || relation.SourceFieldIDs is null || relation.SourceFieldIDs.Count != key.FieldIDs.Count
                || relation.SourceFieldIDs.Distinct().Count() != relation.SourceFieldIDs.Count
                || relation.SourceFieldIDs.Any(id => source.RelationalSchema.Fields.All(field => field.FieldID != id)))
            { issues.Add(new("InvalidRelationship", source?.Id ?? Guid.Empty, ConstraintID: relation?.RelationshipID)); continue; }
            if (relation.SourceFieldIDs.Zip(key.FieldIDs).Any(pair =>
                source.RelationalSchema.Fields.Single(field => field.FieldID == pair.First).Type
                != target!.RelationalSchema!.Fields.Single(field => field.FieldID == pair.Second).Type))
            { issues.Add(new("RelationshipTypeMismatch", source.Id, ConstraintID: relation.RelationshipID)); continue; }
            var targets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in target!.Records)
                if (Tuple(contexts, target, record, key.FieldIDs, out var tuple, out var empty) && !empty) targets.Add(tuple);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in source.Records)
            {
                if (issues.Count >= 256) return issues;
                if (!Tuple(contexts, source, record, relation.SourceFieldIDs, out var tuple, out var empty)) continue;
                if (empty)
                {
                    if (!relation.Optional) issues.Add(new("MissingReference", source.Id, RecordID: record.RecordID, ConstraintID: relation.RelationshipID));
                    continue;
                }
                if (!targets.Contains(tuple)) issues.Add(new("MissingReferencedKey", source.Id, RecordID: record.RecordID, ConstraintID: relation.RelationshipID));
                if (relation.Cardinality == DataRelationshipCardinality.OneToOne && !seen.Add(tuple))
                    issues.Add(new("DuplicateOneToOneReference", source.Id, RecordID: record.RecordID, ConstraintID: relation.RelationshipID));
            }
        }
        return issues;
    }

    public static IReadOnlyList<DataSchemaIssue> InspectCellEdit(DataWorkbook workbook, Guid sheetID,
        int row, int column, string? value, string? formula = null, DataCellKind? kind = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        if (workbook.Relationships.Count == 0 && workbook.Tables.All(table => table.RelationalSchema is null)) return [];
        var candidate = JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.Serialize(workbook))!;
        var sheet = candidate.Sheets.SingleOrDefault(item => item.Id == sheetID)
            ?? throw new KeyNotFoundException("SheetNotFound");
        sheet.SetCell(row, column, value, formula, kind ?? KindForCellEdit(workbook, sheetID, row, column, value ?? string.Empty));
        return Inspect(candidate);
    }

    /// <summary>Typing in an authored field follows its scalar declaration. Ordinary grid cells retain inference.</summary>
    public static DataCellKind KindForCellEdit(DataWorkbook workbook, Guid sheetID, int row, int column, string value)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        var table = workbook.Tables.SingleOrDefault(table => table.RelationalSchema is not null && table.SheetId == sheetID
            && table.Range.Contains(row, column) && (!table.HasHeaders || row != table.Range.StartRow));
        if (table is null) return DataCell.InferKind(value);
        var fieldID = table.Fields.Single(field => field.SheetColumn == column).FieldID;
        var field = table.RelationalSchema!.Fields.Single(field => field.FieldID == fieldID);
        return field.Type switch
        {
            DataFieldType.Integer or DataFieldType.Decimal or DataFieldType.Currency => DataCellKind.Number,
            DataFieldType.Boolean => DataCellKind.Boolean,
            DataFieldType.Date or DataFieldType.DateTime => DataCellKind.Date,
            DataFieldType.Text or DataFieldType.Category or DataFieldType.Uuid or DataFieldType.Duration => DataCellKind.Text,
            _ => throw new InvalidDataException("The authored field kind is unavailable.")
        };
    }

    public static void Validate(DataWorkbook workbook)
    {
        var issues = Inspect(workbook);
        if (issues.Count != 0) throw new InvalidDataException($"{issues[0].Code}: table {issues[0].TableID:D}, constraint {issues[0].ConstraintID:D}");
    }

    private static bool Tuple(IReadOnlyDictionary<Guid, TableContext> contexts, DataTableDefinition table, DataTableRecord record,
        IReadOnlyList<Guid> fields, out string tuple, out bool empty)
    {
        var values = new List<string?>(); empty = false; tuple = "";
        foreach (var id in fields)
        {
            var field = contexts[table.Id].Definition(id);
            var cell = contexts[table.Id].Cell(record, id);
            if (cell?.Kind == DataCellKind.Formula || !Compatible(field, cell) || !TryCanonical(field, cell?.Value, out var value)) return false;
            values.Add(value); empty |= value is null;
        }
        tuple = JsonSerializer.Serialize(values); return true;
    }

    private sealed class TableContext
    {
        private readonly DataSheet _sheet;
        private readonly IReadOnlyDictionary<Guid, int> _columns;
        private readonly DataTableDefinition _table;
        private IReadOnlyDictionary<Guid, DataFieldDefinition>? _definitions;
        public TableContext(DataWorkbook workbook, DataTableDefinition table)
        {
            _table = table;
            _sheet = workbook.Sheets.Single(sheet => sheet.Id == table.SheetId);
            _columns = table.Fields.ToDictionary(field => field.FieldID, field => field.SheetColumn);
        }
        public DataCell? Cell(DataTableRecord record, Guid fieldID) => _sheet.GetCell(record.SheetRow, _columns[fieldID]);
        public DataFieldDefinition Definition(Guid fieldID) =>
            (_definitions ??= _table.RelationalSchema!.Fields.ToDictionary(field => field.FieldID))[fieldID];
    }

    private static bool Compatible(DataFieldDefinition field, DataCell? cell)
    {
        if (cell is null || cell.Value.Length == 0) return field.Nullable;
        return field.Type switch
        {
            DataFieldType.Integer or DataFieldType.Decimal or DataFieldType.Currency => cell.Kind == DataCellKind.Number,
            DataFieldType.Boolean => cell.Kind == DataCellKind.Boolean,
            DataFieldType.Date or DataFieldType.DateTime => cell.Kind == DataCellKind.Date,
            DataFieldType.Text or DataFieldType.Category or DataFieldType.Uuid or DataFieldType.Duration => cell.Kind == DataCellKind.Text,
            _ => false
        };
    }

    private static bool TryCanonical(DataFieldDefinition field, string? value, out string? canonical)
    {
        canonical = value;
        if (string.IsNullOrEmpty(value)) { canonical = null; return field.Nullable; }
        switch (field.Type)
        {
            case DataFieldType.Text: return true;
            case DataFieldType.Category: return field.Categories?.Contains(value, StringComparer.Ordinal) == true;
            case DataFieldType.Integer:
                if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)) return false;
                canonical = integer.ToString(CultureInfo.InvariantCulture); return true;
            case DataFieldType.Decimal or DataFieldType.Currency:
                if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)) return false;
                canonical = number.ToString("G29", CultureInfo.InvariantCulture); return true;
            case DataFieldType.Boolean:
                if (!bool.TryParse(value, out var boolean)) return false;
                canonical = boolean ? "true" : "false"; return true;
            case DataFieldType.Uuid:
                if (!Guid.TryParseExact(value, "D", out var uuid)) return false;
                canonical = uuid.ToString("D"); return true;
            case DataFieldType.Date:
                if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return false;
                canonical = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); return true;
            case DataFieldType.DateTime:
                if (!DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)) return false;
                canonical = timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture); return true;
            case DataFieldType.Duration:
                if (!TimeSpan.TryParseExact(value, "c", CultureInfo.InvariantCulture, out var duration)) return false;
                canonical = duration.ToString("c", CultureInfo.InvariantCulture); return true;
            default: return false;
        }
    }
}
