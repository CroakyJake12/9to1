using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Media;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces.Home;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

/// <summary>
/// Rendered proof that <c>SpacesHome.cui</c> binds real <see cref="SpacesHomeViewState"/>
/// through <see cref="SpacesHomeCuiBindings"/>. A document that merely parses can still render
/// nothing if a binding path is wrong, so these tests exercise the actual runtime projection.
/// </summary>
public sealed class SpacesHomeCuiRenderingTests
{
    private static SpacesHomeViewState StateWithSelection(SpacesHomeSelection selection) =>
        SpacesHomeViewState.Empty with
        {
            BuiltIns =
            [
                new SpacesHomeItem(SpaceRegistry.ChatSpaceId, "Chat", "General chat", "spaces.chat", true, null, 0, 1, false) { IconKey = "chat" },
                new SpacesHomeItem(SpaceRegistry.StudySpaceId, "Study", "Study space", "spaces.study", true, null, 1, 1, false) { IconKey = "book" }
            ],
            CustomSpaces =
            [
                new SpacesHomeItem(Guid.NewGuid(), "Recipes", "Dinner plans", "spaces.custom.1", false, null, 0, 1, true) { IconKey = "unknown-user-icon" }
            ],
            Selection = selection,
            MoveParentOptions =
            [
                new SpacesHomeMoveCandidate(null, "(no parent)"),
                new SpacesHomeMoveCandidate(SpaceRegistry.ChatSpaceId, "Chat")
            ],
            Revision = 1
        };

    [Fact]
    public async Task Authored_spaces_home_renders_icon_rail_with_accessible_names_and_preserved_actions()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch<bool>(async () =>
        {
            var state = StateWithSelection(new SpacesHomeSelection(
                SpaceRegistry.ChatSpaceId, "Chat", "General chat", true, false, false, null,
                "Default", "Ask", "Conversation", [], [], false, false, false));
            var actions = new List<(string Command, object? Parameter)>();
            var bindings = new SpacesHomeCuiBindings(() => state, (command, parameter, _) =>
            { actions.Add((command, parameter)); return ValueTask.CompletedTask; });

            using var loader = new CuiControlLoader(SpacesHomeCuiDocument.CreateControlRegistry());
            loader.SetBindingContext(bindings);
            loader.SetActionDispatcher(bindings);
            Window? window = null;
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(ReadAuthoredSource());
                Assert.Empty(diagnostics);
                var root = Assert.IsAssignableFrom<Control>(tree);
                loader.WireBindings(root);

                window = new Window { Content = root, Width = 1280, Height = 900 };
                window.Show();
                for (var pass = 0; pass < 3; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }

                var texts = root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                var rendered = string.Join(" | ", texts);
                Assert.Contains("Chat", rendered); // Selected-Space content remains labelled outside the rail.
                Assert.Contains("Spaces", rendered);
                var rail = Assert.Single(root.GetVisualDescendants().OfType<Control>(), control => control.Name == "spaces-sidebar");
                Assert.Equal(72, rail.Bounds.Width);
                Assert.DoesNotContain(rail.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text));
                var buttons = rail.GetVisualDescendants().OfType<Button>().Where(button => button.Name is "first-party-space-open" or "open-custom-space").ToArray();
                Assert.Equal(3, buttons.Length);
                Assert.Equal(new[] { "Open Chat", "Open Study", "Open Recipes" }, buttons.Select(AutomationProperties.GetName));
                Assert.All(buttons, button => Assert.IsType<DrawingImage>(Assert.Single(button.GetVisualDescendants().OfType<Image>()).Source));
                Assert.Equal("Recipes\nDinner plans", ToolTip.GetTip(buttons[2]));
                Assert.Equal(true, CuiPropertyPresentation.For(buttons[0]).Resolve("Selected").Value);
                Assert.Equal(false, CuiPropertyPresentation.For(buttons[2]).Resolve("Selected").Value);
                buttons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync();
                Assert.Equal(("spaces.select", (object?)state.CustomSpaces[0].Id), Assert.Single(actions));
                Assert.Contains(root.GetVisualDescendants().OfType<TextBox>(), input => input.Name == "spaces-search");
                Assert.DoesNotContain(rail.GetVisualDescendants().OfType<TextBox>(), input => input.Name == "spaces-search");
                Assert.DoesNotContain("Create a Space to keep related conversations and work together", texts);
                Assert.Contains("No sources added", texts);
                Assert.Contains("No extra capabilities enabled", texts);

                // The same authored surface must restore its empty label when the real
                // projection becomes empty, rather than permanently suppressing it.
                state = state with { CustomSpaces = [] };
                bindings.Refresh();
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var emptyTexts = root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                Assert.Contains("Create a Space to keep related conversations and work together", emptyTexts);
                Assert.DoesNotContain(root.GetVisualDescendants().OfType<Button>(), button => button.Name == "open-custom-space");
            }
            finally { window?.Close(); loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, lifetime.Token);
    }

    /// <summary>
    /// The canonical Spaces sidebar is contextual to the selected Space: its header displays that
    /// Space and switches Spaces, an adjacent overflow holds that Space's quick actions, and the
    /// whole sidebar collapses. These are the specification's MUST controls, so they are proven on the
    /// authored surface rather than inferred from a view-model flag.
    /// </summary>
    [Fact]
    public async Task Authored_contextual_sidebar_presents_switcher_quick_actions_and_collapse()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch<bool>(async () =>
        {
            var state = StateWithSelection(new SpacesHomeSelection(
                SpaceRegistry.ChatSpaceId, "Chat", "General chat", true, false, false, null,
                "Default", "Ask", "Conversation", [], [], false, false, false));
            var actions = new List<(string Command, object? Parameter)>();
            var bindings = new SpacesHomeCuiBindings(() => state, (command, parameter, _) =>
            { actions.Add((command, parameter)); return ValueTask.CompletedTask; });

            using var loader = new CuiControlLoader(SpacesHomeCuiDocument.CreateControlRegistry());
            loader.SetBindingContext(bindings);
            loader.SetActionDispatcher(bindings);
            Window? window = null;
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(ReadAuthoredSource());
                Assert.Empty(diagnostics);
                var root = Assert.IsAssignableFrom<Control>(tree);
                loader.WireBindings(root);
                window = new Window { Content = root, Width = 1280, Height = 900 };
                window.Show();
                for (var pass = 0; pass < 3; pass++)
                { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }

                Control Named(string id) => Assert.Single(root!.GetVisualDescendants().OfType<Control>(),
                    control => control.Name == id);
                async Task<Button> Press(string id)
                {
                    var button = Assert.IsType<Button>(Named(id));
                    Assert.True(button.IsEnabled, $"{id} must stay actionable for the current selection.");
                    actions!.Clear();
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await loader!.WhenActionsIdleAsync();
                    return button;
                }

                Assert.True(bindings.TryGetValue("spaces.layoutColumns", out var expandedColumns));
                Assert.Equal("72 260 1fr", expandedColumns);

                var switcher = await Press("space-switcher");
                Assert.Equal("Switch Space", AutomationProperties.GetName(switcher));
                Assert.Equal("Chat", Assert.Single(switcher.GetVisualDescendants().OfType<TextBlock>()).Text);
                Assert.Equal(("spaces.toggle-space-switcher", (object?)null), Assert.Single(actions));

                state = state with { SpaceSwitcherOpen = true }; bindings.Refresh();
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var choices = root.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Name is "space-switcher-builtin-row" or "space-switcher-custom-row")
                    .ToArray();
                Assert.Equal(3, choices.Length);
                Assert.Equal(new[] { "Open Chat", "Open Study", "Open Recipes" },
                    choices.Select(AutomationProperties.GetName));

                await Press("space-overflow");
                Assert.Equal(("spaces.toggle-space-overflow", (object?)null), Assert.Single(actions));
                state = state with { SpaceSwitcherOpen = false, SpaceOverflowOpen = true }; bindings.Refresh();
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var quick = root.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Name is "space-overflow-manage" or "space-overflow-rename"
                        or "space-overflow-archive").ToArray();
                Assert.Equal(3, quick.Length);
                // A built-in Space keeps the quick actions visible but not actionable, so the surface
                // stays deterministic instead of silently offering protected operations.
                Assert.All(quick, button => Assert.False(button.IsEnabled));

                await Press("space-sidebar-collapse");
                Assert.Equal(("spaces.toggle-sidebar", (object?)null), Assert.Single(actions));
                state = state with { SidebarCollapsed = true, SpaceOverflowOpen = false }; bindings.Refresh();
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var sidebar = root.GetVisualDescendants().OfType<Control>()
                    .SingleOrDefault(control => control.Name == "space-context-sidebar");
                Assert.True(sidebar is null || !sidebar.IsEffectivelyVisible,
                    "The collapsed contextual sidebar must not be presented.");
                Assert.True(bindings.TryGetValue("spaces.layoutColumns", out var collapsedColumns));
                Assert.Equal("72 0 1fr", collapsedColumns);
                var expand = await Press("space-sidebar-expand");
                Assert.Equal("Expand this Space's sidebar", AutomationProperties.GetName(expand));
                Assert.Equal(("spaces.toggle-sidebar", (object?)null), Assert.Single(actions));
            }
            finally { window?.Close(); loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, lifetime.Token);
    }

    /// <summary>
    /// The contextual sidebar carries the selected Space's declared custom elements. Until the owning
    /// native surface supplies the real extension projection, the authored area must stay empty rather
    /// than invent rows, and its stable-identity markup and command mapping must remain present.
    /// </summary>
    [Fact]
    public async Task Authored_custom_sidebar_area_stays_empty_without_an_extension_projection()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch<bool>(async () =>
        {
            var state = StateWithSelection(new SpacesHomeSelection(
                SpaceRegistry.ChatSpaceId, "Chat", "General chat", true, false, false, null,
                "Default", "Ask", "Conversation", [], [], false, false, false));
            var bindings = new SpacesHomeCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);
            using var loader = new CuiControlLoader(SpacesHomeCuiDocument.CreateControlRegistry());
            loader.SetBindingContext(bindings);
            loader.SetActionDispatcher(bindings);
            Window? window = null;
            try
            {
                var source = ReadAuthoredSource();
                var (tree, diagnostics) = loader.LoadMarkup(source);
                Assert.Empty(diagnostics);
                var root = Assert.IsAssignableFrom<Control>(tree);
                loader.WireBindings(root);
                window = new Window { Content = root, Width = 1280, Height = 900 };
                window.Show();
                for (var pass = 0; pass < 3; pass++)
                { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }

                Assert.DoesNotContain(root.GetVisualDescendants().OfType<Button>(),
                    button => button.Name == "open-space-sidebar-item");
                Assert.Contains("<Repeat id=\"space-sidebar-items\"", source);
                Assert.Contains("Key=\"{Binding item.sidebarItemId}\"", source);
                Assert.Contains("<Action name=\"OpenSidebarItem\" command=\"spaces.open-sidebar-item\"", source);
                Assert.True(bindings.TryGetValue("spaces.sidebarItems", out var items));
                Assert.Empty((IEnumerable<SpacesHomeSidebarRow>)items!);
            }
            finally { window?.Close(); loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, lifetime.Token);
    }

    [Fact]
    public async Task Selection_details_render_context_references_and_capabilities()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch<bool>(async () =>
        {
            var contextId = Guid.NewGuid();
            var state = StateWithSelection(new SpacesHomeSelection(
                SpaceRegistry.StudySpaceId, "Study", "Study space", true, false, false, null,
                "model-a", "Ask", "Space",
                [new SpacesHomeContextReference(contextId, "/docs/notes.md", "Read", "Ready")],
                [new SpacesHomeCapability("plugin.analysis", "Plugin:analysis")],
                true, true, false));
            var bindings = new SpacesHomeCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);

            using var loader = new CuiControlLoader(SpacesHomeCuiDocument.CreateControlRegistry());
            loader.SetBindingContext(bindings);
            loader.SetActionDispatcher(bindings);
            Window? window = null;
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(ReadAuthoredSource());
                Assert.Empty(diagnostics);
                var root = Assert.IsAssignableFrom<Control>(tree);
                loader.WireBindings(root);

                window = new Window { Content = root, Width = 1280, Height = 900 };
                window.Show();
                for (var pass = 0; pass < 3; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }

                var texts = root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                var rendered = string.Join(" | ", texts);
                Assert.Contains("/docs/notes.md", rendered);
                Assert.Contains("Ready", rendered);
                Assert.Contains("Plugin:analysis", rendered);
                Assert.Contains("model-a", rendered);
                Assert.DoesNotContain("No sources added", texts);
                Assert.DoesNotContain("No extra capabilities enabled", texts);
                Assert.DoesNotContain("Create a Space to keep related conversations and work together", texts);
            }
            finally { window?.Close(); loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, lifetime.Token);
    }

    [Fact]
    public async Task An_unauthored_command_is_refused_rather_than_dispatched()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch<bool>(async () =>
        {
            var state = StateWithSelection(new SpacesHomeSelection(
                SpaceRegistry.ChatSpaceId, "Chat", "General chat", true, false, false, null,
                "Default", "Ask", "Conversation", [], [], false, false, false));
            var dispatched = new List<string>();
            var bindings = new SpacesHomeCuiBindings(
                () => state,
                (command, _, _) => { dispatched.Add(command); return ValueTask.CompletedTask; });

            await bindings.DispatchAsync("spaces.create", null);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => bindings.DispatchAsync("spaces.explode", null).AsTask());

            Assert.Equal(["spaces.create"], dispatched);
            Assert.Null(bindings.IsActionAvailable("spaces.explode"));
            return true;
        }, lifetime.Token);
    }

    [Fact]
    public void Built_in_selection_disables_edit_actions()
    {
            var state = StateWithSelection(new SpacesHomeSelection(
                SpaceRegistry.ChatSpaceId, "Chat", "General chat", true, false, false, null,
                "Default", "Ask", "Conversation", [], [], false, false, false));
        var bindings = new SpacesHomeCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);

        Assert.False(bindings.IsActionAvailable("spaces.rename"));
        Assert.False(bindings.IsActionAvailable("spaces.move"));
        Assert.False(bindings.IsActionAvailable("spaces.archive"));
        Assert.True(bindings.IsActionAvailable("spaces.fork"));
    }

    [Fact]
    public void Custom_selection_enables_edit_actions()
    {
        var customId = Guid.NewGuid();
            var state = StateWithSelection(new SpacesHomeSelection(
                customId, "Recipes", "Dinner plans", false, true, false, null,
                "Default", "Ask", "Conversation", [], [], true, false, true));
        var bindings = new SpacesHomeCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);

        Assert.True(bindings.IsActionAvailable("spaces.rename"));
        Assert.True(bindings.IsActionAvailable("spaces.move"));
        Assert.True(bindings.IsActionAvailable("spaces.archive"));
        Assert.True(bindings.IsActionAvailable("spaces.fork"));
        Assert.True(bindings.IsActionAvailable("spaces.add-context"));
    }

    [Fact]
    public void Bindings_resolve_repeat_item_properties()
    {
        var item = new SpacesHomeItem(Guid.NewGuid(), "Alpha", "Summary", "spaces.custom.a", false, null, 0, 1, true) { IconKey = "book" };
        var state = SpacesHomeViewState.Empty with { CustomSpaces = [item] };
        var bindings = new SpacesHomeCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);

        Assert.True(bindings.TryGetItemValue(item, "id", out var id));
        Assert.Equal(item.Id, id);
        Assert.True(bindings.TryGetItemValue(item, "name", out var name));
        Assert.Equal("Alpha", name);
        Assert.True(bindings.TryGetItemValue(item, "canEdit", out var canEdit));
        Assert.True((bool)canEdit!);
        Assert.True(bindings.TryGetItemValue(item, "iconSource", out var source));
        Assert.Equal("icon:book", source);
        Assert.True(bindings.TryGetItemValue(item, "tooltip", out var tip));
        Assert.Equal("Alpha\nSummary", tip);
        var malformed = item with { IconKey = "not/a/url" };
        state = state with { CustomSpaces = [malformed] }; bindings.Refresh();
        Assert.True(bindings.TryGetItemValue(malformed, "iconSource", out source));
        Assert.Equal("icon:unknown", source);
        Assert.Equal("not/a/url", malformed.IconKey); // Projection never rewrites saved custom configuration.
        Assert.False(bindings.TryGetItemValue(item, "unknown", out _));
    }

    [Fact]
    public void Bindings_resolve_context_reference_and_capability_properties()
    {
        var reference = new SpacesHomeContextReference(Guid.NewGuid(), "/file.txt", "Read", "Ready");
        var capability = new SpacesHomeCapability("skill.x", "Skill:x");
            var state = StateWithSelection(new SpacesHomeSelection(
                Guid.NewGuid(), "Custom", "Desc", false, true, false, null,
                "Default", "Ask", "Conversation",
                [reference],
                [capability],
                true, false, true));
        var bindings = new SpacesHomeCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);

        Assert.True(bindings.TryGetItemValue(reference, "label", out var label));
        Assert.Equal("/file.txt", label);
        Assert.True(bindings.TryGetItemValue(reference, "permissionLabel", out var permission));
        Assert.Equal("Read", permission);

        Assert.True(bindings.TryGetItemValue(capability, "id", out var capId));
        Assert.Equal("skill.x", capId);
        Assert.True(bindings.TryGetItemValue(capability, "label", out var capLabel));
        Assert.Equal("Skill:x", capLabel);
    }

    [Fact]
    public void Query_write_is_dispatched_and_republishes()
    {
        var state = SpacesHomeViewState.Empty;
        var dispatched = new List<(string Command, object? Parameter)>();
        var bindings = new SpacesHomeCuiBindings(
            () => state,
            (command, parameter, _) => { dispatched.Add((command, parameter)); return ValueTask.CompletedTask; });

        Assert.True(bindings.TrySetValue("spaces.query", "find"));

        Assert.Single(dispatched);
        Assert.Equal("spaces.set-query", dispatched[0].Command);
        Assert.Equal("find", dispatched[0].Parameter);
    }

    [Fact]
    public async Task Authored_details_action_is_distinct_targeted_and_hidden_without_host_availability()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch<bool>(async () =>
        {
            var id = Guid.NewGuid();
            var selection = new SpacesHomeSelection(id, "Details target", "Description", false, true, false, null,
                "Default", "Ask", "Conversation", [], [], true, false, true) { CanEditDetails = true };
            var state = StateWithSelection(selection);
            var actions = new List<(string Command, object? Parameter)>();
            var bindings = new SpacesHomeCuiBindings(() => state, (command, parameter, _) =>
            { actions.Add((command, parameter)); return ValueTask.CompletedTask; });
            using var loader = new CuiControlLoader(SpacesHomeCuiDocument.CreateControlRegistry());
            loader.SetBindingContext(bindings);
            loader.SetActionDispatcher(bindings);
            Window? window = null;
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(ReadAuthoredSource());
                Assert.Empty(diagnostics);
                var root = Assert.IsAssignableFrom<Control>(tree);
                loader.WireBindings(root);
                window = new Window { Content = root, Width = 1280, Height = 900 };
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var details = Assert.Single(root.GetVisualDescendants().OfType<Button>(), button => button.Name == "edit-space-details");
                Assert.True(details.IsEnabled);
                Assert.True(bindings.IsActionAvailable("spaces.edit-details"));
                details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync();
                Assert.Equal(("spaces.edit-details", (object?)id), Assert.Single(actions));
                Assert.Contains(root.GetVisualDescendants().OfType<Button>(), button => button.Name == "edit-graph");
                Assert.True(bindings.IsActionAvailable("spaces.edit-graph"));

                state = state with { Selection = selection with { CanEditDetails = false } };
                bindings.Refresh(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.False(bindings.IsActionAvailable("spaces.edit-details"));
                Assert.DoesNotContain(root.GetVisualDescendants().OfType<Button>(), button => button.Name == "edit-space-details");
                Assert.True(bindings.IsActionAvailable("spaces.edit-graph"));
            }
            finally { window?.Close(); loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, lifetime.Token);
    }

    private static string ReadAuthoredSource()
    {
        var assembly = typeof(SpacesHomeCuiBindings).Assembly;
        using var stream = assembly.GetManifestResourceStream(SpacesHomeCuiDocument.ResourceName)
            ?? throw new InvalidOperationException($"'{SpacesHomeCuiDocument.ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
