using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

public sealed class AssistantProactivityConfigurationTests
{
    [Fact]
    public void Contact_choice_updates_only_its_preference_and_preserves_unknown_values()
    {
        var events = new List<string> { "future-event" }; var channels = new List<string> { "future-channel" };
        var schedules = new List<string> { "original-automation" };
        var project = new DeveloperProjectReference(Guid.NewGuid(), 1, Guid.NewGuid(), 1, Guid.NewGuid());
        var draft = new AssistantConfigurationDraft(Definition(2, new() { Name = "Saved helper",
            ProjectReferences = [project], KnowledgeResourceIds = ["original-resource"],
            Proactive = new(EventKinds: events, NotificationChannels: channels, AutomationIds: schedules) }));
        Assert.True(draft.TrySetBoolean("DraftNotifyInApp", true));
        Assert.True(draft.TrySetBoolean("DraftEventTaskCompleted", true));
        var submission = draft.CaptureSubmission();
        events.Add("later-event"); channels.Add("later-channel"); schedules.Add("later-schedule");
        Assert.Equal(new[] { "future-event", "task.completed" }, submission.Configuration.Proactive.EventKinds);
        Assert.Equal(new[] { "future-channel", "in-app" }, submission.Configuration.Proactive.NotificationChannels);
        Assert.Equal(new[] { "original-automation" }, submission.Configuration.Proactive.AutomationIds);
        Assert.True(draft.TrySetBoolean("DraftNotifyInApp", false));
        Assert.True(draft.TrySetBoolean("DraftEventTaskCompleted", false));
        Assert.Equal(new[] { "future-event" }, draft.Configuration.Proactive.EventKinds);
        Assert.Equal(new[] { "future-channel" }, draft.Configuration.Proactive.NotificationChannels);
        Assert.Equal(new[] { project }, draft.Configuration.ProjectReferences);
        Assert.Equal(new[] { "original-resource" }, draft.Configuration.KnowledgeResourceIds);
    }

    [Fact]
    public void New_contact_edit_survives_an_older_acknowledged_save_on_the_same_identity()
    {
        var original = Definition(4, new() { Name = "Saved helper" }); var draft = new AssistantConfigurationDraft(original);
        Assert.True(draft.TrySetBoolean("DraftProactivityEnabled", true));
        var submitted = draft.CaptureSubmission();
        Assert.True(draft.TrySetBoolean("DraftAllowCheckIns", true));
        draft.Acknowledge(submitted, Definition(5, submitted.Configuration));
        Assert.True(draft.IsDirty); Assert.Equal(original.Identity, draft.Identity); Assert.Equal(5, draft.ExpectedRevision);
        Assert.True(draft.Configuration.Proactive.Enabled); Assert.True(draft.Configuration.Proactive.AllowCheckIns);
        Assert.False(submitted.Configuration.Proactive.AllowCheckIns);
        draft.DiscardChanges(); Assert.False(draft.IsDirty); Assert.True(draft.Configuration.Proactive.Enabled);
        Assert.False(draft.Configuration.Proactive.AllowCheckIns);
    }
    private static AssistantDefinitionSnapshot Definition(long revision, AssistantConfiguration configuration) =>
        new(new("same-den", "personal", "same-helper"), revision, ConfiguredIdentityKind.Assistant, configuration, []);
}
