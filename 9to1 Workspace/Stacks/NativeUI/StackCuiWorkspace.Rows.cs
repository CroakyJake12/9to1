using Haven.Infrastructure;

namespace HavenOS.Apps.Stacks.NativeUI;

public sealed record StackNativeRow(string Key, string Label, object Target);
public sealed record StackNativeDiffRow(string Key, string BeforeNumber, string AfterNumber,
    string BeforeText, string AfterText, string Change);

public sealed partial class StackCuiWorkspace
{
    private const int PageSize = 100;
    private abstract record RowTarget(StackCuiWorkspace Owner, object Snapshot);
    private sealed record ProjectTarget(StackCuiWorkspace Owner, object Snapshot, ProjectTab Tab) : RowTarget(Owner, Snapshot);
    private sealed record DomainTarget(StackCuiWorkspace Owner, object Snapshot, ProjectTab Tab, Guid DomainId) : RowTarget(Owner, Snapshot);
    private sealed record FileTarget(StackCuiWorkspace Owner, object Snapshot, ProjectTab Tab, string Path) : RowTarget(Owner, Snapshot);
    private sealed record ConflictProposalTarget(StackCuiWorkspace Owner, object Snapshot, ProjectTab Tab,
        Guid DomainId, Guid ConflictId, Guid ProposalId) : RowTarget(Owner, Snapshot);
    private readonly HashSet<object> _issuedRows = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<StackNativeRow> _tabRows = [], _projectRows = [], _domainRows = [], _fileRows = [], _proposalRows = [];
    private IReadOnlyList<StackNativeDiffRow> _diffRows = [], _reviewDiffRows = [];
    private ConflictProposalTarget? _reviewTarget;
    private T DemandRow<T>(object? parameter) where T : RowTarget
    {
        if (parameter is not T target || !ReferenceEquals(target.Owner, this) || !ReferenceEquals(target.Snapshot, _snapshot) || !_issuedRows.Contains(target))
            throw new InvalidOperationException("This Stacks row belongs to a previous view or another workspace.");
        if (target is DomainTarget domain && !ReferenceEquals(domain.Tab, _tab) ||
            target is FileTarget file && !ReferenceEquals(file.Tab, _tab) ||
            target is ConflictProposalTarget proposal && (!ReferenceEquals(proposal.Tab, _tab) || proposal.DomainId != _tab?.Domain?.Id))
            throw new InvalidOperationException("This Stacks row belongs to another project tab.");
        return target;
    }
    private StackNativeRow Issue(string key, string label, RowTarget target)
    { _issuedRows.Add(target); return new(key, label, target); }
    private void PublishRows()
    {
        _issuedRows.Clear();
        _projectRows = _projects.Where(p => p.Summary is not null).Select(p => Issue(p.Summary!.ProjectId.ToString("D"), p.Summary.Name,
            new ProjectTarget(this, _snapshot, p))).ToArray();
        _tabRows = _openedTabs.Where(tab => tab.Summary is not null).Select(tab => Issue(tab.Summary!.ProjectId.ToString("D"),
            tab.Summary.Name + (ReferenceEquals(tab, _tab) ? " · current" : ""), new ProjectTarget(this, _snapshot, tab))).ToArray();
        _reviewTarget = null;
        if (_tab is null) { _domainRows = []; _proposalRows = []; }
        else
        {
            _domainRows = _tab.Domains.Select(item => Issue(item.Id.ToString("D"),
                $"{new string(' ', item.Depth * 2)}{item.Kind} · {item.Name}{(item.IsActive ? " · active" : "")}{(item.WorkingChangeCount > 0 ? " ↑" : "")}",
                new DomainTarget(this, _snapshot, _tab, item.Id))).ToArray();
            var conflicts = _tab.Conflicts.ToDictionary(conflict => conflict.Id);
            var proposals = _tab.ConflictProposals.Where(proposal => conflicts.ContainsKey(proposal.ConflictId)).ToArray();
            _proposalRows = proposals.Select(proposal =>
                {
                    StackConflictSummary conflict = conflicts[proposal.ConflictId];
                    var target = new ConflictProposalTarget(this, _snapshot, _tab, _tab.Domain!.Id, conflict.Id, proposal.Id);
                    if (_tab.SelectedProposalId == proposal.Id) _reviewTarget = target;
                    return Issue($"{conflict.Id:D}/{proposal.Id:D}",
                        $"{conflict.Path} · proposal {proposal.Id:D} · {proposal.State} · {proposal.Summary}", target);
                }).ToArray();
            if (_reviewTarget is null && _tab.ReviewProposal is { } selectedProposal &&
                conflicts.TryGetValue(selectedProposal.ConflictId, out StackConflictSummary? selectedConflict))
            {
                _reviewTarget = new ConflictProposalTarget(this, _snapshot, _tab, _tab.Domain!.Id,
                    selectedConflict.Id, selectedProposal.Id);
                _issuedRows.Add(_reviewTarget);
            }
        }
        PublishFileRows(); PublishDiffRows(); PublishReviewDiffRows();
    }
    private IReadOnlyList<string> FilteredFiles()
    {
        if (_tab?.Tree is null || _tab.Domain is null) return [];
        var filter = _fields["FileFilter"];
        return _tab.Tree.Files.Keys.Union(_tab.Domain.BaseTree.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(path => string.IsNullOrEmpty(filter) || path.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private void PublishFileRows()
    {
        _issuedRows.RemoveWhere(row => row is FileTarget);
        var all = FilteredFiles();
        if (_tab is not null) _tab.FilePage = Math.Clamp(_tab.FilePage, 0, Math.Max(0, (all.Count - 1) / PageSize));
        _fileRows = _tab is null ? [] : all.Skip(_tab.FilePage * PageSize).Take(PageSize).Select(path =>
        {
            var domain = _tab.Domain!; var current = _tab.Tree!.Files.GetValueOrDefault(path); var before = domain.BaseTree.GetValueOrDefault(path);
            var owned = _tab.Changes!.Changes.Any(change => string.Equals(change.Path, path, StringComparison.OrdinalIgnoreCase) || string.Equals(change.RenameTo, path, StringComparison.OrdinalIgnoreCase));
            var suffix = current is null ? " · deleted" : before is null ? " · added" : owned ? " · owned change" : " · inherited";
            if (current?.IsBinary == true || before?.IsBinary == true) suffix += " · binary";
            return Issue(path, path + suffix, new FileTarget(this, _snapshot, _tab, path));
        }).ToArray();
    }
    private void PublishDiffRows()
    {
        var lines = _diff?.Lines ?? [];
        if (_tab is not null) _tab.DiffPage = Math.Clamp(_tab.DiffPage, 0, Math.Max(0, (lines.Count - 1) / PageSize));
        _diffRows = lines.Skip((_tab?.DiffPage ?? 0) * PageSize).Take(PageSize).Select((line, index) => new StackNativeDiffRow(
            ((_tab?.DiffPage ?? 0) * PageSize + index).ToString(System.Globalization.CultureInfo.InvariantCulture),
            line.BeforeLineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            line.AfterLineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            line.BeforeText ?? "", line.AfterText ?? "", line.Kind switch
            { SourceTextDiffKind.Added => "+", SourceTextDiffKind.Removed => "−", _ => "" })).ToArray();
    }
    private void PublishReviewDiffRows()
    {
        var lines = _tab?.ReviewDiff?.Lines ?? [];
        if (_tab is not null) _tab.ReviewDiffPage = Math.Clamp(_tab.ReviewDiffPage, 0, Math.Max(0, (lines.Count - 1) / PageSize));
        int page = _tab?.ReviewDiffPage ?? 0;
        _reviewDiffRows = lines.Skip(page * PageSize).Take(PageSize).Select((line, index) => new StackNativeDiffRow(
            (page * PageSize + index).ToString(System.Globalization.CultureInfo.InvariantCulture),
            line.BeforeLineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            line.AfterLineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            line.BeforeText ?? "", line.AfterText ?? "", line.Kind switch
            { SourceTextDiffKind.Added => "+", SourceTextDiffKind.Removed => "−", _ => "" })).ToArray();
    }
    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = item switch
        {
            StackNativeRow row => path switch { "Key" => row.Key, "Label" => row.Label, "Target" => row.Target, _ => null },
            StackNativeDiffRow row => path switch { "Key" => row.Key, "BeforeNumber" => row.BeforeNumber, "AfterNumber" => row.AfterNumber,
                "BeforeText" => row.BeforeText, "AfterText" => row.AfterText, "Change" => row.Change, _ => null }, _ => null
        };
        return value is not null;
    }
    public bool TrySetItemValue(object item, string path, object? value) => false;
}
