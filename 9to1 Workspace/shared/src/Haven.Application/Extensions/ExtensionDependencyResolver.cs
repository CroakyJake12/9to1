using System.Globalization;
using Haven.Core;

namespace Haven.Application;

/// <summary>Deterministic, side-effect-free validation of extension dependency versions and cycles.</summary>
public static class ExtensionDependencyResolver
{
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ValidateGraph(
        IEnumerable<ExtensionPackageManifest> candidates,
        IEnumerable<InstalledExtensionPackage>? installed = null)
    {
        var packages = new Dictionary<string, ExtensionPackageManifest>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in installed ?? []) packages[item.Manifest.PackageId] = item.Manifest;
        foreach (var item in candidates) packages[item.PackageId] = item;

        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages.Values)
        {
            var issues = new List<string>();
            foreach (var dependency in EffectiveDependencies(package))
            {
                if (!packages.TryGetValue(dependency.PackageId, out var target))
                {
                    if (dependency.Type == ExtensionDependencyType.Required)
                        issues.Add($"Required dependency '{dependency.PackageId}' is not installed or included in this package set.");
                    continue;
                }
                if (!MatchesVersion(target.Version, dependency.VersionRange))
                    issues.Add($"Dependency '{dependency.PackageId}' version {target.Version} does not satisfy {dependency.VersionRange}.");
            }
            if (issues.Count > 0) errors[package.PackageId] = issues;
        }

        var visit = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();
        var cyclic = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in packages.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)) Visit(id);
        foreach (var id in cyclic)
        {
            if (!errors.TryGetValue(id, out var issues)) errors[id] = issues = [];
            issues.Add("Extension dependency graph contains a cycle.");
        }

        return errors.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.Distinct(StringComparer.Ordinal).ToArray(), StringComparer.OrdinalIgnoreCase);

        void Visit(string id)
        {
            if (visit.TryGetValue(id, out var state))
            {
                if (state == 1)
                {
                    var start = stack.FindIndex(value => value.Equals(id, StringComparison.OrdinalIgnoreCase));
                    if (start >= 0) foreach (var member in stack.Skip(start)) cyclic.Add(member);
                }
                return;
            }
            visit[id] = 1;
            stack.Add(id);
            foreach (var dependency in EffectiveDependencies(packages[id])
                         .Where(item => item.Type == ExtensionDependencyType.Required && packages.ContainsKey(item.PackageId))
                         .OrderBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase))
                Visit(dependency.PackageId);
            stack.RemoveAt(stack.Count - 1);
            visit[id] = 2;
        }
    }

    public static IReadOnlyList<ExtensionDependency> EffectiveDependencies(ExtensionPackageManifest package)
    {
        var dependencies = (package.DependencyDefinitions ?? []).ToDictionary(item => item.PackageId, StringComparer.OrdinalIgnoreCase);
        foreach (var legacy in package.Dependencies ?? [])
        {
            var id = legacy.Trim();
            if (id.Length == 0 || dependencies.ContainsKey(id)) continue;
            dependencies[id] = new ExtensionDependency(id, "*", ExtensionDependencyType.Required);
        }
        return dependencies.Values.OrderBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool IsValidVersionRange(string? range)
    {
        if (string.IsNullOrWhiteSpace(range) || range == "*") return !string.IsNullOrWhiteSpace(range);
        range = range.Trim();
        if (Version.TryParse(range, out _)) return true;
        if (range.StartsWith(">=", StringComparison.Ordinal) || range.StartsWith("<=", StringComparison.Ordinal) ||
            range.StartsWith(">", StringComparison.Ordinal) || range.StartsWith("<", StringComparison.Ordinal))
            return Version.TryParse(range.TrimStart('>', '<', '='), out _);
        if (range.Length >= 5 && (range[0] == '[' || range[0] == '(') && (range[^1] == ']' || range[^1] == ')'))
        {
            var bounds = range[1..^1].Split(',', StringSplitOptions.TrimEntries);
            return bounds.Length == 2 && (bounds[0].Length == 0 || Version.TryParse(bounds[0], out _)) &&
                   (bounds[1].Length == 0 || Version.TryParse(bounds[1], out _));
        }
        return false;
    }

    public static bool MatchesVersion(string version, string range)
    {
        if (!Version.TryParse(version, out var actual) || !IsValidVersionRange(range)) return false;
        range = range.Trim();
        if (range == "*") return true;
        if (Version.TryParse(range, out var exact)) return actual == exact;
        if (range.StartsWith(">=", StringComparison.Ordinal)) return actual >= Version.Parse(range[2..]);
        if (range.StartsWith("<=", StringComparison.Ordinal)) return actual <= Version.Parse(range[2..]);
        if (range.StartsWith(">", StringComparison.Ordinal)) return actual > Version.Parse(range[1..]);
        if (range.StartsWith("<", StringComparison.Ordinal)) return actual < Version.Parse(range[1..]);
        var minimumInclusive = range[0] == '[';
        var maximumInclusive = range[^1] == ']';
        var bounds = range[1..^1].Split(',', StringSplitOptions.TrimEntries);
        var aboveMinimum = bounds[0].Length == 0 || (minimumInclusive ? actual >= Version.Parse(bounds[0]) : actual > Version.Parse(bounds[0]));
        var belowMaximum = bounds[1].Length == 0 || (maximumInclusive ? actual <= Version.Parse(bounds[1]) : actual < Version.Parse(bounds[1]));
        return aboveMinimum && belowMaximum;
    }
}
