using HavenOS.Apps.Assistants.Migration;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsLegacyMigrationCuiBindings
{
    private bool _supportsImport;
    private LegacyAgentImportPreview? _importPreview;
    internal LegacyAgentImportPreview? OriginalImportPreview => _importPreview;
    internal void SetImportPreview(LegacyAgentImportPreview actual)
    {
        _supportsImport = true; _importPreview = actual; _status = actual.Reason; _error = "";
        if (!actual.CanBrowse)
        {
            _generation = checked(_generation + 1); _rows = []; _nextCursor = null;
            _draft = null; _result = null; _recovery = null; _links = []; _nextLinks = null;
        }
        Refresh();
    }
    private bool IsImportContentActionAdmitted(string command) => !_supportsImport || _importPreview?.CanBrowse == true ||
        command is "assistants.legacy.back" or "assistants.legacy.list" ||
        command.StartsWith("assistants.legacy.import.", StringComparison.Ordinal);
    private bool IsImportActionAvailable(string command) => _hasOwner && _supportsImport && (!HasUnconfirmedChanges ||
        command == "assistants.legacy.import.audit" && _importPreview?.CanRetryAudit == true) && (command switch
    {
        "assistants.legacy.import.inspect" => true,
        "assistants.legacy.import.request" => _importPreview?.CanRequest == true,
        "assistants.legacy.import.refresh" => _importPreview is not null,
        "assistants.legacy.import.complete" => _importPreview?.CanComplete == true,
        "assistants.legacy.import.audit" => _importPreview?.CanRetryAudit == true,
        _ => false
    });
    private void RefreshImportBindings()
    {
        Set("ShowLegacyImport", _supportsImport && _importPreview?.CanBrowse != true);
        Set("LegacyImportSource", _importPreview?.SourceLabel ?? "Configured saved agents");
        Set("LegacyImportStore", _importPreview?.StoreId?.ToString("D") ?? "Identity unavailable");
        Set("LegacyImportState", _importPreview?.State.ToString() ?? "");
        Set("LegacyImportReason", _importPreview?.Reason ?? "");
        Set("LegacyImportRequest", _importPreview?.RequestId ?? "");
        Set("HasLegacyImportRequest", _importPreview?.RequestId is not null);
        foreach (var item in new[] { ("CanInspectLegacyImport", "inspect"), ("CanRequestLegacyImport", "request"),
            ("CanRefreshLegacyImport", "refresh"), ("CanCompleteLegacyImport", "complete"), ("CanRetryLegacyImportAudit", "audit") })
            Set(item.Item1, IsActionAvailable("assistants.legacy.import." + item.Item2) == true);
    }
}
