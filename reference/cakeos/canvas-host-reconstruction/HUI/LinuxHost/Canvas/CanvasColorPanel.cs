using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;
using HuiText = Haven.UI.Components.Text;

namespace CakeOS.HuiLinuxHost.Canvas;

/// <summary>
/// Rnote colour-row equivalent: stroke + fill colours with donor palette
/// presets and hex editing. HUI has no shared colour picker (see
/// NEEDS-FROM-W4), so presets + hex Input are the faithful Phase-1 mapping;
/// every change writes through to the real engine config.
/// </summary>
internal sealed class CanvasColorPanel : Container
{
    private static readonly (string Label, CanvasRgba Color)[] StrokePresets =
    [
        ("Black", new CanvasRgba(0, 0, 0, 1)),
        ("Red", new CanvasRgba(0.88, 0.11, 0.14, 1)),
        ("Orange", new CanvasRgba(1, 0.47, 0, 1)),
        ("Yellow", new CanvasRgba(0.96, 0.83, 0.18, 1)),
        ("Green", new CanvasRgba(0.2, 0.82, 0.48, 1)),
        ("Blue", new CanvasRgba(0.21, 0.52, 0.89, 1)),
        ("Purple", new CanvasRgba(0.57, 0.26, 0.67, 1)),
        ("White", new CanvasRgba(1, 1, 1, 1)),
    ];

    private static readonly (string Label, CanvasRgba Color)[] FillPresets =
    [
        ("None", new CanvasRgba(0, 0, 0, 0)),
        ("Red", new CanvasRgba(0.88, 0.11, 0.14, 1)),
        ("Yellow", new CanvasRgba(0.96, 0.83, 0.18, 1)),
        ("Green", new CanvasRgba(0.2, 0.82, 0.48, 1)),
        ("Blue", new CanvasRgba(0.21, 0.52, 0.89, 1)),
        ("White", new CanvasRgba(1, 1, 1, 1)),
    ];

    private readonly Input _strokeHex;
    private readonly Input _fillHex;
    private readonly HuiText _strokeValue;
    private readonly HuiText _fillValue;
    private bool _updating;

    public CanvasColorPanel()
    {
        Name = "Canvas.Colors";
        Layout = HavenLayout.Vertical;
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.Gap, HavenLength.Px(6));
        SetValue(HavenProperties.Padding, HavenThickness.Parse("8px"));
        SetValue(HavenProperties.Background, "SurfaceRaised");
        SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));

        var caption = new HuiText { Content = "Stroke / fill — Rnote colour pickers" };
        caption.SetValue(HavenProperties.FontSize, 12d);
        caption.SetValue(HavenProperties.Foreground, "TextSecondary");
        Add(caption);

        Add(MakeRowLabel("Stroke"));
        Add(MakePresetRow(StrokePresets, c => StrokeColorChanged?.Invoke(c)));
        _strokeHex = MakeHexInput("Canvas.Stroke.Hex", "#000000");
        _strokeHex.TextChanged += (_, _) => EmitHex(_strokeHex, v => StrokeColorChanged?.Invoke(v));
        Add(_strokeHex);
        _strokeValue = MakeValueLabel("Canvas.Stroke.Value");
        Add(_strokeValue);

        Add(MakeRowLabel("Fill"));
        Add(MakePresetRow(FillPresets, c => FillColorChanged?.Invoke(c)));
        _fillHex = MakeHexInput("Canvas.Fill.Hex", "#00000000");
        _fillHex.TextChanged += (_, _) => EmitHex(_fillHex, v => FillColorChanged?.Invoke(v));
        Add(_fillHex);
        _fillValue = MakeValueLabel("Canvas.Fill.Value");
        Add(_fillValue);
    }

    public event Action<CanvasRgba>? StrokeColorChanged;
    public event Action<CanvasRgba>? FillColorChanged;

    public void ApplyState(CanvasRgba stroke, CanvasRgba fill)
    {
        _updating = true;
        try
        {
            _strokeHex.Text = ToHex(stroke);
            _fillHex.Text = ToHex(fill);
            _strokeValue.Content = $"Stroke {ToHex(stroke)}";
            _fillValue.Content = fill.A <= 0 ? "Fill none" : $"Fill {ToHex(fill)}";
        }
        finally { _updating = false; }
    }

    public static bool TryParseHex(string text, out CanvasRgba color)
    {
        color = default;
        var hex = text.Trim().TrimStart('#');
        if (hex.Length != 6 && hex.Length != 8)
            return false;
        try
        {
            var r = Convert.ToInt32(hex.Substring(0, 2), 16) / 255.0;
            var g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255.0;
            var b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255.0;
            var a = hex.Length == 8 ? Convert.ToInt32(hex.Substring(6, 2), 16) / 255.0 : 1.0;
            color = new CanvasRgba(r, g, b, a);
            return true;
        }
        catch { return false; }
    }

    public static string ToHex(CanvasRgba color)
    {
        var r = (int)Math.Clamp(Math.Round(color.R * 255), 0, 255);
        var g = (int)Math.Clamp(Math.Round(color.G * 255), 0, 255);
        var b = (int)Math.Clamp(Math.Round(color.B * 255), 0, 255);
        var a = (int)Math.Clamp(Math.Round(color.A * 255), 0, 255);
        return a >= 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{r:X2}{g:X2}{b:X2}{a:X2}";
    }

    private void EmitHex(Input input, Action<CanvasRgba> emit)
    {
        if (_updating) return;
        if (TryParseHex(input.Text, out var color))
            emit(color);
    }

    private static HuiText MakeRowLabel(string text)
    {
        var label = new HuiText { Content = text };
        label.SetValue(HavenProperties.FontSize, 12d);
        label.SetValue(HavenProperties.Foreground, "TextSecondary");
        return label;
    }

    private static HuiText MakeValueLabel(string name)
    {
        var label = new HuiText { Name = name, Content = string.Empty };
        label.SetValue(HavenProperties.FontSize, 12d);
        label.SetValue(HavenProperties.Foreground, "TextSecondary");
        return label;
    }

    private static Container MakePresetRow(
        (string Label, CanvasRgba Color)[] presets, Action<CanvasRgba> onPick)
    {
        var row = new Container { Name = "Canvas.Color.Presets", Layout = HavenLayout.Wrap };
        row.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        row.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        foreach (var (label, color) in presets)
        {
            var captured = color;
            var button = new HuiButton { Name = $"Canvas.Color.{label}", Content = label };
            button.SetValue(HavenProperties.Width, HavenLength.Px(76));
            button.SetValue(HavenProperties.Height, HavenLength.Px(32));
            button.SetValue(HavenProperties.MinHeight, HavenLength.Px(32));
            button.SetValue(HavenProperties.FontSize, 12d);
            button.Accessibility.AccessibleName = $"{label} {ToHex(color)}";
            button.Invoked += (_, _) => onPick(captured);
            row.Add(button);
        }
        return row;
    }

    private static Input MakeHexInput(string name, string placeholder)
    {
        var input = new Input { Name = name, Placeholder = placeholder };
        input.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        input.SetValue(HavenProperties.MinHeight, HavenLength.Px(40));
        input.Accessibility.AccessibleName = placeholder;
        return input;
    }
}
