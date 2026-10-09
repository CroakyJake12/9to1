using CakeOS.Cui.Runtime;
using HavenOS.Apps.Assistants.Memory;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsMemoryCuiBindings : ICuiRepeatItemBindingContext
{
    private AssistantMemoryView? _memoryRowsView;
    private IReadOnlyList<MemoryRow> _memoryRows = [];
    private void PublishMemoryRepeatRows()
    {
        if (!ReferenceEquals(_memoryRowsView, _view))
        { _memoryRowsView = _view; _memoryRows = _view?.Records.Select(record => new MemoryRow(record)).ToArray() ?? []; }
        Set("MemoryRecords", _memoryRows);
    }
    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        if (!Current || item is not MemoryRow row || !_memoryRows.Any(actual => ReferenceEquals(actual, row))) return false;
        value = path switch { "Id" => row.Id, "Title" => row.Title, "Summary" => row.Summary, "Privacy" => row.Privacy,
            "Origin" => _view?.IsPreservedLegacyRecord(row.Original) == true ? "Preserved saved Agent memory · read-only" : "This Assistant’s memory",
            "CanChange" => CanChangeOriginalMemoryRow(row) && IsActionAvailable("assistants.memory.correct") == true, _ => null };
        return value is not null;
    }
    public bool TrySetItemValue(object item, string path, object? value) => false;
}
