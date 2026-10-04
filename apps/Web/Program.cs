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
        try
        {
            await CuiNativeHost.ConfigureFonts(AppBuilder.Configure<BrowserApplication>()).StartBrowserAppAsync("nine-to-one-root", new BrowserPlatformOptions
            {
                RegisterAvaloniaServiceWorker = false,
                RenderingMode = [BrowserRenderingMode.WebGL2, BrowserRenderingMode.WebGL1, BrowserRenderingMode.Software2D],
            });
            await Application!.OpenFragmentAsync(ReadFragment());
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            ShowStatus("BrowserRuntimeUnavailable", "9to1 could not start. Reload to try again.");
            throw;
        }
    }

    internal static void Attach(BrowserApplication application) => Application = application;

    [JSExport]
    public static void LocationChanged(string fragment) => Application?.QueueNavigation(fragment);

    [JSExport]
    public static async Task<bool> CloseShell()
    {
        if (Application is { } application && !await application.CloseAsync()) return false;
        await ReleasePrivateAccountContext();
        return true;
    }

    [JSExport]
    public static bool HasUnsavedChanges() => Application?.HasUnsavedChanges == true;

    [JSExport]
    public static string ReadAccessibility() => Application?.ReadAccessibility() ?? "{\"generation\":0,\"elements\":[],\"unsupported\":[]}";

    [JSExport]
    public static bool PerformAccessibility(string id, string operation, string? value) => Application?.PerformAccessibility(id, operation, value) == true;

    [JSExport]
    public static async Task PrivateContextInvalidated()
    {
        if (Application is { } application) await application.ReplacePrivateAccountSettingsAsync();
        ShowStatus("PermissionRequired", "Sign in before reopening private content.");
    }

    [JSExport]
    public static Task OwnedAccountContextChanged() => Application?.ReplacePrivateAccountSettingsAsync() ?? Task.CompletedTask;

    [JSExport]
    public static Task RevokePrivateContext() => Application?.ResetPrivateContextAsync() ?? Task.CompletedTask;

    [JSImport("readFragment", "nineToOneBrowser")]
    internal static partial string ReadFragment();

    [JSImport("writeFragment", "nineToOneBrowser")]
    internal static partial void WriteFragment(string fragment, bool replace);

    [JSImport("showStatus", "nineToOneBrowser")]
    internal static partial void ShowStatus(string code, string message);

    [JSImport("reduceMotion", "nineToOneBrowser")]
    internal static partial bool ReduceMotion();

    [JSImport("releasePrivateAccountContext", "nineToOneBrowser")]
    internal static partial Task ReleasePrivateAccountContext();
}
