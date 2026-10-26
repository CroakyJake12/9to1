using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Chat;
using Haven.UI;
using Container = Haven.UI.Components.Container;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;
using NativeButton = Avalonia.Controls.Button;

namespace Haven.Desktop.Tests;

public sealed class ChatAddMenuNativePopupTests
{
    [AvaloniaFact]
    public async Task Real_chat_add_menu_uses_native_popup_above_the_mounted_composer_and_retires_cleanly()
    {
        var resolver = new ChatGenUiNativeControlResolver();
        using var scene = new ChatHavenScene(resolver);
        var host = new HavenSceneControl(new HavenDesktopImageResolver(), resolver) { Root = scene.Root };
        var window = new Window { Width = 1000, Height = 760, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();
            await scene.ComposerView.InitializeAsync();
            window.UpdateLayout();

            // The real canonical CUI composer is admitted through the same native resolver as the
            // product page; this is not a stand-in control or an in-scene z-index assertion.
            Assert.Contains(host.Children, child => ReferenceEquals(child, scene.ComposerView));
            Assert.True(scene.ComposerView.IsAttachedToVisualTree());
            var addButton = Assert.Single(scene.ComposerView.GetVisualDescendants().OfType<NativeButton>(), button =>
                AutomationProperties.GetName(button) == "Add files or actions");
            var message = Assert.Single(scene.ComposerView.GetVisualDescendants().OfType<TextBox>(), input =>
                AutomationProperties.GetName(input) == "Message");
            message.Focus();
            addButton.RaiseEvent(new RoutedEventArgs(NativeButton.ClickEvent));
            window.UpdateLayout();

            var popup = Assert.IsType<Avalonia.Controls.Primitives.Popup>(scene.AddMenuPopup);
            var popupHost = Assert.IsType<HavenSceneControl>(scene.AddMenuPopupHost);
            Assert.True(popup.IsOpen);
            Assert.False(popup.ShouldUseOverlayLayer);
            Assert.False(popup.IsUsingOverlayLayer);
            Assert.Same(scene.AddMenuPrefab, popupHost.Root);
            Assert.NotSame(window, TopLevel.GetTopLevel(popupHost));
            Assert.Same(addButton, popup.PlacementTarget);
            Assert.Equal(Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft, popup.PlacementAnchor);
            Assert.Equal(Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.TopRight, popup.PlacementGravity);
            Assert.Equal(-6, popup.VerticalOffset);
            Assert.True(popupHost.SurfaceMetrics.Viewport.Width < window.ClientSize.Width);
            Assert.True(popupHost.SurfaceMetrics.Viewport.Width > 240);
            Assert.True(popupHost.SurfaceMetrics.Viewport.Height < window.ClientSize.Height);
            Assert.True(popupHost.IsVisible);

            // The compact menu remains a grouped list and category rows still drill into the real
            // shared catalogue without expanding or replacing the actual composer.
            Assert.Equal("Add", scene.AddMenuPrefab.GetComponent<HavenText>("MenuTitle").Content);
            Assert.Equal(HavenVisibility.Visible, scene.AddMenuPrefab.GetComponent<Container>("MainMenu")
                .GetValue(HavenProperties.Visibility));
            Assert.Equal(HavenVisibility.Collapsed, scene.CatalogSearch.GetValue(HavenProperties.Visibility));
            Assert.Same(popupHost, TopLevel.GetTopLevel(popupHost)?.FocusManager?.GetFocusedElement());
            var menuRouter = new HavenInputRouter(popupHost.Root!);
            Click(menuRouter, scene.AddMenuPrefab.GetComponent<HavenButton>("Agents"));
            window.UpdateLayout();
            Assert.Equal(HavenVisibility.Visible, scene.CatalogPanel.GetValue(HavenProperties.Visibility));
            Assert.Equal(HavenVisibility.Collapsed, scene.AddMenuPrefab.GetComponent<Container>("MainMenu")
                .GetValue(HavenProperties.Visibility));
            Click(menuRouter, scene.AddMenuPrefab.GetComponent<HavenButton>("CatalogBack"));
            Assert.Equal(HavenVisibility.Visible, scene.AddMenuPrefab.GetComponent<Container>("MainMenu")
                .GetValue(HavenProperties.Visibility));

            // Mention-search/catalogue rows still come from the same ChatAddMenuSurface, and
            // selecting its real Files row closes the native host before forwarding the action.
            var selected = new List<Haven.Desktop.Views.Shell.TopRail.AddMenu.AddMenuAction>();
            scene.AddActionSelected += (_, action) => selected.Add(action);
            scene.ShowMentionSearch("files");
            window.UpdateLayout();
            Assert.True(popup.IsOpen);
            Assert.Equal(HavenVisibility.Visible, scene.CatalogPanel.GetValue(HavenProperties.Visibility));
            var fileRow = Assert.Single(scene.CatalogRows.Children.OfType<HavenButton>());
            Click(new HavenInputRouter(popupHost.Root!), fileRow);
            window.UpdateLayout();

            Assert.Equal([Haven.Desktop.Views.Shell.TopRail.AddMenu.AddMenuAction.File], selected);
            Assert.False(popup.IsOpen);
            Assert.Equal(HavenVisibility.Collapsed, scene.AddOverlay.GetValue(HavenProperties.Visibility));
            Assert.True(message.IsFocused);

            // Mention search opens for each edit while the native composer owns its query. The
            // actual CUI TextBox must retain focus/caret so the user can continue typing.
            message.Focus();
            scene.Input.Text = "@f";
            scene.Input.PlaceCaretAtEnd();
            scene.ShowMentionSearch("f");
            window.UpdateLayout();
            Assert.True(popup.IsOpen);
            Assert.True(message.IsFocused);
            Assert.Equal(2, message.CaretIndex);
            message.Text += "iles";
            message.CaretIndex = message.Text.Length;
            scene.ShowMentionSearch("files");
            window.UpdateLayout();
            Assert.True(message.IsFocused);
            Assert.Equal("@files", scene.Input.Text);
            Assert.Equal(scene.Input.Text.Length, message.CaretIndex);

            // Escape closes the mention popup without moving caret focus away from the composer.
            message.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape
            });
            Assert.False(popup.IsOpen);
            Assert.Equal(HavenVisibility.Collapsed, scene.AddOverlay.GetValue(HavenProperties.Visibility));

            // An explicit Add open takes keyboard focus for row navigation, then owner detachment
            // retires the native host so it cannot leave an invisible layer intercepting input.
            scene.ShowAddMenu();
            window.UpdateLayout();
            Assert.True(popup.IsOpen);
            Assert.Same(popupHost, TopLevel.GetTopLevel(popupHost)?.FocusManager?.GetFocusedElement());
            addButton.Focus();
            scene.HideAddMenu();
            window.UpdateLayout();
            Assert.True(addButton.IsFocused);

            scene.ShowAddMenu();
            window.UpdateLayout();
            Assert.True(popup.IsOpen);
            window.Content = new Border();
            window.UpdateLayout();
            Assert.False(popup.IsOpen);
            Assert.Equal(HavenVisibility.Collapsed, scene.AddOverlay.GetValue(HavenProperties.Visibility));
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void Click(HavenInputRouter router, HavenElement element)
    {
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2,
            element.Bounds.Y + element.Bounds.Height / 2);
        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
    }
}
