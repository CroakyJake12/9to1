using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;

namespace NineToOne.Web.Spaces;

/// <summary>Presentation of one original Space revision. The registry remains the
/// only configuration writer; this session cannot publish or execute a graph.</summary>
public sealed class BrowserSpaceBuilderSession(Func<SpaceRegistry> registry, Action demandCurrent,
    Func<Action, Task> present, Guid spaceId) : IBrowserSpacesPresentationSession
{
    private readonly object _ownership = new();
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Task> _commands = [];
    private SpaceDefinition? _space;
    private bool _disposed, _admitted, _busy, _dirty, _advancedDirty, _uncertain;
    private string _name = "", _description = "", _icon = "", _instructions = "";
    private string _modelId = "", _provider = "", _modelRequired = "", _maximumItems = "";
    private SpaceContextPolicy _context = new();
    private SpaceMemoryPolicy _memory = new([SpaceMemoryScope.Conversation]);
    private SpaceCapabilityBinding[] _bindings = [];
    private string _tab = "Basic", _page = "Details", _status = "Loading saved Space configuration…";
    private Task? _close;
    private static readonly string[] Pages = ["Details", "Harness Logic", "Model Logic", "User Message Logic", "Files"];
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool HasUnsavedChanges => !_disposed && (_dirty || _uncertain || _busy);
    private bool CanEdit => !_disposed && _admitted && !_busy && !_uncertain && _space is { IsBuiltIn: false, IsArchived: false };

    public Task PrepareAsync(CancellationToken cancellationToken) => Track(async () =>
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await SerializeAsync(() => RefreshAsync(false, linked.Token), linked.Token);
    });
    public void Admit() { demandCurrent(); ObjectDisposedException.ThrowIf(_disposed, this); _admitted = true; Changed(); }
    public void Deactivate() => _admitted = false;
    private void Changed() => PropertyChanged?.Invoke(this, new(null));

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "builder.title" => _space is null ? "Space configuration" : _space.Name,
            "builder.identity" => _space is null ? "" : $"Space {_space.Id:D} · revision {_space.Revision}",
            "builder.status" => _status,
            "builder.canEdit" => CanEdit,
            "builder.basic" => _tab == "Basic", "builder.advanced" => _tab == "Advanced", "builder.expert" => _tab == "Expert",
            "builder.documentVisible" => _tab != "Expert" || _page == "Details",
            "builder.name" => _name, "builder.description" => _description,
            "builder.icon" => _icon, "builder.instructions" => _instructions,
            "builder.modelId" => _modelId, "builder.provider" => _provider, "builder.modelRequired" => _modelRequired,
            "builder.webOptions" => Enum.GetNames<SpaceWebAccessPolicy>(), "builder.webIndex" => (int)_context.WebAccess,
            "builder.sourceOnly" => _context.SourceOnly, "builder.includeSelection" => _context.IncludeCurrentSelection,
            "builder.maximumItems" => _maximumItems,
            "builder.memoryOptions" => (_memory.AllowedScopes ?? []).Select(scope => scope.ToString()).ToArray(),
            "builder.memoryIndex" => Array.IndexOf((_memory.AllowedScopes ?? []).ToArray(), _memory.DefaultScope),
            "builder.bindingOptions" => new[] { "Inherited", "Allowed", "Blocked" },
            "builder.bindings" => _disposed ? [] : _bindings,
            "builder.page" => _page, "builder.pages" => Pages,
            "builder.filesVisible" => _tab == "Expert" && _page == "Files",
            "builder.graphVisible" => _tab == "Expert" && _page is "Harness Logic" or "Model Logic" or "User Message Logic",
            "builder.resourceSummary" => _space is null ? "" : $"{_space.Files.Count} file references · {_space.ContextReferences?.Count ?? 0} context sources. Folder editing and package import are not connected in this browser.",
            "builder.resources" => _disposed ? [] : _space?.ContextReferences?.Select(source => new Dictionary<string, object?>
            { ["id"] = source.ContextId.ToString("D"), ["label"] = source.Kind + ": " + source.CanonicalEntityId,
              ["state"] = $"Recorded permission: {source.Permission} · revision {source.RevisionToken ?? "unspecified"}" }).ToArray() ?? [],
            "builder.legacyLayout" => _space?.LayoutDocument is { Nodes.Count: > 0 } layout
                ? $"Your existing layout ({layout.Nodes.Count} nodes) is preserved. It has not been converted to executable Expert logic." : "",
            "builder.hasLegacyLayout" => _space?.LayoutDocument is { Nodes.Count: > 0 },
            _ => null
        };
        return value is not null;
    }

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        if (_disposed) return false;
        if (item is IDictionary<string, object?> row) return row.TryGetValue(path, out value);
        if (item is SpaceCapabilityBinding binding && _bindings.Contains(binding))
            value = path switch
            {
                "id" => binding.Kind + ":" + binding.CapabilityId, "label" => binding.Kind + " · " + binding.CapabilityId,
                "index" => (int)binding.Override, "version" => binding.VersionPin ?? "Installed version", _ => null
            };
        return value is not null;
    }
    public bool TrySetItemValue(object item, string path, object? value)
    {
        if (!CanEdit || item is not SpaceCapabilityBinding binding || path != "index" || value is not int index || index is < 0 or > 2)
            return false;
        demandCurrent();
        var position = Array.IndexOf(_bindings, binding);
        if (position < 0) return false;
        _bindings[position] = binding with { Override = (SpaceCapabilityOverride)index };
        _dirty = _advancedDirty = true; Changed(); return true;
    }
    public bool TrySetValue(string path, object? value)
    {
        if (!CanEdit) return false;
        demandCurrent();
        var advanced = true;
        switch (path)
        {
            case "builder.name" when value is string text: _name = text; advanced = false; break;
            case "builder.description" when value is string text: _description = text; advanced = false; break;
            case "builder.icon" when value is string text: _icon = text; advanced = false; break;
            case "builder.instructions" when value is string text: _instructions = text; advanced = false; break;
            case "builder.modelId" when value is string text: _modelId = text; break;
            case "builder.provider" when value is string text: _provider = text; break;
            case "builder.modelRequired" when value is string text: _modelRequired = text; break;
            case "builder.maximumItems" when value is string text: _maximumItems = text; break;
            case "builder.webIndex" when value is int index && index is >= 0 and <= 2:
                _context = _context with { WebAccess = (SpaceWebAccessPolicy)index }; break;
            case "builder.sourceOnly" when value is bool flag: _context = _context with { SourceOnly = flag }; break;
            case "builder.includeSelection" when value is bool flag: _context = _context with { IncludeCurrentSelection = flag }; break;
            case "builder.memoryIndex" when value is int index && index >= 0 && index < (_memory.AllowedScopes?.Count ?? 0):
                _memory = _memory with { DefaultScope = _memory.AllowedScopes![index] }; break;
            default: return false;
        }
        _dirty = true; _advancedDirty |= advanced; Changed(); return true;
    }

    public bool? IsActionAvailable(string command) => !_disposed && _admitted && (command switch
    {
        "builder.basic" or "builder.advanced" or "builder.expert" or "builder.details" or "builder.harness" or
            "builder.model" or "builder.message" or "builder.files" => !_busy,
        "builder.save" => CanEdit && _dirty && _name.Trim().Length is > 0 and <= 80 && !string.IsNullOrWhiteSpace(_icon),
        "builder.refresh" => !_busy,
        "builder.discard" => !_busy && (_dirty || _uncertain),
        _ => false
    });
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
        new(DispatchOwnedAsync(command, cancellationToken, default));
    public ValueTask DispatchWithLifetimeAsync(string command, object? parameter, CuiActionDispatchLifetime lifetime) =>
        new(DispatchOwnedAsync(command, lifetime.CallerCancellation, lifetime.ViewCancellation));
    private Task DispatchOwnedAsync(string command, CancellationToken caller, CancellationToken view) => Track(async () =>
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, view, _lifetime.Token);
        var token = linked.Token;
        await SerializeAsync(async () =>
        {
            demandCurrent(); token.ThrowIfCancellationRequested();
            if (IsActionAvailable(command) != true) return;
            if (command == "builder.save") { await SaveAsync(token); return; }
            if (command is "builder.refresh" or "builder.discard") { await RefreshAsync(command == "builder.discard", token); return; }
            await PresentAsync(() =>
            {
                switch (command)
                {
                    case "builder.basic": _tab = "Basic"; break;
                    case "builder.advanced": _tab = "Advanced"; break;
                    case "builder.expert": _tab = "Expert"; break;
                    case "builder.details": _page = "Details"; break;
                    case "builder.harness": _page = "Harness Logic"; break;
                    case "builder.model": _page = "Model Logic"; break;
                    case "builder.message": _page = "User Message Logic"; break;
                    case "builder.files": _page = "Files"; break;
                }
            }, token);
        }, token);
    });
    private async Task SerializeAsync(Func<Task> operation, CancellationToken token)
    {
        await _serial.WaitAsync(token);
        try { await operation(); }
        finally { _serial.Release(); }
    }

    private async Task SaveAsync(CancellationToken token)
    {
        var original = _space ?? throw new InvalidOperationException("The original Space is unavailable.");
        var details = new SpaceBuilderBasicPatch(_name, _description, _icon, _instructions);
        SpaceBuilderAdvancedPatch? advanced = null;
        if (_advancedDirty)
        {
            int? maximum = null;
            if (!string.IsNullOrWhiteSpace(_maximumItems))
            {
                if (!int.TryParse(_maximumItems, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed is < 0 or > 1000)
                { await PresentAsync(() => _status = "Maximum retrieved items must be blank or a whole number from 0 to 1000.", token); return; }
                maximum = parsed;
            }
            advanced = new(details, new(NullIfEmpty(_modelId),
                    _modelRequired.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray(), NullIfEmpty(_provider)),
                _context with { MaximumRetrievedItems = maximum }, _memory, _bindings.ToArray(), original.Inheritance ?? new());
        }
        // Close input admission before yielding to the presentation dispatcher, so
        // edits cannot race the captured save and then be erased by its return.
        _busy = true;
        var failures = new BrowserSpacesFailures();
        Task? actual = null;
        Task<SpaceDefinition>? write = null;
        bool writerEntered = false;
        Exception? handledRefusal = null;
        try
        {
            actual = PresentAsync(() => _status = "Saving configuration…", token);
            await actual;
            demandCurrent();
            var owner = registry();
            writerEntered = true;
            actual = write = advanced is null
                ? owner.ApplyBuilderBasicAsync(spaceId, original.Revision, details, token)
                : owner.ApplyBuilderAdvancedAsync(spaceId, original.Revision, advanced, token);
            var saved = await write;
            demandCurrent(); token.ThrowIfCancellationRequested();
            actual = PresentAsync(() => { Load(saved); _status = "Configuration saved on this browser. No graph was activated and no model or tool was run."; }, token);
            await actual;
        }
        catch (Exception error) when (writerEntered && IsSingleKnownRefusal(error, write))
        {
            handledRefusal = error;
            await failures.ObserveAsync(() => PresentAsync(() => _status = error.Message + " Your unsaved changes are retained.", CancellationToken.None));
        }
        catch (Exception error)
        {
            // Preserve the writer's original failure before touching the presenter.
            // Setting the replay fence does not depend on presentation succeeding.
            _uncertain = true;
            failures.Retain(error, actual);
            await failures.ObserveAsync(() => PresentAsync(() => _status = "The save outcome could not be confirmed. Refresh to inspect the saved revision before choosing whether to discard this local draft.", CancellationToken.None));
        }
        finally
        {
            // A failed dispatcher must not permanently prevent read/discard recovery.
            _busy = false;
            await failures.ObserveAsync(() => PresentAsync(() => { }, CancellationToken.None));
        }
        if (failures.HasFailures && handledRefusal is not null) failures.Retain(handledRefusal, write);
        failures.ThrowIfAny();
    }
    private static bool IsSingleKnownRefusal(Exception error, Task? write) =>
        // Untyped validation/JSON failures can occur after a committed transport
        // reply. Only the registry's explicit revision refusal proves no write.
        error is SpaceRevisionConflictException &&
        (write is null || write.IsFaulted && write.Exception!.InnerExceptions.Count == 1 &&
            ReferenceEquals(write.Exception.InnerExceptions[0], error));

    private async Task RefreshAsync(bool discardDraft, CancellationToken token)
    {
        demandCurrent();
        if (spaceId == Guid.Empty) throw new ArgumentException("A canonical Space ID is required.");
        var failures = new BrowserSpacesFailures();
        Task? actual = null;
        _busy = true;
        try
        {
            actual = PresentAsync(() => { }, token);
            await actual;
            var read = registry().GetAsync(spaceId, token);
            actual = read;
            var observed = await read ?? throw new InvalidOperationException("This saved Space is no longer available.");
            demandCurrent(); token.ThrowIfCancellationRequested();
            actual = PresentAsync(() =>
            {
                if ((!_dirty && !_uncertain) || discardDraft) Load(observed);
                _status = _dirty || _uncertain
                    ? $"Saved revision {observed.Revision}; your local draft still uses revision {_space?.Revision}. Discard unsaved changes to load the saved version."
                    : observed.IsBuiltIn ? "This built-in Space is protected. Make a copy in Spaces to configure your own version."
                    : "Edit this Space's saved configuration. Workspace execution and graph publication are not connected in this browser.";
            }, token);
            await actual;
        }
        catch (Exception error) { failures.Retain(error, actual); }
        finally
        {
            _busy = false;
            await failures.ObserveAsync(() => PresentAsync(() => { }, CancellationToken.None));
        }
        failures.ThrowIfAny();
    }
    private void Load(SpaceDefinition source)
    {
        _space = source;
        _name = source.Name; _description = source.Description; _icon = source.IconKey; _instructions = source.Instructions;
        _modelId = source.ModelPolicy?.DefaultModelId ?? ""; _provider = source.ModelPolicy?.ProviderConstraint ?? "";
        _modelRequired = string.Join(", ", source.ModelPolicy?.RequiredCapabilities ?? []);
        _context = source.ContextPolicy ?? new(); _maximumItems = _context.MaximumRetrievedItems?.ToString(CultureInfo.InvariantCulture) ?? "";
        _memory = source.MemoryPolicy ?? new([SpaceMemoryScope.Conversation]);
        _bindings = source.CapabilityBindings?.ToArray() ?? [];
        _dirty = _advancedDirty = _uncertain = false;
    }
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private Task PresentAsync(Action update, CancellationToken token) => present(() =>
    {
        if (_disposed || token.IsCancellationRequested) return;
        demandCurrent(); update(); Changed();
    });
    private Task Track(Func<Task> source)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_ownership)
        {
            if (_disposed) return Task.FromException(new ObjectDisposedException(nameof(BrowserSpaceBuilderSession)));
            _commands.Add(completion.Task);
        }
        _ = SettleAsync(source, completion); return completion.Task;
    }
    private async Task SettleAsync(Func<Task> source, TaskCompletionSource completion)
    {
        Task? actual = null;
        try { actual = source(); await actual; completion.TrySetResult(); }
        catch (OperationCanceledException error) when (actual?.IsCanceled == true) { completion.TrySetCanceled(error.CancellationToken); }
        catch (Exception error)
        {
            if (actual?.Exception is { } faults) completion.TrySetException(faults.InnerExceptions);
            else completion.TrySetException(error);
        }
        finally { lock (_ownership) if (completion.Task.IsCompletedSuccessfully) _commands.Remove(completion.Task); }
    }
    public void RevokePrivateContext()
    {
        lock (_ownership)
        {
            _disposed = true; _admitted = false; _space = null; _bindings = [];
            _name = _description = _icon = _instructions = _modelId = _provider = _modelRequired = _maximumItems = "";
            _context = new(); _memory = new([SpaceMemoryScope.Conversation]);
            _status = "Space configuration is closed.";
        }
    }
    public void Dispose() { RevokePrivateContext(); _ = DisposeAsync(); }
    public ValueTask DisposeAsync()
    {
        RevokePrivateContext();
        TaskCompletionSource completion;
        Task[] commands;
        lock (_ownership)
        {
            if (_close is not null) return new(_close);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = completion.Task;
            commands = _commands.ToArray();
        }
        // Publish the one close before cancellation can invoke reentrant callbacks.
        _ = CloseAsync(commands, completion);
        return new(completion.Task);
    }
    private async Task CloseAsync(Task[] commands, TaskCompletionSource completion)
    {
        try
        {
            var sources = new List<Func<Task>>
            {
                () => { _lifetime.Cancel(); return Task.CompletedTask; }
            };
            sources.AddRange(commands.Select<Task, Func<Task>>(command => () => command));
            await BrowserSpacesDrain.CompleteAsync(sources);
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
        finally { _lifetime.Dispose(); }
    }
}
