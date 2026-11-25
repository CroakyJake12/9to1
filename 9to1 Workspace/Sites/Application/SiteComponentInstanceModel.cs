using System.Text.Json;
using HavenOS.Apps.Sites.Domain;

namespace HavenOS.Apps.Sites.Application;

/// <summary>How a component instance relates to its reusable definition. Linked tracks the definition with no overrides, Overridden tracks it but
/// replaces specific properties, Detached is an independent tree that no longer follows the definition.</summary>
public enum SiteInstanceKind { Standalone, Linked, Overridden, Detached }

/// <summary>Pure reusable-instance semantics over the existing SiteComponent / SiteReusableComponent model. It never mutates a definition; it only
/// classifies instances, computes an effective property view, previews which instances a definition edit affects, and detaches into an independent tree.</summary>
public static class SiteComponentInstanceModel
{
    /// <summary>Classify one component against the set of known reusable definitions.</summary>
    public static SiteInstanceKind Classify(SiteComponent component, IReadOnlyCollection<Guid> knownDefinitionIds)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (component.ReusableDefinitionId is not { } definitionId) return SiteInstanceKind.Standalone;
        // A detached copy keeps its origin id recorded but no longer tracks the definition; it carries an independent property tree.
        if (!knownDefinitionIds.Contains(definitionId)) return SiteInstanceKind.Detached;
        return component.Overrides.Count == 0 ? SiteInstanceKind.Linked : SiteInstanceKind.Overridden;
    }

    /// <summary>The effective properties for an instance: the definition's declared values overlaid by the instance's explicit overrides.
/// The definition is never mutated.</summary>
    public static IReadOnlyDictionary<string, JsonElement> EffectiveProperties(SiteReusableComponent definition, SiteComponent instance)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(instance);
        var effective = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in definition.DeclaredProperties) effective[property.Key] = property.Value;
        // The instance's own authored properties take precedence over declared defaults.
        foreach (var property in instance.Properties) effective[property.Key] = property.Value;
        // Explicit overrides win last, without touching the definition.
        foreach (var overrideProperty in instance.Overrides) effective[overrideProperty.Key] = overrideProperty.Value;
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, JsonElement>(effective);
    }

    /// <summary>Preview which instances a definition edit affects: only Linked and Overridden instances track the definition. Standalone and Detached are unaffected.</summary>
    public static IReadOnlyList<Guid> AffectedInstances(Guid definitionId, IEnumerable<SiteComponent> components, IReadOnlyCollection<Guid> knownDefinitionIds)
    {
        ArgumentNullException.ThrowIfNull(components);
        return components
            .Where(component => component.ReusableDefinitionId == definitionId && Classify(component, knownDefinitionIds) is SiteInstanceKind.Linked or SiteInstanceKind.Overridden)
            .Select(component => component.ComponentId)
            .ToArray();
    }

    /// <summary>Detach an instance into a declared independent tree: it keeps its current effective properties but no longer tracks the definition.
/// The original instance and the definition are unchanged.</summary>
    public static SiteComponent Detach(SiteReusableComponent definition, SiteComponent instance)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(instance);
        if (instance.ReusableDefinitionId is null) throw new InvalidOperationException("Only a reusable instance can be detached.");
        // Freeze the effective properties into the instance and clear the tracking link so later definition edits do not apply.
        var frozen = EffectiveProperties(definition, instance);
        return instance with { ReusableDefinitionId = null, Overrides = new Dictionary<string, JsonElement>(), Properties = frozen, Revision = checked(instance.Revision + 1) };
    }

    /// <summary>Reset a single property back to inherited: remove only that override, leaving the definition and other overrides intact.</summary>
    public static SiteComponent ResetToInherited(SiteComponent instance, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (string.IsNullOrWhiteSpace(propertyName)) throw new ArgumentException("A property name is required.", nameof(propertyName));
        var overrides = instance.Overrides.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        if (!overrides.Remove(propertyName)) throw new InvalidOperationException("That property has no explicit override to reset.");
        return instance with { Overrides = new System.Collections.ObjectModel.ReadOnlyDictionary<string, JsonElement>(overrides), Revision = checked(instance.Revision + 1) };
    }
}
