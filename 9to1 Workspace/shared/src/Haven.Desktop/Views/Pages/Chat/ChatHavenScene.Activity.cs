using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Views.Pages.Chat;

internal sealed partial class ChatHavenScene
{
    // Presentation choices belong to the exact response. The original DynamicUI
    // rows retain their identities, order and source receipts when collapsed.
    private sealed class ActivityDisclosure
    {
        internal bool? UserExpanded;
        internal bool DefaultExpanded;
        internal int Count;
        internal int Failed;
        internal int Deferred;
        internal bool Expanded => Count > 0 && (UserExpanded ?? DefaultExpanded);
    }

    private readonly Dictionary<Guid, ActivityDisclosure> _activityDisclosures = [];

    private void WireActivityDisclosure(DynamicUIItem item, Guid messageId)
    {
        var toggle = item.GetComponent<HavenButton>("ActivityToggle");
        toggle.Invoked += (_, _) =>
        {
            if (_disposed || !_messageItems.TryGetValue(messageId, out var current) ||
                !ReferenceEquals(current, item) || !_activityDisclosures.TryGetValue(messageId, out var state) ||
                state.Count == 0) return;
            state.UserExpanded = !state.Expanded;
            ApplyActivityDisclosure(messageId, item, state);
        };
    }

    private void RefreshActivityDisclosure(ChatSceneMessage message, DynamicUIItem item)
    {
        if (message.Role != MessageRole.Assistant) return;
        if (!_activityDisclosures.TryGetValue(message.Id, out var state))
            _activityDisclosures[message.Id] = state = new();
        var activities = message.ToolActivities ?? [];
        state.Count = activities.Count;
        state.Failed = activities.Count(activity => !activity.Succeeded);
        state.Deferred = activities.Count(activity => activity.HasDeferredInvocations);
        state.DefaultExpanded = message.IsStreaming || state.Failed > 0 || state.Deferred > 0;
        ApplyActivityDisclosure(message.Id, item, state);
    }

    private void ApplyActivityDisclosure(Guid messageId, DynamicUIItem item, ActivityDisclosure state)
    {
        var expanded = state.Expanded;
        var toggle = item.GetComponent<HavenButton>("ActivityToggle");
        var parts = new List<string> { state.Count == 1 ? "1 tool result" : $"{state.Count} tool results" };
        if (state.Failed > 0) parts.Add($"{state.Failed} failed");
        if (state.Deferred > 0) parts.Add(state.Deferred == 1 ? "1 follow-up registered" : $"{state.Deferred} follow-ups registered");
        var outcome = string.Join(" · ", parts);
        toggle.Content = expanded ? "Hide activity" : "Show activity";
        toggle.Accessibility.AccessibleName = toggle.Content + ": " + outcome;
        toggle.IconKey = expanded ? "chevron-up" : "chevron-down";
        toggle.Accessibility.Description = "Recorded tool results for this response. " + (expanded ? "Expanded." : "Collapsed.");
        toggle.SetState(HavenElementState.Expanded, expanded);
        item.GetComponent<Text>("ActivityOutcome").Content = outcome;
        item.GetComponent<Container>("ActivitySummary").SetValue(HavenProperties.Visibility,
            state.Count > 0 ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        foreach (var activityId in _toolOwners.Where(pair => pair.Value == messageId).Select(pair => pair.Key).ToArray())
            if (_toolItems.TryGetValue(activityId, out var activity) && _toolOwners.GetValueOrDefault(activityId) == messageId)
                activity.SetValue(HavenProperties.Visibility, expanded ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    }

    private static string ToolActivityStatus(ToolActivity activity)
    {
        // This flag records registration, not the continuation's current state.
        var observedResult = activity.Succeeded ? "Completed" : "Failed";
        return activity.HasDeferredInvocations ? observedResult + " · follow-up registered" : observedResult;
    }
}
