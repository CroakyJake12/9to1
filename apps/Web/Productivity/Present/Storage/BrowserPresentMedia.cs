using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace NineToOne.Web.Productivity.Present.Storage;

/// <summary>The Present half of device-local file delivery: a download and a picker.</summary>
[SupportedOSPlatform("browser")]
public sealed partial class BrowserPresentMedia : IPresentBrowserMedia
{
    public async Task DownloadAsync(string name, string base64, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = await Download(name, base64);
    }

    public async Task<PresentPickedFile?> PickAsync(string accept, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = await Choose(accept);
        if (string.IsNullOrEmpty(json)) return null;
        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        if (parsed.RootElement.ValueKind == System.Text.Json.JsonValueKind.Null) return null;
        var name = parsed.RootElement.GetProperty("name").GetString();
        var bytes = parsed.RootElement.GetProperty("base64").GetString();
        return name is null || bytes is null ? null : new PresentPickedFile(name, bytes);
    }

    [JSImport("download", "nineToOnePresent")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> Download(string name, string base64);

    [JSImport("pick", "nineToOnePresent")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> Choose(string accept);
}
