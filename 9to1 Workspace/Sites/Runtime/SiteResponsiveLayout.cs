using System.Collections.ObjectModel;
using System.Text.Json;
using HavenOS.Apps.Sites.Domain;

namespace HavenOS.Apps.Sites.Runtime;

public sealed record SiteResponsiveProperty(string Name, JsonElement Value, string? InheritedFromBreakpoint,
    bool IsExplicitOverride);
public sealed record SiteResponsiveLayoutObservation(Guid SiteId, Guid ProjectId, long ProjectRevision, Guid PageId,
    Guid ComponentId, string? Breakpoint, int? MaximumWidth, IReadOnlyDictionary<string, SiteResponsiveProperty> Properties);

/// <summary>Projects a named view of the same source using the exact stylesheet cascade.</summary>
public static class SiteResponsiveLayout
{
    public static IReadOnlyDictionary<string, int> DefaultBreakpoints() =>
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(StringComparer.Ordinal)
        { ["tablet"] = 1024, ["mobile"] = 640 });

    internal static IOrderedEnumerable<KeyValuePair<string, SiteLayoutOverride>> OrderedOverrides(
        SiteDesignSystem design, SiteLayout layout) => layout.BreakpointOverrides
        .OrderByDescending(pair => design.Breakpoints.GetValueOrDefault(pair.Key))
        .ThenBy(pair => pair.Key, StringComparer.Ordinal);

    internal static Dictionary<string, JsonElement> BaseProperties(SiteLayout layout)
    {
        var properties = new Dictionary<string, JsonElement>(layout.Properties, StringComparer.Ordinal);
        if (layout.Mode != SiteLayoutMode.Flow)
            properties.TryAdd("display", JsonSerializer.SerializeToElement(layout.Mode == SiteLayoutMode.Grid ? "grid" : "flex"));
        return properties;
    }

    internal static IReadOnlyDictionary<string, JsonElement>? PrepareMaximumWidth(
        SiteProject project, SitePage page, SiteComponent component, string breakpoint, string? value)
    {
        const string property = "max-width";
        _ = Inspect(project, page.PageId, component.ComponentId, breakpoint);
        var patch = component.Layout.BreakpointOverrides.TryGetValue(breakpoint, out var existing)
            ? existing.ChangedProperties.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (value is null)
        {
            if (!patch.Remove(property)) throw new ArgumentException("There is no explicit maximum width to reset.");
        }
        else
        {
            var normalized = value.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(normalized,
                @"\A(?:none|(?:0|[0-9]{1,5}(?:\.[0-9]{1,3})?(?:px|em|rem|%|vw)))\z",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                throw new ArgumentException("Use none, 0 or a non-negative number with px, em, rem, %, or vw.");
            patch[property] = JsonSerializer.SerializeToElement(normalized);
        }
        return patch.Count == 0 ? null : new ReadOnlyDictionary<string, JsonElement>(patch);
    }

    public static SiteResponsiveLayoutObservation Inspect(SiteProject project, Guid pageId, Guid componentId, string? breakpoint)
    {
        var page = project.Pages.Single(page => page.PageId == pageId);
        if (!Application.SiteAuthoringService.Reachable(project, page.RootComponentIds).Contains(componentId))
            throw new ArgumentException("Component does not belong to the selected page.", nameof(componentId));
        var component = project.Components.Single(node => node.ComponentId == componentId);
        int? width = null;
        if (breakpoint is not null)
        {
            if (!project.DesignSystem.Breakpoints.TryGetValue(breakpoint, out int selected) || selected <= 0)
                throw new ArgumentException("The selected breakpoint is not defined in this project.", nameof(breakpoint));
            width = selected;
        }

        var properties = BaseProperties(component.Layout).Where(pair => SiteDocumentRenderer.IsSupportedStyle(pair.Key, pair.Value))
            .ToDictionary(pair => pair.Key, pair => new SiteResponsiveProperty(pair.Key, pair.Value.Clone(), null, false), StringComparer.Ordinal);
        if (width is { } maximum)
            foreach (var rule in OrderedOverrides(project.DesignSystem, component.Layout))
            {
                if (!project.DesignSystem.Breakpoints.TryGetValue(rule.Key, out int ruleMaximum) || ruleMaximum <= 0 || ruleMaximum < maximum)
                    continue;
                foreach (var pair in rule.Value.ChangedProperties.Where(pair => SiteDocumentRenderer.IsSupportedStyle(pair.Key, pair.Value)))
                {
                    var explicitOverride = StringComparer.Ordinal.Equals(rule.Key, breakpoint);
                    properties[pair.Key] = new(pair.Key, pair.Value.Clone(), explicitOverride ? null : rule.Key, explicitOverride);
                }
            }

        return new(project.SiteId, project.ProjectId, project.Revision, pageId, componentId, breakpoint, width,
            new ReadOnlyDictionary<string, SiteResponsiveProperty>(properties));
    }
}
