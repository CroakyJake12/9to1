using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces;

namespace Haven.Desktop.Views.Shell.NativePresentation;

/// <summary>Canonical CUI presentation borrowed by the existing native sidebar.
/// Owns no conversations, preferences, authentication or task execution.</summary>
internal sealed class NativeChatSidebarCuiAdapter : HavenOS.Apps.Spaces.Chat.Rendering.ISpacesShellNavigation, ICuiWritableBindingContext,
    ICuiRepeatItemBindingContext, ICuiActionDispatcher, INotifyPropertyChanged, IDisposable, IAsyncDisposable
{
    private static readonly AsyncLocal<NativeChatSidebarCuiAdapter?> ExecutingCommand = new();
    private readonly NativeChatSidebarCuiActions _actions;
    private readonly CuiControlLoader _loader;
    private readonly NativeChatSidebarCuiInput _input;
    private readonly ResourceDictionary _visualResources;
    private readonly List<Exception> _closeFailures = [];
    private readonly Dictionary<string, ChatSidebarEntry> _entries = new(StringComparer.Ordinal);
    private readonly List<Task> _originalCommands = [];
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<Task>> _choices = new(StringComparer.Ordinal);
    private IReadOnlyList<NativeSpaceNavigationDestination> _pins = [];
    private HavenMode _mode = HavenMode.Chat;
    private Guid? _spaceId;
    private string? _spaceName;
    private bool _opensAssistants, _collapsed, _busy, _retiring, _navigationPreferencesObserved;
    private bool _compactViewport, _compactDrawer;
    private NativeSpaceSwitcherLayout _switcher = NativeSpaceSwitcherLayout.Rail;
    private long _scopeRevision, _dialogRevision;
    private Func<string, Task>? _confirm;
    private Task? _close;

    public NativeChatSidebarCuiAdapter(NativeChatSidebarCuiActions actions,
        CuiThemePreferenceContext? currentHomeTheme = null, CuiDocument? document = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _values["sidebar.query"] = "";
        _values["sidebar.status"] = "";
        _values["sidebar.sections"] = Array.Empty<object>();
        _values["sidebar.canStart"] = true;
        _values["dialog.visible"] = false;
        _values["dialog.isText"] = false;
        _values["dialog.value"] = "";
        _values["dialog.error"] = "";
        _values["dialog.choices"] = Array.Empty<object>();
        var registry = new CuiControlRegistry();
        NineToOne.Web.BrowserCanonicalIcons.Register(registry);
        _loader = new CuiControlLoader(registry);
        try
        {
            var appearance = CuiThemeScopeApplier.DetectAppearance();
            _visualResources = CuiSceneVisualResources.Create("Spaces", appearance);
            _loader.SetSurface("Spaces", "app.spaces");
            _loader.SetAppearance(appearance);
            if (currentHomeTheme is not null) _loader.SetThemePreferenceContext(currentHomeTheme);
            _loader.SetBindingContext(this);
            _loader.SetActionDispatcher(this);
            var loaded = _loader.TryLoad(document ?? SpaceNavigationCuiDocument.Load());
            if (loaded.Root is null || loaded.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidOperationException(string.Join(Environment.NewLine, loaded.Diagnostics));
            Root = loaded.Root;
            _loader.WireBindings(Root);
            Root.Resources.MergedDictionaries.Add(_visualResources);
            Root.SetValue(ThemeVariantScope.RequestedThemeVariantProperty, CuiSceneVisualResources.Variant(appearance));
            _input = new NativeChatSidebarCuiInput(Root);
        }
        catch { _loader.Dispose(); throw; }
    }

    public Control Root { get; }
    public double PreferredWidth => _compactViewport ? (_compactDrawer ? 350 : 70) : (_switcher == NativeSpaceSwitcherLayout.Rail || _collapsed ? 70 : 0) + (_collapsed ? 0 : 280);
    internal void SetViewportWidth(double width)
    {
        if (_retiring || width <= 0 || !double.IsFinite(width)) return;
        var compact = width < 860;
        if (compact == _compactViewport) return;
        _compactViewport = compact; _compactDrawer = false;
        Changed(); LayoutChanged?.Invoke(this, EventArgs.Empty);
    }
    public event EventHandler? LayoutChanged;
    public event PropertyChangedEventHandler? PropertyChanged;
    public Task? OriginalClose => _close;
    internal Task WhenActionsIdleAsync() => Task.WhenAll(_originalCommands.Append(_loader.WhenActionsIdleAsync()));

    // This method consumes the real owner's saved pin order. It never infers
    // pins from all Spaces and does not persist a preference itself.
    public void SetNavigationSnapshot(IReadOnlyList<NativeSpaceNavigationDestination> orderedPins,
        NativeSpaceSwitcherLayout layout, bool contextualSidebarCollapsed)
    {
        ArgumentNullException.ThrowIfNull(orderedPins);
        if (!Enum.IsDefined(layout) || orderedPins.Any(row => row.SpaceId == Guid.Empty) ||
            orderedPins.Select(row => row.SpaceId).Distinct().Count() != orderedPins.Count)
            throw new ArgumentException("Navigation requires distinct canonical Space identities.");
        var captured = orderedPins.ToArray();
        Publish(() => { _pins = captured; _switcher = layout; _collapsed = contextualSidebarCollapsed; _navigationPreferencesObserved = true; LayoutChanged?.Invoke(this, EventArgs.Empty); });
    }

    public void SetMode(HavenMode mode) => Publish(() => { _mode = mode; });
    public void SetSpaceScope(string? name, Guid? spaceId = null, bool opensAssistants = false) => Publish(() =>
    {
        if (_spaceId != spaceId) { ++_scopeRevision; CloseDialog(); }
        _spaceId = spaceId; _spaceName = name;
        _opensAssistants = opensAssistants || spaceId == SpaceRegistry.AssistantsSpaceId;
    });
    public void SetStatus(string? status) => Publish(() => _values["sidebar.status"] = status ?? "");
    public void SetNewChatBusy(bool busy) => Publish(() => _values["sidebar.canStart"] = !busy);

    public void SetRows(IReadOnlyList<ChatSidebarEntry> pinned, IReadOnlyList<ChatSidebarEntry> unread,
        IReadOnlyList<ChatSidebarEntry> groups, IReadOnlyList<ChatSidebarEntry> files, IReadOnlyList<ChatSidebarEntry> chats)
    {
        var captured = new[] { groups.ToArray(), pinned.ToArray(), unread.ToArray(), chats.ToArray(), files.ToArray() };
        Publish(() =>
        {
            _entries.Clear();
            foreach (var row in captured.SelectMany(rows => rows)) _entries[Key(row)] = row;
            _values["sidebar.sections"] = captured.Select((rows, index) => new Dictionary<string, object?>
            {
                ["key"] = new[] { "groups", "pinned", "unread", "recent", "files" }[index],
                ["title"] = new[] { "", "Pinned", "Unread", "Recent Chats", "File Library" }[index],
                ["visible"] = rows.Length > 0,
                ["rows"] = rows.Select(ProjectRow).ToArray()
            }).ToArray();
        });
    }

    public void ShowTextPrompt(string title, string initialValue, string confirmLabel, Func<string, Task> confirm)
    {
        ArgumentNullException.ThrowIfNull(confirm);
        Publish(() =>
        {
            CloseDialog(); _confirm = confirm;
            _values["dialog.title"] = title; _values["dialog.value"] = initialValue;
            _values["dialog.confirmLabel"] = confirmLabel; _values["dialog.isText"] = true;
            _values["dialog.visible"] = true;
        });
    }

    public void ShowChoices(string title, IReadOnlyList<(string Label, Func<Task> Action)> choices)
    {
        var captured = choices.ToArray();
        Publish(() =>
        {
            CloseDialog(); _values["dialog.title"] = title; _values["dialog.visible"] = true;
            _values["dialog.choices"] = captured.Select(choice =>
            {
                var id = Guid.NewGuid().ToString("D"); _choices.Add(id, choice.Action);
                return new Dictionary<string, object?> { ["id"] = id, ["label"] = choice.Label };
            }).ToArray();
        });
    }

    private void CloseDialog()
    {
        ++_dialogRevision; _confirm = null; _choices.Clear();
        _values["dialog.visible"] = false; _values["dialog.isText"] = false;
        _values["dialog.error"] = ""; _values["dialog.choices"] = Array.Empty<object>();
    }

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "layout.columns" when _compactViewport => _compactDrawer ? "70 280" : "70 0",
            "layout.columns" => $"{(_switcher == NativeSpaceSwitcherLayout.Rail || _collapsed ? 70 : 0)} {(_collapsed ? "0" : "1fr")}",
            "layout.railVisible" => _compactViewport || _switcher == NativeSpaceSwitcherLayout.Rail || _collapsed,
            "layout.sidebarVisible" => _compactViewport ? _compactDrawer : !_collapsed,
            "layout.collapsed" => _compactViewport ? !_compactDrawer : _collapsed,
            "layout.canChange" => _compactViewport || (_actions.CanChangeSidebarLayout && _navigationPreferencesObserved),
            "navigation.canManageSpaces" => _actions.CanManageSpaces,
            "navigation.canConfigure" => _actions.ConfigureNavigation is not null,
            "navigation.interactive" => !_retiring && !_busy,
            "navigation.pins" => _pins.Select(row => new Dictionary<string, object?>
            {
                ["id"] = row.SpaceId.ToString("D"), ["name"] = row.Name, ["iconSource"] = "icon:" + row.IconKey,
                ["description"] = row.AvailabilityDescription, ["selected"] = row.SpaceId == _spaceId,
                ["available"] = row.Available
            }).ToArray(),
            "sidebar.name" => _spaceName ?? ModeName,
            "sidebar.newChatLabel" => _opensAssistants ? "Open Assistants" : _mode switch { HavenMode.Study => "New Study Chat", HavenMode.Tasks => "New Task", _ => "New Chat" },
            "sidebar.groupsLabel" => _mode switch { HavenMode.Study => "Subjects", HavenMode.Tasks => "Task Groups", _ => "Chat Groups" },
            "sidebar.newGroupLabel" => _mode switch { HavenMode.Study => "Create Subject", HavenMode.Tasks => "Create Task Group", _ => "Create Chat Group" },
            "sidebar.groupsVisible" => _spaceId is null || (_mode == HavenMode.Chat && _spaceId == SpaceRegistry.ChatSpaceId),
            "sidebar.isChat" => _mode == HavenMode.Chat && (_spaceId is null || _spaceId == SpaceRegistry.ChatSpaceId),
            "sidebar.specialistsAvailable" => _actions.OpenOwnedSpecialists is not null,
            "sidebar.specialistsDescription" => _actions.OpenOwnedSpecialists is null ? "Owned Specialists are unavailable on this host." : "Open your available Specialists.",
            "dialog.canInteract" => !_busy && !_retiring,
            "dialog.canConfirm" => !_busy && !_retiring && _confirm is not null && !string.IsNullOrWhiteSpace(_values["dialog.value"] as string),
            _ => _values.GetValueOrDefault(path)
        };
        return value is not null;
    }
    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        return item is IReadOnlyDictionary<string, object?> row && row.TryGetValue(path, out value);
    }
    public bool TrySetItemValue(object item, string path, object? value) => false;
    public bool TrySetValue(string path, object? value)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_retiring || _busy || value is not string text) return false;
        if (path == "sidebar.query") { _values[path] = text; _actions.Search(text.Trim()); Changed(); return true; }
        if (path == "dialog.value" && _confirm is not null) { _values[path] = text; Changed(); return true; }
        return false;
    }

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        if (_retiring) return ValueTask.FromException(new ObjectDisposedException(nameof(NativeChatSidebarCuiAdapter)));
        if (_busy) return ValueTask.FromException(new InvalidOperationException("The current navigation action must finish before another begins."));
        // Settled successes no longer need a lifetime join. Failed, canceled and
        // pending original Tasks remain retained for the owning close report.
        _originalCommands.RemoveAll(task => task.IsCompletedSuccessfully);
        if (_originalCommands.Count >= 256) return ValueTask.FromException(new InvalidOperationException("Navigation has too many unresolved failures; reopen this Space after reviewing them."));
        var start = new TaskCompletionSource();
        var actual = DispatchAfterAdmissionAsync(start.Task, command, parameter, cancellationToken);
        _originalCommands.Add(actual); start.SetResult();
        return new(actual);
    }
    private async Task DispatchAfterAdmissionAsync(Task start, string command, object? parameter, CancellationToken cancellationToken)
    {
        await start;
        cancellationToken.ThrowIfCancellationRequested();
        if (_retiring) throw new ObjectDisposedException(nameof(NativeChatSidebarCuiAdapter));
        var scope = _scopeRevision; var dialog = _dialogRevision;
        var previousCommand = ExecutingCommand.Value;
        try
        {
            ExecutingCommand.Value = this;
            _busy = true; Changed();
            Task owner;
            if (command == "nav.cancel") { CloseDialog(); return; }
            if (command == "nav.new-group") { _actions.NewGroup(); return; }
            if (command == "nav.toggle-sidebar")
            {
                if (_compactViewport)
                {
                    _compactDrawer = !_compactDrawer; Changed(); LayoutChanged?.Invoke(this, EventArgs.Empty); return;
                }
                if (!_actions.CanChangeSidebarLayout || !_navigationPreferencesObserved) throw new InvalidOperationException("The current Home owner cannot persist sidebar preferences.");
                var next = !_collapsed; await AwaitOwner(() => _actions.SetSidebarCollapsed(next));
                if (!_retiring && scope == _scopeRevision) { _collapsed = next; LayoutChanged?.Invoke(this, EventArgs.Empty); }
                return;
            }
            if (command == "nav.confirm")
            {
                var text = (_values["dialog.value"] as string)?.Trim() ?? "";
                if (_confirm is null || text.Length == 0) return;
                var confirm = _confirm; await AwaitOwner(() => confirm(text));
                if (!_retiring && dialog == _dialogRevision && scope == _scopeRevision) CloseDialog();
                return;
            }
            if (command == "nav.choose")
            {
                if (parameter is not string choice || !_choices.TryGetValue(choice, out var invoke)) throw new InvalidOperationException("That navigation choice expired.");
                await AwaitOwner(invoke);
                if (!_retiring && dialog == _dialogRevision && scope == _scopeRevision) CloseDialog();
                return;
            }
            owner = command switch
            {
                "nav.new-chat" when _values["sidebar.canStart"] is true => InvokeOwner(_actions.NewChat),
                "nav.choose-space" => InvokeOwner(_actions.SpacePicker),
                "nav.manage-spaces" when _actions.CanManageSpaces => InvokeOwner(_actions.ManageSpaces),
                "nav.configure" when _actions.ConfigureNavigation is not null => InvokeOwner(_actions.ConfigureNavigation),
                "nav.specialists" when _actions.OpenOwnedSpecialists is { } specialists => InvokeOwner(specialists),
                "nav.open-space" => OpenSpace(parameter),
                "nav.toggle-group" => InvokeOwner(() => _actions.Group(new(ReadGroupId(parameter), ChatSidebarGroupAction.Toggle))),
                _ => InvokeEntry(command, parameter)
            };
            await RetainOriginalFault(owner);
        }
        catch (Exception error)
        {
            if (!_retiring && scope == _scopeRevision)
            {
                _values["sidebar.status"] = error.Message;
                if (dialog == _dialogRevision) _values["dialog.error"] = error.Message;
            }
            throw;
        }
        finally
        {
            ExecutingCommand.Value = previousCommand;
            _busy = false;
            if (!_retiring) Changed();
        }
    }

    private Task OpenSpace(object? parameter)
    {
        if (parameter is not string text || !Guid.TryParse(text, out var id) || !_pins.Any(row => row.SpaceId == id && row.Available))
            throw new InvalidOperationException("That pinned Space is no longer available.");
        return InvokeOwner(() => _actions.OpenSpace(id));
    }
    private Guid ReadGroupId(object? parameter)
    {
        if (parameter is not string text || !Guid.TryParse(text, out var id) || !_entries.Values.Any(row => row.Id == id && row.Kind == ChatSidebarEntryKind.Group))
            throw new InvalidOperationException("That group is no longer in the current sidebar.");
        return id;
    }
    private Task InvokeEntry(string command, object? parameter)
    {
        if (parameter is not string key || !_entries.TryGetValue(key, out var row)) throw new InvalidOperationException("That sidebar item expired.");
        if (row.Kind == ChatSidebarEntryKind.File && command == "nav.open-entry") return InvokeOwner(() => _actions.File(row.Id));
        if (row.Kind == ChatSidebarEntryKind.Group)
        {
            var action = command switch
            {
                "nav.open-entry" => ChatSidebarGroupAction.Open, "nav.rename" => ChatSidebarGroupAction.Rename,
                "nav.pin" => ChatSidebarGroupAction.TogglePin, "nav.archive" => ChatSidebarGroupAction.Archive,
                "nav.delete" => ChatSidebarGroupAction.Delete, "nav.group-new-chat" => ChatSidebarGroupAction.NewChat,
                _ => throw new InvalidOperationException("That action does not apply to a group.")
            };
            return InvokeOwner(() => _actions.Group(new(row.Id, action)));
        }
        if (row.Kind != ChatSidebarEntryKind.Conversation) throw new InvalidOperationException("That action does not apply to a file reference.");
        var chatAction = command switch
        {
            "nav.open-entry" => ChatSidebarConversationAction.Open, "nav.rename" => ChatSidebarConversationAction.Rename,
            "nav.pin" => ChatSidebarConversationAction.TogglePin, "nav.read" => ChatSidebarConversationAction.ToggleRead,
            "nav.move-group" => ChatSidebarConversationAction.Move, "nav.move-space" => ChatSidebarConversationAction.MoveToSpace,
            "nav.archive" => ChatSidebarConversationAction.Archive, "nav.delete" => ChatSidebarConversationAction.Delete,
            _ => throw new InvalidOperationException("That action does not apply to a conversation.")
        };
        return InvokeOwner(() => _actions.Conversation(new(row.Id, chatAction)));
    }
    private static Task InvokeOwner(Func<Task> source)
    {
        try { return source() ?? throw new InvalidOperationException("The actual navigation owner did not return its operation."); }
        catch (OperationCanceledException error) { return Task.FromException(new AggregateException("The navigation source faulted synchronously.", error)); }
    }
    private static Task AwaitOwner(Func<Task> source) => RetainOriginalFault(InvokeOwner(source));
    private static async Task RetainOriginalFault(Task actual)
    {
        try { await actual; }
        catch { if (actual.IsFaulted) throw actual.Exception!; throw; }
    }

    private void Publish(Action update)
    {
        if (Dispatcher.UIThread.CheckAccess()) { if (!_retiring) { update(); Changed(); } }
        else Dispatcher.UIThread.Post(() => { if (!_retiring) { update(); Changed(); } });
    }
    private void Changed() => PropertyChanged?.Invoke(this, new(null));
    private string ModeName => _mode switch { HavenMode.Study => "Study", HavenMode.Tasks => "Tasks", _ => "Chat" };
    private static string Key(ChatSidebarEntry row) => $"{row.Kind}:{row.Id:D}";
    private static Dictionary<string, object?> ProjectRow(ChatSidebarEntry row) => new()
    {
        ["key"] = Key(row), ["id"] = row.Id.ToString("D"), ["title"] = row.Title,
        ["selected"] = row.Active, ["unread"] = row.Unread, ["isGroup"] = row.Kind == ChatSidebarEntryKind.Group,
        ["isConversation"] = row.Kind == ChatSidebarEntryKind.Conversation,
        ["hasActions"] = row.Kind != ChatSidebarEntryKind.File, ["margin"] = row.Indented ? "16,0,0,0" : "0",
        ["accessibleName"] = (row.Kind == ChatSidebarEntryKind.File ? "Open source chat for file " : "") + row.Title + (row.Unread ? ", unread" : ""),
        ["toggleLabel"] = row.Expanded ? "−" : "+", ["toggleDescription"] = (row.Expanded ? "Collapse " : "Expand ") + row.Title,
        ["pinLabel"] = row.Pinned ? "Unpin" : "Pin", ["readLabel"] = row.Unread ? "Mark read" : "Mark unread",
        ["deleteLabel"] = row.Kind == ChatSidebarEntryKind.Group ? "Delete and detach chats" : "Delete",
        ["manageLabel"] = "Manage " + row.Title
    };

    public Task CloseAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (ReferenceEquals(ExecutingCommand.Value, this))
            throw new InvalidOperationException("Join the sidebar close outside its original navigation command.");
        if (_close is not null) return _close;
        var start = new TaskCompletionSource();
        _retiring = true;
        _close = CloseCoreAsync(start.Task);
        try { Changed(); }
        catch (Exception error) { _closeFailures.Add(error); }
        finally { start.SetResult(); }
        return _close;
    }
    private async Task CloseCoreAsync(Task start)
    {
        await start;
        var failures = new List<Exception>(_closeFailures);
        var original = Task.WhenAll(_originalCommands.ToArray());
        try { await original; }
        catch (Exception error) { if (original.IsFaulted) failures.AddRange(original.Exception!.InnerExceptions); else failures.Add(error); }
        try { _input.Dispose(); }
        catch (Exception error) { failures.Add(error); }
        try { _loader.Dispose(); }
        catch (Exception error) { failures.Add(error); }
        var loader = _loader.WhenActionsIdleAsync();
        try { await loader; }
        catch (Exception error) { if (loader.IsFaulted) failures.AddRange(loader.Exception!.InnerExceptions); else failures.Add(error); }
        try { Root.Resources.MergedDictionaries.Remove(_visualResources); }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count > 0) throw new AggregateException(failures.Distinct<Exception>(ReferenceEqualityComparer.Instance));
    }
    public void Dispose() => _ = CloseAsync();
    public ValueTask DisposeAsync() => new(CloseAsync());
}
