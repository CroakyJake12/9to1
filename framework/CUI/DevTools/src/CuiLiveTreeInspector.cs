using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace Haven.CUI.DevTools;

/// <summary>Read-only inspection of the actual Avalonia tree produced by a CUI host.</summary>
public sealed class CuiLiveTreeInspector
{
    private readonly IReadOnlyDictionary<ElementId, Control> _controls;
    private readonly CuiControlLoader? _loader;

    private CuiLiveTreeInspector(CuiElementTree tree, Dictionary<ElementId, Control> controls, CuiControlLoader? loader)
    {
        Tree = tree;
        _controls = controls;
        _loader = loader;
    }

    public CuiElementTree Tree { get; }

    public static CuiLiveTreeInspector Capture(Control root, CuiControlLoader? loader = null, long revision = 0)
    {
        ArgumentNullException.ThrowIfNull(root);
        var controls = new Dictionary<ElementId, Control>();
        CuiElementNode CaptureNode(Control control, string path)
        {
            var id = new ElementId(path);
            controls.Add(id, control);
            var children = Children(control).Select((child, index) => CaptureNode(child, $"{path}/{index}"));
            var text = control switch
            {
                TextBlock block => block.Text,
                TextBox box => box.Text,
                _ => null
            };
            return new CuiElementNode(id, control.GetType().Name, control.Name, control.Classes, text, children);
        }

        return new CuiLiveTreeInspector(new CuiElementTree(CaptureNode(root, "root"), revision), controls, loader);
    }

    public LayoutSnapshot? GetLayout(ElementId id)
    {
        if (!_controls.TryGetValue(id, out var control)) return null;
        var bounds = control.Bounds;
        var desired = control.DesiredSize;
        var margin = control.Margin;
        return new LayoutSnapshot(
            new CuiRect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            new CuiSize(desired.Width, desired.Height),
            new CuiSize(control.MinWidth, control.MinHeight),
            new CuiSize(control.MaxWidth, control.MaxHeight),
            new CuiThickness(margin.Left, margin.Top, margin.Right, margin.Bottom),
            control.HorizontalAlignment.ToString(), control.VerticalAlignment.ToString(),
            control.ClipToBounds, control.RenderTransform?.ToString());
    }

    public AccessibilitySnapshot? GetAccessibility(ElementId id)
    {
        if (!_controls.TryGetValue(id, out var control)) return null;
        var peer = ControlAutomationPeer.CreatePeerForElement(control);
        var name = AutomationProperties.GetName(control);
        if (string.IsNullOrWhiteSpace(name)) name = peer.GetName();
        if (string.IsNullOrWhiteSpace(name)) name = AutomationProperties.GetAutomationId(control);
        if (string.IsNullOrWhiteSpace(name)) name = control.Name;
        return new AccessibilitySnapshot(
            peer.GetAutomationControlType().ToString(), name, AutomationProperties.GetHelpText(control),
            control is TextBlock block ? block.Text : null,
            [], new Dictionary<string, string>
            {
                ["Enabled"] = peer.IsEnabled().ToString(),
                ["KeyboardFocusable"] = peer.IsKeyboardFocusable().ToString(),
                ["Offscreen"] = peer.IsOffscreen().ToString()
            }, !control.IsVisible);
    }

    public CuiAuthoredControlTrace? GetSource(ElementId id)
    {
        var diagnostic = GetAuthored(id);
        if (diagnostic is null) return null;
        return new CuiAuthoredControlTrace(Source(diagnostic.Source, diagnostic.AuthoredId),
            diagnostic.ComponentType, diagnostic.AuthoredId);
    }

    public BindingSnapshot GetBindings(ElementId id)
    {
        var diagnostic = GetAuthored(id);
        if (diagnostic is null || !_controls.ContainsKey(id)) return new BindingSnapshot([]);
        var values = diagnostic.Properties
            .Where(pair => pair.Value is CuiBindingValue)
            .Select(pair =>
            {
                var binding = (CuiBindingValue)pair.Value;
                var observed = diagnostic.Bindings.FirstOrDefault(item => item.Property == pair.Key);
                return new BindingValueSnapshot(pair.Key, binding.Path, observed?.SourceType,
                    observed?.ResolvedValue, observed is null ? BindingStatus.Pending
                        : observed.Error is not null ? BindingStatus.Faulted
                        : !observed.SourceFound ? BindingStatus.MissingSource : BindingStatus.Active,
                    observed?.Error);
            }).ToArray();
        return new BindingSnapshot(values);
    }

    public IReadOnlyList<CuiBindingTrace> GetBindingTraces(ElementId id)
    {
        var diagnostic = GetAuthored(id);
        if (diagnostic is null || !_controls.TryGetValue(id, out var control)) return [];
        return Array.AsReadOnly(diagnostic.Properties
            .Where(pair => pair.Value is CuiBindingValue)
            .Select(pair =>
            {
                var binding = (CuiBindingValue)pair.Value;
                var observed = diagnostic.Bindings.FirstOrDefault(item => item.Property == pair.Key);
                var status = observed is null ? BindingStatus.Pending
                    : observed.Error is not null ? BindingStatus.Faulted
                    : !observed.SourceFound ? BindingStatus.MissingSource : BindingStatus.Active;
                return new CuiBindingTrace(pair.Key, binding.Path, binding.Mode.ToString(), binding.Fallback,
                    observed?.ResolvedValue, NativeValue(control, pair.Key), observed?.SourceType,
                    status, observed?.UsedFallback ?? false, observed?.Error);
            }).ToArray());
    }

    public IReadOnlyList<CuiActionTrace> GetActions(ElementId id)
    {
        var diagnostic = GetAuthored(id);
        if (diagnostic is null || !_controls.TryGetValue(id, out var control)) return [];
        return Array.AsReadOnly(diagnostic.Actions.Select(pair => new CuiActionTrace(
            pair.Key, pair.Value.Name, Source(pair.Value.Span, diagnostic.AuthoredId),
            diagnostic.DispatcherConnected, diagnostic.ActionsWired,
            control.IsEnabled, diagnostic.ActionRegistered, diagnostic.ActionAvailable)).ToArray());
    }

    public IReadOnlyList<CuiVisualPropertyTrace> GetVisualProperties(ElementId id)
    {
        if (!_controls.TryGetValue(id, out var control)) return [];
        var diagnostic = GetAuthored(id);
        var properties = new List<CuiVisualPropertyTrace>();
        void Add(string name, object? effective, string attribute)
        {
            if (effective is null) return;
            CuiValue? authored = null;
            if (diagnostic is not null) diagnostic.Properties.TryGetValue(attribute, out authored);
            properties.Add(new CuiVisualPropertyTrace(name, Format(effective), Authored(authored),
                authored?.Span.ToString()));
        }
        Add("Opacity", control.Opacity, "opacity");
        if (control is Panel panel) Add("Background", panel.Background, "background");
        if (control is Border border)
        {
            Add("Background", border.Background, "background");
            Add("BorderBrush", border.BorderBrush, "border-brush");
            Add("BorderThickness", border.BorderThickness, "border-thickness");
            Add("CornerRadius", border.CornerRadius, "corner-radius");
        }
        if (control is TextBlock text)
        {
            Add("Foreground", text.Foreground, "foreground");
            Add("FontSize", text.FontSize, "font-size");
        }
        if (control is TemplatedControl templated)
        {
            Add("Background", templated.Background, "background");
            Add("Foreground", templated.Foreground, "foreground");
        }
        return properties.AsReadOnly();
    }

    public ResourceSnapshot GetResources(ElementId id)
    {
        if (!_controls.TryGetValue(id, out var control)) return new ResourceSnapshot([]);
        var diagnostic = GetAuthored(id);
        var keys = diagnostic?.Properties.Values.OfType<CuiResourceValue>().Select(value => value.Key)
            .Concat(["CuiTheme", "CuiAppearance", "CuiControlRadius", "CuiCardRadius", "CuiMotionDurationScale"])
            .Distinct(StringComparer.Ordinal) ?? [];
        return new ResourceSnapshot(Array.AsReadOnly(keys.Select(key =>
        {
            var found = control.TryGetResource(key, null, out var value);
            return new ResourceValueSnapshot(key, found ? Format(value) : "", "effective control scope", found);
        }).ToArray()));
    }

    public CuiRenderingTrace? GetRendering(ElementId id)
    {
        if (!_controls.TryGetValue(id, out var control)) return null;
        var bounds = control.Bounds;
        return new CuiRenderingTrace(new CuiRect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            control.Opacity, control.IsVisible, control.IsLoaded,
            TopLevel.GetTopLevel(control)?.RenderScaling ?? 1d,
            control.RenderTransform?.ToString(), control.Clip?.ToString());
    }

    private CuiControlDiagnostics? GetAuthored(ElementId id) =>
        _controls.TryGetValue(id, out var control) ? _loader?.Inspect(control) : null;

    private static CuiSourceLocation Source(CakeOS.Cui.Language.CuiSourceSpan span, string? id) =>
        CuiSourceLocation.Create(span.SourceName,
            new CuiSourceSpan(span.Start.Line, span.Start.Column, span.End.Line, span.End.Column), id);

    private static string? Authored(CuiValue? value) => value switch
    {
        CuiLiteralValue literal => literal.Value,
        CuiResourceValue resource => $"{{Resource {resource.Key}}}",
        CuiBindingValue binding => $"{{Binding {binding.Path}}}",
        _ => null
    };

    private static string? NativeValue(Control control, string property) =>
        property.ToLowerInvariant() switch
        {
            "text" when control is TextBlock text => text.Text,
            "text" when control is TextBox box => box.Text,
            "content" when control is ContentControl content => content.Content?.ToString(),
            _ => null
        };

    private static string Format(object? value) => value switch
    {
        SolidColorBrush brush => brush.Color.ToString(),
        LinearGradientBrush gradient => string.Join(" → ", gradient.GradientStops.Select(stop => stop.Color.ToString())),
        _ => value?.ToString() ?? "(null)"
    };

    private static IEnumerable<Control> Children(Control control)
    {
        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
                if (child is Control candidate) yield return candidate;
        }
        else if (control is Decorator decorator && decorator.Child is Control child)
            yield return child;
        else if (control is ContentControl content && content.Content is Control contentChild)
            yield return contentChild;
        else if (control is ItemsControl items)
        {
            foreach (var item in items.Items)
                if (item is Control candidate) yield return candidate;
        }
    }
}
