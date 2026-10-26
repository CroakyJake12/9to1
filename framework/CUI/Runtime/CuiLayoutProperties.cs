using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;

namespace CakeOS.Cui.Runtime;

/// <summary>Canonical layout projection through the SAME per-element CUI effective-property store.</summary>
public static class CuiLayoutProperties
{
    private static readonly ConditionalWeakTable<Control, Projection> Projections = new();
    public static bool SetLiteral(Control control, string property, string value,
        CuiPropertyLevel level = CuiPropertyLevel.Instance, string source = "CUI runtime assignment", long order = 0) =>
        Projections.GetValue(control, target => new(target)).Set(property, value, level, source, order);
    public static void Detach(Control control)
    { if (Projections.TryGetValue(control, out var value)) { value.Dispose(); Projections.Remove(control); } }

    private sealed class Projection : IDisposable
    {
        private readonly Control _control;
        private readonly CuiEffectivePropertyStore _store;
        private readonly HashSet<string> _owned = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(string, CuiPropertyLevel, string), string> _lastLiterals = [];
        private bool _bringIntoView, _revealPending, _disposed;
        public Projection(Control control)
        { _control = control; _store = CuiPropertyPresentation.For(control); }
        private Grid RequireGrid() => (_control as CuiContainerControl)?.Layout as Grid ?? _control as Grid
            ?? throw new InvalidOperationException("This property requires Container Type Grid.");

        public bool Set(string property, string text, CuiPropertyLevel level, string source, long order)
        {
            if (!CuiControlRegistry.Default.TryGetProperty(property, out var descriptor)) return false;
            var name = descriptor.Name;
            object value;
            object initial;
            Type type;
            switch (name)
            {
                case "RowSizes": RequireGrid(); value = new RowDefinitions(CuiGridSizes.ToNative(text)); initial = new RowDefinitions(); type = typeof(RowDefinitions); break;
                case "ColumnSizes": RequireGrid(); value = new ColumnDefinitions(CuiGridSizes.ToNative(text)); initial = new ColumnDefinitions(); type = typeof(ColumnDefinitions); break;
                case "Rows": case "Columns": RequireGrid(); goto case "Row";
                case "Row": case "Column": case "RowSpan": case "ColumnSpan":
                    value = int.Parse(text, CultureInfo.InvariantCulture); type = typeof(int); initial = name.EndsWith("Span", StringComparison.Ordinal) ? 1 : 0;
                    if ((int)value < 0 || (name.EndsWith("Span", StringComparison.Ordinal) && (int)value < 1)) throw new FormatException($"{name} must be a valid nonnegative index or positive span.");
                    break;
                case "CellGap": value = double.Parse(text, CultureInfo.InvariantCulture); type = typeof(double); initial = 0d; break;
                case "X": case "Y": case "XPercent": case "YPercent": case "XPixel": case "YPixel":
                    value = double.Parse(text, CultureInfo.InvariantCulture); type = typeof(double?); initial = null!;
                    CuiAbsoluteLayout.Validate(_control, name, (double)value);
                    break;
                case "XInherited": case "YInherited":
                    if (_control is not CuiContainerControl) throw new InvalidOperationException("Inherited coordinate axes require a Container.");
                    value = bool.Parse(text); type = typeof(bool); initial = false; break;
                case "CellMargin": value = Thickness.Parse(text); type = typeof(Thickness); initial = default(Thickness); break;
                case "Orientation": value = Enum.Parse<Orientation>(text, true); type = typeof(Orientation); initial = Orientation.Horizontal; break;
                case "ScrollBarThickness":
                    if (_control is not CuiContainerControl) return false;
                    value = double.Parse(text, CultureInfo.InvariantCulture); type = typeof(double); initial = 0d;
                    if (!double.IsFinite((double)value) || (double)value is < 0 or > 64) throw new FormatException("ScrollBarThickness must be from 0 to 64 DIP.");
                    break;
                case "HorizontalScrolling": case "VerticalScrolling": case "HorizontalOverflowArrows": case "FollowAtEnd": case "BringIntoView":
                    if (_control is not CuiContainerControl) return false;
                    value = bool.Parse(text); type = typeof(bool); initial = false; break;
                default: return false;
            }
            var key = (name, level, source);
            if (_lastLiterals.TryGetValue(key, out var previous) && previous == text) return true;
            if (_owned.Add(name))
                CuiPropertyPresentation.Register(_control, name, type, initial,
                    next => Apply(new CuiEffectiveProperty(name, type, next, CuiPropertyLevel.BuiltIn, "CUI layout projection", 0)));
            CuiPropertyPresentation.Set(_control, name, value, level, source, order); _lastLiterals[key] = text;
            return true;
        }
        private void Apply(CuiEffectiveProperty property)
        {
            switch (property.Name)
            {
                case "X": case "Y": case "XPercent": case "YPercent": case "XPixel": case "YPixel":
                    CuiAbsoluteLayout.Apply(_control, property.Name, (double?)property.Value); break;
                case "XInherited": case "YInherited":
                    ((CuiContainerControl)_control).SetInheritedAxis(property.Name == "XInherited", (bool)property.Value!); break;
                case "RowSizes": RequireGrid().RowDefinitions = (RowDefinitions)property.Value!; break;
                case "ColumnSizes": RequireGrid().ColumnDefinitions = (ColumnDefinitions)property.Value!; break;
                case "Rows": RequireGrid().RowDefinitions = new RowDefinitions(string.Join(',', Enumerable.Repeat("*", (int)property.Value!))); break;
                case "Columns": RequireGrid().ColumnDefinitions = new ColumnDefinitions(string.Join(',', Enumerable.Repeat("*", (int)property.Value!))); break;
                case "Row": Grid.SetRow(_control, (int)property.Value!); break;
                case "Column": Grid.SetColumn(_control, (int)property.Value!); break;
                case "RowSpan": Grid.SetRowSpan(_control, (int)property.Value!); break;
                case "ColumnSpan": Grid.SetColumnSpan(_control, (int)property.Value!); break;
                case "CellGap":
                    if (_control is CuiContainerControl container) container.SetCellGap((double)property.Value!);
                    else if (_control is Grid grid) { grid.RowSpacing = (double)property.Value!; grid.ColumnSpacing = (double)property.Value!; }
                    else if (_control is StackPanel stack) stack.Spacing = (double)property.Value!;
                    else throw new InvalidOperationException("CellGap requires a grid, stack or wrap Container.");
                    break;
                case "CellMargin":
                    // Each item has an independent inset; authored child Margin is never overwritten.
                    if (_control is CuiContainerControl marginContainer) marginContainer.SetCellMargin((Thickness)property.Value!);
                    else throw new InvalidOperationException("CellMargin requires a Container.");
                    break;
                case "Orientation":
                    if ((_control as CuiContainerControl)?.Layout is WrapPanel wrap) wrap.Orientation = (Orientation)property.Value!;
                    else if (_control is WrapPanel nativeWrap) nativeWrap.Orientation = (Orientation)property.Value!;
                    else throw new InvalidOperationException("Orientation is declared by stack Type; only Wrap exposes Orientation.");
                    break;
                case "HorizontalScrolling": ((CuiContainerControl)_control).SetScrolling(horizontal: (bool)property.Value!); break;
                case "VerticalScrolling": ((CuiContainerControl)_control).SetScrolling(vertical: (bool)property.Value!); break;
                case "HorizontalOverflowArrows": ((CuiContainerControl)_control).SetHorizontalOverflowArrows((bool)property.Value!); break;
                case "ScrollBarThickness": ((CuiContainerControl)_control).SetScrollBarThickness((double)property.Value!); break;
                case "FollowAtEnd": ((CuiContainerControl)_control).SetFollowAtEnd((bool)property.Value!); break;
                case "BringIntoView": SetBringIntoView((bool)property.Value!); break;
            }
        }
        // Declarative, one-shot viewport reveal. The app supplies the state (for example selection),
        // never a native-control search or focus command. Wait for actual layout after Repeat has
        // inserted/arranged the SAME element; ongoing manual scrolling does not retrigger a true value.
        private void SetBringIntoView(bool enabled)
        {
            if (_disposed || _bringIntoView == enabled) return;
            _bringIntoView = enabled;
            if (enabled)
            {
                _control.AttachedToVisualTree += OnRevealAttached;
                _control.DetachedFromVisualTree += OnRevealDetached;
                RequestReveal();
            }
            else
            {
                _control.AttachedToVisualTree -= OnRevealAttached;
                _control.DetachedFromVisualTree -= OnRevealDetached;
                CancelReveal();
            }
        }
        private void OnRevealAttached(object? sender, VisualTreeAttachmentEventArgs args) => RequestReveal();
        private void OnRevealDetached(object? sender, VisualTreeAttachmentEventArgs args) => CancelReveal();
        private void RequestReveal()
        {
            if (!_bringIntoView || _revealPending) return;
            _revealPending = true;
            _control.LayoutUpdated += RevealAfterLayout;
            _control.InvalidateArrange(); // A selection-only binding need not otherwise cause layout.
        }
        private void CancelReveal()
        {
            _revealPending = false;
            _control.LayoutUpdated -= RevealAfterLayout;
        }
        private void RevealAfterLayout(object? sender, EventArgs args)
        {
            if (!_bringIntoView || !_revealPending || TopLevel.GetTopLevel(_control) is null || !_control.IsEffectivelyVisible
                || !_control.IsMeasureValid || !_control.IsArrangeValid || _control.Bounds.Width <= 0 || _control.Bounds.Height <= 0) return;
            var viewport = _control.GetVisualAncestors().OfType<ScrollContentPresenter>().FirstOrDefault();
            if (viewport is not null && (!viewport.IsArrangeValid || viewport.Viewport.Width <= 0 || viewport.Viewport.Height <= 0)) return;
            CancelReveal(); // Retire the request before changing offset can cause another layout.
            viewport?.BringDescendantIntoView(_control, new Rect(_control.Bounds.Size));
        }
        public void Dispose() { SetBringIntoView(false); _disposed = true; }
    }
}
