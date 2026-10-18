using System.Collections.Frozen;
using CakeOS.Cui.Language;

namespace CakeOS.Cui;

/// <summary>
/// Shared authoring/load validation for the currently executable ActionEvent subset.
/// Required Style, targeting, propagation and event-context semantics remain unsupported;
/// this boundary rejects them explicitly instead of compiling an inert visual component.
/// </summary>
public static class CuiActionEventContract
{
    private static readonly FrozenSet<string> KnownEvents = new[]
    {
        "Hover", "OnceHover", "Clicked", "OnceClick", "PointerEnter", "PointerExit",
        "Loaded", "OnceLoaded", "Deactivated", "OnceDeactivated"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsActionEvent(CuiComponent component) =>
        component.Type.Equals("ActionEvent", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<CuiDiagnostic> Validate(CuiDocument document)
    {
        var diagnostics = new List<CuiDiagnostic>();
        Visit(document.Components, null);
        foreach (var template in document.Templates.Values) Visit(template.Content, null);
        return diagnostics;

        void Error(string code, string message, CuiSourceSpan span) =>
            diagnostics.Add(new(code, CuiDiagnosticSeverity.Error, message, span));

        void Visit(IEnumerable<CuiComponent> components, CuiComponent? owner)
        {
            foreach (var component in components)
            {
                if (IsActionEvent(component)) Check(component, owner);
                Visit(component.Children, component);
                Visit(component.ElseChildren, component);
            }
        }

        void Check(CuiComponent declaration, CuiComponent? owner)
        {
            var types = declaration.Properties.Where(pair => pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)).ToArray();
            var type = types.Length == 1 ? types[0].Value as CuiLiteralValue : null;
            if (type is null || string.IsNullOrWhiteSpace(type.Value))
                Error("CUIE002", "ActionEvent requires exactly one literal Type.", declaration.Span);
            else if (!KnownEvents.Contains(type.Value))
                Error("CUIE001", $"Unknown CUI action event '{type.Value}'.", type.Span);
            else if (!type.Value.Equals("Clicked", StringComparison.OrdinalIgnoreCase)
                && !type.Value.Equals("OnceClick", StringComparison.OrdinalIgnoreCase))
                Error("CUIE003", $"ActionEvent '{type.Value}' is specified but its runtime semantics are not implemented yet.", type.Span);

            if (owner is null || !owner.Type.Equals("Button", StringComparison.OrdinalIgnoreCase))
                Error("CUIE003", "Executable ActionEvent currently requires an owning Button; Style and external event targets are not implemented yet.", declaration.Span);
            else if (HasCompetingActionRoutes(owner))
                Error("CUIE004", "A Button currently supports one action route: either its direct Action or one owned ActionEvent.", declaration.Span);

            foreach (var group in declaration.Properties.GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                if (group.Count() > 1)
                    Error("CUIE002", $"ActionEvent property '{group.Key}' is repeated with different casing.", group.Last().Value.Span);
            foreach (var (name, value) in declaration.Properties)
            {
                if (name.Equals("Type", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("Bubble", StringComparison.OrdinalIgnoreCase))
                {
                    if (value is not CuiLiteralValue literal || !bool.TryParse(literal.Value, out var bubble))
                        Error("CUIE002", "ActionEvent Bubble requires a literal Boolean.", value.Span);
                    else if (bubble)
                        Error("CUIE003", "ActionEvent upward propagation and cancellation are not implemented yet; Bubble=True cannot execute.", value.Span);
                    continue;
                }
                if (name.Equals("TargetID", StringComparison.OrdinalIgnoreCase) || name.Equals("TargetGroup", StringComparison.OrdinalIgnoreCase))
                    Error("CUIE003", "Explicit ActionEvent ID/Group targets are not implemented yet.", value.Span);
                else Error("CUIE002", $"Unknown or unsupported ActionEvent property '{name}'.", value.Span);
            }

            if (!declaration.Actions.TryGetValue("action", out var action) || string.IsNullOrWhiteSpace(action.Name))
                Error("CUIE003", "Executable ActionEvent currently requires a named Action; property/event overlays are not implemented yet.", declaration.Span);
            else if (document.Actions.TryGetValue(action.Name, out var definition)
                && definition.Parameter is CuiBindingValue binding
                && (binding.Path.Equals("Event", StringComparison.OrdinalIgnoreCase) || binding.Path.StartsWith("Event.", StringComparison.OrdinalIgnoreCase)))
                Error("CUIE003", "ActionEvent context bindings and cancellation are not implemented yet.", binding.Span);

            if (declaration.Actions.Keys.Any(name => name != "action") || declaration.Children.Count > 0
                || declaration.ElseChildren.Count > 0 || !string.IsNullOrWhiteSpace(declaration.Text)
                || declaration.Name is not null || declaration.Groups.Count > 0 || declaration.Classes.Count > 0
                || declaration.IsDefinition || declaration.Condition is not null || declaration.List is not null || declaration.Repeat is not null)
                Error("CUIE003", "ActionEvent supports only the owned named-action declaration; nested rules, lifecycle modifiers and event addressing are not implemented yet.", declaration.Span);
        }
    }

    /// <summary>Call only after validating the document; no declaration creates a visual control.</summary>
    public static CuiActionReference? OwnedAction(CuiComponent owner) =>
        owner.Children.FirstOrDefault(IsActionEvent)?.Actions.GetValueOrDefault("action");

    /// <summary>Recheck after Definition instance merging, where caller properties can add a competing route.</summary>
    public static bool HasCompetingActionRoutes(CuiComponent owner)
    {
        var count = owner.Children.Count(IsActionEvent);
        return count > 1 || count > 0 && (owner.Actions.ContainsKey("action") || owner.Actions.ContainsKey("on:click"));
    }
}
