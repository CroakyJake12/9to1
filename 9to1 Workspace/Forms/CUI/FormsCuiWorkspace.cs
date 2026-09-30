using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core.Forms;

namespace HavenOS.Forms;

/// <summary>Owning authoring surface over canonical revisioned Forms services. Host availability
/// controls affordances only; every persisted operation still checks the store's actual authority.</summary>
public sealed class FormsCuiWorkspace(FormPublicationService publications, FormAuthoringService authoring,
    Func<Guid?> selectedForm, Func<string, bool> available,
    Func<FormNativePreview, CancellationToken, Task>? showPreview = null) : ICuiWritableBindingContext,
    ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private FormPublication? _opened;
    private FormProject? _project;
    private Guid? _pageID, _fieldID;
    private string _title = "Untitled form", _label = "Question", _help = "";
    private string _status = "Create a form or open a selected form";
    private bool _busy;
    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid? FormID => _opened?.FormID;
    private FormPage? Page => _project?.Pages.SingleOrDefault(page => page.PageID == _pageID);
    private FormField? Field => _project?.Fields.SingleOrDefault(item => item.FieldID == _fieldID);

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
            "Title" => _title, "Label" => _label, "Help" => _help, "Status" => _status,
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
            "CanPublish" => IsActionAvailable("9to1.Forms.Publish"), "CanClose" => IsActionAvailable("9to1.Forms.Close"),
            _ => null
        };
        return path is "Title" or "Label" or "Help" or "Status" or "PreviewLabel" or "Page" or "Field" or "FieldType" or "Required"
            or "Contents" or "PageNames" or "FieldNames" or "SelectedPageIndex" or "SelectedFieldIndex"
            or "CanCreate" or "CanOpen" or "CanAdd" or "CanEdit" or "CanSelectPage" or "CanSelectField" or "CanPreview" or "CanPublish" or "CanClose";
    }

    public bool TrySetValue(string path, object? value)
    {
        if (!_busy && value is int index && index >= 0 && _project is not null)
        {
            if (path == "SelectedPageIndex" && IsActionAvailable("9to1.Forms.NextPage") == true && index < _project.Pages.Count)
                _pageID = _project.Pages[index].PageID;
            else if (path == "SelectedFieldIndex" && IsActionAvailable("9to1.Forms.NextField") == true && index < _project.Fields.Count)
            {
                var field = _project.Fields[index];
                _fieldID = field.FieldID; _label = field.Label; _help = field.Help ?? "";
            }
            else return false;
            Changed(); return true;
        }
        if (_busy || value is not string text || text.Length > 4096) return false;
        if (path == "Title" && IsActionAvailable("9to1.Forms.Create") == true) _title = text;
        else if (path is "Label" or "Help" && IsActionAvailable("9to1.Forms.SaveField") == true)
        { if (path == "Label") _label = text; else _help = text; }
        else return false;
        Changed(); return true;
    }

    public bool? IsActionAvailable(string command) => !_busy && available(command) && (command switch
    {
        "9to1.Forms.Create" => true, "9to1.Forms.Open" => selectedForm() is not null,
        "9to1.Forms.AddText" or "9to1.Forms.AddNumber" or "9to1.Forms.AddPage" or "9to1.Forms.NextPage"
            or "9to1.Forms.NextField" or "9to1.Forms.Preview" or "9to1.Forms.Publish" or "9to1.Forms.Close" => _opened is not null,
        "9to1.Forms.SaveField" or "9to1.Forms.ToggleRequired" or "9to1.Forms.MoveToPage" => Field is not null,
        _ => false
    });

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (parameter is not null || IsActionAvailable(command) != true) throw new InvalidOperationException("Forms action is unavailable.");
        var opened = _opened; var project = _project; var page = Page; var field = Field;
        var selected = selectedForm(); var title = _title; var label = _label; var help = _help;
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
                case "9to1.Forms.AddText":
                case "9to1.Forms.AddNumber":
                    var addedField = new FormField(Guid.NewGuid(), command.EndsWith("AddNumber", StringComparison.Ordinal)
                        ? FormFieldKind.Number : FormFieldKind.ShortText, "Question", null, JsonSerializer.SerializeToElement(new { }), false, new());
                    result = await authoring.AddFieldAsync(opened!.FormID, opened.Revision, page!.PageID, addedField, cancellationToken);
                    if (result.Success) _fieldID = addedField.FieldID;
                    break;
                case "9to1.Forms.SaveField":
                    result = await authoring.UpdateFieldAsync(opened!.FormID, opened.Revision, field! with { Label = label, Help = help }, cancellationToken); break;
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
                        using var preview = new FormNativePreview(project!, runtime);
                        await showPreview(preview, cancellationToken);
                        _status = $"Preview closed at revision {opened.Revision}";
                    }
                    else _status = $"Preview validated at revision {opened.Revision}: {runtime.Read().State}";
                    return;
                case "9to1.Forms.Publish": result = await publications.PublishAsync(opened!.FormID, opened.Revision, cancellationToken); break;
                case "9to1.Forms.Close": result = await publications.CloseAsync(opened!.FormID, opened.Revision, cancellationToken); break;
            }
            if (result is not null)
            {
                if (!result.Success) throw new InvalidOperationException(result.Code);
                _opened = result.Publication!;
                _project = FormProjectCodec.Decode(Encoding.UTF8.GetBytes(_opened.Draft.GetRawText()));
            }
            _pageID ??= _project?.Pages[0].PageID;
            _fieldID ??= _project?.Fields.FirstOrDefault()?.FieldID;
            if (Field is { } current) { _label = current.Label; _help = current.Help ?? ""; }
            _status = _opened is null ? "No form" : $"{_project!.Title}: {_opened.State}, revision {_opened.Revision}";
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { _status = error.Message; throw; }
        finally { _busy = false; Changed(); }
    }
    public void RefreshAvailability() => Changed();
    private void Changed() => PropertyChanged?.Invoke(this, new(null));
}
