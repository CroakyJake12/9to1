using Haven.Application;
using System.Text.Json;
using HavenText = Haven.UI.Components.Text;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Views.Pages.Data;

internal sealed record DataRelationshipEditorSnapshot(NodeEditorDocument Document, NodeEditorSchemaRegistry Schema);

internal static class DataRelationshipEditorAdapter
{
    private static string RelationshipLabel(DataRelationshipDefinition relationship) =>
        relationship.Name + " · " + (relationship.Cardinality == DataRelationshipCardinality.OneToOne ? "one to one" : "many to one")
        + " · " + (relationship.Optional ? "optional" : "required");

    public static DataRelationshipEditorSnapshot Capture(DataWorkbook workbook)
    {
        var snapshot = DataRelationshipGraphProjection.Capture(workbook);
        var schema = new NodeEditorSchemaRegistry();
        var nodes = snapshot.Graph.Nodes.Select((node, index) =>
        {
            var table = workbook.Tables.Single(table => table.Id == node.NodeId);
            var ports = node.Ports.Select(port => new NodeEditorPort(port.PortId.ToString("D"),
                table.RelationalSchema?.Fields.FirstOrDefault(field => field.FieldID == port.PortId)?.Name
                    ?? table.RelationalSchema?.Keys.FirstOrDefault(key => key.KeyID == port.PortId)?.Name
                    ?? workbook.Relationships.FirstOrDefault(relation => relation.RelationshipID == port.PortId)?.Name
                    ?? port.Key,
                port.Direction == Haven.Application.NodeGraph.GraphPortDirection.Output
                    ? NodeEditorPortDirection.Output : NodeEditorPortDirection.Input,
                port.DataType, port.Multiple)).ToArray();
            schema.Register(new NodeEditorNodeSchema(node.TypeId, node.TypeVersion, ports,
                new HashSet<string>(StringComparer.Ordinal) { DataRelationshipGraphProjection.CapabilityID }));
            return new NodeEditorNode(node.NodeId, "Data table", table.Name)
            {
                TypeId = node.TypeId, SchemaVersion = node.TypeVersion, Ports = ports,
                X = index % 3 * 300, Y = index / 3 * 260,
                Height = Math.Max(118, 72 + ports.Length * 24),
                Subtitle = $"{table.Records.Count} records"
            };
        }).ToArray();
        var owners = snapshot.Graph.Nodes.SelectMany(node => node.Ports.Select(port => (port.PortId, node.NodeId)))
            .ToDictionary(pair => pair.PortId, pair => pair.NodeId);
        var edges = snapshot.Graph.Connections.Select(edge => new NodeEditorEdge(edge.ConnectionId,
            owners[edge.OutputPortId], edge.OutputPortId.ToString("D"),
            owners[edge.InputPortId], edge.InputPortId.ToString("D"))
        {
            Label = RelationshipLabel(workbook.Relationships.Single(relation => relation.RelationshipID == edge.ConnectionId))
        }).ToArray();
        schema.Register(new NodeEditorConnectionProfile(DataRelationshipGraphProjection.ProfileID,
            nodes.SelectMany(node => node.Ports).Select(port => port.DataType).ToHashSet(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal) { DataRelationshipGraphProjection.CapabilityID }));
        return new(new NodeEditorDocument(nodes, edges)
        {
            GraphId = snapshot.Graph.GraphId, Revision = snapshot.Graph.Revision,
            ConnectionProfileId = DataRelationshipGraphProjection.ProfileID
        }, schema);
    }
}

public sealed partial class DataPage
{
    private DataRecordCreationPanel? _recordCreation;
    private void InitializeRecordCreation(IDataRecordCreator? creator)
    {
        _recordCreation = new(() => Workbook,
            (saved, calculation) =>
            {
                Workbook = saved; _lastQueryResult = null;
                // The approved projection already saved its formula caches. Do
                // not rerun volatile formulas using the display clock.
                _formulaReport = calculation ?? new(0, 0, 0, []);
                RenderCurrent();
            }, creator, () => !_disposed && !_dirty && !_busy, () => _recordDisplaySelection);
        _route.Editor.Add(_recordCreation);
    }
    private DataTableDesignPanel? _tableDesign;
    private DataRelationshipDesignPanel? _relationships;
    private void InitializeRelationships(IDataRelationshipDesigner? designer)
    {
        _relationships = new(() => Workbook,
            saved => { Workbook = saved; _lastQueryResult = null; RenderCurrent(); },
            designer, () => !_disposed && !_dirty && !_busy);
        _route.Editor.Add(_relationships);
    }
    private void InitializeTableDesign(IDataTableSchemaDesigner? designer)
    {
        _tableDesign = new(() => Workbook,
            () => CurrentSheet is { } sheet ? CurrentTableDefinition(sheet) : null,
            saved => { Workbook = saved; _lastQueryResult = null; RenderCurrent(); },
            designer, () => !_disposed && !_dirty && !_busy);
        _route.Editor.Add(_tableDesign);
    }

    private void InitializeRecoveredDataUi()
    {
        var toolbar = _route.Root.DescendantsAndSelf().OfType<Container>().FirstOrDefault(element => element.Name == "Data.Grid.Toolbar");
        if (toolbar is not null && !toolbar.Children.Any(child => child.Name == "Data.Format.Bold"))
        {
            toolbar.Add(RecoveredButton("Data.Validation.WholeNumber", "Whole #", ApplyWholeNumberValidation));
            toolbar.Add(RecoveredButton("Data.Validation.Clear", "Clear validation", ClearValidation));
            toolbar.Add(RecoveredButton("Data.Format.Bold", "Bold", () => ApplySelectionFormat(DataCellFormatMetadata.FontWeight, "700", "Applied bold formatting.")));
            toolbar.Add(RecoveredButton("Data.Format.Underline", "Underline", () => ApplySelectionFormat(DataCellFormatMetadata.Underline, "true", "Applied underline formatting.")));
            toolbar.Add(RecoveredButton("Data.Format.Fill", "Fill", () => ApplySelectionFormat(DataCellFormatMetadata.Fill, "AccentSoft", "Applied cell fill.")));
            toolbar.Add(RecoveredButton("Data.Format.Border", "Border", () => ApplySelectionFormat(DataCellFormatMetadata.Border, "Accent", "Applied cell border.")));
            toolbar.Add(RecoveredButton("Data.Format.Center", "Center", () => ApplySelectionFormat(DataCellFormatMetadata.HorizontalAlignment, "center", "Centered selected cells.")));
            toolbar.Add(RecoveredButton("Data.Format.Decimal", "0.00", () => ApplySelectionFormat(DataCellFormatMetadata.NumberFormat, "0.00", "Applied decimal number format.")));
            toolbar.Add(RecoveredButton("Data.Format.Percent", "%", () => ApplySelectionFormat(DataCellFormatMetadata.NumberFormat, "percent", "Applied percentage number format.")));
        }
        InitializeCharting();
    }

    private void SyncRecoveredDataUi()
    {
        if (CurrentSheet is { } sheet && SpreadsheetSurface() is { } surface)
        {
            var table = Workbook?.Tables.FirstOrDefault(value => value.SheetId == sheet.Id && value.Range.Contains(surface.ActiveRow, surface.ActiveColumn))
                ?? Workbook?.Tables.FirstOrDefault(value => value.SheetId == sheet.Id);
            surface.ApplyTableDefinition(table);
        }
        SyncChartUi();
        _tableDesign?.Refresh();
        _relationships?.Refresh();
        _recordCreation?.Refresh();
    }

    private DataTableDefinition? CurrentTableDefinition(DataSheet sheet, DataSpreadsheetSurface? surface = null)
    {
        if (Workbook is null) return null;
        surface ??= SpreadsheetSurface();
        return surface is null
            ? Workbook.Tables.FirstOrDefault(value => value.SheetId == sheet.Id)
            : Workbook.Tables.FirstOrDefault(value => value.SheetId == sheet.Id && value.Range.Contains(surface.ActiveRow, surface.ActiveColumn))
              ?? Workbook.Tables.FirstOrDefault(value => value.SheetId == sheet.Id);
    }

    private void ApplyWholeNumberValidation()
    {
        if (Workbook is null || CurrentSheet is not { } sheet || SpreadsheetSurface() is not { } surface) return;
        var selection = surface.Selection; if (!ValidateCommandRange(selection, "apply validation")) return;
        var activity = BeginDataActivity("Data validation", $"Applying whole-number validation to {Address(selection.StartRow, selection.StartColumn)}:{Address(selection.EndRow, selection.EndColumn)}.");
        CaptureSpreadsheetUndo();
        Workbook.Validations.RemoveAll(rule => rule.SheetId == sheet.Id && RangesOverlap(rule.Range, selection));
        Workbook.Validations.Add(new DataValidationRule
        {
            SheetId = sheet.Id, Range = ToCoreRange(selection), Kind = DataValidationKind.WholeNumber, AllowBlank = true,
            ErrorMessage = "Enter a whole number or leave the cell blank."
        });
        MarkDirty(); RenderCurrent(); var result = $"Whole-number validation applied to {Address(selection.StartRow, selection.StartColumn)}:{Address(selection.EndRow, selection.EndColumn)}."; _route.SetStatus(result); CompleteDataActivity(activity, result);
    }

    private void ClearValidation()
    {
        if (Workbook is null || CurrentSheet is not { } sheet || SpreadsheetSurface() is not { } surface) return;
        var selection = surface.Selection; var matches = Workbook.Validations.Where(rule => rule.SheetId == sheet.Id && RangesOverlap(rule.Range, selection)).ToArray(); if (matches.Length == 0) { _route.SetStatus("No validation rules overlap the selected range."); return; }
        CaptureSpreadsheetUndo(); foreach (var rule in matches) Workbook.Validations.Remove(rule); MarkDirty(); RenderCurrent(); _route.SetStatus($"Removed {matches.Length} validation rule(s).");
    }

    private void ApplySelectionFormat(string key, string value, string status)
    {
        if (CurrentSheet is not { } sheet || SpreadsheetSurface() is not { } surface) return; var selection = surface.Selection; if (!ValidateCommandRange(selection, "format this range")) return;
        var activity = BeginDataActivity("Format spreadsheet range", $"Formatting {Address(selection.StartRow, selection.StartColumn)}:{Address(selection.EndRow, selection.EndColumn)}.");
        CaptureSpreadsheetUndo(); DataSpreadsheetOperations.ApplyFormat(sheet, ToCoreRange(selection), new Dictionary<string, string?> { [key] = value }); MarkDirty(); RenderCurrent(); _route.SetStatus(status); CompleteDataActivity(activity, status);
    }

    private DataValidationResult ValidateCellValue(DataSheet sheet, int row, int column, string value)
    {
        if (Workbook is null) return DataValidationResult.Valid;
        var validation = DataSpreadsheetOperations.ValidateValue(Workbook.Validations, sheet.Id, row, column, value);
        if (!validation.IsValid) return validation;
        var issues = DataRelationalSchema.InspectCellEdit(Workbook, sheet.Id, row, column, value);
        return issues.Count == 0 ? DataValidationResult.Valid : new(false, Message: issues[0].Code);
    }
    private static DataCellRange ToCoreRange(DataSpreadsheetRange range) => new() { StartRow = range.StartRow, StartColumn = range.StartColumn, EndRow = range.EndRow, EndColumn = range.EndColumn };
    private static bool RangesOverlap(DataCellRange left, DataSpreadsheetRange right) => left.StartRow <= right.EndRow && left.EndRow >= right.StartRow && left.StartColumn <= right.EndColumn && left.EndColumn >= right.StartColumn;

    private static HavenButton RecoveredButton(string name, string content, Action action)
    {
        var button = new HavenButton { Name = name, Content = content, Variant = ButtonVariant.Tertiary }; button.Accessibility.AccessibleName = content; button.SetValue(HavenProperties.MinHeight, HavenLength.Px(38)); button.Invoked += (_, _) => action(); return button;
    }

    private static Container RecoveredCard(string name)
    {
        var card = new Container { Name = name, Layout = HavenLayout.Vertical }; card.SetValue(HavenProperties.Background, "SurfaceRaised"); card.SetValue(HavenProperties.BorderColor, "Border"); card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1)); card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14))); card.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px")); card.SetValue(HavenProperties.Gap, HavenLength.Px(8)); return card;
    }

    private static Container RecoveredToolbar(string name) { var toolbar = new Container { Name = name, Layout = HavenLayout.Horizontal }; toolbar.SetValue(HavenProperties.Gap, HavenLength.Px(6)); toolbar.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll); return toolbar; }
    private static Input RecoveredInput(string name, string accessibleName, string placeholder) { var input = new Input { Name = name, Placeholder = placeholder }; input.Accessibility.AccessibleName = accessibleName; input.SetValue(HavenProperties.MinHeight, HavenLength.Px(38)); return input; }
}

/// <summary>Editable local Table Design draft. It edits the owning canonical table schema,
/// and never writes sheet values or creates another relational store.</summary>
internal sealed class DataTableDesignPanel : Container, IDisposable
{
    private readonly Func<DataWorkbook?> _workbook;
    private readonly Func<DataTableDefinition?> _table;
    private readonly Action<DataWorkbook> _apply;
    private readonly IDataTableSchemaDesigner? _designer;
    private readonly Func<bool> _canReview;
    private readonly CancellationTokenSource _lifetime = new();
    private DataTableSchemaReview? _review;
    private bool _working;
    private readonly List<DataFieldDefinition> _fields = [];
    private readonly HashSet<Guid> _keyFields = [];
    private IReadOnlyList<DataKeyDefinition> _keys = [];
    private Guid? _tableID;
    private Guid _workbookID, _primaryKeyID;
    private long? _schemaRevision;
    private int _workbookVersion;
    private Guid _workbookRevision;
    private int _page;
    private int _generation;
    private bool _disposed;
    private readonly HavenText _status = new() { Name = "Data.Schema.Status", Level = TextLevel.Caption };
    private string _keyName = "Primary key";
    private readonly Dictionary<(Guid WorkbookID, Guid TableID), Draft> _retained = [];
    private sealed record Draft(long? SchemaRevision, int WorkbookVersion, Guid WorkbookRevision,
        IReadOnlyList<DataFieldDefinition> Fields, IReadOnlyList<DataKeyDefinition> Keys,
        IReadOnlySet<Guid> KeyFields, Guid PrimaryKeyID, string KeyName, int Page, string Status);

    public DataTableDesignPanel(Func<DataWorkbook?> workbook, Func<DataTableDefinition?> table, Action<DataWorkbook> apply, IDataTableSchemaDesigner? designer, Func<bool> canReview)
    {
        _workbook = workbook; _table = table; _apply = apply; _designer = designer; _canReview = canReview;
        Name = "Data.Schema.Design"; Layout = HavenLayout.Vertical;
        SetValue(HavenProperties.Gap, HavenLength.Px(8));
        SetValue(HavenProperties.Padding, HavenThickness.Parse("12px"));
        SetValue(HavenProperties.Background, "SurfaceRaised");
        Load(_table(), _workbook());
    }

    public void Refresh()
    {
        if (_disposed) return;
        var table = _table(); var workbook = _workbook();
        if (_tableID == table?.Id && _workbookID == workbook?.Id)
        {
            if (_schemaRevision != table?.RelationalSchema?.Revision || _workbookVersion != workbook?.Version
                || _workbookRevision != workbook?.RevisionId)
                _status.Content = "Workbook changed. Your schema draft is retained; reload to use the saved schema.";
            return;
        }
        if (_tableID is { } prior)
            _retained[(_workbookID, prior)] = new(_schemaRevision, _workbookVersion, _workbookRevision, _fields.ToArray(),
                _keys.ToArray(), _keyFields.ToHashSet(), _primaryKeyID, _keyName, _page, _status.Content);
        if (table is not null && workbook is not null && _retained.TryGetValue((workbook!.Id, table.Id), out var draft))
        {
            _review = null; _workbookID = workbook!.Id; _tableID = table.Id; _schemaRevision = draft.SchemaRevision; _workbookVersion = draft.WorkbookVersion;
            _workbookRevision = draft.WorkbookRevision; _fields.Clear(); _fields.AddRange(draft.Fields);
            _keys = draft.Keys; _keyFields.Clear(); _keyFields.UnionWith(draft.KeyFields);
            _primaryKeyID = draft.PrimaryKeyID; _keyName = draft.KeyName; _page = draft.Page; _status.Content = draft.Status; Build(); return;
        }
        Load(table, workbook);
    }
    private void Load(DataTableDefinition? table, DataWorkbook? workbook)
    {
        if (table is not null && workbook is not null) _retained.Remove((workbook!.Id, table.Id));
        _review = null;
        _workbookID = workbook?.Id ?? Guid.Empty; _primaryKeyID = Guid.NewGuid();
        _tableID = table?.Id; _schemaRevision = table?.RelationalSchema?.Revision;
        _workbookVersion = workbook?.Version ?? 0; _workbookRevision = workbook?.RevisionId ?? Guid.Empty;
        _fields.Clear(); _keyFields.Clear(); _keys = table?.RelationalSchema?.Keys ?? [];
        _keyName = "Primary key"; _page = 0;
        if (table is not null && workbook is not null && table.RecordIdentityVersion == 1)
        {
            var sheet = workbook.Sheets.Single(sheet => sheet.Id == table.SheetId);
            _fields.AddRange(table.RelationalSchema?.Fields ?? table.Fields.OrderBy(field => field.SheetColumn)
                .Select(field => new DataFieldDefinition(field.FieldID,
                    table.HasHeaders ? sheet.GetCell(table.Range.StartRow, field.SheetColumn)?.Value ?? $"Field {field.SheetColumn + 1}"
                        : $"Field {field.SheetColumn + 1}", InferType(sheet, table, field))).ToArray());
            if (_keys.SingleOrDefault(key => key.Kind == DataKeyKind.Primary) is { } primary)
            { _primaryKeyID = primary.KeyID; _keyFields.UnionWith(primary.FieldIDs); _keyName = primary.Name; }
        }
        _status.Content = ""; Build();
    }
    private void Build()
    {
        var generation = ++_generation;
        foreach (var child in Children.ToArray()) Remove(child);
        Add(new HavenText("Table Design") { Level = TextLevel.H3 });
        if (_tableID is null || _fields.Count == 0)
        {
            Add(new HavenText("Select a table with canonical record identities to edit its field definitions and keys.") { Level = TextLevel.Caption });
            return;
        }
        Add(new HavenText("Field names, types and required values are checked against the existing records before applying.") { Level = TextLevel.Caption });
        foreach (var (field, index) in _fields.Select((field, index) => (field, index)).Skip(_page * 64).Take(64))
        {
            var row = new Container { Name = $"Data.Schema.Field.{field.FieldID:N}", Layout = HavenLayout.Horizontal };
            row.SetValue(HavenProperties.Gap, HavenLength.Px(8));
            var name = Edit($"Data.Schema.Name.{field.FieldID:N}", "Field name", field.Name);
            name.TextChanged += (_, _) => { if (Current(generation)) _fields[index] = _fields[index] with { Name = name.Text }; };
            var type = Action($"Data.Schema.Type.{field.FieldID:N}", field.Type.ToString(), () =>
            {
                var kinds = Enum.GetValues<DataFieldType>(); var next = kinds[(Array.IndexOf(kinds, _fields[index].Type) + 1) % kinds.Length];
                _fields[index] = _fields[index] with { Type = next }; Build();
            }, generation);
            var required = new Toggle { Name = $"Data.Schema.Required.{field.FieldID:N}", IsChecked = !field.Nullable };
            required.Accessibility.AccessibleName = "Required: " + field.Name;
            required.CheckedChanged += (_, _) => { if (Current(generation)) _fields[index] = _fields[index] with { Nullable = !required.IsChecked }; };
            var key = new Toggle { Name = $"Data.Schema.Key.{field.FieldID:N}", IsChecked = _keyFields.Contains(field.FieldID) };
            key.Accessibility.AccessibleName = "Primary key member: " + field.Name;
            key.CheckedChanged += (_, _) => { if (!Current(generation)) return; if (key.IsChecked) _keyFields.Add(field.FieldID); else _keyFields.Remove(field.FieldID); };
            var defaultValue = Edit($"Data.Schema.Default.{field.FieldID:N}", "Default value", field.DefaultValue ?? "");
            defaultValue.TextChanged += (_, _) => { if (Current(generation)) _fields[index] = _fields[index] with { DefaultValue = defaultValue.Text.Length == 0 ? null : defaultValue.Text }; };
            row.Add(name); row.Add(type); row.Add(new HavenText("Required")); row.Add(required);
            row.Add(new HavenText("Primary key")); row.Add(key); row.Add(defaultValue); Add(row);
            if (field.Type == DataFieldType.Category)
            {
                var categories = Edit($"Data.Schema.Categories.{field.FieldID:N}", "Categories (one per line)", string.Join('\n', field.Categories ?? []));
                categories.Multiline = true;
                categories.TextChanged += (_, _) => { if (Current(generation)) _fields[index] = _fields[index] with { Categories = categories.Text.Split('\n') }; };
                Add(categories);
            }
        }
        var nameInput = Edit("Data.Schema.PrimaryKey.Name", "Primary key name", _keyName);
        nameInput.TextChanged += (_, _) => { if (Current(generation)) _keyName = nameInput.Text; }; Add(nameInput);
        BuildUniqueKeys(generation);
        var controls = new Container { Name = "Data.Schema.Actions", Layout = HavenLayout.Horizontal };
        controls.Add(Action("Data.Schema.Review", "Review schema change", () => _ = ReviewAsync(), generation));
        controls.Add(Action("Data.Schema.Apply", "Apply approved change", () => _ = CommitAsync(), generation));
        controls.Add(Action("Data.Schema.Reload", "Reload schema (discard draft)", () => Load(_table(), _workbook()), generation));
        if (_page > 0) controls.Add(Action("Data.Schema.Previous", "Previous fields", () => { _page--; Build(); }, generation));
        if ((_page + 1) * 64 < _fields.Count) controls.Add(Action("Data.Schema.Next", "Next fields", () => { _page++; Build(); }, generation));
        Add(controls); Add(_status);
    }
    private void BuildUniqueKeys(int generation)
    {
        Add(new HavenText("Unique keys") { Level = TextLevel.H3 });
        foreach (var key in _keys.Where(key => key.Kind == DataKeyKind.Unique))
        {
            var keyID = key.KeyID;
            var name = Edit($"Data.Schema.Unique.Name.{keyID:N}", "Unique key name", key.Name);
            name.TextChanged += (_, _) => { if (Current(generation)) { Replace(keyID, key with { Name = name.Text }); _review = null; } }; Add(name);
            var members = new Container { Layout = HavenLayout.Horizontal };
            foreach (var id in key.FieldIDs)
            {
                var field = _fields.SingleOrDefault(field => field.FieldID == id); var label = field?.Name ?? "Missing field";
                members.Add(new HavenText(label));
                members.Add(Action($"Data.Schema.Unique.Earlier.{keyID:N}.{id:N}", "Move " + label + " earlier", () =>
                {
                    var current = _keys.Single(item => item.KeyID == keyID); var order = current.FieldIDs.ToList(); var index = order.IndexOf(id);
                    if (index > 0) { (order[index - 1], order[index]) = (order[index], order[index - 1]); Replace(keyID, current with { FieldIDs = order.ToArray() }); _review = null; Build(); }
                }, generation));
            }
            Add(members);
            foreach (var field in _fields.Skip(_page * 64).Take(64))
            {
                var id = field.FieldID;
                var toggle = new Toggle { Name = $"Data.Schema.Unique.Member.{keyID:N}.{id:N}", IsChecked = key.FieldIDs.Contains(id) };
                toggle.Accessibility.AccessibleName = key.Name + " includes " + field.Name;
                toggle.CheckedChanged += (_, _) =>
                {
                    if (!Current(generation)) return;
                    var current = _keys.Single(item => item.KeyID == keyID); var order = current.FieldIDs.Where(item => item != id).ToList();
                    if (toggle.IsChecked) order.Add(id); Replace(keyID, current with { FieldIDs = order.ToArray() }); _review = null; Build();
                }; Add(new HavenText(field.Name)); Add(toggle);
            }
            Add(Action($"Data.Schema.Unique.Remove.{keyID:N}", "Remove " + key.Name, () =>
            { _keys = _keys.Where(item => item.KeyID != keyID).ToArray(); _review = null; Build(); }, generation));
        }
        Add(Action("Data.Schema.Unique.Add", "Add unique key", () =>
        {
            if (_keys.Count >= 256) { _status.Content = "This draft has reached the supported key limit."; return; }
            _keys = _keys.Append(new DataKeyDefinition(Guid.NewGuid(), "Unique key", DataKeyKind.Unique, [_fields[0].FieldID])).ToArray();
            _review = null; Build();
        }, generation));
        void Replace(Guid id, DataKeyDefinition replacement) => _keys = _keys.Select(item => item.KeyID == id ? replacement : item).ToArray();
    }
    private DataTableDesignResult Candidate()
    {
        if (_workbook() is not { } workbook || _tableID is not { } tableID)
            return new(null, [new("TableNotFound", Guid.Empty)]);
        var keys = _keys.Where(key => key.Kind != DataKeyKind.Primary).ToList();
        if (_keyFields.Count != 0)
        {
            var current = _keys.SingleOrDefault(key => key.Kind == DataKeyKind.Primary);
            var members = current is not null && current.FieldIDs.ToHashSet().SetEquals(_keyFields)
                ? current.FieldIDs : _fields.Where(field => _keyFields.Contains(field.FieldID)).Select(field => field.FieldID).ToArray();
            keys.Add(new(current?.KeyID ?? _primaryKeyID, _keyName, DataKeyKind.Primary, members));
        }
        return DataTableDesign.SetSchema(workbook, tableID, _workbookVersion, _workbookRevision, _schemaRevision, _fields, keys);
    }
    private async Task ReviewAsync()
    {
        if (_disposed || _working) return;
        if (_designer is null) { _status.Content = "Schema review is unavailable in this session."; return; }
        if (!_canReview()) { _status.Content = "Save or reload workbook edits before reviewing a schema change."; return; }
        var edited = Candidate();
        if (!edited.Success) { _status.Content = "Schema not reviewed: " + string.Join(", ", edited.Issues.Take(5).Select(issue => issue.Code)); return; }
        var workbook = edited.Workbook!; var tableID = _tableID!.Value; var generation = _generation; var workbookID = workbook.Id;
        var schema = workbook.Tables.Single(table => table.Id == tableID).RelationalSchema!;
        _working = true; SetValue(HavenProperties.Enabled, false);
        try
        {
            var review = await _designer.ReviewAsync(workbook.Id, tableID, _workbookVersion, _workbookRevision,
                _schemaRevision, schema.Fields, schema.Keys, _lifetime.Token);
            if (!SameSession(generation, workbookID, tableID)) return;
            _review = review;
            // Retain generated key identity so unchanged drafts match the exact reviewed proposal.
            _keys = schema.Keys;
            _status.Content = "Review requested in Home: " + review.RequestID + ". Apply after approving that exact change.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or NotSupportedException or KeyNotFoundException or OperationCanceledException)
        { if (SameSession(generation, workbookID, tableID)) _status.Content = "Schema review failed; draft retained: " + error.Message; }
        finally { _working = false; if (!_disposed) SetValue(HavenProperties.Enabled, true); }
    }
    private async Task CommitAsync()
    {
        if (_disposed || _working) return;
        if (_designer is null || _review is null) { _status.Content = "Request and approve a schema review first."; return; }
        if (!_canReview()) { _status.Content = "Workbook edits changed; save or reload before applying the reviewed schema."; return; }
        var edited = Candidate();
        if (!edited.Success || JsonSerializer.Serialize(edited.Workbook!.Tables.Single(table => table.Id == _tableID).RelationalSchema)
            != JsonSerializer.Serialize(_review.Intent.Schema))
        { _status.Content = "The draft changed after review. Request a new review; your draft is retained."; return; }
        var review = _review; var generation = _generation; var workbookID = _workbookID; var tableID = _tableID!.Value; _working = true; SetValue(HavenProperties.Enabled, false);
        try
        {
            var result = await _designer.CommitAsync(review, _lifetime.Token);
            if (!SameSession(generation, workbookID, tableID)) return;
            if (result.Committed)
            {
                if (result.Workbook is not null && _workbook()?.Id == result.Workbook.Id && _canReview())
                { _apply(result.Workbook); Load(_table(), _workbook()); }
                _review = null; _status.Content = result.Code;
            }
            else _status.Content = "Schema not applied: " + result.Code + ". Draft retained.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or NotSupportedException or KeyNotFoundException or OperationCanceledException)
        { if (SameSession(generation, workbookID, tableID)) _status.Content = "Schema result unavailable; draft retained. Reload to inspect saved state: " + error.Message; }
        finally { _working = false; if (!_disposed) SetValue(HavenProperties.Enabled, true); }
    }
    private static DataFieldType InferType(DataSheet sheet, DataTableDefinition table, DataTableField field)
    {
        var cell = table.Records.Select(record => sheet.GetCell(record.SheetRow, field.SheetColumn))
            .FirstOrDefault(cell => cell is not null && cell.Value.Length != 0);
        return cell?.Kind switch
        {
            DataCellKind.Number => DataFieldType.Decimal,
            DataCellKind.Boolean => DataFieldType.Boolean,
            DataCellKind.Date => cell.Value.Contains('T') ? DataFieldType.DateTime : DataFieldType.Date,
            _ => DataFieldType.Text
        };
    }
    private bool SameSession(int generation, Guid workbookID, Guid tableID) => !_disposed && generation == _generation
        && _workbookID == workbookID && _tableID == tableID && _workbook()?.Id == workbookID && _table()?.Id == tableID
        && _workbook()?.Version == _workbookVersion && _workbook()?.RevisionId == _workbookRevision;
    private bool Current(int generation) => !_disposed && !_working && generation == _generation;
    private HavenButton Action(string name, string label, Action action, int generation)
    {
        var button = new HavenButton { Name = name, Content = label, Variant = ButtonVariant.Tertiary };
        button.Accessibility.AccessibleName = label;
        button.Invoked += (_, _) => { if (Current(generation)) action(); }; return button;
    }
    private static Input Edit(string name, string label, string text)
    {
        var input = new Input { Name = name, Text = text, Placeholder = label }; input.Accessibility.AccessibleName = label; return input;
    }
    public void Dispose()
    {
        _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); _review = null; _generation++; foreach (var child in Children.ToArray()) Remove(child);
        _fields.Clear(); _keyFields.Clear(); _retained.Clear(); SetValue(HavenProperties.Enabled, false);
    }
}


internal sealed class DataRelationshipDesignPanel : Container, IDisposable
{
    private readonly Func<DataWorkbook?> _workbook;
    private readonly Action<DataWorkbook> _apply;
    private readonly IDataRelationshipDesigner? _designer;
    private readonly Func<bool> _canReview;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HavenText _status = new() { Name = "Data.Relationship.Status", Level = TextLevel.Caption };
    private readonly NodeEditor _editor = new() { Name = "Data.Relationship.Graph", TopologyPolicy = NodeEditorTopologyPolicy.RelationalAuthoring };
    private DataWorkbook? _snapshot;
    private DataRelationshipDefinition? _draft;
    private long? _expectedRelationshipRevision;
    private DataRelationshipMutationKind _kind;
    private DataRelationshipReview? _review;
    private bool _working, _disposed, _projecting;
    private int _generation;
    private readonly Dictionary<Guid, Retained> _retained = [];
    private sealed record Retained(DataWorkbook Snapshot, DataRelationshipDefinition? Draft, long? ExpectedRevision,
        DataRelationshipMutationKind Kind, NodeEditorDocument Document, string Status);
    public DataRelationshipDesignPanel(Func<DataWorkbook?> workbook, Action<DataWorkbook> apply,
        IDataRelationshipDesigner? designer, Func<bool> canReview)
    {
        _workbook = workbook; _apply = apply; _designer = designer; _canReview = canReview;
        Name = "Data.Relationship.Design"; Layout = HavenLayout.Vertical;
        SetValue(HavenProperties.Gap, HavenLength.Px(8));
        _editor.SetValue(HavenProperties.Height, HavenLength.Px(360));
        _editor.DocumentChanged += GraphChanged;
        Reload();
    }
    public void Refresh()
    {
        if (_disposed) return;
        var current = _workbook();
        if (current?.Id != _snapshot?.Id)
        {
            if (_snapshot is { } prior) _retained[prior.Id] = new(prior, _draft, _expectedRelationshipRevision, _kind, _editor.Document, _status.Content);
            _review = null;
            if (current is not null && _retained.TryGetValue(current.Id, out var retained))
            {
                _snapshot = retained.Snapshot; _draft = retained.Draft; _expectedRelationshipRevision = retained.ExpectedRevision;
                _kind = retained.Kind; _editor.Document = retained.Document; _status.Content = retained.Status; Build(false);
            }
            else Reload();
        }
        if (current?.Version != _snapshot?.Version || current?.RevisionId != _snapshot?.RevisionId)
            _status.Content = "Workbook changed. Your relationship draft is retained; reload to use saved relationships.";
    }
    private void Reload()
    {
        if (_workbook() is { } current) _retained.Remove(current.Id);
        _review = null; _draft = null; _expectedRelationshipRevision = null;
        _snapshot = _workbook() is { } workbook
            ? JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.Serialize(workbook)) : null;
        _status.Content = "Select a relationship or connect a field to a key, then review the change.";
        Build(true);
    }
    private void Build(bool resetGraph)
    {
        _generation++; var generation = _generation;
        foreach (var child in Children.ToArray()) Remove(child);
        Add(new HavenText("Relationships") { Level = TextLevel.H2 });
        if (_snapshot is null) { Add(new HavenText("Open a workbook to design relationships.")); return; }
        if (resetGraph)
        {
            _projecting = true;
            try { var projected = DataRelationshipEditorAdapter.Capture(_snapshot); _editor.Document = projected.Document; }
            finally { _projecting = false; }
        }
        Add(_editor);
        var choose = new Select { Name = "Data.Relationship.Select",
            Items = new[] { "New relationship" }.Concat(_snapshot.Relationships.Select(item => item.Name)).ToArray(),
            SelectedIndex = _draft is null ? 0 : _snapshot.Relationships.FindIndex(item => item.RelationshipID == _draft.RelationshipID) + 1 };
        choose.SelectionChanged += (_, _) =>
        {
            if (!Current(generation)) return;
            _review = null; _kind = DataRelationshipMutationKind.Upsert;
            _draft = choose.SelectedIndex <= 0 ? null : _snapshot.Relationships[choose.SelectedIndex - 1];
            _expectedRelationshipRevision = _draft?.Revision;
            Build(false);
        };
        Add(choose);
        if (_draft is null)
        {
            var source = _snapshot.Tables.FirstOrDefault(table => table.RelationalSchema?.Fields.Count > 0);
            var target = _snapshot.Tables.FirstOrDefault(table => table.RelationalSchema?.Keys.Count > 0);
            if (source is not null && target is not null)
                _draft = new(Guid.NewGuid(), "New relationship", source.Id, [source.RelationalSchema!.Fields[0].FieldID],
                    target.Id, target.RelationalSchema!.Keys[0].KeyID, DataRelationshipCardinality.OneToMany, true, 0);
        }
        if (_draft is not null)
        {
            var name = new Input { Name = "Data.Relationship.Name", Text = _draft.Name, Placeholder = "Relationship name" };
            name.TextChanged += (_, _) => { if (Current(generation)) { _draft = _draft! with { Name = name.Text }; _review = null; } };
            Add(name);
            var tables = _snapshot.Tables.Where(table => table.RelationalSchema is not null).ToArray();
            var source = new Select { Name = "Data.Relationship.SourceTable", Items = tables.Select(table => table.Name).ToArray(),
                SelectedIndex = Array.FindIndex(tables, table => table.Id == _draft.SourceTableID) };
            source.SelectionChanged += (_, _) =>
            {
                if (!Current(generation) || source.SelectedIndex < 0) return;
                var table = tables[source.SelectedIndex];
                _draft = _draft! with { SourceTableID = table.Id, SourceFieldIDs = [] }; _review = null; Build(false);
            };
            Add(new HavenText("Referencing table and fields (select in key order)")); Add(source);
            var sourceTable = tables.FirstOrDefault(table => table.Id == _draft.SourceTableID);
            foreach (var field in sourceTable?.RelationalSchema?.Fields ?? [])
            {
                var toggle = new Toggle { Name = $"Data.Relationship.Field.{field.FieldID:N}", IsChecked = _draft.SourceFieldIDs.Contains(field.FieldID) };
                toggle.Accessibility.AccessibleName = field.Name;
                toggle.CheckedChanged += (_, _) =>
                {
                    if (!Current(generation)) return;
                    var members = _draft!.SourceFieldIDs.Where(id => id != field.FieldID).ToList();
                    if (toggle.IsChecked) members.Add(field.FieldID);
                    _draft = _draft with { SourceFieldIDs = members }; _review = null;
                };
                var row = new Container { Layout = HavenLayout.Horizontal }; row.Add(new HavenText(field.Name)); row.Add(toggle); Add(row);
            }
            var targets = tables.Where(table => table.RelationalSchema!.Keys.Count != 0).ToArray();
            var target = new Select { Name = "Data.Relationship.TargetTable", Items = targets.Select(table => table.Name).ToArray(),
                SelectedIndex = Array.FindIndex(targets, table => table.Id == _draft.TargetTableID) };
            target.SelectionChanged += (_, _) =>
            {
                if (!Current(generation) || target.SelectedIndex < 0) return;
                var table = targets[target.SelectedIndex]; _draft = _draft! with { TargetTableID = table.Id, TargetKeyID = table.RelationalSchema!.Keys[0].KeyID };
                _review = null; Build(false);
            };
            Add(new HavenText("Referenced table and key")); Add(target);
            var keys = targets.FirstOrDefault(table => table.Id == _draft.TargetTableID)?.RelationalSchema?.Keys.ToArray() ?? [];
            var key = new Select { Name = "Data.Relationship.TargetKey", Items = keys.Select(item => item.Name).ToArray(),
                SelectedIndex = Array.FindIndex(keys, item => item.KeyID == _draft.TargetKeyID) };
            key.SelectionChanged += (_, _) => { if (Current(generation) && key.SelectedIndex >= 0) { _draft = _draft! with { TargetKeyID = keys[key.SelectedIndex].KeyID }; _review = null; } };
            Add(key);
            var optional = new Toggle { Name = "Data.Relationship.Optional", IsChecked = _draft.Optional };
            optional.Accessibility.AccessibleName = "Optional reference";
            optional.CheckedChanged += (_, _) => { if (Current(generation)) { _draft = _draft! with { Optional = optional.IsChecked }; _review = null; } };
            Add(new HavenText("Optional reference")); Add(optional);
            Add(Button("Data.Relationship.Cardinality", _draft.Cardinality == DataRelationshipCardinality.OneToOne ? "One to one" : "Many references to one key", () =>
            { _draft = _draft! with { Cardinality = _draft.Cardinality == DataRelationshipCardinality.OneToOne ? DataRelationshipCardinality.OneToMany : DataRelationshipCardinality.OneToOne }; _review = null; Build(false); }, generation));
        }
        var actions = new Container { Layout = HavenLayout.Horizontal };
        actions.Add(Button("Data.Relationship.Review", "Review relationship change", () => { _kind = DataRelationshipMutationKind.Upsert; _ = ReviewAsync(); }, generation));
        actions.Add(Button("Data.Relationship.Remove", "Review removal", () => { _kind = DataRelationshipMutationKind.Remove; _ = ReviewAsync(); }, generation));
        actions.Add(Button("Data.Relationship.Apply", "Apply approved change", () => _ = CommitAsync(), generation));
        actions.Add(Button("Data.Relationship.Reload", "Reload relationships (discard draft)", Reload, generation));
        Add(actions); Add(_status);
    }
    private void GraphChanged(NodeEditorDocument document)
    {
        if (_projecting || _disposed || _working || _snapshot is null) return;
        var added = document.Edges.Where(edge => _snapshot.Relationships.All(item => item.RelationshipID != edge.Id)).ToArray();
        var removed = _snapshot.Relationships.Where(item => document.Edges.All(edge => edge.Id != item.RelationshipID)).ToArray();
        if (added.Length == 1 && removed.Length == 0)
        {
            var edge = added[0];
            if (!Guid.TryParse(edge.FromPortId, out var fieldID) || !Guid.TryParse(edge.ToPortId, out var keyID)) return;
            var fields = _snapshot.Relationships.FirstOrDefault(item => item.RelationshipID == fieldID && item.SourceTableID == edge.FromNodeId)?.SourceFieldIDs ?? [fieldID];
            _draft = new(edge.Id, "New relationship", edge.FromNodeId, fields.ToArray(), edge.ToNodeId, keyID,
                DataRelationshipCardinality.OneToMany, true, 0);
            _expectedRelationshipRevision = null; _kind = DataRelationshipMutationKind.Upsert; _review = null; Build(false);
        }
        else if (added.Length == 0 && removed.Length == 1)
        { _draft = removed[0]; _expectedRelationshipRevision = _draft.Revision; _kind = DataRelationshipMutationKind.Remove; _review = null; Build(false); }
        else if (added.Length + removed.Length > 1)
            _status.Content = "Review one relationship change at a time. Reload to restore saved connections.";
    }
    private DataTableDesignResult Candidate() => _workbook() is not { } workbook || _snapshot is null || _draft is null
        ? new(null, [new("RelationshipRequired", Guid.Empty)])
        : _kind == DataRelationshipMutationKind.Remove
            ? _expectedRelationshipRevision is { } revision
                ? DataTableDesign.RemoveRelationship(workbook, _snapshot.Version, _snapshot.RevisionId, _draft.RelationshipID, revision)
                : new(null, [new("RelationshipNotSaved", _draft.SourceTableID)])
            : DataTableDesign.SetRelationship(workbook, _snapshot.Version, _snapshot.RevisionId, _expectedRelationshipRevision, _draft);
    private async Task ReviewAsync()
    {
        if (_disposed || _working) return;
        if (_designer is null) { _status.Content = "Relationship review is unavailable in this session."; return; }
        if (!_canReview()) { _status.Content = "Save or reload workbook edits before reviewing a relationship change."; return; }
        var candidate = Candidate();
        if (!candidate.Success) { _status.Content = "Relationship not reviewed: " + string.Join(", ", candidate.Issues.Take(5).Select(issue => issue.Code)) + ". Draft retained."; return; }
        var generation = _generation; var snapshot = _snapshot!; var draft = _draft!;
        var kind = _kind; var expectedRevision = _expectedRelationshipRevision;
        _working = true; SetValue(HavenProperties.Enabled, false);
        try
        {
            var review = await _designer.ReviewAsync(snapshot.Id, snapshot.Version, snapshot.RevisionId,
                kind, draft, expectedRevision, _lifetime.Token);
            if (!_disposed && generation == _generation && _snapshot?.Id == snapshot.Id) { _review = review; _status.Content = "Review requested in Home. Approve that change, then apply it here."; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or KeyNotFoundException or OperationCanceledException)
        { if (!_disposed && generation == _generation && _snapshot?.Id == snapshot.Id) _status.Content = "Review failed; draft retained: " + error.Message; }
        finally { _working = false; if (!_disposed) SetValue(HavenProperties.Enabled, true); }
    }
    private async Task CommitAsync()
    {
        if (_disposed || _working) return;
        if (_designer is null || _review is null) { _status.Content = "Request and approve a relationship review first."; return; }
        if (!_canReview()) { _status.Content = "Save or reload workbook edits before applying the reviewed relationship."; return; }
        var candidate = Candidate();
        var exact = _kind == DataRelationshipMutationKind.Remove ? _snapshot!.Relationships.FirstOrDefault(item => item.RelationshipID == _draft!.RelationshipID)
            : candidate.Workbook?.Relationships.FirstOrDefault(item => item.RelationshipID == _draft!.RelationshipID);
        if (!candidate.Success || _review.Intent.Kind != _kind || JsonSerializer.Serialize(exact) != JsonSerializer.Serialize(_review.Intent.Relationship))
        { _status.Content = "The draft changed after review. Request a new review; your draft is retained."; return; }
        var generation = _generation; var review = _review; var workbookID = _snapshot!.Id;
        _working = true; SetValue(HavenProperties.Enabled, false);
        try
        {
            var result = await _designer.CommitAsync(review, _lifetime.Token);
            if (_disposed || generation != _generation || _snapshot?.Id != workbookID) return;
            if (result.Committed)
            {
                if (result.Workbook is not null && _workbook()?.Id == result.Workbook.Id && _canReview()) { _apply(result.Workbook); Reload(); }
                _review = null; _status.Content = result.AuditRecorded ? "Relationship change saved." : "Relationship change saved; Home is reconciling its activity record.";
            }
            else _status.Content = "Relationship not applied: " + result.Code + ". Draft retained.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or KeyNotFoundException or OperationCanceledException)
        { if (!_disposed && generation == _generation && _snapshot?.Id == workbookID) _status.Content = "Result unavailable; draft retained. Reload to inspect saved relationships: " + error.Message; }
        finally { _working = false; if (!_disposed) SetValue(HavenProperties.Enabled, true); }
    }
    private bool Current(int generation) => !_disposed && !_working && generation == _generation;
    private HavenButton Button(string name, string label, Action action, int generation)
    {
        var button = new HavenButton { Name = name, Content = label, Variant = ButtonVariant.Tertiary };
        button.Accessibility.AccessibleName = label; button.Invoked += (_, _) => { if (Current(generation)) action(); }; return button;
    }
    public void Dispose()
    {
        _disposed = true; _generation++; _editor.DocumentChanged -= GraphChanged; _lifetime.Cancel(); _lifetime.Dispose(); _review = null;
        foreach (var child in Children.ToArray()) Remove(child); _retained.Clear(); SetValue(HavenProperties.Enabled, false);
    }
}

internal sealed class DataRecordCreationPanel : Container, IDisposable
{
    private readonly Func<DataWorkbook?> _workbook;
    private readonly Action<DataWorkbook, DataFormulaRecalculationReport?> _apply;
    private readonly IDataRecordCreator? _creator;
    private readonly Func<IDataRecordDisplaySelection?> _selection;
    private readonly Func<bool> _canReview;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HavenText _status = new() { Name = "Data.RecordCreate.Status", Level = TextLevel.Caption };
    private DataWorkbook? _snapshot;
    private Guid _tableID, _recordID;
    private readonly Dictionary<Guid, DataRecordDraftField> _fields = [];
    private DataRecordCreateReview? _review;
    private int _generation;
    private bool _working, _disposed;
    private readonly Dictionary<(Guid WorkbookID, Guid TableID), Retained> _retained = [];
    private readonly Dictionary<Guid, Guid> _selectedTables = [];
    private sealed record Retained(DataWorkbook Snapshot, Guid TableID, Guid RecordID, DataRecordDraftField[] Fields, string Status);
    public DataRecordCreationPanel(Func<DataWorkbook?> workbook, Action<DataWorkbook, DataFormulaRecalculationReport?> apply,
        IDataRecordCreator? creator, Func<bool> canReview, Func<IDataRecordDisplaySelection?> selection)
    {
        _workbook = workbook; _apply = apply; _creator = creator; _canReview = canReview; _selection = selection;
        Name = "Data.RecordCreate.Design"; Layout = HavenLayout.Vertical; SetValue(HavenProperties.Gap, HavenLength.Px(8)); Reload();
    }
    public void Refresh()
    {
        if (_disposed) return; var current = _workbook();
        if (current?.Id != _snapshot?.Id)
        {
            RetainDraft();
            _review = null;
            if (current is not null && _selectedTables.TryGetValue(current.Id, out var selected) && _retained.TryGetValue((current.Id, selected), out var retained))
            { _snapshot = retained.Snapshot; _tableID = retained.TableID; _recordID = retained.RecordID; _fields.Clear(); foreach (var field in retained.Fields) _fields.Add(field.FieldID, field); _status.Content = retained.Status; Build(); }
            else Reload();
        }
        if (current?.Version != _snapshot?.Version || current?.RevisionId != _snapshot?.RevisionId)
            _status.Content = "Workbook changed. Your record creation draft is retained; reload to use saved schemas.";
    }
    private void Reload()
    {
        if (_workbook() is { } current) _retained.Remove((current.Id, _tableID));
        var selectedTable = _tableID;
        _review = null; _recordID = Guid.NewGuid();
        _snapshot = _workbook() is { } workbook ? JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.Serialize(workbook)) : null;
        _tableID = _snapshot?.Tables.FirstOrDefault(table => table.RecordIdentityVersion == 1 && table.Id == selectedTable)?.Id
            ?? _snapshot?.Tables.FirstOrDefault(table => table.RecordIdentityVersion == 1)?.Id ?? Guid.Empty;
        if (_snapshot is not null) _selectedTables[_snapshot.Id] = _tableID;
        ResetFields(); _status.Content = "Enter a complete record. Omitted fields use authored defaults when available."; Build();
    }
    private void ResetFields()
    {
        _fields.Clear(); var table = _snapshot?.Tables.SingleOrDefault(table => table.Id == _tableID);
        if (table is null) return;
        foreach (var field in table.Fields)
        {
            var authored = table.RelationalSchema?.Fields.SingleOrDefault(item => item.FieldID == field.FieldID);
            _fields.Add(field.FieldID, new(field.FieldID, authored is null || (!authored.Nullable && string.IsNullOrEmpty(authored.DefaultValue)), ""));
        }
    }
    private void Build()
    {
        _generation++; var generation = _generation;
        foreach (var child in Children.ToArray()) Remove(child);
        Add(new HavenText("Create record") { Level = TextLevel.H2 });
        var table = _snapshot?.Tables.SingleOrDefault(table => table.Id == _tableID);
        if (_snapshot is null || table is null) { Add(new HavenText("Create or select a table before creating a record.")); return; }
        var tables = _snapshot.Tables.Where(item => item.RecordIdentityVersion == 1).ToArray();
        var tableSelect = new Select { Name = "Data.RecordCreate.Table", Items = tables.Select(item => item.Name).ToArray(),
            SelectedIndex = Array.FindIndex(tables, item => item.Id == _tableID) };
        tableSelect.Accessibility.AccessibleName = "Record table";
        tableSelect.SelectionChanged += (_, _) =>
        {
            if (!Current(generation) || tableSelect.SelectedIndex < 0) return;
            RetainDraft(); _tableID = tables[tableSelect.SelectedIndex].Id; _review = null;
            _selectedTables[_snapshot!.Id] = _tableID;
            if (_retained.TryGetValue((_snapshot.Id, _tableID), out var retained))
            { _snapshot = retained.Snapshot; _recordID = retained.RecordID; _fields.Clear(); foreach (var value in retained.Fields) _fields.Add(value.FieldID, value); _status.Content = retained.Status; }
            else { _recordID = Guid.NewGuid(); ResetFields(); _status.Content = "Enter a complete record. Omitted fields use authored defaults when available."; }
            Build();
        }; Add(tableSelect);
        foreach (var field in table.Fields.OrderBy(field => field.SheetColumn))
        {
            var id = field.FieldID; var draft = _fields[id];
            var authored = table.RelationalSchema?.Fields.SingleOrDefault(item => item.FieldID == id);
            var label = authored?.Name ?? _snapshot.Sheets.Single(sheet => sheet.Id == table.SheetId).GetCell(table.Range.StartRow, field.SheetColumn)?.Value ?? "Field";
            Add(new HavenText(label + " · " + (authored?.Type.ToString() ?? "Text")));
            Add(new HavenText("Enter value (leave off to use a default)") { Level = TextLevel.Caption });
            var supplied = new Toggle { Name = $"Data.RecordCreate.Supply.{id:N}", IsChecked = draft.Supplied };
            supplied.Accessibility.AccessibleName = "Enter a value for " + label;
            supplied.CheckedChanged += (_, _) => { if (Current(generation)) { _fields[id] = _fields[id] with { Supplied = supplied.IsChecked }; _review = null; } }; Add(supplied);
            var value = new Input { Name = $"Data.RecordCreate.Value.{id:N}", Text = draft.Text,
                Placeholder = authored?.DefaultValue is { Length: > 0 } defaultValue ? "Default when omitted: " + defaultValue : "Value" };
            value.Accessibility.AccessibleName = label + " value";
            value.TextChanged += (_, _) =>
            {
                if (!Current(generation)) return;
                _fields[id] = _fields[id] with { Text = value.Text, Supplied = true };
                supplied.IsChecked = true; _review = null;
            }; Add(value);
        }
        var actions = new Container { Layout = HavenLayout.Horizontal };
        actions.Add(Button("Data.RecordCreate.Review", "Review record", () => _ = ReviewAsync(), generation));
        actions.Add(Button("Data.RecordCreate.Apply", "Create approved record", () => _ = CommitAsync(), generation));
        actions.Add(Button("Data.RecordCreate.Reload", "Reload (discard draft)", Reload, generation)); Add(actions); Add(_status);
    }
    private void RetainDraft()
    {
        if (_snapshot is not { } snapshot) return;
        _retained[(snapshot.Id, _tableID)] = new(snapshot, _tableID, _recordID, _fields.Values.ToArray(), _status.Content);
        _selectedTables[snapshot.Id] = _tableID;
    }
    private DataRecordDraftCapture Values() => _snapshot?.Tables.SingleOrDefault(table => table.Id == _tableID) is { } table
        ? DataRecordCreationDraft.Capture(table, _fields.Values.ToArray()) : new(null, [new("TableNotFound", _tableID)]);
    private DataTableDesignResult Candidate(IReadOnlyDictionary<Guid, DataScalarRecordValue> values) => _workbook() is { } current && _snapshot is not null
        ? DataRecordCreation.Prepare(current, _tableID, _recordID, _snapshot.Version, _snapshot.RevisionId, values)
        : new(null, [new("RecordDefinitionRequired", _tableID)]);
    private async Task ReviewAsync()
    {
        if (_disposed || _working) return;
        if (_creator is null) { _status.Content = "Record creation review is unavailable in this session."; return; }
        if (!_canReview()) { _status.Content = "Save or reload workbook edits before reviewing a record."; return; }
        var values = Values();
        if (!values.Success) { _status.Content = "Check values for: " + string.Join(", ", values.Issues.Select(issue => FieldLabel(issue.FieldID)).Distinct()) + ". Draft retained."; return; }
        var candidate = Candidate(values.Values!);
        if (!candidate.Success) { _status.Content = "Record not reviewed: " + string.Join(", ", candidate.Issues.Take(5).Select(issue => FieldLabel(issue.FieldID) + ": " + issue.Code)) + ". Draft retained."; return; }
        var snapshot = _snapshot!; var tableID = _tableID; var recordID = _recordID; var generation = _generation;
        _working = true; SetValue(HavenProperties.Enabled, false);
        try
        {
            var review = await _creator.ReviewAsync(_selection() ?? throw new UnauthorizedAccessException("Reload the owning workbook before review."), snapshot.Id, tableID, recordID, snapshot.Version, snapshot.RevisionId, values.Values!, _lifetime.Token);
            if (SameSession(generation, snapshot.Id)) { _review = review; var defaults = review.Intent.Arguments.GetProperty("actualValues").EnumerateArray().Count(value => value.GetProperty("defaultApplied").GetBoolean());
                var calculation = review.Intent.Calculation;
                _status.Content = $"Review requested in Home: one record, {defaults} authored defaults, {calculation.ChangedCells} changed formula results. Approve that change, then create the record here."; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or KeyNotFoundException or OperationCanceledException)
        { if (SameSession(generation, snapshot.Id)) _status.Content = "Review failed; draft retained: " + error.Message; }
        finally { _working = false; if (!_disposed) SetValue(HavenProperties.Enabled, true); }
    }
    private async Task CommitAsync()
    {
        if (_disposed || _working) return;
        if (_creator is null || _review is null) { _status.Content = "Request and approve a record creation review first."; return; }
        if (!_canReview()) { _status.Content = "Save or reload workbook edits before creating the reviewed record."; return; }
        var values = Values();
        if (!values.Success) { _status.Content = "Check values for: " + string.Join(", ", values.Issues.Select(issue => FieldLabel(issue.FieldID)).Distinct()) + ". Draft retained."; return; }
        var candidate = Candidate(values.Values!);
        if (!candidate.Success || _review.Intent.TableID != _tableID || _review.Intent.RecordID != _recordID
            || JsonSerializer.Serialize(values.Values!.OrderBy(pair => pair.Key)) != JsonSerializer.Serialize(_review.Intent.Values.OrderBy(pair => pair.Key)))
        { _status.Content = "The draft changed after review. Request a new review; your draft is retained."; return; }
        var review = _review; var workbookID = _snapshot!.Id; var generation = _generation;
        _working = true; SetValue(HavenProperties.Enabled, false);
        try
        {
            var result = await _creator.CommitAsync(review, _lifetime.Token);
            if (!SameSession(generation, workbookID)) return;
            if (result.Committed)
            {
                if (result.Workbook is not null && _workbook()?.Id == result.Workbook.Id && _canReview()) { _apply(result.Workbook, result.Calculation); Reload(); }
                _review = null; _status.Content = result.AuditRecorded ? "Record created." : "Record created; activity confirmation is pending.";
            }
            else _status.Content = "Record not created: " + result.Code + ". Draft retained.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or KeyNotFoundException or OperationCanceledException)
        { if (SameSession(generation, workbookID)) _status.Content = "Result unavailable; draft retained. Reload to inspect saved records: " + error.Message; }
        finally { _working = false; if (!_disposed) SetValue(HavenProperties.Enabled, true); }
    }
    private string FieldLabel(Guid? id)
    {
        var table = _snapshot?.Tables.SingleOrDefault(table => table.Id == _tableID);
        if (table is null || id is null) return "record fields";
        if (table.RelationalSchema?.Fields.SingleOrDefault(field => field.FieldID == id) is { } authored) return authored.Name;
        var field = table.Fields.SingleOrDefault(field => field.FieldID == id);
        return field is null ? "record fields" : _snapshot!.Sheets.Single(sheet => sheet.Id == table.SheetId)
            .GetCell(table.Range.StartRow, field.SheetColumn)?.Value ?? "field";
    }
    private bool Current(int generation) => !_disposed && !_working && generation == _generation;
    private bool SameSession(int generation, Guid workbookID) => !_disposed && generation == _generation && _snapshot?.Id == workbookID && _workbook()?.Id == workbookID
        && _workbook()?.Version == _snapshot?.Version && _workbook()?.RevisionId == _snapshot?.RevisionId;
    private HavenButton Button(string name, string label, Action action, int generation)
    {
        var button = new HavenButton { Name = name, Content = label, Variant = ButtonVariant.Tertiary };
        button.Accessibility.AccessibleName = label; button.Invoked += (_, _) => { if (Current(generation)) action(); }; return button;
    }
    public void Dispose()
    {
        _disposed = true; _generation++; _lifetime.Cancel(); _lifetime.Dispose(); _review = null; _retained.Clear(); _selectedTables.Clear();
        foreach (var child in Children.ToArray()) Remove(child); SetValue(HavenProperties.Enabled, false);
    }
}
