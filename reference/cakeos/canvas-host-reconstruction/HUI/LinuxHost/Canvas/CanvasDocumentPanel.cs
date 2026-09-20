using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;
using HuiText = Haven.UI.Components.Text;

namespace CakeOS.HuiLinuxHost.Canvas;

/// <summary>
/// Rnote document sidebar equivalent: layout, background (colour / pattern /
/// pattern size), page format (size / DPI), snap, export preferences and
/// document export. All controls write through to the real Rnote document and
/// export prefs; formats map 1:1 to Rnote's SVG / PDF / XOPP exporters.
/// </summary>
internal sealed class CanvasDocumentPanel : Container
{
    private static readonly string[] LayoutLabels = ["Fixed size", "Continuous vertical", "Semi-infinite", "Infinite"];
    private static readonly CanvasLayout[] LayoutValues =
        [CanvasLayout.FixedSize, CanvasLayout.ContinuousVertical, CanvasLayout.SemiInfinite, CanvasLayout.Infinite];

    private static readonly string[] PatternLabels = ["None", "Lines", "Grid", "Dots", "Isometric grid", "Isometric dots"];
    private static readonly CanvasPattern[] PatternValues =
        [CanvasPattern.None, CanvasPattern.Lines, CanvasPattern.Grid, CanvasPattern.Dots,
         CanvasPattern.IsometricGrid, CanvasPattern.IsometricDots];

    private readonly Select _layout;
    private readonly Select _pattern;
    private readonly Input _backgroundHex;
    private readonly Input _patternColorHex;
    private readonly Input _formatWidth;
    private readonly Input _formatHeight;
    private readonly Input _formatDpi;
    private readonly Toggle _snap;
    private readonly Toggle _withBackground;
    private readonly Toggle _withPattern;
    private readonly Toggle _optimizePrinting;
    private readonly HuiButton _exportSvg;
    private readonly HuiButton _exportPdf;
    private readonly HuiButton _exportXopp;
    private readonly HuiButton _exportSelection;
    private bool _updating;

    public CanvasDocumentPanel()
    {
        Name = "Canvas.Document";
        Layout = HavenLayout.Vertical;
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.Gap, HavenLength.Px(6));
        SetValue(HavenProperties.Padding, HavenThickness.Parse("8px"));
        SetValue(HavenProperties.Background, "SurfaceRaised");
        SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));

        Add(Caption("Page — layout, background, format (Rnote document)"));

        _layout = MakeSelect("Canvas.Layout", LayoutLabels, 3);
        _layout.SelectionChanged += (_, _) =>
        {
            if (_updating || _layout.SelectedIndex < 0) return;
            LayoutChanged?.Invoke(LayoutValues[_layout.SelectedIndex]);
        };
        Add(Labeled("Layout", _layout));

        _pattern = MakeSelect("Canvas.Background.Pattern", PatternLabels, 3);
        _pattern.SelectionChanged += (_, _) =>
        {
            if (_updating || _pattern.SelectedIndex < 0) return;
            BackgroundPatternChanged?.Invoke(PatternValues[_pattern.SelectedIndex]);
        };
        Add(Labeled("Pattern", _pattern));

        _backgroundHex = MakeInput("Canvas.Background.Color", "#FFFFFF");
        _backgroundHex.TextChanged += (_, _) => EmitColor(_backgroundHex, c => BackgroundColorChanged?.Invoke(c));
        Add(Labeled("Background #", _backgroundHex));

        _patternColorHex = MakeInput("Canvas.Pattern.Color", "#CCE5FF");
        _patternColorHex.TextChanged += (_, _) => EmitColor(_patternColorHex, c => PatternColorChanged?.Invoke(c));
        Add(Labeled("Pattern #", _patternColorHex));

        _formatWidth = MakeInput("Canvas.Format.Width", "1123");
        _formatHeight = MakeInput("Canvas.Format.Height", "1587");
        _formatDpi = MakeInput("Canvas.Format.Dpi", "96");
        var formatRow = new Container { Name = "Canvas.Format.Row", Layout = HavenLayout.Horizontal };
        formatRow.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        formatRow.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        formatRow.Add(_formatWidth);
        formatRow.Add(_formatHeight);
        formatRow.Add(_formatDpi);
        _formatWidth.TextChanged += (_, _) => EmitFormat();
        _formatHeight.TextChanged += (_, _) => EmitFormat();
        _formatDpi.TextChanged += (_, _) => EmitDpi();
        Add(Labeled("Format W / H / DPI", formatRow));

        _snap = MakeToggle("Canvas.Snap", false);
        _snap.CheckedChanged += (_, _) =>
        {
            if (_updating) return;
            SnapChanged?.Invoke(_snap.IsChecked);
        };
        Add(Labeled("Snap to grid", _snap));

        Add(Caption("Export — prefs + document / selection (Rnote exporters)"));
        _withBackground = MakeToggle("Canvas.Export.Background", true);
        _withPattern = MakeToggle("Canvas.Export.Pattern", true);
        _optimizePrinting = MakeToggle("Canvas.Export.Optimize", false);
        _withBackground.CheckedChanged += (_, _) => EmitExportPrefs();
        _withPattern.CheckedChanged += (_, _) => EmitExportPrefs();
        _optimizePrinting.CheckedChanged += (_, _) => EmitExportPrefs();
        Add(Labeled("With background", _withBackground));
        Add(Labeled("With pattern", _withPattern));
        Add(Labeled("Optimize print", _optimizePrinting));

        var exportRow = new Container { Name = "Canvas.Export.Row", Layout = HavenLayout.Wrap };
        exportRow.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        exportRow.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        _exportSvg = MakeCommand("Canvas.Export.Svg", "SVG", () => ExportRequested?.Invoke(CanvasDocExportFormat.Svg));
        _exportPdf = MakeCommand("Canvas.Export.Pdf", "PDF", () => ExportRequested?.Invoke(CanvasDocExportFormat.Pdf));
        _exportXopp = MakeCommand("Canvas.Export.Xopp", "XOPP", () => ExportRequested?.Invoke(CanvasDocExportFormat.Xopp));
        _exportSelection = MakeCommand("Canvas.Export.Selection", "Selection SVG", () => SelectionExportRequested?.Invoke());
        exportRow.Add(_exportSvg);
        exportRow.Add(_exportPdf);
        exportRow.Add(_exportXopp);
        exportRow.Add(_exportSelection);
        Add(exportRow);
    }

    public event Action<CanvasLayout>? LayoutChanged;
    public event Action<CanvasPattern>? BackgroundPatternChanged;
    public event Action<CanvasRgba>? BackgroundColorChanged;
    public event Action<CanvasRgba>? PatternColorChanged;
    public event Action<double, double>? FormatSizeChanged;
    public event Action<double>? FormatDpiChanged;
    public event Action<bool>? SnapChanged;
    public event Action<bool, bool, bool>? ExportPrefsChanged;
    public event Action<CanvasDocExportFormat>? ExportRequested;
    public event Action? SelectionExportRequested;

    public void ApplyDocumentState(CanvasLayout layout, CanvasPattern pattern)
    {
        _updating = true;
        try
        {
            _layout.SelectedIndex = Array.IndexOf(LayoutValues, layout);
            _pattern.SelectedIndex = Array.IndexOf(PatternValues, pattern);
        }
        finally { _updating = false; }
    }

    private void EmitColor(Input input, Action<CanvasRgba> emit)
    {
        if (_updating) return;
        if (CanvasColorPanel.TryParseHex(input.Text, out var color))
            emit(color);
    }

    private void EmitFormat()
    {
        if (_updating) return;
        if (double.TryParse(_formatWidth.Text, out var w) && double.TryParse(_formatHeight.Text, out var h))
            FormatSizeChanged?.Invoke(w, h);
    }

    private void EmitDpi()
    {
        if (_updating) return;
        if (double.TryParse(_formatDpi.Text, out var dpi))
            FormatDpiChanged?.Invoke(dpi);
    }

    private void EmitExportPrefs()
    {
        if (_updating) return;
        ExportPrefsChanged?.Invoke(_withBackground.IsChecked, _withPattern.IsChecked, _optimizePrinting.IsChecked);
    }

    private static HuiText Caption(string text)
    {
        var label = new HuiText { Content = text };
        label.SetValue(HavenProperties.FontSize, 12d);
        label.SetValue(HavenProperties.Foreground, "TextSecondary");
        return label;
    }

    private static Container Labeled(string label, HavenElement control)
    {
        var row = new Container { Layout = HavenLayout.Horizontal };
        row.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        row.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        row.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        var text = new HuiText { Content = label };
        text.SetValue(HavenProperties.FontSize, 12d);
        text.SetValue(HavenProperties.Foreground, "TextSecondary");
        text.SetValue(HavenProperties.Width, HavenLength.Px(120));
        row.Add(text);
        row.Add(control);
        return row;
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

    private static Input MakeInput(string name, string placeholder)
    {
        var input = new Input { Name = name, Placeholder = placeholder };
        input.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        input.SetValue(HavenProperties.MinHeight, HavenLength.Px(40));
        input.Accessibility.AccessibleName = name;
        return input;
    }

    private static Toggle MakeToggle(string name, bool value)
    {
        var toggle = new Toggle { Name = name, IsChecked = value };
        toggle.Accessibility.AccessibleName = name;
        return toggle;
    }

    private static HuiButton MakeCommand(string name, string content, Action onInvoke)
    {
        var button = new HuiButton { Name = name, Content = content };
        button.SetValue(HavenProperties.Height, HavenLength.Px(36));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
        button.SetValue(HavenProperties.FontSize, 13d);
        button.Accessibility.AccessibleName = content;
        button.Invoked += (_, _) => onInvoke();
        return button;
    }
}
