using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperSavedTestEvidenceTests
{
    [Fact]
    public void ReportPreservesProviderOutcomesAndNeverClaimsCurrentSourcePassed()
    {
        var id = Guid.NewGuid();
        var samples = new[] { "Passed", "Failed", "NotExecuted", "Aborted", "NotRunnable" };
        var rows = samples.Select(Outcome).ToArray();
        var evidence = DeveloperSavedTestEvidence.FromReport(new(id, "Run", "Completed", null, null, rows));
        Assert.Contains(id.ToString("D"), evidence.Summary);
        Assert.Contains("not a verification of the current command or source revision", evidence.Summary);
        foreach (var state in samples)
        {
            Assert.Contains(state + ": 1", evidence.Summary);
            Assert.Contains(state + "  Example", evidence.Details);
        }
    }

    [Fact]
    public void ReportProjectionBoundsVisualRowsAndRetainsProviderCounts()
    {
        var rows = Enumerable.Repeat(Outcome("Passed"), 201).ToArray();
        var evidence = DeveloperSavedTestEvidence.FromReport(new(Guid.NewGuid(), "Run", "Passed", null, null, rows));
        Assert.Contains("Passed: 201", evidence.Summary);
        Assert.Contains("first 200 of 201", evidence.Details);
        Assert.Equal(201, evidence.Details.Split(Environment.NewLine).Length);
    }

    private static DeveloperRecordedTest Outcome(string outcome) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Example", outcome, DeveloperRecordedTestOutcome.Unknown,
            null, null, null, null, "", "", "", "", false);
}
