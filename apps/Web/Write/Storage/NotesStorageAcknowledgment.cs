using System.Text.Json;
namespace NineToOne.Web.Write.Storage;
internal static class NotesStorageAcknowledgment
{
    internal static JsonElement Parse(string action, string json)
    {
        JsonElement result;
        try { using var reply = JsonDocument.Parse(json); result = reply.RootElement.Clone(); }
        catch (JsonException error)
        {
            if (action is "Save" or "Delete") throw new NotesCommitOutcomeUnknownException("Malformed storage acknowledgment; inspect durable state before retrying.", error);
            throw new IOException("Malformed browser notes response.", error);
        }
        if (result.ValueKind != JsonValueKind.Object)
        {
            if (action is "Save" or "Delete") throw new NotesCommitOutcomeUnknownException("Non-object storage acknowledgment; inspect durable state before retrying.");
            throw new IOException("Non-object browser notes response.");
        }
        return result;
    }
}
