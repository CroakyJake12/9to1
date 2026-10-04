namespace CakeOS.HuiWindowsHost.Canvas;

internal static class CanvasManagedBoundaryProof
{
    public static void Run()
    {
        using var source = new CanvasNativeSession();
        source.SetViewportSize(1280, 720);
        source.ZoomTo(1.5);
        source.PanBy(240, -120);
        source.SetTool(CanvasTool.Pen);
        source.BeginStroke(110, 105, 0.25, 9, -4);
        source.UpdateStroke(155, 135, 0.55, 7, -3);
        source.UpdateStroke(205, 170, 0.85, 5, -2);
        source.EndStroke(250, 205, 0.6, 3, -1);

        source.SetTool(CanvasTool.Highlighter);
        source.BeginStroke(125, 170, 0.5);
        source.EndStroke(265, 170, 0.5);

        source.SetShape(CanvasShape.Ellipse);
        source.SetTool(CanvasTool.Shape);
        source.BeginStroke(290, 100, 0.5);
        source.EndStroke(380, 180, 0.5);

        source.SetTool(CanvasTool.Selector);
        source.BeginStroke(95, 85, 0.5);
        source.UpdateStroke(410, 85, 0.5);
        source.UpdateStroke(410, 235, 0.5);
        source.UpdateStroke(95, 235, 0.5);
        source.EndStroke(95, 85, 0.5);

        if (!source.Undo() || !source.Redo())
            throw new InvalidOperationException("Managed Canvas proof could not undo and redo a completed Rnote operation.");

        var before = source.RenderSvg();
        var payload = source.SaveRnote();
        if (payload.Length <= 100)
            throw new InvalidOperationException("Managed Canvas persistence proof produced an unexpectedly small Rnote payload.");

        using var restored = CanvasNativeSession.FromRnote(payload);
        var after = restored.RenderSvg();
        if (after.Bounds.Width <= 0 || after.Bounds.Height <= 0)
            throw new InvalidOperationException("Managed Canvas persistence proof restored invalid render bounds.");
        if (!after.Svg.Contains("<svg", StringComparison.Ordinal))
            throw new InvalidOperationException("Managed Canvas persistence proof restored a non-SVG render frame.");
        if (Math.Abs(before.Bounds.X - after.Bounds.X) > 0.001
            || Math.Abs(before.Bounds.Y - after.Bounds.Y) > 0.001
            || Math.Abs(before.Bounds.Width - after.Bounds.Width) > 0.001
            || Math.Abs(before.Bounds.Height - after.Bounds.Height) > 0.001)
        {
            throw new InvalidOperationException(
                $"Managed Canvas persistence proof changed render bounds: before={before.Bounds}, after={after.Bounds}.");
        }

        Console.WriteLine(
            $"CANVAS_RNOTE_MANAGED_SAVE_RELOAD_READY bytes={payload.Length} width={after.Bounds.Width:0.###} height={after.Bounds.Height:0.###}");
    }
}
