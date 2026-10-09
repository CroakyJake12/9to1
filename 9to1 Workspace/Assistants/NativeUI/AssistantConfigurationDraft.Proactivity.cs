using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantConfigurationDraft
{
    internal const string InAppNotificationPreference = "in-app";
    internal const string TaskCompletedEventPreference = "task.completed";
    internal const string TaskAttentionEventPreference = "task.user-action-required";

    // Persisted opt-ins only. They do not register a trigger, create work or grant
    // resource access. Existing unrecognised values and automation IDs survive.
    private AssistantConfiguration? ProactivityPreferenceReplacement(string field, bool value)
    {
        var current = Configuration.Proactive;
        AssistantProactivePreferences? replacement = field switch
        {
            "DraftProactivityEnabled" => current with { Enabled = value },
            "DraftAllowCheckIns" => current with { AllowCheckIns = value },
            "DraftAllowUnsolicitedContact" => current with { AllowUnsolicitedConversations = value },
            "DraftNotifyInApp" => SetPreference(current.NotificationChannels, InAppNotificationPreference, value) is { } channels
                ? current with { NotificationChannels = channels } : current,
            "DraftEventTaskCompleted" => SetPreference(current.EventKinds, TaskCompletedEventPreference, value) is { } completed
                ? current with { EventKinds = completed } : current,
            "DraftEventTaskNeedsAttention" => SetPreference(current.EventKinds, TaskAttentionEventPreference, value) is { } attention
                ? current with { EventKinds = attention } : current,
            _ => null
        };
        return replacement is null ? null : Configuration with { Proactive = replacement };
    }
    private static IReadOnlyList<string>? SetPreference(IReadOnlyList<string>? original, string selected, bool enabled)
    {
        if ((original?.Contains(selected, StringComparer.Ordinal) == true) == enabled) return null;
        return Array.AsReadOnly(enabled ? (original ?? []).Append(selected).ToArray()
            : (original ?? []).Where(value => value != selected).ToArray());
    }
}
