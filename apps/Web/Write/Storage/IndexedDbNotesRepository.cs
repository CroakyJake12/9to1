using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
namespace NineToOne.Web.Write.Storage;

/// <summary>This-origin browser storage of the actual canonical document. No account or remote ACL authority.</summary>
public sealed class IndexedDbNotesRepository(INotesBrowserTransport transport, INotesDocumentValidator validator) : INotesRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value, Json);
    private static string Hash(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    private void Validate(NotesDocument document)
    {
        NotesValidationResult result;
        try { result = validator.Validate(document); }
        catch (NullReferenceException error) { throw new InvalidDataException("Canonical document has null required content.", error); }
        if (!result.IsValid) throw new InvalidDataException(string.Join("; ", result.Issues.Where(x => x.IsError).Select(x => $"{x.Path}: {x.Message}")));
    }
    private async Task<JsonElement> Call(string action, object args, CancellationToken ct)
    {
        var reply = await transport.InvokeAsync(action, Args(args), ct);
        if (reply.ValueKind != JsonValueKind.Object || !reply.TryGetProperty("ok", out var ok) || ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw action is "Save" or "Delete" ? new NotesCommitOutcomeUnknownException("Malformed storage acknowledgment; inspect durable state before retrying.") : new IOException("Malformed storage response.");
        if (ok.GetBoolean()) return reply;
        var error = reply.GetProperty("error");
        var code = error.GetProperty("code").GetString();
        if (code == "Cancelled") throw new OperationCanceledException(ct);
        if (code == "RevisionConflict") throw new NotesRevisionConflictException(error.GetProperty("documentId").GetGuid(),
            long.Parse(error.GetProperty("expectedVersion").GetString()!, CultureInfo.InvariantCulture), long.Parse(error.GetProperty("actualVersion").GetString()!, CultureInfo.InvariantCulture));
        throw new IOException($"Browser notes storage: {code}.");
    }
    private static JsonElement Value(JsonElement reply) => reply.GetProperty("value");
    private async Task<JsonElement> Raw(Guid id, CancellationToken ct) => Value(await Call("Load", new { documentId = id }, ct));
    private (NotesDocument Document, NotesVersionInfo Info) Decode(JsonElement record, Guid? id = null)
    {
        try
        {
            var text = record.GetProperty("documentJson").GetString() ?? throw new InvalidDataException("Empty document JSON.");
            var document = JsonSerializer.Deserialize<NotesDocument>(text, Json) ?? throw new InvalidDataException("Empty document.");
            Validate(document);
            var info = JsonSerializer.Deserialize<NotesVersionInfo>(record.GetProperty("versionInfoJson").GetString() ?? throw new InvalidDataException("Empty version metadata JSON."), Json) ?? throw new InvalidDataException("Empty version metadata.");
            if (document.Id != record.GetProperty("id").GetGuid() || id is { } expected && document.Id != expected ||
                document.Version.ToString(CultureInfo.InvariantCulture) != record.GetProperty("version").GetString() ||
                Hash(text) != record.GetProperty("sha256").GetString() || info.Sha256 != Hash(text) || info.Version != document.Version || info.SizeBytes != Encoding.UTF8.GetByteCount(text) ||
                info.VersionId != $"v{document.Version:D10}-{info.CreatedAt.UtcDateTime:yyyyMMdd-HHmmssfff}" ||
                record.TryGetProperty("versionId", out var versionId) && versionId.GetString() != info.VersionId ||
                record.TryGetProperty("documentId", out var historyId) && historyId.GetGuid() != document.Id)
                throw new InvalidDataException("Document index, hash or version metadata does not match canonical content.");
            return (document, info);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw new InvalidDataException("Malformed browser document record.", error); }
    }
    public async Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var snapshot = Value(await Call("List", new { }, cancellationToken));
        var current = snapshot.GetProperty("documents").EnumerateArray().Select(x => x.Clone()).ToArray();
        var history = snapshot.GetProperty("history").EnumerateArray().Select(x => x.Clone()).ToArray();
        var ids = current.Select(record => Id(record, "id")).Concat(history.Select(record => Id(record, "documentId")))
            .Where(id => id != Guid.Empty).Distinct();
        var result = new List<NotesDocumentSummary>();
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NotesDocument? document = null;
            var record = current.FirstOrDefault(record => Id(record, "id") == id);
            if (record.ValueKind != JsonValueKind.Undefined)
            { try { document = Decode(record, id).Document; } catch (InvalidDataException) { } }
            document ??= MarkRecovered(Latest(history.Where(record => Id(record, "documentId") == id), id, cancellationToken));
            // Match the owner library: unreadable roots without valid recovery are not promoted into summaries.
            if (document is null) continue;
            result.Add(new(document.Id, document.Title, document.UpdatedAt, document.Version, document.Sections.Count,
                document.Sections.Sum(x => x.Pages.Sum(p => p.Blocks.Count)), NotesTextStatistics.Calculate(document).Words, document.Recovery.HasUnsavedRecovery));
        }
        return result.OrderByDescending(x => x.UpdatedAt).ToArray();
    }
    private static Guid Id(JsonElement record, string field) => record.ValueKind == JsonValueKind.Object &&
        record.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String && value.TryGetGuid(out var id) ? id : Guid.Empty;
    public async Task<NotesDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var raw = await Raw(documentId, cancellationToken);
        if (raw.ValueKind == JsonValueKind.Null) return await RecoverLatestAsync(documentId, cancellationToken);
        try { return Decode(raw, documentId).Document; }
        catch (InvalidDataException) { return await RecoverLatestAsync(documentId, cancellationToken) ?? throw new InvalidDataException("Current document and all history are invalid."); }
    }
    public async Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document);
        // Snapshot the real typed object before the first await; caller edits cannot alter the dispatched save.
        var candidate = JsonSerializer.Deserialize<NotesDocument>(JsonSerializer.Serialize(document, Json), Json)!;
        Validate(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        var expected = candidate.Version;
        var current = await Raw(candidate.Id, cancellationToken);
        var actual = 0L;
        var preserveCorruptCurrent = false;
        string? expectedHistoryRecords = null;
        if (current.ValueKind != JsonValueKind.Null)
        {
            try { actual = Decode(current, candidate.Id).Document.Version; }
            catch (InvalidDataException)
            {
                var records = await History(candidate.Id, cancellationToken);
                expectedHistoryRecords = "[" + string.Join(",", records.Select(record => record.GetRawText())) + "]";
                var recovered = Latest(records, candidate.Id, cancellationToken) ?? throw new InvalidDataException("Current document has no validated recovery copy; refusing overwrite.");
                actual = recovered.Version;
                preserveCorruptCurrent = true;
            }
        }
        else
        {
            var records = await History(candidate.Id, cancellationToken);
            expectedHistoryRecords = "[" + string.Join(",", records.Select(record => record.GetRawText())) + "]";
            var recovered = Latest(records, candidate.Id, cancellationToken);
            if (recovered is not null) actual = recovered.Version;
            else if (records.Length > 0) throw new InvalidDataException("Historical records exist but none are valid; refusing new-document overwrite.");
        }
        if (actual != expected) throw new NotesRevisionConflictException(candidate.Id, expected, actual);
        candidate.Version = checked(expected + 1);
        var now = DateTimeOffset.UtcNow;
        candidate.UpdatedAt = now;
        candidate.Recovery.LastAutosaveAt = now;
        candidate.Recovery.HasUnsavedRecovery = false;
        candidate.Recovery.RecoveryReason = string.Empty;
        Validate(candidate);
        var text = JsonSerializer.Serialize(candidate, Json);
        var sha = Hash(text);
        var versionId = $"v{candidate.Version:D10}-{now.UtcDateTime:yyyyMMdd-HHmmssfff}";
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "Save" : reason.Trim();
        if (normalizedReason.Length > 160) normalizedReason = normalizedReason[..160];
        var info = new NotesVersionInfo(versionId, candidate.Version, now, normalizedReason, Encoding.UTF8.GetByteCount(text), sha);
        var receipt = new NotesSaveResult(candidate.Id, candidate.Version, now, sha, $"indexeddb://nine-to-one-write/documents/{candidate.Id:D}", $"indexeddb://nine-to-one-write/history/{candidate.Id:D}/{versionId}");
        NotesCommitOutcomeUnknownException Unknown(string message, Exception? inner = null) => new(message, inner)
        { Operation = "Save", DocumentId = candidate.Id, ExpectedVersion = expected, IntendedVersion = candidate.Version, ContentSha256 = sha, VersionId = versionId };
        JsonElement reply;
        try
        {
            reply = await Call("Save", new { documentId = candidate.Id, expectedVersion = expected.ToString(CultureInfo.InvariantCulture), version = candidate.Version.ToString(CultureInfo.InvariantCulture),
                expectedRecord = current.GetRawText(), preserveCorruptCurrent, expectedHistoryRecords, documentJson = text, versionId, sha256 = sha, versionInfoJson = JsonSerializer.Serialize(info, Json), receiptJson = JsonSerializer.Serialize(receipt, Json) }, cancellationToken);
        }
        catch (NotesCommitOutcomeUnknownException error) { throw Unknown(error.Message, error); }
        if (!reply.TryGetProperty("committed", out var committed) || committed.ValueKind != JsonValueKind.True ||
            !reply.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String || value.GetString() != JsonSerializer.Serialize(receipt, Json))
            throw Unknown("Save has no verified commit receipt; inspect durable state before retrying.");
        // Known transaction completion wins over a cancellation observed after commit.
        if (cancellationToken.IsCancellationRequested || reply.TryGetProperty("cancelledAfterCommit", out var cancelled) && cancelled.ValueKind == JsonValueKind.True)
            receipt = receipt with { PostCommitWarning = "Save committed before cancellation; do not retry this revision." };
        // Follow the existing repository contract: update caller version only after a durable receipt.
        document.Version = candidate.Version; document.UpdatedAt = now; document.Recovery.LastAutosaveAt = now; document.Recovery.HasUnsavedRecovery = false; document.Recovery.RecoveryReason = string.Empty;
        return receipt;
    }
    public async Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            var reply = await Call("Delete", new { documentId }, cancellationToken);
            if (!reply.TryGetProperty("committed", out var committed) || committed.ValueKind != JsonValueKind.True)
                throw new NotesCommitOutcomeUnknownException("Delete has no commit acknowledgment; inspect durable state before retrying.");
        }
        catch (NotesCommitOutcomeUnknownException error)
        { throw new NotesCommitOutcomeUnknownException(error.Message, error) { Operation = "Delete", DocumentId = documentId }; }
    }

    private async Task<JsonElement[]> History(Guid id, CancellationToken ct) => Value(await Call("Versions", new { documentId = id }, ct)).EnumerateArray().Select(x => x.Clone()).ToArray();
    public async Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var result = new List<NotesVersionInfo>();
        foreach (var record in await History(documentId, cancellationToken))
        { cancellationToken.ThrowIfCancellationRequested(); try { result.Add(Decode(record, documentId).Info); } catch (InvalidDataException) { /* Invalid history is excluded, never promoted. */ } }
        return result.OrderByDescending(x => x.Version).ToArray();
    }
    public async Task<NotesDocument?> LoadVersionAsync(Guid documentId, string versionId, CancellationToken cancellationToken)
    {
        var record = Value(await Call("LoadVersion", new { documentId, versionId }, cancellationToken));
        return record.ValueKind == JsonValueKind.Null ? null : Decode(record, documentId).Document;
    }
    public async Task<NotesDocument?> RecoverLatestAsync(Guid documentId, CancellationToken cancellationToken)
    {
        return MarkRecovered(Latest(await History(documentId, cancellationToken), documentId, cancellationToken));
    }
    private static NotesDocument? MarkRecovered(NotesDocument? recovered)
    {
        if (recovered is null) return null;
        recovered.Recovery.HasUnsavedRecovery = true;
        recovered.Recovery.LastRecoveredAt = DateTimeOffset.UtcNow;
        recovered.Recovery.RecoveryReason = "Recovered from validated this-browser IndexedDB history.";
        recovered.Revisions.Add(new NotesRevision { Kind = NotesRevisionKind.Restored, CreatedAt = DateTimeOffset.UtcNow, Author = "Haven recovery", Summary = recovered.Recovery.RecoveryReason });
        return recovered;
    }
    private NotesDocument? Latest(IEnumerable<JsonElement> records, Guid id, CancellationToken ct)
    {
        var valid = new List<NotesDocument>();
        foreach (var record in records)
        { ct.ThrowIfCancellationRequested(); try { valid.Add(Decode(record, id).Document); } catch (InvalidDataException) { } }
        return valid.OrderByDescending(x => x.Version).FirstOrDefault();
    }
    public async Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        query = query.Trim(); if (query.Length < 2) return [];
        var hits = new List<NotesSearchHit>();
        foreach (var summary in await ListAsync(cancellationToken))
        {
            var document = await LoadAsync(summary.Id, cancellationToken); if (document is null) continue;
            foreach (var match in NotesDocumentSearch.Find(document, query, new NotesFindOptions(), 500 - hits.Count))
                hits.Add(new(document.Id, document.Title, match.SectionId, match.PageId, match.BlockId, match.BlockKind, match.Context, match.Start));
            if (hits.Count >= 500) break;
        }
        return hits;
    }
}
