#if !ANDROID
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
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
            var input = AssertOriginalComposer(window);
            var draft = "Keep this viewport task draft\n" + string.Join("\n", Enumerable.Repeat("A long Task instruction remains editable and saved.", 30));
            input.Text = draft; Assert.True(input.Focus());
            input.CaretIndex = 18; input.SelectionStart = 10; input.SelectionEnd = 18;
            foreach (var size in new[] { new Size(940, 660), new Size(500, 650), new Size(360, 640) })
            {
                window.Width = size.Width; window.Height = size.Height;
                await FlushNativeMemoryUi(window); await FlushNativeMemoryUi(window);
                AssertOriginalViewportChrome(window, task: true);
                Assert.Same(input, AssertOriginalComposer(window)); Assert.Equal(draft, input.Text);
                Assert.Equal("viewport", input.SelectedText); Assert.True(input.Bounds.Height <= 112);
                var transcript = Assert.IsType<CuiContainerControl>(OriginalViewportControl(window, "assistant-transcript-scroll"));
                var scroll = Assert.Single(transcript.GetVisualDescendants().OfType<ScrollViewer>(), value =>
                    ReferenceEquals(value.GetVisualAncestors().OfType<CuiContainerControl>().FirstOrDefault(), transcript));
                Assert.True(double.IsFinite(scroll.Viewport.Height) && scroll.Viewport.Height > 0,
                    "The real Task composer must leave visible transcript space with a long draft.");
                Assert.Null(controller.Snapshot.Conversation!.CanonicalTask); Assert.Empty(controller.Snapshot.Models);
            }
            await ClickCanonicalCaptureControl(window, surface, "assistant-save-draft", () => !surface.HasUnsavedChanges);
            var saved = await rig.Bridge.ReadConversationAsync(controller.Snapshot.ConversationBinding!, Token);
            Assert.Equal(draft, saved.Draft!.Content); Assert.Empty(saved.Messages); Assert.Null(saved.CanonicalTask);
        });
    });

    [AvaloniaFact]
    public Task Actual_assistant_composer_stays_in_view_while_saved_history_scrolls_and_workspace_resizes() => RunAsync(async rig =>
    {
        var original = await rig.CreateAsync(new AssistantConfiguration { Name = "Viewport helper" });
        var created = DateTimeOffset.UtcNow;
        var messages = new List<ChatMessage>();
        for (var index = 0; index < 30; index++)
        {
            var message = new ChatMessage(Guid.NewGuid(), original.Conversation.Id, MessageRole.User,
                $"Saved viewport entry {index}. " + string.Join(" ", Enumerable.Repeat("Retain this actual conversation history.", 12)),
                null, null, null, created.AddSeconds(index));
            messages.Add(message);
            var write = rig.OriginalConversations.AddMessageAsync(message, Token); rig.Retain(write); await write;
        }
        const string draft = "Keep this viewport draft while I review saved history.";
        await WithActualMemorySurface(rig, async (window, surface, controller, _) =>
        {
            window.Width = 1320; window.Height = 960;
            await OpenActualSavedMemoryConversation(surface, original.Definition.Identity, original.Conversation.Id, window);
            var actualBinding = controller.Snapshot.ConversationBinding;
            var input = AssertOriginalComposer(window);
            input.Text = draft;
            Assert.True(input.Focus());
            input.CaretIndex = 18; input.SelectionStart = 10; input.SelectionEnd = 18;
            Assert.Equal("viewport", input.SelectedText);
            var originalCaret = input.CaretIndex;
            var footer = OriginalViewportControl(window, "assistant-composer-footer");
            var transcript = Assert.IsType<CuiContainerControl>(OriginalViewportControl(window, "assistant-transcript-scroll"));
            var scroll = Assert.Single(transcript.GetVisualDescendants().OfType<ScrollViewer>(), value =>
                ReferenceEquals(value.GetVisualAncestors().OfType<CuiContainerControl>().FirstOrDefault(), transcript));
            bool WorkspaceOpen() => surface.Bindings.TryGetValue("ShowAssistantWorkspace", out var visible) && visible is true;
            Assert.False(WorkspaceOpen());

            foreach (var size in new[] { new Size(1320, 960), new Size(940, 660), new Size(500, 650), new Size(360, 640) })
            {
                window.Width = size.Width; window.Height = size.Height;
                await FlushNativeMemoryUi(window); await FlushNativeMemoryUi(window);
                AssertOriginalViewportChrome(window);
                Assert.True(double.IsFinite(scroll.Viewport.Height) && scroll.Viewport.Height > 0,
                    "The actual transcript must receive a finite, positive part of the viewport.");
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 100,
                    "The regression requires actual saved history beyond the visible transcript.");
                var pinnedFooter = OriginalViewportBounds(window, footer);
                var pinnedInput = OriginalViewportBounds(window, input);
                var transcriptBounds = OriginalViewportBounds(window, transcript);
                Assert.True(transcriptBounds.Bottom <= pinnedFooter.Top + 1,
                    "The transcript must end above the separate composer row.");
                Assert.DoesNotContain(footer.GetVisualAncestors(), value => value is ScrollViewer);

                scroll.ScrollToHome(); await FlushNativeMemoryUi(window);
                Assert.Equal(0, scroll.Offset.Y);
                scroll.ScrollToEnd(); await FlushNativeMemoryUi(window);
                Assert.True(scroll.Offset.Y > 0);
                Assert.Equal(pinnedFooter, OriginalViewportBounds(window, footer));
                Assert.Equal(pinnedInput, OriginalViewportBounds(window, input));
                AssertOriginalViewportChrome(window);
                AssertRetainedDraft();

                // Reach Workspace through the actual keyboard order from the retained draft.
                Assert.True(input.Focus());
                var toggle = OriginalComposerButton(window, "assistant-workspace-toggle");
                for (var count = 0; count < 40 && !toggle.IsFocused; count++)
                {
                    window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                    window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                    await FlushNativeMemoryUi(window);
                }
                Assert.True(toggle.IsFocused, "Workspace must remain keyboard-reachable for first-use model selection.");
                var toggleBounds = OriginalViewportBounds(window, toggle);
                var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(toggleBounds.Center));
                Assert.Contains(toggle, hit.GetSelfAndVisualAncestors());
                await ClickCanonicalCaptureControl(window, surface, "assistant-workspace-toggle", WorkspaceOpen);
                await FlushNativeMemoryUi(window); await FlushNativeMemoryUi(window);
                AssertOriginalViewportControl(window, OriginalComposerButton(window, "assistant-workspace-close"));
                Assert.Same(input, AssertOriginalComposer(window)); AssertRetainedDraft();
                await ClickCanonicalCaptureControl(window, surface, "assistant-workspace-close", () => !WorkspaceOpen());
                await FlushNativeMemoryUi(window); await FlushNativeMemoryUi(window);
                AssertOriginalViewportChrome(window); AssertRetainedDraft();
                Assert.Equal(pinnedFooter, OriginalViewportBounds(window, footer));
            }

            await ClickCanonicalCaptureControl(window, surface, "assistant-save-draft", () => !surface.HasUnsavedChanges);
            var saved = await rig.Bridge.ReadConversationAsync(controller.Snapshot.ConversationBinding!, Token);
            Assert.Equal(draft, saved.Draft!.Content);
            Assert.Equal(messages.Select(message => message.Id), saved.Messages.Select(message => message.Id));
            Assert.Empty(controller.Snapshot.Models); Assert.Null(controller.Snapshot.Conversation!.CanonicalTask);

            void AssertRetainedDraft()
            {
                Assert.Same(input, AssertOriginalComposer(window)); Assert.Same(actualBinding, controller.Snapshot.ConversationBinding);
                Assert.Equal(draft, input.Text); Assert.Equal(draft, surface.Bindings.Prompt);
                Assert.Equal(10, input.SelectionStart); Assert.Equal(18, input.SelectionEnd);
                Assert.Equal(originalCaret, input.CaretIndex); Assert.Equal("viewport", input.SelectedText);
                Assert.Equal(messages.Count, controller.Snapshot.Conversation!.Messages.Count);
            }
        });
        await rig.ReopenAsync();
        await WithActualMemorySurface(rig, async (window, surface, controller, _) =>
        {
            window.Width = 500; window.Height = 650;
            await OpenActualSavedMemoryConversation(surface, original.Definition.Identity, original.Conversation.Id, window);
            await FlushNativeMemoryUi(window); await FlushNativeMemoryUi(window);
            AssertOriginalViewportChrome(window);
            Assert.Equal(draft, AssertOriginalComposer(window).Text);
            Assert.Equal(messages.Select(message => message.Id), controller.Snapshot.Conversation!.Messages.Select(message => message.Id));
            Assert.False(surface.HasUnsavedChanges);
        });
    });

    private static Control OriginalViewportControl(Window window, string id) => Assert.Single(
        window.GetVisualDescendants().OfType<CuiContainerControl>(), value => value.IsEffectivelyVisible && HasComposerId(value, id));

    private static Rect OriginalViewportBounds(Window window, Control control) => new(
        control.TranslatePoint(default, window) ?? throw new InvalidOperationException("The original viewport control is detached."), control.Bounds.Size);

    private static void AssertOriginalViewportControl(Window window, Control control)
    {
        Assert.True(control.IsEffectivelyVisible);
        var bounds = OriginalViewportBounds(window, control);
        Assert.True(bounds.Width > 0 && bounds.Height > 0);
        Assert.True(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= window.ClientSize.Width + 1 && bounds.Bottom <= window.ClientSize.Height + 1,
            $"{CuiRuntimeIdentity.GetStableId(control) ?? control.GetType().Name} must fit inside the initial {window.ClientSize} viewport; actual bounds {bounds}.");
    }

    private static void AssertOriginalViewportChrome(Window window, bool task = false)
    {
        // Geometry is asserted before any BringIntoView, so a whole-page scroller cannot satisfy this test accidentally.
        AssertOriginalViewportControl(window, AssertOriginalComposer(window));
        AssertOriginalViewportControl(window, OriginalViewportControl(window, "assistant-composer-footer"));
        foreach (var id in new[] { "new-conversation", "assistant-workspace-toggle", "conversation-attach-file",
            "assistant-save-draft", "conversation-refresh-models", "conversation-model-settings" })
            AssertOriginalViewportControl(window, OriginalComposerButton(window, id));
        foreach (var id in task ? new[] { "assistant-start-task", "assistant-steer", "assistant-queue", "assistant-stop" } : new[] { "assistant-send" })
            AssertOriginalViewportControl(window, OriginalComposerButton(window, id));
        foreach (var name in new[] { "Selected model", "Model availability help" })
            AssertOriginalViewportControl(window, Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), value =>
                value.IsEffectivelyVisible && AutomationProperties.GetName(value) == name));
    }

    private static TextBox AssertOriginalComposer(Window window) => Assert.Single(
        window.GetVisualDescendants().OfType<TextBox>(), value => value.IsEffectivelyVisible &&
        AutomationProperties.GetName(value) == "Message to your Assistant");
    private static Button OriginalComposerButton(Window window, string id) => Assert.Single(
        window.GetVisualDescendants().OfType<Button>(), value => value.IsEffectivelyVisible && HasComposerId(value, id));
    private static bool HasComposerId(Control value, string id) => CuiRuntimeIdentity.GetStableId(value) is { } actual &&
        (actual == "id:" + id || actual.EndsWith("/id:" + id, StringComparison.Ordinal));
}
#endif
