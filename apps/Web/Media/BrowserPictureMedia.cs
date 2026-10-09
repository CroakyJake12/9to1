using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace NineToOne.Web.Media;

[SupportedOSPlatform("browser")]
public sealed partial class BrowserPictureMedia : IPictureBrowserMedia
{
    public async Task<string> InvokeAsync(string action, string arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Invoke(action, arguments);
    }
    public void SetDirty(bool dirty) => MarkDirty(dirty);
    public void Release() => Close();
    [JSImport("invoke", "nineToOnePicture")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> Invoke(string action, string arguments);
    [JSImport("setDirty", "nineToOnePicture")]
    private static partial void MarkDirty(bool dirty);
    [JSImport("release", "nineToOnePicture")]
    private static partial void Close();
}
