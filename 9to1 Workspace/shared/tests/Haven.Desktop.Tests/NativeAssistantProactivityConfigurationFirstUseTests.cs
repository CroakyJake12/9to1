#if !ANDROID
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Assistants.NativeUI;
using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [AvaloniaFact]
    public Task Rendered_proactive_contact_preferences_save_and_reopen_same_Assistant_without_arming_work() => RunAsync(async rig =>
    {
        AssistantIdentity? identity = null; Guid conversationId = default;
        await WithActualProactivitySurface(rig, async (window, surface, controller) =>
        {
            await surface.Bindings.DispatchAsync("assistants.create.start", null, Token);
            Assert.True(surface.Bindings.TrySetValue("DraftName", "Fictional contact preference helper"));
            await FlushNativeMemoryUi(window);
            foreach (var name in ProactivityCheckboxNames)
            {
                var toggle = MemoryToggle(window, name); Assert.True(toggle.IsEnabled); Assert.False(toggle.IsChecked == true);
                toggle.IsChecked = true;
            }
            var originalRequests = (await rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests.Select(value => value.RequestId).ToArray();
            await surface.Bindings.DispatchAsync("assistants.configuration.save", null, Token);
            var selected = controller.Snapshot.SelectedAssistant!; identity = selected.Identity;
            AssertRequestedContactPreferences(selected.Configuration.Proactive);
            Assert.Contains(selected.Capabilities, value => value.Feature == "Proactivity and schedules" && value.State == AssistantSupportState.Unsupported);
            Assert.True(surface.Bindings.TryGetValue("ProactivityStatus", out var status)); Assert.Contains("unavailable", Assert.IsType<string>(status));
            Assert.True(surface.Bindings.TryGetValue("CanConfigureProactivity", out var available)); Assert.False(Assert.IsType<bool>(available));
            Assert.False(surface.Bindings.IsActionAvailable("assistants.proactivity.configure") == true);
            Assert.Equal(originalRequests, (await rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests.Select(value => value.RequestId).ToArray());
            await surface.Bindings.DispatchAsync("assistants.configuration.back", null, Token);
            await surface.Bindings.DispatchAsync("assistants.conversation.new", null, Token);
            conversationId = controller.Snapshot.ConversationBinding!.Conversation.Id;
            Assert.Single(controller.Snapshot.Conversations); Assert.Null(controller.Snapshot.Work?.Controls?.CanonicalTaskContext);
        });
        await rig.ReopenAsync();
        await WithActualProactivitySurface(rig, async (window, surface, controller) =>
        {
            await OpenActualSavedMemoryConversation(surface, identity!, conversationId, window);
            await surface.Bindings.DispatchAsync("assistants.configuration.open", null, Token); await FlushNativeMemoryUi(window);
            Assert.Equal(identity, controller.Snapshot.SelectedAssistant!.Identity);
            Assert.Equal(conversationId, controller.Snapshot.ConversationBinding!.Conversation.Id);
            foreach (var name in ProactivityCheckboxNames) Assert.True(MemoryToggle(window, name).IsChecked == true);
            AssertRequestedContactPreferences(controller.Snapshot.SelectedAssistant.Configuration.Proactive);
        });
    }, importMemory: false);

    [AvaloniaFact]
    public Task Turning_contact_preferences_off_preserves_unknown_events_channels_resource_references_and_history() => RunAsync(async rig =>
    {
        AssistantIdentity? identity = null; Guid conversationId = default;
        await WithActualProactivitySurface(rig, async (window, surface, controller) =>
        {
            await surface.Bindings.DispatchAsync("assistants.create.start", null, Token);
            surface.Bindings.Draft!.SetConfiguration(new() { Name = "Fictional preserved preferences",
                KnowledgeResourceIds = ["original-resource"], ProjectReferences = [PreservedContactProject],
                Proactive = new(true, true, true, ["future-event", "task.completed", "task.user-action-required"],
                    ["future-channel", "in-app"], ["original-automation"]) });
            await surface.Bindings.DispatchAsync("assistants.configuration.save", null, Token);
            identity = controller.Snapshot.SelectedAssistant!.Identity;
            await surface.Bindings.DispatchAsync("assistants.configuration.back", null, Token);
            await surface.Bindings.DispatchAsync("assistants.conversation.new", null, Token);
            conversationId = controller.Snapshot.ConversationBinding!.Conversation.Id;
            await surface.Bindings.DispatchAsync("assistants.configuration.open", null, Token); await FlushNativeMemoryUi(window);
            var pending = (await rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests.Select(value => value.RequestId).ToArray();
            foreach (var name in ProactivityCheckboxNames) MemoryToggle(window, name).IsChecked = false;
            await surface.Bindings.DispatchAsync("assistants.configuration.save", null, Token);
            AssertPreservedContactConfiguration(controller.Snapshot.SelectedAssistant!.Configuration);
            Assert.Equal(identity, controller.Snapshot.ConversationBinding!.Definition.Identity);
            Assert.Equal(conversationId, controller.Snapshot.ConversationBinding.Conversation.Id);
            Assert.Equal(pending, (await rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests.Select(value => value.RequestId).ToArray());
        });
        await rig.ReopenAsync();
        await WithActualProactivitySurface(rig, async (window, surface, controller) =>
        {
            await OpenActualSavedMemoryConversation(surface, identity!, conversationId, window);
            AssertPreservedContactConfiguration(controller.Snapshot.SelectedAssistant!.Configuration);
            Assert.Equal(conversationId, controller.Snapshot.ConversationBinding!.Conversation.Id);
            Assert.Single(controller.Snapshot.Conversations);
        });
    }, importMemory: false);

    // Fictional saved preference metadata; this does not issue project access.
    private static readonly DeveloperProjectReference PreservedContactProject = new(
        Guid.Parse("70000000-0000-0000-0000-000000000001"), 1,
        Guid.Parse("70000000-0000-0000-0000-000000000002"), 1,
        Guid.Parse("70000000-0000-0000-0000-000000000003"));
    private static readonly string[] ProactivityCheckboxNames = ["Opt in to proactive contact", "Allow supported check-ins",
        "Allow proactive conversations", "Prefer in-app notifications", "Request Task completion check-ins", "Request Task attention check-ins"];
    private static void AssertRequestedContactPreferences(AssistantProactivePreferences preferences)
    {
        Assert.True(preferences.Enabled); Assert.True(preferences.AllowCheckIns); Assert.True(preferences.AllowUnsolicitedConversations);
        Assert.Equal(new[] { "in-app" }, preferences.NotificationChannels);
        Assert.Equal(new[] { "task.completed", "task.user-action-required" }, preferences.EventKinds);
    }
    private static void AssertPreservedContactConfiguration(AssistantConfiguration configuration)
    {
        Assert.False(configuration.Proactive.Enabled); Assert.False(configuration.Proactive.AllowCheckIns);
        Assert.False(configuration.Proactive.AllowUnsolicitedConversations);
        Assert.Equal(new[] { "future-channel" }, configuration.Proactive.NotificationChannels);
        Assert.Equal(new[] { "future-event" }, configuration.Proactive.EventKinds);
        Assert.Equal(new[] { "original-automation" }, configuration.Proactive.AutomationIds);
        Assert.Equal(new[] { "original-resource" }, configuration.KnowledgeResourceIds);
        Assert.Equal(new[] { PreservedContactProject }, configuration.ProjectReferences);
    }
    private static async Task WithActualProactivitySurface(Rig rig,
        Func<Window, AssistantsNativeCuiSurface, AssistantsWorkspaceController, Task> body)
    {
        var controller = new AssistantsWorkspaceController(rig.Bridge); AssistantsNativeCuiSurface? surface = null;
        var window = new Window { Width = 1100, Height = 950 }; window.Show(); var errors = new List<Exception>();
        var originals = new List<Task>();
        try
        {
            surface = new(controller, new MemoryFixtureReadiness(), captureOriginalOwner: actual => surface = actual);
            window.Content = surface; var initialize = surface.InitializeAsync(Token); originals.Add(initialize); rig.Retain(initialize);
            await initialize; await FlushNativeMemoryUi(window); await body(window, surface, controller);
            Assert.True(await surface.PrepareToCloseAsync(Token));
        }
        catch (Exception cause) { errors.Add(cause); }
        finally
        {
            foreach (var acquire in new Func<Task>[] { () => surface?.CloseAndDrainAsync() ?? Task.CompletedTask, controller.CloseAndDrainAsync })
            {
                Task? raw = null;
                try { raw = acquire(); originals.Add(raw); rig.Retain(raw); await raw; }
                catch (Exception cause) { errors.Add(raw?.Exception ?? cause); }
            }
            if (surface?.OriginalClose?.IsCompletedSuccessfully == true && controller.OriginalClose?.IsCompletedSuccessfully == true) window.Close();
        }
        if (errors.Count != 0) throw new AggregateException("Actual proactive preference UI/Den originals remain retained.", errors);
    }
}
#endif
