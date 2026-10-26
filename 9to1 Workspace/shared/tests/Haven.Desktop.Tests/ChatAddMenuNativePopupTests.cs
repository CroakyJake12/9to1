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
            addButton.RaiseEvent(new RoutedEventArgs(NativeButton.ClickEvent));
            window.UpdateLayout();

            var popup = Assert.IsType<Avalonia.Controls.Primitives.Popup>(scene.AddMenuPopup);
            var popupHost = Assert.IsType<HavenSceneControl>(scene.AddMenuPopupHost);
            Assert.True(popup.IsOpen);
            Assert.False(popup.ShouldUseOverlayLayer);
            Assert.False(popup.IsUsingOverlayLayer);
            Assert.Same(scene.AddMenuPrefab, popupHost.Root);
            Assert.NotSame(window, TopLevel.GetTopLevel(popupHost));
            Assert.InRange(Math.Abs(window.ClientSize.Width - popupHost.SurfaceMetrics.Viewport.Width), 0, 0.5);
            Assert.InRange(Math.Abs(window.ClientSize.Height - popupHost.SurfaceMetrics.Viewport.Height), 0, 0.5);
            Assert.True(popupHost.IsVisible);

            // The full-client surface shares the HUI scene's client coordinate space even though
            // the actual CUI composer is a separately arranged native child above the canvas.
            var composerOrigin = scene.ComposerView.TranslatePoint(default, window);
            Assert.NotNull(composerOrigin);
            Assert.InRange(Math.Abs(-composerOrigin.Value.X - popup.HorizontalOffset), 0, 0.5);
            Assert.InRange(Math.Abs(-composerOrigin.Value.Y - popup.VerticalOffset), 0, 0.5);
            Assert.Equal("Attach to Chat", scene.AddMenuPrefab.GetComponent<HavenText>("MenuTitle").Content);

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

            // Re-open and exercise Escape plus native-host detachment so neither can leave an
            // invisible full-client input surface intercepting future composer events.
            scene.ShowAddMenu();
            window.UpdateLayout();
            Assert.True(popup.IsOpen);
            popupHost.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape
            });
            Assert.False(popup.IsOpen);
            Assert.Equal(HavenVisibility.Collapsed, scene.AddOverlay.GetValue(HavenProperties.Visibility));

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
