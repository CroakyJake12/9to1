using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsCuiBindings
{
    // A read-only product projection of the SAME host observation. These labels
    // do not configure a capability, issue a resource, or authorize an action.
    private void RefreshHomeReadiness()
    {
        var rows = _snapshot.HostCapabilities
            .Where(capability => capability.State != AssistantSupportState.Available)
            .Select((capability, ordinal) => new HomeCapabilityReadinessRow(ordinal,
                capability.Feature, capability.State switch
                {
                    AssistantSupportState.NotConfigured => "Setup needed",
                    AssistantSupportState.RequiresAuthorization => "Approval required",
                    AssistantSupportState.RequiresInspection => "Review needed",
                    AssistantSupportState.Unsupported => "Not available yet",
                    _ => "Unavailable"
                }))
            .ToArray();
        Set("HomeCapabilityReadinessRows", rows);
    }

    public sealed record HomeCapabilityReadinessRow(int Id, string Name, string StatusLabel);
}
