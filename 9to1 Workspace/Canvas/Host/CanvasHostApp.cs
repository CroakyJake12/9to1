using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace HavenOS.Apps.Canvas;

/// <summary>The real window belongs to the genuine Home process through CanvasHomeWindowFactory.
/// This executable cannot turn an unconfigured standalone launch into an installed app grant.</summary>
public sealed partial class CanvasHostApp : Application
{
    public override void Initialize() => CakeOS.Cui.Runtime.CuiNativeHost.InitialisePrimitiveTheme(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
            throw new PlatformNotSupportedException("Open Canvas through the genuine Home host. Independent installed Canvas domain admission is not configured.");
        base.OnFrameworkInitializationCompleted();
    }
}
