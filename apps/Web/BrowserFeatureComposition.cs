using Avalonia.Threading;
using System.Runtime.Versioning;
using NineToOne.Web.Accounts;
using NineToOne.Web.Wave;

namespace NineToOne.Web;

/// <summary>Browser-owned local features and explicitly unconfigured account presentation.</summary>
internal static class BrowserFeatureComposition
{
    [SupportedOSPlatform("browser")]
    public static void Register(BrowserSurfaceRegistry registry)
    {
        var wave = WaveBrowserFeature.Register(registry);
        if (!wave.Succeeded) throw new InvalidOperationException(wave.Message);
        var settings = AccountSettingsFeature.CreateForBrowser(action => Dispatcher.UIThread.InvokeAsync(action).GetTask());
        var registered = registry.Register(settings, settings.Render);
        if (!registered.Succeeded) { settings.Dispose(); throw new InvalidOperationException(registered.Message); }
    }
}
