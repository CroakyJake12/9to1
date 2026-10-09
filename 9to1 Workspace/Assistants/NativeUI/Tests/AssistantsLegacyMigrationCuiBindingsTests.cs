using HavenOS.Apps.Assistants.Contracts;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

public sealed class AssistantsLegacyMigrationCuiBindingsTests
{
    [Fact]
    public async Task Missing_source_shows_setup_and_refuses_every_business_action_then_revoke_hides_values()
    {
        var calls = 0;
        var bindings = new AssistantsLegacyMigrationCuiBindings(false, "Import through Home first.",
            (_, _, _) => { calls++; return ValueTask.CompletedTask; }, action => action(), () => true);
        Assert.True(bindings.TryGetValue("MigrationStatus", out var status)); Assert.Equal("Import through Home first.", status);
        foreach (var command in new[] { "assistants.legacy.list", "assistants.legacy.preview", "assistants.legacy.kind.assistant",
            "assistants.legacy.kind.specialist", "assistants.legacy.confirm", "assistants.legacy.recovery", "assistants.legacy.undo" })
        {
            Assert.False(bindings.IsActionAvailable(command));
            await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync(command, null).AsTask());
        }
        Assert.Equal(0, calls); Assert.False(bindings.TrySetValue("MigrationName", "Foreign write"));
        bindings.Revoke(); Assert.True(bindings.TryGetValue("MigrationStatus", out status)); Assert.Null(status);
        Assert.True(bindings.TryGetValue("CanMigrationConfirm", out var canConfirm)); Assert.Equal(false, canConfirm);
    }

    [Fact]
    public void Reopened_classification_uses_the_same_owner_returned_destination_for_recovery()
    {
        var definition = new AssistantDefinitionSnapshot(new("actual-fixture-den", "personal", "stable-fixture-identity"),
            3, ConfiguredIdentityKind.Specialist, new() { Name = "Saved Specialist" }, []);
        var preview = LegacyAgentMigrationDraftTests.Preview(classified: definition);
        var bindings = new AssistantsLegacyMigrationCuiBindings(true, "", (_, _, _) => ValueTask.CompletedTask,
            action => action(), () => true);
        bindings.SetPreview(preview);
        Assert.Same(definition, bindings.OriginalClassifiedDefinition);
        Assert.Same(preview, bindings.OriginalDraft!.Preview);
        Assert.True(bindings.IsActionAvailable("assistants.legacy.recovery"));
        Assert.False(bindings.IsActionAvailable("assistants.legacy.confirm"));
        Assert.False(bindings.IsActionAvailable("assistants.legacy.result.open")); // No Assistant editor for a Specialist.
        var generation = bindings.ReviewGeneration;
        bindings.CancelReview();
        Assert.False(bindings.IsReviewCurrent(preview, generation)); Assert.Null(bindings.OriginalClassifiedDefinition);
    }
}
