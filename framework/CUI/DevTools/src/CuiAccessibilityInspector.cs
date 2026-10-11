using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using LanguageSpan = CakeOS.Cui.Language.CuiSourceSpan;

namespace Haven.CUI.DevTools;

public sealed record CuiAccessibilityNode(
    ElementId ElementId,
    string Type,
    string? AuthoredId,
    string? Role,
    string? AccessibleName,
    string? AccessibleDescription,
    string? AutomationId,
    bool IsFocusable,
    int? TabIndex,
    bool IsHidden,
    bool HasStableSemanticSelector,
    IReadOnlyList<string> DynamicFields,
    IReadOnlyList<string> Actions);

public sealed record CuiAccessibilityFinding(
    ElementId ElementId,
    string Issue,
    CuiSourceLocation Source);

public sealed class CuiAccessibilityInspection
{
    private readonly Dictionary<ElementId, CuiAccessibilityNode> _nodes = new();

    public CuiAccessibilityInspection(
        CuiElementTree tree,
        IEnumerable<CuiAccessibilityNode> nodes,
        IEnumerable<CuiAccessibilityFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(tree);
        Tree = tree;
        Nodes = DiagnosticsCollections.Copy(nodes);
        Findings = DiagnosticsCollections.Copy(findings);
        foreach (var node in Nodes) _nodes[node.ElementId] = node;
    }

    public CuiElementTree Tree { get; }

    public IReadOnlyList<CuiAccessibilityNode> Nodes { get; }

    public IReadOnlyList<CuiAccessibilityFinding> Findings { get; }

    public IReadOnlyList<CuiAccessibilityNode> Find(CuiElementSelector selector) =>
        Array.AsReadOnly(Tree.Find(selector).Select(node => _nodes[node.Id]).ToArray());
}

/// <summary>
/// Builds the semantic accessibility tree from the CUI language model without a live host.
/// Definition declarations are excluded because a definition is not rendered merely because
/// its declaration is present. Dynamic values are reported as dynamic rather than guessed.
/// </summary>
public sealed class CuiAccessibilityInspector
{
    private static readonly CuiControlRegistry Registry = CuiControlRegistry.Default;

    public CuiAccessibilityInspection Inspect(CuiDocument document, long revision = 0)
    {
        ArgumentNullException.ThrowIfNull(document);

        var nodes = new List<CuiAccessibilityNode>();
        var findings = new List<CuiAccessibilityFinding>();

        CuiElementNode Build(CuiComponent component)
        {
            var canonical = Canonicalize(component);
            var role = Literal(canonical.GetValueOrDefault("Role"));
            var name = Literal(canonical.GetValueOrDefault("AccessibleName"));
            var description = Literal(canonical.GetValueOrDefault("AccessibleDescription"));
            var automationId = Literal(canonical.GetValueOrDefault("AutomationId"));
            var focusable = ParseBool(canonical.GetValueOrDefault("Focusable")) ?? false;
            var hidden = ParseBool(canonical.GetValueOrDefault("Hidden"))
                         ?? !(ParseBool(canonical.GetValueOrDefault("IsVisible")) ?? true);
            var tabIndex = ParseInt(canonical.GetValueOrDefault("TabIndex"));
            var dynamicFields = canonical
                .Where(pair => pair.Value is CuiBindingValue)
                .Select(pair => pair.Key)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var actions = component.Actions.Keys.Order(StringComparer.Ordinal).ToArray();
            var hasStableSelector = component.Name is not null || role is not null || name is not null;
            var elementId = new ElementId(component.StableId);

            var node = new CuiAccessibilityNode(elementId, component.Type, component.Name, role, name, description,
                automationId, focusable, tabIndex, hidden, hasStableSelector, dynamicFields, actions);
            nodes.Add(node);

            if (component.IsDefinition)
                return new CuiElementNode(elementId, component.Type, component.Name, component.Classes, null, [],
                    automationId, name, role, false, true);

            if (actions.Length > 0 && !hasStableSelector)
                findings.Add(new CuiAccessibilityFinding(elementId,
                    $"Element '{component.Type}' exposes an action but has no id, role or accessible name, so semantic targeting falls back to raw coordinates.",
                    LocationOf(document.SourceName, component.Span, component.Name)));

            var children = component.Children.Concat(component.ElseChildren)
                .Where(child => !child.IsDefinition)
                .Select(Build);
            return new CuiElementNode(elementId, component.Type, component.Name, component.Classes, component.Text,
                children, automationId, name, role, focusable, hidden);
        }

        var root = new CuiElementNode(new ElementId("document"), "Cui", null, [], null,
            document.Components.Select(Build), null, null, null, false, false);
        return new CuiAccessibilityInspection(new CuiElementTree(root, revision), nodes, findings);
    }

    private static Dictionary<string, CuiValue> Canonicalize(CuiComponent component)
    {
        var result = new Dictionary<string, CuiValue>(StringComparer.Ordinal);
        foreach (var (name, value) in component.Properties)
            result[Registry.TryGetProperty(name, out var descriptor) ? descriptor.Name : name] = value;
        return result;
    }

    private static string? Literal(CuiValue? value) => value is CuiLiteralValue literal ? literal.Value : null;

    private static bool? ParseBool(CuiValue? value) =>
        value is CuiLiteralValue literal && bool.TryParse(literal.Value, out var result) ? result : null;

    private static int? ParseInt(CuiValue? value) =>
        value is CuiLiteralValue literal && int.TryParse(literal.Value, out var result) ? result : null;

    private static CuiSourceLocation LocationOf(string sourceName, LanguageSpan span, string? authoredId) =>
        CuiSourceLocation.Create(sourceName,
            new CuiSourceSpan(span.Start.Line, span.Start.Column, span.End.Line, span.End.Column), authoredId);
}