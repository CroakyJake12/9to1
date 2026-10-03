using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;
using CakeOS.Cui.Runtime;

namespace NineToOne.Web;

[SupportedOSPlatform("browser")]
public static partial class Program
{
    internal static BrowserApplication? Application { get; private set; }

    public static async Task Main()
    {
        await CuiNativeHost.ConfigureFonts(AppBuilder.Configure<BrowserApplication>()).StartBrowserAppAsync("nine-to-one-root", new BrowserPlatformOptions
        {
            RegisterAvaloniaServiceWorker = false,
            RenderingMode = [BrowserRenderingMode.WebGL2, BrowserRenderingMode.WebGL1, BrowserRenderingMode.Software2D],
        });
        await Application!.OpenFragmentAsync(ReadFragment());
    }

    internal static void Attach(BrowserApplication application) => Application = application;

    [JSExport]
    public static void LocationChanged(string fragment) => Application?.QueueNavigation(fragment);

    [JSExport]
    public static void CloseShell() => Application?.Dispose();

    [JSExport]
    public static void PrivateContextInvalidated()
    {
        Application?.ResetPrivateContext();
        ShowStatus("PermissionRequired", "Your session must be checked before reopening private content. Reload to continue.");
    }

    [JSImport("readFragment", "nineToOneBrowser")]
    internal static partial string ReadFragment();

    [JSImport("writeFragment", "nineToOneBrowser")]
    internal static partial void WriteFragment(string fragment, bool replace);

    [JSImport("showStatus", "nineToOneBrowser")]
    internal static partial void ShowStatus(string code, string message);
}
