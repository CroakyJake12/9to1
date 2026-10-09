using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsCuiBindings
{
    private void RefreshProactivityConfiguration(AssistantConfiguration? configuration, bool usable)
    {
        var preferences = configuration?.Proactive;
        Set("DraftProactivityEnabled", preferences?.Enabled == true);
        Set("DraftAllowCheckIns", preferences?.AllowCheckIns == true);
        Set("DraftAllowUnsolicitedContact", preferences?.AllowUnsolicitedConversations == true);
        Set("DraftNotifyInApp", preferences?.NotificationChannels?.Contains(AssistantConfigurationDraft.InAppNotificationPreference, StringComparer.Ordinal) == true);
        Set("DraftEventTaskCompleted", preferences?.EventKinds?.Contains(AssistantConfigurationDraft.TaskCompletedEventPreference, StringComparer.Ordinal) == true);
        Set("DraftEventTaskNeedsAttention", preferences?.EventKinds?.Contains(AssistantConfigurationDraft.TaskAttentionEventPreference, StringComparer.Ordinal) == true);
        Set("CanEditProactivityPreferences", usable && !Busy && _draft is not null);
        Set("ProactivityStatus", preferences?.Enabled == true
            ? "Your contact preferences can be saved. Automatic check-ins and scheduled work are unavailable in this host."
            : "Proactive contact is off. Automatic check-ins and scheduled work are unavailable in this host.");
        Set("ProactivityResourceStatus", "Contact preferences use the same configured resources. Home must still approve access and actions when a supported trigger becomes available.");
    }
}
