using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using CakeOS.Cui.Runtime;

namespace Haven.Desktop.Mathematics;

/// <summary>Observe authored text properties synchronously before a Save/Next click.
/// Avalonia TextChanged is queued; the existing CUI binding is retained, but cannot be
/// the only draft witness. This is local presentation logic, without framework changes.</summary>
internal sealed class NativeMathInputObservation(ICuiWritableBindingContext context, Func<bool> alive) : IDisposable
{
    private readonly Dictionary<TextBox, Action> _detach = [];
    public void Bind(Control root, IReadOnlyDictionary<string, string> authoredBindings)
    {
        var inputs = root.GetLogicalDescendants().OfType<TextBox>()
            .Where(x => x.Name is { } name && authoredBindings.ContainsKey(name)).ToArray();
        foreach (var retired in _detach.Keys.Except(inputs).ToArray())
        { _detach[retired](); _detach.Remove(retired); }
        foreach (var input in inputs)
        {
            if (_detach.ContainsKey(input)) continue;
            var path = authoredBindings[input.Name!];
            void Edited(object? sender, AvaloniaPropertyChangedEventArgs args)
            {
                if (args.Property == TextBox.TextProperty && alive() && input.IsEffectivelyEnabled)
                    context.TrySetValue(path, input.Text ?? "");
            }
            input.PropertyChanged += Edited;
            _detach.Add(input, () => input.PropertyChanged -= Edited);
        }
    }
    public void Dispose() { foreach (var detach in _detach.Values) detach(); _detach.Clear(); }
}
