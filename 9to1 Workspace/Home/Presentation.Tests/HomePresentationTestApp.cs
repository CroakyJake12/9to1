using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;

[assembly: AvaloniaTestApplication(typeof(HavenOS.Home.Presentation.Tests.HomePresentationTestApp))]

namespace HavenOS.Home.Presentation.Tests;

/// <summary>
/// Runs the production Home views under their CUI theme on the host architecture.
/// Full desktop startup and its Windows/x64 infrastructure remain covered by
/// the original desktop test application's platform-specific acceptance suite.
/// </summary>
public sealed class HomePresentationTestApp : Application
{
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this);

    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(
        AppBuilder.Configure<HomePresentationTestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions()));
}
