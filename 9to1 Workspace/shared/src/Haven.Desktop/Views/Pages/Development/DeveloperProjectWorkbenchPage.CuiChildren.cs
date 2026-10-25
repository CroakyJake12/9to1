using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private string _cuiNewTextId = "new-text", _cuiNewTextValue = "New text";
    private string _cuiTextChildStatus = "Select a Page or layout Container in the CUI outline.";
    private bool CanConfigureCuiTextChild() => CurrentCuiPropertyInspection() is { UnavailableReason: null, CanAddTextChild: true };
    private void OnCuiTextChildTargetChanged(DeveloperCuiPropertyInspection? inspection) =>
        _cuiTextChildStatus = inspection is not null && CanConfigureCuiTextChild()
            ? "Add Text inside " + inspection.AuthoredId + "." : "Select a Page or basic layout Container. Other component types stay in source.";

    private bool TryGetCuiChildrenValue(string path, out object? value)
    {
        value = path switch
        {
            "CuiNewTextId" => _cuiNewTextId,
            "CuiNewTextValue" => _cuiNewTextValue,
            "CuiTextChildStatus" => _cuiTextChildStatus,
            "CanConfigureCuiTextChild" => !_pending && CanConfigureCuiTextChild(),
            "CanAddCuiTextChild" => IsActionAvailable("dev.add-cui-text-child"),
            _ => null
        };
        return path is "CuiNewTextId" or "CuiNewTextValue" or "CuiTextChildStatus" or "CanConfigureCuiTextChild" or "CanAddCuiTextChild";
    }

    private ValueTask DispatchCuiTextChildAsync(CancellationToken token)
    {
        long generation, revision; Guid tabId; DeveloperCuiPropertyInspection inspection; string text, childId, value;
        lock (_gate)
        {
            if (IsActionAvailable("dev.add-cui-text-child") != true || CurrentCuiPropertyInspection() is not { } current || CurrentSourceTab() is not { } tab)
                throw new InvalidOperationException("Select a current Page or layout Container before adding Text.");
            generation = _generation; revision = CurrentDraftRevision(); tabId = tab.Id; inspection = current;
            text = CurrentEditorText(); childId = _cuiNewTextId; value = _cuiNewTextValue; _pending = true;
        }
        try
        {
            return new(_work.RunAsync(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation)); var changed = false;
                try
                {
                    var result = await original.AwaitAsync(Task.Run(() => DeveloperCuiPropertyEditor.AddTextChild(inspection, text, childId, value, token), token)).ConfigureAwait(false);
                    await PublishAsync(original, () =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (CurrentSourceTab()?.Id != tabId || CurrentDraftRevision() != revision ||
                            !ReferenceEquals(CurrentCuiPropertyInspection(), inspection) || CurrentEditorText() != text || _cuiNewTextId != childId || _cuiNewTextValue != value)
                        { _cuiTextChildStatus = "The draft or new text changed. Check CUI and try again."; return; }
                        if (result.Error is { } error) { _cuiTextChildStatus = error; return; }
                        if (result.Text == text) { _cuiTextChildStatus = "The draft is unchanged."; return; }
                        InvokeOriginalSource(() => { UpdateCurrentDraftText(result.Text); return true; }); changed = true;
                        _cuiTextChildStatus = "Text added to the draft. Undo is available. Check CUI to refresh the outline, and review before saving.";
                        _status = _cuiTextChildStatus;
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
