using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;

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
    public async Task SuccessPathPersistsSingleStableRunAndReturnsIt()
    {
        var repository = new MemoryAutomationRepository();
        var model = new FakeModelClient("ok result");
        var runner = new ScheduledTaskRunner(repository, model, new ScheduledTaskScheduleCalculator());
        var task = Definition(AutomationScheduleKind.Daily, enabled: true);

        var run = await runner.RunOneAsync(task, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(AutomationRunStatus.Succeeded, run.Status);
        Assert.Single(repository.Runs);
        Assert.Equal(run.Id, repository.Runs[0].Id);
        Assert.Equal(1, model.CompleteCount);
    }

    [Fact]
    public async Task CompletionPersistenceFaultUsesOneStableRunIdentityAndNeverCreatesAFailedRow()
    {
        var repository = new MemoryAutomationRepository();
        var controlled = new AckLossAutomationRepository(repository) { FailOnComplete = true };
        var model = new FakeModelClient("ok result");
        var runner = new ScheduledTaskRunner(controlled, model, new ScheduledTaskScheduleCalculator());
        var task = Definition(AutomationScheduleKind.Daily, enabled: true);

        var ex = await Assert.ThrowsAsync<ScheduledTaskCompletionUncertaintyException>(
            () => runner.RunOneAsync(task, DateTimeOffset.UtcNow, CancellationToken.None));

        // Execution ran once and the exact success payload is retained, never relabelled Failed/Cancelled.
        Assert.Equal(1, model.CompleteCount);
        Assert.Equal(AutomationRunStatus.Succeeded, ex.Draft.Run.Status);
        Assert.Single(repository.Runs);
        Assert.Equal(ex.Draft.Run.Id, repository.Runs[0].Id);
        Assert.Equal(AutomationRunStatus.Succeeded, repository.Runs[0].Status);

        // Reconciliation of the exact already-committed payload is a confirmed no-write ACK and never
        // re-executes the model.
        var reconciliation = await runner.ReconcileCompletionAsync(ex.Draft, CancellationToken.None);
        Assert.Equal(ScheduledTaskCompletionReconciliationKind.ConfirmedNoWrite, reconciliation.Kind);
        Assert.Equal(1, model.CompleteCount);
        Assert.Single(repository.Runs);
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

        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            CompleteCount++;
            return Failure is null ? Task.FromResult(response) : Task.FromException<string>(Failure);
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

    /// <summary>Completes the durable write through the real memory repository, then throws a distinct
    /// ACK-loss exception — the exact lost-acknowledgement-after-commit simulation. It never fabricates
    /// a failed/duplicate terminal row.</summary>
    private sealed class AckLossAutomationRepository(MemoryAutomationRepository inner) : IAutomationRepository
    {
        public bool FailOnComplete { get; init; }

        public async Task CompleteRunAsync(AutomationRun run, DateTimeOffset? nextRunAt, CancellationToken cancellationToken)
        {
            await inner.CompleteRunAsync(run, nextRunAt, cancellationToken);
            if (FailOnComplete) throw new IOException("Simulated ACK loss after durable commit.");
        }

        public Task<IReadOnlyList<AutomationDefinition>> GetAllAsync(CancellationToken cancellationToken) => inner.GetAllAsync(cancellationToken);
        public Task<IReadOnlyList<AutomationDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken) => inner.GetDueAsync(now, cancellationToken);
        public Task UpsertAsync(AutomationDefinition automation, CancellationToken cancellationToken) => inner.UpsertAsync(automation, cancellationToken);
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => inner.DeleteAsync(id, cancellationToken);
        public Task<bool> TryAcquireLeaseAsync(Guid automationId, string leaseToken, DateTimeOffset leaseUntil, CancellationToken cancellationToken) =>
            inner.TryAcquireLeaseAsync(automationId, leaseToken, leaseUntil, cancellationToken);
        public Task<IReadOnlyList<AutomationRun>> GetRunsAsync(Guid automationId, int limit, CancellationToken cancellationToken) =>
            inner.GetRunsAsync(automationId, limit, cancellationToken);
    }
}
