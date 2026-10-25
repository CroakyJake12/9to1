using CakeOS.Cui.Runtime;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private sealed record EditorFindObservation(Guid TabId, string Query, bool MatchCase, string Text,
        DeveloperTextMatch Match, bool IsMatch = true);
    private string _sourceFindQuery = "", _sourceReplaceText = "", _sourceFindStatus = "Find literal text in the current draft.";
    private bool _sourceFindMatchCase = true;
    private EditorFindObservation? _sourceFindObservation;

    private void InvalidateSourceFind()
    { _sourceFindObservation = null; _sourceFindStatus = "The draft or query changed. Find text in the current draft."; }

    private bool CanReplaceSourceMatch() => _sourceFindObservation is { IsMatch: true } observed &&
        CurrentSourceTab()?.Id == observed.TabId && observed.MatchCase == _sourceFindMatchCase &&
        StringComparer.Ordinal.Equals(observed.Query, _sourceFindQuery) && StringComparer.Ordinal.Equals(observed.Text, CurrentEditorText());

    // Both keyboard input and replace actions enter the SAME original draft owner and invalidate
    // old review state. No source save, permission decision or executable action happens here.
    private void UpdateCurrentDraftText(string text, bool recordHistory = true)
    {
        var previous = CurrentEditorText();
        var history = recordHistory ? CurrentDraftHistory() : null;
        if (!StringComparer.Ordinal.Equals(previous, text) && CurrentSourceTab() is { } source)
        { source.CuiAnalysis = null; source.CuiAnalysisText = null; }
        _editorTextSelection = null; InvalidateSourceFind();
        if (_manual && _editor is null)
        {
            if (CurrentSourceTab() is null && text.Length != 0)
                throw new InvalidOperationException("Open an authorized source document before editing a draft.");
            _manualDraftText = text; _manualDraftRevision++; _manualReviewed = false;
            _manualAfterText = ""; _manualAfterSha256 = ""; _manualReviewRevision = -1;
            if (CurrentSourceTab() is { } manualTab) { manualTab.IsPinned = true; manualTab.IsPreview = false; }
            StoreSelectedTab(); history?.RecordChange(previous, text); return;
        }
        if (_editor is null) throw new InvalidOperationException("Open a real complete source document first.");
        _editor = _review!.UpdateDraft(_editor.EditorId, _editor.DraftRevision, text);
        if (CurrentSourceTab() is { } editedTab) { editedTab.IsPinned = true; editedTab.IsPreview = false; }
        StoreSelectedTab(); history?.RecordChange(previous, text);
    }

    private ValueTask DispatchSourceFindAsync(string command, CancellationToken token)
    {
        long generation; Guid tabId; string query, replacement, text; bool matchCase; EditorFindObservation? observed;
        lock (_gate)
        {
            if (IsActionAvailable(command) != true || CurrentSourceTab() is not { } tab)
                throw new InvalidOperationException("Open source and enter literal text before finding or replacing it.");
            generation = _generation; tabId = tab.Id; query = _sourceFindQuery; replacement = _sourceReplaceText;
            matchCase = _sourceFindMatchCase; text = CurrentEditorText(); observed = _sourceFindObservation; _pending = true;
        }
        try
        {
            return new(_work.RunAsync(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation));
                var changed = false;
                try
                {
                    token.ThrowIfCancellationRequested();
                    await PublishAsync(original, () =>
                    {
                        if (CurrentSourceTab()?.Id != tabId || !StringComparer.Ordinal.Equals(query, _sourceFindQuery) ||
                            matchCase != _sourceFindMatchCase || !StringComparer.Ordinal.Equals(text, CurrentEditorText()))
                        { _sourceFindStatus = "Source or search changed. Repeat the action on the current draft."; return; }
                        if (command is "dev.find-next-source" or "dev.find-previous-source")
                        {
                            var previous = command == "dev.find-previous-source";
                            var same = observed is not null && observed.TabId == tabId && observed.MatchCase == matchCase &&
                                StringComparer.Ordinal.Equals(observed.Query, query) && StringComparer.Ordinal.Equals(observed.Text, text);
                            var start = previous ? text.Length : 0;
                            if (same && observed is { } prior) start = previous ? prior.Match.Start : prior.Match.Start + prior.Match.Length;
                            var match = DeveloperTextSearch.Find(text, query, start, previous, matchCase, token);
                            if (match is null) { _sourceFindObservation = null; _sourceFindStatus = "No matching literal text in this draft."; return; }
                            _sourceFindObservation = new(tabId, query, matchCase, text, match);
                            _editorTextSelection = CuiTextSelection.ForText(checked(++_editorNavigationRequest), text, match.Start, match.Length);
                            _sourceFindStatus = match.Wrapped ? "Found literal text; wrapped around the draft." : "Found literal text in the current draft.";
                            return;
                        }
                        DeveloperTextReplacement result;
                        if (command == "dev.replace-source-match")
                        {
                            if (!ReferenceEquals(observed, _sourceFindObservation) || !CanReplaceSourceMatch())
                            { _sourceFindStatus = "The found match changed. Find it again before replacing."; return; }
                            result = DeveloperTextSearch.ReplaceMatch(text, query, replacement, observed!.Match, matchCase, token);
                        }
                        else result = DeveloperTextSearch.ReplaceAll(text, query, replacement, matchCase, token);
                        token.ThrowIfCancellationRequested();
                        if (result.Error is { } error) { _sourceFindStatus = error; return; }
                        if (StringComparer.Ordinal.Equals(result.Text, text))
                        { _sourceFindStatus = result.Matches == 0 ? "No matching literal text in this draft." : "The replacement leaves the draft unchanged."; return; }
                        InvokeOriginalSource(() => { UpdateCurrentDraftText(result.Text); return true; });
                        changed = true;
                        if (command == "dev.replace-source-match")
                        {
                            var replaced = new DeveloperTextMatch(observed!.Match.Start, replacement.Length, false);
                            _sourceFindObservation = new(tabId, query, matchCase, result.Text, replaced, false);
                            _editorTextSelection = CuiTextSelection.ForText(checked(++_editorNavigationRequest), result.Text, replaced.Start, replaced.Length);
                        }
                        _sourceFindStatus = result.Matches == 1 ? "Replaced one match in the draft. Review before saving source."
                            : $"Replaced {result.Matches} matches in the draft. Review before saving source.";
                    }).ConfigureAwait(false);
                    if (changed) { ScheduleManualRecoverySave(); ScheduleTaskRecoverySave(); }
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
}
