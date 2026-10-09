using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Memory;
using HavenOS.Apps.Assistants.Migration;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

[Collection("Assistants native UI")]
public sealed class AssistantsMemoryMigrationRepeatRenderingTests
{
    private static readonly List<(Window Window, CuiControlLoader Loader, IReadOnlyList<Exception> Failures)> Retained = [];
    [Fact]
    public async Task Actual_memory_scene_renders_both_rows_and_second_click_delivers_same_current_record()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        var completed = await native.Dispatch<bool>(async () =>
        {
            object? selected = null; var dispatches = 0;
            var bindings = new AssistantsMemoryCuiBindings(true, "", (command, value, _) =>
            { Assert.Equal("assistants.memory.correct", command); dispatches++; selected = value; return ValueTask.CompletedTask; }, action => action(), () => true);
            bindings.SetRevisionSupport(true);
            var now = DateTimeOffset.UtcNow;
            var definition = new AssistantDefinitionSnapshot(new("fixture-den", "personal", "fixture-assistant"), 1,
                ConfiguredIdentityKind.Assistant, new() { Name = "Fictional helper" }, []);
            var binding = new AssistantConversationBinding(new object(), definition, "fixture-session", 1,
                new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Display fixture", null, null, false, false, now, now));
            KnowledgeRecord Record(string title) => new(Guid.NewGuid(), KnowledgeCategory.LearnMe, "Preference", title,
                title + " body", KnowledgePrivacyClass.Private, 1, false, now, now, null, "Display fixture only", []);
            var first = Record("First scoped preference"); var second = Record("Second scoped preference");
            bindings.SetView(new(new object(), binding, new DisplayInput(), [first, second], "Display only"));
            Assert.True(bindings.TryGetValue("MemoryRecords", out var observed));
            var rows = Assert.IsAssignableFrom<IReadOnlyList<AssistantsMemoryCuiBindings.MemoryRow>>(observed);
            Assert.True(bindings.TryGetItemValue(rows[1], "Id", out var key)); Assert.Equal(second.Id, key);
            Assert.False(bindings.TryGetItemValue(new AssistantsMemoryCuiBindings.MemoryRow(second), "Title", out _));
            await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("assistants.memory.correct",
                new AssistantsMemoryCuiBindings.MemoryRow(second), CancellationToken.None).AsTask());
            Assert.Equal(0, dispatches);
            await Render(AssistantsCuiScene.Memory, bindings, bindings, async (window, loader) =>
            {
                var texts = window.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
                Assert.Contains(first.Title, texts); Assert.Contains(second.Title, texts); Assert.Contains(second.Summary, texts);
                var buttons = window.GetLogicalDescendants().OfType<Button>().Where(button => button.Content as string == "Correct this memory").ToArray();
                Assert.Equal(2, buttons.Length); Assert.True(buttons[1].IsEnabled);
                buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                Assert.Same(rows[1], selected); Assert.Same(second, Assert.IsType<AssistantsMemoryCuiBindings.MemoryRow>(selected).Original);
                bindings.SetBusy(true); Assert.True(bindings.TryGetItemValue(rows[1], "Title", out _));
                bindings.SetView(new(new object(), binding, new DisplayInput(), [first], "Fresh view"));
                Assert.False(bindings.TryGetItemValue(rows[1], "Title", out _));
                bindings.SetBusy(false);
                await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("assistants.memory.reject", rows[1], CancellationToken.None).AsTask());
                Assert.Equal(1, dispatches);
                bindings.Revoke(); Assert.False(bindings.TryGetItemValue(rows[0], "Title", out _));
            });
            return true;
        }, CancellationToken.None);
        Assert.True(completed);
    }

    [Fact]
    public async Task Actual_legacy_scene_renders_current_names_notes_links_and_delivers_selected_original_row()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        var completed = await native.Dispatch<bool>(async () =>
        {
            object? selected = null; var dispatches = 0;
            var bindings = new AssistantsLegacyMigrationCuiBindings(true, "", (command, value, _) =>
            { Assert.Equal("assistants.legacy.preview", command); dispatches++; selected = value; return ValueTask.CompletedTask; }, action => action(), () => true);
            var first = new LegacyAgentMigrationItem(Guid.NewGuid(), "First existing identity", "First description", true, false, LegacyAgentMigrationState.Unselected);
            var second = first with { LegacyAgentId = Guid.NewGuid(), Name = "Second existing identity" };
            bindings.SetPage(new([first, second], null));
            Assert.True(bindings.TryGetValue("MigrationRows", out var observed));
            var rows = Assert.IsAssignableFrom<IReadOnlyList<AssistantsLegacyMigrationCuiBindings.LegacyRow>>(observed);
            Assert.True(bindings.TryGetItemValue(rows[1], "Id", out var key)); Assert.Equal(second.LegacyAgentId, key);
            await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("assistants.legacy.preview",
                new AssistantsLegacyMigrationCuiBindings.LegacyRow(second), CancellationToken.None).AsTask());
            Assert.Equal(0, dispatches);
            await Render(AssistantsCuiScene.LegacyMigration, bindings, bindings, async (window, loader) =>
            {
                var texts = window.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
                Assert.Contains(first.Name, texts); Assert.Contains(second.Name, texts);
                var buttons = window.GetLogicalDescendants().OfType<Button>().Where(button => button.Content as string == "Review definition").ToArray();
                Assert.Equal(2, buttons.Length); buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                Assert.Same(rows[1], selected);
            });
            var preview = LegacyAgentMigrationDraftTests.Preview(); bindings.SetPreview(preview);
            bindings.SetLinks("runs", new([new("runs", "fixture-run", "fixture-conversation", "Preserved original run")], null));
            await Render(AssistantsCuiScene.LegacyMigration, bindings, bindings, (window, _) =>
            {
                var texts = window.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
                foreach (var note in preview.CompatibilityNotes) Assert.Contains(note, texts);
                Assert.Contains("Preserved original run", texts); Assert.Contains("fixture-conversation", texts); return Task.CompletedTask;
            });
            bindings.SetPage(new([], null)); Assert.False(bindings.TryGetItemValue(rows[1], "Name", out _));
            await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("assistants.legacy.preview", rows[1], CancellationToken.None).AsTask());
            Assert.Equal(1, dispatches);
            Assert.True(bindings.TryGetValue("MigrationLinks", out var cleared)); Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<AssistantsLegacyMigrationCuiBindings.LinkRow>>(cleared));
            bindings.Revoke(); Assert.False(bindings.TryGetItemValue(rows[0], "Id", out _));
            return true;
        }, CancellationToken.None);
        Assert.True(completed);
    }
    private static async Task Render(AssistantsCuiScene scene, ICuiBindingContext bindings, ICuiActionDispatcher dispatch,
        Func<Window, CuiControlLoader, Task> body)
    {
        var loader = new CuiControlLoader(); var window = new Window(); var failures = new List<Exception>();
        try
        {
            loader.SetBindingContext(bindings); loader.SetActionDispatcher(dispatch);
            window.Content = loader.Load(AssistantsCuiScenes.ReadDocument(scene)); window.Show(); window.UpdateLayout();
            await body(window, loader);
        }
        catch (Exception failure) { failures.Add(failure); }
        finally
        {
            try { await loader.WhenActionsIdleAsync(); } catch (Exception failure) { failures.Add(failure); }
            try { loader.Dispose(); } catch (Exception failure) { failures.Add(failure); }
            try { await loader.WhenActionsIdleAsync(); } catch (Exception failure) { failures.Add(failure); }
            if (failures.Count == 0) window.Close();
        }
        if (failures.Count != 0)
        {
            lock (Retained) Retained.Add((window, loader, failures.ToArray()));
            throw new AggregateException("Actual repeated-row control sources retained.", failures);
        }
    }
    private sealed class DisplayInput : IChatOriginalPersistentMemoryInput { }
}
