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

    [Fact]
    public void FailuresBeyondThePreviewLimitRemainVisibleWithBoundedRecordedReasons()
    {
        var passed = Enumerable.Repeat(Outcome("Passed") with
        {
            Outcome = DeveloperRecordedTestOutcome.Passed,
            Message = "do not display passing provider chatter"
        }, 240);
        var firstFailure = Outcome("Failed") with
        {
            Name = "Late failing test",
            Outcome = DeveloperRecordedTestOutcome.Failed,
            Message = "Expected the saved revision\nDo not dump the rest",
            StandardError = "do not copy process output",
            StackTrace = "do not copy the full stack trace"
        };
        var secondFailure = Outcome("Error") with
        {
            Name = "Late errored test",
            Outcome = DeveloperRecordedTestOutcome.Error,
            Message = "Error: " + new string('x', 1000)
        };
        var rows = passed.Append(firstFailure).Append(secondFailure).ToArray();

        var evidence = DeveloperSavedTestEvidence.FromReport(new(Guid.NewGuid(), "Run", "Failed", null, null, rows));

        var lines = evidence.Details.Split(Environment.NewLine);
        Assert.StartsWith("Failed  Late failing test", lines[0]);
        Assert.Contains("Expected the saved revision", lines[0]);
        Assert.StartsWith("Error  Late errored test", lines[1]);
        Assert.True(lines[1].Length < 430);
        Assert.EndsWith("…", lines[1]);
        Assert.DoesNotContain("Do not dump", evidence.Details);
        Assert.DoesNotContain("do not copy", evidence.Details);
        Assert.DoesNotContain("do not display", evidence.Details);
        Assert.Contains("Passed: 240", evidence.Summary);
        Assert.Contains("first 200 of 242", evidence.Details);
        Assert.Equal(201, lines.Length);
    }

    private static DeveloperRecordedTest Outcome(string outcome) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Example", outcome, DeveloperRecordedTestOutcome.Unknown,
            null, null, null, null, "", "", "", "", false);
}
