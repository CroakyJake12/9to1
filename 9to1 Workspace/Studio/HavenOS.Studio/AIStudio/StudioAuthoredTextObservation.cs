using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;

namespace HavenOS.AIStudio;

/// <summary>Owning presentation witness for actual authored Text properties. Avalonia queues
/// TextChanged; the existing CUI binding remains, while immediate Create/Save sees visible input.
/// This does not authorize a Den mutation, resolve dependencies or replace the Home session.</summary>
internal sealed class StudioAuthoredTextObservation(ICuiWritableBindingContext context, Func<bool> alive) : IDisposable
{
    private readonly Dictionary<TextBox, Action> _detach = [];
    private bool _disposed;
    public void Bind(Control root, IReadOnlyDictionary<string, string> authoredBindings)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var inputs = root.GetLogicalDescendants().OfType<Control>().Prepend(root).OfType<TextBox>()
            .Where(input => input.Name is { } name && authoredBindings.ContainsKey(name)).ToArray();
        if (inputs.Length != authoredBindings.Count || inputs.Select(input => input.Name!).Distinct(StringComparer.Ordinal).Count() != authoredBindings.Count)
            throw new InvalidDataException("The original named Agent authoring inputs are missing or duplicated.");
        foreach (var retired in _detach.Keys.Except(inputs).ToArray())
        { _detach[retired](); _detach.Remove(retired); }
        foreach (var input in inputs)
        {
            if (_detach.ContainsKey(input)) continue;
            var path = authoredBindings[input.Name!];
            void Edited(object? sender, AvaloniaPropertyChangedEventArgs args)
            {
                if (args.Property == TextBox.TextProperty && alive() && input.IsEffectivelyEnabled && !input.IsReadOnly &&
                    !context.TrySetValue(path, input.Text ?? ""))
                    throw new InvalidOperationException("The original Agent authoring field is no longer bound.");
            }
            input.PropertyChanged += Edited;
            _detach.Add(input, () => input.PropertyChanged -= Edited);
        }
    }
    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        foreach (var detach in _detach.Values) detach();
        _detach.Clear();
    }
}
