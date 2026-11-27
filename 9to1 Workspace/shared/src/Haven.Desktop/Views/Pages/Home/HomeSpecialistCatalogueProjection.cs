using System.Security.Cryptography;
using System.Text.Json;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Home;

internal enum HomeSpecialistFilter { All, Enabled, Disabled }
internal sealed record HomeSpecialistTarget(Guid AgentId, string Snapshot);
internal sealed record HomeSpecialistConfigurationRequest(Guid? AgentId, DateTimeOffset? ExpectedUpdatedAt,
    string? ExpectedSnapshot);
internal sealed record HomeSpecialistScope(IReadOnlyList<string> Capabilities, IReadOnlyList<string> Sources,
    bool IsValid, string Note, bool HasUninspectedFields = false)
{
    internal string Summary => !IsValid ? "Permission policy needs attention" : HasUninspectedFields
        ? "Additional policy scope requires inspection" : Capabilities.Count == 0 && Sources.Count == 0
        ? "No tools or knowledge sources configured" :
        $"{Capabilities.Count} configured tool{(Capabilities.Count == 1 ? string.Empty : "s")} · {Sources.Count} source{(Sources.Count == 1 ? string.Empty : "s")}";
}
internal sealed record HomeSpecialistRow(AgentDefinition Definition, HomeSpecialistTarget Target,
    HomeSpecialistScope Scope, int Row, int Column)
{
    internal string Status => Definition.IsEnabled ? "Enabled" : "Disabled";
    internal string Origin => Definition.IsBuiltIn ? "Built-in definition" : "Your definition";
    internal string ModelPolicy => string.IsNullOrWhiteSpace(Definition.PreferredModel)
        ? "Not specified" : Definition.PreferredModel;
    internal string FallbackPolicy => string.IsNullOrWhiteSpace(Definition.FallbackModel)
        ? "No fallback configured" : Definition.FallbackModel;
    internal string Updated => Definition.UpdatedAt.ToLocalTime().ToString("g");
    internal string DetailsLabel => "Details for " + Definition.Name;
    internal string ConfigureLabel => "Configure " + Definition.Name;
    internal string InviteReason => Definition.IsEnabled
        ? "Choose a conversation or document to set the invitation scope."
        : "This Specialist is disabled. Enable it through its configuration owner before using it.";
    internal string IconSource
    {
        get
        {
            var key = Definition.IconKey?.Trim() ?? string.Empty;
            return key.Length is > 0 and <= 64 && key.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
                ? "icon:" + key : "icon:agent-default";
        }
    }
}

/// <summary>Read-only presentation of the existing Agent catalogue. It creates no registry, grant,
/// runtime or numeric definition revision. The fingerprint only identifies the inspected snapshot.</summary>
internal static class HomeSpecialistCatalogueProjection
{
    internal static string Fingerprint(AgentDefinition definition) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(definition)));

    internal static AgentDefinition[] Validate(IReadOnlyList<AgentDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if (definitions.Any(definition => definition is null || definition.Id == Guid.Empty) ||
            definitions.Select(definition => definition.Id).Distinct().Count() != definitions.Count)
            throw new InvalidDataException("The Specialist catalogue contains an invalid or ambiguous identity.");
        return definitions.ToArray();
    }

    internal static HomeSpecialistRow[] Project(IReadOnlyList<AgentDefinition> definitions, string query,
        HomeSpecialistFilter filter, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        var term = query.Trim();
        return definitions.Where(definition => filter == HomeSpecialistFilter.All ||
                definition.IsEnabled == (filter == HomeSpecialistFilter.Enabled))
            .Select(definition => (Definition: definition, Scope: ReadScope(definition.PermissionsJson)))
            .Where(item => term.Length == 0 || Contains(item.Definition.Name, term) ||
                Contains(item.Definition.Description, term) || Contains(item.Definition.DetectionRules, term) ||
                item.Scope.Capabilities.Any(value => Contains(value, term)) || item.Scope.Sources.Any(value => Contains(value, term)))
            .OrderByDescending(item => item.Definition.IsBuiltIn)
            .ThenBy(item => item.Definition.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Definition.Id)
            .Select((item, index) => new HomeSpecialistRow(item.Definition,
                new(item.Definition.Id, Fingerprint(item.Definition)), item.Scope, index / columns, index % columns)).ToArray();
    }

    internal static HomeSpecialistRow Inspect(AgentDefinition definition) => new(definition,
        new(definition.Id, Fingerprint(definition)), ReadScope(definition.PermissionsJson), 0, 0);

    internal static HomeSpecialistScope ReadScope(string? json)
    {
        const string runtimePolicy = "Configured tools and sources are checked against current permissions and availability for each submission.";
        if (string.IsNullOrWhiteSpace(json)) return new([], [], true, runtimePolicy);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return new([], [], false, "The stored permission policy is not an object. Its tools and sources cannot be confirmed here.");
            var root = document.RootElement;
            var tools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sources = new List<string>();
            var valid = ReadArray(root, "capabilities", tools.Add) & ReadArray(root, "knowledgeResources", value =>
            { sources.Add(DisplaySource(value)); return true; });
            foreach (var (flag, key) in new[] { ("webSearch", "web-search"), ("browserUse", "browser-use"), ("computerUse", "computer-device-use") })
            {
                if (!root.TryGetProperty(flag, out var value)) continue;
                if (value.ValueKind == JsonValueKind.True) tools.Add(key);
                else if (value.ValueKind != JsonValueKind.False) valid = false;
            }
            var known = new HashSet<string>(StringComparer.Ordinal)
                { "capabilities", "knowledgeResources", "webSearch", "browserUse", "computerUse" };
            var additional = root.EnumerateObject().Any(property => !known.Contains(property.Name));
            var note = !valid ? "Some stored permission fields are malformed. The configured scope needs review." :
                additional ? "Additional policy fields require the configuration owner for inspection. " + runtimePolicy : runtimePolicy;
            return new(tools.Order(StringComparer.OrdinalIgnoreCase).ToArray(), sources.Distinct(StringComparer.Ordinal).ToArray(), valid, note, additional);
        }
        catch (JsonException)
        {
            return new([], [], false, "The stored permission policy could not be read. Its tools and sources cannot be confirmed here.");
        }
    }

    private static bool ReadArray(JsonElement root, string name, Func<string, bool> add)
    {
        if (!root.TryGetProperty(name, out var values)) return true;
        if (values.ValueKind != JsonValueKind.Array) return false;
        var valid = true;
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) { valid = false; continue; }
            add(value.GetString()!);
        }
        return valid;
    }

    private static string DisplaySource(string source)
    {
        // A definition can contain a resource URL. Show its resource identity without URI credentials
        // or query secrets; no bytes are read and the reference does not imply current authorization.
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme is "http" or "https"))
            return new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty, Query = string.Empty, Fragment = string.Empty }
                .Uri.GetLeftPart(UriPartial.Path) + (uri.Query.Length > 0 ? " (query parameters hidden)" : string.Empty);
        return source;
    }

    private static bool Contains(string? value, string query) => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}
