using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Migration;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

/// <summary>Local UI review controls only; fixture previews grant no product authority.</summary>
public sealed class LegacyAgentMigrationDraftTests
{
    [Fact]
    public void A_fresh_review_has_no_kind_or_confirmation_until_an_explicit_choice()
    {
        var preview = Preview(); var draft = new LegacyAgentMigrationDraft(preview);
        Assert.Null(draft.Kind); Assert.False(draft.CanConfirm); Assert.False(draft.HasUnconfirmedChanges);
        Assert.Throws<InvalidOperationException>(() => draft.CaptureSubmission());
        draft.ChooseKind(ConfiguredIdentityKind.Specialist);
        var submitted = draft.CaptureSubmission();
        Assert.Same(preview, submitted.Preview); Assert.Equal(ConfiguredIdentityKind.Specialist, submitted.Kind);
        Assert.NotEqual(Guid.Empty, submitted.OperationId); Assert.True(draft.HasUnconfirmedChanges);
    }

    [Fact]
    public void A_staged_review_keeps_the_same_recorded_kind_configuration_and_operation()
    {
        var operation = Guid.NewGuid();
        var preview = Preview(new(operation, ConfiguredIdentityKind.Specialist));
        var draft = new LegacyAgentMigrationDraft(preview);
        Assert.True(draft.IsContinuation); Assert.False(draft.HasUnconfirmedChanges);
        Assert.False(draft.TrySetText("MigrationName", "A different identity"));
        Assert.Throws<InvalidOperationException>(() => draft.ChooseKind(ConfiguredIdentityKind.Assistant));
        var submission = draft.CaptureSubmission();
        Assert.Same(preview, submission.Preview); Assert.Equal(operation, submission.OperationId);
        Assert.Equal(ConfiguredIdentityKind.Specialist, submission.Kind); Assert.Equal("Legacy helper", submission.Configuration.Name);
        Assert.Equal(operation, draft.CaptureSubmission().OperationId);
    }

    [Fact]
    public void Review_edits_preserve_saved_preferences_and_capture_a_private_configuration_copy()
    {
        var tools = new List<string> { "original-tool" };
        var events = new List<string> { "original-event" };
        var configuration = new AssistantConfiguration { Name = "Legacy helper", ToolIds = tools,
            Model = new("actual-provider", "saved-model", EffortLevel.High, false, false),
            Memory = new(true, true), Proactive = new(true, true, true, events),
            KnowledgeResourceIds = ["saved-resource"] };
        var preview = Preview(configuration: configuration); var draft = new LegacyAgentMigrationDraft(preview);
        tools.Add("later-foreign-tool"); events.Add("later-foreign-event");
        draft.ChooseKind(ConfiguredIdentityKind.Assistant); Assert.True(draft.TrySetText("MigrationPurpose", "A reviewed purpose"));
        var submitted = draft.CaptureSubmission();
        Assert.Equal(["original-tool"], submitted.Configuration.ToolIds);
        Assert.Equal(["original-event"], submitted.Configuration.Proactive.EventKinds);
        Assert.Equal(configuration.Model, submitted.Configuration.Model); Assert.Equal(configuration.Memory, submitted.Configuration.Memory);
        Assert.Equal(["saved-resource"], submitted.Configuration.KnowledgeResourceIds);
        Assert.Equal("A reviewed purpose", submitted.Configuration.Purpose);
    }

    [Fact]
    public void Only_the_same_receipt_acknowledges_a_review_and_newer_local_edits_remain_dirty()
    {
        var draft = new LegacyAgentMigrationDraft(Preview()); draft.ChooseKind(ConfiguredIdentityKind.Assistant);
        var submission = draft.CaptureSubmission();
        var definition = new AssistantDefinitionSnapshot(new("fixture-den", "personal", submission.Preview.LegacyAgentId.ToString("D")),
            1, ConfiguredIdentityKind.Assistant, submission.Configuration, []);
        var receipt = new LegacyAgentMigrationResult(definition, submission.Preview.LegacyAgentId,
            submission.Preview.SourceRevision, submission.OperationId, true);
        Assert.Throws<InvalidOperationException>(() => draft.Acknowledge(submission, receipt with { OperationId = Guid.NewGuid() }));
        Assert.True(draft.HasUnconfirmedChanges);
        draft.TrySetText("MigrationName", "A newer name"); draft.Acknowledge(submission, receipt);
        Assert.True(draft.HasUnconfirmedChanges); Assert.Equal("A newer name", draft.Configuration.Name);
    }

    internal static LegacyAgentMigrationPreview Preview(LegacyAgentPendingClassification? pending = null,
        AssistantConfiguration? configuration = null, AssistantDefinitionSnapshot? classified = null)
    {
        var owner = new object(); var original = new object(); var id = Guid.NewGuid();
        var source = new AgentDefinition(id, "Legacy helper", "Existing definition", "Original instructions", "user", "saved-model", null,
            "", "{}", false, true, DateTimeOffset.UtcNow);
        return new(owner, original, id, "fixture-source-revision", source, configuration ?? new() { Name = "Legacy helper" },
            classified is not null ? LegacyAgentMigrationState.Classified : pending is null ? LegacyAgentMigrationState.Unselected : LegacyAgentMigrationState.Staged,
            classified?.Revision ?? 0, new(2, 3, true, true), ["Original unknown source fields are preserved."], classified is null, pending, classified);
    }
}
