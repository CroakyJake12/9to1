using System.Collections;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input.Raw;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

// Actual authored CUI/loader/bindings projection and routing controls. Fixtures
// below issue display metadata only; no installed/Home/project/Task grant is tested.
[Collection("Assistants native UI")]
public sealed class AssistantsCuiRepeatedConfigurationRoutingTests
{
    [Fact]
    public async Task Workspace_overlay_resizes_and_owns_only_its_exact_input_and_focus()
    {
        using var lifetime = new CancellationTokenSource();
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        Assert.True(await native.Dispatch<bool>(async () =>
        {
            var bindings = Bindings([]);
            var assistant = Definition();
            bindings.ShowWork();
            var main = new AssistantConversationSummary(Guid.NewGuid(), "Main", DateTimeOffset.UtcNow, false, null) { IsMain = true };
            bindings.ApplySnapshot(AssistantsWorkspaceSnapshot.Empty with
            { Revision = 1, SelectedAssistant = assistant, Conversations = [main] });
            var registry = AssistantsNativeCuiSurface.CreateAvatarRegistry();
            var composer = new TextBox { Text = "Unsent draft" };
            registry.RegisterObjectRenderer("AssistantConversation", _ => composer);
            using var loader = new CuiControlLoader(registry);
            loader.SetBindingContext(bindings); loader.SetActionDispatcher(bindings);
            var window = new Window { Width = 1038, Height = 660 };
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(AssistantsCuiScenes.ReadSource(AssistantsCuiScene.Work));
                Assert.Empty(diagnostics);
                var root = Assert.IsAssignableFrom<Control>(tree);
                loader.WireBindings(root);
                using var keyboard = new AssistantWorkspaceKeyboard(root, bindings);
                window.Content = root; window.Show();
                Button FindButton(string name) => Assert.Single(root.GetVisualDescendants().OfType<Button>(), b => b.Name == name);
                async Task Layout()
                {
                    await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
                    await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
                }
                await Layout();
                var toggle = FindButton("assistant-workspace-toggle");
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync(); await Layout();
                var close = FindButton("assistant-workspace-close");
                Assert.True(close.IsFocused);
                Assert.True(composer.Focus());
                var unrelated = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
                composer.RaiseEvent(unrelated);
                Assert.False(unrelated.Handled);
                Assert.Equal(assistant.Identity, bindings.VisibleWorkspaceIdentity);
                var pane = Assert.Single(root.GetVisualDescendants().OfType<CuiContainerControl>(), c => c.Name == "assistant-work-pane");
                var primary = Assert.Single(root.GetVisualDescendants().OfType<CuiContainerControl>(), c => c.Name == "assistant-work-main");
                var navigation = Assert.Single(root.GetVisualDescendants().OfType<CuiContainerControl>(), c => c.Name == "assistant-navigation");
                Assert.Equal(2, Grid.GetColumn(pane));
                Assert.Equal(218, navigation.Bounds.Width);
                Assert.True(primary.Bounds.Width >= 460);
                Assert.True(primary.IsEffectivelyEnabled);

                window.Width = 940;
                await Layout();
                Assert.Equal(1, Grid.GetColumn(pane));
                Assert.True(navigation.IsEffectivelyVisible); Assert.Equal(218, navigation.Bounds.Width);
                Assert.True(navigation.IsEffectivelyEnabled); Assert.False(bindings.IsAssistantNavigationOpen);
                Assert.False(primary.IsEffectivelyEnabled); Assert.True(close.IsFocused);
                var home = FindButton("work-home"); Assert.True(home.Focus());
                var fromUncoveredNavigation = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
                home.RaiseEvent(fromUncoveredNavigation);
                Assert.False(fromUncoveredNavigation.Handled);
                Assert.Equal(assistant.Identity, bindings.VisibleWorkspaceIdentity);

                window.Width = 500;
                await Layout();
                Assert.Equal(1, Grid.GetColumn(pane));
                Assert.Equal(360, pane.Bounds.Width); Assert.False(navigation.IsEffectivelyVisible);
                Assert.False(primary.IsEffectivelyEnabled); Assert.False(primary.IsHitTestVisible);
                Assert.Same(window, TopLevel.GetTopLevel(composer));
                Assert.True(close.IsFocused); // The previously focused wide navigation is now hidden.
                var reachedAnotherControl = false;
                for (var i = 0; i < 5; i++)
                {
                    window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                    window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                    Assert.True(pane.IsKeyboardFocusWithin);
                    reachedAnotherControl |= !close.IsFocused;
                }
                Assert.True(reachedAnotherControl);
                close.Focus();
                var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
                close.RaiseEvent(escape);
                await Layout();
                Assert.True(escape.Handled); Assert.Null(bindings.VisibleWorkspaceIdentity);
                Assert.True(toggle.IsFocused); Assert.True(primary.IsEffectivelyEnabled); Assert.True(primary.IsHitTestVisible);
                Assert.Same(window, TopLevel.GetTopLevel(composer)); Assert.Equal("Unsent draft", composer.Text);

                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync(); await Layout();
                Assert.True(close.IsFocused); Assert.False(primary.IsEffectivelyEnabled);
                var backdrop = Assert.Single(root.GetVisualDescendants().OfType<CuiContainerControl>(), c => c.Name == "assistant-workspace-backdrop");
                var target = backdrop.TranslatePoint(new Point(20, 30), window)!.Value;
                var releaseAway = backdrop.TranslatePoint(new Point(110, 150), window)!.Value;
                window.MouseDown(target, MouseButton.Left);
                window.MouseMove(releaseAway);
                window.MouseUp(releaseAway, MouseButton.Left);
                Assert.Equal(assistant.Identity, bindings.VisibleWorkspaceIdentity);
                var tapTarget = backdrop.TranslatePoint(new Point(70, 210), window)!.Value;
                Assert.True(backdrop.IsEffectivelyVisible); Assert.NotNull(backdrop.Background);
                var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(tapTarget));
                Assert.Contains(backdrop, hit.GetSelfAndVisualAncestors());
                window.MouseMove(tapTarget);
                window.MouseDown(tapTarget, MouseButton.Left);
                window.MouseUp(tapTarget, MouseButton.Left);
                await Layout();
                Assert.Null(bindings.VisibleWorkspaceIdentity); Assert.True(toggle.IsFocused);
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync(); await Layout();
                window.Width = 1038;
                await Layout();
                Assert.Equal(2, Grid.GetColumn(pane)); Assert.True(primary.IsEffectivelyEnabled);
                Assert.True(close.IsFocused); Assert.Equal("Unsent draft", composer.Text);

                window.Width = 500; await Layout(); Assert.False(primary.IsEffectivelyEnabled);
                bindings.ShowHome(); await Layout();
                Assert.Null(bindings.WorkRouteIdentity); Assert.False(backdrop.IsEffectivelyVisible);
                Assert.True(primary.IsEffectivelyEnabled); Assert.True(primary.IsHitTestVisible);
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); window.Close(); }
            return true;
        }, lifetime.Token));
    }

    [Fact]
    public async Task Assistant_navigation_retains_one_wide_list_and_compact_drawer_with_original_rows_and_draft()
    {
        using var lifetime = new CancellationTokenSource();
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        Assert.True(await native.Dispatch<bool>(async () =>
        {
            var calls = new List<(string Command, object? Parameter)>();
            var bindings = Bindings(calls);
            var assistant = Definition();
            var other = assistant with
            {
                Identity = new(assistant.Identity.DenId, "another-namespace", assistant.Identity.DefinitionId),
                Configuration = assistant.Configuration with { Name = "Second Assistant with a deliberately long descriptive name" }
            };
            AssistantDefinitionSnapshot[] catalogue = [assistant, other, .. Enumerable.Range(0, 16).Select(index => assistant with
            {
                Identity = new(assistant.Identity.DenId, assistant.Identity.NamespaceId, $"catalogue-{index}"),
                Configuration = assistant.Configuration with { Name = $"Catalogue Assistant {index}" }
            })];
            var now = DateTimeOffset.UtcNow;
            const string draft = "Retain this conversation draft";
            var savedConversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
                "Navigation viewport", null, null, false, false, now, now);
            var actualBinding = new AssistantConversationBinding(new object(), assistant, "display-session", 1, savedConversation);
            var data = new AssistantConversationData(savedConversation, [], [], [],
                new(savedConversation.Id, null, draft, "[]", now), null);
            var snapshot = AssistantsWorkspaceSnapshot.Empty with
            { Revision = 1, SelectedAssistant = assistant, Assistants = catalogue, ConversationBinding = actualBinding, Conversation = data };
            bindings.ShowWork(); bindings.ApplySnapshot(snapshot); bindings.SetMessages([]);
            using var conversationLoader = new CuiControlLoader();
            conversationLoader.RegisterObjectRenderer("AssistantMessage", _ => new CuiMarkdownView());
            conversationLoader.SetBindingContext(bindings); conversationLoader.SetActionDispatcher(bindings);
            var (conversationTree, conversationDiagnostics) = conversationLoader.LoadMarkup(AssistantsCuiScenes.ReadSource(AssistantsCuiScene.Conversation));
            Assert.Empty(conversationDiagnostics);
            var conversation = Assert.IsAssignableFrom<Control>(conversationTree); conversationLoader.WireBindings(conversation);
            var composer = Assert.Single(conversation.GetVisualDescendants().OfType<TextBox>(), value => value.Name == "conversation-draft");
            var registry = AssistantsNativeCuiSurface.CreateAvatarRegistry();
            registry.RegisterObjectRenderer("AssistantConversation", _ => conversation);
            using var loader = new CuiControlLoader(registry);
            loader.SetBindingContext(bindings); loader.SetActionDispatcher(bindings);
            var window = new Window { Width = 940, Height = 650 };
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(AssistantsCuiScenes.ReadSource(AssistantsCuiScene.Work));
                Assert.Empty(diagnostics);
                var root = Assert.IsAssignableFrom<Control>(tree);
                loader.WireBindings(root);
                using var keyboard = new AssistantWorkspaceKeyboard(root, bindings);
                window.Content = root; window.Show();
                Button? Visible(string name) => root.GetVisualDescendants().OfType<Button>()
                    .SingleOrDefault(b => b.Name == name && b.IsEffectivelyVisible);
                async Task Layout()
                {
                    await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
                    await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
                }
                async Task Click(string id)
                {
                    Assert.IsType<Button>(Visible(id)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await loader.WhenActionsIdleAsync(); await Layout();
                }
                void InViewport(Control control)
                {
                    var origin = control.TranslatePoint(default, window) ?? throw new InvalidOperationException("Detached navigation control.");
                    Assert.True(control.IsEffectivelyVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0);
                    Assert.True(origin.X >= 0 && origin.Y >= 0 && origin.X + control.Bounds.Width <= window.ClientSize.Width + 1 &&
                        origin.Y + control.Bounds.Height <= window.ClientSize.Height + 1);
                }
                await Layout(); Assert.True(composer.Focus());
                composer.CaretIndex = 24; composer.SelectionStart = 12; composer.SelectionEnd = 24;
                var caret = composer.CaretIndex;
                void Retained()
                {
                    Assert.Same(window, TopLevel.GetTopLevel(conversation));
                    Assert.Same(composer, Assert.Single(conversation.GetVisualDescendants().OfType<TextBox>(), value => value.Name == "conversation-draft"));
                    Assert.Same(actualBinding, bindings.Snapshot.ConversationBinding);
                    Assert.Equal(draft, composer.Text); Assert.Equal(draft, bindings.Prompt);
                    Assert.Equal("conversation", composer.SelectedText); Assert.Equal(caret, composer.CaretIndex);
                    Assert.Equal(12, composer.SelectionStart); Assert.Equal(24, composer.SelectionEnd);
                }
                var navigation = Assert.Single(root.GetVisualDescendants().OfType<CuiContainerControl>(), c => c.Name == "assistant-navigation");
                var primary = Assert.Single(root.GetVisualDescendants().OfType<CuiContainerControl>(), c => c.Name == "assistant-work-main");
                var list = Assert.Single(navigation.GetVisualDescendants().OfType<CuiContainerControl>(), c => c.Name == "assistant-navigation-list");
                var scroll = Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>());
                var rows = root.GetVisualDescendants().OfType<Button>().Where(b => b.Tag as string == "OpenAssistant").ToArray();
                Assert.Equal(catalogue.Length, rows.Length);
                var selected = Assert.Single(rows, b => AutomationProperties.GetName(b) == assistant.Configuration.Name);
                var otherButton = Assert.Single(rows, b => AutomationProperties.GetName(b) == other.Configuration.Name);
                Assert.True(CuiPropertyPresentation.TryGetExisting(selected, out var selectedProperties));
                Assert.True(CuiPropertyPresentation.TryGetExisting(otherButton, out var otherProperties));
                Assert.True(Assert.IsType<bool>(selectedProperties!.Resolve("Selected").Value));
                Assert.False(Assert.IsType<bool>(otherProperties!.Resolve("Selected").Value));
                Assert.True(selected.IsEffectivelyEnabled); // Opening this selected Assistant still routes to its protected Main.
                Assert.Equal(other.Configuration.Name, ToolTip.GetTip(otherButton));
                Assert.Equal(218, navigation.Bounds.Width); Assert.Equal(0, Grid.GetColumn(navigation));
                Assert.False(bindings.IsAssistantNavigationOpen); Assert.Null(bindings.VisibleWorkspaceIdentity);
                Assert.Null(Visible("assistant-navigation-toggle")); Assert.Null(Visible("assistant-navigation-close"));
                Assert.Null(Visible("assistant-navigation-to-workspace")); Assert.Null(Visible("assistant-navigation-home"));
                InViewport(selected); InViewport(otherButton);
                Assert.True(scroll.Viewport.Height > 0 && scroll.Extent.Height > scroll.Viewport.Height);
                var home = Assert.IsType<Button>(Visible("work-home")); var homeBounds = home.Bounds;
                scroll.ScrollToEnd(); await Layout(); Assert.True(scroll.Offset.Y > 0);
                Assert.Equal(homeBounds, home.Bounds); InViewport(home); Retained();
                scroll.ScrollToHome(); await Layout();

                Assert.True(selected.Focus()); window.Width = 750; await Layout();
                Assert.False(navigation.IsEffectivelyVisible);
                Assert.True(Visible("assistant-navigation-toggle")!.IsFocused); Retained();
                window.Width = 360; window.Height = 640; await Layout();
                await Click("assistant-navigation-toggle");
                Assert.Same(navigation, Assert.Single(root.GetVisualDescendants().OfType<CuiContainerControl>(), c => c.Name == "assistant-navigation"));
                Assert.Equal(230, navigation.Bounds.Width); Assert.Equal(1, Grid.GetColumn(navigation));
                Assert.True(bindings.IsAssistantNavigationOpen); Assert.Null(bindings.VisibleWorkspaceIdentity);
                Assert.False(primary.IsEffectivelyEnabled); Assert.False(primary.IsHitTestVisible);
                var close = Assert.IsType<Button>(Visible("assistant-navigation-close")); Assert.True(close.IsFocused); InViewport(close);
                for (var i = 0; i < 6; i++)
                {
                    window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                    window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                    Assert.True(navigation.IsKeyboardFocusWithin);
                }
                scroll.ScrollToEnd(); await Layout(); InViewport(close); Retained();
                var backdrop = Assert.Single(root.GetVisualDescendants().OfType<CuiContainerControl>(), c => c.Name == "assistant-workspace-backdrop");
                var outside = backdrop.TranslatePoint(new Point(backdrop.Bounds.Width - 20, 210), window)!.Value;
                var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(outside));
                Assert.Contains(backdrop, hit.GetSelfAndVisualAncestors()); // Exposed right strip of the left drawer.
                window.MouseMove(outside); window.MouseDown(outside, MouseButton.Left); window.MouseUp(outside, MouseButton.Left);
                await Layout();
                Assert.False(bindings.IsAssistantNavigationOpen); Assert.True(Visible("assistant-navigation-toggle")!.IsFocused);
                Assert.True(primary.IsEffectivelyEnabled); Retained();

                await Click("assistant-workspace-toggle");
                Assert.True(Visible("assistant-workspace-close")!.IsFocused); Assert.Null(Visible("assistant-navigation-close"));
                await Click("assistant-workspace-to-navigation");
                Assert.True(close.IsFocused); Assert.Null(Visible("assistant-workspace-close"));
                Assert.Null(bindings.VisibleWorkspaceIdentity); Assert.True(bindings.IsAssistantNavigationOpen);
                await Click("assistant-navigation-to-workspace");
                Assert.True(Visible("assistant-workspace-close")!.IsFocused); Assert.False(bindings.IsAssistantNavigationOpen);
                await Click("assistant-workspace-to-navigation");
                var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
                close.RaiseEvent(escape); await Layout();
                Assert.True(escape.Handled); Assert.True(Visible("assistant-navigation-toggle")!.IsFocused);
                Assert.Empty(calls); Retained();

                window.Width = 940; await Layout();
                Assert.True(home.IsFocused); Assert.Null(Visible("assistant-navigation-toggle"));
                Assert.Equal(218, navigation.Bounds.Width); Assert.True(primary.IsEffectivelyEnabled);
                Assert.Same(selected, root.GetVisualDescendants().OfType<Button>().Single(b => b.Tag as string == "OpenAssistant" && AutomationProperties.GetName(b) == assistant.Configuration.Name));
                Assert.True(selected.Focus()); window.Width = 500; await Layout();
                await Click("assistant-navigation-toggle"); scroll.ScrollToHome(); await Layout(); Assert.True(otherButton.Focus());
                window.Width = 940; await Layout();
                Assert.True(otherButton.IsFocused); // This same retained row stays visible when the drawer becomes a sidebar.
                Assert.False(bindings.IsAssistantNavigationOpen); Assert.Null(Visible("assistant-navigation-close"));
                Assert.True(primary.IsEffectivelyEnabled); Retained();
                window.Width = 500; await Layout(); await Click("assistant-navigation-toggle");
                Assert.True(close.IsFocused); window.Width = 940; await Layout();
                Assert.True(home.IsFocused); // The compact-only close button became hidden.

                window.Width = 500; await Layout(); await Click("assistant-navigation-toggle");
                scroll.ScrollToHome(); await Layout(); Assert.True(otherButton.Focus()); Retained();
                var currentRow = Assert.Single(Rows(bindings, "Assistants").Cast<AssistantsCuiBindings.AssistantRow>(), row => row.Identity == other.Identity);
                otherButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                var invoked = Assert.Single(calls); Assert.Equal("assistants.open", invoked.Command); Assert.Same(currentRow, invoked.Parameter);
                Assert.Equal(assistant.Identity.DefinitionId, currentRow.Identity.DefinitionId);
                Assert.NotEqual(assistant.Identity.NamespaceId, currentRow.Identity.NamespaceId);
                // The real controller clears selection in BeginNavigation before publishing the acknowledged target.
                bindings.ApplySnapshot(snapshot with { Revision = 2, SelectedAssistant = null, ConversationBinding = null,
                    Conversation = null, Conversations = [], IsLoading = true });
                await Layout(); Assert.Null(bindings.WorkRouteIdentity); Assert.False(bindings.IsAssistantNavigationOpen);
                Assert.False(navigation.IsEffectivelyVisible); Assert.True(primary.IsEffectivelyEnabled);
                var targetConversation = savedConversation with { Id = Guid.NewGuid(), Title = "Target Main" };
                var targetBinding = new AssistantConversationBinding(new object(), other, "display-session", 1, targetConversation);
                const string targetDraft = "Saved target conversation draft";
                var targetData = new AssistantConversationData(targetConversation, [], [], [],
                    new(targetConversation.Id, null, targetDraft, "[]", now), null);
                bindings.ApplySnapshot(snapshot with { Revision = 3, SelectedAssistant = other,
                    ConversationBinding = targetBinding, Conversation = targetData });
                bindings.DismissAssistantNavigationAfterOpen(); bindings.ShowWork(); await Layout();
                Assert.True(Visible("assistant-navigation-toggle")!.IsFocused);
                Assert.False(bindings.IsAssistantNavigationOpen); Assert.Null(bindings.VisibleWorkspaceIdentity);
                Assert.Same(composer, Assert.Single(conversation.GetVisualDescendants().OfType<TextBox>(), value => value.Name == "conversation-draft"));
                Assert.Same(targetBinding, bindings.Snapshot.ConversationBinding);
                Assert.Equal(targetDraft, composer.Text); Assert.Equal(targetDraft, bindings.Prompt);
                Assert.False(Assert.IsType<bool>(selectedProperties!.Resolve("Selected").Value));
                Assert.True(Assert.IsType<bool>(otherProperties!.Resolve("Selected").Value));
                var freshRow = Assert.Single(Rows(bindings, "Assistants").Cast<AssistantsCuiBindings.AssistantRow>(), row => row.Identity == other.Identity);
                Assert.NotSame(currentRow, freshRow); Assert.False(bindings.TryGetItemValue(currentRow, "IsSelected", out _));
                await bindings.DispatchAsync(invoked.Command, invoked.Parameter, lifetime.Token);
                Assert.Single(calls);
            }
            finally
            {
                loader.Dispose(); conversationLoader.Dispose();
                await loader.WhenActionsIdleAsync(); await conversationLoader.WhenActionsIdleAsync(); window.Close();
            }
            return true;
        }, lifetime.Token));
    }

    [Fact]
    public async Task Only_the_real_secondary_workspace_row_exposes_archive_and_restore()
    {
        using var lifetime = new CancellationTokenSource();
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        Assert.True(await native.Dispatch<bool>(async () =>
        {
            var calls = new List<(string Command, object? Parameter)>();
            var bindings = Bindings(calls); bindings.HasSecondaryConversationArchiveOwner = true;
            var assistant = Definition();
            var main = new AssistantConversationSummary(Guid.NewGuid(), "Main", DateTimeOffset.UtcNow, false, null) { IsMain = true };
            var history = new AssistantConversationSummary(Guid.NewGuid(), "Secondary", DateTimeOffset.UtcNow, false, null) { IsOrdinarySecondary = true };
            var snapshot = AssistantsWorkspaceSnapshot.Empty with
            { Revision = 1, SelectedAssistant = assistant, Conversations = [main, history] };
            bindings.ShowWork(); bindings.ApplySnapshot(snapshot);
            await bindings.DispatchAsync("assistants.workspace.toggle", assistant.Identity, lifetime.Token);
            var registry = AssistantsNativeCuiSurface.CreateAvatarRegistry();
            registry.RegisterObjectRenderer("AssistantConversation", _ => new Panel());
            using var loader = new CuiControlLoader(registry);
            loader.SetBindingContext(bindings); loader.SetActionDispatcher(bindings);
            var window = new Window { Width = 900, Height = 700 };
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(AssistantsCuiScenes.ReadSource(AssistantsCuiScene.Work));
                Assert.Empty(diagnostics);
                var root = Assert.IsAssignableFrom<Control>(tree);
                loader.WireBindings(root); window.Content = root; window.Show(); window.UpdateLayout();
                Button VisibleArchive() => Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                    b => b.Tag as string == "ToggleConversationArchive" && b.IsEffectivelyVisible);
                Assert.Equal("Archive", VisibleArchive().Content);
                VisibleArchive().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync();
                var click = Assert.Single(calls);
                Assert.Equal("assistants.conversation.archive.toggle", click.Command);
                Assert.Equal(history.ConversationId, Assert.IsType<AssistantsCuiBindings.ConversationRow>(click.Parameter).Id);

                bindings.ApplySnapshot(snapshot with { Revision = 2, Conversations = [main, history with { IsArchived = true }] });
                window.UpdateLayout();
                Assert.Equal("Restore", VisibleArchive().Content);
                await bindings.DispatchAsync(click.Command, click.Parameter, lifetime.Token);
                Assert.Single(calls); // Stale previous row no longer has current canonical authority.
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); window.Close(); }
            return true;
        }, lifetime.Token));
    }

    [Fact]
    public async Task Workspace_remembers_each_canonical_assistant_without_touching_the_draft_or_routing()
    {
        using var lifetime = new CancellationTokenSource();
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        Assert.True(await native.Dispatch<bool>(async () =>
        {
            var calls = new List<(string Command, object? Parameter)>();
            var bindings = Bindings(calls);
            var first = Definition();
            // The same visible ID in another namespace must not share this preference.
            var second = first with { Identity = first.Identity with { NamespaceId = "another-owner" } };
            var snapshot = AssistantsWorkspaceSnapshot.Empty with
            { Revision = 1, SelectedAssistant = first, Assistants = [first, second] };
            bindings.ShowWork();
            bindings.ApplySnapshot(snapshot);
            Assert.True(bindings.TrySetValue("Prompt", "Unsent draft"));
            var registry = AssistantsNativeCuiSurface.CreateAvatarRegistry();
            var composer = new Panel();
            registry.RegisterObjectRenderer("AssistantConversation", _ => composer);
            using var loader = new CuiControlLoader(registry);
            loader.SetBindingContext(bindings); loader.SetActionDispatcher(bindings);
            var window = new Window { Width = 900, Height = 700 };
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(AssistantsCuiScenes.ReadSource(AssistantsCuiScene.Work));
                Assert.Empty(diagnostics);
                var root = Assert.IsAssignableFrom<Control>(tree);
                loader.WireBindings(root);
                window.Content = root; window.Show(); window.UpdateLayout();
                bool Open() => bindings.TryGetValue("ShowAssistantWorkspace", out var result) && result is true;
                Button Toggle() => Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                    b => b.Tag as string == "ToggleAssistantWorkspace");
                Task Click()
                {
                    Toggle().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    return loader.WhenActionsIdleAsync();
                }

                Assert.False(Open());
                await Click();
                Assert.True(Open());
                Assert.Equal("Close workspace", Toggle().Content);
                Assert.True(composer.IsEffectivelyVisible);
                Assert.Equal("Unsent draft", bindings.Prompt);

                bindings.ApplySnapshot(snapshot with { Revision = 2, SelectedAssistant = second });
                Assert.False(Open());
                await bindings.DispatchAsync("assistants.workspace.toggle", first.Identity, lifetime.Token);
                Assert.False(Open());
                bindings.ApplySnapshot(snapshot with { Revision = 3,
                    SelectedAssistant = first with { Revision = first.Revision + 1 } });
                Assert.True(Open());
                Assert.True(composer.IsEffectivelyVisible);
                await Click();
                Assert.False(Open());
                Assert.True(composer.IsEffectivelyVisible);
                Assert.Equal("Unsent draft", bindings.Prompt);
                Assert.Empty(calls); // Disclosure issues no canonical work/action/store command.
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); window.Close(); }
            return true;
        }, lifetime.Token));
    }

    [Fact]
    public async Task Authored_configuration_renders_typed_tool_connected_project_and_effort_rows_and_routes_the_same_second_row()
    {
        using var lifetime = new CancellationTokenSource();
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        var completed = await native.Dispatch<bool>(async () =>
        {
            var calls = new List<(string Command, object? Parameter)>();
            var bindings = Bindings(calls); var definition = Definition();
            bindings.ApplySnapshot(AssistantsWorkspaceSnapshot.Empty with
            { Revision = 1, SelectedAssistant = definition, Assistants = [definition] });
            bindings.EditConfiguration(definition);
            var submitted = bindings.OriginalDraft!.CaptureSubmission();
            bindings.BeginConfigurationCapabilityReview(submitted);
            var tool = CapabilityRegistryCatalog.BuiltIns.Single(row => row.Key == "read-file");
            var connection = tool with { Id = Guid.NewGuid(), Key = "fixture.connection", Name = "Observed connected capability", IsBuiltIn = false };
            var catalogue = new AssistantOriginalConfigurationCapabilityCatalogue(new object(), new object(), definition,
                new("fixture-actor", "fixture-profile", null, null, "fixture-auth"), CapabilityOriginalCatalogueState.Available,
                "Observed fixture metadata", [tool, connection]);
            Assert.True(bindings.PublishConfigurationCapabilityCatalogue(submitted, catalogue));
            using var loader = new CuiControlLoader(AssistantsNativeCuiSurface.CreateAvatarRegistry()); loader.SetBindingContext(bindings); loader.SetActionDispatcher(bindings);
            var window = new Window { Width = 1000, Height = 800 };
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(AssistantsCuiScenes.ReadSource(AssistantsCuiScene.Configuration));
                Assert.Empty(diagnostics); var root = Assert.IsAssignableFrom<Control>(tree); loader.WireBindings(root);
                window.Content = root; window.Show(); window.UpdateLayout();
                var controls = root.GetVisualDescendants().OfType<Control>().ToArray();
                Assert.Contains(controls.OfType<TextBlock>(), text => text.Text == tool.Name);
                Assert.Contains(controls.OfType<TextBlock>(), text => text.Text == connection.Name);
                Assert.Contains(controls.OfType<TextBlock>(), text => text.Text == "Saved development project 1");
                var choices = controls.OfType<Button>().Where(button => button.Tag as string == "ChooseCapabilityPreference").ToArray();
                Assert.Equal(2, choices.Length);
                Assert.Equal(choices.Length, choices.Select(CuiRuntimeIdentity.GetStableId).Distinct().Count());
                Assert.All(choices, button => Assert.False(string.IsNullOrWhiteSpace(CuiRuntimeIdentity.GetStableId(button))));
                var rows = Rows(bindings, "ConfigurationCapabilityRows");
                Assert.Equal(2, rows.Length); Assert.True(choices[1].IsEnabled);
                choices[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                var selected = Assert.Single(calls);
                Assert.Equal("assistants.configuration.capability.choose", selected.Command); Assert.Same(rows[1], selected.Parameter);
                Assert.Same(rows[1], bindings.FindConfigurationCapabilityRow(selected.Parameter));

                var projects = Rows(bindings, "ConfiguredDevelopmentProjects");
                var review = Assert.Single(controls.OfType<Button>(), button => button.Tag as string == "ReviewConfiguredProject");
                review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                Assert.Equal("assistants.configuration.project.review", calls[1].Command); Assert.Same(Assert.Single(projects), calls[1].Parameter);
                var efforts = controls.OfType<Button>().Where(button => button.Tag as string == "ChooseEffort").ToArray();
                Assert.NotEmpty(efforts); Assert.All(efforts, button => Assert.False(string.IsNullOrWhiteSpace(button.Content as string)));
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); window.Close(); }
            return true;
        }, lifetime.Token);
        Assert.True(completed);
    }

    [Fact]
    public async Task Authored_project_chooser_reuses_display_key_but_updated_button_routes_only_the_same_current_row()
    {
        using var lifetime = new CancellationTokenSource();
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        var completed = await native.Dispatch<bool>(async () =>
        {
            var calls = new List<(string Command, object? Parameter)>(); var bindings = Bindings(calls);
            var definition = Definition(); bindings.ApplySnapshot(AssistantsWorkspaceSnapshot.Empty with
            { Revision = 1, SelectedAssistant = definition, Assistants = [definition] });
            bindings.ShowWork(); bindings.BeginProjectSelection(false); bindings.SetProjectCatalogue(Page("First observed project"));
            var old = Assert.Single(Rows(bindings, "ProjectChoices"));
            using var conversationLoader = new CuiControlLoader();
            conversationLoader.RegisterObjectRenderer("AssistantMessage", _ => new CuiMarkdownView());
            conversationLoader.SetBindingContext(bindings); conversationLoader.SetActionDispatcher(bindings);
            var (conversationTree, conversationDiagnostics) = conversationLoader.LoadMarkup(AssistantsCuiScenes.ReadSource(AssistantsCuiScene.Conversation));
            Assert.Empty(conversationDiagnostics);
            var conversation = Assert.IsAssignableFrom<Control>(conversationTree); conversationLoader.WireBindings(conversation);
            var registry = AssistantsNativeCuiSurface.CreateAvatarRegistry(); registry.RegisterObjectRenderer("AssistantConversation", _ => conversation);
            using var loader = new CuiControlLoader(registry); loader.SetBindingContext(bindings); loader.SetActionDispatcher(bindings);
            var window = new Window { Width = 1000, Height = 800 };
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(AssistantsCuiScenes.ReadSource(AssistantsCuiScene.Work));
                Assert.Empty(diagnostics); var root = Assert.IsAssignableFrom<Control>(tree); loader.WireBindings(root);
                using var keyboard = new AssistantWorkspaceKeyboard(root, bindings);
                window.Content = root; window.Show(); window.UpdateLayout();
                Assert.Null(bindings.VisibleWorkspaceIdentity);
                var chooser = Assert.Single(root.GetVisualDescendants().OfType<CuiContainerControl>(), control => control.Name == "assistant-project-selection");
                Assert.True(chooser.IsEffectivelyVisible);
                var query = Assert.Single(root.GetVisualDescendants().OfType<TextBox>(), control => control.Name == "project-title-search");
                Assert.True(query.IsEffectivelyVisible); Assert.True(query.Focus());
                Assert.Null(bindings.VisibleWorkspaceIdentity); // The real chooser does not require opening Workspace.
                var firstButton = Assert.Single(root.GetVisualDescendants().OfType<Button>(), button => button.Tag as string == "ChooseOriginalProject");
                var originalId = CuiRuntimeIdentity.GetStableId(firstButton); Assert.Equal("First observed project", firstButton.Content);
                var currentPage = Page("Fresh current project"); bindings.SetProjectCatalogue(currentPage);
                var currentRow = Assert.Single(Rows(bindings, "ProjectChoices")); Assert.NotSame(old, currentRow);
                var currentButton = Assert.Single(root.GetVisualDescendants().OfType<Button>(), button => button.Tag as string == "ChooseOriginalProject");
                Assert.Same(firstButton, currentButton); Assert.Equal(originalId, CuiRuntimeIdentity.GetStableId(currentButton));
                Assert.Equal("Fresh current project", currentButton.Content);
                currentButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await conversationLoader.WhenActionsIdleAsync();
                var actual = Assert.Single(calls); Assert.Equal("assistants.project.choose", actual.Command); Assert.Same(currentRow, actual.Parameter);
                Assert.Same(currentPage.Candidates[0], bindings.DemandOriginalProjectCandidate(actual.Parameter));
                Assert.False(bindings.TryGetItemValue(old, "Title", out _));
                await bindings.DispatchAsync("assistants.project.choose", old, lifetime.Token);
                Assert.Single(calls); // The stale object is refused before acquiring a product command.
                bindings.Revoke(); Assert.False(bindings.TryGetItemValue(currentRow, "Id", out _));
            }
            finally
            {
                loader.Dispose(); conversationLoader.Dispose();
                await loader.WhenActionsIdleAsync(); await conversationLoader.WhenActionsIdleAsync(); window.Close();
            }
            return true;
        }, lifetime.Token);
        Assert.True(completed);
    }

    [Theory]
    [InlineData(940, 660)]
    [InlineData(500, 650)]
    [InlineData(360, 640)]
    public async Task Task_composer_with_long_draft_and_attachments_keeps_workspace_model_and_remove_actions_in_the_viewport(double width, double height)
    {
        using var lifetime = new CancellationTokenSource();
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        Assert.True(await native.Dispatch<bool>(async () =>
        {
            var calls = new List<(string Command, object? Parameter)>();
            var bindings = Bindings(calls);
            var definition = Definition() with { Configuration = new AssistantConfiguration
                { Name = "Model review", Model = new("fixture", "missing-saved-model") } };
            AssistantModelChoice Model(string name) => new(new ModelDescriptor(name, 1, "family", "parameters", "quantization",
                new HashSet<ToolCapability>(), DateTimeOffset.UnixEpoch), "fixture");
            var choices = new[] { Model("first-local-model"), Model("second-local-model") };
            var now = DateTimeOffset.UtcNow; var conversationId = Guid.NewGuid();
            var draft = "Keep this viewport draft\n" + string.Join("\n", Enumerable.Repeat("Long Task draft retained through viewport changes.", 30));
            var savedConversation = new Conversation(conversationId, HavenMode.Chat, ConversationKind.Task,
                "Typed Task viewport", null, null, false, false, now, now);
            var attachments = Enumerable.Range(1, 3).Select(index => new MessageAttachment(Guid.NewGuid(), conversationId,
                null, null, $"Current attachment {index} with a deliberately long descriptive saved filename.txt", "", "text/plain",
                MessageAttachmentKind.PlainText, 1200, new string('a', 64), AttachmentProcessingState.Ready,
                AttachmentAnalysisMethod.TextExtracted, new string((char)('a' + index), 1200), "{}", now, now)).ToArray();
            var data = new AssistantConversationData(savedConversation, [], attachments, [],
                new(conversationId, null, draft, JsonSerializer.Serialize(attachments.Select(value => value.Id)), now), null);
            var actualBinding = new AssistantConversationBinding(new object(), definition, "display-session", 1, savedConversation);
            // These are actual typed presentation rows, not an invented Home attachment-import grant.
            bindings.ShowWork();
            bindings.ApplySnapshot(AssistantsWorkspaceSnapshot.Empty with
            { Revision = 1, SelectedAssistant = definition, Assistants = [definition], Models = choices,
                ConversationBinding = actualBinding, Conversation = data });
            bindings.SetMessages([]);
            Assert.Null(bindings.SelectedModel); // The missing saved preference is not silently replaced.
            using var conversationLoader = new CuiControlLoader();
            conversationLoader.RegisterObjectRenderer("AssistantMessage", _ => new CuiMarkdownView());
            conversationLoader.SetBindingContext(bindings); conversationLoader.SetActionDispatcher(bindings);
            var (conversationTree, conversationDiagnostics) = conversationLoader.LoadMarkup(AssistantsCuiScenes.ReadSource(AssistantsCuiScene.Conversation));
            Assert.Empty(conversationDiagnostics);
            var conversation = Assert.IsAssignableFrom<Control>(conversationTree); conversationLoader.WireBindings(conversation);
            var registry = AssistantsNativeCuiSurface.CreateAvatarRegistry();
            registry.RegisterObjectRenderer("AssistantConversation", _ => conversation);
            using var loader = new CuiControlLoader(registry); loader.SetBindingContext(bindings); loader.SetActionDispatcher(bindings);
            var window = new Window { Width = width, Height = height };
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(AssistantsCuiScenes.ReadSource(AssistantsCuiScene.Work));
                Assert.Empty(diagnostics); var root = Assert.IsAssignableFrom<Control>(tree); loader.WireBindings(root);
                using var keyboard = new AssistantWorkspaceKeyboard(root, bindings);
                window.Content = root; window.Show();
                async Task Layout()
                {
                    await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
                    await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
                }
                Button FindButton(string id) => Assert.Single(root.GetVisualDescendants().OfType<Button>(), value => value.Name == id);
                void InViewport(Control control)
                {
                    Assert.True(control.IsEffectivelyVisible);
                    var origin = control.TranslatePoint(default, window) ?? throw new InvalidOperationException("Detached model control.");
                    Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
                    Assert.True(origin.X >= 0 && origin.Y >= 0 && origin.X + control.Bounds.Width <= window.ClientSize.Width + 1 &&
                        origin.Y + control.Bounds.Height <= window.ClientSize.Height + 1, "The model choice path must fit the current viewport.");
                }
                await Layout();
                var input = Assert.Single(conversation.GetVisualDescendants().OfType<TextBox>(), value => value.Name == "conversation-draft");
                Assert.Equal(draft, input.Text); Assert.True(input.Focus());
                input.CaretIndex = 18; input.SelectionStart = 10; input.SelectionEnd = 18;
                var footer = Assert.Single(conversation.GetVisualDescendants().OfType<CuiContainerControl>(), value => value.Name == "assistant-composer-footer");
                var transcript = Assert.Single(conversation.GetVisualDescendants().OfType<CuiContainerControl>(), value => value.Name == "assistant-transcript-scroll");
                var scroll = Assert.Single(transcript.GetVisualDescendants().OfType<ScrollViewer>(), value =>
                    ReferenceEquals(value.GetVisualAncestors().OfType<CuiContainerControl>().FirstOrDefault(), transcript));
                InViewport(footer); InViewport(input);
                Assert.True(input.Bounds.Height <= 112);
                var editorScroll = Assert.Single(input.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.True(editorScroll.Viewport.Height > 0 && editorScroll.Extent.Height > editorScroll.Viewport.Height,
                    "The retained multiline editor must scroll its long draft within the composer cap.");
                var composer = Assert.Single(conversation.GetVisualDescendants().OfType<CuiContainerControl>(), value => value.Name == "assistant-message-composer");
                var secondaryActions = Assert.Single(conversation.GetVisualDescendants().OfType<CuiContainerControl>(), value => value.Name == "assistant-composer-secondary-actions");
                var identityBar = Assert.Single(root.GetVisualDescendants().OfType<CuiContainerControl>(), value => value.Name == "assistant-identity-bar");
                Assert.True(scroll.Viewport.Height >= 64 && double.IsFinite(scroll.Viewport.Height),
                    $"Task controls, a long draft and attachment chips must leave at least 64px of transcript viewport. " +
                    $"Viewport={scroll.Viewport}; transcript={transcript.Bounds}; footer={footer.Bounds}; " +
                    $"composer={composer.Bounds}; secondary actions={secondaryActions.Bounds}; identity bar={identityBar.Bounds}; conversation={conversation.Bounds}.");
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                var footerBounds = new Rect(footer.TranslatePoint(default, window)!.Value, footer.Bounds.Size);
                scroll.ScrollToEnd(); await Layout(); Assert.True(scroll.Offset.Y > 0);
                Assert.Equal(footerBounds, new Rect(footer.TranslatePoint(default, window)!.Value, footer.Bounds.Size));
                foreach (var id in new[] { "assistant-start-task", "assistant-steer", "assistant-queue", "assistant-stop",
                    "assistant-save-draft", "assistant-new-task", "conversation-attach-file", "conversation-refresh-models", "conversation-model-settings", "new-conversation" }) InViewport(FindButton(id));
                foreach (var name in new[] { "Selected model", "Model availability help" })
                    InViewport(Assert.Single(conversation.GetVisualDescendants().OfType<TextBlock>(), value =>
                        value.IsEffectivelyVisible && AutomationProperties.GetName(value) == name));
                var removeButtons = footer.GetVisualDescendants().OfType<Button>().Where(value => value.Tag as string == "DetachAttachment").ToArray();
                Assert.Equal(attachments.Length, removeButtons.Length);
                foreach (var attachment in attachments)
                {
                    var label = Assert.Single(footer.GetVisualDescendants().OfType<TextBlock>(), value =>
                        AutomationProperties.GetName(value) == attachment.OriginalName);
                    Assert.Equal(attachment.OriginalName, ToolTip.GetTip(label));
                    Assert.Contains(transcript.GetVisualDescendants().OfType<TextBlock>(), value => value.Text == attachment.ExtractedText);
                }
                var toggle = FindButton("assistant-workspace-toggle"); InViewport(toggle);
                Assert.True(toggle.Focus());
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync(); await Layout();
                var close = FindButton("assistant-workspace-close"); InViewport(close); Assert.True(close.IsFocused);
                var buttons = root.GetVisualDescendants().OfType<Button>().Where(value => value.IsEffectivelyVisible && value.Tag as string == "ChooseModel").ToArray();
                Assert.Equal(2, buttons.Length); Assert.All(buttons, InViewport);
                var rows = Rows(bindings, "Models");
                buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                var dispatched = Assert.Single(calls);
                Assert.Equal("assistants.model.choose", dispatched.Command); Assert.Same(rows[1], dispatched.Parameter);
                Assert.Same(choices[1], Assert.IsType<AssistantsCuiBindings.ModelRow>(dispatched.Parameter).Original);
                Assert.Null(bindings.SelectedModel); // This projection fixture records routing; it invents no model-owner result.
                close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync(); await Layout();
                Assert.Null(bindings.VisibleWorkspaceIdentity); InViewport(toggle); Assert.True(toggle.IsFocused);
                Assert.Same(window, TopLevel.GetTopLevel(conversation)); Assert.Single(calls);
                Assert.Same(input, Assert.Single(conversation.GetVisualDescendants().OfType<TextBox>(), value => value.Name == "conversation-draft"));
                Assert.Equal(draft, input.Text); Assert.Equal(draft, bindings.Prompt); Assert.Equal("viewport", input.SelectedText);
                Assert.Equal(10, input.SelectionStart); Assert.Equal(18, input.SelectionEnd);
                Assert.Equal(footerBounds, new Rect(footer.TranslatePoint(default, window)!.Value, footer.Bounds.Size));
                var currentAttachments = Rows(bindings, "DraftAttachments");
                removeButtons[^1].BringIntoView(); await Layout(); InViewport(removeButtons[^1]);
                removeButtons[^1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await conversationLoader.WhenActionsIdleAsync();
                Assert.Equal(2, calls.Count); Assert.Equal("assistants.attachment.detach", calls[1].Command);
                Assert.Same(currentAttachments[^1], calls[1].Parameter);
                Assert.Same(attachments[^1], Assert.IsType<AssistantsCuiBindings.AttachmentRow>(calls[1].Parameter).Original);
                Assert.Equal(draft, input.Text); Assert.Equal("viewport", input.SelectedText);
                Assert.Equal(footerBounds, new Rect(footer.TranslatePoint(default, window)!.Value, footer.Bounds.Size));
            }
            finally
            {
                loader.Dispose(); conversationLoader.Dispose();
                await loader.WhenActionsIdleAsync(); await conversationLoader.WhenActionsIdleAsync(); window.Close();
            }
            return true;
        }, lifetime.Token));
    }

    [Fact]
    public void Typed_row_port_rejects_equal_foreign_rows_nested_source_fields_writes_and_newer_form_rows()
    {
        var bindings = Bindings([]); var definition = Definition(); bindings.ApplySnapshot(AssistantsWorkspaceSnapshot.Empty with
        { Revision = 1, SelectedAssistant = definition, Assistants = [definition] }); bindings.EditConfiguration(definition);
        var row = Assert.IsType<AssistantsCuiBindings.AssistantRow>(Assert.Single(Rows(bindings, "Assistants")));
        Assert.True(bindings.TryGetItemValue(row, "Name", out var name)); Assert.Equal(definition.Configuration.Name, name);
        Assert.True(bindings.TryGetItemValue(row, "IsSelected", out var selected)); Assert.Equal(true, selected);
        Assert.False(bindings.TryGetItemValue(row with { }, "Name", out _));
        Assert.False(bindings.TryGetItemValue(row with { }, "IsSelected", out _));
        Assert.False(bindings.TryGetItemValue(row, "Identity.DefinitionId", out _));
        Assert.False(bindings.TrySetItemValue(row, "Name", "Injected"));
        var submitted = bindings.OriginalDraft!.CaptureSubmission(); bindings.BeginConfigurationCapabilityReview(submitted);
        var capability = CapabilityRegistryCatalog.BuiltIns[0]; var catalogue = new AssistantOriginalConfigurationCapabilityCatalogue(
            new object(), new object(), definition, new("fixture-actor", "fixture-profile", null, null, "fixture-auth"),
            CapabilityOriginalCatalogueState.Available, "Fixture", [capability]);
        Assert.True(bindings.PublishConfigurationCapabilityCatalogue(submitted, catalogue));
        var old = Assert.Single(Rows(bindings, "ConfigurationCapabilityRows")); Assert.True(bindings.TryGetItemValue(old, "Id", out _));
        Assert.True(bindings.TrySetValue("DraftMemoryEnabled", true));
        Assert.Empty(Rows(bindings, "ConfigurationCapabilityRows")); Assert.False(bindings.TryGetItemValue(old, "Id", out _));
    }

    private static AssistantsCuiBindings Bindings(List<(string Command, object? Parameter)> calls) =>
        new((command, parameter, _) => { calls.Add((command, parameter)); return ValueTask.CompletedTask; }, body => body(), () => true);
    private static object[] Rows(AssistantsCuiBindings bindings, string key)
    { Assert.True(bindings.TryGetValue(key, out var actual)); return Assert.IsAssignableFrom<IEnumerable>(actual).Cast<object>().ToArray(); }
    private static AssistantDefinitionSnapshot Definition() => new(new("fixture-den", "personal", "fixture-assistant"),
        1, ConfiguredIdentityKind.Assistant, new() { Name = "Rendered fixture", ProjectReferences =
            [new DeveloperProjectReference(Guid.NewGuid(), 1, Guid.NewGuid(), 1, Guid.NewGuid())] }, []);
    private static AssistantOriginalProjectCatalogue Page(string title) => new(
        [new AssistantOriginalProjectCandidate(new object(), new object(), title,
            new DeveloperProjectReference(Guid.NewGuid(), 1, Guid.NewGuid(), 1, Guid.NewGuid()))], false, "Fixture source rows");
}
