using Avalonia;
using Avalonia.Controls;
using CakeOS.Cui.Themes;

namespace CakeOS.Cui.Runtime;

/// <summary>A Page/Layer keeps the native Panel child and layout contract while its visual-only
/// background uses the same material renderer as CUI containers. No authored child is reparented.</summary>
public sealed class CuiMaterialPanel : Panel
{
    private CuiMaterialBorder.MaterialLayer? _materialLayer;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != BackgroundProperty) return;
        if (Background is CuiMaterialBrush or CuiCustomMaterialBrush or CuiGlowFieldBrush)
        {
            if (_materialLayer is null)
            {
                _materialLayer = new() { IsHitTestVisible = false, Focusable = false, ZIndex = int.MinValue };
                // Panel inserts/replaces/moves authored children by their Children index. Keeping
                // this extra visual at the end preserves that exact index map; ZIndex paints it
                // behind those children without making it a logical child or a focus target.
                VisualChildren.Add(_materialLayer);
            }
            _materialLayer.Material = Background;
            _materialLayer.IsVisible = true;
            _materialLayer.InvalidateVisual();
            InvalidateArrange();
        }
        else if (_materialLayer is not null) _materialLayer.IsVisible = false;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        if (_materialLayer is { } layer)
        {
            layer.Measure(finalSize);
            layer.Arrange(new Rect(finalSize));
        }
        return size;
    }
}
