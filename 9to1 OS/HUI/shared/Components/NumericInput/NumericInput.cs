using System.Globalization;

namespace Haven.UI.Components;

/// <summary>
/// Generic numeric input: a text field with stepper buttons, clamped range,
/// and step snapping (Slider-compatible semantics). Stroke width, zoom, column
/// widths, and counts compose this; strings never leak into engines.
/// </summary>
public sealed class NumericInput : Container
{
    private double _minimum;
    private double _maximum = 100d;
    private double _step = 1d;
    private int _precision = 2;
    private string _suffix = string.Empty;
    private bool _syncing;

    public NumericInput()
    {
        Accessibility.Role = HavenAccessibleRole.Group;
        Layout = HavenLayout.Horizontal;
        SetValue(HavenProperties.Gap, HavenLength.Px(4), HavenValueSource.Default);
        SetValue(HavenProperties.Background, "Transparent", HavenValueSource.Default);

        Decrease = new Button { Content = "−", Variant = ButtonVariant.Ghost };
        Decrease.Accessibility.AccessibleName = "Decrease value";
        Decrease.SetValue(HavenProperties.Width, HavenLength.Px(36));
        Decrease.SetValue(HavenProperties.MinHeight, HavenLength.Px(40));
        Decrease.Invoked += (_, _) => Nudge(-1);
        Add(Decrease);

        Field = new Input();
        Field.SetValue(HavenProperties.Width, HavenLength.Fr(1));
        Field.SetValue(HavenProperties.MinHeight, HavenLength.Px(40));
        Field.TextChanged += (_, _) => OnFieldText();
        Add(Field);

        SuffixLabel = new Text { Level = TextLevel.Caption, Content = string.Empty };
        SuffixLabel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Add(SuffixLabel);

        Increase = new Button { Content = "+", Variant = ButtonVariant.Ghost };
        Increase.Accessibility.AccessibleName = "Increase value";
        Increase.SetValue(HavenProperties.Width, HavenLength.Px(36));
        Increase.SetValue(HavenProperties.MinHeight, HavenLength.Px(40));
        Increase.Invoked += (_, _) => Nudge(1);
        Add(Increase);

        SyncField();
    }

    public Input Field { get; }
    public Button Decrease { get; }
    public Button Increase { get; }
    public Text SuffixLabel { get; }

    public event EventHandler? ValueChanged;

    public static readonly HavenProperty<double> ValueProperty =
        HavenPropertyRegistry.Register(new HavenProperty<double>("NumericInput.Value", 0d));

    public double Minimum
    {
        get => _minimum;
        set { _minimum = value; if (_maximum < _minimum) _maximum = _minimum; Value = Value; SyncField(); }
    }

    public double Maximum
    {
        get => _maximum;
        set { _maximum = Math.Max(value, _minimum); Value = Value; SyncField(); }
    }

    public double Step
    {
        get => _step;
        set { _step = Math.Max(0d, value); Value = Value; }
    }

    public int Precision
    {
        get => _precision;
        set { _precision = Math.Clamp(value, 0, 6); SyncField(); }
    }

    public string Suffix
    {
        get => _suffix;
        set
        {
            _suffix = value ?? string.Empty;
            SuffixLabel.Content = _suffix;
            SuffixLabel.SetValue(HavenProperties.Visibility,
                string.IsNullOrWhiteSpace(_suffix) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        }
    }

    public string Label
    {
        get => Field.Accessibility.AccessibleName ?? string.Empty;
        set
        {
            Field.Accessibility.AccessibleName = value ?? string.Empty;
            Accessibility.AccessibleName = value ?? "Numeric input";
            Decrease.Accessibility.AccessibleName = string.IsNullOrWhiteSpace(value) ? "Decrease value" : $"Decrease {value}";
            Increase.Accessibility.AccessibleName = string.IsNullOrWhiteSpace(value) ? "Increase value" : $"Increase {value}";
        }
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set
        {
            var next = Snap(Math.Clamp(value, _minimum, _maximum));
            if (Math.Abs(next - GetValue(ValueProperty)) < 0.0000001d) return;
            SetValue(ValueProperty, next);
            SyncField();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double NormalizedValue => _maximum <= _minimum ? 0d : (Value - _minimum) / (_maximum - _minimum);

    public void Nudge(int direction)
    {
        if (direction == 0) return;
        var step = _step > 0 ? _step : Math.Max((_maximum - _minimum) / 100d, 0.01d);
        Value += step * Math.Sign(direction);
    }

    public void SetToMinimum() => Value = _minimum;
    public void SetToMaximum() => Value = _maximum;

    private void OnFieldText()
    {
        if (_syncing) return;
        var text = Field.Text.Trim();
        if (text.Length == 0) return;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed))
            Value = parsed;
    }

    private void SyncField()
    {
        _syncing = true;
        try
        {
            var formatted = Math.Round(Value, _precision).ToString($"F{_precision}", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
            if (formatted.Length == 0) formatted = "0";
            if (!string.Equals(Field.Text, formatted, StringComparison.Ordinal)) Field.Text = formatted;
        }
        finally
        {
            _syncing = false;
        }
    }

    private double Snap(double value)
    {
        if (_step <= 0) return value;
        return Math.Clamp(_minimum + Math.Round((value - _minimum) / _step, MidpointRounding.AwayFromZero) * _step, _minimum, _maximum);
    }

    public override HavenComponentMetadata Metadata => new(
        "NumericInput",
        "HUI/shared/Components/NumericInput/NumericInput.cs",
        ["NumericInput"],
        [],
        "Clamped, snapped numeric entry with stepper buttons; Slider-compatible range semantics.");
}
