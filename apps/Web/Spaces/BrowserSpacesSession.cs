using System.ComponentModel;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using NineToOne.Web.Spaces.Storage;

namespace NineToOne.Web.Spaces;

/// <summary>View-local editing state over the canonical registry. Selection inspects
/// configuration; this view does not impersonate an uncomposed Chat/model/Task owner.</summary>
public sealed class BrowserSpacesSession(Func<SpaceRegistry> registry, Action demandCurrent, Func<Action, Task> present,
    Action<Guid>? editConfiguration = null) : IBrowserSpacesPresentationSession
{
    private sealed record Row(SpaceDefinition Source);
    private readonly object _ownership = new();
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Task> _commands = [];
    private Row[] _rows = [];
    private Guid? _selectedId;
    private SpaceDefinition? _editing;
    private bool _disposed, _admitted, _busy, _available, _uncertain, _showArchived, _sidebar = true, _editor;
    private string _query = "", _name = "", _description = "", _editorError = "";
    private string _status = "Checking your saved Space configuration…";
    private bool _compact, _desktopSidebar = true;
    private Task? _close;
    private SpaceDefinition? Selected => _rows.FirstOrDefault(row => row.Source.Id == _selectedId)?.Source;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool HasUnsavedChanges => !_disposed && (_editor || _busy || _uncertain);

    public Task PrepareAsync(CancellationToken token) => Track(async () =>
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _serial.WaitAsync(linked.Token);
        try { await RefreshAsync(linked.Token); }
        finally { _serial.Release(); }
    });
    public void Admit() { demandCurrent(); if (_disposed) throw new ObjectDisposedException(nameof(BrowserSpacesSession)); _admitted = true; Changed(); }
    public void Deactivate() { _admitted = false; }
    public void SetViewportWidth(double width)
    {
        if (_disposed || width <= 0 || !double.IsFinite(width)) return;
        var compact = width < 720;
        if (_compact == compact) return;
        _compact = compact; _sidebar = compact ? false : _desktopSidebar; Changed();
    }
    private void Changed() => PropertyChanged?.Invoke(this, new(null));

    public bool TryGetValue(string path, out object? value)
    {
        var selected = _disposed ? null : Selected;
        value = path switch
        {
            "spaces.query" => _query,
            "spaces.showArchived" => _showArchived,
            "spaces.builtIns" => VisibleRows(true),
            "spaces.customSpaces" => VisibleRows(false),
            "layout.columns" => _compact ? (_sidebar ? "1fr 0" : "0 1fr") : (_sidebar ? "280 1fr" : "0 1fr"),
            "layout.sidebarVisible" => _sidebar,
            "layout.sidebarHidden" => !_sidebar,
            "layout.detailsVisible" => !_compact || !_sidebar,
            "status.message" => _status,
            "selection.id" => selected?.Id.ToString("D") ?? "",
            "selection.name" => selected?.Name ?? "Manage Spaces",
            "selection.description" => selected?.Description ?? "Create and organise your Space configuration. Workspace services are configured separately.",
            "selection.canEdit" => false, // Context-source admission is not provided by a configuration editor.
            "selection.canOpen" => false,
            "selection.canBuild" => !_disposed && !_busy && _available && !_uncertain && !_editor && selected is { IsBuiltIn: false, IsArchived: false } && editConfiguration is not null,
            "selection.unavailableReason" => selected is null ? "" : "This Space's workspace is not connected in this browser. Its saved configuration remains available here.",
            "selection.effectiveModelLabel" => selected is null ? "" : "Configured model: " + (selected.ModelName ?? "Account default") + " (runtime unavailable)",
            "selection.webAccessLabel" => selected is null ? "" : "Configured web policy: " + (selected.ContextPolicy?.WebAccess.ToString() ?? "Inherited"),
            "selection.memoryScopeLabel" => selected is null ? "" : "Configured memory scope: " + (selected.MemoryPolicy?.DefaultScope.ToString() ?? "Inherited"),
            "selection.contextReferences" => selected?.ContextReferences?.Select(source => new Dictionary<string, object?>
            {
                ["id"] = source.ContextId.ToString("D"), ["label"] = source.Kind + ": " + source.CanonicalEntityId,
                ["permissionLabel"] = "Recorded permission: " + source.Permission,
                ["indexStateLabel"] = "Recorded index state: " + source.IndexState
            }).ToArray() ?? [],
            "selection.capabilities" => selected?.CapabilityBindings?.Select(capability => new Dictionary<string, object?>
            {
                ["id"] = capability.CapabilityId, ["label"] = capability.CapabilityId + ": " + capability.Override + " (configuration only)"
            }).ToArray() ?? [],
            "editor.visible" => _editor && !_disposed,
            "editor.title" => _editing is null ? "New Space" : "Edit Space",
            "editor.name" => _name,
            "editor.description" => _description,
            "editor.error" => _editorError,
            "editor.canConfirm" => !_disposed && !_busy && _available && !_uncertain && _editor && _name.Trim().Length is > 0 and <= 80,
            _ => null
        };
        return value is not null;
    }
    private Row[] VisibleRows(bool builtIn) => _disposed ? [] : _rows.Where(row => row.Source.IsBuiltIn == builtIn &&
        (_showArchived || !row.Source.IsArchived) && (string.IsNullOrWhiteSpace(_query) ||
            row.Source.Name.Contains(_query, StringComparison.OrdinalIgnoreCase) ||
            row.Source.Description.Contains(_query, StringComparison.OrdinalIgnoreCase))).ToArray();

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        if (_disposed) return false;
        if (item is IDictionary<string, object?> projected) return projected.TryGetValue(path, out value);
        if (item is not Row row || !_rows.Contains(row)) return false;
        var space = row.Source;
        value = path switch
        {
            "id" => space.Id.ToString("D"),
            "name" => _rows.Any(other => other.Source.Id != space.Id && other.Source.Name.Equals(space.Name, StringComparison.OrdinalIgnoreCase))
                ? $"{space.Name} ({(space.IsBuiltIn ? "built-in" : "custom")})" : space.Name,
            "summary" => space.IsArchived ? "Archived · " + space.Description : space.Description,
            "isCurrent" => _selectedId == space.Id,
            "isArchived" => space.IsArchived,
            "isActive" => !space.IsArchived,
            _ => null
        };
        return value is not null;
    }
    public bool TrySetItemValue(object item, string path, object? value) => false;
    public bool TrySetValue(string path, object? value)
    {
        if (_disposed || !_admitted) return false;
        switch (path)
        {
            case "spaces.query" when value is string query: _query = query; break;
            case "spaces.showArchived" when value is bool archived: _showArchived = archived; break;
            case "editor.name" when _editor && !_busy && value is string name: _name = name; break;
            case "editor.description" when _editor && !_busy && value is string description: _description = description; break;
            default: return false;
        }
        Changed(); return true;
    }

    public bool? IsActionAvailable(string command) => !_disposed && _admitted && (command switch
    {
        "spaces.refresh" => !_busy && !_editor,
        "spaces.select" => !_busy && _available && !_editor,
        "spaces.toggle-sidebar" or "spaces.clear-search" => true,
        "spaces.create" or "spaces.rename" or "spaces.fork" or "spaces.archive" or "spaces.restore" => !_busy && _available && !_uncertain && !_editor,
        "spaces.build" => !_busy && _available && !_uncertain && !_editor && editConfiguration is not null && Selected is { IsBuiltIn: false, IsArchived: false },
        "spaces.cancel-edit" => _editor && !_busy,
        "spaces.confirm-edit" => _editor && !_busy && _available && !_uncertain && _name.Trim().Length is > 0 and <= 80,
        _ => false
    });
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
        new(DispatchOwnedAsync(command, parameter, cancellationToken, default));
    public ValueTask DispatchWithLifetimeAsync(string command, object? parameter, CuiActionDispatchLifetime lifetime) =>
        new(DispatchOwnedAsync(command, parameter, lifetime.CallerCancellation, lifetime.ViewCancellation));
    private Task DispatchOwnedAsync(string command, object? parameter, CancellationToken caller, CancellationToken view) =>
        Track(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, view, _lifetime.Token);
            var token = linked.Token;
            await _serial.WaitAsync(token);
            try
            {
                demandCurrent(); token.ThrowIfCancellationRequested();
                if (IsActionAvailable(command) != true) return;
                if (command == "spaces.toggle-sidebar") { _sidebar = !_sidebar; if (!_compact) _desktopSidebar = _sidebar; Changed(); return; }
                if (command == "spaces.clear-search") { _query = ""; Changed(); return; }
                if (command == "spaces.cancel-edit") { CloseEditor(); Changed(); return; }
                if (command == "spaces.create") { _editing = null; _editor = true; _name = _description = _editorError = ""; if (_compact) _sidebar = false; Changed(); return; }
                if (command == "spaces.refresh") { await RefreshAsync(token); return; }
                if (command == "spaces.confirm-edit") { await SaveEditorAsync(token); return; }
                if (command == "spaces.build")
                {
                    var requested = ReadId(parameter);
                    if (Selected?.Id != requested) throw new InvalidOperationException("The selected Space changed. Choose it again before editing.");
                    editConfiguration!(requested); return;
                }
                var id = ReadId(parameter);
                var source = _rows.FirstOrDefault(row => row.Source.Id == id)?.Source
                    ?? throw new InvalidOperationException("This Space is no longer in the current view. Refresh the list.");
                if (command == "spaces.select") { _selectedId = id; if (_compact) _sidebar = false; Changed(); return; }
                if (command == "spaces.rename")
                {
                    if (source.IsBuiltIn || source.IsArchived) return;
                    _selectedId = id; _editing = source; _name = source.Name; _description = source.Description;
                    _editorError = ""; _editor = true; if (_compact) _sidebar = false; Changed(); return;
                }
                await MutateAsync(async () =>
                {
                    var owner = registry();
                    var fresh = await owner.GetAsync(id, token) ?? throw new InvalidOperationException("The Space was removed.");
                    if (fresh.Revision != source.Revision) throw new SpaceRevisionConflictException(id, source.Revision, fresh.Revision);
                    switch (command)
                    {
                        case "spaces.fork" when !fresh.IsArchived:
                            var stem = fresh.Name.Length > 60 ? fresh.Name[..60] : fresh.Name;
                            _selectedId = (await owner.ForkAsync(id, stem + " copy", token)).Id; break;
                        case "spaces.archive" when !fresh.IsBuiltIn && !fresh.IsArchived:
                            await owner.SetArchivedAsync(id, true, token); break;
                        case "spaces.restore" when !fresh.IsBuiltIn && fresh.IsArchived:
                            await owner.RestoreAsync(id, token); break;
                        default: throw new InvalidOperationException("This Space action is unavailable.");
                    }
                }, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or KeyNotFoundException)
            {
                Task? presentation = null;
                try { presentation = PresentAsync(() => { if (!_uncertain) { _editorError = error.Message; _status = error.Message; } }, token); await presentation; }
                catch (Exception presentationError)
                {
                    var failures = new BrowserSpacesFailures(); failures.Retain(error); failures.Retain(presentationError, presentation); failures.ThrowIfAny();
                }
                // A foreign write/acknowledgement failure is not proven domain validation.
                // Keep the actual failed operation owned even when its status rendered.
                if (_uncertain) throw;
            }
            finally { _serial.Release(); }
        });

    private async Task SaveEditorAsync(CancellationToken token)
    {
        var name = _name.Trim(); var description = _description.Trim(); var editing = _editing;
        await MutateAsync(async () =>
        {
            var owner = registry();
            var saved = editing is null ? await owner.CreateAsync(name, description, token)
                : await owner.UpdateAsync(editing with { Name = name, Description = description }, editing.Revision, token);
            _selectedId = saved.Id;
            // The canonical owner acknowledged this edit. Do not leave Create active if the next read fails.
            CloseEditor();
        }, token);
    }
    private async Task MutateAsync(Func<Task> mutation, CancellationToken token)
    {
        var failures = new BrowserSpacesFailures();
        Task? original = null;
        var mutationIssued = false;
        try
        {
            _busy = true;
            await PresentAsync(() => { _editorError = ""; _status = "Saving Space configuration…"; }, token);
            mutationIssued = true; original = mutation(); await original;
            demandCurrent(); token.ThrowIfCancellationRequested();
            await RefreshAsync(token);
        }
        catch (Exception error)
        {
            failures.Retain(error, original);
            if (mutationIssued && error is not SpaceRevisionConflictException)
            {
                // This owner fences replay before presentation, which can independently fail.
                if (!_disposed) _uncertain = true;
                await failures.ObserveAsync(() => PresentAsync(() =>
                {
                    _status = "The save could not be confirmed. Close the editor and refresh the saved state before retrying.";
                    _editorError = _status;
                }, CancellationToken.None));
            }
        }
        _busy = false;
        await failures.ObserveAsync(() => PresentAsync(() => { }, CancellationToken.None));
        failures.ThrowIfAny();
    }
    private async Task RefreshAsync(CancellationToken token)
    {
        var failures = new BrowserSpacesFailures();
        Task<IReadOnlyList<SpaceDefinition>>? original = null;
        try
        {
            _busy = true;
            await PresentAsync(() => { }, token);
            demandCurrent();
            original = registry().GetAllAsync(includeArchived: true, cancellationToken: token);
            var rows = await original;
            demandCurrent(); token.ThrowIfCancellationRequested();
            await PresentAsync(() =>
            {
                _rows = rows.Select(space => new Row(space)).ToArray(); _available = true;
                // Reopening the presentation may refresh records but must never re-enable
                // an uncertain retained Create draft. The user first cancels that draft.
                _uncertain = _uncertain && _editor;
                if (!_rows.Any(row => row.Source.Id == _selectedId)) _selectedId = _rows.FirstOrDefault()?.Source.Id;
                if (!_uncertain) _status = "Space configuration is saved on this browser for your signed-in profile. Account sync and workspace services are not connected here.";
            }, token);
        }
        catch (BrowserTaskContextUnavailableException error)
        {
            Task? presentation = null;
            try
            {
                presentation = PresentAsync(() =>
                {
                    _rows = []; _selectedId = null; _available = false;
                    _status = "Sign in through Home settings, then reopen Spaces to save and organise Space configuration on this browser.";
                }, token);
                await presentation;
            }
            catch (Exception presentationError)
            { failures.Retain(error, original); failures.Retain(presentationError, presentation); }
        }
        catch (Exception error) { failures.Retain(error, original); }
        _busy = false;
        await failures.ObserveAsync(() => PresentAsync(() => { }, CancellationToken.None));
        failures.ThrowIfAny();
    }
    private Task PresentAsync(Action update, CancellationToken token) => present(() =>
    {
        if (_disposed || token.IsCancellationRequested) return;
        demandCurrent(); update(); Changed();
    });
    private void CloseEditor() { _editor = false; _editing = null; _name = _description = _editorError = ""; }
    private static Guid ReadId(object? value) => Guid.TryParse(value?.ToString(), out var id) && id != Guid.Empty
        ? id : throw new ArgumentException("Choose a valid Space.");
    private Task Track(Func<Task> source)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_ownership)
        {
            if (_disposed) return Task.FromException(new ObjectDisposedException(nameof(BrowserSpacesSession)));
            _commands.Add(completion.Task);
        }
        _ = SettleAsync(source, completion); return completion.Task;
    }
    private async Task SettleAsync(Func<Task> source, TaskCompletionSource completion)
    {
        Task? original = null;
        try { original = source(); await original; completion.TrySetResult(); }
        catch (OperationCanceledException error) when (original?.IsCanceled == true) { completion.TrySetCanceled(error.CancellationToken); }
        catch (Exception error)
        {
            if (original?.Exception is { } failure) completion.TrySetException(failure.InnerExceptions);
            else completion.TrySetException(error);
        }
        finally { lock (_ownership) if (completion.Task.IsCompletedSuccessfully) _commands.Remove(completion.Task); }
    }
    public void RevokePrivateContext()
    {
        lock (_ownership)
        {
            _disposed = true; _admitted = false; _available = false; _rows = []; _selectedId = null;
            CloseEditor(); _query = ""; _status = "Spaces is closed.";
        }
    }
    public void Dispose() { RevokePrivateContext(); _ = DisposeAsync(); }
    public ValueTask DisposeAsync()
    {
        RevokePrivateContext();
        TaskCompletionSource completion; Task[] commands;
        lock (_ownership)
        {
            if (_close is not null) return new(_close);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = completion.Task;
            commands = _commands.ToArray();
        }
        _ = CompleteCloseAsync(commands, completion); return new(completion.Task);
    }
    private async Task CompleteCloseAsync(Task[] commands, TaskCompletionSource completion)
    {
        try { await CloseAsync(commands); completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); }
    }
    private async Task CloseAsync(Task[] commands)
    {
        try
        {
            await BrowserSpacesDrain.CompleteAsync(new Func<Task>[] { () => { _lifetime.Cancel(); return Task.CompletedTask; } }
                .Concat(commands.Select(task => (Func<Task>)(() => task))));
        }
        finally { _lifetime.Dispose(); }
    }
}
