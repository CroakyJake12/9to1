using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

/// <summary>A presentation callback supplied by the actual native host. Only the SAME
/// canonical owner-issued binding enters it; the host still validates page/window/Home
/// and transfers independent Dev custody. This interface creates no authorization.</summary>
public interface IAssistantsNativeOriginalDevelopmentRoute
{
    Task OpenOriginalAsync(AssistantsNativeCuiSurface actualOrigin,
        AssistantDevelopmentBinding actualBinding, long originalPresentationGeneration,
        CancellationToken token);
}
