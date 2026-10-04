using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
namespace NineToOne.Web.Services;
[SupportedOSPlatform("browser")]
public static partial class BrowserAccountSignIn
{
    public static bool IsAvailable => SignInAvailable();
    public static async Task RequestAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Guid.NewGuid().ToString("N");
        using var cancel = cancellationToken.Register(() => Cancel(id));
        cancellationToken.ThrowIfCancellationRequested();
        var json = await RequestSignIn(id);
        cancellationToken.ThrowIfCancellationRequested();
        using var reply = JsonDocument.Parse(json);
        if (reply.RootElement.ValueKind != JsonValueKind.Object || !reply.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
            throw new IOException("Sign-in did not complete. No account access has been confirmed.");
    }
    [JSImport("signInAvailable", "nineToOneAccounts")]
    private static partial bool SignInAvailable();
    [JSImport("requestSignIn", "nineToOneAccounts")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> RequestSignIn(string id);
    [JSImport("cancel", "nineToOneAccounts")]
    private static partial void Cancel(string id);
}
