using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Sites.Domain;

namespace NineToOne.Web.Sites;

/// <summary>A CUI authoring workflow over host-retained canonical owner operations.</summary>
public sealed class SitesBrowserController(SitesBrowserOperations owner) : ICuiWritableBindingContext,
    ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged, IDisposable
{
    private enum Edit { None, Project, Page, Paragraph, Text }
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<SiteProjectRow> _projects = [];
    private SiteProject? _project, _editProject;
    private SitePage? _page;
    private SiteComponent? _component;
    private Edit _edit;
    private string _name = "", _path = "/", _text = "", _status = "";
    private bool _busy, _disposed, _denied, _uncertain;
    private readonly object _issuedGate = new();
    private readonly HashSet<TaskCompletionSource> _issued = [];
    private bool _lifetimeDisposed;
    internal Func<bool>? CommandAdmission { get; set; }
    public bool HasUnsavedChanges => !_disposed && (_edit != Edit.None || _uncertain || _busy);
    internal Task DrainIssuedAsync()
    {
        lock (_issuedGate) return Task.WhenAll(_issued.Select(work => work.Task).ToArray());
    }
    private IDisposable EnterIssued()
    {
        lock (_issuedGate)
        {
            if (_disposed) throw new OperationCanceledException("Private presentation closed.");
            var work = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _issued.Add(work); return new IssuedLease(this, work);
        }
    }
    private sealed class IssuedLease(SitesBrowserController owner, TaskCompletionSource work) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._issuedGate) { owner._issued.Remove(work); work.TrySetResult(); }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public SiteProject? CurrentProject => _project;
    public SitePage? CurrentPage => _page;

    public async Task InitializeAsync(Guid? siteID, CancellationToken cancellationToken)
    {
        using var issued = EnterIssued();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        if (siteID is { } id) await OpenAsync(id, linked.Token);
        else await ListAsync(linked.Token);
        if (_denied || (siteID is not null && _project is null)) throw new UnauthorizedAccessException("Sites destination unavailable.");
        Changed();
    }

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "Projects" => _projects,
            "ProjectName" => _project?.Name ?? "Choose a website",
            "ProjectDetails" => _project is null ? "" : $"Revision {_project.Revision} · {_project.Source.FrameworkId}",
            "Pages" => _project?.Pages.Take(250).Select(page => new SitePageRow(page)).ToArray() ?? [],
            "PageName" => _page?.Name ?? "Choose a page",
            "Components" => PageComponents(),
            "SelectedText" => _component is null ? "Select a paragraph or heading to edit." : Text(_component),
            "Status" => _status,
            "Editing" => _edit != Edit.None,
            "Naming" => _edit is Edit.Project or Edit.Page,
            "PageEditing" => _edit == Edit.Page,
            "TextEditing" => _edit is Edit.Paragraph or Edit.Text,
            "EditTitle" => _edit switch { Edit.Project => "New website", Edit.Page => "New page", Edit.Paragraph => "Add paragraph", Edit.Text => "Edit text", _ => "" },
            "Name" => _name,
            "Path" => _path,
            "Text" => _text,
            "CanBrowse" => Available("OpenProject"),
            "CanReload" => Available("Reload"),
            "CanCreate" => Available("NewProject"),
            "CanCreatePage" => Available("NewPage"),
            "CanAddText" => Available("AddParagraph"),
            "CanEditText" => Available("EditText"),
            "CanSave" => Available("Save"),
            "CanCancel" => Available("Cancel"),
            "CanEditDraft" => !_disposed && !_denied && !_busy && !_uncertain && _edit != Edit.None,
            _ => null
        };
        return path is "Projects" or "ProjectName" or "ProjectDetails" or "Pages" or "PageName" or "Components" or "SelectedText"
            or "Status" or "Editing" or "Naming" or "PageEditing" or "TextEditing" or "EditTitle" or "Name" or "Path" or "Text"
            or "CanBrowse" or "CanReload" or "CanCreate" or "CanCreatePage" or "CanAddText" or "CanEditText" or "CanSave" or "CanCancel" or "CanEditDraft";
    }

    public bool TrySetValue(string path, object? value)
    {
        if (CommandAdmission?.Invoke() == false) return false;
        if (_disposed || _denied || _busy || _uncertain || _edit == Edit.None || value is not string text) return false;
        switch (path)
        {
            case "Name" when _edit is Edit.Project or Edit.Page: _name = text; break;
            case "Path" when _edit == Edit.Page: _path = text; break;
            case "Text" when _edit is Edit.Paragraph or Edit.Text: _text = text; break;
            default: return false;
        }
        Changed(); return true;
    }

    public bool? IsActionAvailable(string command) => CommandAdmission?.Invoke() == false ? false : Available(command);
    private bool Available(string command)
    {
        if (_disposed || _denied || _busy) return false;
        return command switch
        {
            "Reload" => _edit == Edit.None || _uncertain,
            "OpenProject" or "SelectPage" or "SelectComponent" => _edit == Edit.None,
            "NewProject" => _edit == Edit.None,
            "NewPage" => _edit == Edit.None && _project is not null,
            "AddParagraph" => _edit == Edit.None && _project is not null && _page is not null,
            "EditText" => _edit == Edit.None && _project is not null && _component is not null && Editable(_component),
            "Save" => !_uncertain && _edit != Edit.None && (_edit is Edit.Project or Edit.Page ? !string.IsNullOrWhiteSpace(_name) : true),
            "Cancel" => _edit != Edit.None && !_uncertain,
            _ => false
        };
    }

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (CommandAdmission?.Invoke() == false || !Available(command)) return;
        using var issued = EnterIssued();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var ct = linked.Token; _busy = true;
        try
        {
            Changed();
            ct.ThrowIfCancellationRequested();
            switch (command)
            {
                case "Reload":
                    if (_project is { } existing) await OpenAsync(existing.SiteId, ct); else await ListAsync(ct);
                    if (!_denied) EndEdit();
                    break;
                case "OpenProject" when parameter is SiteProjectRow project && _projects.Any(row => row.Project.SiteId == project.Project.SiteId):
                    await OpenAsync(project.Project.SiteId, ct); break;
                case "SelectPage" when parameter is SitePageRow page:
                    _page = _project?.Pages.SingleOrDefault(item => item.PageId == page.Page.PageId); _component = null; break;
                case "SelectComponent" when parameter is SiteComponentRow component:
                    _component = PageComponents().Select(row => row.Component).SingleOrDefault(item => item.ComponentId == component.Component.ComponentId); break;
                case "NewProject": BeginEdit(Edit.Project); break;
                case "NewPage": BeginEdit(Edit.Page); break;
                case "AddParagraph": BeginEdit(Edit.Paragraph); break;
                case "EditText": BeginEdit(Edit.Text); _text = Text(_component!); break;
                case "Cancel": EndEdit(); _status = "Edits discarded."; break;
                case "Save": await SaveAsync(ct); break;
            }
        }
        catch (UnauthorizedAccessException) { Denied(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { if (!_disposed) _status = _uncertain ? UnknownMessage : "Operation cancelled."; }
        catch { if (!_disposed) _status = _uncertain ? UnknownMessage : "Sites is unavailable. Retry the action."; }
        finally { _busy = false; if (!_disposed) Changed(); }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        // The canonical authoring service has revision CAS but no generic mutation-id protocol.
        // Do not repeat an unknown creation/edit or invent a transport idempotency key.
        _uncertain = true;
        var basis = _editProject;
        // Capture the actual typed edit before owner awaits; receipt checks grant no authority.
        (Guid Id, long Revision, string Type, string Text)? textIntent = _edit == Edit.Text
            ? (_component!.ComponentId, _component.Revision, _component.ComponentType, _text) : null;
        var result = _edit switch
        {
            Edit.Project => await owner.Create(_name, ct),
            Edit.Page => await owner.CreatePage(basis!.SiteId, basis!.Revision, _name, _path, ct),
            Edit.Paragraph => await owner.AddParagraph(basis!.SiteId, basis!.Revision, _page!.PageId, _text, ct),
            Edit.Text => await owner.SetText(basis!.SiteId, basis!.Revision, _component!.ComponentId, _text, ct),
            _ => throw new InvalidOperationException()
        };
        ct.ThrowIfCancellationRequested();
        if (!result.IsSuccess)
        {
            if (IsDenied(result.Error!)) { Denied(); return; }
            if (result.Error!.Code is "RevisionConflict" or "InvalidInput" or "Conflict" or "SiteNotFound" or "IdentityMutationBlocked")
            { _uncertain = false; _status = $"{result.Error.Code}: {result.Error.Message}"; }
            else _status = UnknownMessage;
            return;
        }
        var saved = result.Value;
        if (saved is null || saved.SiteId == Guid.Empty || saved.ProjectId == Guid.Empty || saved.Source.FilesDirectoryId == Guid.Empty ||
            saved.SchemaVersion != SiteProjectFormat.CurrentSchemaVersion || (_edit == Edit.Project ? saved.Revision != 1 :
                basis is null || saved.SiteId != basis.SiteId || saved.ProjectId != basis.ProjectId || saved.Source != basis.Source || saved.Revision != basis.Revision + 1))
            throw new InvalidOperationException("Sites owner returned an incompatible revision receipt.");
        if (textIntent is { } intent && !CompatibleTextReceipt(saved, intent))
            throw new InvalidOperationException("Sites owner returned an incompatible text-edit receipt.");
        _project = saved; _projects = _projects.Where(row => row.Project.SiteId != saved.SiteId).Append(new(saved)).Take(200).ToArray();
        _page = _page is null ? saved.Pages.FirstOrDefault() : saved.Pages.FirstOrDefault(page => page.PageId == _page.PageId);
        _component = _component is null ? null : saved.Components.FirstOrDefault(component => component.ComponentId == _component.ComponentId);
        EndEdit(); _status = "Saved.";
    }

    private static bool CompatibleTextReceipt(SiteProject saved, (Guid Id, long Revision, string Type, string Text) intent)
    {
        var matches = saved.Components.Where(component => component.ComponentId == intent.Id).Take(2).ToArray();
        return matches.Length == 1 && intent.Revision > 0 && matches[0].Revision == checked(intent.Revision + 1)
            && matches[0].ComponentType == intent.Type
            && matches[0].Properties.TryGetValue("text", out var text)
            && text.ValueKind == System.Text.Json.JsonValueKind.String && text.GetString() == intent.Text;
    }

    private async Task ListAsync(CancellationToken ct)
    {
        var result = await owner.List(ct); ct.ThrowIfCancellationRequested();
        if (!result.IsSuccess)
        { if (IsDenied(result.Error!)) Denied(); else throw new InvalidOperationException(result.Error!.Code); return; }
        _projects = result.Value!.Take(200).Select(project => new SiteProjectRow(project)).ToArray(); _status = "Websites loaded.";
    }
    private async Task OpenAsync(Guid id, CancellationToken ct)
    {
        var result = await owner.Open(id, ct); ct.ThrowIfCancellationRequested();
        if (!result.IsSuccess)
        { if (IsDenied(result.Error!)) Denied(); else throw new InvalidOperationException(result.Error!.Code); return; }
        var opened = result.Value;
        if (opened is null || opened.SiteId != id || opened.SchemaVersion != SiteProjectFormat.CurrentSchemaVersion || opened.Revision < 1 ||
            opened.ProjectId == Guid.Empty || opened.Source.FilesDirectoryId == Guid.Empty) throw new InvalidOperationException("Sites owner returned an incompatible project.");
        _project = opened; _page = opened.Pages.FirstOrDefault(); _component = null; _status = "Website opened.";
    }
    private SiteComponentRow[] PageComponents()
    {
        if (_project is null || _page is null) return [];
        var seen = new HashSet<Guid>(); var queue = new Queue<Guid>(_page.RootComponentIds); var result = new List<SiteComponentRow>();
        while (queue.TryDequeue(out var id) && result.Count < 500)
        {
            if (!seen.Add(id)) continue;
            var component = _project.Components.FirstOrDefault(item => item.ComponentId == id);
            if (component is null) continue;
            result.Add(new(component)); foreach (var child in component.ChildIds.Concat(component.Slots.Values.SelectMany(slot => slot))) queue.Enqueue(child);
        }
        return result.ToArray();
    }
    private void BeginEdit(Edit edit) { _edit = edit; _editProject = _project; _name = _text = ""; _path = "/"; _status = "Edit the fields, then save."; }
    private void EndEdit() { _edit = Edit.None; _editProject = null; _name = _text = ""; _path = "/"; _uncertain = false; }
    private static bool IsDenied(SiteApiError error) => error.Code is "PermissionDenied" or "PermissionRequired" or "Unauthorized";
    private static bool Editable(SiteComponent component) => component.ComponentType is "paragraph" or "text" or "heading" or "inline-text";
    internal static string Text(SiteComponent component) => component.Properties.TryGetValue("text", out var text) && text.ValueKind == System.Text.Json.JsonValueKind.String ? text.GetString() ?? "" : "";
    private const string UnknownMessage = "The save outcome is unknown. Reload saved state before making another edit.";
    private void Denied() { _denied = true; Clear(); _status = "Sites access was denied. Reopen Sites after signing in."; }
    private void Clear() { _projects = []; _project = _editProject = null; _page = null; _component = null; EndEdit(); }
    private void Changed()
    {
        // Presentation failures cannot change an owner commit or stop later observers.
        if (PropertyChanged is not { } observers) return;
        foreach (PropertyChangedEventHandler observer in observers.GetInvocationList())
        {
            try { observer(this, new PropertyChangedEventArgs(null)); }
            catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Sites presentation observer failed: {0}", error.GetType().Name); }
        }
    }
    internal void ClearPrivatePresentation()
    {
        _disposed = true; Clear(); _status = "";
    }
    public void Dispose()
    {
        lock (_issuedGate)
        {
            if (_lifetimeDisposed) return;
            _lifetimeDisposed = true;
        }
        ClearPrivatePresentation();
        try { _lifetime.Cancel(); }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Sites owner cancellation callback failed: {0}", error.GetType().Name); }
        finally { _lifetime.Dispose(); }
    }
}

public sealed record SiteProjectRow(SiteProject Project) : ICuiBindingContext
{
    public bool TryGetValue(string path, out object? value)
    { value = path switch { "Id" => Project.SiteId.ToString("N"), "Name" => Project.Name, _ => null }; return path is "Id" or "Name"; }
}
public sealed record SitePageRow(SitePage Page) : ICuiBindingContext
{
    public bool TryGetValue(string path, out object? value)
    { value = path switch { "Id" => Page.PageId.ToString("N"), "Name" => Page.Name, _ => null }; return path is "Id" or "Name"; }
}
public sealed record SiteComponentRow(SiteComponent Component) : ICuiBindingContext
{
    public bool TryGetValue(string path, out object? value)
    { value = path switch { "Id" => Component.ComponentId.ToString("N"), "Type" => Component.ComponentType, "Text" => SitesBrowserController.Text(Component), _ => null }; return path is "Id" or "Type" or "Text"; }
}
