using System.Collections.Frozen;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace CakeOS.Cui.Runtime;

/// <summary>Internal backend resource ownership for one resolved CUI palette.</summary>
internal sealed class CuiThemeResourceProvider(FrozenDictionary<object, object?> snapshot) : ResourceProvider
{
    private Dictionary<LinearGradientBrush, LinearGradientBrush>? _gradients;

    public override bool HasResources => snapshot.Count > 0;

    public override bool TryGetResource(object key, ThemeVariant? theme, out object? value)
    {
        if (!snapshot.TryGetValue(key, out value)) return false;
        if (value is not LinearGradientBrush template) return true;

        // A renderer can mutate a native gradient. Share immutable palette values,
        // but retain a separate mutable brush per control, including its aliases.
        _gradients ??= new(ReferenceEqualityComparer.Instance);
        if (!_gradients.TryGetValue(template, out var owned))
        {
            owned = new LinearGradientBrush
            {
                StartPoint = template.StartPoint, EndPoint = template.EndPoint,
                SpreadMethod = template.SpreadMethod, Opacity = template.Opacity,
                Transform = template.Transform, TransformOrigin = template.TransformOrigin
            };
            foreach (var stop in template.GradientStops)
                owned.GradientStops.Add(new GradientStop(stop.Color, stop.Offset));
            _gradients.Add(template, owned);
        }
        value = owned;
        return true;
    }
}
