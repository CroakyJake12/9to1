using System.Text.Json;

namespace HavenOS.AIStudio;

public sealed record StudioTemplate(string TemplateId, int Version, string Name, string Description,
    StudioProjectType ProjectType, StudioToolType? ToolType, string DefinitionJson,
    IReadOnlyList<StudioDependency> Dependencies);

public static class StudioTemplateCatalog
{
    public static IReadOnlyList<StudioTemplate> All { get; } = Build();

    public static StudioTemplate? Find(string? id) => All.FirstOrDefault(item =>
        string.Equals(item.TemplateId, id, StringComparison.Ordinal));

    public static IReadOnlyList<StudioTemplate> Search(string? query, StudioProjectType? type = null)
    {
        var terms = query?.Trim();
        return All.Where(item => type is null || item.ProjectType == type)
            .Where(item => string.IsNullOrEmpty(terms) ||
                item.Name.Contains(terms, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(terms, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<StudioTemplate> Build()
    {
        HarnessDefinition Baseline(string prompt) => new(prompt, "universal-default", [], [], [], [], [], [],
            new Dictionary<string, string>(), "{}", "{}", null, true, 16, 0);

        ToolDefinition Tool(StudioToolType type, string name, string description, string instructions,
            string manifest, IReadOnlyList<StudioDependency>? dependencies = null,
            IReadOnlyDictionary<string, string>? permissions = null) =>
            new(type, description, instructions, manifest, dependencies ?? [], permissions ?? new Dictionary<string, string>(),
                "1.0.0", "{}", null);

        var items = new List<StudioTemplate>
        {
            new("harness.assistant.v1", 1, "Assistant Harness", "A tested baseline for request, response and approved tools.",
                StudioProjectType.Harness, null,
                JsonSerializer.Serialize(Baseline("Respond clearly to the user's request. Use only explicitly configured, authorised tools. Ask before consequential external actions."), StudioJson.Options), []),
            new("harness.research.v1", 1, "Research Harness", "A source-aware research workflow with explicit review before external actions.",
                StudioProjectType.Harness, null,
                JsonSerializer.Serialize(Baseline("Research the question using configured sources. Separate sourced findings from inference, retain provenance, and ask before consequential external actions."), StudioJson.Options), []),
            new("tool.skill.v1", 1, "Skill", "Reusable instructions, examples and declared tool dependencies.",
                StudioProjectType.Tool, StudioToolType.Skill,
                JsonSerializer.Serialize(Tool(StudioToolType.Skill, "New skill", "Describe the reusable workflow.",
                    "## Instructions\nDescribe the task, boundaries and expected output.\n\n## Examples\nAdd representative input and output.",
                    "{\"schemaVersion\":1,\"kind\":\"skill\",\"id\":\"replace-with-stable-id\",\"name\":\"New skill\",\"version\":\"1.0.0\",\"dependencies\":[],\"permissions\":[]}"), StudioJson.Options), []),
            new("tool.plugin.v1", 1, "Plugin", "Typed actions with declared permissions and account dependencies.",
                StudioProjectType.Tool, StudioToolType.Plugin,
                JsonSerializer.Serialize(Tool(StudioToolType.Plugin, "New plugin", "Describe the plugin capability.", string.Empty,
                    "{\"schemaVersion\":1,\"kind\":\"plugin\",\"id\":\"replace-with-stable-id\",\"name\":\"New plugin\",\"version\":\"1.0.0\",\"actions\":[],\"permissions\":[],\"dependencies\":[]}"), StudioJson.Options), []),
            new("tool.mcp-client.v1", 1, "MCP Client", "Configure a client connection, schemas, authentication reference and permissions.",
                StudioProjectType.Tool, StudioToolType.Mcp,
                JsonSerializer.Serialize(Tool(StudioToolType.Mcp, "New MCP connection", "Describe the MCP server.", string.Empty,
                    "{\"schemaVersion\":1,\"kind\":\"mcp\",\"id\":\"replace-with-stable-id\",\"role\":\"client\",\"transport\":null,\"endpoint\":null,\"authReference\":null,\"capabilities\":[],\"permissions\":[]}"), StudioJson.Options), [])
        };
        return items.AsReadOnly();
    }
}
