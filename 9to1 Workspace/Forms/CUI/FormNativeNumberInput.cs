using System.Text.Json;
using Avalonia.Controls;

namespace HavenOS.Forms;

/// <summary>Preserves the visible numeric draft. NumericUpDown retains its last Value when Text
/// cannot parse; forwarding that text lets the canonical validator block stale-value submission.</summary>
internal sealed class FormNativeNumberInput : NumericUpDown, IDisposable
{
    protected override Type StyleKeyOverride => typeof(NumericUpDown);
    private readonly Action<JsonElement> _changed;
    private string? _last;
    private bool _disposed;

    public FormNativeNumberInput(decimal? value, Action<JsonElement> changed, string? draftText = null)
    {
        Minimum = decimal.MinValue; Maximum = decimal.MaxValue; Value = value;
        if (draftText is not null) Text = draftText;
        _changed = changed;
        ValueChanged += ChangedValue; PropertyChanged += ChangedText;
    }
    private void ChangedValue(object? sender, NumericUpDownValueChangedEventArgs args) => Publish(JsonSerializer.SerializeToElement(Value));
    private void ChangedText(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property != TextProperty) return;
        Publish(string.IsNullOrWhiteSpace(Text) ? JsonSerializer.SerializeToElement<decimal?>(null)
            : decimal.TryParse(Text, ParsingNumberStyle, NumberFormat, out var value)
                ? JsonSerializer.SerializeToElement(value) : JsonSerializer.SerializeToElement(Text));
    }
    private void Publish(JsonElement value)
    {
        if (_disposed || !IsEffectivelyEnabled) return;
        var current = value.GetRawText();
        if (_last == current) return;
        _last = current; _changed(value);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; ValueChanged -= ChangedValue; PropertyChanged -= ChangedText; IsEnabled = false;
    }
}
