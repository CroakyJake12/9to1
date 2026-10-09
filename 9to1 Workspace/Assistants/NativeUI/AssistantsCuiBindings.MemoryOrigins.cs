namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsMemoryCuiBindings
{
    private void RefreshMemoryOriginActions() => _publish(() =>
    {
        if (Current) PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs("CanChange"));
    });

    // Observation only. The actual source must still authorize every operation.
    // Never derive lineage or editability from an Agent ID, label or Scope string.
    private bool CanChangeOriginalMemoryRow(MemoryRow row) => Current && _view is not null &&
        _memoryRows.Any(actual => ReferenceEquals(actual, row)) &&
        _view.Records.Any(record => ReferenceEquals(record, row.Original)) &&
        !_view.IsPreservedLegacyRecord(row.Original);
}
