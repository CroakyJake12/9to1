using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsCuiBindings
{
    private void RefreshMemoryConfiguration(AssistantConfiguration? configuration, bool usable)
    {
        var memory = configuration?.Memory;
        Set("DraftMemoryEnabled", memory?.Enabled == true);
        Set("DraftMemoryIncludeProjectContext", memory?.IncludeProjectContext == true);
        Set("CanEditMemoryPreferences", usable && !Busy && _draft is not null);
        Set("MemoryPreferenceStatus", memory?.Enabled != true
            ? "Memory is off. This Assistant will not read saved memories."
            : memory.IncludeProjectContext
                ? "Project context is requested. Project memory is unavailable here, so memory use will be refused until an authorized project source is available."
                : configuration?.Model.AllowCloud == true
                    ? "Memory is requested. Turn off cloud models to use scoped local memory; cloud disclosure is unavailable here."
                    : "This Assistant may use its own saved memories after Home grants access.");
        var capability = (_snapshot.SelectedAssistant?.Capabilities ?? _snapshot.HostCapabilities)
            .FirstOrDefault(value => value.Feature == "Own Assistant memory");
        Set("MemoryPreferenceAvailability", capability?.State switch
        {
            AssistantSupportState.Available => "Own memory is available, subject to current access checks.",
            AssistantSupportState.RequiresAuthorization => "Own memory needs access to its store through Home.",
            AssistantSupportState.RequiresInspection => "Review current memory access in Home before using it.",
            AssistantSupportState.Unsupported => "Own memory is unavailable in this host. Your preference can be saved, but grants no access.",
            _ => "Current memory access will be checked when this Assistant uses it."
        });
    }
}
