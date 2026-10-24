using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private ValueTask DispatchTaskRecoveryConflictAsync(string command, object? parameter, CancellationToken token)
    {
        ManualRecoveryCandidate candidate; long generation;
        lock (_gate)
        {
            if (_manual || _taskRecoveryOwner is null || IsActionAvailable(command) != true)
                throw new InvalidOperationException("Protected task reconciliation is unavailable.");
            if (command == "dev.inspect-recovered-draft")
            {
                if (parameter is not string key || !Guid.TryParse(key, out var id) ||
                    _manualRecoveryCandidates.SingleOrDefault(value => value.ViewId == id) is not { } actual)
                    throw new InvalidOperationException("Choose this workbench's actual protected task draft observation.");
                candidate = actual;
            }
            else candidate = SelectedManualRecoveryCandidate() ?? throw new InvalidOperationException("Select a protected task draft first.");
            _pending = true; generation = _generation;
        }
        try
        {
            return new(_work.RunAsync(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation));
                DeveloperPreparedSourceOpen? prepared = null; var resumeRecovery = false;
                try
                {
                    original.DemandPublication(); token.ThrowIfCancellationRequested();
                    var project = await ResolveProjectAsync(original, token).ConfigureAwait(false);
                    var context = await CaptureContextAsync(original, token).ConfigureAwait(false);
                    var document = DemandValue(await original.AwaitAsync(InvokeOriginalSource(() =>
                        _resolveDocument(project, candidate.SavedDraft.RelativePath, token))).ConfigureAwait(false));
                    prepared = DemandValue(await RunTaskSourceAsync(original, project, context,
                        () => _review!.PrepareOpenAsync(project.Reference, context, document, _businessToken), _businessToken).ConfigureAwait(false));
                    var source = prepared; var fresh = source.Snapshot.OriginalText; var freshSha = source.Snapshot.OriginalSha256;
                    await PublishAsync(original, () =>
                    {
                        var position = _manualRecoveryCandidates.FindIndex(value => ReferenceEquals(value, candidate));
                        if (position < 0 || _project.Reference != project.Reference)
                        { _status = "The protected task observation changed during its genuine READ. Live and saved drafts were preserved."; return; }
                        if (command == "dev.reconcile-recovered-draft" && _selectedManualRecoveryCandidate != candidate.ViewId)
                        { _status = "Recovery selection changed. No task draft was substituted."; return; }
                        var refreshed = candidate with { CurrentSourceText = fresh, CurrentSourceSha256 = freshSha };
                        _manualRecoveryCandidates[position] = refreshed; _selectedManualRecoveryCandidate = refreshed.ViewId;
                        if (command == "dev.inspect-recovered-draft")
                        { _status = "Current task source and your protected draft are shown separately. Reconcile explicitly only after comparing them."; return; }
                        var adoption = PrepareExplicitManualRecovery(candidate.SavedDraft, candidate.CurrentSourceSha256, fresh);
                        if (adoption is null)
                        { _status = "Source changed again after inspection. The comparison was refreshed; inspect before reconciling again."; return; }
                        if (adoption.RetainedDraftText.Length > DeveloperSourceReviewSession.MaximumTextCharacters ||
                            adoption.RetainedDraftText.IndexOf('\0') >= 0)
                        { _status = "This protected draft cannot be enrolled as a complete text document. Its original encrypted recovery was preserved."; return; }
                        StoreSelectedTab();
                        var existing = _sourceTabs.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.Path, adoption.RelativePath));
                        if (existing is not null && IsTabDirty(existing))
                        { _status = "A different live draft already uses this file. Both drafts were preserved; resolve that tab before adopting the saved draft."; return; }
                        if (existing is null && _sourceTabs.Count >= DeveloperSourceReviewSession.MaximumOpenDocuments)
                        { _status = "Close a saved source tab before adopting another task draft. No live or pending draft was discarded."; return; }
                        if (existing is not null && existing.Editor is null)
                            throw new InvalidOperationException("The existing task tab has no genuine source editor owner.");
                        // This separately named explicit operation may refresh only the SAME
                        // exact clean editor from this genuinely prepared fresh observation.
                        // Normal source Open retains its strict changed-baseline refusal.
                        var clean = DemandValue(InvokeOriginalSource(() => _review!.AcceptPreparedRecoveryOpen(source, existing?.Editor)));
                        var editor = InvokeOriginalSource(() => _review!.UpdateDraft(clean.EditorId, clean.DraftRevision, adoption.RetainedDraftText));
                        var tab = existing ?? new SourceTab(editor.EditorId, adoption.RelativePath, editor, null);
                        tab.Editor = editor; tab.Manual = null; tab.Pass = null; tab.IsPinned = true;
                        if (existing is null) _sourceTabs.Add(tab);
                        SelectTab(tab);
                        _manualRecoveryCandidates.RemoveAt(position);
                        _selectedManualRecoveryCandidate = _manualRecoveryCandidates.FirstOrDefault()?.ViewId;
                        _status = $"Protected task draft adopted against current source {adoption.CurrentSourceSha256}. Review it before applying; no source file changed.";
                        if (_manualRecoveryCandidates.Count == 0)
                        {
                            _taskRecoveryRequested = null; _taskRecoveryAcknowledged = null;
                            resumeRecovery = true;
                        }
                    }).ConfigureAwait(false);
                    if (resumeRecovery) ScheduleTaskRecoverySave(forceCompleteSnapshot: true);
                }
                catch (Exception cause) { original.Retain(cause); }
                finally
                {
                    if (prepared is not null)
                        try { InvokeOriginalSource(() => { _review!.AbandonPreparedOpen(prepared); return true; }); }
                        catch (Exception cause) { original.Retain(cause); }
                    lock (_gate) _pending = false;
                    if (IsCurrent(generation))
                        try { await PublishAsync(original, () => { }).ConfigureAwait(false); }
                        catch (Exception cause) { original.Retain(cause); }
                    original.ThrowRetained();
                }
            }));
        }
        catch { lock (_gate) _pending = false; throw; }
    }
}
