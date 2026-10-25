using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private string _cuiTextEditingStatus = "Select a uniquely named self-closing literal Text in a named Page or basic layout Container.";
    private Guid _cuiTextEditingStatusTab;
    private long _cuiTextEditingStatusRevision = -1;
    private void SetCuiTextEditingStatus(string value)
    { _cuiTextEditingStatus = value; _cuiTextEditingStatusTab = CurrentSourceTab()?.Id ?? Guid.Empty; _cuiTextEditingStatusRevision = CurrentDraftRevision(); }
    private void OnCuiTextEditingTargetChanged(DeveloperCuiPropertyInspection? inspection) => SetCuiTextEditingStatus(
        inspection is { CanRemoveText: true } ? "Move or remove Text #" + inspection.AuthoredId + " in this draft. Surrounding comments stay in place."
            : "Select a uniquely named self-closing literal Text in a named Page or basic layout Container. Bindings, actions, shorthand and managed scopes stay in source.");

    private bool TryGetCuiTextEditingValue(string path, out object? value)
    {
        value = path switch
        {
            "CuiTextEditingStatus" => CurrentSourceTab()?.Id == _cuiTextEditingStatusTab && CurrentDraftRevision() == _cuiTextEditingStatusRevision
                ? _cuiTextEditingStatus : "Check the current CUI draft and select a uniquely named self-closing literal Text to move or remove it.",
            "CanMoveCuiTextUp" => IsActionAvailable("dev.move-cui-text-up"),
            "CanMoveCuiTextDown" => IsActionAvailable("dev.move-cui-text-down"),
            "CanRemoveCuiText" => IsActionAvailable("dev.remove-cui-text"),
            _ => null
        };
        return path is "CuiTextEditingStatus" or "CanMoveCuiTextUp" or "CanMoveCuiTextDown" or "CanRemoveCuiText";
    }
    private ValueTask DispatchCuiTextEditingAsync(string command, CancellationToken token)
    {
        long generation, revision; Guid tabId; DeveloperCuiPropertyInspection inspection; string text;
        lock (_gate)
        {
            if (IsActionAvailable(command) != true || CurrentCuiPropertyInspection() is not { } current || CurrentSourceTab() is not { } tab)
                throw new InvalidOperationException("Select a supported current Text component before moving or removing it.");
            generation = _generation; revision = CurrentDraftRevision(); tabId = tab.Id; inspection = current;
            text = CurrentEditorText(); _pending = true;
        }
        var edit = command switch { "dev.move-cui-text-up" => DeveloperCuiTextChildEdit.MoveUp,
            "dev.move-cui-text-down" => DeveloperCuiTextChildEdit.MoveDown, "dev.remove-cui-text" => DeveloperCuiTextChildEdit.Remove,
            _ => throw new ArgumentOutOfRangeException(nameof(command)) };
        try
        {
            return new(_work.RunAsync(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation)); var changed = false;
                try
                {
                    var result = await original.AwaitAsync(Task.Run(() => DeveloperCuiPropertyEditor.EditTextChild(inspection, text, edit, token), token)).ConfigureAwait(false);
                    await PublishAsync(original, () =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (CurrentSourceTab()?.Id != tabId || CurrentDraftRevision() != revision ||
                            !ReferenceEquals(CurrentCuiPropertyInspection(), inspection) || CurrentEditorText() != text)
                        { SetCuiTextEditingStatus("The draft changed. Check CUI and select the current Text again."); return; }
                        if (result.Error is { } error) { SetCuiTextEditingStatus(error); return; }
                        if (result.Text == text) { SetCuiTextEditingStatus("The draft is unchanged."); return; }
                        InvokeOriginalSource(() => { UpdateCurrentDraftText(result.Text); return true; }); changed = true;
                        SetCuiTextEditingStatus((edit == DeveloperCuiTextChildEdit.Remove ? "Text removed from the draft. Undo is available."
                            : "Text moved in the draft. Surrounding comments stayed in place. Undo is available.") +
                            " Check CUI to refresh the outline, and review before saving.");
                        _status = _cuiTextEditingStatus;
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
