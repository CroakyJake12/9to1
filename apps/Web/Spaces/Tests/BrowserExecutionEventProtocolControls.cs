using System.Globalization;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using NineToOne.Web.Spaces.Storage;

// Owning wire counterexamples only. Controlled observations do not verify a signed actor,
// real IndexedDB/migration, browser lifecycle, canonical admission or a provider.
internal static class BrowserExecutionEventProtocolControls
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid Account = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly AuthenticatedResourceActor Actor = new("cake-task:" + new string('a', 64) + ":" + Account,
        "cake-account-profile:" + new string('a', 64) + ":" + Account, Account, null, "controlled-observation-not-authority");
    public static async Task Main()
    {
        await ImmutableCompleteEventAndHighSequence();
        await CollisionRefusesWithoutReplay();
        await WrongCanonicalRunReceiptRemainsUnknown();
        await LateCancellationKeepsExactAcknowledgedAppend();
        await CorruptProfileOrLinkRefusesRead();
        await OwningSearchFilterLimitAndFullExecutionSemantics();
        await MalformedEnvelopeAndLateRefusalKeepCompleteUnknownObservation();
        Console.WriteLine("7 canonical event wire controls passed; real auth/IndexedDB remain unverified.");
    }
    private static ExecutionEvent Event(Guid? execution = null, ExecutionActionType type = ExecutionActionType.ToolResult,
        string name = "canonical event", DateTimeOffset? time = null) => new(Guid.NewGuid(), execution ?? Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), ExecutionOrigin.Mcp, type, ExecutionActionStatus.Completed, name,
        "safe summary", "safe detail", "component", time ?? DateTimeOffset.Parse("2026-10-07T08:00:00.1234567+00:00"),
        DateTimeOffset.Parse("2026-10-07T07:59:59+00:00"), DateTimeOffset.Parse("2026-10-07T08:00:00+00:00"),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        new("observed-code", "observed-title", "safe failure", "safe provider", 503, 3,
            DateTimeOffset.Parse("2026-10-07T08:01:00+00:00"), "observed-component", true),
        new Dictionary<string, string> { ["safe"] = "original" });
    private static JsonElement Stored(JsonElement candidate, long sequence, Action<Dictionary<string, JsonElement>>? corrupt = null)
    {
        var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(candidate.GetRawText())!;
        fields["profileId"] = JsonSerializer.SerializeToElement(Actor.ProfileId);
        fields["sequence"] = JsonSerializer.SerializeToElement(sequence.ToString(CultureInfo.InvariantCulture));
        fields["sequenceSort"] = JsonSerializer.SerializeToElement(sequence.ToString("D19", CultureInfo.InvariantCulture));
        corrupt?.Invoke(fields); return JsonSerializer.SerializeToElement(fields);
    }
    private static JsonElement Reply(IEnumerable<JsonElement> rows, bool committed = true) =>
        JsonSerializer.SerializeToElement(new { ok = true, committed, value = rows.ToArray() });
    private sealed class Wire(Func<string, JsonElement, CancellationToken, Task<JsonElement>> source) : IExecutionEventBrowserTransport
    {
        public int Calls { get; private set; }
        public async Task<(AuthenticatedResourceActor Actor, JsonElement Reply)> InvokeAsync(string action, JsonElement args, CancellationToken token)
        { Calls++; return (BrowserExecutionEventProtocolControls.Actor, await source(action, args, token)); }
    }
    private static async Task ImmutableCompleteEventAndHighSequence()
    {
        var metadata = new Dictionary<string, string> { ["observation"] = "Bearer synthetic-secret" };
        var original = Event() with { SafeMetadata = metadata };
        JsonElement captured = default;
        var wire = new Wire((action, args, _) =>
        {
            Require(action == "ExecutionEvents.Append", "Wrong typed append operation.");
            captured = args.GetProperty("rows")[0].Clone(); metadata["observation"] = "modified after source acquisition";
            var payload = JsonSerializer.Deserialize<ExecutionEvent>(captured.GetProperty("json").GetString()!, Json)!;
            Require(payload.ExecutionId == original.ExecutionId && payload.TaskId == original.TaskId && payload.ActionId == original.ActionId &&
                payload.ParentActionId == original.ParentActionId && payload.RetryOfActionId == original.RetryOfActionId &&
                payload.RecoveryOfActionId == original.RecoveryOfActionId && payload.RemediationId == original.RemediationId &&
                payload.TabId == original.TabId && payload.ProjectId == original.ProjectId && payload.Failure == original.Failure &&
                payload.Timestamp == original.Timestamp, "Full canonical event links/failure/tick identity was lost.");
            Require(!payload.SafeMetadata!["observation"].Contains("synthetic-secret", StringComparison.Ordinal) &&
                !payload.SafeMetadata["observation"].Contains("modified", StringComparison.Ordinal), "Original metadata was mutable or not redacted.");
            return Task.FromResult(Reply([Stored(captured, 9_007_199_254_740_993)]));
        });
        await new IndexedDbExecutionEventRepository(wire).AppendAsync([original], CancellationToken.None);
        Require(wire.Calls == 1, "Append replayed.");
        var read = new Wire((_, _, _) => Task.FromResult(Reply([Stored(captured, 9_007_199_254_740_993)], false)));
        var stored = await new IndexedDbExecutionEventRepository(read).GetExecutionAsync(original.ExecutionId, CancellationToken.None);
        Require(stored.Single().EventId == original.EventId, "High Int64 sequence was truncated.");
    }
    private static async Task CollisionRefusesWithoutReplay()
    {
        var row = Event();
        var wire = new Wire((_, _, _) => Task.FromResult(JsonSerializer.SerializeToElement(new
        { ok = false, error = new { code = "ExecutionEventIdentityConflict", eventId = row.EventId } })));
        var error = await Failure(new IndexedDbExecutionEventRepository(wire).AppendAsync([row], CancellationToken.None));
        Require(error is BrowserExecutionEventIdentityConflictException conflict && conflict.EventId == row.EventId && wire.Calls == 1,
            "The original event collision was swallowed or dispatched again.");
    }
    private static async Task WrongCanonicalRunReceiptRemainsUnknown()
    {
        var wire = new Wire((_, args, _) => Task.FromResult(Reply([Stored(args.GetProperty("rows")[0], 1,
            fields => fields["executionId"] = JsonSerializer.SerializeToElement(Guid.NewGuid()))])));
        Require(await Failure(new IndexedDbExecutionEventRepository(wire).AppendAsync([Event()], CancellationToken.None))
            is BrowserExecutionEventAppendOutcomeUnknownException && wire.Calls == 1, "Wrong ExecutionId became an ack or replay.");
    }
    private static async Task LateCancellationKeepsExactAcknowledgedAppend()
    {
        using var stop = new CancellationTokenSource();
        var wire = new Wire((_, args, _) => { var reply = Reply([Stored(args.GetProperty("rows")[0], 1)]); stop.Cancel(); return Task.FromResult(reply); });
        var actual = new IndexedDbExecutionEventRepository(wire).AppendAsync([Event()], stop.Token);
        await actual; Require(actual.IsCompletedSuccessfully && wire.Calls == 1 && stop.IsCancellationRequested, "Late cancel lost the actual ack.");
    }
    private static async Task CorruptProfileOrLinkRefusesRead()
    {
        JsonElement captured = default;
        var setup = new Wire((_, args, _) => { captured = args.GetProperty("rows")[0].Clone(); return Task.FromResult(Reply([Stored(captured, 1)])); });
        var original = Event(); await new IndexedDbExecutionEventRepository(setup).AppendAsync([original], CancellationToken.None);
        foreach (var field in new[] { "profileId", "taskId", "sha256" })
        {
            var wire = new Wire((_, _, _) => Task.FromResult(Reply([Stored(captured, 1, fields => fields[field] =
                JsonSerializer.SerializeToElement(field == "taskId" ? Guid.NewGuid().ToString() : "corrupt"))], false)));
            Require(await Failure(new IndexedDbExecutionEventRepository(wire).GetExecutionAsync(original.ExecutionId, CancellationToken.None))
                is InvalidDataException && wire.Calls == 1, "Corrupt original metadata was accepted: " + field);
        }
    }
    private static async Task OwningSearchFilterLimitAndFullExecutionSemantics()
    {
        var execution = Guid.NewGuid();
        var prompt = Event(execution, ExecutionActionType.UserPrompt, "original prompt", DateTimeOffset.Parse("2026-10-07T01:00:00+00:00"));
        var match = Event(execution, name: "needle match", time: DateTimeOffset.Parse("2026-10-07T02:00:00+00:00"));
        var latest = Event(execution, name: "latest unmatched", time: DateTimeOffset.Parse("2026-10-07T03:00:00+00:00")) with { Status = ExecutionActionStatus.Failed };
        var other = Event(name: "needle second", time: DateTimeOffset.Parse("2026-10-07T00:00:00+00:00"));
        var rows = new List<JsonElement>(); long sequence = 0;
        var setup = new Wire((_, args, _) => {
            var batch = args.GetProperty("rows").EnumerateArray().Select(row => Stored(row, ++sequence)).ToArray(); rows.AddRange(batch);
            return Task.FromResult(Reply(batch));
        });
        await new IndexedDbExecutionEventRepository(setup).AppendAsync([prompt, match, latest, other], CancellationToken.None);
        var search = new Wire((action, _, _) => { Require(action == "ExecutionEvents.Search", "Search started work."); return Task.FromResult(Reply(rows, false)); });
        var repo = new IndexedDbExecutionEventRepository(search);
        var one = (await repo.SearchExecutionsAsync(" needle ", 1, CancellationToken.None)).Single();
        Require(one.ExecutionId == other.ExecutionId, "Native matching MAX(sequence) candidate limit was moved after timestamp ordering.");
        var all = await repo.SearchExecutionsAsync("needle", -1, CancellationToken.None);
        var same = all.First(); Require(same.ExecutionId == execution && same.PromptSummary == prompt.Name && same.Origin == prompt.Origin &&
            same.Status == latest.Status && same.TaskId == latest.TaskId && same.TabId == latest.TabId &&
            same.StartedAt == match.Timestamp && same.UpdatedAt == match.Timestamp && same.ActionCount == 1,
            "Filtered native extrema/counts or whole-execution prompt/latest record semantics changed.");
        Require((await repo.SearchExecutionsAsync(null, 0, CancellationToken.None)).Count == 0, "Native zero limit changed.");
        Require((await repo.SearchExecutionsAsync("need_e", -1, CancellationToken.None)).Count == 2, "SQLite LIKE underscore behavior changed.");
    }
    private static async Task MalformedEnvelopeAndLateRefusalKeepCompleteUnknownObservation()
    {
        var original = Event();
        var malformed = new[] { "[]", "{}", "{\"ok\":1}", "{\"ok\":false}",
            "{\"ok\":false,\"error\":null}", "{\"ok\":false,\"error\":{\"code\":null}}",
            "{\"ok\":true,\"committed\":\"true\",\"value\":[]}" }.Select(text =>
            { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }).ToList();
        foreach (var code in new[] { "Cancelled", "ExecutionEventIdentityConflict" })
            foreach (var invalidCommit in new object?[] { "false", 0, null, new { claimed = false } })
                malformed.Add(JsonSerializer.SerializeToElement(new { ok = false, committed = invalidCommit,
                    error = new { code, eventId = original.EventId }, value = new { retained = "unvalidated original" } }));
        foreach (var observed in malformed)
        {
            var wire = new Wire((_, _, _) => Task.FromResult(observed));
            var actual = new IndexedDbExecutionEventRepository(wire).AppendAsync([original], CancellationToken.None);
            var error = await Failure(actual);
            Require(error is BrowserExecutionEventAppendOutcomeUnknownException unknown && unknown.InnerException is not null &&
                unknown.OriginalReply is { } saved && saved.GetRawText() == observed.GetRawText() && actual.IsFaulted && wire.Calls == 1,
                "Malformed dispatched append became cancellation/conflict, lost full reply/cause, or replayed: " + observed.GetRawText());
        }
        foreach (var code in new[] { "Cancelled", "ExecutionEventIdentityConflict" })
        {
            var observed = JsonSerializer.SerializeToElement(new { ok = false, committed = true,
                error = new { code, eventId = original.EventId }, value = new { eventId = original.EventId, untouched = "late commit bytes" } });
            var wire = new Wire((_, _, _) => Task.FromResult(observed));
            var actual = new IndexedDbExecutionEventRepository(wire).AppendAsync([original], CancellationToken.None);
            var error = await Failure(actual);
            Require(error is BrowserExecutionEventStorageRefusalException refusal && refusal.OriginalReply.GetRawText() == observed.GetRawText() &&
                actual.IsFaulted && wire.Calls == 1, "Reported late commit lost complete observation to ordinary cancellation/conflict.");
        }
        foreach (var observed in new[] {
            JsonSerializer.SerializeToElement(new { ok = false, error = new { code = "Cancelled" } }),
            JsonSerializer.SerializeToElement(new { ok = false, committed = false, error = new { code = "Cancelled" } }) })
        {
            var wire = new Wire((_, _, _) => Task.FromResult(observed));
            var actual = new IndexedDbExecutionEventRepository(wire).AppendAsync([original], CancellationToken.None);
            Require(await Failure(actual) is OperationCanceledException && actual.IsCanceled && wire.Calls == 1,
                "A well-formed genuine no-commit cancellation refusal lost its finite classification.");
        }
        var conflictReply = JsonSerializer.SerializeToElement(new { ok = false, committed = false,
            error = new { code = "ExecutionEventIdentityConflict", eventId = original.EventId } });
        var conflictWire = new Wire((_, _, _) => Task.FromResult(conflictReply));
        Require(await Failure(new IndexedDbExecutionEventRepository(conflictWire).AppendAsync([original], CancellationToken.None))
            is BrowserExecutionEventIdentityConflictException conflict && conflict.EventId == original.EventId && conflictWire.Calls == 1,
            "A well-formed no-commit conflict was weakened or replayed.");
    }
    private static async Task<Exception> Failure(Task actual)
    { try { await actual; } catch (Exception error) { return error; } throw new InvalidOperationException("Original unexpectedly succeeded."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
