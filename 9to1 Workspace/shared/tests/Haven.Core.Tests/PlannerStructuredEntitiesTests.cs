using Haven.Core;

namespace Haven.Core.Tests;

public sealed class PlannerStructuredEntitiesTests
{
    [Fact]
    public void Assignment_progress_uses_required_items_and_explicit_weights_only()
    {
        var assignment = Assignment([
            Item(PlannerAssignmentItemKind.Required, complete: true, weight: 3m),
            Item(PlannerAssignmentItemKind.Required, complete: false, weight: 1m),
            Item(PlannerAssignmentItemKind.Optional, complete: false, weight: null)
        ]);

        Assert.Equal(0.75m, assignment.GetProgress());
        Assert.False(assignment.CanComplete);
    }

    [Fact]
    public void Assignment_completion_requires_an_explicit_override_for_incomplete_required_items()
    {
        var assignment = Assignment([Item(PlannerAssignmentItemKind.Required, complete: false, weight: null)]);
        var at = DateTimeOffset.Parse("2026-09-26T14:30:00Z");

        Assert.Throws<InvalidOperationException>(() => assignment.Complete(at));
        var completed = assignment.Complete(at, overrideIncompleteRequiredItems: true);

        Assert.Equal(PlannerAssignmentStatus.Completed, completed.Status);
        Assert.Equal(at, completed.CompletedAt);
        Assert.Equal(assignment.Revision + 1, completed.Revision);
    }

    [Fact]
    public void Assignment_rejects_partial_weighting_and_out_of_range_manual_progress()
    {
        var partiallyWeighted = Assignment([
            Item(PlannerAssignmentItemKind.Required, complete: false, weight: 2m),
            Item(PlannerAssignmentItemKind.Required, complete: false, weight: null)
        ]);
        var badManualProgress = Assignment([], manualProgress: 1.5m);

        Assert.Throws<InvalidOperationException>(() => partiallyWeighted.GetProgress());
        Assert.Throws<InvalidOperationException>(() => badManualProgress.GetProgress());
    }

    [Fact]
    public void Schedule_progress_reports_live_elapsed_and_remaining_fractions()
    {
        var start = DateTimeOffset.Parse("2026-09-26T09:00:00Z");
        var first = ScheduleItem("Lecture", start, start.AddHours(1));
        var second = ScheduleItem("Study", start.AddHours(1), start.AddHours(3));

        var progress = PlannerScheduleProgressCalculator.Calculate([second, first], start.AddMinutes(90));

        Assert.Equal("Study", progress.Current!.Title);
        Assert.Equal("Lecture", progress.Previous!.Title);
        Assert.Equal(TimeSpan.FromMinutes(30), progress.Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(90), progress.Remaining);
        Assert.Equal(0.25m, progress.PercentageElapsed);
        Assert.Equal(0.75m, progress.PercentageRemaining);
    }

    [Fact]
    public void Linked_countdown_uses_the_canonical_target_and_all_day_dates_remain_dates()
    {
        var countdown = new PlannerCountdownEntity(
            Guid.NewGuid(), "Project due", PlannerCountdownDateKind.DateTime,
            new DateOnly(2030, 1, 1), DateTimeOffset.Parse("2030-01-01T09:00:00Z"), "UTC",
            null, null, null, null, [], new("planner", "Assignment", "assignment-1"), 1,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var canonical = new PlannerCountdownTarget(PlannerCountdownDateKind.Date, new DateOnly(2030, 2, 4), null, "Europe/London");

        var resolved = PlannerCountdownTargetResolver.Resolve(countdown, _ => canonical);

        Assert.Equal(canonical, resolved);
        Assert.Equal(new DateOnly(2030, 2, 4), resolved!.Date);
        Assert.Null(resolved.DateTime);
    }

    private static PlannerAssignment Assignment(IReadOnlyList<PlannerAssignmentItem> items, decimal? manualProgress = null)
    {
        var now = DateTimeOffset.Parse("2026-09-26T12:00:00Z");
        return new(Guid.NewGuid(), "Assignment", null, PlannerAssignmentStatus.InProgress, null, null, null,
            PlannerPriority.Medium, null, "owner", "personal", [], items, [], [], [], 4, now, now, manualProgress);
    }

    private static PlannerAssignmentItem Item(PlannerAssignmentItemKind kind, bool complete, decimal? weight)
    {
        var now = DateTimeOffset.Parse("2026-09-26T12:00:00Z");
        return new(Guid.NewGuid(), Guid.NewGuid(), null, "Item", null, kind, complete, weight, null, 1, now);
    }

    private static PlannerScheduleItem ScheduleItem(string title, DateTimeOffset start, DateTimeOffset end) =>
        new(Guid.NewGuid(), title, start, end, null, null, null, []);
}
