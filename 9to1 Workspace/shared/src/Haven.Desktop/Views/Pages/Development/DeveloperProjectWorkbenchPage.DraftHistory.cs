using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    // All callers hold the page gate. Changing the saved basis (save/reload/recovery) withdraws
    // old history; switching tabs retains each document's own bounded in-memory history.
    private DeveloperDraftHistory? CurrentDraftHistory()
    {
        if (CurrentSourceTab() is not { } tab) return null;
        var basis = _manual && _editor is null ? _manualDraftBaseline : _editor?.OriginalText;
        if (basis is null) return null;
        if (tab.DraftHistory is null || !StringComparer.Ordinal.Equals(tab.DraftHistoryBasis, basis))
        {
            tab.DraftHistory = new(CurrentEditorText()); tab.DraftHistoryBasis = basis;
        }
        return tab.DraftHistory;
    }

    private ValueTask DispatchDraftHistoryAsync(string command, CancellationToken token)
    {
        long generation; Guid tabId; string text; DeveloperDraftHistory history;
        lock (_gate)
        {
            if (IsActionAvailable(command) != true || CurrentSourceTab() is not { } tab || CurrentDraftHistory() is not { } current)
                throw new InvalidOperationException("No current draft history is available for this document.");
            generation = _generation; tabId = tab.Id; text = CurrentEditorText(); history = current; _pending = true;
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
                        token.ThrowIfCancellationRequested();
                        if (CurrentSourceTab()?.Id != tabId || !ReferenceEquals(CurrentDraftHistory(), history) ||
                            !StringComparer.Ordinal.Equals(text, CurrentEditorText()))
                        { _status = "The source draft changed. Repeat Undo or Redo on the current document."; return; }
                        void Apply(string next) => InvokeOriginalSource(() => { UpdateCurrentDraftText(next, recordHistory: false); return true; });
                        changed = command == "dev.undo-source-edit" ? history.TryUndo(text, Apply) : history.TryRedo(text, Apply);
                        if (changed) _status = command == "dev.undo-source-edit"
                            ? "Undid a draft edit. Review is required before saving source."
                            : "Redid a draft edit. Review is required before saving source.";
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
