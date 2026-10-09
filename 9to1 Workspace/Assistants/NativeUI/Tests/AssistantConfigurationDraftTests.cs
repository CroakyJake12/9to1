using HavenOS.Apps.Assistants.Contracts;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

public sealed class AssistantConfigurationDraftTests
{
    [Fact]
    public void Failed_save_does_not_replace_the_current_draft_or_saved_revision()
    {
        var original = Definition(4, "Saved name");
        var draft = new AssistantConfigurationDraft(original);
        Assert.True(draft.TrySetText("DraftName", "Unsaved name"));
        var submitted = draft.CaptureSubmission();
        // The service refused this submission; no canonical acknowledgement exists.
        Assert.Equal("Unsaved name", submitted.Configuration.Name);
        Assert.Equal("Unsaved name", draft.Configuration.Name);
        Assert.Equal(original.Identity, draft.Identity);
        Assert.Equal(4, draft.ExpectedRevision);
        Assert.True(draft.IsDirty);
    }

    [Fact]
    public void Successful_create_keeps_newer_edits_on_the_same_persistent_identity()
    {
        var draft = new AssistantConfigurationDraft();
        draft.TrySetText("DraftName", "Submitted name");
        var submitted = draft.CaptureSubmission();
        draft.TrySetText("DraftName", "Newer edit during save");
        var saved = Definition(1, "Submitted name");
        draft.Acknowledge(submitted, saved);
        Assert.Equal(saved.Identity, draft.Identity);
        Assert.Equal(1, draft.ExpectedRevision);
        Assert.Equal("Newer edit during save", draft.Configuration.Name);
        Assert.True(draft.IsDirty);
        var next = draft.CaptureSubmission();
        Assert.Equal(saved.Identity, next.Identity);
        Assert.Equal(1, next.ExpectedRevision);
    }

    [Fact]
    public void Foreign_or_stale_definition_cannot_clear_the_current_form()
    {
        var original = Definition(7, "Saved name");
        var draft = new AssistantConfigurationDraft(original);
        draft.TrySetText("DraftInstructions", "Keep this changed instruction");
        var submitted = draft.CaptureSubmission();
        Assert.Throws<InvalidOperationException>(() => draft.Acknowledge(submitted,
            Definition(8, "Foreign") with { Identity = new("same-den", "personal", "other-assistant") }));
        Assert.Throws<InvalidOperationException>(() => draft.Acknowledge(submitted, original));
        Assert.Equal("Keep this changed instruction", draft.Configuration.Instructions);
        Assert.True(draft.IsDirty);
        Assert.Equal(7, draft.ExpectedRevision);
    }

    [Fact]
    public void A_captured_submission_does_not_share_mutable_resource_lists_with_the_form_caller()
    {
        var resourceIds = new List<string> { "original-resource" };
        var draft = new AssistantConfigurationDraft(Definition(1, "Assistant") with
        { Configuration = new AssistantConfiguration { Name = "Assistant", KnowledgeResourceIds = resourceIds } });
        var submitted = draft.CaptureSubmission();
        resourceIds.Add("later-resource");
        Assert.Equal(new[] { "original-resource" }, submitted.Configuration.KnowledgeResourceIds);
        Assert.Equal(new[] { "original-resource" }, draft.Configuration.KnowledgeResourceIds);
    }

    private static AssistantDefinitionSnapshot Definition(long revision, string name) => new(
        new("same-den", "personal", "same-assistant"), revision, ConfiguredIdentityKind.Assistant,
        new AssistantConfiguration { Name = name }, []);
}
