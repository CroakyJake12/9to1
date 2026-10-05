using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

namespace NineToOne.Web.Productivity.Present.Storage;

[SupportedOSPlatform("browser")]
public sealed partial class BrowserPresentTransport : IPresentBrowserTransport
{
    public async Task<JsonElement> InvokeAsync(string action, JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestId = Guid.NewGuid().ToString("N");
        using var registration = cancellationToken.Register(() => Cancel(requestId));
        cancellationToken.ThrowIfCancellationRequested();
        JsonElement reply;
        try
        {
            var original = Invoke(requestId, action, arguments.GetRawText(), cancellationToken.IsCancellationRequested);
            // Invoke publishes the actual JS request inline before returning its Task. A cancellation
            // whose first callback preceded JS admission is now applied to that SAME admitted request.
            if (cancellationToken.IsCancellationRequested) Cancel(requestId);
            var json = await original;
            using var document = JsonDocument.Parse(json);
            reply = document.RootElement.Clone();
            if (reply.ValueKind != JsonValueKind.Object) throw new IOException("Non-object Present storage acknowledgement.");
        }
        catch (Exception error) when (action is "Save" or "Delete")
        { throw new PresentCommitOutcomeUnknownException("Mutation acknowledgment was lost; inspect saved state before retrying.", error); }
        if (!(reply.TryGetProperty("committed", out var committed) && committed.ValueKind == JsonValueKind.True))
            cancellationToken.ThrowIfCancellationRequested();
        return reply;
    }
    [JSImport("invoke", "nineToOnePresent")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> Invoke(string requestId, string action, string arguments, bool cancelledAtAdmission);
    [JSImport("cancel", "nineToOnePresent")]
    private static partial void Cancel(string requestId);
}
