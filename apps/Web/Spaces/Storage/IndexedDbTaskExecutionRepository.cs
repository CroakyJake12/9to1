using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace NineToOne.Web.Spaces.Storage;

/// <summary>This-origin platform adapter of the canonical Task/Run snapshot and revision CAS.
/// Stored owner/receipt observations cannot recreate a live issuer, provider or permission.</summary>
public sealed class IndexedDbTaskExecutionRepository(ITaskExecutionBrowserTransport transport) : ITaskExecutionRepository
{
    // Matches the maintained native UnifiedPersistenceJson options; no alternate task model.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string Revision(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static long ReadRevision(JsonElement row)
    {
        var text = row.GetProperty("revision").GetString();
        if (text is null || !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            value < 0 || Revision(value) != text) throw new InvalidDataException("The durable task revision is invalid.");
        return value;
    }

    private async Task<JsonElement> Call(string action, object args, CancellationToken token)
    {
        var reply = await transport.InvokeAsync(action, JsonSerializer.SerializeToElement(args, Json), token);
        try
        {
        if (reply.ValueKind != JsonValueKind.Object || !reply.TryGetProperty("ok", out var ok) ||
            ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw action == "Upsert" ? new TaskExecutionCommitOutcomeUnknownException("Task CAS acknowledgement was malformed.")
                : new IOException("Task browser storage reply was malformed.");
        if (ok.GetBoolean()) return reply;
        var code = reply.GetProperty("error").GetProperty("code").GetString();
        if (code == "Cancelled") throw new OperationCanceledException(token);
        if (code == "RevisionConflict")
        {
            var error = reply.GetProperty("error");
            throw new TaskExecutionRevisionConflictException(error.GetProperty("taskId").GetGuid(),
                long.Parse(error.GetProperty("expectedRevision").GetString()!, CultureInfo.InvariantCulture),
                long.Parse(error.GetProperty("proposedRevision").GetString()!, CultureInfo.InvariantCulture));
        }
        throw new IOException($"Task browser storage refused: {code}.");
        }
        catch (Exception error) when (error is JsonException or FormatException or KeyNotFoundException ||
            error is InvalidOperationException and not TaskExecutionRevisionConflictException)
        {
            throw action == "Upsert"
                ? new TaskExecutionCommitOutcomeUnknownException("The original task CAS receipt was malformed; reload before retrying.", error)
                : new IOException("Task browser storage reply was malformed.", error);
        }
    }

    public async Task UpsertAsync(TaskExecutionSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.TaskId == Guid.Empty || snapshot.ContextId == Guid.Empty || snapshot.ExecutionId == Guid.Empty ||
            snapshot.PersistenceRevision < 1)
            throw new ArgumentException("A canonical task, context, execution and next positive revision are required.", nameof(snapshot));
        // Capture every model field and metadata before the first await. Preserve all
        // attempts, accepted actions and checkpoint IDs in the SAME canonical payload.
        var payload = JsonSerializer.Serialize(snapshot, Json);
        var captured = JsonSerializer.Deserialize<TaskExecutionSnapshot>(payload, Json)
            ?? throw new InvalidDataException("The original task snapshot could not be captured.");
        var hash = Hash(payload);
        var revision = Revision(captured.PersistenceRevision);
        var expected = Revision(captured.PersistenceRevision - 1);
        cancellationToken.ThrowIfCancellationRequested();
        var row = new
        {
            taskId = captured.TaskId, contextId = captured.ContextId, executionId = captured.ExecutionId,
            createdAt = captured.CreatedAt.ToString("O"), updatedAt = captured.UpdatedAt.ToString("O"),
            state = (int)captured.State, revision, json = payload, sha256 = hash
        };
        var reply = await Call("Upsert", new { expectedRevision = expected, row }, cancellationToken);
        // Only the actual readwrite transaction's oncomplete provides this receipt.
        try
        {
        if (!reply.TryGetProperty("committed", out var committed) || committed.ValueKind != JsonValueKind.True ||
            !reply.TryGetProperty("value", out var value) || value.GetProperty("taskId").GetGuid() != captured.TaskId ||
            value.GetProperty("contextId").GetGuid() != captured.ContextId ||
            value.GetProperty("executionId").GetGuid() != captured.ExecutionId ||
            value.GetProperty("revision").GetString() != revision || value.GetProperty("sha256").GetString() != hash)
            throw new TaskExecutionCommitOutcomeUnknownException("No exact canonical task CAS completion receipt was returned; reload before retrying.");
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        { throw new TaskExecutionCommitOutcomeUnknownException("The actual task CAS completion receipt could not be validated; reload before retrying.", error); }
        // An acknowledged write stays acknowledged even if cancellation arrives later.
    }

    private static TaskExecutionSnapshot Decode(JsonElement row)
    {
        try
        {
            var text = row.GetProperty("json").GetString() ?? throw new InvalidDataException("The task payload is missing.");
            var snapshot = JsonSerializer.Deserialize<TaskExecutionSnapshot>(text, Json)
                ?? throw new InvalidDataException("The canonical task payload is empty.");
            if (snapshot.TaskId == Guid.Empty || snapshot.ContextId == Guid.Empty || snapshot.ExecutionId == Guid.Empty ||
                row.GetProperty("taskId").GetGuid() != snapshot.TaskId ||
                row.GetProperty("contextId").GetGuid() != snapshot.ContextId ||
                row.GetProperty("executionId").GetGuid() != snapshot.ExecutionId ||
                ReadRevision(row) != snapshot.PersistenceRevision || Hash(text) != row.GetProperty("sha256").GetString() ||
                row.GetProperty("state").GetInt32() != (int)snapshot.State ||
                row.GetProperty("createdAt").GetString() != snapshot.CreatedAt.ToString("O") ||
                row.GetProperty("updatedAt").GetString() != snapshot.UpdatedAt.ToString("O"))
                throw new InvalidDataException("The durable task identity, revision, metadata or payload hash differs.");
            return snapshot;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        { throw new InvalidDataException("The canonical task record is malformed.", error); }
    }

    public async Task<TaskExecutionSnapshot?> GetAsync(Guid taskId, CancellationToken cancellationToken)
    {
        if (taskId == Guid.Empty) throw new ArgumentException("Choose a canonical task ID.", nameof(taskId));
        var row = (await Call("Get", new { taskId }, cancellationToken)).GetProperty("value");
        if (row.ValueKind == JsonValueKind.Null) return null;
        var snapshot = Decode(row);
        if (snapshot.TaskId != taskId) throw new InvalidDataException("A different task was returned.");
        return snapshot;
    }

    public async Task<TaskExecutionSnapshot?> GetByContextAsync(Guid contextId, CancellationToken cancellationToken)
    {
        if (contextId == Guid.Empty) throw new ArgumentException("Choose the actual canonical context ID.", nameof(contextId));
        var row = (await Call("GetByContext", new { contextId }, cancellationToken)).GetProperty("value");
        if (row.ValueKind == JsonValueKind.Null) return null;
        var snapshot = Decode(row);
        if (snapshot.ContextId != contextId) throw new InvalidDataException("A different task context was returned.");
        return snapshot;
    }

    public async Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken cancellationToken)
    {
        var rows = (await Call("GetResumable", new { }, cancellationToken)).GetProperty("value");
        var result = new List<TaskExecutionSnapshot>();
        foreach (var row in rows.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = Decode(row);
            if (snapshot.State is not (TaskExecutionLifecycle.Running or TaskExecutionLifecycle.WaitingSafeBoundary or
                TaskExecutionLifecycle.Blocked or TaskExecutionLifecycle.Suspended))
                throw new InvalidDataException("A non-resumable task was returned by the platform adapter.");
            result.Add(snapshot);
        }
        return result;
    }
}

public interface ITaskExecutionBrowserTransport
{
    Task<JsonElement> InvokeAsync(string action, JsonElement arguments, CancellationToken cancellationToken);
}

/// <summary>Transport lost the actual mutation receipt; inspect the SAME durable task before retrying.</summary>
public sealed class TaskExecutionCommitOutcomeUnknownException(string message, Exception? cause = null) : IOException(message, cause);
