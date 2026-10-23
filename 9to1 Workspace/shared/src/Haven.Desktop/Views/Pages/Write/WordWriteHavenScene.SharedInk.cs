using Haven.Application;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Pages.Write;

internal sealed partial class WordWriteHavenScene
{
    internal Container DrawingHost { get; } = new() { Name = "Write.Document.DrawingHost", Layout = HavenLayout.Canvas };
    internal WriteDocumentEditor? SharedInkEditor => _editor;
    internal event EventHandler? DrawingRequested;
    internal event EventHandler? PresentationChanged;
    internal event Action<string, double, bool>? DrawingStyleChanged;
    private bool _sharedInkConfigured;
    private string _drawingColour = "#FF000000";
    private double _drawingWidth = 2.5;
    private bool _drawingMarker;

    internal void ConfigureSharedInk(bool configured)
    {
        _sharedInkConfigured = configured;
        RebuildRibbon();
    }

    internal void SelectSharedInk(Guid blockId)
    {
        if (_editor is null) return;
        _editor.SelectBlock(blockId);
        OnDocumentSelectionChangedCore(this, EventArgs.Empty);
    }

    internal bool InsertSharedInkDrawing()
    {
        if (_editor is null) return false;
        _editor.InsertSharedInk();
        RebuildAll();
        return true;
    }

    private void AddSharedInkInsertion()
    {
        var drawing = Btn("Write.Insert.Drawing", "Drawing");
        drawing.Accessibility.Description = _sharedInkConfigured
            ? "Insert a shared drawing when the native drawing engine is available."
            : "Shared drawing is unavailable because a compatible Home drawing engine is not connected.";
        drawing.SetValue(HavenProperties.Enabled, _sharedInkConfigured);
        drawing.Invoked += (_, _) => OwnOriginalCallback(() => DrawingRequested?.Invoke(this, EventArgs.Empty));
        RibbonContent.Add(drawing);
    }

    private void AddSharedInkControls()
    {
        if (_editor?.SelectedBlock?.SharedProductivityBundles is null) return;
        foreach (var pair in new[] { ("Black", "#FF000000"), ("Blue", "#FF246BCE"), ("Red", "#FFD64545"), ("Green", "#FF238B57") })
        {
            var colour = Btn("Write.Drawing.Colour." + pair.Item1, pair.Item1);
            colour.Accessibility.AccessibleName = "Drawing ink " + pair.Item1.ToLowerInvariant();
            colour.Invoked += (_, _) => OwnOriginalCallback(() =>
            { _drawingColour = pair.Item2; PublishDrawingStyle(); });
            RibbonContent.Add(colour);
        }
        var width = Choice("Write.Drawing.Width", "Drawing stroke width", ["Thin", "Medium", "Thick"], _drawingWidth < 2 ? 0 : _drawingWidth > 4 ? 2 : 1);
        width.SelectionChanged += (_, _) => OwnOriginalCallback(() =>
        { _drawingWidth = width.SelectedIndex == 0 ? 1 : width.SelectedIndex == 2 ? 6 : 2.5; PublishDrawingStyle(); });
        RibbonContent.Add(width);
        var tool = Btn("Write.Drawing.Tool", _drawingMarker ? "Highlighter" : "Pen");
        tool.Accessibility.AccessibleName = "Drawing tool: " + (_drawingMarker ? "highlighter" : "pen");
        tool.Invoked += (_, _) => OwnOriginalCallback(() =>
        { _drawingMarker = !_drawingMarker; PublishDrawingStyle(); RebuildRibbon(); });
        RibbonContent.Add(tool);
    }

    private void PublishDrawingStyle() => DrawingStyleChanged?.Invoke(_drawingColour, _drawingWidth, _drawingMarker);
}
