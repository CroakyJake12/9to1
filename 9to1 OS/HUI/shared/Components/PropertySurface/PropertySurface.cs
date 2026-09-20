namespace Haven.UI.Components;

/// <summary>Generic property descriptors: identity plus a backend-neutral value. No Canvas/Boards types.</summary>
public abstract record HavenPropertyDescriptor(string Key, string Label);
public sealed record HavenTextProperty(string Key, string Label, string Value) : HavenPropertyDescriptor(Key, Label);
public sealed record HavenNumberProperty(string Key, string Label, double Value, double Minimum, double Maximum, double Step) : HavenPropertyDescriptor(Key, Label);
public sealed record HavenToggleProperty(string Key, string Label, bool Value) : HavenPropertyDescriptor(Key, Label);
public sealed record HavenChoiceProperty(string Key, string Label, IReadOnlyList<string> Options, int SelectedIndex) : HavenPropertyDescriptor(Key, Label);
public sealed record HavenColourProperty(string Key, string Label, string Hex) : HavenPropertyDescriptor(Key, Label);

public sealed record HavenPropertyChanged(string Key, object? Value);

/// <summary>
/// Generic property/editor surface: builds labelled editor rows (text, number,
/// toggle, choice, colour) from descriptors and reports typed changes. Canvas
/// stroke/fill inspectors and Boards card/group editors compose this instead of
/// hand-rolling forms; the inspected model stays app-owned.
/// </summary>
public sealed class PropertySurface : Container
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private IReadOnlyList<HavenPropertyDescriptor> _properties = [];

    public PropertySurface()
    {
        Accessibility.Role = HavenAccessibleRole.Group;
        Accessibility.AccessibleName = "Properties";
        Layout = HavenLayout.Vertical;
        SetValue(HavenProperties.Width, HavenLength.Percent(100), HavenValueSource.Default);
        SetValue(HavenProperties.Gap, HavenLength.Px(10), HavenValueSource.Default);
        SetValue(HavenProperties.Background, "Transparent", HavenValueSource.Default);
    }

    public event EventHandler<HavenPropertyChanged>? PropertyChanged;

    public IReadOnlyList<HavenPropertyDescriptor> Properties => _properties;
    public IReadOnlyDictionary<string, object?> Values => _values;

    public void SetProperties(IEnumerable<HavenPropertyDescriptor> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        _properties = properties.ToArray();
        foreach (var child in Children.ToArray()) Remove(child);
        _values.Clear();
        foreach (var descriptor in _properties)
            Add(BuildRow(descriptor));
    }

    public bool TryGetValue<T>(string key, out T? value)
    {
        if (_values.TryGetValue(key, out var stored) && stored is T typed)
        {
            value = typed;
            return true;
        }
        value = default;
        return false;
    }

    private Container BuildRow(HavenPropertyDescriptor descriptor)
    {
        var row = new Container { Layout = HavenLayout.Horizontal, Name = $"Property.{descriptor.Key}" };
        row.Accessibility.AccessibleName = descriptor.Label;
        row.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        row.SetValue(HavenProperties.Gap, HavenLength.Px(8));

        var label = new Text { Content = descriptor.Label };
        label.SetValue(HavenProperties.FontSize, 12d);
        label.SetValue(HavenProperties.Foreground, "TextSecondary");
        label.SetValue(HavenProperties.Width, HavenLength.Px(128));
        row.Add(label);

        var editor = BuildEditor(descriptor);
        editor.SetValue(HavenProperties.Width, HavenLength.Fr(1));
        row.Add(editor);
        return row;
    }

    private HavenElement BuildEditor(HavenPropertyDescriptor descriptor)
    {
        switch (descriptor)
        {
            case HavenTextProperty text:
                _values[text.Key] = text.Value;
                var input = new Input { Text = text.Value ?? string.Empty };
                input.Accessibility.AccessibleName = text.Label;
                input.TextChanged += (_, _) => Emit(text.Key, input.Text);
                return input;
            case HavenNumberProperty number:
                _values[number.Key] = number.Value;
                var numeric = new NumericInput
                {
                    Minimum = number.Minimum,
                    Maximum = number.Maximum,
                    Step = number.Step,
                    Label = number.Label
                };
                numeric.Value = number.Value;
                numeric.ValueChanged += (_, _) => Emit(number.Key, numeric.Value);
                return numeric;
            case HavenToggleProperty toggle:
                _values[toggle.Key] = toggle.Value;
                var check = new Toggle { IsChecked = toggle.Value };
                check.Accessibility.AccessibleName = toggle.Label;
                check.CheckedChanged += (_, _) => Emit(toggle.Key, check.IsChecked);
                return check;
            case HavenChoiceProperty choice:
                var options = choice.Options ?? [];
                var selected = Math.Clamp(choice.SelectedIndex, options.Count == 0 ? -1 : 0, Math.Max(0, options.Count - 1));
                if (options.Count == 0) selected = -1;
                _values[choice.Key] = selected;
                var select = new Select { Items = options };
                select.Accessibility.AccessibleName = choice.Label;
                select.SelectedIndex = selected;
                select.SelectionChanged += (_, _) => Emit(choice.Key, select.SelectedIndex);
                return select;
            case HavenColourProperty colour:
                var hex = HavenColour.TryParse(colour.Hex, out var parsed) ? parsed.ToHex() : "#000000";
                _values[colour.Key] = hex;
                var picker = new ColourPicker { SelectedHex = hex };
                picker.Accessibility.AccessibleName = $"{colour.Label} colour picker";
                picker.SelectedChanged += (_, _) => Emit(colour.Key, picker.SelectedHex);
                return picker;
            default:
                throw new ArgumentException($"Unsupported property descriptor '{descriptor.GetType().Name}'.", nameof(descriptor));
        }
    }

    private void Emit(string key, object? value)
    {
        _values[key] = value;
        PropertyChanged?.Invoke(this, new HavenPropertyChanged(key, value));
    }

    public override HavenComponentMetadata Metadata => new(
        "PropertySurface",
        "HUI/shared/Components/PropertySurface/PropertySurface.cs",
        ["PropertySurface"],
        [],
        "Descriptor-driven editor rows reusing Input/NumericInput/Toggle/Select/ColourPicker.");
}
