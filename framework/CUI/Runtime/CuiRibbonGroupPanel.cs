using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// CUI's labelled command group - the construct a notebook bar / ribbon is built from. The Stage A mirrors
/// measured 4,046 donor ribbon groups (LibreOffice <code>sfxlo-NotebookbarToolBox</code> and
/// <code>sfxlo-Notebookbox</code> regions) that CUI could only flatten into a stack, which lost both the
/// group's caption and its per-group overflow.
///
/// Behaviour, mirroring what the donor actually does:
///  * the authored <c>GroupLabel="..."</c> caption is rendered under the command row and is not an item;
///  * the command row collapses exactly the items the measured width cannot hold, and presents the author's
///    <c>OverflowHost="true"</c> surface only once something collapsed - the same contract as
///    <see cref="CuiToolbarPanel"/>, and collapsing the CELL never the authored control;
///  * the group's rule and caption follow the theme line brush, observed live, so it re-themes with the app.
///
/// The caption and rule are renderer-private elements of this CUI-owned panel; the authored surface is still
/// plain CUI (a Container Type with typed properties), which is the boundary CUI requires.
/// </summary>
public sealed class CuiRibbonGroupPanel : Panel, IDisposable
{
    private const double DefaultThickness = 1;
    private readonly TextBlock _caption = new() { Margin = new Thickness(2, 2, 0, 0) };
    private readonly Rectangle _rule = new();
    private readonly List<IDisposable> _subscriptions = [];
    private double _spacing;
    private object? _lineBrush;
    private object? _thicknessResource;
    private bool _disposed;

    public CuiRibbonGroupPanel()
    {
        Children.Add(_caption);
        Children.Add(_rule);
        _subscriptions.Add(this.GetResourceObservable("CuiLineBrush")
            .Subscribe(new ResourceObserver(value => { _lineBrush = value; ApplyTheme(); })));
        _subscriptions.Add(this.GetResourceObservable("CuiSeparatorThickness")
            .Subscribe(new ResourceObserver(value => { _thicknessResource = value; InvalidateMeasure(); })));
        ApplyTheme();
    }

    public double Spacing
    {
        get => _spacing;
        set
        {
            if (Math.Abs(_spacing - value) < 0.0001) return;
            _spacing = value;
            InvalidateMeasure();
        }
    }

    /// <summary>The authored GroupLabel caption, rendered below the command row.</summary>
    public string? Label
    {
        get => _caption.Text;
        set
        {
            if (_caption.Text == value) return;
            _caption.Text = value;
            _caption.IsVisible = !string.IsNullOrWhiteSpace(value);
            InvalidateMeasure();
        }
    }

    public TextBlock CaptionElement => _caption;
    public Rectangle RuleElement => _rule;

    public int VisibleItemCount { get; private set; }
    public int HiddenItemCount { get; private set; }
    public IReadOnlyList<Control> HiddenItems => _hidden;
    private readonly List<Control> _hidden = [];

    private IEnumerable<Control> Cells()
    {
        foreach (var child in Children)
            if (!ReferenceEquals(child, _caption) && !ReferenceEquals(child, _rule))
                yield return child;
    }

    private bool IsOverflowHost(Control cell)
    {
        var authored = cell is Border bordered ? bordered.Child ?? cell : cell;
        return CuiOverflowHosts.IsHost(authored) || CuiOverflowHosts.IsHost(cell);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _hidden.Clear();
        var constrained = !double.IsInfinity(availableSize.Width);
        var budget = constrained ? Math.Max(0, availableSize.Width) : double.PositiveInfinity;

        _caption.Measure(new Size(budget, double.PositiveInfinity));
        var captionHeight = string.IsNullOrWhiteSpace(_caption.Text) ? 0 : _caption.DesiredSize.Height;

        var host = default(Control?);
        var hostDesired = default(Size);
        var order = new List<(Control Child, Size Desired)>();
        foreach (var cell in Cells())
        {
            cell.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            if (IsOverflowHost(cell)) { host = cell; hostDesired = cell.DesiredSize; continue; }
            order.Add((cell, cell.DesiredSize));
        }

        var gaps = order.Count > 1 ? _spacing * (order.Count - 1) : 0;
        var totalAll = 0d;
        foreach (var (_, desired) in order) totalAll += desired.Width;
        totalAll += gaps;

        var mustCollapse = constrained && totalAll > budget;
        var fitBudget = mustCollapse && host is not null ? Math.Max(0, budget - hostDesired.Width - _spacing) : budget;

        var used = 0d;
        var visible = 0;
        var rowHeight = 0d;
        foreach (var (child, desired) in order)
        {
            var width = desired.Width + (visible > 0 ? _spacing : 0);
            if (constrained && used + width > fitBudget)
            {
                child.IsVisible = false;
                _hidden.Add(child);
                continue;
            }
            child.IsVisible = true;
            used += width;
            visible++;
            rowHeight = Math.Max(rowHeight, desired.Height);
        }
        HiddenItemCount = _hidden.Count;
        VisibleItemCount = visible;
        if (host is not null)
        {
            host.IsVisible = HiddenItemCount > 0;
            if (HiddenItemCount > 0)
            {
                used += _spacing + hostDesired.Width;
                rowHeight = Math.Max(rowHeight, hostDesired.Height);
            }
        }

        var width2 = Math.Min(budget, Math.Max(used, _caption.DesiredSize.Width));
        return new Size(width2, rowHeight + captionHeight + Thickness());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0d;
        var rowHeight = 0d;
        Control? host = null;
        foreach (var cell in Cells())
        {
            if (IsOverflowHost(cell)) { host = cell; continue; }
            if (!cell.IsVisible) { cell.Arrange(new Rect(0, 0, 0, 0)); continue; }
            var width = cell.DesiredSize.Width;
            cell.Arrange(new Rect(x, 0, Math.Max(0, width), Math.Max(cell.DesiredSize.Height, 1)));
            x += width + _spacing;
            rowHeight = Math.Max(rowHeight, cell.DesiredSize.Height);
        }
        if (host is not null && host.IsVisible)
        {
            x = Math.Max(0, Math.Min(x, finalSize.Width - host.DesiredSize.Width));
            host.Arrange(new Rect(x, 0, host.DesiredSize.Width, Math.Max(host.DesiredSize.Height, 1)));
        }

        if (!string.IsNullOrWhiteSpace(_caption.Text))
        {
            _caption.Arrange(new Rect(0, rowHeight, finalSize.Width, _caption.DesiredSize.Height));
            rowHeight += _caption.DesiredSize.Height;
        }
        var thickness = Thickness();
        var y = Math.Max(0, finalSize.Height - thickness);
        _rule.Arrange(new Rect(0, y, finalSize.Width, Math.Max(thickness, finalSize.Height - 2 * y)));
        return finalSize;
    }

    private double Thickness() =>
        _thicknessResource is double size && size > 0 ? size : DefaultThickness;

    /// <summary>Caption and rule follow the theme line brush, observed live: no hard-coded colour and no
    /// invented default - if the theme has not resolved a line brush, the rule simply does not paint.</summary>
    private void ApplyTheme()
    {
        if (_disposed) return;
        var brush = _lineBrush as IBrush;
        _rule.Fill = brush;
        if (brush is not null) _caption.Foreground = brush;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var subscription in _subscriptions) subscription.Dispose();
        _subscriptions.Clear();
    }

    private sealed class ResourceObserver(Action<object?> next) : IObserver<object?>
    {
        public void OnNext(object? value) => next(value);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
