using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace HavenOS.Apps.Spaces;

/// <summary>One canonical compiled surface. Navigation and the active workspace
/// keep their authored controls, binding identities and named actions. There is
/// no secondary loader or embedded donor application.</summary>
public static class SpacesShellCuiDocument
{
    public static CuiDocument Load(CuiDocument? workspace = null, bool includeOriginalGeneratedChildren = true)
    {
        var shell = LoadShell();
        var navigation = SpaceNavigationCuiDocument.Load();
        workspace ??= ChatWorkspaceCuiDocument.Load();
        var navigationComponents = navigation.Components.Select(c => Transform(c, NavigationValue)).ToArray();
        var workspaceComponents = workspace.Components.Select(c => Transform(c, value => value, stripChatHeader: true, includeOriginalGeneratedChildren: includeOriginalGeneratedChildren)).ToArray();
        var documents = new[] { shell, navigation, workspace };
        return shell with
        {
            Resources = Merge(documents.Select(d => d.Resources)),
            Styles = documents.SelectMany(d => d.Styles).ToArray(),
            Actions = Merge(documents.Select(d => d == navigation
                ? (IReadOnlyDictionary<string, CuiActionDefinition>)d.Actions.ToDictionary(p => p.Key,
                    p => p.Value with { Parameter = p.Value.Parameter is { } value ? NavigationValue(value) : null }) : d.Actions)),
            Templates = Merge(documents.Select(d => d.Templates)),
            Imports = documents.SelectMany(d => d.Imports).Distinct().ToArray(),
            Components = shell.Components.Select(c => Fill(c, navigationComponents, workspaceComponents)).ToArray()
        };
    }

    /// <summary>Same authored frame with explicit original-owner surface slots.
    /// Only maintained native composition registers these two narrow renderers.</summary>
    public static CuiDocument LoadForOriginalNativeSlots()
    {
        var shell = LoadShell();
        CuiComponent Slot(string name, string type) => new("Object", name, [],
            new Dictionary<string, CuiValue> { ["Type"] = new CuiLiteralValue(type, shell.Span) },
            new Dictionary<string, CuiActionReference>(), [], null, null, shell.Span);
        return shell with { Components = shell.Components.Select(c => Fill(c,
            [Slot("original-spaces-navigation", "OriginalSpacesNavigation")],
            [Slot("original-spaces-workspace", "OriginalSpacesWorkspace")])).ToArray() };
    }

    public static CuiDocument LoadWorkspaceWithoutHeader()
    {
        var workspace = ChatWorkspaceCuiDocument.Load();
        return workspace with { Components = workspace.Components.Select(c => Transform(c, value => value, stripChatHeader: true)).ToArray() };
    }

    private static CuiDocument LoadShell()
    {
        using var stream = typeof(SpacesShellCuiDocument).Assembly.GetManifestResourceStream("HavenOS.Apps.Spaces.UI.Shell.SpacesShell.cui")
            ?? throw new InvalidDataException("The canonical Spaces shell resource is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var shell = parser.Parse(reader.ReadToEnd(), "SpacesShell.cui");
        if (parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The canonical Spaces shell has syntax errors.");
        return shell;
    }

    private static IReadOnlyDictionary<string, T> Merge<T>(IEnumerable<IReadOnlyDictionary<string, T>> sources)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var source in sources)
        foreach (var (key, value) in source)
            if (!result.TryAdd(key, value)) throw new InvalidDataException($"Shared Spaces declarations conflict: {key}.");
        return result;
    }
    private static CuiValue NavigationValue(CuiValue value) => value is CuiBindingValue binding && binding.Path.StartsWith("dialog.", StringComparison.Ordinal)
        ? binding with { Path = "navDialog." + binding.Path[7..] } : value;
    private static CuiComponent Fill(CuiComponent component, IReadOnlyList<CuiComponent> navigation, IReadOnlyList<CuiComponent> workspace) =>
        Copy(component, component.Properties, component.Name switch
        {
            "spaces-navigation-slot" => navigation,
            "spaces-workspace-slot" => workspace,
            _ => component.Children.Select(c => Fill(c, navigation, workspace)).ToArray()
        }, component.ElseChildren.Select(c => Fill(c, navigation, workspace)).ToArray());

    private static CuiComponent Transform(CuiComponent component, Func<CuiValue, CuiValue> map, bool stripChatHeader = false, bool includeOriginalGeneratedChildren = true)
    {
        var properties = component.Properties.ToDictionary(p => p.Key, p => map(p.Value), StringComparer.OrdinalIgnoreCase);
        if (stripChatHeader && component.Children.Any(child => child.Name == "chat-header"))
            properties["RowSizes"] = new CuiLiteralValue("1fr", component.Span);
        if (stripChatHeader && component.Name == "conversation-surface")
            properties["Row"] = new CuiLiteralValue("0", component.Span);
        return new(component.Type, component.Name, component.Classes, properties, component.Actions,
            component.Children.Where(child => (!stripChatHeader || child.Name != "chat-header") && (includeOriginalGeneratedChildren || !IsOriginalGeneratedMount(child))).Select(c => Transform(c, map, stripChatHeader, includeOriginalGeneratedChildren)).ToArray(),
            component.Condition is { } condition ? condition with { Test = map(condition.Test) } : null,
            component.List is { } list ? list with { Items = map(list.Items) } : null,
            component.Span, component.DefaultTheme,
            component.Repeat is { } repeat ? repeat with { Source = map(repeat.Source), Key = map(repeat.Key) } : null,
            component.ElseChildren.Select(c => Transform(c, map, stripChatHeader, includeOriginalGeneratedChildren)).ToArray(), component.Groups, component.IsDefinition,
            component.PropertyRegion is { } region ? region with { Value = map(region.Value) } : null);
    }
    private static bool IsOriginalGeneratedMount(CuiComponent component) =>
        component.Type.Equals("Object", StringComparison.OrdinalIgnoreCase) &&
        component.Properties.TryGetValue("Type", out var type) && type is CuiLiteralValue literal &&
        literal.Value.Equals("OriginalChatGeneratedChildren", StringComparison.Ordinal);

    private static CuiComponent Copy(CuiComponent component, IReadOnlyDictionary<string, CuiValue> properties,
        IReadOnlyList<CuiComponent> children, IReadOnlyList<CuiComponent> elseChildren) =>
        new(component.Type, component.Name, component.Classes, properties, component.Actions, children,
            component.Condition, component.List, component.Span, component.DefaultTheme, component.Repeat,
            elseChildren, component.Groups, component.IsDefinition, component.PropertyRegion);
}
