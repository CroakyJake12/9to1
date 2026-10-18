using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace CakeOS.Cui.Compiler;

/// <summary>Checks authored reusable-component expansion without expanding a runtime tree.</summary>
internal static class CuiDefinitionDependencies
{
    internal static void Validate(CuiDocument document, CuiDiagnosticBag diagnostics)
    {
        var root = new Scope(null);
        var nodes = new List<Definition>();
        var scopes = new Dictionary<CuiComponent, Scope>();

        foreach (var template in document.Templates.Values)
        {
            var node = new Definition(template.Name, template.Content);
            root.Definitions.TryAdd(node.Name, node);
            nodes.Add(node);
            node.Scope = new Scope(root);
            Register(node.Body, node.Scope);
        }
        Register(document.Components, root);

        foreach (var node in nodes)
        {
            // A named custom declaration can wrap another declaration at its root,
            // not only in its children: <A (Definition) ID="B"/> means B wraps A.
            if (node.WrappedType is { } wrapped && node.DeclaringScope!.Resolve(wrapped) is { } target)
                node.Dependencies.Add(new Dependency(target, node.Declaration!.Span));
            Collect(node.Body, node.Scope!, node.Dependencies);
        }

        var visiting = new HashSet<Definition>();
        var complete = new HashSet<Definition>();
        var path = new List<Definition>();
        foreach (var node in nodes) Visit(node);

        void Register(IReadOnlyList<CuiComponent> components, Scope scope)
        {
            foreach (var component in components)
            {
                // Theme element definitions are presentation rules, not component
                // declarations visible outside the Theme's own presentation scope.
                if (component.IsDefinition && component.Type == "Theme") continue;
                if (ReusableName(component) is { } name)
                {
                    var node = new Definition(name, component.Children.Concat(component.ElseChildren).ToArray())
                    {
                        Scope = new Scope(scope),
                        DeclaringScope = scope,
                        Declaration = component,
                        IsVisualWrapper = CuiLanguageVocabulary.VisualElements.Contains(component.Type),
                        WrappedType = name != component.Type
                            && !CuiLanguageVocabulary.VisualElements.Contains(component.Type)
                            && !CuiLanguageVocabulary.NonvisualElements.Contains(component.Type)
                            ? component.Type : null
                    };
                    if (!scope.Definitions.TryAdd(name, node))
                        diagnostics.Error("CUIC026", $"Definition '{name}' is declared more than once in the same scope.", component.Span);
                    nodes.Add(node);
                    scopes[component] = node.Scope;
                    Register(node.Body, node.Scope);
                    continue;
                }

                // An actual Page and each Repeat item own addressing/definition scopes.
                // Ordinary containers do not accidentally hide forward declarations.
                var childScope = component.Type is "Page" or "Repeat" ? new Scope(scope) : scope;
                scopes[component] = childScope;
                Register(component.Children, childScope);
                Register(component.ElseChildren, childScope);
            }
        }

        void Collect(IReadOnlyList<CuiComponent> components, Scope scope, List<Dependency> edges)
        {
            foreach (var component in components)
            {
                // Declaring an unused inner component does not instantiate it. Its own
                // body is checked as a separate node, whether or not it is used today.
                if (component.IsDefinition) continue;
                if (scope.Resolve(component.Type) is { } target
                    && !(target.IsVisualWrapper && component.Children.Count > 0))
                    edges.Add(new Dependency(target, component.Span));
                // A visual Definition with authored instance children substitutes that
                // finite body for its defaults. Still inspect those supplied children;
                // they may themselves contain an actual expansion dependency.
                var childScope = scopes.GetValueOrDefault(component) ?? scope;
                Collect(component.Children, childScope, edges);
                Collect(component.ElseChildren, childScope, edges);
            }
        }

        void Visit(Definition node)
        {
            if (complete.Contains(node)) return;
            visiting.Add(node);
            path.Add(node);
            foreach (var dependency in node.Dependencies)
            {
                if (visiting.Contains(dependency.Target))
                {
                    var cycle = path.Skip(path.IndexOf(dependency.Target))
                        .Select(item => item.Name).Append(dependency.Target.Name);
                    diagnostics.Error("CUIC025",
                        $"Cyclic Definition/Prefab dependency: {string.Join(" -> ", cycle)}.",
                        dependency.Source);
                }
                else Visit(dependency.Target);
            }
            path.RemoveAt(path.Count - 1);
            visiting.Remove(node);
            complete.Add(node);
        }
    }

    private static string? ReusableName(CuiComponent component)
    {
        if (!component.IsDefinition) return null;
        if (component.Type == "Prefab") return component.Name;
        if (CuiLanguageVocabulary.NonvisualElements.Contains(component.Type)) return null;
        return component.Name ?? (CuiLanguageVocabulary.VisualElements.Contains(component.Type) ? null : component.Type);
    }

    private sealed class Scope(Scope? parent)
    {
        internal Dictionary<string, Definition> Definitions { get; } = new(StringComparer.Ordinal);
        internal Definition? Resolve(string name) =>
            Definitions.GetValueOrDefault(name) ?? parent?.Resolve(name);
    }

    private sealed class Definition(string name, IReadOnlyList<CuiComponent> body)
    {
        internal string Name { get; } = name;
        internal IReadOnlyList<CuiComponent> Body { get; } = body;
        internal Scope? Scope { get; set; }
        internal Scope? DeclaringScope { get; init; }
        internal CuiComponent? Declaration { get; init; }
        internal string? WrappedType { get; init; }
        internal bool IsVisualWrapper { get; init; }
        internal List<Dependency> Dependencies { get; } = [];
    }

    private sealed record Dependency(Definition Target, CuiSourceSpan Source);
}
