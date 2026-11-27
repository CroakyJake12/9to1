using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Chat;
using Haven.UI;
using Haven.UI.Components;
using NativeButton = Avalonia.Controls.Button;

namespace Haven.Desktop.Tests;

public sealed class ChatHavenSceneTests
{
    [AvaloniaFact]
    public Task Chat_scene_uses_canonical_CUI_composer_and_dynamic_message_host() =>
        InComposerSceneAsync(async (scene, host, window) =>
        {
            Assert.Same(scene.Input, scene.ComposerView.Input);
            Assert.Contains(host.Children, child => ReferenceEquals(child, scene.ComposerView));
            Assert.True(scene.ComposerView.IsAttachedToVisualTree());
            var input = ComposerControl<TextBox>(scene, "composer-input");
            Assert.True(input.AcceptsReturn);
            Assert.Equal("Ask Haven anything", input.PlaceholderText);
            Assert.Equal("Message", AutomationProperties.GetName(input));
            Assert.Equal("Add files or actions", AutomationProperties.GetName(ComposerControl<NativeButton>(scene, "add-button")));
            scene.Input.Text = "A real unsent draft";
            await PumpComposerAsync(window);
            Assert.Equal("Send, 1 of 1", AutomationProperties.GetName(ComposerControl<NativeButton>(scene, "send-button")));
            Assert.Equal("Messages", scene.Messages.Name);
        });

    [AvaloniaFact]
    public Task Sending_state_turns_primary_action_into_stop_and_restores_send() =>
        InComposerSceneAsync(async (scene, _, window) =>
        {
            var sends = 0;
            var stops = 0;
            scene.SendRequested += (_, _) => sends++;
            scene.StopRequested += (_, _) => stops++;
            scene.SetSending(true, modelAvailable: true);
            await PumpComposerAsync(window);

            Assert.True(ComposerControl<TextBox>(scene, "composer-input").IsEffectivelyEnabled);
            var stop = ComposerControl<NativeButton>(scene, "send-button");
            Assert.True(stop.IsEffectivelyEnabled);
            Assert.True(scene.ComposerSource.CanStop);
            Assert.Equal("Cancel response, 1 of 1", AutomationProperties.GetName(stop));
            stop.RaiseEvent(new RoutedEventArgs(NativeButton.ClickEvent));
            await scene.ComposerView.CaptureOriginalActionSettlement();
            Assert.Equal(0, sends);
            Assert.Equal(1, stops);

            scene.SetSending(false, modelAvailable: true);
            scene.Input.Text = "Send this new message";
            await PumpComposerAsync(window);
            Assert.True(ComposerControl<TextBox>(scene, "composer-input").IsEffectivelyEnabled);
            var send = ComposerControl<NativeButton>(scene, "send-button");
            Assert.True(send.IsEffectivelyEnabled);
            Assert.True(scene.ComposerSource.CanSend);
            Assert.Equal("Send, 1 of 1", AutomationProperties.GetName(send));
            send.RaiseEvent(new RoutedEventArgs(NativeButton.ClickEvent));
            await scene.ComposerView.CaptureOriginalActionSettlement();
            Assert.Equal(1, sends);
            Assert.Equal(1, stops);
        });

    [AvaloniaFact]
    public Task Tool_activity_rows_share_message_runtime_and_restore_in_order() =>
        InComposerSceneAsync((scene, _, _) =>
        {
            var assistantId = Guid.NewGuid();
            var toolId = Guid.NewGuid();
            var activity = new ToolActivity(toolId, "Browser search", "Found 3 sources", true, TimeSpan.FromMilliseconds(850), DateTimeOffset.UtcNow, 3, 1);
            var assistant = new ChatSceneMessage(assistantId, MessageRole.Assistant, "Answer", "Haven", false, string.Empty, [activity]);
            var user = new ChatSceneMessage(Guid.NewGuid(), MessageRole.User, "Thanks", string.Empty, false, string.Empty);

            scene.SyncMessages([assistant, user]);

            Assert.Equal(3, scene.Messages.Items.Count);
            var tool = scene.Messages.Items[1];
            Assert.Equal("Completed", tool.GetComponent<Haven.UI.Components.Text>("Status").Content);
            Assert.Equal("Browser search", tool.GetComponent<Haven.UI.Components.Text>("Title").Content);
            Assert.Equal("Found 3 sources", tool.GetComponent<Haven.UI.Components.Text>("Detail").Content);
            Assert.Contains("0.9s", tool.GetComponent<Haven.UI.Components.Text>("Meta").Content);
            Assert.Contains("+3 -1", tool.GetComponent<Haven.UI.Components.Text>("Meta").Content);

            scene.SyncMessages([assistant with { ToolActivities = [] }, user]);

            Assert.Equal(2, scene.Messages.Items.Count);
        });

    [AvaloniaFact]
    public Task Streaming_assistant_update_preserves_dynamic_item_and_markdown_identity() =>
        InComposerSceneAsync((scene, _, _) =>
        {
            var userId = Guid.NewGuid();
            var assistantId = Guid.NewGuid();
            scene.SyncMessages([
                new ChatSceneMessage(userId, MessageRole.User, "Hello", "", false, ""),
                new ChatSceneMessage(assistantId, MessageRole.Assistant, "Hi", "Haven", true, "Thinking")
            ]);

            Assert.Equal(2, scene.Messages.Items.Count);
            var assistantItem = scene.Messages.Items[1];
            var markdown = assistantItem.GetComponent<Markdown>("Body");
            Assert.Equal("Hi", markdown.Content);

            scene.UpdateMessage(new ChatSceneMessage(assistantId, MessageRole.Assistant, "Hi again", "Haven", true, "Thinking"));

            Assert.Same(assistantItem, scene.Messages.Items[1]);
            Assert.Same(markdown, assistantItem.GetComponent<Markdown>("Body"));
            Assert.Equal("Hi again", markdown.Content);
        });

    [AvaloniaFact]
    public Task Generated_content_mount_survives_streaming_message_updates_without_reparenting() =>
        InComposerSceneAsync((scene, _, _) =>
        {
            var assistantId = Guid.NewGuid();
            scene.SyncMessages([new ChatSceneMessage(assistantId, MessageRole.Assistant, "Preparing", "Haven", true, string.Empty)]);
            var generated = new Container { Name = "GeneratedTestSurface" };
            generated.Add(new Text { Content = "Interactive result" });

            scene.SetGeneratedContent(assistantId, [generated]);
            var item = scene.Messages.GetItem(assistantId.ToString("N"));
            var host = item.GetComponent<Container>("GeneratedContent");
            Assert.Same(generated, Assert.Single(host.Children));

            scene.UpdateMessage(new ChatSceneMessage(assistantId, MessageRole.Assistant, "Done", "Haven", false, string.Empty));

            Assert.Same(generated, Assert.Single(host.Children));
            Assert.Same(host, item.GetComponent<Container>("GeneratedContent"));
        });

    [AvaloniaFact]
    public Task Composer_add_can_be_disabled_without_disabling_input_or_send() =>
        InComposerSceneAsync(async (scene, _, window) =>
        {
            scene.Input.Text = "Keep this draft";
            scene.SetAddEnabled(false);
            await PumpComposerAsync(window);
            Assert.False(scene.ComposerSource.HasAdd);
            Assert.False(scene.ComposerView.GetVisualDescendants().OfType<NativeButton>()
                .SingleOrDefault(button => AutomationProperties.GetAutomationId(button) == "add-button")?.IsVisible ?? false);
            Assert.True(ComposerControl<TextBox>(scene, "composer-input").IsEffectivelyEnabled);
            Assert.True(ComposerControl<NativeButton>(scene, "send-button").IsEffectivelyEnabled);
            Assert.True(scene.ComposerSource.CanSend);

            scene.SetAddEnabled(true);
            await PumpComposerAsync(window);
            Assert.True(scene.ComposerSource.HasAdd);
            Assert.True(ComposerControl<NativeButton>(scene, "add-button").IsVisible);
            Assert.Equal("Keep this draft", scene.Input.Text);
        });

    [AvaloniaFact]
    public Task Chat_scene_uses_shared_add_menu_prefab_in_attach_to_chat_mode() =>
        InComposerSceneAsync((scene, _, _) =>
        {
            Assert.Equal("ChatAddMenu", scene.AddMenuPrefab.PrefabID);
            Assert.Equal(HavenVisibility.Collapsed, scene.AddOverlay.GetValue(HavenProperties.Visibility));

            scene.ShowAddMenu();
            Assert.Equal(HavenVisibility.Visible, scene.AddOverlay.GetValue(HavenProperties.Visibility));
            Assert.Equal("Attach to Chat", scene.AddMenuPrefab.GetComponent<Text>("MenuTitle").Content);
            Assert.Equal(HavenVisibility.Visible, scene.AddMenuPrefab.GetComponent<Container>("AttachCategories").GetValue(HavenProperties.Visibility));
            Assert.Equal(HavenVisibility.Collapsed, scene.AddMenuPrefab.GetComponent<Container>("MainMenu").GetValue(HavenProperties.Visibility));

            scene.HideAddMenu();
            Assert.Equal(HavenVisibility.Collapsed, scene.AddOverlay.GetValue(HavenProperties.Visibility));
        });

    [AvaloniaFact]
    public Task Chat_composer_stays_inside_viewport_and_plus_button_opens_shared_add_menu() =>
        InComposerSceneAsync(async (scene, host, window) =>
        {
            Assert.True(scene.ComposerHost.Bounds.Bottom <= host.SurfaceMetrics.Viewport.Height + 0.5);
            Assert.True(scene.ComposerHost.Bounds.Y >= 0);
            var composerOrigin = scene.ComposerView.TranslatePoint(new Point(0, 0), window)!.Value;
            Assert.InRange(composerOrigin.X, 0, window.ClientSize.Width);
            Assert.True(composerOrigin.X + scene.ComposerView.Bounds.Width <= window.ClientSize.Width + .5);
            Assert.True(composerOrigin.Y + scene.ComposerView.Bounds.Height <= window.ClientSize.Height + .5);
            Assert.Equal(HavenVisibility.Collapsed, scene.AddOverlay.GetValue(HavenProperties.Visibility));
            var add = ComposerControl<NativeButton>(scene, "add-button");
            add.RaiseEvent(new RoutedEventArgs(NativeButton.ClickEvent));
            await scene.ComposerView.CaptureOriginalActionSettlement();
            await PumpComposerAsync(window);
            Assert.Equal(HavenVisibility.Visible, scene.AddOverlay.GetValue(HavenProperties.Visibility));
            var popup = Assert.IsType<Avalonia.Controls.Primitives.Popup>(scene.AddMenuPopup);
            Assert.True(popup.IsOpen);
            Assert.Same(add, popup.PlacementTarget);
            Assert.Same(scene.ComposerView, popup.Parent);
            var popupHost = Assert.IsType<HavenSceneControl>(scene.AddMenuPopupHost);
            Assert.Same(scene.AddMenuPrefab, popupHost.Root);
            Assert.InRange(popupHost.SurfaceMetrics.Viewport.Height, 1, window.ClientSize.Height);
        });

    [AvaloniaFact]
    public Task Chat_message_action_controls_are_compact_more_icons_with_accessible_names() =>
        InComposerSceneAsync((scene, _, window) =>
        {
            var userId = Guid.NewGuid();
            var assistantId = Guid.NewGuid();
            scene.SyncMessages([
                new ChatSceneMessage(userId, MessageRole.User, "Hello", "", false, ""),
                new ChatSceneMessage(assistantId, MessageRole.Assistant, "Hi", "Haven", false, "")
            ]);
            window.UpdateLayout();

            var userMenu = scene.Messages.GetItem(userId.ToString("N")).GetComponent<Haven.UI.Components.Button>("MessageMenu");
            var assistantMenu = scene.Messages.GetItem(assistantId.ToString("N")).GetComponent<Haven.UI.Components.Button>("MessageMenu");
            Assert.Equal(string.Empty, userMenu.Content);
            Assert.Equal(string.Empty, assistantMenu.Content);
            Assert.Equal("more", userMenu.IconKey);
            Assert.Equal("more", assistantMenu.IconKey);
            Assert.Equal("Message actions", userMenu.Accessibility.AccessibleName);
            Assert.Equal("Response actions", assistantMenu.Accessibility.AccessibleName);
            Assert.InRange(userMenu.Bounds.Width, 31.5, 32.5);
            Assert.InRange(assistantMenu.Bounds.Width, 31.5, 32.5);
            Assert.True(new HavenSceneRenderer().Render(scene.Root).OfType<HavenIconCommand>().Count(command => command.Key == "more") >= 2);
        });

    [AvaloniaFact]
    public Task Choice_prompt_uses_bounded_raised_card_over_overlay_scrim() =>
        InComposerSceneAsync((scene, _, window) =>
        {
            scene.ShowChoicePrompt("Resolve Problems", "Choose what is going wrong.",
            [
                ("Hallucinating", () => { }),
                ("Looping", () => { }),
                ("Something Else", () => { })
            ]);
            window.UpdateLayout();
            var overlay = scene.Root.DescendantsAndSelf().OfType<Container>().Single(element => element.Name == "ChatModalOverlay");
            var card = scene.Root.DescendantsAndSelf().OfType<Container>().Single(element => element.Name == "ChatModalCard");
            Assert.Equal("Overlay", overlay.GetValue(HavenProperties.Background));
            Assert.Equal("SurfaceRaised", card.GetValue(HavenProperties.Background));
            Assert.InRange(card.Bounds.Width, 300, 420.5);
            Assert.True(card.Bounds.Height < overlay.Bounds.Height * .84);
            Assert.Equal(1d, card.GetValue(HavenProperties.BorderWidth).Value, 3);
            Assert.Equal("Card", card.GetValue(HavenProperties.Shadow));
            Assert.Equal("Cancel", Assert.IsType<Haven.UI.Components.Button>(card.Children.Last()).Content);
        });

    [AvaloniaFact]
    public Task Manage_responses_shows_the_live_per_chat_response_state() =>
        InComposerSceneAsync((scene, _, _) =>
        {
            scene.SetResponseState("Research Agent", ChatActionMode.AllowAllActions, GenerativeUiResponseMode.AlwaysVisual);
            scene.ShowAddMenu();

            var summary = scene.AddMenuPrefab.GetComponent<Haven.UI.Components.Text>("CurrentResponseState");
            Assert.Equal("Research Agent · All actions · Always visual", summary.Content);
            var specialists = scene.AddMenuPrefab.GetComponent<Haven.UI.Components.Button>("Agents");
            Assert.Equal("Specialists", specialists.Content);
            Assert.False(specialists.State.HasFlag(HavenElementState.Expanded));
            Assert.Equal(
                "Collapsed. Browse context for this chat. Browsing does not run tools or change permissions.",
                specialists.Accessibility.Description);
            var categoryDescription = specialists.Accessibility.Description;
            scene.SetResponseState("Review Agent", ChatActionMode.AllowBasicActions, GenerativeUiResponseMode.Auto);
            Assert.Equal("Review Agent · Basic actions · Auto", summary.Content);
            Assert.Equal(categoryDescription, specialists.Accessibility.Description);
            Assert.False(specialists.State.HasFlag(HavenElementState.Expanded));
            Assert.Equal(HavenVisibility.Visible, scene.AddOverlay.GetValue(HavenProperties.Visibility));
        });

    [AvaloniaFact]
    public Task Message_three_dot_opens_detached_popup_without_changing_message_height() =>
        InComposerSceneAsync((scene, _, window) =>
        {
            var userId = Guid.NewGuid();
            ChatMessageActionRequest? request = null;
            scene.MessageActionRequested += (_, value) => request = value;
            scene.SyncMessages([new ChatSceneMessage(userId, MessageRole.User, "Hello", string.Empty, false, string.Empty)]);
            var item = scene.Messages.GetItem(userId.ToString("N"));
            var menu = item.GetComponent<Haven.UI.Components.Button>("MessageMenu");
            Assert.DoesNotContain(item.DescendantsAndSelf(), element => element.Name == "Actions");
            window.UpdateLayout();
            var height = item.Bounds.Height;
            var router = new HavenInputRouter(scene.Root);
            Click(router, menu);
            window.UpdateLayout();
            Assert.Equal(height, item.Bounds.Height, 3);
            var popup = Assert.Single(scene.Root.Children.OfType<PopupMenu>());
            Assert.DoesNotContain(item.DescendantsAndSelf(), element => element is PopupMenu);
            var copy = popup.Card.DescendantsAndSelf().OfType<Haven.UI.Components.Button>().Single(button => button.Content == "Copy");
            Click(router, copy);
            window.UpdateLayout();
            Assert.NotNull(request);
            Assert.Equal(userId, request!.MessageId);
            Assert.Equal(ChatMessageAction.Copy, request.Action);
            Assert.Empty(scene.Root.Children.OfType<PopupMenu>());
        });

    [AvaloniaFact]
    public Task Resolve_problems_uses_detached_shared_popup_and_dismisses_after_selection() =>
        InComposerSceneAsync((scene, _, window) =>
        {
            var selected = string.Empty;
            window.UpdateLayout();
            scene.ShowResolveProblemsMenu(
                "Resolve Problems",
                "Choose a recovery.",
                [("Hallucinating", () => selected = "hallucinating"), ("Looping", () => selected = "looping")]);
            window.UpdateLayout();

            var popup = Assert.Single(scene.Root.Children.OfType<PopupMenu>());
            Assert.DoesNotContain(scene.Root.Children, child => child.Name == "ChatModalOverlay");
            Assert.Equal("Resolve Problems", popup.Card.Accessibility.AccessibleName);
            var looping = popup.Card.DescendantsAndSelf().OfType<Haven.UI.Components.Button>().Single(button => button.Content == "Looping");
            var router = new HavenInputRouter(scene.Root);
            Click(router, looping);
            window.UpdateLayout();

            Assert.Equal("looping", selected);
            Assert.Empty(scene.Root.Children.OfType<PopupMenu>());
        });

    [AvaloniaFact]
    public Task Message_secondary_choices_use_detached_popup_anchored_to_the_message() =>
        InComposerSceneAsync((scene, _, window) =>
        {
            var messageId = Guid.NewGuid();
            var selected = false;
            scene.SyncMessages([new ChatSceneMessage(messageId, MessageRole.User, "Hello", string.Empty, false, string.Empty)]);
            window.UpdateLayout();
            scene.ShowMessageChoiceMenu(messageId, "Branch message", [("Branch in new chat", () => selected = true)]);
            window.UpdateLayout();

            var popup = Assert.Single(scene.Root.Children.OfType<PopupMenu>());
            Assert.DoesNotContain(scene.Root.Children, child => child.Name == "ChatModalOverlay");
            Assert.Equal("Branch message", popup.Card.Accessibility.AccessibleName);
            var choice = popup.Card.DescendantsAndSelf().OfType<Haven.UI.Components.Button>().Single(button => button.Content == "Branch in new chat");
            var router = new HavenInputRouter(scene.Root);
            Click(router, choice);
            window.UpdateLayout();

            Assert.True(selected);
            Assert.Empty(scene.Root.Children.OfType<PopupMenu>());
        });

    [AvaloniaFact]
    public Task Interleaved_transcript_updates_preserve_order_identity_disclosures_and_actions() =>
        InComposerSceneAsync((scene, _, window) =>
        {
            static ToolActivity Activity(string title) =>
                new(Guid.NewGuid(), title, "Completed observation", true, TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch);
            static void Press(Haven.UI.Components.Button button)
            {
                var keyboard = Assert.IsAssignableFrom<IHavenKeyboardInputTarget>(button);
                Assert.True(keyboard.KeyDown(new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None)));
                Assert.True(keyboard.KeyUp(new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None)));
            }
            static string ToolId(ToolActivity activity) => "tool-" + activity.Id.ToString("N");
            var firstTool = Activity("First retained tool");
            var removedTool = Activity("Removed tool");
            var secondTool = Activity("Second response tool");
            var first = new ChatSceneMessage(Guid.NewGuid(), MessageRole.Assistant, "First answer", "Haven", false, "",
                [firstTool, removedTool]);
            var second = new ChatSceneMessage(Guid.NewGuid(), MessageRole.Assistant, "Second answer", "Haven", false, "",
                [secondTool]);
            var user = new ChatSceneMessage(Guid.NewGuid(), MessageRole.User, "Question", "", false, "");
            var removedUser = new ChatSceneMessage(Guid.NewGuid(), MessageRole.User, "Old follow-up", "", false, "");
            scene.SyncMessages([user, first, removedUser, second]);
            var firstItem = scene.Messages.GetItem(first.Id.ToString("N"));
            var secondItem = scene.Messages.GetItem(second.Id.ToString("N"));
            var retainedToolItem = scene.Messages.GetItem(ToolId(firstTool));
            var removedToolItem = scene.Messages.GetItem(ToolId(removedTool));
            var removedUserItem = scene.Messages.GetItem(removedUser.Id.ToString("N"));
            var body = firstItem.GetComponent<Markdown>("Body");
            var menu = firstItem.GetComponent<Haven.UI.Components.Button>("MessageMenu");
            var generated = new Container { Name = "RetainedGeneratedResult" };
            var generatedAction = new Haven.UI.Components.Button { Content = "Generated action" };
            var generatedActions = 0;
            generatedAction.Invoked += (_, _) => generatedActions++;
            generated.Add(generatedAction);
            scene.SetGeneratedContent(first.Id, [generated]);
            var generatedHost = firstItem.GetComponent<Container>("GeneratedContent");
            Press(firstItem.GetComponent<Haven.UI.Components.Button>("ActivityToggle"));

            var insertedTool = Activity("Inserted tool");
            var insertedUser = new ChatSceneMessage(Guid.NewGuid(), MessageRole.User, "New follow-up", "", false, "");
            first = first with { ToolActivities = [insertedTool, firstTool] };
            scene.SyncMessages([second, insertedUser, first, user]);
            Assert.Equal(new[]
            {
                second.Id.ToString("N"), ToolId(secondTool), insertedUser.Id.ToString("N"),
                first.Id.ToString("N"), ToolId(insertedTool), ToolId(firstTool), user.Id.ToString("N")
            }, scene.Messages.Items.Select(item => item.InstanceID));
            Assert.True(removedUserItem.IsDeleted);
            Assert.True(removedToolItem.IsDeleted);

            // Update an existing message in the middle, reorder its tools and insert another tool.
            var streamedTool = Activity("Streamed tool");
            first = first with { Content = "First answer continues", IsStreaming = true,
                ToolActivities = [firstTool, streamedTool, insertedTool] };
            scene.UpdateMessage(first);
            Assert.Equal(new[]
            {
                second.Id.ToString("N"), ToolId(secondTool), insertedUser.Id.ToString("N"),
                first.Id.ToString("N"), ToolId(firstTool), ToolId(streamedTool), ToolId(insertedTool), user.Id.ToString("N")
            }, scene.Messages.Items.Select(item => item.InstanceID));
            Assert.Same(firstItem, scene.Messages.GetItem(first.Id.ToString("N")));
            Assert.Same(secondItem, scene.Messages.GetItem(second.Id.ToString("N")));
            Assert.Same(retainedToolItem, scene.Messages.GetItem(ToolId(firstTool)));
            Assert.Same(body, firstItem.GetComponent<Markdown>("Body"));
            Assert.Equal(first.Content, body.Content);
            Assert.Same(generatedHost, firstItem.GetComponent<Container>("GeneratedContent"));
            Assert.Same(generated, Assert.Single(generatedHost.Children));
            Assert.Same(menu, firstItem.GetComponent<Haven.UI.Components.Button>("MessageMenu"));
            foreach (var activity in first.ToolActivities!)
                Assert.Equal(HavenVisibility.Visible, scene.Messages.GetItem(ToolId(activity)).GetValue(HavenProperties.Visibility));
            Assert.Equal(HavenVisibility.Collapsed, scene.Messages.GetItem(ToolId(secondTool)).GetValue(HavenProperties.Visibility));

            // Repeating an already ordered projection must not structurally rebuild the item host.
            var orderedItems = scene.Messages.Items.ToArray();
            var structuralChanges = 0;
            scene.Messages.Invalidated += (_, _) =>
            {
                if (scene.Messages.LastInvalidationKinds.HasFlag(HavenInvalidationKinds.Structure))
                    structuralChanges++;
            };
            scene.SyncMessages([second, insertedUser, first, user]);
            Assert.Equal(0, structuralChanges);
            Assert.Equal(orderedItems.Length, scene.Messages.Items.Count);
            for (var index = 0; index < orderedItems.Length; index++)
                Assert.Same(orderedItems[index], scene.Messages.Items[index]);

            // Retained generated and message controls still dispatch their original semantic actions.
            Press(generatedAction);
            Assert.Equal(1, generatedActions);
            ChatMessageActionRequest? action = null;
            scene.MessageActionRequested += (_, request) => action = request;
            window.UpdateLayout();
            Press(menu);
            var popup = Assert.Single(scene.Root.Children.OfType<PopupMenu>());
            Press(Assert.Single(popup.Card.DescendantsAndSelf().OfType<Haven.UI.Components.Button>(),
                button => button.Content == "Copy"));
            Assert.NotNull(action);
            Assert.Equal(first.Id, action!.MessageId);
            Assert.Equal(ChatMessageAction.Copy, action.Action);
            Assert.Empty(scene.Root.Children.OfType<PopupMenu>());
        });

    [AvaloniaFact]
    public Task Transcript_sync_handles_120_messages_without_losing_dynamic_item_identity() =>
        InComposerSceneAsync((scene, _, _) =>
        {
            var messages = Enumerable.Range(0, 120)
                .Select(index => new ChatSceneMessage(
                    Guid.NewGuid(),
                    index % 2 == 0 ? MessageRole.User : MessageRole.Assistant,
                    $"Message {index}",
                    index % 2 == 0 ? string.Empty : "Haven",
                    false,
                    string.Empty))
                .ToArray();

            scene.SyncMessages(messages);

            Assert.Equal(120, scene.Messages.Items.Count);
            Assert.All(messages, message => Assert.NotNull(scene.Messages.GetItem(message.Id.ToString("N"))));

            var tracked = messages[61];
            var originalItem = scene.Messages.GetItem(tracked.Id.ToString("N"));
            scene.UpdateMessage(tracked with { Content = "Updated at scale" });

            Assert.Equal(120, scene.Messages.Items.Count);
            Assert.Same(originalItem, scene.Messages.GetItem(tracked.Id.ToString("N")));
            Assert.Equal("Updated at scale", originalItem.GetComponent<Markdown>("Body").Content);
        });

    [AvaloniaFact]
    public Task Safety_lock_disables_authoritative_composer_and_consequential_message_actions() =>
        InComposerSceneAsync(async (scene, _, window) =>
        {
            scene.Input.Text = "unsafe continuation";
            scene.SetSending(false, modelAvailable: true);
            scene.SetSafetyLocked(true);
            await PumpComposerAsync(window);
            Assert.True(scene.ComposerSource.IsSafetyLocked);
            Assert.False(ComposerControl<TextBox>(scene, "composer-input").IsEffectivelyEnabled);
            Assert.False(scene.ComposerView.Bindings.IsActionAvailable("composer.send"));
            Assert.False(ComposerControl<NativeButton>(scene, "add-button").IsEffectivelyEnabled);
            Assert.False(scene.ComposerView.Bindings.IsActionAvailable("composer.add"));
            Assert.Equal("unsafe continuation", scene.Input.Text);

            scene.SetSafetyLocked(false);
            scene.SetSending(false, modelAvailable: true);
            await PumpComposerAsync(window);
            Assert.True(ComposerControl<TextBox>(scene, "composer-input").IsEffectivelyEnabled);
            Assert.True(ComposerControl<NativeButton>(scene, "send-button").IsEffectivelyEnabled);
            Assert.True(ComposerControl<NativeButton>(scene, "add-button").IsEffectivelyEnabled);
            Assert.Equal("unsafe continuation", scene.Input.Text);
        });

    private static Task InComposerSceneAsync(Action<ChatHavenScene, HavenSceneControl, Window> scenario) =>
        InComposerSceneAsync((scene, host, window) =>
        {
            scenario(scene, host, window);
            return Task.CompletedTask;
        });

    private static async Task InComposerSceneAsync(Func<ChatHavenScene, HavenSceneControl, Window, Task> scenario)
    {
        ChatHavenScene? scene = null;
        Window? window = null;
        var failures = new List<Exception>();
        try
        {
            var resolver = new ChatGenUiNativeControlResolver();
            scene = new ChatHavenScene(resolver);
            var host = new HavenSceneControl(new HavenDesktopImageResolver(), resolver) { Root = scene.Root };
            window = new Window { Width = 1000, Height = 760, Content = host };
            window.Show();
            await Task.WhenAll(scene.ComposerInitialization, scene.HeaderInitialization);
            await PumpComposerAsync(window);
            await scenario(scene, host, window);
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
        finally
        {
            if (scene is not null)
            {
                try { scene.Dispose(); }
                catch (Exception error) { failures.Add(error); }

                // Dispose requests these closes. Join every real release while the native
                // dispatcher, resolver and window still belong to this Avalonia test.
                var releases = new List<Task>();
                foreach (var release in new Func<Task>[]
                {
                    scene.JoinHeaderCloseAndReleaseAsync,
                    scene.JoinComposerCloseAndReleaseAsync,
                    scene.JoinRecoveryCloseAndReleaseAsync
                })
                {
                    try { releases.Add(release()); }
                    catch (Exception error) { failures.Add(error); }
                }
                var settlement = Task.WhenAll(releases);
                try { await settlement; }
                catch (Exception error) { failures.Add(settlement.Exception ?? error); }
            }
            if (window is not null)
            {
                try { window.Content = null; }
                catch (Exception error) { failures.Add(error); }
                try { window.Close(); }
                catch (Exception error) { failures.Add(error); }
            }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("The scene scenario and its original cleanup did not all settle successfully.", failures);
    }

    private static T ComposerControl<T>(ChatHavenScene scene, string id) where T : Control =>
        Assert.Single(scene.ComposerView.GetVisualDescendants().OfType<T>(), control => AutomationProperties.GetAutomationId(control) == id);

    private static async Task PumpComposerAsync(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
        }
    }

    private static void Click(HavenInputRouter router, HavenElement element)
    {
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
    }
}
