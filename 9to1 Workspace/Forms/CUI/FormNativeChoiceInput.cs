using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Haven.Core.Forms;

namespace HavenOS.Forms;

/// <summary>Native set selection over the canonical option identities; the owning response runtime
/// remains responsible for answer validation, marking, revision and release policy.</summary>
internal sealed class FormNativeChoiceInput : StackPanel, IDisposable
{
    private readonly List<(Guid OptionID, CheckBox Control)> _choices = [];
    private readonly List<Action> _detach = [];
    private readonly Action<JsonElement> _changed;
    private bool _disposed;

    public FormNativeChoiceInput(FormField field, JsonElement? answer, Action<JsonElement> changed)
    {
        if (field.Kind is not (FormFieldKind.MultipleChoice or FormFieldKind.CheckboxSet) || field.Options is null)
            throw new ArgumentException("A canonical set-valued choice field is required.", nameof(field));
        _changed = changed;
        Spacing = (double)field.Layout.Gap;
        HashSet<Guid> selected = answer is { ValueKind: JsonValueKind.Array } values
            ? values.EnumerateArray().Select(item => item.GetGuid()).ToHashSet() : [];
        foreach (var option in field.Options)
        {
            var check = new CheckBox { Content = option.Label, IsChecked = selected.Contains(option.OptionID), IsThreeState = false };
            AutomationProperties.SetName(check, option.Label);
            void SelectionChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
            {
                if (!_disposed && IsEnabled && check.IsEnabled)
                    _changed(JsonSerializer.SerializeToElement(_choices.Where(item => item.Control.IsChecked == true)
                        .Select(item => item.OptionID).ToArray()));
            }
            check.IsCheckedChanged += SelectionChanged;
            _detach.Add(() => check.IsCheckedChanged -= SelectionChanged);
            _choices.Add((option.OptionID, check));
            Children.Add(check);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var detach in _detach) detach();
        _detach.Clear();
        IsEnabled = false;
    }
}
