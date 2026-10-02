using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Stacks.NativeUI;

/// <summary>Native navigation and projections over the canonical Stack engine. No CUI field supplies a storage path or execution authority.</summary>
public sealed class StackCuiController(IStackNativeProjectSource projects) : ICuiWritableBindingContext,
    ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, StackNativeProjectContext> _authorized = [];
    private readonly Dictionary<Guid, Tab> _tabs = [];
    private readonly Tab _home = new("Home", new("Dashboard", null));
    private Guid? _selectedTab;
    private string _projectId = "", _domainId = "";
    private long _selectionGeneration;
    private string _page = "Dashboard", _displayTabKey = "Home";
    private bool _canBack, _canForward, _hasProject;
    private string _status = "Open an authorized Stack project from Home.", _title = "Stacks Home";
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Projects { get; private set; } = [];
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Tabs { get; private set; } = [];
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Domains { get; private set; } = [];
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Files { get; private set; } = [];
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Activity { get; private set; } = [];
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> History { get; private set; } = [];
    private Tab Current => _selectedTab is Guid id ? _tabs[id] : _home;

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "Status" => _status, "Title" => _title, "ProjectID" => _projectId, "DomainID" => _domainId,
            "Projects" => Projects, "Tabs" => Tabs, "Domains" => Domains, "Files" => Files,
            "Activity" => Activity, "History" => History, "Page" => _page, _ => null
        };
        return value is not null;
    }
    public bool TrySetValue(string path, object? value)
    {
        if (value is not string text || path is not ("ProjectID" or "DomainID")) return false;
        if (path == "ProjectID") _projectId = text; else _domainId = text;
        _selectionGeneration = checked(_selectionGeneration + 1); Notify(path); return true;
    }
    public bool? IsActionAvailable(string command) => command switch
    {
        "Home" or "RefreshProjects" or "OpenProject" or "SelectProject" => true,
        "Back" => _canBack,
        "Forward" => _canForward,
        "OpenDomain" or "SelectDomain" or "ShowActivity" or "ShowHistory" or "RefreshProject" => _hasProject,
        _ => false
    };
    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken ct = default)
    {
        // Capture text selection before the first await; a queued native click may not
        // silently adopt text entered while an earlier owner read was suspended.
        var generation = _selectionGeneration;
        var projectInput = _projectId; var domainInput = _domainId;
        var originatingTabKey = _displayTabKey;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (generation != _selectionGeneration)
                throw new ArgumentException("Stack selection changed before this action was admitted. Choose the current selection again.");
            if (command is "Back" or "Forward" or "OpenDomain" or "SelectDomain" or "ShowHistory" or "ShowActivity" or "RefreshProject" &&
                originatingTabKey != (_selectedTab?.ToString("D") ?? "Home"))
                throw new ArgumentException("The originating Stack tab changed while this action was queued. Choose the current tab again.");
            switch (command)
            {
                case "Home":
                    if (_selectedTab is null) _home.Navigate(new("Dashboard", null));
                    _selectedTab = null; ClearProject(); _title = "Stacks Home"; break;
                case "RefreshProjects": await RefreshProjectsAsync(ct).ConfigureAwait(false); break;
                case "SelectProject":
                case "OpenProject":
                    var requestedProject = command == "SelectProject" ? parameter as string : projectInput;
                    if (requestedProject == "Home") { _selectedTab = null; ClearProject(); _title = "Stacks Home"; break; }
                    if (!Guid.TryParse(requestedProject, out var projectId) || !_authorized.ContainsKey(projectId))
                        throw new UnauthorizedAccessException("Choose a project from the current authorized Home list.");
                    if (!_tabs.ContainsKey(projectId)) _tabs.Add(projectId, new(requestedProject!, new("Project", null)));
                    _selectedTab = projectId;
                    await RefreshCurrentAsync(generation, ct).ConfigureAwait(false); break;
                case "SelectDomain":
                case "OpenDomain":
                    var requestedDomain = command == "SelectDomain" ? parameter as string : domainInput;
                    if (!Guid.TryParse(requestedDomain, out var domainId)) throw new ArgumentException("Choose a canonical domain ID.");
                    await NavigateAsync(new("Source", domainId), generation, ct).ConfigureAwait(false); break;
                case "ShowActivity": await NavigateAsync(new("Activity", null), generation, ct).ConfigureAwait(false); break;
                case "ShowHistory":
                    if (!Guid.TryParse(domainInput, out var historyDomain)) throw new ArgumentException("Choose a canonical domain ID.");
                    await NavigateAsync(new("History", historyDomain), generation, ct).ConfigureAwait(false); break;
                case "RefreshProject": await RefreshCurrentAsync(generation, ct).ConfigureAwait(false); break;
                case "Back":
                    if (Current.Index > 0) Current.Index--;
                    await RefreshCurrentAsync(generation, ct).ConfigureAwait(false); break;
                case "Forward":
                    if (Current.Index + 1 < Current.Locations.Count) Current.Index++;
                    await RefreshCurrentAsync(generation, ct).ConfigureAwait(false); break;
                default: throw new InvalidOperationException("This Stack owner action is unavailable.");
            }
            _status = "Current canonical Stack state loaded.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { ClearProject(); throw; }
        catch (UnauthorizedAccessException e) { ClearAuthorization(); _status = e.Message; }
        catch (StackFailureException e) { if (command == "RefreshProjects") ClearAuthorization(); else ClearProject(); _status = $"{e.Code}: {e.Message}"; }
        catch (ArgumentException e) { if (command == "RefreshProjects") ClearAuthorization(); _status = e.Message; }
        catch (IOException) { if (command == "RefreshProjects") ClearAuthorization(); else ClearProject(); _status = "Canonical Stack storage is unavailable. Retry after recovery."; }
        finally { UpdateTabs(); NotifyAll(); _gate.Release(); }
    }
    private async Task RefreshProjectsAsync(CancellationToken ct)
    {
        var contexts = await projects.ListAuthorizedAsync(ct).ConfigureAwait(false);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        var verified = new Dictionary<Guid, StackNativeProjectContext>();
        foreach (var context in contexts)
        {
            var revision = await context.DemandCurrentAsync(ct).ConfigureAwait(false);
            var summary = await context.Engine.GetProjectSummaryAsync(context.Reader, ct).ConfigureAwait(false);
            if (await context.DemandCurrentAsync(ct).ConfigureAwait(false) != revision)
                throw new UnauthorizedAccessException("The Stack project changed while its Home projection was loading. Refresh projects.");
            if (!verified.TryAdd(summary.ProjectId, context)) throw new InvalidDataException("Duplicate canonical project identity.");
            rows.Add(Row(("ProjectID", summary.ProjectId.ToString("D")), ("Name", summary.Name),
                ("State", $"{summary.ActiveDomainCount} domains · {summary.UnresolvedConflictCount} conflicts · revision {summary.RevisionSequence}")));
        }
        _authorized.Clear(); foreach (var entry in verified) _authorized.Add(entry.Key, entry.Value);
        foreach (var id in _tabs.Keys.Where(id => !verified.ContainsKey(id)).ToArray()) _tabs.Remove(id);
        if (_selectedTab is Guid selected && !_tabs.ContainsKey(selected)) { _selectedTab = null; ClearProject(); }
        Projects = rows.AsReadOnly();
    }
    private async Task NavigateAsync(Location location, long generation, CancellationToken ct)
    {
        var tab = Current; var saved = tab.Locations.ToArray(); var savedIndex = tab.Index;
        tab.Navigate(location);
        try { await RefreshCurrentAsync(generation, ct).ConfigureAwait(false); }
        catch { tab.Locations.Clear(); tab.Locations.AddRange(saved); tab.Index = savedIndex; throw; }
    }
    private async Task RefreshCurrentAsync(long generation, CancellationToken ct)
    {
        if (_selectedTab is not Guid id) { ClearProject(); _title = "Stacks Home"; return; }
        if (!_authorized.TryGetValue(id, out var context)) throw new UnauthorizedAccessException("This project is no longer authorized.");
        var revision = await context.DemandCurrentAsync(ct).ConfigureAwait(false);
        var summary = await context.Engine.GetProjectSummaryAsync(context.Reader, ct).ConfigureAwait(false);
        var lineage = await context.Engine.GetLineageAsync(ct).ConfigureAwait(false);
        var location = Current.Location;
        if (location.DomainId is Guid domainId && !lineage.Any(domain => domain.Id == domainId))
            throw new StackFailureException(StackFailureCode.DomainNotFound, "The selected domain is unavailable.", domainId.ToString("D"));
        var fileRows = new List<IReadOnlyDictionary<string, object?>>();
        var historyRows = new List<IReadOnlyDictionary<string, object?>>();
        var activityRows = new List<IReadOnlyDictionary<string, object?>>();
        if (location is { Page: "Source", DomainId: Guid sourceDomain })
        {
            var source = await context.Engine.InspectRecordedTreeAsync(sourceDomain, context.Reader, ct).ConfigureAwait(false);
            foreach (var file in source.Files.OrderBy(item => item.Key, StringComparer.Ordinal))
                fileRows.Add(Row(("Path", file.Key), ("Size", file.Value.Content.LongLength), ("Visibility", file.Value.Visibility.ToString()), ("HeadRevisionID", source.RevisionId.ToString("D")), ("ManifestRevision", revision)));
        }
        if (location is { Page: "History", DomainId: Guid revisionDomain })
            foreach (var commit in await context.Engine.GetRevisionHistoryAsync(revisionDomain, context.Reader, ct).ConfigureAwait(false))
                historyRows.Add(Row(("RevisionID", commit.Id.ToString("D")), ("Message", commit.Message), ("Actor", commit.ActorId), ("Paths", string.Join(", ", commit.ChangedPaths))));
        if (location.Page == "Activity")
            foreach (var entry in await context.Engine.GetActivityAsync(context.Reader, ct).ConfigureAwait(false))
                activityRows.Add(Row(("ActivityID", entry.Id.ToString("D")), ("Action", entry.Action), ("Actor", entry.ActorId), ("Outcome", entry.Outcome), ("TargetID", entry.TargetId?.ToString("D") ?? "")));
        if (await context.DemandCurrentAsync(ct).ConfigureAwait(false) != revision)
            throw new UnauthorizedAccessException("The Stack project changed while loading. Refresh the current project.");
        if (generation != _selectionGeneration) throw new UnauthorizedAccessException("Stack selection changed while loading. Refresh the current selection.");
        _title = summary.Name; Current.Name = summary.Name;
        Domains = lineage.Select(domain => Row(("DomainID", domain.Id.ToString("D")), ("Name", domain.Name),
            ("Kind", domain.Kind.ToString()), ("ParentID", domain.ParentId?.ToString("D") ?? ""), ("Active", domain.IsActive ? "Active default" : ""))).ToArray();
        Files = fileRows.AsReadOnly(); History = historyRows.AsReadOnly(); Activity = activityRows.AsReadOnly();
    }
    private void ClearAuthorization() { _authorized.Clear(); _tabs.Clear(); _selectedTab = null; Projects = []; ClearProject(); _title = "Stacks Home"; }
    private void ClearProject() { Domains = []; Files = []; History = []; Activity = []; }
    private void UpdateTabs() => Tabs = new[] { Row(("ProjectID", "Home"), ("Name", "Home")) }
        .Concat(_tabs.Select(entry => Row(("ProjectID", entry.Key.ToString("D")), ("Name", entry.Value.Name)))).ToArray();
    private void NotifyAll() { _displayTabKey = _selectedTab?.ToString("D") ?? "Home"; _page = Current.Location.Page; _canBack = Current.Index > 0; _canForward = Current.Index + 1 < Current.Locations.Count; _hasProject = _selectedTab is not null; foreach (var name in new[] { "Title", "Status", "Page", "Projects", "Tabs", "Domains", "Files", "History", "Activity" }) Notify(name); }
    private void Notify(string name) => PropertyChanged?.Invoke(this, new(name));
    private static IReadOnlyDictionary<string, object?> Row(params (string Key, object? Value)[] values) =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(values.ToDictionary(item => item.Key, item => item.Value));
    private sealed record Location(string Page, Guid? DomainId);
    private sealed class Tab(string name, Location first)
    {
        public string Name { get; set; } = name;
        public List<Location> Locations { get; } = [first];
        public int Index { get; set; }
        public Location Location => Locations[Index];
        public void Navigate(Location location)
        {
            if (Location == location) return;
            Locations.RemoveRange(Index + 1, Locations.Count - Index - 1); Locations.Add(location); Index++;
        }
    }
}
