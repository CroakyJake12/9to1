using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

/// <summary>ED-BUILD-003: test outcomes distinguish passed, failed, skipped, unavailable and cancelled.
/// Proves each category is tallied distinctly, unavailable/cancelled are not counted as pass/fail, and the overall run state reflects real receipts.</summary>
public sealed class DeveloperTestOutcomeTests
{
    private static DeveloperTestOutcome Outcome(DeveloperTestOutcomeState state, string? reason = null) =>
        new(Guid.NewGuid(), "Test." + state, state, state is DeveloperTestOutcomeState.Passed or DeveloperTestOutcomeState.Failed ? TimeSpan.FromMilliseconds(5) : null, reason);

    [Fact]
    public void EachCategoryIsTalliedSeparately()
    {
        var summary = DeveloperTestRunSummary.From([
            Outcome(DeveloperTestOutcomeState.Passed),
            Outcome(DeveloperTestOutcomeState.Failed, "assert"),
            Outcome(DeveloperTestOutcomeState.Skipped, "not applicable"),
            Outcome(DeveloperTestOutcomeState.Unavailable, "no runtime"),
            Outcome(DeveloperTestOutcomeState.Cancelled, "stopped")]);

        Assert.Equal(1, summary.PassedCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.Equal(1, summary.SkippedCount);
        Assert.Equal(1, summary.UnavailableCount);
        Assert.Equal(1, summary.CancelledCount);
    }

    [Fact]
    public void UnavailableAndCancelledAreNotCountedAsPassOrFail()
    {
        var summary = DeveloperTestRunSummary.From([
            Outcome(DeveloperTestOutcomeState.Passed),
            Outcome(DeveloperTestOutcomeState.Unavailable, "no runtime"),
            Outcome(DeveloperTestOutcomeState.Cancelled, "stopped")]);

        Assert.Equal(1, summary.PassedCount);
        Assert.Equal(0, summary.FailedCount);
        // The run is not a clean pass: it is incomplete because of the unavailable test.
        Assert.Equal(DeveloperTestRunState.Incomplete, summary.Overall);
    }

    [Fact]
    public void AnyFailureFailsTheRun()
    {
        var summary = DeveloperTestRunSummary.From([Outcome(DeveloperTestOutcomeState.Passed), Outcome(DeveloperTestOutcomeState.Failed, "assert")]);
        Assert.Equal(DeveloperTestRunState.Failed, summary.Overall);
    }

    [Fact]
    public void CancellationTakesPrecedenceOverIncomplete()
    {
        var summary = DeveloperTestRunSummary.From([Outcome(DeveloperTestOutcomeState.Passed), Outcome(DeveloperTestOutcomeState.Cancelled, "stopped")]);
        Assert.Equal(DeveloperTestRunState.Cancelled, summary.Overall);
    }

    [Fact]
    public void AllPassedIsPassed()
    {
        var summary = DeveloperTestRunSummary.From([Outcome(DeveloperTestOutcomeState.Passed), Outcome(DeveloperTestOutcomeState.Passed)]);
        Assert.Equal(DeveloperTestRunState.Passed, summary.Overall);
    }

    [Fact]
    public void EmptyRunIsNotRunNeverPassed()
    {
        var summary = DeveloperTestRunSummary.From([]);
        Assert.Equal(DeveloperTestRunState.NotRun, summary.Overall);
        Assert.Equal(0, summary.PassedCount);
    }

    [Fact]
    public void NonPassingOutcomeRequiresReason()
    {
        Assert.Throws<InvalidDataException>(() => new DeveloperTestOutcome(Guid.NewGuid(), "t", DeveloperTestOutcomeState.Failed, null, null).Capture());
        Assert.Throws<InvalidDataException>(() => new DeveloperTestOutcome(Guid.Empty, "t", DeveloperTestOutcomeState.Passed, null, null).Capture());
    }

    [Fact]
    public void DuplicateTestIdIsRejected()
    {
        var id = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => DeveloperTestRunSummary.From([
            new DeveloperTestOutcome(id, "a", DeveloperTestOutcomeState.Passed, null, null),
            new DeveloperTestOutcome(id, "b", DeveloperTestOutcomeState.Failed, null, "x")]));
    }
}
