#if !ANDROID
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [AvaloniaFact]
    public Task Actual_saved_user_message_renders_rich_content_and_stages_code_without_execution_then_reopens_same_history() => RunAsync(async rig =>
    {
        // Explicit fictional user content in the actual maintained repository;
        // no model result, tool execution or provider capability is manufactured.
        const string markdown = """
            # Fictional rich message

            - preserved list
            - [x] read-only task

            | Name | Value |
            | --- | --- |
            | Example | one |

            $$
            \frac{1}{2}
            $$

            ```text
            Fictional code to review
            ```
            """;
        var binding = await rig.CreateAsync(new AssistantConfiguration { Name = "Fictional rich message helper" });
        var message = new ChatMessage(Guid.NewGuid(), binding.Conversation.Id, MessageRole.User, markdown,
            null, null, null, DateTimeOffset.UtcNow);
        var originalWrite = rig.OriginalConversations.AddMessageAsync(message, Token); rig.Retain(originalWrite); await originalWrite;
        string? savedDraft = null;
        await WithActualMemorySurface(rig, async (window, surface, controller, _) =>
        {
            window.Width = 1320; window.Height = 960;
            await OpenActualSavedMemoryConversation(surface, binding.Definition.Identity, binding.Conversation.Id, window);
            var view = Assert.Single(window.GetVisualDescendants().OfType<CuiMarkdownView>());
            Assert.Equal(markdown, view.Text);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Fictional rich message");
            Assert.Contains(view.GetVisualDescendants().OfType<CheckBox>(), checkbox => checkbox.IsChecked == true && !checkbox.IsEnabled);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Example");
            Assert.Contains(view.GetVisualDescendants().OfType<SelectableTextBlock>(), text => text.Text?.Contains('⁄') == true);
            var apply = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Ask to apply"));
            apply.BringIntoView(); await FlushNativeMemoryUi(window);
            Assert.True(surface.Bindings.TrySetValue("Prompt", "Keep my existing draft."));
            await ClickCanonicalCaptureControl(window, surface, apply, () => surface.Bindings.Prompt.Contains("Apply this code safely", StringComparison.Ordinal));
            savedDraft = surface.Bindings.Prompt;
            Assert.StartsWith("Keep my existing draft.", savedDraft);
            Assert.Contains("Fictional code to review", savedDraft);
            var actualConversation = controller.Snapshot.Conversation!;
            Assert.Empty(controller.Snapshot.Models); Assert.Null(actualConversation.CanonicalTask);
            Assert.Equal(message, Assert.Single(actualConversation.Messages));
            await ClickCanonicalCaptureControl(window, surface, "assistant-save-draft", () => !surface.HasUnsavedChanges);
            var persisted = await rig.Bridge.ReadConversationAsync(controller.Snapshot.ConversationBinding!, Token);
            Assert.Equal(savedDraft, persisted.Draft!.Content); Assert.Equal(message, Assert.Single(persisted.Messages));
            await ClickCanonicalCaptureControl(window, surface, "new-conversation", () =>
                controller.Snapshot.ConversationBinding is { } fresh && fresh.Conversation.Id != binding.Conversation.Id && !controller.Snapshot.IsLoading);
            Assert.Empty(surface.Bindings.Prompt);
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await FlushNativeMemoryUi(window);
            Assert.Empty(surface.Bindings.Prompt); Assert.False(surface.HasUnsavedChanges);
            Assert.Empty(controller.Snapshot.Conversation!.Messages);
        });
        await rig.ReopenAsync();
        await WithActualMemorySurface(rig, async (window, surface, controller, _) =>
        {
            await OpenActualSavedMemoryConversation(surface, binding.Definition.Identity, binding.Conversation.Id, window);
            Assert.Equal(markdown, Assert.Single(window.GetVisualDescendants().OfType<CuiMarkdownView>()).Text);
            Assert.Equal(savedDraft, surface.Bindings.Prompt);
            var actualConversation = controller.Snapshot.Conversation!;
            Assert.Equal(message, Assert.Single(actualConversation.Messages));
            Assert.Null(actualConversation.CanonicalTask); Assert.Empty(controller.Snapshot.Models);
            Assert.False(surface.HasUnsavedChanges);
        });
    });
}
#endif
