using Haven.Core;

namespace Haven.Application;

/// <summary>Concrete OS identity only. It neither declares a capability available nor grants
/// model/tool access. Unsupported hosts remain None and current discovery refuses them.</summary>
public static class CapabilityHostPlatform
{
    public static CapabilityPlatform Current => OperatingSystem.IsAndroid() ? CapabilityPlatform.Android :
        OperatingSystem.IsIOS() ? CapabilityPlatform.iOS :
        OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() ? CapabilityPlatform.MacOS :
        OperatingSystem.IsWindows() ? CapabilityPlatform.Windows :
        OperatingSystem.IsLinux() ? CapabilityPlatform.Linux : CapabilityPlatform.None;
}
