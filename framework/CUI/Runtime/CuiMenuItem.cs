using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace CakeOS.Cui.Runtime;

/// <summary>Native menu behavior with CUI-owned automation for opening and invoking it.</summary>
public sealed class CuiMenuItem : MenuItem
{
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(CuiMenuItem owner) : MenuItemAutomationPeer(owner), IInvokeProvider, IExpandCollapseProvider
    {
        public ExpandCollapseState ExpandCollapseState => owner.ItemCount == 0 ? ExpandCollapseState.LeafNode
            : owner.IsSubMenuOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        public bool ShowsMenu => true;
        public void Expand() { if (owner.IsEffectivelyEnabled && owner.ItemCount > 0) owner.IsSubMenuOpen = true; }
        public void Collapse() => owner.IsSubMenuOpen = false;
        public void Invoke()
        {
            if (!owner.IsEffectivelyEnabled) return;
            if (owner.ItemCount > 0) { Expand(); return; }
            // Same routed event as the native pointer and keyboard menu handlers.
            Dispatcher.UIThread.Post(() =>
            {
                if (owner.IsEffectivelyEnabled) owner.RaiseEvent(new RoutedEventArgs(ClickEvent));
            });
        }
    }
}
