using System.Globalization;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private sealed record CuiPropertyItem(string SelectionKey, string Label, string Details, bool CanSelect);
    private DeveloperCuiPropertyInspection? _cuiPropertyInspection;
    private Guid _cuiPropertyTabId, _cuiPropertyAnalysisId;
    private string? _cuiPropertyName;
    private string _cuiPropertyDraftValue = "", _cuiPropertyStatus = "Select a named component in the CUI outline to inspect its existing properties.";

    private DeveloperCuiPropertyInspection? CurrentCuiPropertyInspection() => CurrentSourceTab()?.Id == _cuiPropertyTabId &&
        CurrentCuiAnalysis() is { } analysis && analysis.Id == _cuiPropertyAnalysisId &&
        _cuiPropertyInspection is { } inspection && inspection.ContentSha256 == analysis.ContentSha256 ? inspection : null;
    private DeveloperCuiInspectableProperty? SelectedCuiProperty() => CurrentCuiPropertyInspection()?.Properties.SingleOrDefault(property => property.Name == _cuiPropertyName);
    private void SetCuiPropertyInspection(Guid tabId, Guid analysisId, DeveloperCuiPropertyInspection? inspection)
    {
        _cuiPropertyTabId = tabId; _cuiPropertyAnalysisId = analysisId; _cuiPropertyInspection = inspection;
        _cuiPropertyName = null; _cuiPropertyDraftValue = "";
        OnCuiTextChildTargetChanged(inspection);
        OnCuiTextEditingTargetChanged(inspection);
        _cuiPropertyStatus = inspection?.UnavailableReason ?? (inspection is null
            ? "This component has no authored id. Add a unique id in source before property editing."
            : $"{inspection.ComponentType} #{inspection.AuthoredId}: choose an existing literal attribute. Bindings and structured values remain code-defined. Only the chosen attribute value changes; review is required before saving." +
                (inspection.Properties.Count > 100 ? " Showing the first 100 attributes." : ""));
    }
    private bool TryGetCuiPropertyValue(string path, out object? value)
    {
        var inspection = CurrentCuiPropertyInspection(); var property = SelectedCuiProperty();
        value = path switch
        {
            "CuiPropertyStatus" => CurrentSourceTab()?.Id == _cuiPropertyTabId && CurrentCuiAnalysis() is { } analysis && analysis.Id == _cuiPropertyAnalysisId
                ? _cuiPropertyStatus : "Check the current CUI draft and select a uniquely named component in its outline. Existing literal attributes in XML-shaped CUI are editable; shorthand and bindings stay code-defined.",
            "CuiInspectableProperties" => inspection is null ? Array.Empty<CuiPropertyItem>() : inspection.Properties.Take(100).Select((item, index) => new CuiPropertyItem(
                PropertyKey(inspection, index), item.Name + " — " + item.LanguageType, item.UnavailableReason ?? "Editable literal attribute", !_pending && item.CanEdit)).ToArray(),
            "CuiSelectedProperty" => property is null ? "Choose an editable property." : $"{inspection!.AuthoredId} / {property.Name} ({property.LanguageType})",
            "CuiPropertyDraftValue" => property is null ? "" : _cuiPropertyDraftValue,
            "CanEditCuiProperty" => !_pending && property is { CanEdit: true },
            "CanChangeCuiProperty" => IsActionAvailable("dev.change-cui-property"),
            _ => null
        };
        return path is "CuiPropertyStatus" or "CuiInspectableProperties" or "CuiSelectedProperty" or "CuiPropertyDraftValue" or "CanEditCuiProperty" or "CanChangeCuiProperty";
    }
    private static string PropertyKey(DeveloperCuiPropertyInspection inspection, int index) =>
        inspection.Id.ToString("D") + ":" + index.ToString(CultureInfo.InvariantCulture);

    private ValueTask SelectCuiProperty(object? parameter)
    {
        long generation; DeveloperCuiPropertyInspection inspection; DeveloperCuiInspectableProperty property;
        lock (_gate)
        {
            if (IsActionAvailable("dev.select-cui-property") != true || CurrentCuiPropertyInspection() is not { } current || parameter is not string key)
                throw new InvalidOperationException("Select a current component and one of its editable literal properties.");
            var index = Enumerable.Range(0, Math.Min(100, current.Properties.Count)).SingleOrDefault(candidate => PropertyKey(current, candidate) == key, -1);
            if (index < 0 || !current.Properties[index].CanEdit) throw new InvalidOperationException("This property is stale, unsupported or code-defined.");
            generation = _generation; inspection = current; property = current.Properties[index];
        }
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            lock (_gate)
            {
                if (IsActionAvailable("dev.select-cui-property") != true || !ReferenceEquals(CurrentCuiPropertyInspection(), inspection))
                    throw new InvalidOperationException("This property inspection is no longer current.");
                _cuiPropertyName = property.Name; _cuiPropertyDraftValue = property.Value;
                _cuiPropertyStatus = "Edit this literal, then update the draft. Source saving still requires review and Home permission.";
            }
            NotifyOriginal(original);
        });
        return ValueTask.CompletedTask;
    }

    private ValueTask DispatchCuiPropertyChangeAsync(CancellationToken token)
    {
        long generation, revision; Guid tabId; DeveloperCuiPropertyInspection inspection; string property, value, text;
        lock (_gate)
        {
            if (IsActionAvailable("dev.change-cui-property") != true || CurrentCuiPropertyInspection() is not { } current || CurrentSourceTab() is not { } tab || _cuiPropertyName is null)
                throw new InvalidOperationException("Select an editable current literal before updating the draft.");
            generation = _generation; revision = CurrentDraftRevision(); tabId = tab.Id; inspection = current;
            property = _cuiPropertyName; value = _cuiPropertyDraftValue; text = CurrentEditorText(); _pending = true;
        }
        try
        {
            return new(_work.RunAsync(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation)); var changed = false;
                try
                {
                    var result = await original.AwaitAsync(Task.Run(() => DeveloperCuiPropertyEditor.Change(inspection, text, property, value, token), token)).ConfigureAwait(false);
                    await PublishAsync(original, () =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (CurrentSourceTab()?.Id != tabId || CurrentDraftRevision() != revision ||
                            !ReferenceEquals(CurrentCuiPropertyInspection(), inspection) || _cuiPropertyName != property || _cuiPropertyDraftValue != value || CurrentEditorText() != text)
                        { _cuiPropertyStatus = "Source or inspector input changed. Inspect the current component again before updating its draft."; return; }
                        if (result.Error is { } error) { _cuiPropertyStatus = error; return; }
                        if (result.Text == text) { _cuiPropertyStatus = "This literal leaves the source unchanged."; return; }
                        InvokeOriginalSource(() => { UpdateCurrentDraftText(result.Text); return true; }); changed = true;
                        _cuiPropertyStatus = "Updated only the selected attribute value in the draft. Undo is available. Review before saving; check CUI again to refresh the outline and properties.";
                        _status = _cuiPropertyStatus;
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
