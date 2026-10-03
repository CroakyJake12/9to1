using Haven.Core.Mathematics;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.TickGenerators;

namespace Haven.Desktop.Mathematics;

/// <summary>Transient maintained ScottPlot projection of the shared canonical graph.
/// Decimal values remain canonical; plotting converts them to display coordinates.
/// Symbolic curves require a declared evaluator and are reported unavailable here.</summary>
public static class ScottPlotGraphAdapter
{
    public const string Implementation = "ScottPlot.Avalonia 5.1.59; canonical coordinate projection v1";
    public static GraphProjection Render(AvaPlot control, GraphDefinition graph, MathServiceLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        var bounds = limits ?? new(); var captured = MathObjectCodec.Capture(graph, bounds);
        var axes = captured.Axes;
        var left = (double)axes.XMinimum; var right = (double)axes.XMaximum;
        var bottom = (double)axes.YMinimum; var top = (double)axes.YMaximum;
        if (left >= right || bottom >= top) throw new NotSupportedException("GraphRangeBelowDisplayPrecision");
        var xSpacing = TickSpacing(left, right, axes.XGridSpacing, bounds.MaxGraphTickCount);
        var ySpacing = TickSpacing(bottom, top, axes.YGridSpacing, bounds.MaxGraphTickCount);
        // Prepare every coordinate before touching the live native plot. No partial projection
        // is exposed on validation failure and no source primitives are dropped or rewritten.
        var native = new List<(GraphPrimitive Primitive, Coordinates[] Coordinates)>();
        var unavailable = new List<Guid>();
        foreach (var primitive in captured.Primitives)
        {
            GraphCoordinate[]? points = primitive switch
            {
                GraphPoint point => [point.Position], GraphLine line => [line.Start, line.End],
                GraphCurve curve => curve.Points, GraphCoordinateTable table => table.Points,
                GraphRegion region => region.Boundary, _ => null
            };
            if (points is null) { unavailable.Add(primitive.PrimitiveID); continue; }
            var coordinates = points.Select(x => new Coordinates((double)x.X, (double)x.Y)).ToArray();
            if (primitive is GraphLine && coordinates[0] == coordinates[1])
                throw new NotSupportedException("GraphLineBelowDisplayPrecision");
            native.Add((primitive, coordinates));
        }
        control.UserInputProcessor.Disable(); control.Menu = null;
        var plot = control.Plot; plot.Clear();
        plot.ShowAxesAndGrid();
        if (!axes.ShowAxes) plot.HideAxesAndGrid();
        plot.Grid.IsVisible = axes.ShowGrid;
        plot.Axes.SetLimits(left, right, bottom, top);
        plot.Axes.Bottom.TickGenerator = new NumericFixedInterval(xSpacing) { MaxTickCount = bounds.MaxGraphTickCount };
        plot.Axes.Left.TickGenerator = new NumericFixedInterval(ySpacing) { MaxTickCount = bounds.MaxGraphTickCount };
        plot.XLabel(axes.XLabel); plot.YLabel(axes.YLabel);
        foreach (var item in native)
        {
            switch (item.Primitive)
            {
                case GraphPoint: plot.Add.Marker(item.Coordinates[0], size: 12); break;
                case GraphCoordinateTable: plot.Add.ScatterPoints(item.Coordinates); break;
                case GraphLine or GraphCurve: plot.Add.ScatterLine(item.Coordinates); break;
                case GraphRegion: plot.Add.Polygon(item.Coordinates); break;
            }
        }
        control.Refresh();
        return new(captured.GraphID, captured.Revision, native.Select(x => x.Primitive.PrimitiveID).ToArray(),
            unavailable.ToArray(), Implementation);
    }
    private static double TickSpacing(double minimum, double maximum, decimal spacing, int maximumTicks)
    {
        var interval = (double)spacing;
        var required = (maximum - minimum) / interval + 4;
        if (!double.IsFinite(required) || required > maximumTicks)
            throw new NotSupportedException("GraphGridBudgetExceeded");
        return interval;
    }
}

public sealed record GraphProjection(Guid GraphID, long Revision, Guid[] RenderedPrimitiveIDs,
    Guid[] UnavailableSymbolicPrimitiveIDs, string Implementation);
