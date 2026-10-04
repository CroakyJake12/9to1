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

    [JSImport("invoke", "nineToOneWave")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> Invoke(string action, string arguments);

    [JSImport("setDirty", "nineToOneWave")]
    private static partial void MarkDirty(bool dirty);

    [JSImport("release", "nineToOneWave")]
    private static partial void Close();
}
