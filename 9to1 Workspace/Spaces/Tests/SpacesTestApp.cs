using Avalonia;
using Avalonia.Headless;
using CakeOS.Cui.Runtime;


namespace HavenOS.Apps.Spaces.Tests;

/// <summary>
/// Shared Avalonia application for Spaces headless rendering tests. It loads the real primitive
/// CUI theme and bundled fonts so the authored SpacesHome.cui renders exactly as it would in a
/// native app tab.
/// </summary>
public sealed class SpacesTestApp : Application
{
    // Theme is applied per-test so each theory case can exercise a specific appearance without
    // duplicating global styles across cases.
    public override void Initialize()
    {
        CuiNativeHost.InitialisePrimitiveTheme(this);
    }

    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(
        AppBuilder.Configure<SpacesTestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }));
}
