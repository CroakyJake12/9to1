using System.Globalization;

namespace Haven.UI.Components;

/// <summary>
/// Backend-neutral colour value. Hex is the interchange format: Rnote pen
/// colours and AppFlowy tag colours cross the HUI boundary as "#RRGGBB" without
/// exposing Avalonia/Flutter colour types.
/// </summary>
public readonly record struct HavenColour(byte R, byte G, byte B)
{
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public static HavenColour FromHex(string hex)
    {
        if (!TryParse(hex, out var colour))
            throw new FormatException($"'{hex}' is not a Haven colour. Use #RRGGBB.");
        return colour;
    }

    public static bool TryParse(string? value, out HavenColour colour)
    {
        colour = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (text.StartsWith('#')) text = text[1..];
        if (text.Length != 6) return false;
        if (!byte.TryParse(text.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)) return false;
        if (!byte.TryParse(text.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)) return false;
        if (!byte.TryParse(text.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return false;
        colour = new HavenColour(r, g, b);
        return true;
    }

    /// <summary>Neutral + pen-friendly swatches shared by Canvas and Boards.</summary>
    public static IReadOnlyList<string> DefaultPalette { get; } =
    [
        "#000000", "#FFFFFF", "#6B7280", "#E81123", "#F28C28",
        "#F2C230", "#2E9E44", "#2563EB", "#7C3AED", "#EC4899"
    ];
}

/// <summary>
/// Generic colour picker: swatch grid plus custom hex entry. The selected value
/// is a hex string; swatch fills set Background to the hex so capable backends
/// (Windows/Linux preview surfaces resolve #RRGGBB) paint the colour while
/// selection state and accessible names carry the semantics everywhere.
/// </summary>
public class ColourPicker : Container, IHavenKeyboardInputTarget
{
    private readonly List<Button> _swatches = [];
    private IReadOnlyList<string> _palette = HavenColour.DefaultPalette;
    private string _selectedHex = "#000000";
    private bool _syncing;

    public ColourPicker()
    {
        Accessibility.Role = HavenAccessibleRole.Group;
        Accessibility.AccessibleName = "Colour picker";
        Accessibility.Focusable = true;
        Layout = HavenLayout.Vertical;
        SetValue(HavenProperties.Gap, HavenLength.Px(8), HavenValueSource.Default);
        SetValue(HavenProperties.Background, "Transparent", HavenValueSource.Default);

        SwatchGrid = new Container { Name = "ColourPicker.Swatches", Layout = HavenLayout.Wrap };
        SwatchGrid.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SwatchGrid.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        Add(SwatchGrid);

        CustomRow = new Container { Name = "ColourPicker.Custom", Layout = HavenLayout.Horizontal };
        CustomRow.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        CustomRow.SetValue(HavenProperties.Gap, HavenLength.Px(6));

        CustomField = new Input { Placeholder = "#RRGGBB" };
        CustomField.Accessibility.AccessibleName = "Custom colour hex value";
        CustomField.SetValue(HavenProperties.Width, HavenLength.Fr(1));
        CustomField.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
        CustomField.TextChanged += (_, _) => OnCustomText();
        CustomRow.Add(CustomField);
        Add(CustomRow);

        RebuildSwatches();
        SelectedHex = _selectedHex;
    }

    public Container SwatchGrid { get; }
    public Container CustomRow { get; }
    public Input CustomField { get; }
    public IReadOnlyList<Button> SwatchButtons => _swatches;

    public event EventHandler? SelectedChanged;

    public IReadOnlyList<string> Palette
    {
        get => _palette;
        set
        {
            _palette = value?.Where(hex => HavenColour.TryParse(hex, out _)).ToArray() ?? [];
            RebuildSwatches();
            if (!Contains(_selectedHex) && _palette.Count > 0) SelectedHex = _palette[0];
            else SyncSelection();
        }
    }

    public string SelectedHex
    {
        get => _selectedHex;
        set
        {
            var next = Normalize(value);
            if (_selectedHex == next) return;
            _selectedHex = next;
            SyncSelection();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public HavenColour SelectedColour => HavenColour.FromHex(_selectedHex);

    public bool KeyDown(HavenKeyInput input)
    {
        if (_swatches.Count == 0) return false;
        return input.Key switch
        {
            HavenKey.Right or HavenKey.Down => Move(1, true),
            HavenKey.Left or HavenKey.Up => Move(-1, true),
            HavenKey.Home => MoveTo(0, true),
            HavenKey.End => MoveTo(_swatches.Count - 1, true),
            _ => false
        };
    }

    private bool Move(int direction, bool handled)
    {
        var current = IndexOf(_selectedHex);
        var next = Math.Clamp(current < 0 ? (direction >= 0 ? 0 : _swatches.Count - 1) : current + direction, 0, _swatches.Count - 1);
        return MoveTo(next, handled);
    }

    private bool MoveTo(int index, bool handled)
    {
        if (index < 0 || index >= _palette.Count) return false;
        SelectedHex = _palette[index];
        return handled;
    }

    private void OnCustomText()
    {
        if (_syncing) return;
        if (HavenColour.TryParse(CustomField.Text.Trim(), out var colour)) SelectedHex = colour.ToHex();
    }

    private void RebuildSwatches()
    {
        foreach (var child in SwatchGrid.Children.ToArray()) SwatchGrid.Remove(child);
        _swatches.Clear();
        foreach (var hex in _palette)
        {
            var normalized = HavenColour.FromHex(hex).ToHex();
            var swatch = new Button { Content = string.Empty, Variant = ButtonVariant.Icon, IconKey = string.Empty };
            swatch.Accessibility.Role = HavenAccessibleRole.Button;
            swatch.Accessibility.AccessibleName = $"Colour {normalized}";
            swatch.SetValue(HavenProperties.Width, HavenLength.Px(36));
            swatch.SetValue(HavenProperties.Height, HavenLength.Px(36));
            swatch.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
            swatch.SetValue(HavenProperties.Padding, HavenThickness.Zero);
            swatch.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(10)));
            swatch.SetValue(HavenProperties.Background, normalized);
            swatch.SetValue(HavenProperties.BorderColor, "Border");
            swatch.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
            swatch.Invoked += (_, _) => SelectedHex = normalized;
            SwatchGrid.Add(swatch);
            _swatches.Add(swatch);
        }
    }

    private void SyncSelection()
    {
        _syncing = true;
        try
        {
            if (!string.Equals(CustomField.Text.Trim(), _selectedHex, StringComparison.OrdinalIgnoreCase))
                CustomField.Text = _selectedHex;
        }
        finally
        {
            _syncing = false;
        }
        for (var i = 0; i < _swatches.Count; i++)
        {
            var selected = string.Equals(_palette[i], _selectedHex, StringComparison.OrdinalIgnoreCase);
            _swatches[i].SetState(HavenElementState.Selected, selected);
            _swatches[i].Accessibility.Selected = selected;
            _swatches[i].Accessibility.AccessibleName = selected ? $"Colour {_palette[i]} selected" : $"Colour {_palette[i]}";
        }
        Accessibility.AccessibleName = $"Colour picker, {_selectedHex} selected";
    }

    private bool Contains(string hex) => _palette.Any(candidate => string.Equals(candidate, hex, StringComparison.OrdinalIgnoreCase));
    private int IndexOf(string hex)
    {
        for (var i = 0; i < _palette.Count; i++)
            if (string.Equals(_palette[i], hex, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static string Normalize(string? value) =>
        HavenColour.TryParse(value, out var colour) ? colour.ToHex() : "#000000";

    public override HavenComponentMetadata Metadata => new(
        "ColourPicker",
        "HUI/shared/Components/ColourPicker/ColourPicker.cs",
        ["ColourPicker"],
        [],
        "Swatch grid plus hex entry; the value is backend-neutral hex.");
}

/// <summary>en-US alias; identical semantics to <see cref="ColourPicker"/>.</summary>
public sealed class ColorPicker : ColourPicker
{
    public override HavenComponentMetadata Metadata => new(
        "ColorPicker",
        "HUI/shared/Components/ColourPicker/ColourPicker.cs",
        ["ColourPicker"],
        [],
        "Alias of ColourPicker for en-US consumers.");
}
