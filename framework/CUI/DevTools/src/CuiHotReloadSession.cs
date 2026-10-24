using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;

namespace Haven.CUI.DevTools;

public enum CuiHotReloadChangeKind
{
    Added,
    Removed,
    Replaced,
    Unchanged,
    Moved
}

public sealed record CuiHotReloadChange(string StableId, CuiHotReloadChangeKind Kind);

public sealed record CuiHotReloadResult(
    bool Applied,
    long Revision,
    CuiCompiledDocument? Current,
    IReadOnlyList<CuiDiagnostic> Diagnostics,
    IReadOnlyList<CuiHotReloadChange> Changes);

/// <summary>
/// Keeps the last valid compiled tree while edits are being compiled and computes
/// stable-identity changes for a host to apply without rebuilding unrelated nodes.
/// </summary>
public sealed class CuiHotReloadSession
{
    private readonly record struct NodePosition(string? ParentStableId, int Index, bool IsElseBranch);

    private readonly CuiCompiler _compiler;
    private readonly CuiCompilerOptions _options;
    private CuiCompiledDocument? _current;
    private long _revision;

    public CuiHotReloadSession(CuiCompiler compiler, CuiCompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(options);
        _compiler = compiler;
        _options = options;
    }

    public long Revision => _revision;

    public CuiCompiledDocument? Current => _current;

    public CuiHotReloadResult Update(string source, string sourceName)
    {
        var compilation = _compiler.Compile(source, sourceName, _options);
        if (!compilation.Succeeded || compilation.Output is null)
            return new CuiHotReloadResult(false, _revision, _current, compilation.Diagnostics, []);

        var next = compilation.Output;
        if (_current is not null && _current.Metadata != next.Metadata)
        {
            var diagnostic = new CuiDiagnostic("CUIH001", CuiDiagnosticSeverity.Error,
                "Hot reload cannot apply a document with different language, runtime ABI, registry, or capability metadata.",
                next.Semantics.Span);
            return new CuiHotReloadResult(false, _revision, _current,
                Array.AsReadOnly(compilation.Diagnostics.Append(diagnostic).ToArray()), []);
        }

        var changes = Diff(_current, next);
        _current = next;
        _revision = checked(_revision + 1);
        return new CuiHotReloadResult(true, _revision, _current, compilation.Diagnostics, changes);
    }

    private static IReadOnlyList<CuiHotReloadChange> Diff(CuiCompiledDocument? previous, CuiCompiledDocument next)
    {
        var oldNodes = previous is null ? new Dictionary<string, CuiCompiledNode>(StringComparer.Ordinal)
            : Flatten(previous.Components.Concat(previous.Templates.Values.SelectMany(template => template.Content)));
        var newNodes = Flatten(next.Components.Concat(next.Templates.Values.SelectMany(template => template.Content)));
        var oldPositions = previous is null
            ? new Dictionary<string, NodePosition>(StringComparer.Ordinal)
            : Positions(previous);
        var newPositions = Positions(next);
        var oldSemantics = previous is null
            ? new Dictionary<string, CuiComponent>(StringComparer.Ordinal)
            : SemanticComponents(previous);
        var newSemantics = SemanticComponents(next);
        var changes = new List<CuiHotReloadChange>();
        foreach (var (stableId, newNode) in newNodes)
        {
            if (!oldNodes.TryGetValue(stableId, out var oldNode))
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Added));
            else if (oldNode.Type != newNode.Type)
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Replaced));
            else if (oldNode.IsDefinition != newNode.IsDefinition)
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Replaced));
            else if (!PropertiesEqual(oldNode, newNode))
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Replaced));
            else if (!ComponentsEqual(oldSemantics[stableId], newSemantics[stableId]))
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Replaced));
            else if (oldPositions[stableId] != newPositions[stableId])
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Moved));
            else
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Unchanged));
        }
        foreach (var stableId in oldNodes.Keys.Except(newNodes.Keys, StringComparer.Ordinal))
            changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Removed));
        DiffTemplates(previous?.Templates, next.Templates, changes);
        DiffActions(previous?.Semantics.Actions, next.Semantics.Actions, changes);
        DiffResources(previous?.Semantics.Resources, next.Semantics.Resources, changes);
        DiffStyles(previous?.Semantics.Styles, next.Semantics.Styles, changes);
        DiffVariables(previous?.Semantics.Variables, next.Semantics.Variables, changes);
        DiffEffects(previous?.Semantics.Effects, next.Semantics.Effects, changes);
        DiffImports(previous?.Semantics.Imports, next.Semantics.Imports, changes);
        DiffValues("root-property", previous?.Semantics.RootProperties, next.Semantics.RootProperties, changes);
        return Array.AsReadOnly(changes.OrderBy(change => change.StableId, StringComparer.Ordinal).ToArray());
    }

    private static void DiffVariables(
        IReadOnlyDictionary<string, CuiVariableDeclaration>? previous,
        IReadOnlyDictionary<string, CuiVariableDeclaration> next,
        ICollection<CuiHotReloadChange> changes)
    {
        foreach (var (name, variable) in next)
        {
            var stableId = $"variable:{name}";
            if (previous is null || !previous.TryGetValue(name, out var oldVariable))
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Added));
            else if (!VariablesEqual(oldVariable, variable))
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Replaced));
        }

        if (previous is null) return;
        foreach (var name in previous.Keys.Except(next.Keys, StringComparer.Ordinal))
            changes.Add(new CuiHotReloadChange($"variable:{name}", CuiHotReloadChangeKind.Removed));
    }

    private static bool VariablesEqual(CuiVariableDeclaration first, CuiVariableDeclaration second) =>
        string.Equals(first.Name, second.Name, StringComparison.Ordinal)
        && string.Equals(first.Type, second.Type, StringComparison.Ordinal)
        && string.Equals(first.Value, second.Value, StringComparison.Ordinal)
        && string.Equals(first.Source, second.Source, StringComparison.Ordinal)
        && string.Equals(first.Selection, second.Selection, StringComparison.Ordinal);

    private static void DiffEffects(
        IReadOnlyList<CuiEffectDefinition>? previous,
        IReadOnlyList<CuiEffectDefinition> next,
        ICollection<CuiHotReloadChange> changes)
    {
        var oldEffects = EffectIdentities(previous ?? Array.Empty<CuiEffectDefinition>());
        var newEffects = EffectIdentities(next);
        foreach (var (stableId, current) in newEffects)
        {
            if (!oldEffects.TryGetValue(stableId, out var old))
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Added));
            else if (!EffectsEqual(old.Effect, current.Effect))
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Replaced));
            else if (old.Index != current.Index)
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Moved));
        }

        foreach (var stableId in oldEffects.Keys.Except(newEffects.Keys, StringComparer.Ordinal))
            changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Removed));
    }

    private static Dictionary<string, (CuiEffectDefinition Effect, int Index)> EffectIdentities(
        IReadOnlyList<CuiEffectDefinition> effects)
    {
        var counts = effects.GroupBy(effect => effect.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new Dictionary<string, (CuiEffectDefinition Effect, int Index)>(StringComparer.Ordinal);
        for (var index = 0; index < effects.Count; index++)
        {
            var effect = effects[index];
            occurrences.TryGetValue(effect.Name, out var occurrence);
            occurrences[effect.Name] = occurrence + 1;
            var stableId = counts[effect.Name] == 1
                ? $"effect:{effect.Name}"
                : $"effect:{effect.Name}#{occurrence}";
            result.Add(stableId, (effect, index));
        }

        return result;
    }

    private static bool EffectsEqual(CuiEffectDefinition first, CuiEffectDefinition second) =>
        string.Equals(first.Name, second.Name, StringComparison.Ordinal)
        && ConditionsEqual(first.Condition, second.Condition)
        && string.Equals(first.ActionName, second.ActionName, StringComparison.Ordinal);

    private static void DiffImports(
        IReadOnlyList<CuiImportDefinition>? previous,
        IReadOnlyList<CuiImportDefinition> next,
        ICollection<CuiHotReloadChange> changes)
    {
        for (var index = 0; index < next.Count; index++)
        {
            var import = next[index];
            var stableId = $"import:{index}";
            if (previous is null || index >= previous.Count)
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Added));
            else if (!ImportsEqual(previous[index], import))
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Replaced));
        }

        if (previous is null) return;
        for (var index = next.Count; index < previous.Count; index++)
            changes.Add(new CuiHotReloadChange($"import:{index}", CuiHotReloadChangeKind.Removed));
    }

    private static bool ImportsEqual(CuiImportDefinition first, CuiImportDefinition second) =>
        string.Equals(first.Source, second.Source, StringComparison.Ordinal)
        && string.Equals(first.Alias, second.Alias, StringComparison.Ordinal);

    private static void DiffValues(
        string prefix,
        IReadOnlyDictionary<string, CuiValue>? previous,
        IReadOnlyDictionary<string, CuiValue> next,
        ICollection<CuiHotReloadChange> changes)
    {
        foreach (var (key, value) in next)
        {
            var stableId = $"{prefix}:{key}";
            if (previous is null || !previous.TryGetValue(key, out var oldValue))
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Added));
            else if (!ValuesEqual(oldValue, value))
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Replaced));
        }

        if (previous is null) return;
        foreach (var key in previous.Keys.Except(next.Keys, StringComparer.Ordinal))
            changes.Add(new CuiHotReloadChange($"{prefix}:{key}", CuiHotReloadChangeKind.Removed));
    }

    private static void DiffStyles(
        IReadOnlyList<CuiStyleDefinition>? previous,
        IReadOnlyList<CuiStyleDefinition> next,
        ICollection<CuiHotReloadChange> changes)
    {
        for (var index = 0; index < next.Count; index++)
        {
            var style = next[index];
            var stableId = $"style:{index}";
            if (previous is null || index >= previous.Count)
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Added));
            else if (!StylesEqual(previous[index], style))
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Replaced));
        }

        if (previous is null) return;
        for (var index = next.Count; index < previous.Count; index++)
            changes.Add(new CuiHotReloadChange($"style:{index}", CuiHotReloadChangeKind.Removed));
    }

    private static bool StylesEqual(CuiStyleDefinition first, CuiStyleDefinition second) =>
        string.Equals(first.Selector, second.Selector, StringComparison.Ordinal)
        && first.Setters.Count == second.Setters.Count
        && first.Setters.Zip(second.Setters).All(pair =>
            string.Equals(CuiThemeResourceInspection.CanonicalName(pair.First.Property),
                CuiThemeResourceInspection.CanonicalName(pair.Second.Property), StringComparison.Ordinal)
            && ValuesEqual(pair.First.Value, pair.Second.Value));

    private static void DiffResources(
        IReadOnlyDictionary<string, CuiResourceDefinition>? previous,
        IReadOnlyDictionary<string, CuiResourceDefinition> next,
        ICollection<CuiHotReloadChange> changes)
    {
        foreach (var (key, resource) in next)
        {
            var stableId = $"resource:{key}";
            if (previous is null || !previous.TryGetValue(key, out var oldResource))
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Added));
            else if (!ValuesEqual(oldResource.Value, resource.Value))
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Replaced));
        }

        if (previous is null) return;
        foreach (var key in previous.Keys.Except(next.Keys, StringComparer.Ordinal))
            changes.Add(new CuiHotReloadChange($"resource:{key}", CuiHotReloadChangeKind.Removed));
    }

    private static void DiffActions(
        IReadOnlyDictionary<string, CuiActionDefinition>? previous,
        IReadOnlyDictionary<string, CuiActionDefinition> next,
        ICollection<CuiHotReloadChange> changes)
    {
        foreach (var (name, action) in next)
        {
            var stableId = $"action:{name}";
            if (previous is null || !previous.TryGetValue(name, out var oldAction))
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Added));
            else if (!ActionsEqual(oldAction, action))
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Replaced));
        }

        if (previous is null) return;
        foreach (var name in previous.Keys.Except(next.Keys, StringComparer.Ordinal))
            changes.Add(new CuiHotReloadChange($"action:{name}", CuiHotReloadChangeKind.Removed));
    }

    private static bool ActionsEqual(CuiActionDefinition first, CuiActionDefinition second)
    {
        var firstSets = first.Sets ?? Array.Empty<CuiSetDirective>();
        var secondSets = second.Sets ?? Array.Empty<CuiSetDirective>();
        return string.Equals(first.Name, second.Name, StringComparison.Ordinal)
               && string.Equals(first.Command, second.Command, StringComparison.Ordinal)
               && OptionalValuesEqual(first.Parameter, second.Parameter)
               && string.Equals(first.LocalTarget, second.LocalTarget, StringComparison.Ordinal)
               && string.Equals(first.ResultTarget, second.ResultTarget, StringComparison.Ordinal)
               && firstSets.Count == secondSets.Count
               && firstSets.Zip(secondSets).All(pair =>
                   string.Equals(pair.First.Target, pair.Second.Target, StringComparison.Ordinal)
                   && ValuesEqual(pair.First.Value, pair.Second.Value)
                   && ConditionsEqual(pair.First.When, pair.Second.When));
    }

    private static Dictionary<string, NodePosition> Positions(CuiCompiledDocument document)
    {
        var result = new Dictionary<string, NodePosition>(StringComparer.Ordinal);

        void Add(IReadOnlyList<CuiCompiledNode> nodes, string? parentStableId, bool isElseBranch)
        {
            for (var index = 0; index < nodes.Count; index++)
            {
                var node = nodes[index];
                result.Add(node.StableId, new NodePosition(parentStableId, index, isElseBranch));
                Add(node.Children, node.StableId, isElseBranch: false);
                Add(node.ElseChildren, node.StableId, isElseBranch: true);
            }
        }

        Add(document.Components, parentStableId: null, isElseBranch: false);
        foreach (var template in document.Templates.Values)
            Add(template.Content, $"template:{template.Name}", isElseBranch: false);

        return result;
    }

    private static Dictionary<string, CuiComponent> SemanticComponents(CuiCompiledDocument document)
    {
        var result = new Dictionary<string, CuiComponent>(StringComparer.Ordinal);

        void Add(IReadOnlyList<CuiCompiledNode> nodes, IReadOnlyList<CuiComponent> components)
        {
            if (nodes.Count != components.Count)
                throw new InvalidOperationException("Compiled CUI nodes do not match their retained semantic components.");

            for (var index = 0; index < nodes.Count; index++)
            {
                var node = nodes[index];
                var component = components[index];
                result.Add(node.StableId, component);
                Add(node.Children, component.Children);
                Add(node.ElseChildren, component.ElseChildren);
            }
        }

        Add(document.Components, document.Semantics.Components);
        foreach (var (name, template) in document.Templates)
        {
            if (!document.Semantics.Templates.TryGetValue(name, out var semanticTemplate))
                throw new InvalidOperationException($"Compiled CUI template '{name}' has no retained semantic definition.");
            Add(template.Content, semanticTemplate.Content);
        }

        return result;
    }

    private static bool ComponentsEqual(CuiComponent first, CuiComponent second) =>
        string.Equals(first.Name, second.Name, StringComparison.Ordinal)
        && first.Classes.SequenceEqual(second.Classes, StringComparer.Ordinal)
        && ActionsEqual(first.Actions, second.Actions)
        && ConditionsEqual(first.Condition, second.Condition)
        && ListsEqual(first.List, second.List)
        && RepeatsEqual(first.Repeat, second.Repeat)
        && string.Equals(first.DefaultTheme, second.DefaultTheme, StringComparison.Ordinal)
        && first.Groups.SequenceEqual(second.Groups, StringComparer.Ordinal)
        && PropertyRegionsEqual(first.PropertyRegion, second.PropertyRegion)
        && first.IsThemeScope == second.IsThemeScope
        && string.Equals(first.Text, second.Text, StringComparison.Ordinal)
        && TextPartsEqual(first.TextParts, second.TextParts);

    private static bool ActionsEqual(
        IReadOnlyDictionary<string, CuiActionReference> first,
        IReadOnlyDictionary<string, CuiActionReference> second) =>
        first.Count == second.Count
        && first.All(pair => second.TryGetValue(pair.Key, out var action)
                             && string.Equals(pair.Value.Name, action.Name, StringComparison.Ordinal));

    private static bool ConditionsEqual(CuiCondition? first, CuiCondition? second) =>
        first is null ? second is null
        : second is not null && ValuesEqual(first.Test, second.Test)
          && first.Negate == second.Negate && first.IsLive == second.IsLive;

    private static bool ListsEqual(CuiListDefinition? first, CuiListDefinition? second) =>
        first is null ? second is null
        : second is not null && ValuesEqual(first.Items, second.Items)
          && string.Equals(first.ItemTemplate, second.ItemTemplate, StringComparison.Ordinal)
          && string.Equals(first.EmptyTemplate, second.EmptyTemplate, StringComparison.Ordinal);

    private static bool RepeatsEqual(CuiRepeatDefinition? first, CuiRepeatDefinition? second) =>
        first is null ? second is null
        : second is not null && ValuesEqual(first.Source, second.Source)
          && string.Equals(first.ItemName, second.ItemName, StringComparison.Ordinal)
          && ValuesEqual(first.Key, second.Key)
          && OptionalValuesEqual(first.Template, second.Template);

    private static bool PropertyRegionsEqual(
        CuiPropertyRegionDefinition? first,
        CuiPropertyRegionDefinition? second) =>
        first is null ? second is null
        : second is not null && string.Equals(first.PropertyName, second.PropertyName, StringComparison.Ordinal)
          && ValuesEqual(first.Value, second.Value);

    private static bool OptionalValuesEqual(CuiValue? first, CuiValue? second) =>
        first is null ? second is null : second is not null && ValuesEqual(first, second);

    private static bool TextPartsEqual(IReadOnlyList<CuiTextPart> first, IReadOnlyList<CuiTextPart> second) =>
        first.Count == second.Count
        && first.Zip(second).All(pair => (pair.First, pair.Second) switch
        {
            (CuiLiteralTextPart left, CuiLiteralTextPart right) =>
                string.Equals(left.Value, right.Value, StringComparison.Ordinal),
            (CuiExpressionTextPart left, CuiExpressionTextPart right) => ValuesEqual(left.Value, right.Value),
            _ => pair.First == pair.Second
        });

    private static void DiffTemplates(
        IReadOnlyDictionary<string, CuiCompiledTemplate>? previous,
        IReadOnlyDictionary<string, CuiCompiledTemplate> next,
        ICollection<CuiHotReloadChange> changes)
    {
        foreach (var (name, template) in next)
        {
            var stableId = $"template:{name}";
            if (previous is null || !previous.TryGetValue(name, out var oldTemplate))
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Added));
            else if (!oldTemplate.Parameters.SequenceEqual(template.Parameters, StringComparer.Ordinal))
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Replaced));
        }

        if (previous is null) return;
        foreach (var name in previous.Keys.Except(next.Keys, StringComparer.Ordinal))
            changes.Add(new CuiHotReloadChange($"template:{name}", CuiHotReloadChangeKind.Removed));
    }

    private static Dictionary<string, CuiCompiledNode> Flatten(IEnumerable<CuiCompiledNode> roots)
    {
        var result = new Dictionary<string, CuiCompiledNode>(StringComparer.Ordinal);
        void Visit(IEnumerable<CuiCompiledNode> nodes)
        {
            foreach (var node in nodes)
            {
                result.Add(node.StableId, node);
                Visit(node.Children);
                Visit(node.ElseChildren);
            }
        }
        Visit(roots);
        return result;
    }

    private static bool PropertiesEqual(CuiCompiledNode first, CuiCompiledNode second) =>
        first.Properties.Count == second.Properties.Count
        && first.Properties.All(pair => second.Properties.TryGetValue(pair.Key, out var value)
                                        && pair.Value.LanguageType == value.LanguageType
                                        && ValuesEqual(pair.Value.Value, value.Value));

    private static bool ValuesEqual(CuiValue first, CuiValue second) => (first, second) switch
    {
        (CuiLiteralValue left, CuiLiteralValue right) =>
            string.Equals(left.Value, right.Value, StringComparison.Ordinal),
        (CuiBindingValue left, CuiBindingValue right) =>
            string.Equals(left.Path, right.Path, StringComparison.Ordinal)
            && left.Mode == right.Mode
            && string.Equals(left.Fallback, right.Fallback, StringComparison.Ordinal)
            && string.Equals(left.TargetType, right.TargetType, StringComparison.Ordinal),
        (CuiResourceValue left, CuiResourceValue right) =>
            string.Equals(left.Key, right.Key, StringComparison.Ordinal),
        (CuiInvalidValue left, CuiInvalidValue right) =>
            string.Equals(left.RawValue, right.RawValue, StringComparison.Ordinal)
            && string.Equals(left.DiagnosticCode, right.DiagnosticCode, StringComparison.Ordinal)
            && string.Equals(left.Message, right.Message, StringComparison.Ordinal),
        _ => first == second
    };
}
