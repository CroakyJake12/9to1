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
        internal string Status = "Open a project supplied by Home.";
        internal IReadOnlyList<StackLineageEntry> Domains = [];
        internal int DomainCount;
        internal StackDomainSnapshot? Domain;
        internal StackEffectiveTreeSnapshot? Tree;
        internal StackDomainChanges? Changes;
        internal IReadOnlyList<StackConflictSummary> Conflicts = [];
        internal IReadOnlyList<StackConflictProposalSummary> ConflictProposals = [];
        internal Guid? SelectedProposalId;
        internal StackConflict? ReviewConflict;
        internal StackConflictProposal? ReviewProposal;
        internal StackNativeSourceDiff? ReviewDiff;
        internal int ReviewDiffPage;
        internal int ProposalPage;
        internal int ProposalCount;
        internal readonly List<Guid> Navigation = [];
        internal int NavigationIndex = -1;
        internal string? SelectedPath;
        internal string DomainName = "", CommitMessage = "", FileFilter = "";
        internal int FilePage, DiffPage, DomainPage;
    }
    private readonly ProjectTab[] _projects;
    private readonly List<ProjectTab> _openedTabs = [];
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
            "ConflictProposals" => _proposalRows, "ReviewDiffLines" => _reviewDiffRows, "ReviewTarget" => _reviewTarget,
            "IsProject" => _tab is not null, "IsProjectsPage" => _homePage == "Projects", "IsAboutPage" => _homePage == "About", "NoProjects" => _projects.Length == 0, "HomePage" => _homePage,
            "ProjectLabel" => _tab?.Summary?.Name ?? "Stacks",
            "DomainLabel" => _tab?.Domain is { } domain ? $"{domain.Kind} · {domain.Name}" : "Select a domain",
            "ActiveDomainLabel" => _tab?.Domains.SingleOrDefault(entry => entry.Id == _tab?.Summary?.ActiveDomainId) is { } active
                ? $"Active for editor and build · {active.Kind} · {active.Name}"
                : _tab?.Summary?.ActiveDomainId is { } activeId ? $"Active for editor and build · {activeId:D}" : "No active domain",
            "Counts" => _tab?.Changes is { } changes ? $"{changes.Added} added · {changes.Deleted} deleted · {changes.Modified} modified · {changes.Renamed} renamed" : "",
            "Conflicts" => _tab is null ? "" : $"{_tab.Conflicts.Count(c => !c.IsResolved)} unresolved conflicts",
            "ReviewIdentity" => _tab?.ReviewConflict is { } reviewConflict && _tab.ReviewProposal is { } reviewProposal
                ? $"Conflict {reviewConflict.Id:D} · proposal {reviewProposal.Id:D} · {reviewConflict.Path}"
                : "Select a pending conflict proposal to review.",
            "ReviewSummary" => _tab?.ReviewProposal is { } selectedProposal
                ? $"{selectedProposal.State} · proposed by {selectedProposal.ProposedBy} · {selectedProposal.Summary}"
                : "",
            "ReviewDiffDescription" => _tab?.ReviewDiff is { } reviewDiff
                ? reviewDiff.IsText ? "Text diff: current local conflict content versus the proposed resolution."
                    : $"Current local conflict content versus the proposed resolution. {reviewDiff.Description}"
                : "No conflict proposal selected.",
            "ReviewDiffPageLabel" => $"Proposal diff · page {(_tab?.ReviewDiffPage ?? 0) + 1} of {Math.Max(1, ((_tab?.ReviewDiff?.Lines.Count ?? 0) + PageSize - 1) / PageSize)}",
            "ProposalPageLabel" => $"Proposals · page {(_tab?.ProposalPage ?? 0) + 1} of {Math.Max(1, ((_tab?.ProposalCount ?? 0) + PageSize - 1) / PageSize)}",
            "SourceDescription" => _diff?.Description ?? "Select a file to view its effective source and owned changes.",
            "HasDraft" => HasDraft, "CanEditText" => CanEditText, "CanApply" => IsActionAvailable("9to1.Stacks.ApplyText"),
            "CanCreateDomain" => IsActionAvailable("9to1.Stacks.CreateDomain"), "CanCommit" => IsActionAvailable("9to1.Stacks.Commit"),
            "CanNavigate" => !_busy && !_retiring && !HasDraft,
            "DomainPageLabel" => $"Domains · page {(_tab?.DomainPage ?? 0) + 1} of {Math.Max(1, ((_tab?.DomainCount ?? 0) + PageSize - 1) / PageSize)}",
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
            "9to1.Stacks.SelectConflictProposal" => _tab is not null && !HasDraft,
            "9to1.Stacks.RejectConflictProposal" or "9to1.Stacks.AcceptConflictProposalWithoutMerge" or
                "9to1.Stacks.AcceptConflictProposalAndMerge" =>
                !HasDraft && _tab?.ReviewProposal is { State: StackConflictProposalState.PendingReview } &&
                _tab.ReviewConflict is { IsResolved: false } && _reviewTarget is not null &&
                _tab.Source.Actor.Can(StackCapability.Contribute),
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
            "9to1.Stacks.NextDomains" => _tab is not null && (_tab.DomainPage + 1) * PageSize < _tab.DomainCount,
            "9to1.Stacks.PreviousFiles" => _tab?.FilePage > 0,
            "9to1.Stacks.NextFiles" => _tab is not null && (_tab.FilePage + 1) * PageSize < FilteredFiles().Count,
            "9to1.Stacks.PreviousDiff" => _tab?.DiffPage > 0,
            "9to1.Stacks.NextDiff" => _tab is not null && (_tab.DiffPage + 1) * PageSize < (_diff?.Lines.Count ?? 0),
            "9to1.Stacks.PreviousReviewDiff" => _tab?.ReviewDiffPage > 0,
            "9to1.Stacks.NextReviewDiff" => _tab?.ReviewDiff is { } review && (_tab.ReviewDiffPage + 1) * PageSize < review.Lines.Count,
            "9to1.Stacks.PreviousConflictProposals" => _tab?.ProposalPage > 0,
            "9to1.Stacks.NextConflictProposals" => _tab is not null && (_tab.ProposalPage + 1) * PageSize < _tab.ProposalCount,
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
            case "9to1.Stacks.Home": SaveTabFields(_tab); _tab = null; _homePage = "Projects"; ClearSource(); break;
            case "9to1.Stacks.About": SaveTabFields(_tab); _tab = null; _homePage = "About"; ClearSource(); break;
            case "9to1.Stacks.OpenProject":
                var project = DemandRow<ProjectTarget>(parameter);
                if (ReferenceEquals(_tab, project.Tab)) SaveTabFields(_tab);
                var stagedProject = CloneProjectTab(project.Tab);
                await RefreshAsync(stagedProject, token);
                token.ThrowIfCancellationRequested();
                SaveTabFields(_tab);
                CopyProjectTabState(stagedProject, project.Tab);
                if (!_openedTabs.Contains(project.Tab)) _openedTabs.Add(project.Tab);
                _tab = project.Tab;
                LoadTabFields(_tab);
                PresentTab(_tab);
                break;
            case "9to1.Stacks.SelectDomain":
                var domain = DemandRow<DomainTarget>(parameter);
                await NavigateDomainAsync(domain.DomainId, true, token); break;
            case "9to1.Stacks.SelectConflictProposal":
                var selectedProposal = DemandRow<ConflictProposalTarget>(parameter);
                await SelectConflictProposalAsync(selectedProposal, token);
                break;
            case "9to1.Stacks.RejectConflictProposal":
            case "9to1.Stacks.AcceptConflictProposalWithoutMerge":
            case "9to1.Stacks.AcceptConflictProposalAndMerge":
                var reviewTarget = DemandRow<ConflictProposalTarget>(parameter);
                var currentProposal = _tab!.ReviewProposal;
                if (currentProposal is not { State: StackConflictProposalState.PendingReview } || currentProposal.Id != reviewTarget.ProposalId)
                    throw new StackFailureException(StackFailureCode.RevisionConflict,
                        "Select the current pending proposal before reviewing it.", reviewTarget.ProposalId.ToString("D"), recoverable: true, retryable: true);
                var reviewingTab = _tab!;
                bool rejectProposal = command == "9to1.Stacks.RejectConflictProposal";
                bool acceptProposal = !rejectProposal;
                bool mergeProposal = command == "9to1.Stacks.AcceptConflictProposalAndMerge";
                await SourceAsync(() => reviewingTab.Source.Engine.ReviewConflictProposalAsync(
                    reviewTarget.ProposalId, accept: acceptProposal, merge: mergeProposal,
                    actor: reviewingTab.Source.Actor, cancellationToken: token));
                await RefreshAsync(reviewingTab, token);
                _status = rejectProposal ? "Conflict proposal rejected."
                    : mergeProposal ? "Conflict proposal accepted and merged into the selected domain."
                    : "Conflict proposal accepted without merging; its changes remain in the selected domain.";
                break;
            case "9to1.Stacks.Back":
            case "9to1.Stacks.Forward":
                var tab = _tab!; var next = tab.NavigationIndex + (command.EndsWith("Back", StringComparison.Ordinal) ? -1 : 1);
                var requestedDomain = tab.Navigation[next];
                await NavigateDomainAsync(requestedDomain, false, token);
                // A stale history target may have recovered to the live active
                // domain and reset the history; only apply the old index if the
                // requested canonical domain was actually restored.
                if (tab.Domain?.Id == requestedDomain) tab.NavigationIndex = next;
                break;
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
                var requestedActiveDomain = active.Domain!.Id;
                await RefreshAsync(active, token);
                if (active.Domain!.Id != requestedActiveDomain)
                {
                    _status = "The selected domain is no longer available; the project was refreshed without changing its active domain.";
                    break;
                }
                await SourceAsync(() => active.Source.Engine.SetActiveDomainAsync(active.Domain!.Id, active.Source.Actor, token));
                await RefreshAsync(active, token); _status = "Active development domain updated."; break;
            case "9to1.Stacks.Commit":
                var commit = _tab!;
                var revision = await SourceAsync(() => commit.Source.Engine.CreateCommitAsync(commit.Domain!.Id, _fields["CommitMessage"], commit.Source.Actor, cancellationToken: token));
                await RefreshAsync(commit, token); _fields["CommitMessage"] = ""; _status = $"Committed · revision {revision.Sequence}."; break;
            case "9to1.Stacks.PreviousDomains":
                var previousDomains = _tab!;
                await RefreshAsync(previousDomains, token, previousDomains.Domain?.Id ?? previousDomains.Summary?.ActiveDomainId,
                    requestedPageIndex: Math.Max(0, previousDomains.DomainPage - 1)); break;
            case "9to1.Stacks.NextDomains":
                var nextDomains = _tab!;
                await RefreshAsync(nextDomains, token, nextDomains.Domain?.Id ?? nextDomains.Summary?.ActiveDomainId,
                    requestedPageIndex: nextDomains.DomainPage + 1); break;
            case "9to1.Stacks.PreviousFiles": _tab!.FilePage--; PublishFileRows(); break;
            case "9to1.Stacks.NextFiles": _tab!.FilePage++; PublishFileRows(); break;
            case "9to1.Stacks.PreviousDiff": _tab!.DiffPage--; PublishDiffRows(); break;
            case "9to1.Stacks.NextDiff": _tab!.DiffPage++; PublishDiffRows(); break;
            case "9to1.Stacks.PreviousReviewDiff": _tab!.ReviewDiffPage--; PublishReviewDiffRows(); break;
            case "9to1.Stacks.NextReviewDiff": _tab!.ReviewDiffPage++; PublishReviewDiffRows(); break;
            case "9to1.Stacks.PreviousConflictProposals":
                await LoadConflictProposalPageAsync(_tab!, Math.Max(0, _tab!.ProposalPage - 1), token); break;
            case "9to1.Stacks.NextConflictProposals":
                await LoadConflictProposalPageAsync(_tab!, _tab!.ProposalPage + 1, token); break;
            default: throw new InvalidOperationException("Unknown Stacks action.");
        }
        PublishRows();
    }
    private async Task NavigateDomainAsync(Guid id, bool addHistory, CancellationToken token)
    {
        var tab = _tab!;
        // Browsing has its own per-tab selection/history. Only SetActive changes the stored
        // default development domain used by editors/builds.
        tab.SelectedPath = null; await RefreshAsync(tab, token, id, locateRequestedDomain: true);
        if (addHistory)
        {
            if (tab.NavigationIndex + 1 < tab.Navigation.Count) tab.Navigation.RemoveRange(tab.NavigationIndex + 1, tab.Navigation.Count - tab.NavigationIndex - 1);
            // Refresh may recover a stale restore target to the current live
            // domain; history must bind to that actual result, never the request.
            var actualDomain = tab.Domain!.Id;
            if (tab.NavigationIndex < 0)
            {
                tab.Navigation.Add(actualDomain); tab.NavigationIndex = 0;
            }
            else if (tab.Navigation[tab.NavigationIndex] != actualDomain)
            {
                tab.Navigation.Add(actualDomain); tab.NavigationIndex = tab.Navigation.Count - 1;
            }
        }
    }
    private async Task RefreshAsync(ProjectTab tab, CancellationToken token, Guid? requestedDomain = null,
        bool locateRequestedDomain = false, int? requestedPageIndex = null)
    {
        DemandView(tab.Source);
        // Refresh the engine's pinned source revision before reading current state.
        // A stale CAS receipt otherwise seals the engine; re-open is also the
        // explicit recovery boundary after a source conflict. Never rebind a tab
        // to a different canonical project supplied by its current Home owner.
        Guid? boundProjectId = tab.Summary?.ProjectId;
        var reopened = await SourceAsync(() => tab.Source.Engine.OpenProjectAsync(tab.Source.Actor, token));
        if (boundProjectId is { } expected && reopened.ProjectId != expected)
            throw new StackFailureException(StackFailureCode.ProjectNotFound,
                "The source now identifies a different project. This tab was not rebound; reopen the intended project.",
                reopened.ProjectId.ToString("D"), recoverable: true, retryable: true);
        tab.Summary = await SourceAsync(() => tab.Source.Engine.GetProjectSummaryAsync(tab.Source.Actor, token));
        bool recoveredStaleDomain = false;
        Guid browsed;
        StackLineagePage lineage;
        browsed = requestedDomain ?? tab.Domain?.Id ?? tab.Summary.ActiveDomainId;
        int pageIndex = requestedPageIndex ?? tab.DomainPage;
        bool resolveDomainPage = locateRequestedDomain || tab.Domain is null;
        lineage = await SourceAsync(() => tab.Source.Engine.GetLineagePageAsync(tab.Source.Actor, pageIndex,
            PageSize, browsed, resolveDomainPage, token));
        if (lineage.Entries.Count == 0 && lineage.TotalCount > 0)
        {
            pageIndex = (lineage.TotalCount - 1) / PageSize;
            lineage = await SourceAsync(() => tab.Source.Engine.GetLineagePageAsync(tab.Source.Actor, pageIndex,
                PageSize, browsed, resolveDomainPage, token));
        }
        if (!lineage.RequestedDomainFound)
        {
            recoveredStaleDomain = true;
            browsed = tab.Summary.ActiveDomainId;
            lineage = await SourceAsync(() => tab.Source.Engine.GetLineagePageAsync(tab.Source.Actor, pageIndex,
                PageSize, browsed, locateRequestedDomain: true, cancellationToken: token));
            if (!lineage.RequestedDomainFound)
            {
                lineage = await SourceAsync(() => tab.Source.Engine.GetLineagePageAsync(tab.Source.Actor, 0, PageSize, cancellationToken: token));
                browsed = lineage.Entries[0].Id;
            }
        }
        var tree = await SourceAsync(() => tab.Source.Engine.GetEffectiveTreeAsync(browsed, tab.Source.Actor, token));
        var changes = await SourceAsync(() => tab.Source.Engine.GetDomainChangesAsync(tree.DomainId, tab.Source.Actor, token));
        lineage = await SourceAsync(() => tab.Source.Engine.GetLineagePageAsync(tab.Source.Actor, lineage.PageIndex,
            PageSize, tree.DomainId, cancellationToken: token));
        if (!lineage.RequestedDomainFound)
            throw new StackFailureException(StackFailureCode.DomainNotFound, "The selected domain changed while its current page was loading.", tree.DomainId.ToString("D"), recoverable: true, retryable: true);
        StackDomainSnapshot domain = await SourceAsync(() => tab.Source.Engine.GetDomainSnapshotAsync(
            tree.DomainId, tab.Source.Actor, token));
        var conflicts = await SourceAsync(() => tab.Source.Engine.GetConflictSummariesAsync(domain.Id, tab.Source.Actor, token));
        StackConflictProposalPage proposalPage = await SourceAsync(() => tab.Source.Engine.GetConflictProposalsPageAsync(
            tab.Source.Actor, domain.Id, tab.ProposalPage, PageSize, tab.SelectedProposalId,
            locateRequestedProposal: tab.SelectedProposalId is not null, cancellationToken: token));
        if (proposalPage.Entries.Count == 0 && proposalPage.TotalCount > 0)
        {
            int lastProposalPage = (proposalPage.TotalCount - 1) / PageSize;
            proposalPage = await SourceAsync(() => tab.Source.Engine.GetConflictProposalsPageAsync(
                tab.Source.Actor, domain.Id, lastProposalPage, PageSize, tab.SelectedProposalId,
                locateRequestedProposal: tab.SelectedProposalId is not null, cancellationToken: token));
        }
        else if (proposalPage.TotalCount == 0 && proposalPage.PageIndex != 0)
        {
            proposalPage = await SourceAsync(() => tab.Source.Engine.GetConflictProposalsPageAsync(
                tab.Source.Actor, domain.Id, 0, PageSize, cancellationToken: token));
        }
        DemandView(tab.Source);
        if (_retiring) return;
        tab.Tree = tree; tab.Changes = changes; tab.Domains = lineage.Entries; tab.DomainCount = lineage.TotalCount;
        tab.DomainPage = lineage.PageIndex; tab.Domain = domain; tab.Conflicts = conflicts;
        tab.ConflictProposals = proposalPage.Entries;
        tab.ProposalCount = proposalPage.TotalCount;
        tab.ProposalPage = proposalPage.PageIndex;
        tab.ReviewProposal = tab.SelectedProposalId is not null && proposalPage.RequestedProposalFound
            ? proposalPage.RequestedProposal
            : null;
        tab.ReviewConflict = null;
        if (tab.ReviewProposal is { } currentProposal && conflicts.Any(conflict => conflict.Id == currentProposal.ConflictId))
        {
            StackConflict reviewConflict = await SourceAsync(() => tab.Source.Engine.GetConflictSnapshotAsync(
                currentProposal.ConflictId, tab.Source.Actor, token));
            if (reviewConflict.DomainId != domain.Id)
                throw new StackFailureException(StackFailureCode.SourceCorrupt,
                    "The selected conflict proposal does not belong to the current domain.", currentProposal.Id.ToString("D"), recoverable: true);
            tab.ReviewConflict = reviewConflict;
        }
        if (tab.ReviewProposal is null || tab.ReviewConflict is null)
        {
            tab.SelectedProposalId = null; tab.ReviewProposal = null; tab.ReviewConflict = null; tab.ReviewDiff = null;
        }
        else tab.ReviewDiff = StackNativeSourceDiff.Read(tab.ReviewConflict.Local, tab.ReviewProposal.ProposedResource);
        if (recoveredStaleDomain)
        {
            tab.Navigation.Clear();
            tab.Navigation.Add(domain.Id);
            tab.NavigationIndex = 0;
        }
        else if (tab.NavigationIndex < 0) { tab.Navigation.Add(domain.Id); tab.NavigationIndex = 0; }
        tab.Status = recoveredStaleDomain
            ? "The selected domain is no longer available; showing the current project domain. Pending fields are kept until applied or discarded."
            : "Project loaded from the current canonical source. Pending fields are kept until applied or discarded.";
        if (ReferenceEquals(_tab, tab)) PresentTab(tab);
    }
    private void ClearSource()
    {
        if (_tab is not null) { _tab.SelectedPath = null; _tab.DiffPage = 0; }
        _diff = null; _fields["SourcePath"] = ""; _fields["SourceText"] = _loadedText = "";
    }
    private void SaveTabFields(ProjectTab? tab)
    {
        if (tab is null) return;
        tab.DomainName = _fields["DomainName"];
        tab.CommitMessage = _fields["CommitMessage"];
        tab.FileFilter = _fields["FileFilter"];
    }
    private async Task SelectConflictProposalAsync(ConflictProposalTarget target, CancellationToken token)
    {
        var tab = _tab;
        if (tab is null || target.DomainId != tab.Domain?.Id)
            throw new InvalidOperationException("The conflict proposal is not part of the selected domain.");
        var proposalSummary = tab.ConflictProposals.SingleOrDefault(item => item.Id == target.ProposalId && item.ConflictId == target.ConflictId)
            ?? throw new StackFailureException(StackFailureCode.DomainNotFound,
                "The conflict proposal is no longer available in this domain.", target.ProposalId.ToString("D"), recoverable: true, retryable: true);
        var conflictSummary = tab.Conflicts.SingleOrDefault(item => item.Id == target.ConflictId)
            ?? throw new StackFailureException(StackFailureCode.DomainNotFound,
                "The conflict is no longer available in this domain.", target.ConflictId.ToString("D"), recoverable: true, retryable: true);
        StackConflictProposal proposal = await SourceAsync(() => tab.Source.Engine.GetConflictProposalSnapshotAsync(
            target.ProposalId, tab.Source.Actor, token));
        StackConflict conflict = await SourceAsync(() => tab.Source.Engine.GetConflictSnapshotAsync(
            target.ConflictId, tab.Source.Actor, token));
        if (proposal.ConflictId != target.ConflictId || conflict.DomainId != target.DomainId ||
            conflictSummary.DomainId != target.DomainId)
            throw new StackFailureException(StackFailureCode.SourceCorrupt,
                "The selected conflict proposal does not belong to the current domain.", target.ProposalId.ToString("D"), recoverable: true);
        tab.SelectedProposalId = proposal.Id; tab.ReviewProposal = proposal; tab.ReviewConflict = conflict;
        tab.ReviewDiff = StackNativeSourceDiff.Read(conflict.Local, proposal.ProposedResource);
        tab.ReviewDiffPage = 0;
    }
    private async Task LoadConflictProposalPageAsync(ProjectTab tab, int pageIndex, CancellationToken token)
    {
        StackConflictProposalPage page = await SourceAsync(() => tab.Source.Engine.GetConflictProposalsPageAsync(
            tab.Source.Actor, tab.Domain!.Id, pageIndex, PageSize, tab.SelectedProposalId, cancellationToken: token));
        if (page.Entries.Count == 0 && page.TotalCount > 0)
        {
            int lastPage = (page.TotalCount - 1) / PageSize;
            page = await SourceAsync(() => tab.Source.Engine.GetConflictProposalsPageAsync(
                tab.Source.Actor, tab.Domain!.Id, lastPage, PageSize, tab.SelectedProposalId, cancellationToken: token));
        }
        else if (page.TotalCount == 0 && page.PageIndex != 0)
        {
            page = await SourceAsync(() => tab.Source.Engine.GetConflictProposalsPageAsync(
                tab.Source.Actor, tab.Domain!.Id, 0, PageSize, cancellationToken: token));
        }
        tab.ConflictProposals = page.Entries;
        tab.ProposalCount = page.TotalCount;
        tab.ProposalPage = page.PageIndex;
        if (tab.SelectedProposalId is { } selectedId && page.RequestedProposalFound && page.RequestedProposal is { } selected)
        {
            StackConflictSummary? conflictSummary = tab.Conflicts.SingleOrDefault(conflict => conflict.Id == selected.ConflictId);
            if (conflictSummary is not null)
            {
                StackConflict conflict = await SourceAsync(() => tab.Source.Engine.GetConflictSnapshotAsync(
                    selected.ConflictId, tab.Source.Actor, token));
                if (conflict.DomainId != tab.Domain!.Id || conflictSummary.DomainId != tab.Domain.Id ||
                    selected.ConflictId != conflict.Id)
                    throw new StackFailureException(StackFailureCode.SourceCorrupt,
                        "The selected conflict proposal does not belong to the current domain.", selectedId.ToString("D"), recoverable: true);
                tab.ReviewProposal = selected;
                tab.ReviewConflict = conflict;
                tab.ReviewDiff = StackNativeSourceDiff.Read(conflict.Local, selected.ProposedResource);
            }
            else
            {
                tab.SelectedProposalId = null; tab.ReviewProposal = null; tab.ReviewConflict = null; tab.ReviewDiff = null;
            }
        }
        else if (tab.SelectedProposalId is not null)
        {
            tab.SelectedProposalId = null; tab.ReviewProposal = null; tab.ReviewConflict = null; tab.ReviewDiff = null;
        }
    }
    private void LoadTabFields(ProjectTab tab)
    {
        _fields["DomainName"] = tab.DomainName;
        _fields["CommitMessage"] = tab.CommitMessage;
        _fields["FileFilter"] = tab.FileFilter;
    }
    private void PresentTab(ProjectTab tab)
    {
        _snapshot = new();
        if (tab.SelectedPath is { } path && tab.Tree is { } tree && tab.Domain is { } domain &&
            (tree.Files.ContainsKey(path) || domain.BaseTree.ContainsKey(path))) SelectFile(path);
        else ClearSource();
        _status = tab.Status;
    }
    private static ProjectTab CloneProjectTab(ProjectTab source)
    {
        var clone = new ProjectTab(source.Source)
        {
            Summary = source.Summary, Status = source.Status, Domains = source.Domains, DomainCount = source.DomainCount,
            Domain = source.Domain, Tree = source.Tree, Changes = source.Changes, Conflicts = source.Conflicts,
            ConflictProposals = source.ConflictProposals, SelectedProposalId = source.SelectedProposalId,
            ReviewConflict = source.ReviewConflict, ReviewProposal = source.ReviewProposal, ReviewDiff = source.ReviewDiff,
            ReviewDiffPage = source.ReviewDiffPage, ProposalPage = source.ProposalPage, ProposalCount = source.ProposalCount,
            NavigationIndex = source.NavigationIndex, SelectedPath = source.SelectedPath,
            DomainName = source.DomainName, CommitMessage = source.CommitMessage, FileFilter = source.FileFilter,
            FilePage = source.FilePage, DiffPage = source.DiffPage, DomainPage = source.DomainPage,
        };
        clone.Navigation.AddRange(source.Navigation);
        return clone;
    }
    private static void CopyProjectTabState(ProjectTab source, ProjectTab target)
    {
        target.Summary = source.Summary; target.Status = source.Status; target.Domains = source.Domains; target.DomainCount = source.DomainCount;
        target.Domain = source.Domain; target.Tree = source.Tree; target.Changes = source.Changes; target.Conflicts = source.Conflicts;
        target.ConflictProposals = source.ConflictProposals; target.SelectedProposalId = source.SelectedProposalId;
        target.ReviewConflict = source.ReviewConflict; target.ReviewProposal = source.ReviewProposal;
        target.ReviewDiff = source.ReviewDiff; target.ReviewDiffPage = source.ReviewDiffPage;
        target.ProposalPage = source.ProposalPage; target.ProposalCount = source.ProposalCount;
        target.Navigation.Clear(); target.Navigation.AddRange(source.Navigation); target.NavigationIndex = source.NavigationIndex;
        target.SelectedPath = source.SelectedPath; target.DomainName = source.DomainName;
        target.CommitMessage = source.CommitMessage; target.FileFilter = source.FileFilter;
        target.FilePage = source.FilePage; target.DiffPage = source.DiffPage; target.DomainPage = source.DomainPage;
    }
    private void SelectFile(string path)
    {
        var tab = _tab!; tab.SelectedPath = path; tab.DiffPage = 0;
        tab.Domain!.BaseTree.TryGetValue(path, out var before); tab.Tree!.Files.TryGetValue(path, out var current);
        _diff = StackNativeSourceDiff.Read(before, current);
        _fields["SourcePath"] = path; _fields["SourceText"] = _loadedText = _diff.CurrentText ?? "";
    }
}
