using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Desktop.Views.Shell.NativePresentation;

namespace Haven.Desktop.Tests;

/// <summary>Explicit presentation-owner fixtures. These do not attest a profile,
/// persistence backend, native readiness or a running conversation service.</summary>
public sealed class NativeChatSidebarCuiAdapterTests
{
    [AvaloniaFact]
    public async Task Actual_CUI_row_keeps_control_identity_and_dispatches_the_current_canonical_conversation()
    {
        var id = Guid.NewGuid(); var opened = new List<Guid>();
        await using var view = new NativeChatSidebarCuiAdapter(Actions() with
        {
            Conversation = request => { opened.Add(request.ConversationId); return Task.CompletedTask; }
        });
        view.SetRows([], [], [], [], [new(ChatSidebarEntryKind.Conversation, id, "Before", false, false, false)]);
        var window = new Window { Width = 390, Height = 700, Content = view.Root };
        try
        {
            window.Show(); window.UpdateLayout();
            var first = Find<Button>(view.Root, "sidebar-entry-open");
            view.SetRows([], [], [], [], [new(ChatSidebarEntryKind.Conversation, id, "After", true, false, false)]);
            window.UpdateLayout();
            Assert.Same(first, Find<Button>(view.Root, "sidebar-entry-open"));
            first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.WhenActionsIdleAsync();
            Assert.Equal([id], opened);
            Assert.Contains(first.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "After");
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Close_waits_for_the_actual_navigation_owner_and_seals_late_invocations()
    {
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var view = new NativeChatSidebarCuiAdapter(Actions() with { NewChat = () => { calls++; return reply.Task; } });
        var command = view.DispatchAsync("nav.new-chat", null).AsTask();
        Assert.Equal(1, calls); Assert.False(command.IsCompleted);
        var close = view.CloseAsync();
        Assert.False(close.IsCompleted);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => view.DispatchAsync("nav.new-chat", null).AsTask());
        reply.SetResult(); await command; await close;
        Assert.Equal(1, calls); Assert.Same(close, view.OriginalClose);
    }

    [AvaloniaFact]
    public async Task Collapsing_uses_real_owner_ack_without_reopening_or_mutating_a_conversation()
    {
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0; var changes = new List<bool>();
        await using var view = new NativeChatSidebarCuiAdapter(Actions() with
        {
            NewChat = () => { starts++; return Task.CompletedTask; },
            SetSidebarCollapsed = collapsed => { changes.Add(collapsed); return reply.Task; }
        });
        view.SetNavigationSnapshot([new(SpaceRegistry.ChatSpaceId, "Chat", "chat", true, "")], NativeSpaceSwitcherLayout.Rail, false);
        var command = view.DispatchAsync("nav.toggle-sidebar", null).AsTask();
        Assert.Equal(350, view.PreferredWidth); Assert.Equal([true], changes);
        reply.SetResult(); await command;
        Assert.Equal(70, view.PreferredWidth); Assert.Equal(0, starts);
        Assert.True(view.TryGetValue("layout.railVisible", out var rail)); Assert.Equal(true, rail);
    }

    [AvaloniaFact]
    public async Task Real_text_input_and_confirmation_wait_for_the_same_prompt_owner()
    {
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? received = null;
        await using var view = new NativeChatSidebarCuiAdapter(Actions());
        view.ShowTextPrompt("Rename chat", "Old", "Save", value => { received = value; return reply.Task; });
        var window = new Window { Width = 390, Height = 700, Content = view.Root };
        try
        {
            window.Show(); window.UpdateLayout();
            var input = Find<TextBox>(view.Root, "sidebar-dialog-text");
            input.Focus(); input.SelectAll(); window.KeyTextInput("Canonical title");
            var command = view.DispatchAsync("nav.confirm", null).AsTask();
            Assert.Equal("Canonical title", received); Assert.False(command.IsCompleted);
            view.TryGetValue("dialog.visible", out var before); Assert.Equal(true, before);
            reply.SetResult(); await command;
            view.TryGetValue("dialog.visible", out var after); Assert.Equal(false, after);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_late_prompt_reply_cannot_close_a_new_contexts_prompt()
    {
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var view = new NativeChatSidebarCuiAdapter(Actions());
        view.SetSpaceScope("First", Guid.NewGuid());
        view.ShowTextPrompt("First prompt", "one", "Save", _ => reply.Task);
        var command = view.DispatchAsync("nav.confirm", null).AsTask();
        view.SetSpaceScope("Second", Guid.NewGuid());
        view.ShowTextPrompt("Second prompt", "two", "Save", _ => Task.CompletedTask);
        reply.SetResult(); await command;
        view.TryGetValue("dialog.visible", out var visible); Assert.Equal(true, visible);
        view.TryGetValue("dialog.title", out var title); Assert.Equal("Second prompt", title);
    }

    [AvaloniaFact]
    public async Task Completed_navigation_does_not_exhaust_a_long_lived_presentation()
    {
        var count = 0;
        await using var view = new NativeChatSidebarCuiAdapter(Actions() with
        {
            NewChat = () => { count++; return Task.CompletedTask; }
        });
        for (var i = 0; i < 300; i++) await view.DispatchAsync("nav.new-chat", null);
        Assert.Equal(300, count);
    }

    [AvaloniaFact]
    public async Task Reentrant_close_observer_receives_the_same_original_close_task()
    {
        var view = new NativeChatSidebarCuiAdapter(Actions());
        Task? reentrant = null;
        view.PropertyChanged += (_, _) => reentrant = view.CloseAsync();
        var close = view.CloseAsync();
        Assert.Same(close, reentrant);
        await close;
    }

    [AvaloniaFact]
    public async Task A_navigation_owner_cannot_join_its_own_enclosing_close()
    {
        NativeChatSidebarCuiAdapter? view = null;
        var refused = false;
        view = new NativeChatSidebarCuiAdapter(Actions() with
        {
            NewChat = () =>
            {
                Assert.Throws<InvalidOperationException>((Action)(() => { _ = view!.CloseAsync(); }));
                refused = true;
                return Task.CompletedTask;
            }
        });
        await view.DispatchAsync("nav.new-chat", null);
        Assert.True(refused);
        await view.CloseAsync();
    }

    [AvaloniaFact]
    public async Task Completed_original_context_does_not_refuse_a_later_external_close()
    {
        ExecutionContext? captured = null;
        var view = new NativeChatSidebarCuiAdapter(Actions() with
        {
            NewChat = () => { captured = ExecutionContext.Capture(); return Task.CompletedTask; }
        });
        var original = view.DispatchAsync("nav.new-chat", null).AsTask();
        await original;
        Assert.True(original.IsCompletedSuccessfully);
        Assert.NotNull(captured);
        Task? close = null;
        ExecutionContext.Run(captured!, _ => close = view.CloseAsync(), null);
        Assert.Same(view.OriginalClose, close);
        await close!;
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    private static NativeChatSidebarCuiActions Actions() => new(
        _ => { }, () => Task.CompletedTask, () => { }, () => Task.CompletedTask,
        _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask,
        _ => Task.CompletedTask, () => Task.CompletedTask, _ => Task.CompletedTask);
}
