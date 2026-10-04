using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace NineToOne.Web.Media;

[SupportedOSPlatform("browser")]
public sealed partial class BrowserWaveMedia : IWaveBrowserMedia
{
    public async Task<string> InvokeAsync(string action, string arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Invoke(action, arguments);
    }

    public void SetDirty(bool dirty) => MarkDirty(dirty);
    public void Release() => Close();


    // Focus authority is independent of saved project/audio identity. Unavailable interop denies.
    public static string CaptureFocusClaim()
    {
        if (!OperatingSystem.IsBrowser()) return "";
        try { return CaptureHostFocus() ?? ""; } catch { return ""; }
    }

    public static bool ValidateFocusClaim(string token)
    {
        if (!OperatingSystem.IsBrowser() || string.IsNullOrEmpty(token)) return false;
        try { return ConsumeHostFocus(token); } catch { return false; }
    }

    [JSImport("captureFocusClaim", "nineToOneWave")]
    private static partial string CaptureHostFocus();

    [JSImport("validateFocusClaim", "nineToOneWave")]
    private static partial bool ConsumeHostFocus(string token);

    [JSImport("invoke", "nineToOneWave")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> Invoke(string action, string arguments);

    [JSImport("setDirty", "nineToOneWave")]
    private static partial void MarkDirty(bool dirty);

    [JSImport("release", "nineToOneWave")]
    private static partial void Close();
}
