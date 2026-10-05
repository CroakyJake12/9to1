using Avalonia;
using Avalonia.Controls;

namespace HavenOS.Forms;

/// <summary>Ordered native field layout. Requested columns reflow into fewer columns when the
/// available width cannot provide 240 logical pixels per field. This changes presentation only.</summary>
public sealed class FormNativePageColumns : Panel
{
    private readonly int _requestedColumns;
    private readonly double _gap;
    private int _columns = 1;
    private double[] _rowHeights = [];
    private Control[] _visible = [];
    public FormNativePageColumns(int requestedColumns, double gap)
    {
        if (requestedColumns is < 1 or > 64 || !double.IsFinite(gap) || gap < 0)
            throw new ArgumentOutOfRangeException(nameof(requestedColumns));
        _requestedColumns = requestedColumns; _gap = gap;
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        // Hidden Quiz groups stay attached to their original inputs, but reserve no layout slots.
        _visible = Children.Where(child => child.IsVisible).ToArray();
        var minimum = _visible.Length == 0 ? 240 : Math.Max(240, _visible.Max(child => child.MinWidth));
        _columns = Math.Min(_requestedColumns, Math.Max(1, _visible.Length));
        if (double.IsFinite(availableSize.Width))
            _columns = Math.Min(_columns, Math.Max(1, (int)Math.Min(64, Math.Floor((availableSize.Width + _gap) / (minimum + _gap)))));
        var width = double.IsFinite(availableSize.Width)
            ? Math.Max(0, (availableSize.Width - (_columns - 1) * _gap) / _columns) : double.PositiveInfinity;
        _rowHeights = new double[(_visible.Length + _columns - 1) / _columns];
        var desiredWidth = 0d;
        for (var i = 0; i < _visible.Length; i++)
        {
            _visible[i].Measure(new Size(width, double.PositiveInfinity));
            _rowHeights[i / _columns] = Math.Max(_rowHeights[i / _columns], _visible[i].DesiredSize.Height);
            desiredWidth = Math.Max(desiredWidth, _visible[i].DesiredSize.Width);
        }
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width
            : desiredWidth * _columns + (_columns - 1) * _gap,
            _rowHeights.Sum() + Math.Max(0, _rowHeights.Length - 1) * _gap);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = Math.Max(0, (finalSize.Width - (_columns - 1) * _gap) / _columns);
        var top = 0d;
        for (var row = 0; row < _rowHeights.Length; row++)
        {
            for (var column = 0; column < _columns; column++)
            {
                var i = row * _columns + column;
                if (i >= _visible.Length) break;
                _visible[i].Arrange(new Rect(column * (width + _gap), top, width, _rowHeights[row]));
            }
            top += _rowHeights[row] + _gap;
        }
        return finalSize;
    }
}
