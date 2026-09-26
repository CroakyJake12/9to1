namespace Dulche.Runtime.Agents.Tests;

public sealed class AgentExecutionContractTests
{
    [Fact]
    public void ChildReservationsAreChargedAgainstEveryAncestorAndSettlementIsIdempotent()
    {
        var ledger = new AgentBudgetLedger();
        Assert.True(ledger.RegisterRoot("root", new(MaxTokens: 100)).Succeeded);
        Assert.True(ledger.RegisterChild("root", "child", new(MaxTokens: 80)).Succeeded);

        var first = ledger.Reserve("child", "call-1", new(Tokens: 70), DateTimeOffset.UnixEpoch);
        Assert.True(first.Succeeded);
        var second = ledger.Reserve("child", "call-2", new(Tokens: 31), DateTimeOffset.UnixEpoch);
        Assert.Equal(AgentFailureCode.BudgetExceeded, second.Error?.Code);

        var settlement = new BudgetSettlement(
            UsageValue<long>.Measured(60), UsageValue<TimeSpan>.Empty(), UsageValue<long>.Measured(1),
            UsageValue<long>.Measured(1), UsageValue<decimal>.Empty());
        var settled = ledger.Settle("child", "call-1", settlement, DateTimeOffset.UnixEpoch.AddSeconds(1));
        var repeated = ledger.Settle("child", "call-1", settlement, DateTimeOffset.UnixEpoch.AddSeconds(2));

        Assert.True(settled.Succeeded);
        Assert.Equal(settled.Value, repeated.Value);
        Assert.Equal(60, ledger.Snapshot("root").Value!.Tokens.Value);
        Assert.Equal(AgentFailureCode.IdempotencyMismatch,
            ledger.Settle("child", "call-1", settlement with { Tokens = UsageValue<long>.Measured(61) }, DateTimeOffset.UnixEpoch).Error?.Code);
    }

    [Fact]
    public void UnknownProviderUsageRemainsUnavailableAndDoesNotBecomeZero()
    {
        var ledger = new AgentBudgetLedger();
        ledger.RegisterRoot("root", new(MaxTokens: 100));
        ledger.Reserve("root", "call-1", new(Tokens: 40), DateTimeOffset.UnixEpoch);

        var settled = ledger.Settle("root", "call-1", new(
            UsageValue<long>.Unavailable("provider omitted usage"), UsageValue<TimeSpan>.Empty(),
            UsageValue<long>.Measured(1), UsageValue<long>.Measured(1), UsageValue<decimal>.Empty()), DateTimeOffset.UnixEpoch);

        Assert.True(settled.Succeeded);
        var usage = ledger.Snapshot("root").Value!;
        Assert.Equal(UsageAvailability.ProviderUnavailable, usage.Tokens.Availability);
        Assert.Null(usage.Tokens.Value);
        Assert.Equal(40, usage.KnownTokenSubtotal);
    }

    [Fact]
    public void ApprovalAndBlockerStatesStayDistinctAndCompletedProgressIsExact()
    {
        var queued = CreateRun(AgentRunState.Queued);
        var waitingApproval = AgentRunStateMachine.Transition(queued, AgentRunState.AwaitingApproval, DateTimeOffset.UnixEpoch);
        Assert.True(waitingApproval.Succeeded);
        Assert.Equal(AgentRunState.AwaitingApproval, waitingApproval.Value!.State);
        Assert.Equal(AgentRunState.Paused, AgentRunStateMachine.Transition(waitingApproval.Value, AgentRunState.Paused, DateTimeOffset.UnixEpoch).Value!.State);
        Assert.Equal(AgentFailureCode.RunNotResumable,
            AgentRunStateMachine.Transition(queued, AgentRunState.Completed, DateTimeOffset.UnixEpoch).Error?.Code);
    }

    [Fact]
    public void QueueReorderRejectsDuplicateSequencesAndSchedulerHonorsDependenciesThenPriority()
    {
        var first = CreateQueueItem("first", 1, 1, []);
        var second = CreateQueueItem("second", 10, 2, ["first"]);
        var third = CreateQueueItem("third", 5, 3, []);

        Assert.Equal(AgentFailureCode.InvalidInvocationContext,
            AgentQueueOrdering.Reorder([first, third], [new("first", 2), new("third", 2)], "agent", DateTimeOffset.UnixEpoch).Error?.Code);
        Assert.Equal("third", AgentQueueOrdering.SelectReady([second, third],
            new Dictionary<string, AgentQueueState> { ["first"] = AgentQueueState.Queued }, "agent").Value!.QueueItemId);
        Assert.Equal("second", AgentQueueOrdering.SelectReady([second, third],
            new Dictionary<string, AgentQueueState> { ["first"] = AgentQueueState.Completed }, "agent").Value!.QueueItemId);
    }

    private static AgentRunSnapshot CreateRun(AgentRunState state)
    {
        var now = DateTimeOffset.UnixEpoch;
        var attempt = new AgentAttemptSnapshot("attempt", 1, null, "session", "endpoint", "model", "provider", "new", [], now,
            null, null, state, AgentBudgetUsage.Empty);
        return new("run", "agent", 1, "objective", AgentTriggerKind.User, "trigger", null, null, null, null, null, [],
            state, "caller", "surface", null, null, "session", "endpoint", null, "model", "provider", new HashSet<string>(),
            [], new(new HashSet<string>(), new HashSet<string>(), false, 1, 1, new()), new(), AgentBudgetUsage.Empty,
            [], [], [], [], null, null, now, null, null, null, 0, "attempt", [attempt]);
    }

    private static AgentQueueItem CreateQueueItem(string id, int priority, long sequence, IReadOnlyList<string> dependencies) =>
        new(id, "agent", id, "caller", AgentTriggerKind.User, id, null, priority, sequence, dependencies,
            AgentQueueState.Queued, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null, null, 0);
}
