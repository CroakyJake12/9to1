#if !ANDROID
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.NativeUI;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [AvaloniaFact]
    public Task New_conversation_shows_real_composer_without_a_model_and_preserves_its_draft_after_reopen() => RunAsync(async rig =>
    {
        const string draft = "Fictional saved draft awaiting model access.";
        AssistantIdentity? identity = null; Guid conversationId = Guid.Empty;
        await WithActualMemorySurface(rig, async (window, surface, controller, _) =>
        {
            window.Width = 1320; window.Height = 960;
            await ClickCanonicalCaptureControl(window, surface, "assistant-create", () => surface.Bindings.Draft is not null);
            var name = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), value =>
                AutomationProperties.GetName(value) == "Assistant name");
            name.Text = "Fictional composer helper";
            await ClickCanonicalCaptureControl(window, surface, "assistant-save", () =>
                controller.Snapshot.SelectedAssistant is not null && surface.Bindings.Draft?.IsDirty == false);
            identity = controller.Snapshot.SelectedAssistant!.Identity;
            await ClickCanonicalCaptureControl(window, surface, "configuration-back", () =>
                surface.Bindings.TryGetValue("ShowWork", out var shown) && shown is true);
            await ClickCanonicalCaptureControl(window, surface, "new-conversation", () =>
                controller.Snapshot.ConversationBinding is not null && !controller.Snapshot.IsLoading);
            conversationId = controller.Snapshot.ConversationBinding!.Conversation.Id;
            await FlushNativeMemoryUi(window);
            var input = AssertOriginalComposer(window);
            var top = input.TranslatePoint(new Point(0, 0), window)
                ?? throw new InvalidOperationException("The actual composer is not attached to its shown window.");
            Assert.True(top.Y >= 0 && top.Y + input.Bounds.Height <= window.ClientSize.Height,
                "The actual message composer must be visible in the initial desktop viewport before management controls.");
            var actualConversation = controller.Snapshot.Conversation!;
            Assert.Empty(controller.Snapshot.Models); Assert.Empty(actualConversation.Messages);
            Assert.Null(actualConversation.CanonicalTask);
            Assert.False(OriginalComposerButton(window, "assistant-send").IsEnabled);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), value =>
                value.IsEffectivelyVisible && AutomationProperties.GetName(value) == "Model availability help" &&
                value.Text != null && value.Text.Contains("save a draft", StringComparison.Ordinal));
            var revision = controller.Snapshot.Revision;
            await ClickCanonicalCaptureControl(window, surface, "conversation-refresh-models", () =>
                controller.Snapshot.Revision > revision && !controller.Snapshot.IsLoading);
            Assert.Empty(controller.Snapshot.Models); Assert.False(OriginalComposerButton(window, "assistant-send").IsEnabled);
            input.Text = draft; Assert.Equal(draft, surface.Bindings.Prompt);
            await ClickCanonicalCaptureControl(window, surface, "assistant-save-draft", () => !surface.HasUnsavedChanges);
            var actual = await rig.Bridge.ReadConversationAsync(controller.Snapshot.ConversationBinding!, Token);
            Assert.Equal(draft, actual.Draft!.Content); Assert.Empty(actual.Messages);
            await ClickCanonicalCaptureControl(window, surface, "conversation-model-settings", () =>
                surface.Bindings.TryGetValue("ShowConfiguration", out var shown) && shown is true);
            Assert.Equal(identity, surface.Bindings.Draft!.Identity);
            await ClickCanonicalCaptureControl(window, surface, "configuration-back", () =>
                surface.Bindings.TryGetValue("ShowWork", out var shown) && shown is true);
        });
        await rig.ReopenAsync();
        await WithActualMemorySurface(rig, async (window, surface, controller, _) =>
        {
            window.Width = 1320; window.Height = 960;
            await OpenActualSavedMemoryConversation(surface, identity!, conversationId, window);
            Assert.Equal(draft, AssertOriginalComposer(window).Text);
            Assert.Equal(conversationId, controller.Snapshot.ConversationBinding!.Conversation.Id);
            Assert.Empty(controller.Snapshot.Models); Assert.Empty(controller.Snapshot.Conversation!.Messages);
            Assert.False(OriginalComposerButton(window, "assistant-send").IsEnabled);
            Assert.True(OriginalComposerButton(window, "conversation-refresh-models").IsEnabled);
            Assert.False(surface.HasUnsavedChanges);
        });
    });

    [AvaloniaFact]
    public Task Actual_task_conversation_keeps_task_composer_controls_without_inventing_a_task_or_model() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(new AssistantConfiguration { Name = "Fictional task composer" });
        await WithActualMemorySurface(rig, async (window, surface, controller, _) =>
        {
            window.Width = 1320; window.Height = 960;
            await OpenActualSavedMemoryConversation(surface, binding.Definition.Identity, binding.Conversation.Id, window);
            await ClickCanonicalCaptureControl(window, surface, "assistant-new-task", () =>
                controller.Snapshot.Conversation?.Conversation.Kind == ConversationKind.Task && !controller.Snapshot.IsLoading);
            Assert.True(AssertOriginalComposer(window).IsEnabled);
            Assert.False(OriginalComposerButton(window, "assistant-start-task").IsEnabled);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), value =>
                value.IsEffectivelyVisible && HasComposerId(value, "assistant-send"));
            var actualConversation = controller.Snapshot.Conversation!;
            Assert.Null(actualConversation.CanonicalTask);
            Assert.Empty(controller.Snapshot.Models); Assert.Empty(actualConversation.Messages);
            Assert.Equal(2, controller.Snapshot.Conversations.Count);
        });
    });

    private static TextBox AssertOriginalComposer(Window window) => Assert.Single(
        window.GetVisualDescendants().OfType<TextBox>(), value => value.IsEffectivelyVisible &&
        AutomationProperties.GetName(value) == "Message to your Assistant");
    private static Button OriginalComposerButton(Window window, string id) => Assert.Single(
        window.GetVisualDescendants().OfType<Button>(), value => value.IsEffectivelyVisible && HasComposerId(value, id));
    private static bool HasComposerId(Control value, string id) => CuiRuntimeIdentity.GetStableId(value) is { } actual &&
        (actual == "id:" + id || actual.EndsWith("/id:" + id, StringComparison.Ordinal));
}
#endif
