using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

namespace NineToOne.Web.Services;

[SupportedOSPlatform("browser")]
public sealed partial class BrowserAccountTransport : IAccountBrowserTransport
{
    public async Task<JsonElement> InvokeAsync(string action, JsonElement? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestId = Guid.NewGuid().ToString("N");
        using var cancel = cancellationToken.Register(() => Cancel(requestId));
        cancellationToken.ThrowIfCancellationRequested();
        var json = await Invoke(requestId, action, arguments?.GetRawText() ?? "null");
        cancellationToken.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    [JSImport("invoke", "nineToOneAccounts")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> Invoke(string requestId, string action, string arguments);

    [JSImport("cancel", "nineToOneAccounts")]
    private static partial void Cancel(string requestId);
}
