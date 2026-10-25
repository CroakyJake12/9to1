using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private sealed record LineEndingObservation(Guid TabId, long Revision, string Text, DeveloperTextLineEndingInfo Info);
    private LineEndingObservation? _lineEndingObservation;
    private string _lineEndingMessage = "Check line endings in this draft.";

    private LineEndingObservation? CurrentLineEndingObservation() => _lineEndingObservation is { } value &&
        CurrentSourceTab()?.Id == value.TabId && CurrentDraftRevision() == value.Revision && CurrentEditorText() == value.Text ? value : null;
    private bool CanInspectLineEndings() => CurrentSourceTab() is not null && CurrentEditorText().Length <= DeveloperTextLineEndings.MaximumCharacters;
    private bool CanUseLineEndings(DeveloperTextLineEndingTarget target)
    {
        if (!CanInspectLineEndings()) return false;
        return CurrentLineEndingObservation() is not { } current || (target == DeveloperTextLineEndingTarget.Lf
            ? current.Info.CrLfCount + current.Info.CrCount : current.Info.LfCount + current.Info.CrCount) > 0;
    }
    private static string DescribeLineEndings(DeveloperTextLineEndingInfo info) => info.TotalBreaks == 0 ? "No CR or LF line breaks."
        : info.IsMixed ? $"Mixed line endings: {info.CrLfCount} CRLF, {info.LfCount} LF, {info.CrCount} CR."
        : "Line endings: " + (info.CrLfCount > 0 ? "CRLF." : info.LfCount > 0 ? "LF." : "CR.");

    private bool TryGetLineEndingValue(string path, out object? value)
    {
        if (path is not ("LineEndingsStatus" or "CanCheckLineEndings" or "CanUseLfEndings" or "CanUseCrLfEndings")) { value = null; return false; }
        value = path switch
        {
            "CanCheckLineEndings" => IsActionAvailable("dev.check-line-endings"),
            "CanUseLfEndings" => IsActionAvailable("dev.use-lf-endings"),
            "CanUseCrLfEndings" => IsActionAvailable("dev.use-crlf-endings"),
            _ => CurrentSourceTab() is null ? "Open a text file to check line endings."
                : !CanInspectLineEndings() ? "Line-ending controls support drafts up to 2,000,000 characters."
                : CurrentLineEndingObservation() is { } current ? DescribeLineEndings(current.Info) + " " + _lineEndingMessage
                : _lineEndingObservation is null ? _lineEndingMessage : "Draft changed. Check line endings again."
        };
        return true;
    }

    private ValueTask DispatchLineEndingsAsync(string command, CancellationToken token)
    {
        Guid tabId; long generation, revision; string text;
        lock (_gate)
        {
            if (IsActionAvailable(command) != true || CurrentSourceTab() is not { } tab)
                throw new InvalidOperationException("Open a current text draft before changing line endings.");
            tabId = tab.Id; generation = _generation; revision = CurrentDraftRevision(); text = CurrentEditorText(); _pending = true;
        }
        try
        {
            return new(_work.RunAsync(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation)); var changed = false;
                try
                {
                    DeveloperOperationResult<DeveloperTextLineEndingInfo>? inspected = null;
                    DeveloperOperationResult<DeveloperTextLineEndingChange>? normalized = null;
                    var target = command == "dev.use-crlf-endings" ? DeveloperTextLineEndingTarget.CrLf : DeveloperTextLineEndingTarget.Lf;
                    // Inspect only on explicit actions, on retained CPU tasks; typing/status
                    // refresh never scans a large draft or performs filesystem IO.
                    if (command == "dev.check-line-endings")
                        inspected = await original.AwaitAsync(Task.Run(() => DeveloperTextLineEndings.Inspect(text, token), token)).ConfigureAwait(false);
                    else normalized = await original.AwaitAsync(Task.Run(() => DeveloperTextLineEndings.Normalize(text, target, token), token)).ConfigureAwait(false);
                    await PublishAsync(original, () =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (CurrentSourceTab()?.Id != tabId || CurrentDraftRevision() != revision || CurrentEditorText() != text)
                        { _lineEndingMessage = "The draft changed. Check line endings again."; return; }
                        if (inspected is not null)
                        {
                            if (!inspected.Succeeded || inspected.Value is null) { _lineEndingObservation = null; _lineEndingMessage = inspected.Error?.Message ?? "Line endings could not be checked."; return; }
                            _lineEndingObservation = new(tabId, revision, text, inspected.Value); _lineEndingMessage = ""; return;
                        }
                        if (normalized is not { Succeeded: true, Value: { } result })
                        { _lineEndingObservation = null; _lineEndingMessage = normalized?.Error?.Message ?? "Line endings could not be changed."; return; }
                        if (result.ChangedBreaks != 0)
                        {
                            InvokeOriginalSource(() => { UpdateCurrentDraftText(result.Text); return true; }); changed = true;
                            _lineEndingMessage = $"Changed {result.ChangedBreaks} line endings. Undo is available; review before saving.";
                        }
                        else _lineEndingMessage = "The draft is unchanged.";
                        _lineEndingObservation = new(tabId, CurrentDraftRevision(), CurrentEditorText(), result.After);
                        _status = _lineEndingMessage;
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
