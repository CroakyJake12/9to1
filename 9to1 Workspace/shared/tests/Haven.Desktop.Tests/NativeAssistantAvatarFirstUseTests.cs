#if !ANDROID
using System.Collections;
using System.Diagnostics;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.NativeUI;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [AvaloniaFact]
    public Task Rendered_builtin_avatar_choice_saves_same_identity_and_conversation_then_reopens_on_home_and_work() => RunAsync(async rig =>
    {
        AssistantIdentity? identity = null; Guid conversation = Guid.Empty;
        await WithActualMemorySurface(rig, async (window, surface, controller, unusedMemoryManagement) =>
        {
            await surface.Bindings.DispatchAsync("assistants.create.start", null, Token);
            Assert.True(surface.Bindings.TrySetValue("DraftName", "Fictional icon helper")); await FlushNativeMemoryUi(window);
            Assert.Null(surface.Bindings.Draft!.Configuration.IconResourceId);
            Assert.Contains(VisibleAvatarIcons(window), icon => icon.IconKey == "chat");
            await OpenRenderedAvatarChooser(window, surface);
            var stale = Assert.Single(AvatarChoices(surface), row => row.Id == "code");
            Assert.True(surface.Bindings.TrySetValue("DraftDescription", "Preserve this newer draft"));
            Assert.False(surface.Bindings.TryGetItemValue(stale, "IconKey", out _));
            await surface.Bindings.DispatchAsync("assistants.avatar.select", stale, Token);
            Assert.Null(surface.Bindings.Draft.Configuration.IconResourceId);
            await OpenRenderedAvatarChooser(window, surface);
            await ClickAvatarButton(window, surface, AvatarButton(window, "Code"), () => surface.Bindings.Draft!.Configuration.IconResourceId == "code");
            Assert.Equal("Preserve this newer draft", surface.Bindings.Draft.Configuration.Description);
            Assert.Contains(VisibleAvatarIcons(window), icon => icon.IconKey == "code" && icon.Bounds.Width > 0 && icon.Bounds.Height > 0);
            await surface.Bindings.DispatchAsync("assistants.configuration.save", null, Token);
            identity = controller.Snapshot.SelectedAssistant!.Identity; var firstRevision = controller.Snapshot.SelectedAssistant.Revision;
            Assert.Equal("code", controller.Snapshot.SelectedAssistant.Configuration.IconResourceId);
            await surface.Bindings.DispatchAsync("assistants.configuration.back", null, Token); await FlushNativeMemoryUi(window);
            Assert.Contains(VisibleAvatarIcons(window), icon => icon.IconKey == "code");
            await surface.Bindings.DispatchAsync("assistants.conversation.new", null, Token);
            conversation = controller.Snapshot.ConversationBinding!.Conversation.Id;
            await surface.Bindings.DispatchAsync("assistants.configuration.open", null, Token); await FlushNativeMemoryUi(window);
            await OpenRenderedAvatarChooser(window, surface);
            await ClickAvatarButton(window, surface, AvatarButton(window, "Creative"), () => surface.Bindings.Draft!.Configuration.IconResourceId == "palette");
            await surface.Bindings.DispatchAsync("assistants.configuration.save", null, Token);
            Assert.Equal(identity, controller.Snapshot.SelectedAssistant!.Identity); Assert.True(controller.Snapshot.SelectedAssistant.Revision > firstRevision);
            Assert.Equal(conversation, controller.Snapshot.ConversationBinding!.Conversation.Id);
            await surface.Bindings.DispatchAsync("assistants.configuration.back", null, Token);
            await surface.Bindings.DispatchAsync("assistants.home", null, Token); await FlushNativeMemoryUi(window);
            Assert.Contains(VisibleAvatarIcons(window), icon => icon.IconKey == "palette");
            Assert.Single(controller.Snapshot.Conversations);
        });
        await rig.ReopenAsync();
        await WithActualMemorySurface(rig, async (window, surface, controller, _) =>
        {
            Assert.Contains(VisibleAvatarIcons(window), icon => icon.IconKey == "palette");
            await OpenActualSavedMemoryConversation(surface, identity!, conversation, window);
            Assert.Equal("palette", controller.Snapshot.SelectedAssistant!.Configuration.IconResourceId);
            Assert.Contains(VisibleAvatarIcons(window), icon => icon.IconKey == "palette");
            Assert.Equal(identity, controller.Snapshot.SelectedAssistant.Identity);
            Assert.Equal(conversation, controller.Snapshot.ConversationBinding!.Conversation.Id);
        });
    });

    [AvaloniaFact]
    public Task Unknown_saved_avatar_reference_survives_unrelated_save_and_discarded_builtin_selection_without_asset_access() => RunAsync(async rig =>
    {
        const string original = "den-attachment:retained-fictional-reference";
        var binding = await rig.CreateAsync(new() { Name = "Preserved icon", IconResourceId = original });
        var identity = binding.Definition.Identity; var conversation = binding.Conversation.Id;
        await WithActualMemorySurface(rig, async (window, surface, controller, _) =>
        {
            await OpenActualSavedMemoryConversation(surface, identity, conversation, window);
            await surface.Bindings.DispatchAsync("assistants.configuration.open", null, Token); await FlushNativeMemoryUi(window);
            Assert.True(surface.Bindings.TryGetValue("AvatarResourceStatus", out var reason));
            Assert.Contains("preserved", Assert.IsType<string>(reason));
            Assert.Equal(original, surface.Bindings.Draft!.Configuration.IconResourceId);
            await OpenRenderedAvatarChooser(window, surface);
            await ClickAvatarButton(window, surface, AvatarButton(window, "Writing"), () => surface.Bindings.Draft!.Configuration.IconResourceId == "notes");
            await surface.Bindings.DispatchAsync("assistants.configuration.discard", null, Token);
            Assert.Equal(original, surface.Bindings.Draft.Configuration.IconResourceId);
            Assert.True(surface.Bindings.TrySetValue("DraftDescription", "Only the description changes"));
            await surface.Bindings.DispatchAsync("assistants.configuration.save", null, Token);
            Assert.Equal(original, controller.Snapshot.SelectedAssistant!.Configuration.IconResourceId);
            Assert.Equal(identity, controller.Snapshot.SelectedAssistant.Identity); Assert.Equal(conversation, controller.Snapshot.ConversationBinding!.Conversation.Id);
        });
        await rig.ReopenAsync();
        var persisted = Assert.Single((await rig.Bridge.ListAsync(Token)).Definitions, definition => definition.Identity == identity);
        Assert.Equal(original, persisted.Configuration.IconResourceId); Assert.Equal("Only the description changes", persisted.Configuration.Description);
        // The native renderer is a pure canonical icon lookup. This genuine rig
        // composes no photo/attachment supplier or provider authority.
    });

    private static AssistantsCuiBindings.AvatarRow[] AvatarChoices(AssistantsNativeCuiSurface surface)
    {
        Assert.True(surface.Bindings.TryGetValue("AvatarChoices", out var rows));
        return Assert.IsAssignableFrom<IEnumerable>(rows).Cast<AssistantsCuiBindings.AvatarRow>().ToArray();
    }
    private static AssistantAvatarIcon[] VisibleAvatarIcons(Window window) => window.GetVisualDescendants()
        .OfType<AssistantAvatarIcon>().Where(icon => icon.IsEffectivelyVisible).ToArray();
    private static Button AvatarButton(Window window, string label) => Assert.Single(window.GetVisualDescendants().OfType<Button>(),
        button => button.IsEffectivelyVisible && button.Content as string == label);
    private static Task OpenRenderedAvatarChooser(Window window, AssistantsNativeCuiSurface surface) =>
        ClickAvatarButton(window, surface, Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
            button.IsEffectivelyVisible && AutomationProperties.GetName(button) == "Choose Assistant avatar"), () => AvatarChoices(surface).Length != 0);
    private static async Task ClickAvatarButton(Window window, AssistantsNativeCuiSurface surface, Button button, Func<bool> settled)
    {
        Assert.True(button.IsEnabled); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
            Assert.Null(window.GetVisualDescendants().OfType<CuiSceneHost>().Select(host => host.LastActionFailure).FirstOrDefault(value => value is not null));
            if (settled()) return;
            Assert.False(surface.IsRetiring); await Task.Delay(10, Token);
        }
        throw new TimeoutException("The rendered Assistant icon selection did not settle.");
    }
}
#endif
