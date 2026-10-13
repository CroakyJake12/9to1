using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using HavenInput = Haven.UI.Components.Input;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>Only the canonical CUI root and explicitly registered original GenUI
/// children are hosted here. No whole legacy Chat scene is mounted.</summary>
public sealed class ChatCuiSceneHost : ContentControl
{
    private TopLevel? _top;
    public ChatCuiSceneHost()
    {
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }
    public event Action<HavenInput>? InputSubmitted;
    public event Action? PointerPressedOutside;
    internal bool FocusElement(CuiChatComposer composer) => composer.Control.Focus();
    internal void ForwardOriginalInput(HavenInput input) => InputSubmitted?.Invoke(input);
    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _top = TopLevel.GetTopLevel(this);
        _top?.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, true);
    }
    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _top?.RemoveHandler(PointerPressedEvent, OnPointerPressed);
        _top = null;
    }
    private void OnPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (args.Source is not Control source || !this.IsVisualAncestorOf(source)) PointerPressedOutside?.Invoke();
    }
}
