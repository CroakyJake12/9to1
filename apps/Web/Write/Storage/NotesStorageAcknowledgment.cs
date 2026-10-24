using System.Globalization;
using System.Text.Json;
namespace NineToOne.Web.Write.Storage;
internal static class NotesStorageAcknowledgment
{
    internal static JsonElement Parse(string action, string json, JsonElement? arguments = null)
    {
        JsonElement result;
        try { using var reply = JsonDocument.Parse(json); result = reply.RootElement.Clone(); }
        catch (JsonException error)
        { throw Malformed(action, "Malformed browser notes response.", error); }
        Validate(action, result, arguments);
        return result;
    }

    // The browser calls this before its late-cancellation check. Scripted transports
    // must pass the same validation at the repository boundary.
    internal static void Validate(string action, JsonElement reply, JsonElement? arguments = null)
    {
        if (reply.ValueKind != JsonValueKind.Object)
            throw Malformed(action, "Non-object browser notes response.");
        DemandUniqueFields(action, reply);
        if (!reply.TryGetProperty("ok", out var ok) || ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Malformed(action, "Missing or malformed storage status.");
        var hasCommitted = reply.TryGetProperty("committed", out var committed);
        if (hasCommitted && committed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Malformed(action, "Malformed storage commit status.");
        var hasLateCancellation = reply.TryGetProperty("cancelledAfterCommit", out var lateCancellation);
        if (hasLateCancellation && lateCancellation.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Malformed(action, "Malformed storage cancellation status.");
        var mutation = action is "Save" or "Delete";
        if (lateCancellation.ValueKind == JsonValueKind.True && committed.ValueKind != JsonValueKind.True)
            throw Malformed(action, "Contradictory storage cancellation status.");
        if (ok.GetBoolean())
        {
            if (reply.TryGetProperty("error", out var unexpectedError) && unexpectedError.ValueKind != JsonValueKind.Null)
                throw Malformed(action, "Successful storage response also reports an error.");
            if (mutation)
            {
                if (committed.ValueKind != JsonValueKind.True)
                    throw Malformed(action, "Mutation has no commit acknowledgment; inspect durable state before retrying.");
                // Exact Save receipt identity is checked against the original dispatch
                // by the repository. A malformed receipt must never become cancellation.
                if (action == "Save" && (!reply.TryGetProperty("value", out var receipt) || receipt.ValueKind != JsonValueKind.String))
                    throw Malformed(action, "Save has a malformed commit receipt.");
            }
            else
            {
                if (committed.ValueKind == JsonValueKind.True)
                    throw Malformed(action, "Read response reports a mutation commit.");
                ValidateReadReply(action, reply);
            }
            return;
        }
        if (committed.ValueKind == JsonValueKind.True ||
            reply.TryGetProperty("value", out var failedValue) && failedValue.ValueKind != JsonValueKind.Null)
            throw Malformed(action, "Failed storage response also reports a commit or result.");
        if (!reply.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
            throw Malformed(action, "Missing or malformed storage error.");
        DemandUniqueFields(action, error);
        if (!error.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(code.GetString()) || code.GetString()!.Length > 128)
            throw Malformed(action, "Missing or malformed storage error code.");
        if (code.GetString() == "RevisionConflict" &&
            (action != "Save" || !error.TryGetProperty("documentId", out var id) || id.ValueKind != JsonValueKind.String ||
             !id.TryGetGuid(out var documentId) || documentId == Guid.Empty ||
             !error.TryGetProperty("expectedVersion", out var expected) || !TryVersion(expected, out _) ||
             !error.TryGetProperty("actualVersion", out var actual) || !TryVersion(actual, out _)))
            throw Malformed(action, "Malformed or inapplicable revision-conflict acknowledgment.");
        if (code.GetString() == "RevisionConflict" && arguments is { } sent)
        {
            var conflictId = error.GetProperty("documentId").GetGuid();
            _ = TryVersion(error.GetProperty("expectedVersion"), out var conflictExpected);
            if (sent.ValueKind != JsonValueKind.Object ||
                !sent.TryGetProperty("documentId", out var sentId) || sentId.ValueKind != JsonValueKind.String ||
                !sentId.TryGetGuid(out var expectedId) || expectedId != conflictId ||
                !sent.TryGetProperty("expectedVersion", out var sentVersion) ||
                !TryVersion(sentVersion, out var expectedVersion) || expectedVersion != conflictExpected)
                throw Malformed(action, "Revision-conflict identity does not match the original mutation.");
        }
    }

    private static void ValidateReadReply(string action, JsonElement reply)
    {
        if (!reply.TryGetProperty("value", out var value)) throw Malformed(action, "Missing browser notes result.");
        switch (action)
        {
            case "Load":
            case "LoadVersion":
                if (value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Object))
                    throw Malformed(action, "Malformed browser notes document result.");
                break;
            case "Versions":
                if (value.ValueKind != JsonValueKind.Array) throw Malformed(action, "Malformed browser notes history result.");
                break;
            case "List":
                if (value.ValueKind != JsonValueKind.Object ||
                    !value.TryGetProperty("documents", out var documents) || documents.ValueKind != JsonValueKind.Array ||
                    !value.TryGetProperty("history", out var history) || history.ValueKind != JsonValueKind.Array)
                    throw Malformed(action, "Malformed browser notes library result.");
                DemandUniqueFields(action, value);
                break;
        }
    }

    internal static bool TryVersion(JsonElement value, out long version)
    {
        version = 0;
        return value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out version) &&
            version >= 0 && value.GetString() == version.ToString(CultureInfo.InvariantCulture);
    }

    private static void DemandUniqueFields(string action, JsonElement value)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!names.Add(property.Name)) throw Malformed(action, "Duplicate browser notes response field.");
    }

    internal static IOException Malformed(string action, string message, Exception? inner = null) =>
        action is "Save" or "Delete" ? new NotesCommitOutcomeUnknownException(message, inner) : new IOException(message, inner);
}
