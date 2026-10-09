using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Stacks.NativeUI;

/// <summary>Only the trusted Home composition supplies engines and current actors. This tuple issues no authority.</summary>
public sealed record StackNativeProject(StackEngine Engine, StackActor Actor);

/// <summary>Native Stacks presentation over the existing engine/store. Every accepted write uses that engine.</summary>
public sealed partial class StackCuiWorkspace : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged, IAsyncDisposable
{
    private sealed class ProjectTab(StackNativeProject source)
    {
        internal StackNativeProject Source { get; } = source;
        internal StackProjectSummary? Summary;
        internal IReadOnlyList<StackDomainSnapshot> Domains = [];
        internal StackDomainSnapshot? Domain;
        internal StackEffectiveTreeSnapshot? Tree;
        internal StackDomainChanges? Changes;
        internal IReadOnlyList<StackConflict> Conflicts = [];
        internal readonly List<Guid> Navigation = [];
        internal int NavigationIndex = -1;
        internal string? SelectedPath;
        internal int FilePage, DiffPage, DomainPage;
    }
    private readonly ProjectTab[] _projects;
    private readonly Dictionary<string, string> _fields = new(StringComparer.Ordinal)
    { ["DomainName"] = "", ["SourcePath"] = "", ["SourceText"] = "", ["CommitMessage"] = "", ["FileFilter"] = "" };
    private ProjectTab? _tab;
    private object _snapshot = new();
    private string _loadedText = "", _status = "Open a project supplied by Home.";
    private bool _initialized, _busy, _retiring, _failed;
    private StackNativeSourceDiff? _diff;
    private string _homePage = "Projects";
    public event PropertyChangedEventHandler? PropertyChanged;
    public Task OriginalInitialization { get; }
    public Guid? ProjectId => _tab?.Summary?.ProjectId;
    public Guid? DomainId => _tab?.Domain?.Id;
    public Guid? ActiveDomainId => _tab?.Summary?.ActiveDomainId;
    public bool HasDraft => !string.Equals(_fields["SourceText"], _loadedText, StringComparison.Ordinal);
    public StackCuiWorkspace(IReadOnlyList<StackNativeProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        if (projects.Count > 64) throw new ArgumentOutOfRangeException(nameof(projects));
        _projects = projects.Select(source =>
        {
            ArgumentNullException.ThrowIfNull(source.Engine); ArgumentNullException.ThrowIfNull(source.Actor);
            DemandView(source); return new ProjectTab(source);
        }).ToArray();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        OriginalInitialization = InitializeOriginalAsync(gate.Task); RetainOriginal(OriginalInitialization); gate.SetResult();
    }
    private static void DemandView(StackNativeProject source)
    {
        if (!source.Actor.Can(StackCapability.ViewSource))
            throw new StackFailureException(StackFailureCode.PermissionDenied, "The current actor cannot view this project.", source.Actor.ActorId);
    }
    private async Task InitializeOriginalAsync(Task gate)
    {
        using var invocation = EnterDriver(); await gate;
        try
        {
        foreach (var tab in _projects)
        {
            DemandView(tab.Source);
            _ = await SourceAsync(() => tab.Source.Engine.OpenProjectAsync());
            tab.Summary = await SourceAsync(() => tab.Source.Engine.GetProjectSummaryAsync(tab.Source.Actor));
            DemandView(tab.Source);
        }
        if (_projects.Select(p => p.Summary!.ProjectId).Distinct().Count() != _projects.Length)
            throw new InvalidOperationException("Home supplied the same canonical project more than once.");
        if (_retiring) return;
        _initialized = true; PublishRows(); Changed();
        }
        catch { _failed = true; throw; }
    }
    public static CuiDocument LoadDocument()
    {
        using var source = typeof(StackCuiWorkspace).Assembly.GetManifestResourceStream("HavenOS.Stacks.NativeUI.UI.StacksWorkspace.cui")
            ?? throw new InvalidDataException("Stacks CUI document is missing.");
        using var reader = new StreamReader(source); var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "StacksWorkspace.cui");
        if (parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException(string.Join(Environment.NewLine, parser.Diagnostics.Diagnostics));
        return document;
    }
    public bool TryGetValue(string path, out object? value)
    {
        if (_fields.TryGetValue(path, out var field)) { value = field; return true; }
        value = path switch
        {
            "Status" => _status, "Tabs" => _tabRows, "Projects" => _projectRows, "Domains" => _domainRows,
            "Files" => _fileRows, "DiffLines" => _diffRows, "IsHome" => _tab is null,
            "IsProject" => _tab is not null, "IsProjectsPage" => _homePage == "Projects", "IsAboutPage" => _homePage == "About", "NoProjects" => _projects.Length == 0, "HomePage" => _homePage,
            "ProjectLabel" => _tab?.Summary?.Name ?? "Stacks",
            "DomainLabel" => _tab?.Domain is { } domain ? $"{domain.Kind} · {domain.Name}" : "Select a domain",
            "ActiveDomainLabel" => _tab?.Domains.SingleOrDefault(d => d.Id == _tab?.Summary?.ActiveDomainId) is { } active
                ? $"Active for editor and build · {active.Kind} · {active.Name}" : "No active domain",
            "Counts" => _tab?.Changes is { } changes ? $"{changes.Added} added · {changes.Deleted} deleted · {changes.Modified} modified · {changes.Renamed} renamed" : "",
            "Conflicts" => _tab is null ? "" : $"{_tab.Conflicts.Count(c => !c.IsResolved)} unresolved conflicts",
            "SourceDescription" => _diff?.Description ?? "Select a file to view its effective source and owned changes.",
            "HasDraft" => HasDraft, "CanEditText" => CanEditText, "CanApply" => IsActionAvailable("9to1.Stacks.ApplyText"),
            "CanCreateDomain" => IsActionAvailable("9to1.Stacks.CreateDomain"), "CanCommit" => IsActionAvailable("9to1.Stacks.Commit"),
            "CanNavigate" => !_busy && !_retiring && !HasDraft,
            "DomainPageLabel" => $"Domains · page {(_tab?.DomainPage ?? 0) + 1}",
            "FilePageLabel" => $"Files · page {(_tab?.FilePage ?? 0) + 1}",
            "DiffPageLabel" => $"Diff · page {(_tab?.DiffPage ?? 0) + 1}", _ => null
        };
        return value is not null;
    }
    private bool CanEditText => !_busy && !_retiring && _tab?.Source.Actor.Can(StackCapability.Contribute) == true && _diff?.IsText == true;
    public bool TrySetValue(string path, object? value)
    {
        if (_busy || _retiring || _failed || value is not string text || !_fields.ContainsKey(path)) return false;
        if (text.Length > (path == "SourceText" ? StackNativeSourceDiff.MaximumSourceBytes : 2048)) return false;
        if (path == "SourceText" && !CanEditText) return false;
        if (path == "SourcePath" && _tab?.SelectedPath is not null) return false;
        _fields[path] = text;
        if (path == "FileFilter") { if (_tab is not null) _tab.FilePage = 0; PublishFileRows(); }
        Changed(); return true;
    }
    private void Changed()
    {
        using var physical = EnterPhysical();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    public bool? IsActionAvailable(string command)
    {
        if (!_initialized || _retiring || _busy || _failed) return false;
        var navigating = command is "9to1.Stacks.Home" or "9to1.Stacks.OpenProject" or "9to1.Stacks.SelectDomain"
            or "9to1.Stacks.Back" or "9to1.Stacks.Forward" or "9to1.Stacks.SelectFile" or "9to1.Stacks.Refresh";
        if (navigating && HasDraft) return false;
        return command switch
        {
            "9to1.Stacks.Home" or "9to1.Stacks.OpenProject" or "9to1.Stacks.About" => !HasDraft,
            "9to1.Stacks.SelectDomain" or "9to1.Stacks.SelectFile" or "9to1.Stacks.Refresh" => _tab is not null,
            "9to1.Stacks.Back" => _tab?.NavigationIndex > 0,
            "9to1.Stacks.Forward" => _tab is not null && _tab.NavigationIndex + 1 < _tab.Navigation.Count,
            "9to1.Stacks.CreateDomain" => !string.IsNullOrWhiteSpace(_fields["DomainName"]) && !HasDraft && _tab?.Source.Actor.Can(StackCapability.CreateDomain) == true && _tab.Domain?.Kind != StackDomainKind.Leaf,
            "9to1.Stacks.RenameDomain" => !string.IsNullOrWhiteSpace(_fields["DomainName"]) && !HasDraft && _tab?.Domain is { Kind: not StackDomainKind.Main } && _tab.Source.Actor.Can(StackCapability.CreateDomain),
            "9to1.Stacks.SetActive" => !HasDraft && _tab?.Domain is { IsDeleted: false } &&
                _tab.Summary?.ActiveDomainId != _tab.Domain.Id && _tab.Source.Actor.Can(StackCapability.ViewSource),
            "9to1.Stacks.NewTextFile" => !HasDraft && _tab?.Source.Actor.Can(StackCapability.Contribute) == true,
            "9to1.Stacks.ApplyText" => CanEditText && ValidTextInput() && (HasDraft || _tab?.SelectedPath is null),
            "9to1.Stacks.DiscardDraft" => HasDraft,
            "9to1.Stacks.Commit" => !string.IsNullOrWhiteSpace(_fields["CommitMessage"]) && !HasDraft && _tab?.Source.Actor.Can(StackCapability.Contribute) == true && _tab.Domain?.WorkingChanges.Count > 0 && !_tab.Conflicts.Any(c => !c.IsResolved),
            "9to1.Stacks.PreviousDomains" => _tab?.DomainPage > 0,
            "9to1.Stacks.NextDomains" => _tab is not null && (_tab.DomainPage + 1) * PageSize < _tab.Domains.Count(d => !d.IsDeleted),
            "9to1.Stacks.PreviousFiles" => _tab?.FilePage > 0,
            "9to1.Stacks.NextFiles" => _tab is not null && (_tab.FilePage + 1) * PageSize < FilteredFiles().Count,
            "9to1.Stacks.PreviousDiff" => _tab?.DiffPage > 0,
            "9to1.Stacks.NextDiff" => _tab is not null && (_tab.DiffPage + 1) * PageSize < (_diff?.Lines.Count ?? 0),
            _ => false
        };
    }
    private bool ValidTextInput()
    {
        try
        {
            var path = StackPath.Normalize(_fields["SourcePath"]);
            if (_tab?.SelectedPath is null && (_tab?.Tree?.Files.ContainsKey(path) == true || _tab?.Domain?.BaseTree.GetValueOrDefault(path) is not null)) return false;
            return StackNativeSourceDiff.CanEncode(_fields["SourceText"]);
        }
        catch (StackFailureException failure) when (failure.Code == StackFailureCode.InvalidPath) { return false; }
        catch (ArgumentException) when (string.IsNullOrWhiteSpace(_fields["SourcePath"])) { return false; }
    }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        => new(AdmitOriginalCommand(command, parameter, cancellationToken));
    private async Task RunCommandAsync(string command, object? parameter, CancellationToken token)
    {
        switch (command)
        {
            case "9to1.Stacks.Home": _tab = null; _homePage = "Projects"; ClearSource(); break;
            case "9to1.Stacks.About": _tab = null; _homePage = "About"; ClearSource(); break;
            case "9to1.Stacks.OpenProject":
                var project = DemandRow<ProjectTarget>(parameter); _tab = project.Tab;
                await RefreshAsync(_tab, token); break;
            case "9to1.Stacks.SelectDomain":
                var domain = DemandRow<DomainTarget>(parameter);
                await NavigateDomainAsync(domain.Domain.Id, true, token); break;
            case "9to1.Stacks.Back":
            case "9to1.Stacks.Forward":
                var tab = _tab!; var next = tab.NavigationIndex + (command.EndsWith("Back", StringComparison.Ordinal) ? -1 : 1);
                await NavigateDomainAsync(tab.Navigation[next], false, token); tab.NavigationIndex = next; break;
            case "9to1.Stacks.Refresh": await RefreshAsync(_tab!, token); break;
            case "9to1.Stacks.SelectFile":
                var file = DemandRow<FileTarget>(parameter); SelectFile(file.Path); break;
            case "9to1.Stacks.NewTextFile":
                ClearSource(); _diff = StackNativeSourceDiff.Read(null, new StackResource([])); _fields["SourcePath"] = ""; break;
            case "9to1.Stacks.ApplyText":
                var path = StackPath.Normalize(_fields["SourcePath"]);
                var content = StackNativeSourceDiff.Encode(_fields["SourceText"]);
                var selected = _tab!; var current = selected.Tree?.Files.GetValueOrDefault(path);
                await SourceAsync(() => selected.Source.Engine.ApplyChangeAsync(selected.Domain!.Id,
                    new StackMutation(StackMutationKind.Upsert, path, new StackResource(content, current?.Visibility ?? selected.Domain!.BaseTree.GetValueOrDefault(path)?.Visibility ?? StackVisibility.Private)), selected.Source.Actor, token));
                selected.SelectedPath = path; await RefreshAsync(selected, token); _status = "Working change saved."; break;
            case "9to1.Stacks.DiscardDraft": _fields["SourceText"] = _loadedText; break;
            case "9to1.Stacks.CreateDomain":
                var parent = _tab!;
                var created = await SourceAsync(() => parent.Source.Engine.CreateDomainAsync(parent.Domain!.Id, _fields["DomainName"], parent.Source.Actor, token));
                await NavigateDomainAsync(created.Id, true, token); _fields["DomainName"] = ""; _status = "Domain created."; break;
            case "9to1.Stacks.RenameDomain":
                var rename = _tab!;
                await SourceAsync(() => rename.Source.Engine.RenameDomainAsync(rename.Domain!.Id, _fields["DomainName"], rename.Source.Actor, token));
                await RefreshAsync(rename, token); _fields["DomainName"] = ""; _status = "Domain renamed."; break;
            case "9to1.Stacks.SetActive":
                var active = _tab!;
                await SourceAsync(() => active.Source.Engine.SetActiveDomainAsync(active.Domain!.Id, active.Source.Actor, token));
                await RefreshAsync(active, token); _status = "Active development domain updated."; break;
            case "9to1.Stacks.Commit":
                var commit = _tab!;
                var revision = await SourceAsync(() => commit.Source.Engine.CreateCommitAsync(commit.Domain!.Id, _fields["CommitMessage"], commit.Source.Actor, cancellationToken: token));
                await RefreshAsync(commit, token); _fields["CommitMessage"] = ""; _status = $"Committed · revision {revision.Sequence}."; break;
            case "9to1.Stacks.PreviousDomains": _tab!.DomainPage--; break;
            case "9to1.Stacks.NextDomains": _tab!.DomainPage++; break;
            case "9to1.Stacks.PreviousFiles": _tab!.FilePage--; PublishFileRows(); break;
            case "9to1.Stacks.NextFiles": _tab!.FilePage++; PublishFileRows(); break;
            case "9to1.Stacks.PreviousDiff": _tab!.DiffPage--; PublishDiffRows(); break;
            case "9to1.Stacks.NextDiff": _tab!.DiffPage++; PublishDiffRows(); break;
            default: throw new InvalidOperationException("Unknown Stacks action.");
        }
        PublishRows();
    }
    private async Task NavigateDomainAsync(Guid id, bool addHistory, CancellationToken token)
    {
        var tab = _tab!;
        // Browsing has its own per-tab selection/history. Only SetActive changes the stored
        // default development domain used by editors/builds.
        tab.SelectedPath = null; await RefreshAsync(tab, token, id);
        if (addHistory)
        {
            if (tab.NavigationIndex + 1 < tab.Navigation.Count) tab.Navigation.RemoveRange(tab.NavigationIndex + 1, tab.Navigation.Count - tab.NavigationIndex - 1);
            tab.Navigation.Add(id); tab.NavigationIndex = tab.Navigation.Count - 1;
        }
    }
    private async Task RefreshAsync(ProjectTab tab, CancellationToken token, Guid? requestedDomain = null)
    {
        DemandView(tab.Source);
        tab.Summary = await SourceAsync(() => tab.Source.Engine.GetProjectSummaryAsync(tab.Source.Actor, token));
        var available = await SourceAsync(() => tab.Source.Engine.GetLineageAsync(token));
        var browsed = requestedDomain ?? tab.Domain?.Id ?? tab.Summary.ActiveDomainId;
        if (!available.Any(domain => domain.Id == browsed && !domain.IsDeleted))
        {
            if (requestedDomain is not null) throw new StackFailureException(StackFailureCode.DomainNotFound,
                "This source domain is no longer available.", requestedDomain.Value.ToString("D"), recoverable: true);
            browsed = tab.Summary.ActiveDomainId;
        }
        var tree = await SourceAsync(() => tab.Source.Engine.GetEffectiveTreeAsync(browsed, tab.Source.Actor, token));
        var changes = await SourceAsync(() => tab.Source.Engine.GetDomainChangesAsync(tree.DomainId, tab.Source.Actor, token));
        var domains = await SourceAsync(() => tab.Source.Engine.GetLineageAsync(token));
        var domain = domains.Single(d => d.Id == tree.DomainId);
        var conflicts = await SourceAsync(() => tab.Source.Engine.GetConflictsAsync(domain.Id, tab.Source.Actor, token));
        DemandView(tab.Source);
        if (_retiring) return;
        tab.Tree = tree; tab.Changes = changes; tab.Domains = domains; tab.Domain = domain; tab.Conflicts = conflicts;
        _snapshot = new();
        if (tab.NavigationIndex < 0) { tab.Navigation.Add(domain.Id); tab.NavigationIndex = 0; }
        if (tab.SelectedPath is { } path && (tree.Files.ContainsKey(path) || domain.BaseTree.ContainsKey(path))) SelectFile(path);
        else ClearSource();
        _status = "Project loaded. Pending fields are kept until applied or discarded.";
    }
    private void ClearSource()
    {
        if (_tab is not null) { _tab.SelectedPath = null; _tab.DiffPage = 0; }
        _diff = null; _fields["SourcePath"] = ""; _fields["SourceText"] = _loadedText = "";
    }
    private void SelectFile(string path)
    {
        var tab = _tab!; tab.SelectedPath = path; tab.DiffPage = 0;
        tab.Domain!.BaseTree.TryGetValue(path, out var before); tab.Tree!.Files.TryGetValue(path, out var current);
        _diff = StackNativeSourceDiff.Read(before, current);
        _fields["SourcePath"] = path; _fields["SourceText"] = _loadedText = _diff.CurrentText ?? "";
    }
}
