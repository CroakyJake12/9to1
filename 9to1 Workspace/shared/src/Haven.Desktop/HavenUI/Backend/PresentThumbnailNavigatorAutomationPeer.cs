using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Haven.Desktop.Views.Pages.Present;
using Haven.UI;

namespace Haven.Desktop.HavenUI.Backend;

/// <summary>Exposes the real retained slide thumbnails without duplicate visual buttons.</summary>
internal sealed class PresentThumbnailNavigatorAutomationPeer(
    HavenSceneControl owner, HavenSceneAutomationPeer rootPeer, PresentThumbnailNavigator navigator)
    : HavenElementAutomationPeer(owner, rootPeer, navigator)
{
    private readonly Dictionary<Guid, PresentThumbnailItemAutomationPeer> _items = [];
    private PresentThumbnailNavigator OwnerNavigator => (PresentThumbnailNavigator)Element;

    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore()
    {
        var visible = RootPeer.IsExposed(OwnerNavigator) ? OwnerNavigator.GetVisibleItems() : [];
        var ids = visible.Select(item => item.SlideId).ToHashSet();
        foreach (var stale in _items.Keys.Where(id => !ids.Contains(id)).ToArray()) _items.Remove(stale);
        return visible.Select(item => (AutomationPeer)GetItem(item.SlideId)).ToArray();
    }

    private PresentThumbnailItemAutomationPeer GetItem(Guid slideId)
    {
        if (_items.TryGetValue(slideId, out var peer)) return peer;
        peer = new PresentThumbnailItemAutomationPeer(SceneOwner, RootPeer, this, OwnerNavigator, slideId);
        _items.Add(slideId, peer);
        return peer;
    }
}

internal sealed class PresentThumbnailItemAutomationPeer(
    HavenSceneControl owner, HavenSceneAutomationPeer rootPeer, PresentThumbnailNavigatorAutomationPeer parent,
    PresentThumbnailNavigator navigator, Guid slideId) : ControlAutomationPeer(owner), IInvokeProvider
{
    private PresentThumbnailItem? CurrentItem
    {
        get
        {
            if (!rootPeer.IsExposed(navigator)) return null;
            foreach (var item in navigator.GetVisibleItems())
                if (item.SlideId == slideId) return item;
            return null;
        }
    }

    public void Invoke()
    {
        if (!IsEnabledCore()) return;
        navigator.ActivateSlide(slideId);
        owner.FocusElement(navigator);
    }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
    protected override string GetClassNameCore() => nameof(PresentThumbnailNavigator);
    protected override string? GetAutomationIdCore() => $"Present.Rail.{slideId:N}";
    protected override string? GetNameCore() => CurrentItem?.Name ?? string.Empty;
    protected override AutomationPeer? GetParentCore() => parent;
    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() => [];
    protected override Rect GetBoundingRectangleCore() => CurrentItem is { } item ? rootPeer.BoundsInTopLevel(item.Bounds) : default;
    protected override bool IsControlElementCore() => true;
    protected override bool IsContentElementCore() => true;
    protected override bool IsOffscreenCore() => CurrentItem is null || rootPeer.IsOffscreen(navigator);
    protected override bool IsEnabledCore() => CurrentItem is not null && navigator.Accessibility.Enabled
        && navigator.GetValue(HavenProperties.Enabled) && !navigator.State.HasFlag(HavenElementState.Disabled);
    protected override bool IsKeyboardFocusableCore() => IsEnabledCore();
    protected override bool HasKeyboardFocusCore() => CurrentItem is { IsSelected: true }
        && navigator.State.HasFlag(HavenElementState.Focused);
    protected override void SetFocusCore() { if (IsEnabledCore()) owner.FocusElement(navigator); }
}
