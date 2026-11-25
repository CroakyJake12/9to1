using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Desktop.Tests;

public sealed class ScheduledTaskRuntimeTests
{
    [Fact]
    public async Task ConditionRunPersistsOneSuccessfulRun()
    {
        var repository = new MemoryAutomationRepository();
        var model = new FakeModelClient("{\"conditionMet\":true,\"report\":\"The release is available.\"}");
        var runner = new ScheduledTaskRunner(repository, model, new ScheduledTaskScheduleCalculator());
        var task = Definition(AutomationScheduleKind.ConditionWatch, enabled: true);
        await repository.UpsertAsync(task, CancellationToken.None);

        var run = await runner.RunOneAsync(task, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(AutomationRunStatus.Succeeded, run.Status);
        Assert.Single(repository.Runs);
        Assert.Contains("conditionMet", run.Result ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("release", run.Result ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailedRunRetriesThreeTimesAndPersistsFailure()
    {
        var repository = new MemoryAutomationRepository();
        var model = new FakeModelClient("unused") { Failure = new IOException("Provider unavailable.") };
        var runner = new ScheduledTaskRunner(repository, model, new ScheduledTaskScheduleCalculator());
        var task = Definition(AutomationScheduleKind.Daily, enabled: false);
        await repository.UpsertAsync(task, CancellationToken.None);

        var run = await runner.RunOneAsync(task, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(3, model.CompleteCount);
        Assert.Equal(AutomationRunStatus.Failed, run.Status);
        Assert.Contains("Provider unavailable", run.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Single(repository.Runs);
        Assert.Null(repository.Definitions[task.Id].NextRunAt);
    }

    [Fact]
    public void UnstructuredConditionResponseFailsClosed()
    {
        var result = ScheduledTaskConditionParser.Parse("maybe, but I cannot tell");
        Assert.False(result.ConditionMet);
        Assert.Contains("unstructured", result.Report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Due_runner_never_executes_through_a_store_without_atomic_admission()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new MemoryAutomationRepository();
        var model = new FakeModelClient("must not run");
        var now = DateTimeOffset.UtcNow;
        var task = Definition(AutomationScheduleKind.Daily, enabled: true) with { NextRunAt = now.AddMinutes(-1) };
        await repository.UpsertAsync(task, ct);

        var result = await new ScheduledTaskRunner(repository, model, new ScheduledTaskScheduleCalculator()).RunDueAsync(now, ct);

        Assert.Equal(new ScheduledTaskBatchResult(1, 0, 0, 0, 1), result);
        Assert.Equal(0, model.CompleteCount);
        Assert.Empty(repository.Runs);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Due_runner_does_not_dispatch_if_exact_SQLite_occurrence_was_disabled_or_edited(bool disable)
    {
        var ct = TestContext.Current.CancellationToken;
        using var paths = new ScheduledPaths();
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(ct);
        var writer = new AutomationRepository(database);
        var now = DateTimeOffset.UtcNow;
        var original = Definition(AutomationScheduleKind.Daily, enabled: true) with { NextRunAt = now.AddMinutes(-1) };
        await writer.UpsertAsync(original, ct);
        var changed = disable ? original with { IsEnabled = false }
            : original with { Instruction = "New saved instruction (same UpdatedAt on purpose)." };
        var intercepted = new DueReadBarrier(new AutomationRepository(database), () => writer.UpsertAsync(changed, ct));
        var model = new FakeModelClient("must not run");
        var runner = new ScheduledTaskRunner(intercepted, model, new ScheduledTaskScheduleCalculator());

        var result = await runner.RunDueAsync(now, ct);

        Assert.Equal(new ScheduledTaskBatchResult(1, 0, 0, 0, 1), result);
        Assert.Equal(0, model.CompleteCount);
        Assert.Empty(await writer.GetRunsAsync(original.Id, 5, ct));
        Assert.Equal(changed, Assert.Single(await writer.GetAllAsync(ct)));
    }

    [Fact]
    public async Task Actual_SQLite_due_completion_retains_a_newer_schedule_without_losing_the_older_result()
    {
        var ct = TestContext.Current.CancellationToken;
        using var paths = new ScheduledPaths();
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(ct);
        var repo = new AutomationRepository(database);
        var now = DateTimeOffset.UtcNow;
        var original = Definition(AutomationScheduleKind.Daily, enabled: true) with { NextRunAt = now.AddMinutes(-1) };
        await repo.UpsertAsync(original, ct);
        var replacement = original with { Instruction = "New saved instruction", NextRunAt = now.AddHours(2) };
        var model = new FakeModelClient("original completed") { BeforeCompletion = () => repo.UpsertAsync(replacement, ct) };

        var result = await new ScheduledTaskRunner(repo, model, new ScheduledTaskScheduleCalculator()).RunDueAsync(now, ct);

        Assert.Equal(new ScheduledTaskBatchResult(1, 1, 1, 0, 0), result);
        Assert.Equal(1, model.CompleteCount);
        Assert.Equal(replacement, Assert.Single(await repo.GetAllAsync(ct)));
        var run = Assert.Single(await repo.GetRunsAsync(original.Id, 5, ct));
        Assert.Equal("original completed", run.Result);
        Assert.Equal(original.NextRunAt, run.ScheduledFor);
        Assert.True(await repo.TryAcquireDueLeaseAsync(replacement, replacement.NextRunAt!.Value,
            replacement.NextRunAt.Value, "new-occurrence", replacement.NextRunAt.Value.AddMinutes(15), ct));
    }

    [Fact]
    public async Task Unchanged_SQLite_due_occurrence_advances_once_and_is_not_replayed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var paths = new ScheduledPaths();
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(ct);
        var repo = new AutomationRepository(database);
        var now = DateTimeOffset.UtcNow;
        var original = Definition(AutomationScheduleKind.Daily, enabled: true) with { NextRunAt = now.AddMinutes(-1) };
        await repo.UpsertAsync(original, ct);
        var model = new FakeModelClient("completed");
        var runner = new ScheduledTaskRunner(repo, model, new ScheduledTaskScheduleCalculator(TimeZoneInfo.Utc));

        Assert.Equal(new ScheduledTaskBatchResult(1, 1, 1, 0, 0), await runner.RunDueAsync(now, ct));
        Assert.Equal(new ScheduledTaskBatchResult(0, 0, 0, 0, 0), await runner.RunDueAsync(now, ct));
        Assert.True(Assert.Single(await repo.GetAllAsync(ct)).NextRunAt > now);
        Assert.Equal(1, model.CompleteCount);
        Assert.Single(await repo.GetRunsAsync(original.Id, 5, ct));
    }

    [Fact]
    public async Task Actual_SQLite_due_admission_claims_one_current_occurrence_between_two_adapters()
    {
        var ct = TestContext.Current.CancellationToken;
        using var paths = new ScheduledPaths();
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(ct);
        var left = new AutomationRepository(database);
        var right = new AutomationRepository(database);
        var now = DateTimeOffset.UtcNow;
        var original = Definition(AutomationScheduleKind.Daily, enabled: true) with { NextRunAt = now.AddMinutes(-1) };
        await left.UpsertAsync(original, ct);
        var a = Assert.Single(await left.GetDueAsync(now, ct));
        var b = Assert.Single(await right.GetDueAsync(now, ct));

        var receipts = await Task.WhenAll(
            left.TryAcquireDueLeaseAsync(a, now, now, "left", now.AddMinutes(15), ct),
            right.TryAcquireDueLeaseAsync(b, now, now, "right", now.AddMinutes(15), ct));

        Assert.Single(receipts, claimed => claimed);
    }

    [Fact]
    public async Task Slow_batch_gives_the_claimed_occurrence_a_fresh_lease_without_expanding_the_due_set()
    {
        var ct = TestContext.Current.CancellationToken;
        using var paths = new ScheduledPaths();
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(ct);
        var owner = new AutomationRepository(database);
        var competitor = new AutomationRepository(database);
        // Reuse the same early batch admission bound after 20 minutes would have passed.
        // A separate worker uses its current clock while the first worker is in flight.
        var claimAt = DateTimeOffset.UtcNow;
        var scannedAt = claimAt.AddMinutes(-20);
        var original = Definition(AutomationScheduleKind.Daily, enabled: true) with { NextRunAt = scannedAt.AddMinutes(-1) };
        var becameDue = Definition(AutomationScheduleKind.Daily, enabled: true) with { NextRunAt = scannedAt.AddMinutes(5) };
        await owner.UpsertAsync(original, ct);
        await owner.UpsertAsync(becameDue, ct);
        IReadOnlyList<AutomationDefinition>? competingDue = null;
        bool? duplicateClaim = null;
        var model = new FakeModelClient("ran once")
        {
            BeforeCompletion = async () =>
            {
                competingDue = await competitor.GetDueAsync(claimAt, ct);
                // An earlier observation for this row must not win the SQLite race either.
                duplicateClaim = await competitor.TryAcquireDueLeaseAsync(original, claimAt, claimAt,
                    "competing-worker", claimAt.AddMinutes(15), ct);
            }
        };
        var runner = new ScheduledTaskRunner(owner, model, new ScheduledTaskScheduleCalculator(TimeZoneInfo.Utc));

        Assert.Equal(new ScheduledTaskBatchResult(1, 1, 1, 0, 0), await runner.RunDueAsync(scannedAt, ct));
        Assert.Equal(becameDue.Id, Assert.Single(competingDue!).Id);
        Assert.False(duplicateClaim);
        Assert.Equal(1, model.CompleteCount);
        Assert.Equal(original.NextRunAt, Assert.Single(await owner.GetRunsAsync(original.Id, 5, ct)).ScheduledFor);
        Assert.Empty(await owner.GetRunsAsync(becameDue.Id, 5, ct));
    }

    [Fact]
    public async Task Atomic_claim_refuses_an_already_expired_lease_or_an_occurrence_now_in_the_future()
    {
        var ct = TestContext.Current.CancellationToken;
        using var paths = new ScheduledPaths();
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(ct);
        var repository = new AutomationRepository(database);
        var scanned = DateTimeOffset.UtcNow;
        var scheduledFor = scanned.AddMinutes(-1);
        var task = Definition(AutomationScheduleKind.Daily, enabled: true) with { NextRunAt = scheduledFor };
        await repository.UpsertAsync(task, ct);

        Assert.False(await repository.TryAcquireDueLeaseAsync(task, scanned, scanned.AddMinutes(20),
            "expired", scanned.AddMinutes(15), ct));
        Assert.False(await repository.TryAcquireDueLeaseAsync(task, scanned, scheduledFor.AddSeconds(-1),
            "clock-rolled-back", scanned.AddMinutes(15), ct));
        Assert.Equal(task, Assert.Single(await repository.GetDueAsync(scanned, ct)));
        Assert.Empty(await repository.GetRunsAsync(task.Id, 5, ct));
    }

    private sealed class ScheduledPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("9to1-due-automation-").FullName;
        public string DatabasePath => Path.Combine(DataDirectory, "store.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, recursive: true);
    }

    private sealed class DueReadBarrier(AutomationRepository actual, Func<Task> afterRead)
        : IAutomationRepository, IAutomationDueRunOwner
    {
        public Task<IReadOnlyList<AutomationDefinition>> GetAllAsync(CancellationToken token) => actual.GetAllAsync(token);
        public async Task<IReadOnlyList<AutomationDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken token)
        {
            var observed = await actual.GetDueAsync(now, token);
            await afterRead();
            return observed;
        }
        public Task UpsertAsync(AutomationDefinition definition, CancellationToken token) => actual.UpsertAsync(definition, token);
        public Task DeleteAsync(Guid id, CancellationToken token) => actual.DeleteAsync(id, token);
        public Task<bool> TryAcquireLeaseAsync(Guid id, string leaseToken, DateTimeOffset expires, CancellationToken token) =>
            actual.TryAcquireLeaseAsync(id, leaseToken, expires, token);
        public Task<bool> TryAcquireDueLeaseAsync(AutomationDefinition observed, DateTimeOffset dueAt, DateTimeOffset claimingAt, string leaseToken,
            DateTimeOffset expires, CancellationToken token) => actual.TryAcquireDueLeaseAsync(observed, dueAt, claimingAt, leaseToken, expires, token);
        public Task CompleteRunAsync(AutomationRun run, DateTimeOffset? next, CancellationToken token) => actual.CompleteRunAsync(run, next, token);
        public Task CompleteDueRunAsync(AutomationDefinition observed, AutomationRun run, DateTimeOffset? next, CancellationToken token) =>
            actual.CompleteDueRunAsync(observed, run, next, token);
        public Task<IReadOnlyList<AutomationRun>> GetRunsAsync(Guid id, int count, CancellationToken token) => actual.GetRunsAsync(id, count, token);
    }

    private static AutomationDefinition Definition(AutomationScheduleKind kind, bool enabled)
    {
        var now = DateTimeOffset.UtcNow;
        var schedule = ScheduledTaskScheduleComposer.Compose(
            kind,
            new ScheduledTaskScheduleDraft(now.AddHours(1), new TimeOnly(8, 0), DayOfWeek.Monday, 1, 60));
        return new AutomationDefinition(
            Guid.NewGuid(),
            "Release watch",
            HavenMode.Chat,
            "Check whether the release is available.",
            kind,
            schedule,
            enabled ? now.AddHours(1) : null,
            null,
            enabled,
            now,
            now);
    }

    private sealed class FakeModelClient(string response) : IOllamaClient
    {
        public Exception? Failure { get; init; }
        public Func<Task>? BeforeCompletion { get; init; }
        public int CompleteCount { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([new ModelDescriptor(
                "qwen-test", 1, "qwen", "test", "test",
                new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow)]);

        public async IAsyncEnumerable<string> StreamChatAsync(
            OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return response;
        }

        public async Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            CompleteCount++;
            if (BeforeCompletion is not null) await BeforeCompletion();
            if (Failure is not null) throw Failure;
            return response;
        }

        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(response, []));
    }

    private sealed class MemoryAutomationRepository : IAutomationRepository
    {
        private readonly HashSet<Guid> _leases = [];
        public Dictionary<Guid, AutomationDefinition> Definitions { get; } = [];
        public List<AutomationRun> Runs { get; } = [];

        public Task<IReadOnlyList<AutomationDefinition>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AutomationDefinition>>(Definitions.Values.ToArray());

        public Task<IReadOnlyList<AutomationDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AutomationDefinition>>(Definitions.Values
                .Where(item => item.IsEnabled && item.NextRunAt <= now)
                .ToArray());

        public Task UpsertAsync(AutomationDefinition automation, CancellationToken cancellationToken)
        {
            Definitions[automation.Id] = automation;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            Definitions.Remove(id);
            return Task.CompletedTask;
        }

        public Task<bool> TryAcquireLeaseAsync(Guid automationId, string leaseToken, DateTimeOffset leaseUntil, CancellationToken cancellationToken) =>
            Task.FromResult(_leases.Add(automationId));

        public Task CompleteRunAsync(AutomationRun run, DateTimeOffset? nextRunAt, CancellationToken cancellationToken)
        {
            Runs.Add(run);
            _leases.Remove(run.AutomationId);
            if (Definitions.TryGetValue(run.AutomationId, out var definition))
                Definitions[run.AutomationId] = definition with { NextRunAt = nextRunAt };
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AutomationRun>> GetRunsAsync(Guid automationId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AutomationRun>>(Runs.Where(item => item.AutomationId == automationId).Take(limit).ToArray());
    }
}
