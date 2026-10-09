using HavenOS.Apps.Assistants.Migration;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

/// <summary>Local review observations only. Actual Home approval and SQLite import
/// are exercised by the genuine migration owner controls.</summary>
public sealed class LegacyAgentImportBindingsTests
{
    [Fact]
    public async Task Pending_import_never_exposes_content_or_confirmation_and_dispatches_only_the_current_review()
    {
        AssistantsLegacyMigrationCuiBindings? bindings = null;
        LegacyAgentImportPreview? delivered = null;
        string? command = null;
        bindings = new(true, "", (actual, _, _) =>
        { command = actual; delivered = bindings!.OriginalImportPreview; return ValueTask.CompletedTask; }, action => action(), () => true);
        var store = Guid.NewGuid();
        var review = Preview(store, LegacyAgentImportState.RequiresReview);
        bindings.SetImportPreview(review);
        Assert.True(bindings.IsActionAvailable("assistants.legacy.import.request"));
        Assert.False(bindings.IsActionAvailable("assistants.legacy.import.complete"));
        Assert.False(bindings.IsActionAvailable("assistants.legacy.preview"));
        await bindings.DispatchAsync("assistants.legacy.import.request", null, CancellationToken.None);
        Assert.Equal("assistants.legacy.import.request", command); Assert.Same(review, delivered);
        var pending = Preview(store, LegacyAgentImportState.PendingApproval, "Actual source-issued request observation");
        bindings.SetImportPreview(pending);
        Assert.Same(pending, bindings.OriginalImportPreview);
        Assert.False(bindings.IsActionAvailable("assistants.legacy.import.request"));
        Assert.False(bindings.IsActionAvailable("assistants.legacy.import.complete"));
        Assert.True(bindings.IsActionAvailable("assistants.legacy.import.refresh"));
        Assert.True(bindings.TryGetValue("ShowMigrationList", out var shown)); Assert.Equal(false, shown);
        Assert.False(bindings.HasUnconfirmedChanges); Assert.True(bindings.IsActionAvailable("assistants.legacy.back"));
        bindings.SetImportPreview(Preview(store, LegacyAgentImportState.Approved, pending.RequestId));
        Assert.True(bindings.IsActionAvailable("assistants.legacy.import.complete"));
        Assert.False(bindings.IsActionAvailable("assistants.legacy.preview"));
        bindings.SetImportPreview(Preview(store, LegacyAgentImportState.Imported));
        Assert.True(bindings.TryGetValue("ShowMigrationList", out shown)); Assert.Equal(true, shown);
        Assert.True(bindings.IsActionAvailable("assistants.legacy.preview"));
        Assert.False(bindings.IsActionAvailable("assistants.legacy.import.complete"));
    }

    [Fact]
    public void New_unowned_observation_revokes_old_rows_and_unknown_outcome_never_becomes_a_retry_or_approval()
    {
        var bindings = new AssistantsLegacyMigrationCuiBindings(true, "", (_, _, _) => ValueTask.CompletedTask,
            action => action(), () => true);
        var store = Guid.NewGuid(); bindings.SetImportPreview(Preview(store, LegacyAgentImportState.Imported));
        bindings.SetPage(new([new(Guid.NewGuid(), "Existing original", "Preserved", true, false, LegacyAgentMigrationState.Unselected)], null));
        Assert.True(bindings.TryGetValue("MigrationRows", out var observed));
        var row = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<AssistantsLegacyMigrationCuiBindings.LegacyRow>>(observed));
        bindings.SetPreview(LegacyAgentMigrationDraftTests.Preview());
        bindings.SetImportPreview(Preview(store, LegacyAgentImportState.RequiresReview));
        Assert.Null(bindings.OriginalDraft); Assert.False(bindings.TryGetItemValue(row, "Name", out _));
        Assert.False(bindings.IsActionAvailable("assistants.legacy.confirm"));
        bindings.SetImportPreview(Preview(store, LegacyAgentImportState.Declined));
        Assert.True(bindings.IsActionAvailable("assistants.legacy.import.request"));
        bindings.SetImportPreview(Preview(store, LegacyAgentImportState.AuditPending, "Same original audit request"));
        Assert.True(bindings.IsActionAvailable("assistants.legacy.import.audit"));
        Assert.True(bindings.HasUnconfirmedChanges); Assert.False(bindings.IsActionAvailable("assistants.legacy.back"));
        Assert.False(bindings.IsActionAvailable("assistants.legacy.import.request"));
        bindings.SetImportPreview(Preview(store, LegacyAgentImportState.OutcomeUnconfirmed));
        Assert.True(bindings.HasUnconfirmedChanges);
        foreach (var command in new[] { "request", "complete", "audit", "refresh" })
            Assert.False(bindings.IsActionAvailable("assistants.legacy.import." + command));
        Assert.False(bindings.IsActionAvailable("assistants.legacy.back"));
        bindings.Revoke(); Assert.False(bindings.IsActionAvailable("assistants.legacy.import.inspect"));
    }

    private static LegacyAgentImportPreview Preview(Guid store, LegacyAgentImportState state, string? request = null) =>
        new(new object(), new object(), store, state, "Display-only fixture; no Home authority", request);
}
