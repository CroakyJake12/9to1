using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace NineToOne.Web.Productivity.Present.Storage;

/// <summary>This-origin durable canonical Present bytes; no account, Files or remote storage authority.</summary>
public sealed class IndexedDbPresentRepository(IPresentBrowserTransport transport, Action<PresentDocument> validateForSave)
    : IPresentRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value, Json);
    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static PresentDocument Clone(PresentDocument document) =>
        JsonSerializer.Deserialize<PresentDocument>(JsonSerializer.Serialize(document, Json), Json)
        ?? throw new InvalidDataException("The canonical presentation could not be copied.");

    private async Task<JsonElement> Call(string action, object arguments, CancellationToken token)
    {
        var reply = await transport.InvokeAsync(action, Args(arguments), token);
        if (reply.ValueKind != JsonValueKind.Object || !reply.TryGetProperty("ok", out var ok) ||
            ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw action is "Save" or "Delete" ? new PresentCommitOutcomeUnknownException("Malformed mutation acknowledgment.")
                : new IOException("Malformed Present storage response.");
        if (ok.GetBoolean()) return reply;
        if (!reply.TryGetProperty("error", out var error) || !error.TryGetProperty("code", out var codeValue))
            throw new IOException("Malformed Present storage refusal.");
        var code = codeValue.GetString();
        if (code == "Cancelled") throw new OperationCanceledException(token);
        if (code == "RevisionConflict")
            throw new PresentRevisionConflictException(error.GetProperty("documentId").GetGuid(),
                error.GetProperty("expectedVersion").GetInt32(), error.GetProperty("actualVersion").GetInt32());
        throw new IOException($"Present browser storage: {code}.");
    }
    private async Task<JsonElement> Raw(Guid id, CancellationToken token) =>
        (await Call("Load", new { documentId = id }, token)).GetProperty("value").Clone();
    private PresentDocument Decode(JsonElement copy, Guid id)
    {
        try
        {
            var text = copy.GetProperty("json").GetString() ?? throw new InvalidDataException("Empty presentation JSON.");
            var document = JsonSerializer.Deserialize<PresentDocument>(text, Json)
                ?? throw new InvalidDataException("Empty presentation.");
            validateForSave(document); // SAME canonical owning leaf, before Normalize can repair IDs/schema.
            if (document.Id != id || document.Version != copy.GetProperty("version").GetInt32() ||
                document.Version <= 0 || Hash(text) != copy.GetProperty("sha256").GetString())
                throw new InvalidDataException("Presentation identity, revision or hash differs from stored bytes.");
            document.Normalize(); // Existing canonical load semantics.
            return document;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw new InvalidDataException("Malformed presentation record.", error); }
    }
    private PresentDocument? Read(JsonElement record, Guid id)
    {
        if (record.ValueKind == JsonValueKind.Null) return null;
        try
        {
            if (record.GetProperty("id").GetGuid() != id) throw new InvalidDataException("Stored presentation ID differs.");
            try { return Decode(record.GetProperty("current"), id); }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException)
            {
                var previous = record.GetProperty("previous");
                if (previous.ValueKind == JsonValueKind.Null) throw;
                var recovered = Decode(previous, id);
                recovered.Recovery.RecoveredFromBackup = true;
                recovered.Recovery.RecoveredAt = DateTimeOffset.UtcNow;
                recovered.Recovery.Message = "Recovered the previous valid presentation after current browser bytes could not be read.";
                return recovered;
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw new InvalidDataException("Malformed presentation record.", error); }
    }
    public async Task<IReadOnlyList<PresentDocumentSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = (await Call("List", new { }, cancellationToken)).GetProperty("value");
        var result = new List<PresentDocumentSummary>();
        foreach (var row in rows.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            PresentDocument? document;
            try { document = Read(row, row.GetProperty("id").GetGuid()); }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException) { continue; }
            if (document is null) continue;
            result.Add(new(document.Id, document.Title, document.UpdatedAt, document.Version, document.Slides.Count,
                document.Recovery.RecoveredFromBackup, document.Metadata.TryGetValue("pinned", out var pinned) && bool.TryParse(pinned, out var value) && value));
        }
        return result.OrderByDescending(row => row.UpdatedAt).ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public async Task<PresentDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("Choose a canonical presentation ID.", nameof(documentId));
        return Read(await Raw(documentId, cancellationToken), documentId);
    }
    public async Task<PresentSaveResult> SaveAsync(PresentDocument document, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        validateForSave(document);
        var candidate = Clone(document); // Exact authored model snapshot before the first await.
        validateForSave(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        var expected = candidate.Version;
        if (expected < 0) throw new InvalidDataException("The presentation revision cannot be negative.");
        var current = await Raw(candidate.Id, cancellationToken);
        var persisted = Read(current, candidate.Id);
        if ((persisted?.Version ?? 0) != expected ||
            candidate.Recovery.RecoveredFromBackup != (persisted?.Recovery.RecoveredFromBackup == true))
            throw new PresentRevisionConflictException(candidate.Id, expected, persisted?.Version ?? 0);
        var recovered = candidate.Recovery.RecoveredFromBackup;
        candidate.Normalize();
        candidate.Version = checked(expected + 1);
        candidate.UpdatedAt = DateTimeOffset.UtcNow;
        candidate.Metadata["lastSaveReason"] = reason ?? string.Empty;
        candidate.Recovery.RecoveredFromBackup = false;
        candidate.Recovery.RecoveredAt = null;
        candidate.Recovery.Message = string.Empty;
        var text = JsonSerializer.Serialize(candidate, Json);
        var receipt = new PresentSaveResult(candidate.Id, candidate.Version, candidate.UpdatedAt,
            $"indexeddb://nine-to-one-present/documents/{candidate.Id:D}/current",
            $"indexeddb://nine-to-one-present/documents/{candidate.Id:D}/previous");
        var receiptJson = JsonSerializer.Serialize(receipt, Json);
        var reply = await Call("Save", new { documentId = candidate.Id, expectedVersion = expected, version = candidate.Version,
            expectedRecord = current.GetRawText(), recoveredFromBackup = recovered, documentJson = text,
            sha256 = Hash(text), receiptJson }, cancellationToken);
        if (!reply.TryGetProperty("committed", out var committed) || committed.ValueKind != JsonValueKind.True ||
            !reply.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String || value.GetString() != receiptJson)
            throw new PresentCommitOutcomeUnknownException("Save has no exact transaction-completion receipt; inspect saved state before retrying.");
        // Known commit survives cancellation after acknowledgement. No late cancellation can turn this into an uncommitted retry.
        document.Version = candidate.Version;
        document.UpdatedAt = candidate.UpdatedAt;
        document.Metadata ??= new(StringComparer.Ordinal);
        document.Metadata["lastSaveReason"] = candidate.Metadata["lastSaveReason"];
        document.Recovery ??= new();
        document.Recovery.RecoveredFromBackup = false;
        document.Recovery.RecoveredAt = null;
        document.Recovery.Message = string.Empty;
        return receipt;
    }
    public async Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("Choose a canonical presentation ID.", nameof(documentId));
        var current = await Raw(documentId, cancellationToken);
        var reply = await Call("Delete", new { documentId, expectedRecord = current.GetRawText() }, cancellationToken);
        if (!reply.TryGetProperty("committed", out var committed) || committed.ValueKind != JsonValueKind.True)
            throw new PresentCommitOutcomeUnknownException("Delete has no transaction-completion acknowledgment.");
    }
}

public interface IPresentBrowserTransport
{
    Task<JsonElement> InvokeAsync(string action, JsonElement arguments, CancellationToken cancellationToken);
}
public sealed class PresentCommitOutcomeUnknownException(string message, Exception? inner = null) : IOException(message, inner) { }
