using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core.Forms;
using Haven.Core.Mathematics;

namespace HavenOS.Forms;

/// <summary>Owning authoring surface over canonical revisioned Forms services. Host availability
/// controls affordances only; every persisted operation still checks the store's actual authority.</summary>
public sealed class FormsCuiWorkspace(FormPublicationService publications, FormAuthoringService authoring,
    Func<Guid?> selectedForm, Func<string, bool> available,
    Func<FormNativePreview, CancellationToken, Task>? showPreview,
    FormResponseSessionService? responseSessions,
    Func<FormNativeResponseSurface, CancellationToken, Task>? showResponse,
    IFormDataReferenceLookupSource? referenceLookup,
    IFormNativeMathematicsProvider? mathematics = null,
    Func<FormField, CancellationToken, Task<FormField?>>? showMathematicsEditor = null) : ICuiWritableBindingContext,
    ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    public FormsCuiWorkspace(FormPublicationService publications, FormAuthoringService authoring,
        Func<Guid?> selectedForm, Func<string, bool> available,
        Func<FormNativePreview, CancellationToken, Task>? showPreview = null,
        FormResponseSessionService? responseSessions = null,
        Func<FormNativeResponseSurface, CancellationToken, Task>? showResponse = null)
        : this(publications, authoring, selectedForm, available, showPreview, responseSessions, showResponse, null) { }

    private readonly Dictionary<Guid, Guid> _responseIDs = [];
    private FormPublication? _opened;
    private FormProject? _project;
    private Guid? _pageID, _fieldID, _optionID, _columnID, _fixedRowID;
    private int _paletteIndex;
    private int _columnTypeIndex;
    private static readonly FormTableCellType[] ColumnTypes = [FormTableCellType.Text, FormTableCellType.Number, FormTableCellType.Boolean, FormTableCellType.Date];
    private readonly Dictionary<Guid, (string Minimum, string Maximum)> _tableDrafts = [];
    private readonly Dictionary<Guid, string> _columnDrafts = [];
    private readonly Dictionary<Guid, string> _optionDrafts = [];
    private readonly Dictionary<Guid, (string Label, string Help)> _fieldDrafts = [];
    private bool HasDirtyInspector => _optionDrafts.Count != 0 || _fieldDrafts.Count != 0 || _columnDrafts.Count != 0 || _tableDrafts.Count != 0;
    private static readonly (FormFieldKind Kind, string Label)[] Palette =
    [
        (FormFieldKind.ShortText, "Short text"), (FormFieldKind.LongText, "Long text"),
        (FormFieldKind.Number, "Number"), (FormFieldKind.Decimal, "Decimal"), (FormFieldKind.Currency, "Currency"),
        (FormFieldKind.Email, "Email"), (FormFieldKind.Phone, "Phone"), (FormFieldKind.Date, "Date"),
        (FormFieldKind.Time, "Time"), (FormFieldKind.DateTime, "Date and time"), (FormFieldKind.Duration, "Duration"),
        (FormFieldKind.SingleChoice, "Single choice"), (FormFieldKind.MultipleChoice, "Multiple choice"),
        (FormFieldKind.Dropdown, "Dropdown"), (FormFieldKind.CheckboxSet, "Checkbox set"), (FormFieldKind.Rating, "Rating"),
        (FormFieldKind.TableInput, "Table input"), (FormFieldKind.Ranking, "Ranking"),
        (FormFieldKind.Mathematical, "Mathematical number"), (FormFieldKind.Graph, "Graph point")
    ];
    private string _title = "Untitled form", _label = "Question", _help = "";
    private string _status = "Create a form or open a selected form";
    private bool _busy;
    private string _regexPattern = "", _regexExample = "", _regexResult = "Enter a pattern and example.";
    private int _regexMatchMode, _regexCaseMode;
    private void TestRegexDraft()
    {
        try
        {
            _regexResult = FormMarking.TestRegex(_regexPattern, _regexExample, _regexMatchMode == 0, _regexCaseMode == 1)
                ? "Example matches." : "Example does not match.";
        }
        catch (RegexMatchTimeoutException) { _regexResult = "Pattern exceeded the runtime time limit."; }
        catch (ArgumentException) { _regexResult = "Invalid or unsupported pattern."; }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid? FormID => _opened?.FormID;
    private FormPage? Page => _project?.Pages.SingleOrDefault(page => page.PageID == _pageID);
    private (FormPage Page, int Index)? FieldPosition
    {
        get
        {
            if (_project is null || _fieldID is not { } fieldID) return null;
            foreach (var page in _project.Pages)
                for (var index = 0; index < page.Children.Count; index++)
                    if (page.Children[index].Kind == FormChildKind.Field && page.Children[index].ID == fieldID) return (page, index);
            return null;
        }
    }
    private FormField? Field => _project?.Fields.SingleOrDefault(item => item.FieldID == _fieldID);

    private FormChoiceOption? Choice => Field?.Options?.SingleOrDefault(option => option.OptionID == _optionID);
    private string ChoiceLabel => Choice is { } choice ? _optionDrafts.GetValueOrDefault(choice.OptionID, choice.Label) : "";
    private FormTableColumn? Column => Field?.Table?.Columns.SingleOrDefault(column => column.ColumnID == _columnID);
    private string ColumnLabel => Column is { } column ? _columnDrafts.GetValueOrDefault(column.ColumnID, column.Label) : "";
    private (string Minimum, string Maximum) TableBounds => Field?.Table is { } table
        ? _tableDrafts.GetValueOrDefault(Field.FieldID, (table.MinimumRows.ToString(CultureInfo.InvariantCulture), table.MaximumRows.ToString(CultureInfo.InvariantCulture)))
        : ("", "");
    private void LoadFieldInspector()
    {
        if (Field is not { } current) return;
        var values = _fieldDrafts.GetValueOrDefault(current.FieldID, (current.Label, current.Help ?? ""));
        _label = values.Item1; _help = values.Item2;
    }
    private void SelectCurrentOption()
    {
        if (Choice is null) _optionID = Field?.Options?.FirstOrDefault()?.OptionID;
        if (Column is null) _columnID = Field?.Table?.Columns.FirstOrDefault()?.ColumnID;
        if (_fixedRowID is null || Field?.Table?.FixedRowIDs?.Contains(_fixedRowID.Value) != true)
            _fixedRowID = Field?.Table?.FixedRowIDs?.FirstOrDefault() is { } row && row != Guid.Empty ? row : null;
    }

    public static CuiDocument LoadDocument()
    {
        const string name = "HavenOS.Forms.UI.FormsWorkspace.cui";
        using var stream = typeof(FormsCuiWorkspace).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException("Forms CUI source is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), name);
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException(string.Join(Environment.NewLine, parser.Diagnostics.Diagnostics));
        return document;
    }

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "RegexPattern" => _regexPattern, "RegexExample" => _regexExample, "RegexResult" => _regexResult,
            "RegexMatchModes" => new[] { "Full match", "Partial match" }, "RegexMatchMode" => _regexMatchMode,
            "RegexCaseModes" => new[] { "Case sensitive", "Ignore case" }, "RegexCaseMode" => _regexCaseMode,
            "CanTestRegex" => IsActionAvailable("9to1.Forms.SaveField"),
            "CanEditMathematics" => IsActionAvailable("9to1.Forms.EditMathematics"),
            "PaletteNames" => Palette.Select(item => item.Label).ToArray(), "SelectedPaletteIndex" => _paletteIndex,
            "OptionNames" => Field?.Options?.Select(option => option.Label).ToArray() ?? [],
            "SelectedOptionIndex" => Field?.Options?.ToList().FindIndex(option => option.OptionID == _optionID) ?? -1,
            "OptionLabel" => ChoiceLabel,
            "MinimumRows" => TableBounds.Minimum, "MaximumRows" => TableBounds.Maximum,
            "CanEditTableBounds" => IsActionAvailable("9to1.Forms.SaveTableBounds"),
            "FixedRowNames" => Field?.Table?.FixedRowIDs?.Select((_, index) => $"Fixed row {index + 1}").ToArray() ?? [],
            "SelectedFixedRowIndex" => Field?.Table?.FixedRowIDs?.ToList().FindIndex(row => row == _fixedRowID) ?? -1,
            "AddedRows" => Field?.Table?.AllowAddedRows == true ? "Respondents may add rows" : "Fixed rows only",
            "CanAddFixedRow" => IsActionAvailable("9to1.Forms.AddFixedRow"),
            "CanRemoveFixedRow" => IsActionAvailable("9to1.Forms.RemoveFixedRow"),
            "CanToggleAddedRows" => IsActionAvailable("9to1.Forms.ToggleAddedRows"),
            "ColumnNames" => Field?.Table?.Columns.Select(column => column.Label).ToArray() ?? [],
            "SelectedColumnIndex" => Field?.Table?.Columns.ToList().FindIndex(column => column.ColumnID == _columnID) ?? -1,
            "ColumnLabel" => ColumnLabel, "ColumnType" => Column?.Type.ToString() ?? "",
            "ColumnRequired" => Column?.Required == true ? "Required cell" : "Optional cell",
            "ColumnTypeNames" => ColumnTypes.Select(type => type.ToString()).ToArray(), "SelectedColumnTypeIndex" => _columnTypeIndex,
            "CanAddColumn" => IsActionAvailable("9to1.Forms.AddColumn"), "CanEditColumn" => IsActionAvailable("9to1.Forms.SaveColumn"),
            "CanRemoveColumn" => IsActionAvailable("9to1.Forms.RemoveColumn"),
            "CanAddField" => IsActionAvailable("9to1.Forms.AddField"),
            "CanDiscard" => IsActionAvailable("9to1.Forms.DiscardInspector"),
            "CanAddChoice" => IsActionAvailable("9to1.Forms.AddChoice"), "CanEditChoice" => IsActionAvailable("9to1.Forms.UpdateChoice"),
            "Title" => _title, "Label" => _label, "Help" => _help, "Status" => HasDirtyInspector ? _status + " — unsaved inspector edits" : _status,
            "PreviewLabel" => showPreview is null ? "Validate preview" : "Preview form",
            "Page" => Page?.Title ?? "No page selected", "Field" => Field?.Label ?? "No field selected",
            "FieldType" => Field?.Kind.ToString() ?? "", "Required" => Field?.Required == true ? "Required" : "Optional",
            "PageNames" => _project?.Pages.Select(page => page.Title).ToArray() ?? [],
            "FieldNames" => _project?.Fields.Select(field => field.Label).ToArray() ?? [],
            "SelectedPageIndex" => _project?.Pages.ToList().FindIndex(page => page.PageID == _pageID) ?? -1,
            "SelectedFieldIndex" => _project?.Fields.ToList().FindIndex(field => field.FieldID == _fieldID) ?? -1,
            "Contents" => Page is null ? "" : string.Join(Environment.NewLine, Page.Children.Select(child =>
                child.Kind == FormChildKind.Field ? _project!.Fields.Single(field => field.FieldID == child.ID).Label : "Content component")),
            "CanCreate" => IsActionAvailable("9to1.Forms.Create"), "CanOpen" => IsActionAvailable("9to1.Forms.Open"),
            "CanAdd" => IsActionAvailable("9to1.Forms.AddText"), "CanEdit" => IsActionAvailable("9to1.Forms.SaveField"),
            "CanSelectPage" => IsActionAvailable("9to1.Forms.NextPage"), "CanSelectField" => IsActionAvailable("9to1.Forms.NextField"),
            "CanPreview" => IsActionAvailable("9to1.Forms.Preview"),
            "CanNewResponse" => IsActionAvailable("9to1.Forms.NewResponse"),
            "CanMoveEarlier" => IsActionAvailable("9to1.Forms.MoveEarlier"),
            "CanMoveLater" => IsActionAvailable("9to1.Forms.MoveLater"),
            "CanRespond" => IsActionAvailable("9to1.Forms.Respond"),
            "CanPublish" => IsActionAvailable("9to1.Forms.Publish"), "CanClose" => IsActionAvailable("9to1.Forms.Close"),
            _ => null
        };
        return path is "RegexPattern" or "RegexExample" or "RegexResult" or "RegexMatchModes" or "RegexMatchMode" or "RegexCaseModes" or "RegexCaseMode" or "CanTestRegex" or "PaletteNames" or "SelectedPaletteIndex" or "OptionNames" or "SelectedOptionIndex" or "OptionLabel"
            or "MinimumRows" or "MaximumRows" or "CanEditTableBounds"
            or "FixedRowNames" or "SelectedFixedRowIndex" or "AddedRows" or "CanAddFixedRow" or "CanRemoveFixedRow" or "CanToggleAddedRows"
            or "ColumnNames" or "SelectedColumnIndex" or "ColumnLabel" or "ColumnType" or "ColumnRequired"
            or "ColumnTypeNames" or "SelectedColumnTypeIndex" or "CanAddColumn" or "CanEditColumn" or "CanRemoveColumn"
            or "CanDiscard" or "CanAddField" or "CanAddChoice" or "CanEditChoice" or "Title" or "Label" or "Help" or "Status" or "PreviewLabel" or "Page" or "Field" or "FieldType" or "Required"
            or "Contents" or "PageNames" or "FieldNames" or "SelectedPageIndex" or "SelectedFieldIndex"
            or "CanCreate" or "CanOpen" or "CanAdd" or "CanEdit" or "CanSelectPage" or "CanSelectField" or "CanPreview" or "CanPublish" or "CanClose" or "CanRespond" or "CanNewResponse" or "CanMoveEarlier" or "CanMoveLater" or "CanEditMathematics";
    }

    public bool TrySetValue(string path, object? value)
    {
        if (path is "RegexPattern" or "RegexExample" or "RegexMatchMode" or "RegexCaseMode")
        {
            if (IsActionAvailable("9to1.Forms.SaveField") != true) return false;
            if (path is "RegexPattern" or "RegexExample")
            {
                if (value is not string draft || draft.Length > 4096) return false;
                if (path == "RegexPattern") _regexPattern = draft; else _regexExample = draft;
            }
            else
            {
                if (value is not int mode || mode is < 0 or > 1) return false;
                if (path == "RegexMatchMode") _regexMatchMode = mode; else _regexCaseMode = mode;
            }
            TestRegexDraft();
            PropertyChanged?.Invoke(this, new(path));
            PropertyChanged?.Invoke(this, new("RegexResult"));
            return true;
        }
        if (!_busy && value is int index && index >= 0 && _project is not null)
        {
            if (path == "SelectedPaletteIndex" && IsActionAvailable("9to1.Forms.AddField") == true && index < Palette.Length)
                _paletteIndex = index;
            else if (path == "SelectedOptionIndex" && IsActionAvailable("9to1.Forms.UpdateChoice") == true && index < Field!.Options!.Count)
                _optionID = Field.Options[index].OptionID;
            else if (path == "SelectedFixedRowIndex" && IsActionAvailable("9to1.Forms.RemoveFixedRow") == true && index < Field!.Table!.FixedRowIDs!.Count)
                _fixedRowID = Field.Table.FixedRowIDs[index];
            else if (path == "SelectedColumnIndex" && IsActionAvailable("9to1.Forms.SaveColumn") == true && index < Field!.Table!.Columns.Count)
                _columnID = Field.Table.Columns[index].ColumnID;
            else if (path == "SelectedColumnTypeIndex" && IsActionAvailable("9to1.Forms.AddColumn") == true && index < ColumnTypes.Length)
                _columnTypeIndex = index;
            else if (path == "SelectedPageIndex" && IsActionAvailable("9to1.Forms.NextPage") == true && index < _project.Pages.Count)
                _pageID = _project.Pages[index].PageID;
            else if (path == "SelectedFieldIndex" && IsActionAvailable("9to1.Forms.NextField") == true && index < _project.Fields.Count)
            {
                var field = _project.Fields[index];
                _fieldID = field.FieldID; LoadFieldInspector(); SelectCurrentOption();
            }
            else return false;
            Changed(); return true;
        }
        if (_busy || value is not string text || text.Length > 4096) return false;
        if (path == "Title" && IsActionAvailable("9to1.Forms.Create") == true) _title = text;
        else if (path is "MinimumRows" or "MaximumRows" && IsActionAvailable("9to1.Forms.SaveTableBounds") == true)
        {
            var bounds = TableBounds;
            if (path == "MinimumRows") bounds.Minimum = text; else bounds.Maximum = text;
            var table = Field!.Table!;
            if (bounds.Minimum == table.MinimumRows.ToString(CultureInfo.InvariantCulture)
                && bounds.Maximum == table.MaximumRows.ToString(CultureInfo.InvariantCulture)) _tableDrafts.Remove(Field.FieldID);
            else _tableDrafts[Field.FieldID] = bounds;
        }
        else if (path == "OptionLabel" && IsActionAvailable("9to1.Forms.UpdateChoice") == true)
        { if (text == Choice!.Label) _optionDrafts.Remove(Choice.OptionID); else _optionDrafts[Choice.OptionID] = text; }
        else if (path == "ColumnLabel" && IsActionAvailable("9to1.Forms.SaveColumn") == true)
        { if (text == Column!.Label) _columnDrafts.Remove(Column.ColumnID); else _columnDrafts[Column.ColumnID] = text; }
        else if (path is "Label" or "Help" && IsActionAvailable("9to1.Forms.SaveField") == true)
        {
            if (path == "Label") _label = text; else _help = text;
            if (_label == Field!.Label && _help == (Field.Help ?? "")) _fieldDrafts.Remove(Field.FieldID);
            else _fieldDrafts[Field.FieldID] = (_label, _help);
        }
        else return false;
        Changed(); return true;
    }

    public bool? IsActionAvailable(string command) => !_busy && available(command) && (command switch
    {
        "9to1.Forms.NewResponse" => responseSessions is not null && showResponse is not null && _opened?.State == FormPublicationState.Published && !HasDirtyInspector && _responseIDs.ContainsKey(_opened.FormID),
        "9to1.Forms.MoveEarlier" => _opened is not null && !HasDirtyInspector && FieldPosition is { Index: > 0 },
        "9to1.Forms.MoveLater" => _opened is not null && !HasDirtyInspector && FieldPosition is { } position && position.Index < position.Page.Children.Count - 1,
        "9to1.Forms.Respond" => responseSessions is not null && showResponse is not null && _opened?.State == FormPublicationState.Published && !HasDirtyInspector,
        "9to1.Forms.Create" => true, "9to1.Forms.Open" => selectedForm() is not null,
        "9to1.Forms.AddField" or "9to1.Forms.AddText" or "9to1.Forms.AddNumber" or "9to1.Forms.AddPage" or "9to1.Forms.NextPage"
            or "9to1.Forms.NextField" or "9to1.Forms.Close" => _opened is not null,
        "9to1.Forms.Preview" or "9to1.Forms.Publish" => _opened is not null && !HasDirtyInspector,
        "9to1.Forms.DiscardInspector" => _opened is not null && HasDirtyInspector,
        "9to1.Forms.AddChoice" => Field?.Options is { Count: < 4096 },
        "9to1.Forms.UpdateChoice" => Choice is not null,
        "9to1.Forms.SaveTableBounds" => Field?.Table is not null,
        "9to1.Forms.AddFixedRow" => Field?.Table is { } addTable && (addTable.FixedRowIDs?.Count ?? 0) < addTable.MaximumRows,
        "9to1.Forms.RemoveFixedRow" => _fixedRowID is { } row && Field?.Table is { } removeTable && removeTable.FixedRowIDs?.Contains(row) == true
            && (removeTable.AllowAddedRows || removeTable.FixedRowIDs.Count - 1 >= removeTable.MinimumRows),
        "9to1.Forms.ToggleAddedRows" => Field?.Table is { } toggleTable
            && (!toggleTable.AllowAddedRows || (toggleTable.FixedRowIDs?.Count ?? 0) >= toggleTable.MinimumRows),
        "9to1.Forms.AddColumn" => Field?.Table?.Columns is { Count: < 256 },
        "9to1.Forms.SaveColumn" or "9to1.Forms.ToggleColumnRequired" => Column is not null,
        "9to1.Forms.RemoveColumn" => Column is not null && Field!.Table!.Columns.Count > 1
            && !(Field.Table.UniqueColumnIDs?.Contains(Column.ColumnID) ?? false),
        "9to1.Forms.SaveField" or "9to1.Forms.ToggleRequired" or "9to1.Forms.MoveToPage" => Field is not null,
        "9to1.Forms.EditMathematics" => Field is { Kind: FormFieldKind.Mathematical or FormFieldKind.Graph }
            && showMathematicsEditor is not null && !HasDirtyInspector,
        _ => false
    });

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (parameter is not null || IsActionAvailable(command) != true) throw new InvalidOperationException("Forms action is unavailable.");
        var opened = _opened; var project = _project; var page = Page; var field = Field;
        var selected = selectedForm(); var title = _title; var label = _label; var help = _help;
        var paletteKind = Palette[_paletteIndex].Kind; var choice = Choice; var choiceLabel = ChoiceLabel;
        var fixedRowID = _fixedRowID; var tableBounds = TableBounds;
        var column = Column; var columnLabel = ColumnLabel; var columnType = ColumnTypes[_columnTypeIndex];
        _busy = true; Changed();
        try
        {
            FormPublicationResult? result = null;
            switch (command)
            {
                case "9to1.Forms.Create": result = await authoring.CreateAsync(title, FormModeKind.Form, cancellationToken); if (result.Success) { _pageID = null; _fieldID = null; } break;
                case "9to1.Forms.Open": result = await publications.ReadAsync(selected!.Value, cancellationToken); if (result.Success) { _pageID = null; _fieldID = null; } break;
                case "9to1.Forms.AddPage":
                    var addedPage = new FormPage(Guid.NewGuid(), $"Page {project!.Pages.Count + 1}", [], new());
                    result = await authoring.AddPageAsync(opened!.FormID, opened.Revision, addedPage, cancellationToken);
                    if (result.Success) _pageID = addedPage.PageID;
                    break;
                case "9to1.Forms.AddField":
                case "9to1.Forms.AddText":
                case "9to1.Forms.AddNumber":
                    var kind = command == "9to1.Forms.AddField" ? paletteKind
                        : command.EndsWith("AddNumber", StringComparison.Ordinal) ? FormFieldKind.Number : FormFieldKind.ShortText;
                    var options = kind is FormFieldKind.SingleChoice or FormFieldKind.MultipleChoice or FormFieldKind.Dropdown or FormFieldKind.CheckboxSet or FormFieldKind.Ranking
                        ? new[] { new FormChoiceOption(Guid.NewGuid(), "Option 1"), new FormChoiceOption(Guid.NewGuid(), "Option 2") } : null;
                    var addedFieldID = Guid.NewGuid();
                    var addedField = new FormField(addedFieldID, kind, "Question", null,
                        JsonSerializer.SerializeToElement(new { }), false, new(), Options: options,
                        Table: kind == FormFieldKind.TableInput ? new(addedFieldID, [new(Guid.NewGuid(), "Column 1", FormTableCellType.Text)]) : null,
                        Mathematics: kind == FormFieldKind.Mathematical ? new(Guid.NewGuid(), 1, "x") : null,
                        Graph: kind == FormFieldKind.Graph ? new(Guid.NewGuid(), 1, new(-10, 10, -10, 10), [], [], [GraphResponseTool.PlacePoint]) : null);
                    result = await authoring.AddFieldAsync(opened!.FormID, opened.Revision, page!.PageID, addedField, cancellationToken);
                    if (result.Success) _fieldID = addedField.FieldID;
                    break;
                case "9to1.Forms.AddChoice":
                    var addedChoice = new FormChoiceOption(Guid.NewGuid(), $"Option {field!.Options!.Count + 1}");
                    result = await authoring.UpdateFieldAsync(opened!.FormID, opened.Revision,
                        field with { Options = field.Options.Append(addedChoice).ToArray() }, cancellationToken);
                    if (result.Success) _optionID = addedChoice.OptionID;
                    break;
                case "9to1.Forms.UpdateChoice":
                    result = await authoring.UpdateFieldAsync(opened!.FormID, opened.Revision,
                        field! with { Options = field!.Options!.Select(item => item.OptionID == choice!.OptionID ? item with { Label = choiceLabel } : item).ToArray() }, cancellationToken);
                    if (result.Success) _optionDrafts.Remove(choice!.OptionID);
                    break;
                case "9to1.Forms.DiscardInspector":
                    _fieldDrafts.Clear(); _optionDrafts.Clear(); _columnDrafts.Clear(); _tableDrafts.Clear(); break;
                case "9to1.Forms.SaveTableBounds":
                    if (!int.TryParse(tableBounds.Minimum, NumberStyles.None, CultureInfo.InvariantCulture, out var minimumRows)
                        || !int.TryParse(tableBounds.Maximum, NumberStyles.None, CultureInfo.InvariantCulture, out var maximumRows))
                        throw new ArgumentException("Enter whole-number table row limits.");
                    var boundedTable = field!.Table! with { MinimumRows = minimumRows, MaximumRows = maximumRows };
                    FormTableInput.ValidateDefinition(boundedTable);
                    result = await authoring.UpdateFieldAsync(opened!.FormID, opened.Revision, field with { Table = boundedTable }, cancellationToken);
                    if (result.Success) _tableDrafts.Remove(field.FieldID);
                    break;
                case "9to1.Forms.AddFixedRow":
                    var addedRowID = Guid.NewGuid();
                    result = await authoring.UpdateFieldAsync(opened!.FormID, opened.Revision,
                        field! with { Table = field!.Table! with { FixedRowIDs = (field.Table!.FixedRowIDs ?? []).Append(addedRowID).ToArray() } }, cancellationToken);
                    if (result.Success) _fixedRowID = addedRowID;
                    break;
                case "9to1.Forms.RemoveFixedRow":
                    result = await authoring.UpdateFieldAsync(opened!.FormID, opened.Revision,
                        field! with { Table = field!.Table! with { FixedRowIDs = field.Table!.FixedRowIDs!.Where(row => row != fixedRowID).ToArray() } }, cancellationToken);
                    if (result.Success) _fixedRowID = null;
                    break;
                case "9to1.Forms.ToggleAddedRows":
                    result = await authoring.UpdateFieldAsync(opened!.FormID, opened.Revision,
                        field! with { Table = field!.Table! with { AllowAddedRows = !field.Table!.AllowAddedRows } }, cancellationToken);
                    break;
                case "9to1.Forms.AddColumn":
                    var addedColumn = new FormTableColumn(Guid.NewGuid(), $"Column {field!.Table!.Columns.Count + 1}", columnType);
                    result = await authoring.UpdateFieldAsync(opened!.FormID, opened.Revision,
                        field with { Table = field.Table with { Columns = field.Table.Columns.Append(addedColumn).ToArray() } }, cancellationToken);
                    if (result.Success) _columnID = addedColumn.ColumnID;
                    break;
                case "9to1.Forms.SaveColumn":
                case "9to1.Forms.ToggleColumnRequired":
                    var replacementColumn = command == "9to1.Forms.SaveColumn" ? column! with { Label = columnLabel }
                        : column! with { Required = !column!.Required };
                    result = await authoring.UpdateFieldAsync(opened!.FormID, opened.Revision,
                        field! with { Table = field!.Table! with { Columns = field.Table!.Columns.Select(item => item.ColumnID == column!.ColumnID
                            ? replacementColumn : item).ToArray() } }, cancellationToken);
                    if (result.Success && command == "9to1.Forms.SaveColumn") _columnDrafts.Remove(column!.ColumnID);
                    break;
                case "9to1.Forms.RemoveColumn":
                    result = await authoring.UpdateFieldAsync(opened!.FormID, opened.Revision,
                        field! with { Table = field!.Table! with { Columns = field.Table!.Columns.Where(item => item.ColumnID != column!.ColumnID).ToArray() } }, cancellationToken);
                    if (result.Success) { _columnDrafts.Remove(column!.ColumnID); _columnID = null; }
                    break;
                case "9to1.Forms.EditMathematics":
                    var candidate = await showMathematicsEditor!(field!, cancellationToken);
                    if (candidate is null) { _status = "Question edit cancelled"; return; }
                    if (candidate.FieldID != field!.FieldID || candidate.Revision != field.Revision || candidate.Kind != field.Kind)
                        throw new InvalidOperationException("RevisionConflict");
                    result = await authoring.UpdateFieldAsync(opened!.FormID, opened.Revision, candidate, cancellationToken);
                    break;
                case "9to1.Forms.SaveField":
                    result = await authoring.UpdateFieldAsync(opened!.FormID, opened.Revision, field! with { Label = label, Help = help }, cancellationToken);
                    if (result.Success) _fieldDrafts.Remove(field!.FieldID);
                    break;
                case "9to1.Forms.ToggleRequired":
                    result = await authoring.UpdateFieldAsync(opened!.FormID, opened.Revision, field! with { Required = !field!.Required }, cancellationToken); break;
                case "9to1.Forms.MoveToPage":
                    result = await authoring.MoveFieldAsync(opened!.FormID, opened.Revision, field!.FieldID, page!.PageID,
                        page.Children.Count(child => child != new FormChildReference(FormChildKind.Field, field.FieldID)), cancellationToken); break;
                case "9to1.Forms.NextPage":
                    _pageID = project!.Pages[(project.Pages.ToList().FindIndex(item => item.PageID == _pageID) + 1) % project.Pages.Count].PageID; break;
                case "9to1.Forms.NextField":
                    if (project!.Fields.Count > 0) _fieldID = project.Fields[(project.Fields.ToList().FindIndex(item => item.FieldID == _fieldID) + 1) % project.Fields.Count].FieldID;
                    break;
                case "9to1.Forms.Preview":
                    var runtime = await authoring.PreviewAsync(opened!.FormID, opened.Revision, cancellationToken);
                    if (showPreview is not null)
                    {
                        using var preview = mathematics is null ? new FormNativePreview(project!, runtime) : new FormNativePreview(project!, runtime, mathematics);
                        await showPreview(preview, cancellationToken);
                        _status = $"Preview closed at revision {opened.Revision}";
                    }
                    else _status = $"Preview validated at revision {opened.Revision}: {runtime.Read().State}";
                    return;
                case "9to1.Forms.NewResponse":
                case "9to1.Forms.Respond":
                    // Retain the actual durable ID before presentation. A failed native mount must not
                    // start a duplicate response when the user retries presentation.
                    if (!_responseIDs.TryGetValue(opened!.FormID, out var responseID) || command == "9to1.Forms.NewResponse")
                    {
                        var started = await responseSessions!.StartAsync(opened.FormID, opened.Revision, cancellationToken);
                        if (!started.Success) throw new InvalidOperationException(started.Code);
                        responseID = started.Response!.ResponseID; _responseIDs[opened.FormID] = responseID;
                    }
                    var responseOpen = await FormNativeResponseSurface.OpenAsync(responseSessions!, opened.FormID, responseID, cancellationToken, referenceLookup, mathematics);
                    if (!responseOpen.Success) throw new InvalidOperationException(responseOpen.Code);
                    using (var responseSurface = responseOpen.Surface!) await showResponse!(responseSurface, cancellationToken);
                    _status = "Response closed; its saved answers remain in the published version.";
                    return;
                case "9to1.Forms.MoveEarlier":
                case "9to1.Forms.MoveLater":
                    var originalPosition = FieldPosition!.Value;
                    result = await authoring.MoveFieldAsync(opened!.FormID, opened.Revision, field!.FieldID,
                        originalPosition.Page.PageID, originalPosition.Index + (command == "9to1.Forms.MoveEarlier" ? -1 : 1), cancellationToken);
                    if (result.Success) _pageID = originalPosition.Page.PageID;
                    break;
                case "9to1.Forms.Publish": result = await publications.PublishAsync(opened!.FormID, opened.Revision, cancellationToken); break;
                case "9to1.Forms.Close": result = await publications.CloseAsync(opened!.FormID, opened.Revision, cancellationToken); break;
            }
            if (result is not null)
            {
                if (!result.Success) throw new InvalidOperationException(result.Code);
                if (_opened?.FormID != result.Publication!.FormID) { _fieldDrafts.Clear(); _optionDrafts.Clear(); _columnDrafts.Clear(); _tableDrafts.Clear(); }
                _opened = result.Publication!;
                _project = FormProjectCodec.Decode(Encoding.UTF8.GetBytes(_opened.Draft.GetRawText()));
            }
            _pageID ??= _project?.Pages[0].PageID;
            _fieldID ??= _project?.Fields.FirstOrDefault()?.FieldID;
            LoadFieldInspector();
            SelectCurrentOption();
            _status = _opened is null ? "No form" : $"{_project!.Title}: {_opened.State}, revision {_opened.Revision}";
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { _status = error.Message; throw; }
        finally { _busy = false; Changed(); }
    }
    public void RefreshAvailability() => Changed();
    private void Changed() => PropertyChanged?.Invoke(this, new(null));
}
