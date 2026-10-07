using System.Text.Json;
using Haven.Core;
using NineToOne.Web.Spaces.Storage;

// Controlled transport protocol only. Actual IndexedDB durability, actor, permissions and provider
// capability still require the genuine browser/signed-session and canonical-owner acceptance paths.
internal static class BrowserTaskStorageProtocolControls
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task Main()
    {
        await ExactInt64AndCompleteCanonicalPayload();
        await WrongRunReceiptIsUnknownWithoutMutationReplay();
        await LateCancellationPreservesTheSameAcknowledgedCommit();
        await CorruptReadMetadataRefusesTheStoredTask();
        Console.WriteLine("4 canonical storage protocol controls passed.");
    }

    private static TaskExecutionSnapshot Snapshot()
    {
        var now = DateTimeOffset.Parse("2026-10-07T08:00:00+00:00");
        var task = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var context = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var run = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var accepted = Guid.Parse("44444444-4444-4444-8444-444444444444");
        var cloud = new TaskRunAttempt(Guid.Parse("55555555-5555-4555-8555-555555555555"),
            new TaskRunRouteCandidate("synthetic-observed-cloud", 1, "synthetic-cloud", "model", null, true, ["tools"]),
            TaskRunAttemptState.Failed, "historical-observation-not-authority", now, now);
        var local = new TaskRunAttempt(Guid.Parse("66666666-6666-4666-8666-666666666666"),
            new TaskRunRouteCandidate("synthetic-observed-local", 2, "synthetic-local", "model", "observed-artifact", false, ["tools"]),
            TaskRunAttemptState.Suspended, "historical-observation-not-authority", now, now, cloud.Id);
        return new(task, context, run, "same durable work", TaskExecutionLifecycle.Suspended,
            TaskExecutionDurability.RecoverableCheckpoint, 1,
            [new TaskPlanNode(accepted, null, "accepted work", TaskPlanNodeState.Completed, TaskActionInterruptionPolicy.AtomicCommit, 1)
                { Acceptance = new(cloud.Id, "historical-action-not-authority", now) }], [], [], ["historical-scope-not-authority"],
            accepted, now, now)
        {
            PersistenceRevision = 9_007_199_254_740_993,
            CheckpointId = Guid.Parse("77777777-7777-4777-8777-777777777777"),
            Attempts = [cloud, local],
            OwnerBinding = new(task, context, run, "synthetic-observed-actor", "synthetic-observed-profile", null, null,
                "historical-session-not-authority", "historical-receipt-not-authority")
        };
    }

    private static async Task ExactInt64AndCompleteCanonicalPayload()
    {
        var snapshot = Snapshot();
        var originalPayload = JsonSerializer.Serialize(snapshot, Json);
        var transport = new ControlledTransport((action, args, _) =>
        {
            Require(action == "Upsert", "Unexpected mutation path.");
            var row = args.GetProperty("row");
            Require(args.GetProperty("expectedRevision").GetString() == "9007199254740992" &&
                row.GetProperty("revision").GetString() == "9007199254740993", "Int64 CAS was rounded through JavaScript numbers.");
            Require(row.GetProperty("json").GetString() == originalPayload, "Complete canonical Task payload was replaced or truncated.");
            var decoded = JsonSerializer.Deserialize<TaskExecutionSnapshot>(row.GetProperty("json").GetString()!, Json)!;
            Require(decoded.TaskId == snapshot.TaskId && decoded.ContextId == snapshot.ContextId && decoded.ExecutionId == snapshot.ExecutionId &&
                decoded.Attempts.Count == 2 && decoded.Attempts[1].RetryOfAttemptId == snapshot.Attempts[0].Id &&
                decoded.Plan.Single().Acceptance == snapshot.Plan.Single().Acceptance && decoded.CheckpointId == snapshot.CheckpointId &&
                decoded.OwnerBinding == snapshot.OwnerBinding, "Fallback changed canonical IDs, accepted work, checkpoint or owner observation.");
            return Task.FromResult(Receipt(row));
        });
        await new IndexedDbTaskExecutionRepository(transport).UpsertAsync(snapshot, CancellationToken.None);
        Require(transport.Calls == 1, "The adapter replayed the mutation.");
    }

    private static async Task WrongRunReceiptIsUnknownWithoutMutationReplay()
    {
        var transport = new ControlledTransport((_, args, _) => Task.FromResult(Receipt(args.GetProperty("row"),
            Guid.Parse("88888888-8888-4888-8888-888888888888"))));
        var error = await Failure(new IndexedDbTaskExecutionRepository(transport).UpsertAsync(Snapshot(), CancellationToken.None));
        Require(error is TaskExecutionCommitOutcomeUnknownException && transport.Calls == 1,
            "A mismatched Run receipt became success or triggered an automatic replay.");
    }

    private static async Task LateCancellationPreservesTheSameAcknowledgedCommit()
    {
        using var stop = new CancellationTokenSource();
        var transport = new ControlledTransport((_, args, _) =>
        {
            var receipt = Receipt(args.GetProperty("row"));
            stop.Cancel();
            return Task.FromResult(receipt);
        });
        var actual = new IndexedDbTaskExecutionRepository(transport).UpsertAsync(Snapshot(), stop.Token);
        await actual;
        Require(actual.IsCompletedSuccessfully && stop.IsCancellationRequested && transport.Calls == 1,
            "Cancellation after the exact commit acknowledgement lost that receipt or replayed work.");
    }

    private static async Task CorruptReadMetadataRefusesTheStoredTask()
    {
        var snapshot = Snapshot();
        JsonElement captured = default;
        var transport = new ControlledTransport((action, args, _) =>
        {
            if (action == "Upsert") { captured = args.GetProperty("row").Clone(); return Task.FromResult(Receipt(captured)); }
            Require(action == "Get", "Unexpected recovery mutation.");
            var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(captured.GetRawText())!;
            fields["executionId"] = JsonSerializer.SerializeToElement(Guid.Parse("99999999-9999-4999-8999-999999999999"));
            return Task.FromResult(JsonSerializer.SerializeToElement(new { ok = true, value = fields }));
        });
        var repository = new IndexedDbTaskExecutionRepository(transport);
        await repository.UpsertAsync(snapshot, CancellationToken.None);
        Require(await Failure(repository.GetAsync(snapshot.TaskId, CancellationToken.None)) is InvalidDataException && transport.Calls == 2,
            "Corrupt durable identity was accepted or a replacement Task was created.");
    }

    private static JsonElement Receipt(JsonElement row, Guid? differentRun = null) => JsonSerializer.SerializeToElement(new
    {
        ok = true, committed = true,
        value = new
        {
            taskId = row.GetProperty("taskId").GetGuid(), contextId = row.GetProperty("contextId").GetGuid(),
            executionId = differentRun ?? row.GetProperty("executionId").GetGuid(),
            revision = row.GetProperty("revision").GetString(), sha256 = row.GetProperty("sha256").GetString()
        }
    });

    private sealed class ControlledTransport(Func<string, JsonElement, CancellationToken, Task<JsonElement>> original)
        : ITaskExecutionBrowserTransport
    {
        public int Calls { get; private set; }
        public Task<JsonElement> InvokeAsync(string action, JsonElement arguments, CancellationToken token)
        { Calls++; return original(action, arguments, token); }
    }
    private static async Task<Exception> Failure(Task actual)
    {
        try { await actual; } catch (Exception error) { return error; }
        throw new InvalidOperationException("The original storage protocol unexpectedly succeeded.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
