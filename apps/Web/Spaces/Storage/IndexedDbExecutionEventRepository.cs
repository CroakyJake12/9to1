using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace NineToOne.Web.Spaces.Storage;

/// <summary>Complete canonical event persistence through the same verified-profile browser
/// transport. History and acknowledgements are observations, never Task/Run or permission admission.</summary>
public sealed class IndexedDbExecutionEventRepository(IExecutionEventBrowserTransport transport) : IExecutionEventRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static void Id(Guid id) { if (id == Guid.Empty) throw new InvalidDataException("A complete canonical event identity is required."); }
    private static void OptionalId(Guid? id) { if (id is { } value) Id(value); }
    private static void Validate(ExecutionEvent row)
    {
        Id(row.EventId); Id(row.ExecutionId); Id(row.ActionId);
        foreach (var id in new[] { row.ParentActionId, row.RetryOfActionId, row.RecoveryOfActionId,
            row.RemediationId, row.TaskId, row.TabId, row.ProjectId }) OptionalId(id);
        if (!Enum.IsDefined(row.Origin) || !Enum.IsDefined(row.ActionType) || !Enum.IsDefined(row.Status) || row.Name is null ||
            row.Failure is { } failure && (failure.Code is null || failure.Title is null || failure.Message is null) ||
            row.SafeMetadata is { } metadata && metadata.Any(pair => pair.Key is null || pair.Value is null))
            throw new InvalidDataException("The canonical execution event is invalid.");
    }
    private static ExecutionEvent CaptureSafe(ExecutionEvent original)
    {
        ArgumentNullException.ThrowIfNull(original); Validate(original);
        var metadata = original.SafeMetadata?.ToDictionary(
            pair => SensitiveTextRedactor.Redact(pair.Key, 128),
            pair => SensitiveTextRedactor.Redact(pair.Value, 2_000), StringComparer.Ordinal);
        return original with
        {
            Name = SensitiveTextRedactor.Redact(original.Name, 256),
            SafeReasoningSummary = SensitiveTextRedactor.Redact(original.SafeReasoningSummary, 2_000),
            SafeDetail = SensitiveTextRedactor.Redact(original.SafeDetail, 8_000),
            ComponentId = SensitiveTextRedactor.Redact(original.ComponentId, 256),
            Failure = original.Failure is null ? null : original.Failure with
            {
                Message = SensitiveTextRedactor.Redact(original.Failure.Message, 4_000),
                ProviderMessage = SensitiveTextRedactor.Redact(original.Failure.ProviderMessage, 4_000)
            },
            SafeMetadata = metadata is null ? null : new ReadOnlyDictionary<string, string>(metadata)
        };
    }
    private static object Encode(ExecutionEvent row, string json) => new
    {
        profileId = "", row.EventId, row.ExecutionId, row.ActionId, row.ParentActionId,
        origin = (int)row.Origin, actionType = (int)row.ActionType, status = (int)row.Status, row.Name,
        row.ComponentId, timestamp = row.Timestamp.ToString("O"), row.StartedAt, row.EndedAt,
        row.RetryOfActionId, row.RecoveryOfActionId, row.RemediationId, row.TaskId, row.TabId, row.ProjectId,
        json, sha256 = Hash(json)
    };
    private sealed record Stored(ExecutionEvent Event, string Json, long Sequence, string Timestamp);
    private static Stored Decode(JsonElement row, AuthenticatedResourceActor actor)
    {
        var json = row.GetProperty("json").GetString() ?? throw new InvalidDataException("No complete canonical event payload.");
        var value = JsonSerializer.Deserialize<ExecutionEvent>(json, Json) ?? throw new InvalidDataException("No canonical event was decoded.");
        Validate(value);
        var sequence = row.GetProperty("sequence").GetString();
        if (row.GetProperty("profileId").GetString() != actor.ProfileId || row.GetProperty("sha256").GetString() != Hash(json) ||
            !long.TryParse(sequence, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1 ||
            number.ToString(CultureInfo.InvariantCulture) != sequence ||
            row.GetProperty("sequenceSort").GetString() != number.ToString("D19", CultureInfo.InvariantCulture))
            throw new InvalidDataException("The original profile, sequence or event payload hash differs.");
        using var encoded = JsonDocument.Parse(JsonSerializer.Serialize(Encode(value, json), Json));
        foreach (var field in encoded.RootElement.EnumerateObject())
        {
            if (field.Name == "profileId") continue;
            if (!row.TryGetProperty(field.Name, out var actual) || actual.ValueKind != field.Value.ValueKind ||
                (actual.ValueKind == JsonValueKind.String ? actual.GetString() != field.Value.GetString() : actual.GetRawText() != field.Value.GetRawText()))
                throw new InvalidDataException("The complete canonical event linkage or metadata differs: " + field.Name);
        }
        return new(value, json, number, row.GetProperty("timestamp").GetString()!);
    }
    private static Stored[] Rows(JsonElement rows, AuthenticatedResourceActor actor)
    {
        var values = rows.EnumerateArray().Select(row => Decode(row, actor)).ToArray();
        if (values.Select(row => row.Event.EventId).Distinct().Count() != values.Length ||
            values.Select(row => row.Sequence).Distinct().Count() != values.Length)
            throw new InvalidDataException("The same profile returned repeated event identity or sequence.");
        return values;
    }
    private async Task<(AuthenticatedResourceActor Actor, JsonElement Reply)> Call(string action, object args, CancellationToken token)
    {
        var actual = await transport.InvokeAsync(action, JsonSerializer.SerializeToElement(args, Json), token);
        try
        {
            var reply = actual.Reply;
            if (actual.Actor is null || actual.Actor.ProfileId is null ||
                !actual.Actor.ProfileId.StartsWith("cake-account-profile:", StringComparison.Ordinal))
                throw new InvalidDataException("No same verified-profile boundary supplied this event reply.");
            if (reply.ValueKind != JsonValueKind.Object || !reply.TryGetProperty("ok", out var ok) ||
                ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException("Malformed original event storage reply.");
            if (!ok.GetBoolean())
            {
                var error = reply.GetProperty("error");
                if (error.ValueKind != JsonValueKind.Object || !error.TryGetProperty("code", out var codeValue) ||
                    codeValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(codeValue.GetString()))
                    throw new InvalidDataException("Malformed original event storage refusal.");
                var code = codeValue.GetString();
                var hasCommit = reply.TryGetProperty("committed", out var committed);
                if (hasCommit && committed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidDataException("Malformed original event storage commit observation.");
                // A reported late commit is complete unvalidated custody, even when its code
                // looks like cancellation or identity conflict. It is never a no-write witness.
                if (hasCommit && committed.GetBoolean()) throw new BrowserExecutionEventStorageRefusalException(code, reply);
                if (code == "ExecutionEventIdentityConflict")
                {
                    var eventId = error.GetProperty("eventId").GetGuid(); Id(eventId);
                    throw new BrowserExecutionEventIdentityConflictException(eventId);
                }
                if (code == "Cancelled") throw new OperationCanceledException(token);
                throw new BrowserExecutionEventStorageRefusalException(code, reply);
            }
            return actual;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or FormatException or InvalidOperationException or InvalidDataException)
        {
            if (action == "ExecutionEvents.Append") throw new BrowserExecutionEventAppendOutcomeUnknownException("The actual append receipt is malformed; inspect the original profile before retrying.", error, actual.Reply);
            throw new InvalidDataException("The actual canonical event reply is malformed.", error);
        }
    }
    public async Task AppendAsync(IReadOnlyList<ExecutionEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0) return; // Native repository's empty-list contract; no publication is accepted here.
        var captured = events.Select(CaptureSafe).Select(row => (Event: row, Json: JsonSerializer.Serialize(row, Json))).ToArray();
        foreach (var group in captured.GroupBy(row => row.Event.EventId))
            if (group.Any(row => row.Json != group.First().Json)) throw new BrowserExecutionEventIdentityConflictException(group.Key);
        var supplied = captured.Select(row => Encode(row.Event, row.Json)).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        var actual = await Call("ExecutionEvents.Append", new { rows = supplied }, cancellationToken);
        try
        {
            if (actual.Reply.GetProperty("committed").ValueKind != JsonValueKind.True)
                throw new InvalidDataException("No original complete append transaction acknowledgement.");
            var returned = Rows(actual.Reply.GetProperty("value"), actual.Actor);
            var unique = captured.GroupBy(row => row.Event.EventId).Select(group => group.First()).ToArray();
            if (returned.Length != unique.Length || unique.Any(row => !returned.Any(stored =>
                stored.Event.EventId == row.Event.EventId && stored.Json == row.Json)))
                throw new InvalidDataException("The original append did not conserve each complete canonical event.");
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or FormatException or InvalidOperationException or InvalidDataException)
        { throw new BrowserExecutionEventAppendOutcomeUnknownException("The original append acknowledgement cannot be validated; no mutation was replayed.", error, actual.Reply); }
        // A validated oncomplete receipt stays acknowledged when cancellation arrives later.
    }
    public async Task<IReadOnlyList<ExecutionEvent>> GetExecutionAsync(Guid executionId, CancellationToken cancellationToken)
    {
        Id(executionId);
        var actual = await Call("ExecutionEvents.GetExecution", new { executionId }, cancellationToken);
        var stored = Rows(actual.Reply.GetProperty("value"), actual.Actor);
        if (stored.Any(row => row.Event.ExecutionId != executionId)) throw new InvalidDataException("A different canonical ExecutionId was returned.");
        return stored.OrderBy(row => row.Sequence).Select(row => row.Event).ToArray();
    }
    // SQLite LIKE has ASCII-only case folding and Unicode-character '_' matching.
    // Preserve the owning SQL's literal '%' replacement (brackets are not LIKE escaping).
    private static bool Like(string text, string query)
    {
        static int Fold(Rune value) => value.Value is >= 65 and <= 90 ? value.Value + 32 : value.Value;
        static string SqliteCString(string value) { var nul = value.IndexOf('\0'); return nul < 0 ? value : value[..nul]; }
        var value = SqliteCString(text).EnumerateRunes().Select(Fold).ToArray();
        var pattern = SqliteCString("%" + query.Replace("%", "[%]", StringComparison.Ordinal) + "%").EnumerateRunes().Select(Fold).ToArray();
        var previous = new bool[value.Length + 1]; previous[0] = true;
        foreach (var character in pattern)
        {
            var next = new bool[value.Length + 1]; next[0] = character == '%' && previous[0];
            for (var index = 1; index <= value.Length; index++) next[index] = character == '%'
                ? previous[index] || next[index - 1] : previous[index - 1] && (character == '_' || character == value[index - 1]);
            previous = next;
        }
        return previous[value.Length];
    }
    public async Task<IReadOnlyList<ExecutionSummary>> SearchExecutionsAsync(string? query, int limit, CancellationToken cancellationToken)
    {
        var normalized = query?.Trim() ?? string.Empty;
        var actual = await Call("ExecutionEvents.Search", new { query = normalized, limit }, cancellationToken);
        var rows = Rows(actual.Reply.GetProperty("value"), actual.Actor);
        var groups = rows.GroupBy(row => row.Event.ExecutionId).Select(group =>
        {
            var whole = group.OrderBy(row => row.Sequence).ToArray();
            var matched = whole.Where(row => normalized.Length == 0 || Like(row.Event.Name, normalized)).ToArray();
            return (Whole: whole, Matched: matched);
        }).Where(group => group.Matched.Length != 0).OrderByDescending(group => group.Matched.Max(row => row.Sequence));
        var selected = limit < 0 ? groups : groups.Take(limit);
        return selected.Select(group =>
        {
            var first = group.Whole[0].Event; var last = group.Whole[^1].Event;
            var startText = group.Matched.Select(row => row.Timestamp).Min(StringComparer.Ordinal)!;
            var updateText = group.Matched.Select(row => row.Timestamp).Max(StringComparer.Ordinal)!;
            var started = DateTimeOffset.Parse(startText, CultureInfo.InvariantCulture);
            var updated = DateTimeOffset.Parse(updateText, CultureInfo.InvariantCulture);
            return (Timestamp: updateText, LastSequence: group.Matched.Max(row => row.Sequence), Summary: new ExecutionSummary(
                first.ExecutionId, group.Whole.FirstOrDefault(row => row.Event.ActionType == ExecutionActionType.UserPrompt)?.Event.Name ?? "Execution",
                first.Origin, last.Status, started, updated, group.Matched.Select(row => row.Event.ActionId).Distinct().Count(),
                updated - started, last.TabId, last.TaskId));
        }).OrderByDescending(row => row.Timestamp, StringComparer.Ordinal).ThenByDescending(row => row.LastSequence)
            .Select(row => row.Summary).ToArray();
    }
}

/// <summary>Implemented by the same actual browser transport/verified actor composition.
/// A controlled implementation is useful only for wire counterexamples, never auth/IDB acceptance.</summary>
public interface IExecutionEventBrowserTransport
{
    Task<(AuthenticatedResourceActor Actor, JsonElement Reply)> InvokeAsync(string action, JsonElement arguments, CancellationToken token);
}
public sealed class BrowserExecutionEventIdentityConflictException(Guid eventId)
    : IOException("The same EventId has different canonical bytes; no original record was replaced.")
{ public Guid EventId { get; } = eventId; }
/// <summary>Complete unvalidated platform refusal, including any reported late commit.
/// This is not a canonical append acknowledgement, current actor grant or replay permission.</summary>
public sealed class BrowserExecutionEventStorageRefusalException(string? code, JsonElement originalReply)
    : IOException("Actual canonical event storage refused: " + code)
{ public JsonElement OriginalReply { get; } = originalReply.Clone(); }
/// <summary>The same dispatched append could not supply a valid receipt. Preserve any
/// complete observed envelope and original validation cause; neither permits replay.</summary>
public sealed class BrowserExecutionEventAppendOutcomeUnknownException(string message, Exception? cause = null,
    JsonElement? originalReply = null) : IOException(message, cause)
{ public JsonElement? OriginalReply { get; } = originalReply?.Clone(); }
