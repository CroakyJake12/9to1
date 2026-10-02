using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core.Forms;

namespace HavenOS.Forms;

public sealed record FormNativeResponseOpenResult(bool Success, string? Code, FormNativeResponseSurface? Surface);

/// <summary>Durable authenticated respondent controls. The canonical service owns answer validation,
/// timing, marking and persistence. Local drafts never masquerade as saved response state.</summary>
public sealed class FormNativeResponseSurface : ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability, IDisposable
{
    private readonly FormResponseSessionService _sessions;
    private readonly FormResponsePresentation _project;
    private readonly FormResponseSessionScope _scope;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<Guid, JsonElement> _drafts = [];
    private readonly Dictionary<Guid, string> _fieldErrors = [];
    private readonly IFormDataReferenceLookupSource? _referenceLookup;
    private const string InvalidFieldMessage = "This answer could not be saved. Check its format and requirements.";
    private readonly List<FormNativeAnswerInput> _inputs = [];
    private readonly List<Action> _detachButtons = [];
    private StackPanel? _root;
    private bool _busy;
    private bool _conflicted;
    private bool _disposed;
    public FormResponse? Response { get; private set; }
    public string? StatusCode { get; private set; }
    public int UnsavedAnswerCount => _drafts.Count;

    private FormNativeResponseSurface(FormResponseSessionService sessions, FormResponseDefinitionResult loaded, IFormDataReferenceLookupSource? referenceLookup)
    {
        _sessions = sessions; _referenceLookup = referenceLookup;
        _project = loaded.Presentation!;
        _scope = loaded.Scope!;
        Response = loaded.Response!;
        FormNativePreview.RequireNativeLayout(_project.Fields, _project.Pages, _project.ComponentCount, _project.Theme, referenceLookup is not null);
    }

    public static Task<FormNativeResponseOpenResult> OpenAsync(FormResponseSessionService sessions,
        Guid formID, Guid responseID, CancellationToken token = default)
        => OpenAsync(sessions, formID, responseID, token, null);

    public static async Task<FormNativeResponseOpenResult> OpenAsync(FormResponseSessionService sessions,
        Guid formID, Guid responseID, CancellationToken token, IFormDataReferenceLookupSource? referenceLookup)
    {
        var loaded = await sessions.ReadSessionAsync(formID, responseID, token);
        if (!loaded.Success) return new(false, loaded.Code, null);
        try { return new(true, null, new(sessions, loaded, referenceLookup)); }
        catch (NotSupportedException) { return new(false, "CapabilityUnavailable", null); }
    }

    public void Register(CuiControlRegistry registry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        registry.RegisterObjectRenderer("forms.response", _ => CreateControl());
    }
    public CuiDocument CreateDocument() => new CuiRichParser().Parse(
        "<Cui><Object id=\"durable-response\" type=\"forms.response\" /></Cui>", "Forms response");

    private Control CreateControl()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_root is not null) throw new InvalidOperationException("A response surface belongs to one native mount.");
        _root = new StackPanel { Spacing = 12 };
        _root.DetachedFromVisualTree += Detached;
        Rebuild();
        return _root;
    }
    private void Detached(object? sender, Avalonia.VisualTreeAttachmentEventArgs args) => Dispose();

    private void Rebuild()
    {
        if (_root is null) return;
        foreach (var input in _inputs) input.Dispose();
        _inputs.Clear();
        foreach (var detach in _detachButtons) detach();
        _detachButtons.Clear(); _root.Children.Clear();
        if (_disposed || Response is not { } response) return;
        foreach (var page in _project.Pages)
        {
            var pagePanel = new StackPanel { Spacing = (double)page.Layout.Gap,
                MinWidth = (double)(page.Layout.MinimumWidth ?? 0),
                MaxWidth = page.Layout.MaximumWidth is { } pageMaximum ? (double)pageMaximum : double.PositiveInfinity };
            pagePanel.Children.Add(new TextBlock { Text = page.Title });
            Panel fields = pagePanel;
            if (page.Layout.Columns > 1)
            {
                fields = new FormNativePageColumns(page.Layout.Columns, (double)page.Layout.Gap);
                pagePanel.Children.Add(fields);
            }
            foreach (var child in page.Children)
            {
                var field = _project.Fields.Single(field => field.FieldID == child.ID);
                if (_project.Mode == FormModeKind.Quiz && response.State != FormResponseState.Submitted
                    && response.CurrentFieldID != field.FieldID) continue;
                var value = _drafts.TryGetValue(field.FieldID, out var draft) ? draft
                    : response.Answers.SingleOrDefault(answer => answer.FieldID == field.FieldID)?.Value;
                var input = FormNativeAnswerInput.Create(field, value, answer =>
                {
                    if (_disposed || _busy || _conflicted || Response?.State != FormResponseState.InProgress) return;
                    _drafts[field.FieldID] = answer.Clone();
                    StatusCode = "UnsavedAnswers";
                    RefreshStatus();
                }, _referenceLookup is null ? null : (columnID, inputAlive, token) =>
                    _referenceLookup.OpenForOriginalResponseAsync(response.FormID, response.ResponseID,
                        field.FieldID, columnID, _scope.Actor,
                        () => !_disposed && !_conflicted && Response?.FormVersionID == response.FormVersionID
                            && Response.State == FormResponseState.InProgress && inputAlive(), token));
                AutomationProperties.SetName(input.Control, field.Label);
                AutomationProperties.SetHelpText(input.Control, string.Join(" ",
                    new[] { field.Required ? "Required." : null, field.Help, _fieldErrors.GetValueOrDefault(field.FieldID) }.Where(text => !string.IsNullOrWhiteSpace(text))));
                input.Control.IsEnabled = !_busy && !_conflicted && response.State == FormResponseState.InProgress;
                _inputs.Add(input);
                var group = new StackPanel { Spacing = (double)field.Layout.Gap,
                    MinWidth = (double)(field.Layout.MinimumWidth ?? 0),
                    MaxWidth = field.Layout.MaximumWidth is { } maximum ? (double)maximum : double.PositiveInfinity };
                group.Children.Add(new TextBlock { Text = field.Label + (field.Required ? " *" : "") });
                if (!string.IsNullOrWhiteSpace(field.Help)) group.Children.Add(new TextBlock { Text = field.Help });
                group.Children.Add(input.Control);
                if (_fieldErrors.TryGetValue(field.FieldID, out var fieldError))
                    group.Children.Add(new TextBlock { Text = fieldError });
                if (response.ReleasedResults.TryGetValue(field.FieldID, out var mark))
                    group.Children.Add(new TextBlock { Text = $"{mark.AwardedPoints} / {mark.MaximumPoints}" });
                group.Name = "field-" + field.FieldID.ToString("N");
                fields.Children.Add(group);
            }
            _root.Children.Add(pagePanel);
        }
        AddButton("Save answers", "Save");
        if (_project.Mode == FormModeKind.Quiz) AddButton("Next question", "Advance");
        AddButton("Submit response", "Submit");
        AddButton("Reload saved answers (discard local edits)", "Reload");
        _root.Children.Add(new TextBlock { Name = "response-status" });
        RefreshStatus();
    }
    private void AddButton(string label, string action)
    {
        var button = new Button { Content = label, IsEnabled = IsActionAvailable(action) == true };
        async void Click(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
        {
            if (button.IsEffectivelyEnabled && IsActionAvailable(action) == true) await DispatchAsync(action, null);
        }
        button.Click += Click;
        _detachButtons.Add(() => { button.Click -= Click; button.IsEnabled = false; });
        _root!.Children.Add(button);
    }
    private void RefreshStatus()
    {
        if (_root?.Children.OfType<TextBlock>().SingleOrDefault(block => block.Name == "response-status") is { } status)
            status.Text = StatusCode switch
            {
                "UnsavedAnswers" => "Answers have not been saved.",
                "RevisionConflict" => "This response changed elsewhere. Local edits are retained. Reload to continue.",
                "ValidationFailed" or "InvalidAnswer" => "An answer could not be saved. Check your answers; local edits are retained.",
                null => Response?.State == FormResponseState.Submitted ? "Response submitted" : "Saved response",
                "SaveOutcomeUnknownReloadRequired" => "The save could not be confirmed. Reload saved answers before continuing.",
                "FormClosed" => "This form is closed. Local edits have not been saved.",
                _ => "An answer could not be saved. Check your answers; local edits are retained."
            };
    }
    public bool TryGetValue(string path, out object? value)
    {
        value = path == "Status" ? StatusCode : null;
        return path == "Status";
    }
    public bool? IsActionAvailable(string action) => !_disposed && !_busy && Response is { } response
        && (action == "Reload" || !_conflicted && response.State == FormResponseState.InProgress
            && (action is "Save" or "Submit" || action == "Advance" && _project.Mode == FormModeKind.Quiz));

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (parameter is not null || IsActionAvailable(command) != true) throw new InvalidOperationException("Response action is unavailable.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        _busy = true; Rebuild();
        try
        {
            var response = Response!;
            if (command == "Reload")
            {
                var loaded = await _sessions.ReadSessionAsync(response.FormID, response.ResponseID, token, _scope);
                if (_disposed) return;
                if (!loaded.Success) { HandleFailure(loaded.Code); return; }
                if (loaded.Response!.FormVersionID != response.FormVersionID || loaded.Presentation!.ProjectRevision != _project.ProjectRevision)
                    throw new InvalidDataException("The retained response schema changed.");
                Response = loaded.Response; _drafts.Clear(); _fieldErrors.Clear(); _conflicted = false; StatusCode = null;
                return;
            }
            foreach (var field in _project.Fields)
            {
                if (!_drafts.TryGetValue(field.FieldID, out var draft)) continue;
                response = Response!;
                var saved = await _sessions.AnswerAsync(response.FormID, response.ResponseID, response.Revision,
                    field.FieldID, draft, token, _scope);
                if (_disposed) return;
                if (!saved.Success)
                {
                    if (saved.Code is "ValidationFailed" or "InvalidAnswer")
                        _fieldErrors[field.FieldID] = InvalidFieldMessage;
                    HandleFailure(saved.Code); return;
                }
                Response = saved.Response; _drafts.Remove(field.FieldID); _fieldErrors.Remove(field.FieldID);
            }
            response = Response!;
            if (command is "Advance" or "Submit")
            {
                var completed = command == "Advance"
                    ? await _sessions.AdvanceAsync(response.FormID, response.ResponseID, response.Revision, token, _scope)
                    : await _sessions.SubmitAsync(response.FormID, response.ResponseID, response.Revision, token, _scope);
                if (_disposed) return;
                if (!completed.Success) { HandleFailure(completed.Code); return; }
                Response = completed.Response;
            }
            StatusCode = null;
        }
        catch (OperationCanceledException) { if (!_disposed) { _conflicted = true; StatusCode = "SaveOutcomeUnknownReloadRequired"; } }
        catch (UnauthorizedAccessException) { Dispose(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or JsonException)
        {
            if (!_disposed) { _conflicted = true; StatusCode = "SaveOutcomeUnknownReloadRequired"; }
        }
        finally { _busy = false; if (!_disposed) Rebuild(); }
    }
    private void HandleFailure(string? code)
    {
        StatusCode = code ?? "OperationFailed";
        if (code is "PermissionDenied" or "ResponseUnavailable") { Dispose(); return; }
        if (code is "RevisionConflict" or "FormClosed" or "ResumeDisabled") _conflicted = true;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel();
        foreach (var input in _inputs) input.Dispose();
        _inputs.Clear();
        foreach (var detach in _detachButtons) detach();
        _detachButtons.Clear(); _drafts.Clear(); _fieldErrors.Clear(); Response = null;
        _lifetime.Dispose();
        if (_root is not null) { _root.DetachedFromVisualTree -= Detached; _root.Children.Clear(); _root.IsEnabled = false; }
        // A service commit already published before cancellation remains durable; no rollback is claimed.
    }
}
