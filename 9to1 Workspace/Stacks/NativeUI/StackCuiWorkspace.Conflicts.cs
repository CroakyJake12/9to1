using System.Text;

namespace HavenOS.Apps.Stacks.NativeUI;

public sealed partial class StackCuiWorkspace
{
    private const int ProposalPageSize = 20;
    private const int ReviewTextLimit = 16 * 1024;
    private sealed record ConflictProposalTarget(StackCuiWorkspace Owner, object Snapshot, ProjectTab Tab, Guid ProposalId)
        : RowTarget(Owner, Snapshot);
    private sealed record ActiveConflictReview(ProjectTab Tab, StackConflictProposalReview Original);
    private ActiveConflictReview? _activeConflictReview;
    private IReadOnlyList<StackNativeRow> _conflictRows = [];
    private string _conflictBase = "", _conflictLocal = "", _conflictIncoming = "", _conflictProposed = "";

    private void PublishConflictRows()
    {
        if (_tab is not { } tab) { _conflictRows = []; return; }
        tab.ProposalPage = Math.Clamp(tab.ProposalPage, 0, Math.Max(0, (tab.PendingProposals.Count - 1) / ProposalPageSize));
        var paths = tab.Conflicts.ToDictionary(conflict => conflict.Id, conflict => conflict.Path);
        _conflictRows = tab.PendingProposals.Skip(tab.ProposalPage * ProposalPageSize).Take(ProposalPageSize)
            .Select(proposal => Issue(proposal.Id.ToString("D"), $"Review {paths[proposal.ConflictId]} · {proposal.ProposedBy}",
                new ConflictProposalTarget(this, _snapshot, tab, proposal.Id))).ToArray();
    }

    private bool TryGetConflictValue(string path, out object? value)
    {
        var active = _activeConflictReview;
        value = path switch
        {
            "HasConflictProposals" => _conflictRows.Count > 0,
            "ConflictProposals" => _conflictRows,
            "HasConflictReview" => active is not null,
            "ConflictReviewTarget" => active,
            "CanReviewConflicts" => IsActionAvailable("9to1.Stacks.ConflictShow"),
            "CanRejectConflictProposal" => IsActionAvailable("9to1.Stacks.ConflictReject"),
            "CanAcceptConflictProposal" => IsActionAvailable("9to1.Stacks.ConflictAccept"),
            "ConflictReviewPageLabel" => $"Proposals · page {(_tab?.ProposalPage ?? 0) + 1}",
            "ConflictReviewPath" => active?.Original.Conflict.Path ?? "",
            "ConflictReviewRevisions" => active is null ? "" :
                $"Base {active.Original.Conflict.BaseRevisionId:D} · Domain head {active.Original.DomainHeadRevisionId:D} · Recorded incoming {active.Original.Conflict.IncomingRevisionId:D}",
            "ConflictReviewDescription" => active?.Original.Proposal.Summary ?? "",
            "ConflictReviewRestriction" => active?.Original.AcceptanceBlocker
                ?? "Reject leaves the conflict unresolved. Accept without Merging stages a working change. Accept and Merge commits it to this domain only.",
            "ConflictReviewBase" => active is null ? "" : _conflictBase,
            "ConflictReviewLocal" => active is null ? "" : _conflictLocal,
            "ConflictReviewIncoming" => active is null ? "" : _conflictIncoming,
            "ConflictReviewProposed" => active is null ? "" : _conflictProposed,
            _ => null
        };
        return value is not null || path == "ConflictReviewTarget";
    }

    private bool CanUseConflictCommand(string command)
    {
        if (_tab is not { } tab || HasDraft) return false;
        var review = _activeConflictReview;
        var canReview = review is not null && ReferenceEquals(review.Tab, tab) && tab.Source.Actor.Can(StackCapability.Contribute);
        return command switch
        {
            "9to1.Stacks.ConflictShow" => tab.PendingProposals.Count > 0 && tab.Source.Actor.Can(StackCapability.ViewSource),
            "9to1.Stacks.ConflictClose" => review is not null,
            "9to1.Stacks.ConflictPrevious" => tab.ProposalPage > 0,
            "9to1.Stacks.ConflictNext" => (tab.ProposalPage + 1) * ProposalPageSize < tab.PendingProposals.Count,
            "9to1.Stacks.ConflictReject" => canReview,
            "9to1.Stacks.ConflictAccept" or "9to1.Stacks.ConflictMerge" => canReview && review!.Original.AcceptanceBlocker is null,
            _ => false
        };
    }

    private ActiveConflictReview DemandActiveConflictReview(object? parameter) =>
        parameter is ActiveConflictReview reviewed && ReferenceEquals(reviewed, _activeConflictReview) && ReferenceEquals(reviewed.Tab, _tab)
            ? reviewed : throw new InvalidOperationException("Open the exact current proposal before reviewing it.");

    private async Task RunConflictCommandAsync(string command, object? parameter, CancellationToken token)
    {
        var tab = _tab!;
        switch (command)
        {
            case "9to1.Stacks.ConflictShow":
                var target = DemandRow<ConflictProposalTarget>(parameter);
                if (!ReferenceEquals(target.Tab, tab)) throw new InvalidOperationException("This proposal belongs to another project tab.");
                var observed = await SourceAsync(() => tab.Source.Engine.ReadConflictProposalReviewAsync(
                    tab.Domain!.Id, target.ProposalId, tab.Source.Actor, token));
                if (_retiring) return;
                if (observed is null)
                {
                    tab.PendingProposals = tab.PendingProposals.Where(p => p.Id != target.ProposalId).ToArray();
                    ClearConflictReview();
                    _status = "This conflict proposal is no longer pending.";
                }
                else
                {
                    _activeConflictReview = new(tab, observed);
                    _conflictBase = PreviewConflictResource(observed.Conflict.Base);
                    _conflictLocal = PreviewConflictResource(observed.Conflict.Local);
                    _conflictIncoming = PreviewConflictResource(observed.Conflict.Incoming);
                    _conflictProposed = PreviewConflictResource(observed.Proposal.ProposedResource);
                }
                break;
            case "9to1.Stacks.ConflictClose": ClearConflictReview(); break;
            case "9to1.Stacks.ConflictPrevious": ClearConflictReview(); tab.ProposalPage--; break;
            case "9to1.Stacks.ConflictNext": ClearConflictReview(); tab.ProposalPage++; break;
            case "9to1.Stacks.ConflictReject":
            case "9to1.Stacks.ConflictAccept":
            case "9to1.Stacks.ConflictMerge":
                var review = DemandActiveConflictReview(parameter);
                var outcome = await SourceAsync(() => tab.Source.Engine.ReviewObservedConflictProposalAsync(review.Original,
                    accept: command != "9to1.Stacks.ConflictReject", merge: command == "9to1.Stacks.ConflictMerge", tab.Source.Actor, token));
                if (_retiring) return;
                if (outcome.State is StackConflictProposalState.AcceptedAndMerged or StackConflictProposalState.AcceptedWithoutMerge)
                    await RefreshAsync(tab, token);
                else
                {
                    // A reject changes only its record. A refusal must not cause a source refresh/write.
                    if (outcome.State == StackConflictProposalState.Rejected)
                        tab.PendingProposals = tab.PendingProposals.Where(p => p.Id != review.Original.Proposal.Id).ToArray();
                    ClearConflictReview();
                }
                _status = outcome.State switch
                {
                    StackConflictProposalState.Rejected => "Proposal rejected. No source changed.",
                    StackConflictProposalState.AcceptedWithoutMerge => "Proposal accepted as a working change. Nothing was committed.",
                    StackConflictProposalState.AcceptedAndMerged => "Proposal accepted and committed to this domain. Nothing was pushed.",
                    _ => outcome.Refusal ?? "No resolution was recorded."
                };
                break;
        }
        PublishRows();
    }

    private void ClearConflictReview()
    {
        _activeConflictReview = null;
        _conflictBase = _conflictLocal = _conflictIncoming = _conflictProposed = "";
    }

    private static string PreviewConflictResource(StackResource? resource)
    {
        if (resource is null) return "File absent";
        if (resource.IsBinary) return $"Binary file · {resource.Content.Length:N0} bytes";
        if (resource.Content.Length > ReviewTextLimit) return $"Text preview omitted · {resource.Content.Length:N0} bytes exceeds the 16 KiB preview limit.";
        try
        {
            var text = new UTF8Encoding(false, true).GetString(resource.Content);
            if (text.Contains('\0')) return "Binary data · not displayed as text.";
            return text.Length == 0 ? "Empty file" : text;
        }
        catch (DecoderFallbackException) { return "Non-UTF-8 file · not displayed as text."; }
    }
}
