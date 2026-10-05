using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
namespace NineToOne.Web.Write.Storage;
[SupportedOSPlatform("browser")]
public sealed partial class BrowserNotesTransport : INotesBrowserTransport
{
    public async Task<JsonElement> InvokeAsync(string action, JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Guid.NewGuid().ToString("N");
        using var registration = cancellationToken.Register(() => Cancel(id));
        cancellationToken.ThrowIfCancellationRequested();
        string json;
        try { json = await Invoke(id, action, arguments.GetRawText()); }
        catch (Exception error) when (action is "Save" or "Delete")
        { throw new NotesCommitOutcomeUnknownException("Storage acknowledgment was lost; inspect the durable version before retrying.", error); }
        var result = NotesStorageAcknowledgment.Parse(action, json);
        var committed = result.TryGetProperty("committed", out var flag) && flag.ValueKind == JsonValueKind.True;
        if (!committed) cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
    [JSImport("invoke", "nineToOneNotes")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> Invoke(string id, string action, string arguments);
    [JSImport("cancel", "nineToOneNotes")]
    private static partial void Cancel(string id);
}
