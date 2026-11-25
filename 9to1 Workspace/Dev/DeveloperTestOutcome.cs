namespace HavenOS.Apps.Dev;

/// <summary>The five distinct live-run test outcomes. Unavailable and Cancelled are never folded into Passed or Failed.</summary>
public enum DeveloperTestOutcomeState { Passed, Failed, Skipped, Unavailable, Cancelled }

/// <summary>An immutable per-test result from a live run. A reason is required for non-passing states so the UI reports evidence rather than a blank.
/// Duration is optional because unavailable/cancelled tests may never have executed.</summary>
public sealed record DeveloperTestOutcome(Guid TestId, string Name, DeveloperTestOutcomeState State, TimeSpan? Duration, string? Reason)
{
    public DeveloperTestOutcome Capture()
    {
        if (TestId == Guid.Empty) throw new InvalidDataException("A test outcome requires a test identity.");
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("A test outcome requires a name.");
        if (!Enum.IsDefined(State)) throw new InvalidDataException("The test outcome state is unsupported.");
        if (State != DeveloperTestOutcomeState.Passed && string.IsNullOrWhiteSpace(Reason))
            throw new InvalidDataException("A non-passing test outcome must state why.");
        if (Duration is { } duration && duration < TimeSpan.Zero) throw new InvalidDataException("A test duration may not be negative.");
        return this;
    }
}

/// <summary>The overall run state, derived only from actual receipts. A run with no executed tests is NotRun, never Passed.</summary>
public enum DeveloperTestRunState { NotRun, Passed, Failed, Cancelled, Incomplete }

/// <summary>An aggregate of live test outcomes that tallies each category separately and reports the run's overall state strictly from receipts.
/// Unavailable/Cancelled/Skipped are counted distinctly and never inflate passed or failed totals.</summary>
public sealed record DeveloperTestRunSummary(IReadOnlyList<DeveloperTestOutcome> Outcomes)
{
    public const int MaximumOutcomes = 100_000;

    public DeveloperTestRunSummary Capture()
    {
        var outcomes = (Outcomes ?? []).Select(o => o?.Capture() ?? throw new InvalidDataException("A test outcome is missing.")).ToArray();
        if (outcomes.Length > MaximumOutcomes) throw new InvalidDataException("The test run exceeds the configured outcome limit.");
        if (outcomes.Select(o => o.TestId).Distinct().Count() != outcomes.Length) throw new InvalidOperationException("Duplicate test identity in a run summary.");
        return this with { Outcomes = Array.AsReadOnly(outcomes) };
    }

    public static DeveloperTestRunSummary From(IEnumerable<DeveloperTestOutcome> outcomes) => new((outcomes ?? []).ToArray()).Capture();

    private int Count(DeveloperTestOutcomeState state) => Capture().Outcomes.Count(o => o.State == state);
    public int PassedCount => Count(DeveloperTestOutcomeState.Passed);
    public int FailedCount => Count(DeveloperTestOutcomeState.Failed);
    public int SkippedCount => Count(DeveloperTestOutcomeState.Skipped);
    public int UnavailableCount => Count(DeveloperTestOutcomeState.Unavailable);
    public int CancelledCount => Count(DeveloperTestOutcomeState.Cancelled);

    /// <summary>Overall run state from receipts: any failure fails the run; otherwise cancellation, then incomplete (unavailable/skipped present),
/// else passed only when at least one test actually executed. An empty run is NotRun.</summary>
    public DeveloperTestRunState Overall
    {
        get
        {
            var captured = Capture();
            if (captured.Outcomes.Count == 0) return DeveloperTestRunState.NotRun;
            if (FailedCount > 0) return DeveloperTestRunState.Failed;
            if (CancelledCount > 0) return DeveloperTestRunState.Cancelled;
            if (UnavailableCount > 0 || SkippedCount > 0) return DeveloperTestRunState.Incomplete;
            return PassedCount > 0 ? DeveloperTestRunState.Passed : DeveloperTestRunState.NotRun;
        }
    }
}
