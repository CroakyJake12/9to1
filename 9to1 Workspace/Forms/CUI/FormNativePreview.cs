using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using Avalonia.Automation;
using Avalonia.Controls;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Core.Forms;

namespace HavenOS.Forms;

/// <summary>Native CUI Object renderers over the same canonical response engine as published Forms.
/// This author preview never creates a durable respondent or performs linked Data writes.</summary>
public sealed class FormNativePreview : ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged, IDisposable
{
    private readonly FormProject _project;
    private readonly FormResponseRuntime _runtime;
    private readonly List<(Guid FieldID, Control Input)> _inputs = [];
    private readonly List<Action> _detach = [];
    private readonly Dictionary<Guid, string> _invalidDrafts = [];
    private readonly Dictionary<Guid, TextBlock> _validationLabels = [];
    private readonly Dictionary<Guid, TextBlock> _markLabels = [];
    private readonly Dictionary<Guid, Control> _groups = [];
    private bool _disposed;
    public event EventHandler? Changed;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string? ValidationCode { get; private set; }
    public FormResponse Response => _runtime.Read();

    public FormNativePreview(FormProject project, TimeProvider? clock = null) : this(project, null, clock) { }
    public FormNativePreview(FormProject project, FormResponseRuntime runtime) : this(project, runtime, null) { }
    private FormNativePreview(FormProject project, FormResponseRuntime? runtime, TimeProvider? clock)
    {
        _project = FormProjectCodec.Capture(project);
        RequireNativeLayout(_project.Fields, _project.Pages, _project.Components.Count, _project.Theme);
        _runtime = runtime ?? new(_project, Guid.NewGuid(), clock);
        var response = _runtime.Read();
        if (response.FormID != _project.FormID || response.ProjectRevision != _project.Revision)
            throw new InvalidDataException("The preview runtime belongs to a different form revision.");
    }

    internal static void RequireNativeLayout(IReadOnlyList<FormField> fields, IReadOnlyList<FormPage> pages,
        int componentCount, FormThemeReference theme)
    {
        if (componentCount != 0 || fields.Any(field => !CanRender(field.Kind)
                || field.Table?.Columns.Any(column => column.Type == FormTableCellType.Reference) == true)
            || pages.Any(page => page.Layout.Columns != 1) || fields.Any(field => field.Layout.Columns != 1)
            || theme.ThemeID != "default" || theme.StyleAssetID is not null)
            throw new NotSupportedException("CapabilityUnavailable: this form needs an additional native renderer, layout or theme provider.");
    }

    public static bool CanRender(FormFieldKind kind) => kind is FormFieldKind.ShortText or FormFieldKind.LongText
        or FormFieldKind.Email or FormFieldKind.Phone or FormFieldKind.Date or FormFieldKind.Time
        or FormFieldKind.DateTime or FormFieldKind.Duration or FormFieldKind.Number or FormFieldKind.Decimal
        or FormFieldKind.Currency or FormFieldKind.Rating or FormFieldKind.SingleChoice or FormFieldKind.Dropdown
        or FormFieldKind.MultipleChoice or FormFieldKind.CheckboxSet or FormFieldKind.TableInput or FormFieldKind.Ranking;

    public void Register(CuiControlRegistry registry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        registry.RegisterObjectRenderer("forms.answer", component =>
        {
            if (component.Name is not { } name || !name.StartsWith("field-", StringComparison.Ordinal)
                || !Guid.TryParseExact(name[6..], "N", out var id)) throw new InvalidDataException("Preview field identity is missing.");
            return CreateField(id);
        });
    }

    public CuiDocument CreateDocument()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var text = new StringBuilder();
        using (var writer = XmlWriter.Create(text, new XmlWriterSettings { OmitXmlDeclaration = true }))
        {
            writer.WriteStartElement("Cui"); writer.WriteStartElement("StackPanel"); writer.WriteAttributeString("spacing", "12");
            foreach (var page in _project.Pages)
            {
                writer.WriteStartElement("StackPanel");
                writer.WriteAttributeString("spacing", page.Layout.Gap.ToString(CultureInfo.InvariantCulture));
                if (page.Layout.MinimumWidth is { } minimum) writer.WriteAttributeString("min-width", minimum.ToString(CultureInfo.InvariantCulture));
                if (page.Layout.MaximumWidth is { } maximum) writer.WriteAttributeString("max-width", maximum.ToString(CultureInfo.InvariantCulture));
                writer.WriteStartElement("TextBlock"); writer.WriteAttributeString("text", page.Title); writer.WriteEndElement();
                foreach (var child in page.Children)
                {
                    writer.WriteStartElement("Object"); writer.WriteAttributeString("id", "field-" + child.ID.ToString("N"));
                    writer.WriteAttributeString("type", "forms.answer"); writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }
            writer.WriteStartElement("Button"); writer.WriteAttributeString("action", "9to1.Forms.Preview.Advance");
            writer.WriteAttributeString("is-enabled", "{Binding CanAdvance}"); writer.WriteAttributeString("content", "Next question"); writer.WriteEndElement();
            writer.WriteStartElement("Button"); writer.WriteAttributeString("action", "9to1.Forms.Preview.Submit");
            writer.WriteAttributeString("is-enabled", "{Binding CanSubmit}"); writer.WriteAttributeString("content", "Submit preview"); writer.WriteEndElement();
            writer.WriteStartElement("TextBlock"); writer.WriteAttributeString("text", "{Binding Status}"); writer.WriteEndElement();
            writer.WriteStartElement("TextBlock"); writer.WriteAttributeString("text", "{Binding Score}"); writer.WriteEndElement();
            writer.WriteEndElement(); writer.WriteEndElement();
        }
        return new CuiRichParser().Parse(text.ToString(), "Forms interactive preview");
    }

    private Control CreateField(Guid fieldID)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var definition = _project.Fields.Single(item => item.FieldID == fieldID);
        var response = Response;
        var answer = response.Answers.SingleOrDefault(item => item.FieldID == fieldID)?.Value;
        var ownedInput = FormNativeAnswerInput.Create(definition, answer, value => Answer(fieldID, value));
        var input = ownedInput.Control;
        _detach.Add(ownedInput.Dispose);
        AutomationProperties.SetName(input, definition.Label);
        _inputs.Add((fieldID, input));
        var detachInput = _detach[^1];
        void Detached(object? sender, Avalonia.VisualTreeAttachmentEventArgs args)
        {
            detachInput(); input.DetachedFromVisualTree -= Detached;
            _inputs.RemoveAll(item => ReferenceEquals(item.Input, input));
            input.IsEnabled = false;
        }
        input.DetachedFromVisualTree += Detached;
        _detach.Add(() => input.DetachedFromVisualTree -= Detached);
        var group = new StackPanel { Spacing = (double)definition.Layout.Gap,
            MinWidth = (double)(definition.Layout.MinimumWidth ?? 0),
            MaxWidth = definition.Layout.MaximumWidth is { } maximum ? (double)maximum : double.PositiveInfinity };
        _groups[fieldID] = group;
        group.Children.Add(new TextBlock { Text = definition.Label + (definition.Required ? " *" : "") });
        if (!string.IsNullOrWhiteSpace(definition.Help)) group.Children.Add(new TextBlock { Text = definition.Help });
        group.Children.Add(input);
        var validation = new TextBlock(); _validationLabels[fieldID] = validation; group.Children.Add(validation);
        var mark = new TextBlock(); _markLabels[fieldID] = mark; group.Children.Add(mark);
        RefreshInputs();
        return group;
    }

    private void Answer(Guid fieldID, JsonElement value)
    {
        if (_disposed) return;
        var result = _runtime.Answer(Response.Revision, fieldID, value);
        if (result.Success) _invalidDrafts.Remove(fieldID); else _invalidDrafts[fieldID] = result.Code ?? "ValidationFailed";
        if (_validationLabels.TryGetValue(fieldID, out var label)) label.Text = result.Success ? "" : "Check this answer.";
        ValidationCode = _invalidDrafts.Values.FirstOrDefault();
        RefreshInputs(); Notify();
    }
    public FormResponseOperation Advance()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_invalidDrafts.Count > 0) return new(false, "ValidationFailed", Response);
        var result = _runtime.Advance(Response.Revision); ValidationCode = result.Code;
        RefreshInputs(); Notify(); return result;
    }
    public FormResponseOperation Submit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_invalidDrafts.Count > 0) return new(false, "ValidationFailed", Response);
        var result = _runtime.Submit(Response.Revision); ValidationCode = result.Code;
        RefreshInputs(); Notify(); return result;
    }
    private void RefreshInputs()
    {
        var response = Response;
        foreach (var (fieldID, input) in _inputs)
            input.IsEnabled = !_disposed && response.State == FormResponseState.InProgress
                && (_project.ModeDefinition.Kind != FormModeKind.Quiz || response.CurrentFieldID == fieldID);
        foreach (var (fieldID, group) in _groups)
            group.IsVisible = _project.ModeDefinition.Kind != FormModeKind.Quiz || response.CurrentFieldID == fieldID
                || response.State == FormResponseState.Submitted;
        foreach (var (fieldID, label) in _markLabels)
            label.Text = response.ReleasedResults.TryGetValue(fieldID, out var mark)
                ? $"{mark.AwardedPoints} / {mark.MaximumPoints}" : "";
    }
    public bool TryGetValue(string path, out object? value)
    {
        var response = Response;
        value = path switch
        {
            "CanAdvance" => IsActionAvailable("9to1.Forms.Preview.Advance"), "CanSubmit" => IsActionAvailable("9to1.Forms.Preview.Submit"),
            "Status" => ValidationCode is not null ? "Check your answers before continuing." : response.State == FormResponseState.Submitted ? "Preview submitted" : "Preview in progress",
            "Score" => response.AwardedPoints is { } score ? $"{score} / {response.MaximumPoints}" : "Results are not released yet",
            _ => null
        };
        return path is "CanAdvance" or "CanSubmit" or "Status" or "Score";
    }
    public bool? IsActionAvailable(string action) => !_disposed && _invalidDrafts.Count == 0 && Response.State == FormResponseState.InProgress
        && (action == "9to1.Forms.Preview.Submit" || action == "9to1.Forms.Preview.Advance" && _project.ModeDefinition.Kind == FormModeKind.Quiz && Response.CurrentFieldID is not null);
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (parameter is not null || IsActionAvailable(command) != true) throw new InvalidOperationException("Preview action is unavailable.");
        if (command == "9to1.Forms.Preview.Advance") Advance(); else Submit();
        return ValueTask.CompletedTask;
    }
    private void Notify() { Changed?.Invoke(this, EventArgs.Empty); PropertyChanged?.Invoke(this, new(null)); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var detach in _detach) detach();
        _detach.Clear(); RefreshInputs(); _inputs.Clear(); _validationLabels.Clear(); _markLabels.Clear(); _groups.Clear(); Changed = null; PropertyChanged = null;
    }
}
