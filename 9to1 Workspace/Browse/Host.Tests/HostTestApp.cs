using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;

[assembly: AvaloniaTestApplication(typeof(HavenOS.Browse.Host.Tests.HostTestApp))]
namespace HavenOS.Browse.Host.Tests;

/// <summary>Real CUI primitives; this harness does not claim installed Home or OS-engine admission.</summary>
public sealed class HostTestApp : Application
{
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this);
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<HostTestApp>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }));
}
