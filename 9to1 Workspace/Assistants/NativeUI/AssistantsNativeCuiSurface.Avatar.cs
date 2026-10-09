using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    internal static CuiControlRegistry CreateAvatarRegistry()
    {
        var registry = new CuiControlRegistry();
        registry.RegisterObjectRenderer("AssistantAvatar", _ => new AssistantAvatarIcon());
        return registry;
    }
}
