namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsCuiBindings
{
    private bool IsIdentityEnabled => _snapshot.SelectedAssistant is
        { Configuration.Enabled: true, Configuration.Archived: false };

    private bool CanChangeIdentityAvailability(string command)
    {
        if (_snapshot.SelectedAssistant is not { } selected || Busy || _streaming ||
            _projectBusy || _hasPendingProjectSubmission || _draft?.IsDirty == true) return false;
        return command switch
        {
            "assistants.disable" => selected.Configuration.Enabled && !selected.Configuration.Archived,
            "assistants.enable" => !selected.Configuration.Enabled && !selected.Configuration.Archived,
            "assistants.archive" => !selected.Configuration.Archived,
            "assistants.restore" => selected.Configuration.Archived,
            _ => false
        };
    }

    private void RefreshIdentityAvailability()
    {
        var configuration = _snapshot.SelectedAssistant?.Configuration;
        Set("CanDisable", IsActionAvailable("assistants.disable") == true);
        Set("CanEnable", IsActionAvailable("assistants.enable") == true);
        Set("CanArchive", IsActionAvailable("assistants.archive") == true);
        Set("CanRestore", IsActionAvailable("assistants.restore") == true);
        Set("ShowDisableAssistant", configuration is { Enabled: true, Archived: false });
        Set("ShowEnableAssistant", configuration is { Enabled: false, Archived: false });
        Set("ShowArchiveAssistant", configuration is { Archived: false });
        Set("ShowRestoreAssistant", configuration is { Archived: true });
        Set("IdentityAvailabilityStatus", configuration is null ? ""
            : configuration.Archived ? "Archived. Restore this Assistant to use it again. Its conversations and saved work are kept."
            : !configuration.Enabled ? "Disabled. Enable this Assistant when you are ready to continue. Its conversations and saved work are kept."
            : "Enabled. Disabling or archiving keeps your conversations and saved work. Manage running Tasks with their own controls.");
        if (configuration?.Archived == true)
            Set("ComposerAvailability", "This Assistant is archived. Choose Restore Assistant under Manage Assistant to continue.");
        else if (configuration?.Enabled == false)
            Set("ComposerAvailability", "This Assistant is disabled. Choose Enable Assistant under Manage Assistant to continue.");
    }
}
