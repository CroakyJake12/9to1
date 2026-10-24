using Haven.Desktop.Services;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    // Private view identities select observations, never Files identities, grants or review tokens.
    private sealed record ManualRecoveryCandidate(Guid ViewId, DeveloperDraftRecoveryTab SavedDraft,
        string CurrentSourceText, string CurrentSourceSha256);
    private sealed record ManualRecoveryCandidateItem(string SelectionKey, string Label, string AccessibleName, bool CanInspect);
    private readonly List<ManualRecoveryCandidate> _manualRecoveryCandidates = [];
    private Guid? _selectedManualRecoveryCandidate;

    private ManualRecoveryCandidate? SelectedManualRecoveryCandidate() =>
        _manualRecoveryCandidates.SingleOrDefault(value => value.ViewId == _selectedManualRecoveryCandidate);

    private void StageManualRecoveryConflicts(DeveloperManualRecoveryState restored)
    {
        _manualRecoveryCandidates.Clear();
        _manualRecoveryCandidates.AddRange(restored.Tabs.Select(value => new ManualRecoveryCandidate(Guid.NewGuid(),
            value.Draft, value.FreshBaselineText, value.FreshSourceSha256)));
        _selectedManualRecoveryCandidate = _manualRecoveryCandidates.FirstOrDefault(value =>
            !StringComparer.OrdinalIgnoreCase.Equals(value.SavedDraft.OriginalSha256, value.CurrentSourceSha256))?.ViewId
            ?? _manualRecoveryCandidates.FirstOrDefault()?.ViewId;
        _manualRecoveryRequested = null; _manualRecoveryAcknowledged = null;
        // The original encrypted multi-tab snapshot/generation is left unchanged until ALL
        // candidates have been explicitly reconciled and a new complete payload can be saved.
    }

    private bool TryGetManualRecoveryConflictValue(string path, out object? value)
    {
        var selected = SelectedManualRecoveryCandidate();
        value = path switch
        {
            "HasRecoveredConflicts" => _manual && _manualRecoveryCandidates.Count != 0,
            "RecoveredDraftCandidates" => _manualRecoveryCandidates.Select(candidate => new ManualRecoveryCandidateItem(
                candidate.ViewId.ToString("D"), candidate.SavedDraft.RelativePath +
                    (StringComparer.OrdinalIgnoreCase.Equals(candidate.SavedDraft.OriginalSha256, candidate.CurrentSourceSha256)
                        ? " — saved draft" : " — source changed"),
                "Inspect current source and retained protected draft for " + candidate.SavedDraft.RelativePath, !_pending)).ToArray(),
            "RecoveredDraftSummary" => selected is null ? "Select a protected draft to inspect."
                : $"{selected.SavedDraft.RelativePath}\nSaved baseline SHA-256: {selected.SavedDraft.OriginalSha256}\nCurrent source SHA-256: {selected.CurrentSourceSha256}\nRetained draft SHA-256: {DeveloperManualSourceEditOwner.Sha256(selected.SavedDraft.DraftText)}\nReconciliation changes only the editor basis. Review and Home write approval are still required before source changes.",
            "RecoveredCurrentSource" => selected?.CurrentSourceText ?? "",
            "RecoveredRetainedDraft" => selected?.SavedDraft.DraftText ?? "",
            "CanReconcileRecoveredDraft" => IsActionAvailable("dev.reconcile-recovered-draft"),
            _ => null
        };
        return path is "HasRecoveredConflicts" or "RecoveredDraftCandidates" or "RecoveredDraftSummary" or
            "RecoveredCurrentSource" or "RecoveredRetainedDraft" or "CanReconcileRecoveredDraft";
    }

    private ValueTask DispatchManualRecoveryConflictAsync(string command, object? parameter, CancellationToken token)
    {
        ManualRecoveryCandidate candidate; long generation;
        lock (_gate)
        {
            if (IsActionAvailable(command) != true) throw new InvalidOperationException("Protected draft reconciliation is unavailable.");
            if (command == "dev.inspect-recovered-draft")
            {
                if (parameter is not string key || !Guid.TryParse(key, out var id) ||
                    _manualRecoveryCandidates.SingleOrDefault(value => value.ViewId == id) is not { } actual)
                    throw new InvalidOperationException("Choose this workbench's actual protected draft observation.");
                candidate = actual;
            }
            else candidate = SelectedManualRecoveryCandidate() ?? throw new InvalidOperationException("Select a protected draft first.");
            _pending = true; generation = _generation;
        }
        try
        {
            return new(_work.RunAsync(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation));
                var resumeRecovery = false;
                try
                {
                    original.DemandPublication(); token.ThrowIfCancellationRequested();
                    var project = await ResolveProjectAsync(original, token).ConfigureAwait(false);
                    // Reconciliation never substitutes the cached tray text for a fresh actual
                    // Home/Files/kernel READ. Denied/read/cleanup faults retain existing drafts.
                    var fresh = await original.AwaitAsync(InvokeOriginalSource(() =>
                        _manualReader!.ReadFileAsync(project, candidate.SavedDraft.RelativePath, token))).ConfigureAwait(false);
                    var freshSha = DeveloperManualSourceEditOwner.Sha256(fresh);
                    await PublishAsync(original, () =>
                    {
                        var position = _manualRecoveryCandidates.FindIndex(value => ReferenceEquals(value, candidate));
                        if (position < 0 || _project.Reference != project.Reference)
                        {
                            _status = "The recovery observation changed while source was read. All live drafts were preserved; inspect it again.";
                            return;
                        }
                        if (command == "dev.reconcile-recovered-draft" && _selectedManualRecoveryCandidate != candidate.ViewId)
                        {
                            _status = "Recovery selection changed; no draft was substituted.";
                            return;
                        }
                        var refreshed = candidate with { CurrentSourceText = fresh, CurrentSourceSha256 = freshSha };
                        _manualRecoveryCandidates[position] = refreshed; _selectedManualRecoveryCandidate = refreshed.ViewId;
                        if (command == "dev.inspect-recovered-draft")
                        {
                            _status = "Current source and the retained draft are shown separately. Use the explicit reconciliation action only after comparing them.";
                            return;
                        }
                        var adoption = PrepareExplicitManualRecovery(candidate.SavedDraft, candidate.CurrentSourceSha256, fresh);
                        if (adoption is null)
                        {
                            _status = "Source changed again after inspection. The comparison was refreshed; inspect it before explicitly reconciling again.";
                            return;
                        }
                        StoreSelectedTab();
                        var existing = _sourceTabs.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.Path, adoption.RelativePath));
                        if (existing is not null && IsTabDirty(existing))
                        {
                            _status = "This file already has a different live draft. Both drafts were preserved; resolve that tab before adopting the protected one.";
                            return;
                        }
                        if (existing is null && _sourceTabs.Count >= DeveloperSourceReviewSession.MaximumOpenDocuments)
                        {
                            _status = "Close a saved source tab before adopting another recovered draft. No pending or live draft was discarded.";
                            return;
                        }
                        // This explicit action alone chooses the new basis. No source mutation,
                        // review decision, old change set or write permission is resurrected.
                        var nextDraftRevision = checked((existing?.Manual?.DraftRevision ?? 0) + 1);
                        var manual = new ManualTabState(adoption.RetainedDraftText, adoption.CurrentSourceText,
                            adoption.CurrentSourceText, "", adoption.CurrentSourceSha256, "", nextDraftRevision, -1, false);
                        var tab = existing ?? new SourceTab(Guid.NewGuid(), adoption.RelativePath, null, manual);
                        tab.Manual = manual; tab.Editor = null; tab.Pass = null; tab.IsPinned = true;
                        if (existing is null) _sourceTabs.Add(tab);
                        SelectTab(tab);
                        _manualRecoveryCandidates.RemoveAt(position);
                        _selectedManualRecoveryCandidate = _manualRecoveryCandidates.FirstOrDefault()?.ViewId;
                        _status = $"Recovered draft adopted against current source {adoption.CurrentSourceSha256}. Review it before applying; source files have not changed.";
                        if (_manualRecoveryCandidates.Count == 0)
                        {
                            _manualRecoveryRequested = null; _manualRecoveryAcknowledged = null;
                            resumeRecovery = true;
                        }
                    }).ConfigureAwait(false);
                    if (resumeRecovery) ScheduleManualRecoverySave(forceCompleteSnapshot: true);
                }
                catch (Exception error) { original.Retain(error); }
                finally
                {
                    lock (_gate) _pending = false;
                    if (IsCurrent(generation))
                        try { await PublishAsync(original, () => { }).ConfigureAwait(false); }
                        catch (Exception error) { original.Retain(error); }
                    original.ThrowRetained();
                }
            }));
        }
        catch { lock (_gate) _pending = false; throw; }
    }

    /// <summary>Pure comparison after explicit user choice and genuine fresh READ in the driver.
    /// This result is editor data, never a source/read/write authority or automatic merge.</summary>
    internal static DeveloperManualRecoveryAdoption? PrepareExplicitManualRecovery(DeveloperDraftRecoveryTab saved,
        string inspectedCurrentSha256, string actuallyReadFreshSource)
    {
        ArgumentNullException.ThrowIfNull(saved); ArgumentNullException.ThrowIfNull(actuallyReadFreshSource);
        var fresh = DeveloperManualSourceEditOwner.Sha256(actuallyReadFreshSource);
        if (!StringComparer.OrdinalIgnoreCase.Equals(inspectedCurrentSha256, fresh)) return null;
        return new(saved.RelativePath, actuallyReadFreshSource, fresh, saved.DraftText, saved.OriginalSha256);
    }
}

internal sealed record DeveloperManualRecoveryAdoption(string RelativePath, string CurrentSourceText,
    string CurrentSourceSha256, string RetainedDraftText, string SavedBaselineSha256);
