using Haven.UI;
using Haven.UI.Components;

namespace CakeOS.HuiLinuxHost.Canvas;

/// <summary>
/// Rnote pens-sidebar page equivalent: per-tool configuration for the six
/// donor pens. Sections mirror rnote-ui penssidebar pages (brush, shaper,
/// typewriter, eraser, selector, tools). Built only from existing HUI
/// components (Select / Slider / Toggle / Text). Emits change events; the
/// controller applies them to the native session.
/// </summary>
internal sealed class CanvasPenConfigPanel : Container
{
    private static readonly string[] BrushStyleLabels = ["Marker", "Solid", "Textured"];
    private static readonly CanvasBrushStyle[] BrushStyleValues =
        [CanvasBrushStyle.Marker, CanvasBrushStyle.Solid, CanvasBrushStyle.Textured];

    private static readonly string[] BrushBuilderLabels = ["Simple", "Curved", "Modeled"];
    private static readonly CanvasBrushBuilder[] BrushBuilderValues =
        [CanvasBrushBuilder.Simple, CanvasBrushBuilder.Curved, CanvasBrushBuilder.Modeled];

    private static readonly string[] EraserStyleLabels = ["Trash colliding", "Split colliding"];
    private static readonly CanvasEraserStyle[] EraserStyleValues =
        [CanvasEraserStyle.TrashColliding, CanvasEraserStyle.SplitColliding];

    private static readonly string[] ShapeLabels =
        ["Line", "Arrow", "Rectangle", "Ellipse", "Grid", "CoordSys 2D", "CoordSys 3D",
         "Quadrant 2D", "Foci ellipse", "Quad bezier", "Cubic bezier", "Polyline", "Polygon"];
    private static readonly CanvasShape[] ShapeValues =
        [CanvasShape.Line, CanvasShape.Arrow, CanvasShape.Rectangle, CanvasShape.Ellipse,
         CanvasShape.Grid, CanvasShape.CoordSystem2D, CanvasShape.CoordSystem3D,
         CanvasShape.QuadrantCoordSystem2D, CanvasShape.FociEllipse, CanvasShape.QuadBez,
         CanvasShape.CubBez, CanvasShape.Polyline, CanvasShape.Polygon];

    private static readonly string[] ShaperStyleLabels = ["Smooth", "Rough"];
    private static readonly CanvasShaperStyle[] ShaperStyleValues =
        [CanvasShaperStyle.Smooth, CanvasShaperStyle.Rough];

    private static readonly string[] SelectorStyleLabels = ["Polygon", "Rectangle", "Single", "Intersecting path"];
    private static readonly CanvasSelectorStyle[] SelectorStyleValues =
        [CanvasSelectorStyle.Polygon, CanvasSelectorStyle.Rectangle, CanvasSelectorStyle.Single,
         CanvasSelectorStyle.IntersectingPath];

    private static readonly string[] ToolsStyleLabels = ["Vertical space", "Offset camera", "Zoom", "Laser"];
    private static readonly CanvasToolsStyle[] ToolsStyleValues =
        [CanvasToolsStyle.VerticalSpace, CanvasToolsStyle.OffsetCamera, CanvasToolsStyle.Zoom, CanvasToolsStyle.Laser];

    private readonly Container _brushSection;
    private readonly Container _shaperSection;
    private readonly Container _typewriterSection;
    private readonly Container _eraserSection;
    private readonly Container _selectorSection;
    private readonly Container _toolsSection;

    private readonly Select _brushStyle;
    private readonly Select _brushBuilder;
    private readonly Slider _brushWidth;
    private readonly Select _shaperBuilder;
    private readonly Select _shaperStyle;
    private readonly Slider _shaperWidth;
    private readonly Toggle _shaperConstraints;
    private readonly Slider _typewriterSize;
    private readonly Slider _typewriterWidth;
    private readonly Slider _eraserWidth;
    private readonly Select _eraserStyle;
    private readonly Select _selectorStyle;
    private readonly Toggle _selectorLockAspect;
    private readonly Select _toolsStyle;

    private bool _updating;

    public CanvasPenConfigPanel()
    {
        Name = "Canvas.PenConfig";
        Layout = HavenLayout.Vertical;
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.Gap, HavenLength.Px(8));

        _brushStyle = MakeSelect("Canvas.Brush.Style", BrushStyleLabels, 1);
        _brushBuilder = MakeSelect("Canvas.Brush.Builder", BrushBuilderLabels, 2);
        _brushWidth = MakeSlider("Canvas.Brush.Width", 0.1, 100, 2.0);
        _brushSection = MakeSection("Canvas.Brush.Section", "Brush — ink, builder, width",
            _brushStyle, _brushBuilder, _brushWidth);

        _shaperBuilder = MakeSelect("Canvas.Shaper.Builder", ShapeLabels, 2);
        _shaperStyle = MakeSelect("Canvas.Shaper.Style", ShaperStyleLabels, 0);
        _shaperWidth = MakeSlider("Canvas.Shaper.Width", 0.1, 100, 2.0);
        _shaperConstraints = MakeToggle("Canvas.Shaper.Constraints", false);
        _shaperSection = MakeSection("Canvas.Shaper.Section", "Shaper — shape, style, width, constraints",
            _shaperBuilder, _shaperStyle, _shaperWidth, _shaperConstraints);

        _typewriterSize = MakeSlider("Canvas.Typewriter.Size", 8, 128, 32);
        _typewriterSize.Step = 1;
        _typewriterWidth = MakeSlider("Canvas.Typewriter.Width", 100, 1600, 600);
        _typewriterWidth.Step = 10;
        _typewriterSection = MakeSection("Canvas.Typewriter.Section", "Typewriter — font size, text width",
            _typewriterSize, _typewriterWidth);

        _eraserWidth = MakeSlider("Canvas.Eraser.Width", 1, 100, 12);
        _eraserStyle = MakeSelect("Canvas.Eraser.Style", EraserStyleLabels, 0);
        _eraserSection = MakeSection("Canvas.Eraser.Section", "Eraser — width, mode",
            _eraserWidth, _eraserStyle);

        _selectorStyle = MakeSelect("Canvas.Selector.Style", SelectorStyleLabels, 1);
        _selectorLockAspect = MakeToggle("Canvas.Selector.LockAspect", false);
        _selectorSection = MakeSection("Canvas.Selector.Section", "Selector — capture, aspect lock",
            _selectorStyle, _selectorLockAspect);

        _toolsStyle = MakeSelect("Canvas.Tools.Style", ToolsStyleLabels, 0);
        _toolsSection = MakeSection("Canvas.Tools.Section", "Tools — utility mode",
            _toolsStyle);

        Add(_brushSection);
        Add(_shaperSection);
        Add(_typewriterSection);
        Add(_eraserSection);
        Add(_selectorSection);
        Add(_toolsSection);

        _brushStyle.SelectionChanged += (_, _) => EmitBrushStyle();
        _brushBuilder.SelectionChanged += (_, _) => EmitBrushBuilder();
        _brushWidth.ValueChanged += (_, _) => EmitBrushWidth();
        _shaperBuilder.SelectionChanged += (_, _) => EmitShaperBuilder();
        _shaperStyle.SelectionChanged += (_, _) => EmitShaperStyle();
        _shaperWidth.ValueChanged += (_, _) => EmitShaperWidth();
        _shaperConstraints.CheckedChanged += (_, _) => EmitShaperConstraints();
        _typewriterSize.ValueChanged += (_, _) => EmitTypewriterSize();
        _typewriterWidth.ValueChanged += (_, _) => EmitTypewriterWidth();
        _eraserWidth.ValueChanged += (_, _) => EmitEraserWidth();
        _eraserStyle.SelectionChanged += (_, _) => EmitEraserStyle();
        _selectorStyle.SelectionChanged += (_, _) => EmitSelectorStyle();
        _selectorLockAspect.CheckedChanged += (_, _) => EmitSelectorLock();
        _toolsStyle.SelectionChanged += (_, _) => EmitToolsStyle();

        SetTool(CanvasTool.Pen);
    }

    public event Action<CanvasBrushStyle>? BrushStyleChanged;
    public event Action<CanvasBrushBuilder>? BrushBuilderChanged;
    public event Action<double>? BrushWidthChanged;
    public event Action<CanvasShape>? ShaperBuilderChanged;
    public event Action<CanvasShaperStyle>? ShaperStyleChanged;
    public event Action<double>? ShaperWidthChanged;
    public event Action<bool>? ShaperConstraintsChanged;
    public event Action<double>? TypewriterFontSizeChanged;
    public event Action<double>? TypewriterTextWidthChanged;
    public event Action<double>? EraserWidthChanged;
    public event Action<CanvasEraserStyle>? EraserStyleChanged;
    public event Action<CanvasSelectorStyle>? SelectorStyleChanged;
    public event Action<bool>? SelectorLockAspectChanged;
    public event Action<CanvasToolsStyle>? ToolsStyleChanged;

    /// <summary>Shows the active pen's section first (all sections stay reachable, like the Rnote sidebar stack).</summary>
    public void SetTool(CanvasTool tool)
    {
        SetVisible(_brushSection, tool is CanvasTool.Pen or CanvasTool.Highlighter);
        SetVisible(_shaperSection, tool == CanvasTool.Shape);
        SetVisible(_typewriterSection, tool == CanvasTool.Typewriter);
        SetVisible(_eraserSection, tool == CanvasTool.Eraser);
        SetVisible(_selectorSection, tool == CanvasTool.Selector);
        SetVisible(_toolsSection, tool == CanvasTool.Tools);
    }

    public void ApplyBrushState(CanvasBrushStyle style, double width)
    {
        using var _ = Guard();
        _brushStyle.SelectedIndex = Array.IndexOf(BrushStyleValues, style);
        _brushWidth.Value = width;
    }

    public void ApplyBrushBuilder(CanvasBrushBuilder builder)
    {
        using var _ = Guard();
        _brushBuilder.SelectedIndex = Array.IndexOf(BrushBuilderValues, builder);
    }

    public void ApplyShaperState(CanvasShape builder, CanvasShaperStyle style, double width, bool constraints)
    {
        using var _ = Guard();
        _shaperBuilder.SelectedIndex = Array.IndexOf(ShapeValues, builder);
        _shaperStyle.SelectedIndex = Array.IndexOf(ShaperStyleValues, style);
        _shaperWidth.Value = width;
        _shaperConstraints.IsChecked = constraints;
    }

    public void ApplyEraserState(double width, CanvasEraserStyle style)
    {
        using var _ = Guard();
        _eraserWidth.Value = width;
        _eraserStyle.SelectedIndex = Array.IndexOf(EraserStyleValues, style);
    }

    public void ApplyTypewriterState(double fontSize, double textWidth)
    {
        using var _ = Guard();
        _typewriterSize.Value = fontSize;
        _typewriterWidth.Value = textWidth;
    }

    public void ApplySelectorState(CanvasSelectorStyle style, bool lockAspect)
    {
        using var _ = Guard();
        _selectorStyle.SelectedIndex = Array.IndexOf(SelectorStyleValues, style);
        _selectorLockAspect.IsChecked = lockAspect;
    }

    public void ApplyToolsState(CanvasToolsStyle style)
    {
        using var _ = Guard();
        _toolsStyle.SelectedIndex = Array.IndexOf(ToolsStyleValues, style);
    }

    private void EmitBrushStyle()
    {
        if (_updating || _brushStyle.SelectedIndex < 0) return;
        BrushStyleChanged?.Invoke(BrushStyleValues[_brushStyle.SelectedIndex]);
    }

    private void EmitBrushBuilder()
    {
        if (_updating || _brushBuilder.SelectedIndex < 0) return;
        BrushBuilderChanged?.Invoke(BrushBuilderValues[_brushBuilder.SelectedIndex]);
    }

    private void EmitBrushWidth()
    {
        if (_updating) return;
        BrushWidthChanged?.Invoke(_brushWidth.Value);
    }

    private void EmitShaperBuilder()
    {
        if (_updating || _shaperBuilder.SelectedIndex < 0) return;
        ShaperBuilderChanged?.Invoke(ShapeValues[_shaperBuilder.SelectedIndex]);
    }

    private void EmitShaperStyle()
    {
        if (_updating || _shaperStyle.SelectedIndex < 0) return;
        ShaperStyleChanged?.Invoke(ShaperStyleValues[_shaperStyle.SelectedIndex]);
    }

    private void EmitShaperWidth()
    {
        if (_updating) return;
        ShaperWidthChanged?.Invoke(_shaperWidth.Value);
    }

    private void EmitShaperConstraints()
    {
        if (_updating) return;
        ShaperConstraintsChanged?.Invoke(_shaperConstraints.IsChecked);
    }

    private void EmitTypewriterSize()
    {
        if (_updating) return;
        TypewriterFontSizeChanged?.Invoke(_typewriterSize.Value);
    }

    private void EmitTypewriterWidth()
    {
        if (_updating) return;
        TypewriterTextWidthChanged?.Invoke(_typewriterWidth.Value);
    }

    private void EmitEraserWidth()
    {
        if (_updating) return;
        EraserWidthChanged?.Invoke(_eraserWidth.Value);
    }

    private void EmitEraserStyle()
    {
        if (_updating || _eraserStyle.SelectedIndex < 0) return;
        EraserStyleChanged?.Invoke(EraserStyleValues[_eraserStyle.SelectedIndex]);
    }

    private void EmitSelectorStyle()
    {
        if (_updating || _selectorStyle.SelectedIndex < 0) return;
        SelectorStyleChanged?.Invoke(SelectorStyleValues[_selectorStyle.SelectedIndex]);
    }

    private void EmitSelectorLock()
    {
        if (_updating) return;
        SelectorLockAspectChanged?.Invoke(_selectorLockAspect.IsChecked);
    }

    private void EmitToolsStyle()
    {
        if (_updating || _toolsStyle.SelectedIndex < 0) return;
        ToolsStyleChanged?.Invoke(ToolsStyleValues[_toolsStyle.SelectedIndex]);
    }

    private IDisposable Guard() => new UpdateScope(this);

    private sealed class UpdateScope : IDisposable
    {
        private readonly CanvasPenConfigPanel _panel;
        public UpdateScope(CanvasPenConfigPanel panel) { _panel = panel; _panel._updating = true; }
        public void Dispose() => _panel._updating = false;
    }

    private static void SetVisible(Container section, bool visible) =>
        section.SetValue(HavenProperties.Visibility,
            visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);

    private static Container MakeSection(string name, string caption, params HavenElement[] children)
    {
        var section = new Container { Name = name, Layout = HavenLayout.Vertical };
        section.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        section.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        section.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px"));
        section.SetValue(HavenProperties.Background, "SurfaceRaised");
        section.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        var label = new Text { Content = caption };
        label.SetValue(HavenProperties.FontSize, 12d);
        label.SetValue(HavenProperties.Foreground, "TextSecondary");
        section.Add(label);
        foreach (var child in children)
            section.Add(child);
        return section;
    }

    private static Select MakeSelect(string name, string[] items, int selected)
    {
        var select = new Select { Name = name };
        select.Items = items;
        select.SelectedIndex = selected;
        select.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        select.SetValue(HavenProperties.MinHeight, HavenLength.Px(40));
        select.Accessibility.AccessibleName = name;
        return select;
    }

    private static Slider MakeSlider(string name, double min, double max, double value)
    {
        var slider = new Slider { Name = name, Minimum = min, Maximum = max, Step = 0.1 };
        slider.Value = value;
        slider.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        slider.Accessibility.AccessibleName = name;
        return slider;
    }

    private static Toggle MakeToggle(string name, bool value)
    {
        var toggle = new Toggle { Name = name, IsChecked = value };
        toggle.Accessibility.AccessibleName = name;
        return toggle;
    }
}
