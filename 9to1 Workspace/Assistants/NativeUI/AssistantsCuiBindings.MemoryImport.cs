using HavenOS.Apps.Assistants.Memory;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsMemoryCuiBindings
{
    private bool _supportsImport, _importUnconfirmed;
    private AssistantMemoryImportPreview? _importPreview;
    internal AssistantMemoryImportPreview? OriginalImportPreview => _importPreview;
    internal bool IsImportReadAvailable => !_supportsImport || !_importUnconfirmed && _importPreview?.CanRead == true;
    private bool HasUnconfirmedImport => _importUnconfirmed || _importPreview?.State is
        AssistantMemoryImportState.AuditPending or AssistantMemoryImportState.OutcomeUnconfirmed;
    internal void SetImportSupport(bool supported)
    {
        _supportsImport = supported;
        if (!supported && !HasUnconfirmedImport) _importPreview = null;
        Refresh();
    }
    internal void SetImportPreview(AssistantMemoryImportPreview actual)
    {
        _importPreview = actual; _importUnconfirmed |= actual.State == AssistantMemoryImportState.OutcomeUnconfirmed;
        if (!actual.CanRead || _importUnconfirmed) _view = null; // Revoke displayed content, retain any local draft.
        _error = ""; Refresh();
    }
    internal void MarkImportUnconfirmed(string reason)
    { _importUnconfirmed = true; _view = null; _error = reason; Refresh(); }
    private bool IsImportActionAvailable(string command)
    {
        if (!_supportsImport || !_hasOwner || _busy || HasUnconfirmedWriteChanges || _importPreview is null) return false;
        return command switch
        {
            "assistants.memory.import.request" => !_importUnconfirmed && _importPreview.CanRequest,
            "assistants.memory.import.refresh" => true,
            "assistants.memory.import.complete" => !_importUnconfirmed && _importPreview.CanComplete,
            "assistants.memory.import.audit" => !_importUnconfirmed && _importPreview.CanRetryAudit,
            _ => false
        };
    }
    private void RefreshMemoryImport()
    {
        Set("HasMemoryImport", _supportsImport && _importPreview is not null);
        Set("MemoryImportState", _importPreview?.State.ToString() ?? "");
        Set("MemoryImportReason", _importPreview?.Reason ?? "");
        Set("MemoryImportStore", _importPreview?.StoreId?.ToString("D") ?? "");
        Set("MemoryImportRequest", _importPreview?.RequestId ?? "");
        Set("HasMemoryImportRequest", _importPreview?.RequestId is not null);
        foreach (var action in new[] { ("CanMemoryImportRequest", "request"), ("CanMemoryImportRefresh", "refresh"),
            ("CanMemoryImportComplete", "complete"), ("CanMemoryImportAudit", "audit") })
            Set(action.Item1, IsActionAvailable("assistants.memory.import." + action.Item2) == true);
    }
}
