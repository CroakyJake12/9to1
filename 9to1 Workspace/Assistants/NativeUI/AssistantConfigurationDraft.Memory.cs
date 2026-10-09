using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantConfigurationDraft
{
    // Preferences only. The same original draft submission, CAS save and observed
    // acknowledgement own persistence; neither checkbox issues a memory input/grant.
    private AssistantConfiguration? MemoryPreferenceReplacement(string field, bool value) => field switch
    {
        "DraftMemoryEnabled" => Configuration with { Memory = Configuration.Memory with { Enabled = value } },
        "DraftMemoryIncludeProjectContext" => Configuration with { Memory = Configuration.Memory with { IncludeProjectContext = value } },
        _ => null
    };
}
