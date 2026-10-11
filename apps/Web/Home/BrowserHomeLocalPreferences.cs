using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace NineToOne.Web;

[SupportedOSPlatform("browser")]
internal sealed partial class BrowserHomeLocalPreferences : IHomeLocalPreferences
{
    public string? Read() => Load();
    public void Write(string json) => throw new NotSupportedException("Browser preference writes must await the shared Home storage lock.");
    public async Task WriteAsync(string json, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); await Save(json); }
    [JSImport("load", "nineToOneHomeLocal")]
    private static partial string? Load();
    [JSImport("save", "nineToOneHomeLocal")]
    private static partial Task Save(string json);
}
