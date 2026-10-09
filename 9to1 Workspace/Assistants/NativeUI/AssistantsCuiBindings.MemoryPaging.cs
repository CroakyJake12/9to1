namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsMemoryCuiBindings
{
    private bool _supportsPaging;
    private string _memorySearch = "";
    private long _memorySearchRevision;
    internal string OriginalSearch => _memorySearch.Trim();
    internal long SearchRevision => _memorySearchRevision;
    internal void SetPagingSupport(bool available) { _supportsPaging = available; Refresh(); }
    private bool TrySetMemorySearch(object? value)
    {
        if (!Current || !_supportsPaging || _busy || HasUnconfirmedChanges || value is not string text || text.Length > 256) return false;
        var accepted = false;
        _publish(() =>
        {
            if (!Current || !_supportsPaging || _busy || HasUnconfirmedChanges) return;
            if (_memorySearch != text) { _memorySearch = text; _memorySearchRevision = checked(_memorySearchRevision + 1); _view = null; }
            accepted = true; Refresh();
        });
        return accepted;
    }
    private bool IsPagingActionAvailable(string command) => command switch
    {
        "assistants.memory.search" => _supportsPaging && _hasOwner && !_busy && !HasUnconfirmedChanges,
        "assistants.memory.older" => _supportsPaging && _hasOwner && !_busy && !HasUnconfirmedChanges &&
            _view?.NextContinuation is not null && _view.SearchText == OriginalSearch,
        _ => false
    };
    private void RefreshMemoryPaging()
    {
        Set("MemorySearch", _memorySearch);
        Set("CanEditMemorySearch", _supportsPaging && _hasOwner && !_busy && !HasUnconfirmedChanges);
        Set("CanMemorySearch", IsActionAvailable("assistants.memory.search") == true);
        Set("CanMemoryOlder", IsActionAvailable("assistants.memory.older") == true);
        Set("HasMoreMemories", _view?.NextContinuation is not null && _view.SearchText == OriginalSearch);
    }
}
