using System.Globalization;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;
using Haven.Core;

namespace Haven.Application;

/// <summary>Compiled, pure user-message derivation through the shared Home graph schema,
/// Automation projection and executor. This data grants no model, file or task authority.</summary>
public sealed class SpaceUserMessageGraph
{
    public const string ProfileId = "spaces.user-message.v1";
    public const string SourceType = "spaces.user-message.source";
    public const string TruncateType = "spaces.user-message.truncate";
    public const string OutputType = "spaces.user-message.output";
    public const string ExtractNumbersType = "spaces.user-message.extract-numbers";
    public const string NumbersToTextType = "spaces.user-message.numbers-to-text";
    public const string SearchWordsType = "spaces.user-message.search-words";
    public const string MatchesToTextType = "spaces.user-message.matches-to-text";
    public const string NumbersDataType = "spaces.number-extraction.v1";
    public const string MatchesDataType = "spaces.phrase-matches.v1";
    public const string ParseJsonType = "spaces.user-message.parse-json";
    public const string SelectJsonType = "spaces.user-message.select-json";
    public const string RenderJsonType = "spaces.user-message.render-json";
    public const string JsonStringType = "spaces.user-message.json-string";
    public const string JsonDataType = "spaces.json-value.v1";
    public const string RouteWordsType = "spaces.user-message.route-words";
    public const string MergeType = "spaces.user-message.merge-selected";
    public const string RouteJsonBooleanType = "spaces.user-message.route-json-boolean";
    public const string CapabilityId = "spaces.user-message.derive";
    internal const string Category = "SpaceUserMessage";
    private readonly AutomationGraphProjection _projection;
    private readonly IReadOnlyDictionary<Guid, string> _types;
    private readonly Guid _output;
    private SpaceUserMessageGraph(Guid spaceId, long spaceRevision, AutomationGraphProjection projection,
        IReadOnlyDictionary<Guid, string> types, Guid output)
    { SpaceId = spaceId; SpaceRevision = spaceRevision; _projection = projection; _types = types; _output = output; }
    public Guid SpaceId { get; }
    public long SpaceRevision { get; }
    public Guid GraphId => _projection.GraphId;
    public long GraphRevision => _projection.SourceRevision;
    public string Sha256 => _projection.SourceDigest;

    public static IReadOnlyList<GraphNodeType> Types() =>
    [
        Type(SourceType, "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
            [new("out", GraphPortDirection.Output, "string")]),
        Type(TruncateType, "{\"type\":\"object\",\"properties\":{\"maximumTextElements\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":65536}},\"required\":[\"maximumTextElements\"],\"additionalProperties\":false}",
            [new("in", GraphPortDirection.Input, "string"), new("out", GraphPortDirection.Output, "string")]),
        Type(OutputType, "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
            [new("in", GraphPortDirection.Input, "string")]),
        Type(ExtractNumbersType, "{\"type\":\"object\",\"properties\":{\"locale\":{\"type\":\"string\",\"enum\":[\"invariant\",\"en-US\",\"de-DE\",\"fr-FR\"]}},\"required\":[\"locale\"],\"additionalProperties\":false}",
            [new("in", GraphPortDirection.Input, "string"), new("out", GraphPortDirection.Output, NumbersDataType)]),
        Type(NumbersToTextType, "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
            [new("in", GraphPortDirection.Input, NumbersDataType), new("out", GraphPortDirection.Output, "string")]),
        Type(SearchWordsType, "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":256},\"locale\":{\"type\":\"string\",\"enum\":[\"invariant\"]},\"comparison\":{\"type\":\"string\",\"enum\":[\"ordinal\",\"ordinal-ignore-case\"]},\"boundary\":{\"type\":\"string\",\"enum\":[\"substring\",\"whole-word\"]}},\"required\":[\"query\",\"locale\",\"comparison\",\"boundary\"],\"additionalProperties\":false}",
            [new("in", GraphPortDirection.Input, "string"), new("out", GraphPortDirection.Output, MatchesDataType)]),
        Type(MatchesToTextType, "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
            [new("in", GraphPortDirection.Input, MatchesDataType), new("out", GraphPortDirection.Output, "string")]),
        Type(ParseJsonType, "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
            [new("in", GraphPortDirection.Input, "string"), new("out", GraphPortDirection.Output, JsonDataType)]),
        Type(SelectJsonType, "{\"type\":\"object\",\"properties\":{\"pointer\":{\"type\":\"string\",\"maxLength\":512}},\"required\":[\"pointer\"],\"additionalProperties\":false}",
            [new("in", GraphPortDirection.Input, JsonDataType), new("out", GraphPortDirection.Output, JsonDataType)]),
        Type(RenderJsonType, "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
            [new("in", GraphPortDirection.Input, JsonDataType), new("out", GraphPortDirection.Output, "string")]),
        Type(JsonStringType, "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
            [new("in", GraphPortDirection.Input, JsonDataType), new("out", GraphPortDirection.Output, "string")]),
        Type(RouteWordsType, "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":256},\"locale\":{\"type\":\"string\",\"enum\":[\"invariant\"]},\"comparison\":{\"type\":\"string\",\"enum\":[\"ordinal\",\"ordinal-ignore-case\"]},\"boundary\":{\"type\":\"string\",\"enum\":[\"substring\",\"whole-word\"]}},\"required\":[\"query\",\"locale\",\"comparison\",\"boundary\"],\"additionalProperties\":false}",
            [new("in", GraphPortDirection.Input, "string"), new("true", GraphPortDirection.Output, "string"), new("false", GraphPortDirection.Output, "string")]),
        Type(RouteJsonBooleanType, "{\"type\":\"object\",\"properties\":{\"pointer\":{\"type\":\"string\",\"maxLength\":512}},\"required\":[\"pointer\"],\"additionalProperties\":false}",
            [new("in", GraphPortDirection.Input, JsonDataType), new("true", GraphPortDirection.Output, JsonDataType), new("false", GraphPortDirection.Output, JsonDataType)]),
        Type(MergeType, "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
            [new("in", GraphPortDirection.Input, "string", true), new("out", GraphPortDirection.Output, "string")])
    ];
    public static GraphCapabilityProfile Profile() => new(ProfileId,
        new HashSet<string>([SourceType, TruncateType, OutputType, ExtractNumbersType, NumbersToTextType, SearchWordsType, MatchesToTextType,
            ParseJsonType, SelectJsonType, RenderJsonType, JsonStringType, RouteWordsType, RouteJsonBooleanType, MergeType], StringComparer.Ordinal),
        new HashSet<string>([CapabilityId], StringComparer.Ordinal));
    /// <summary>Author a node from the actual shared registered schema. This is draft
    /// data only; inserting it never activates a Space or starts a request.</summary>
    public static SpaceLayoutNode CreateNode(string typeId)
    {
        var type = CanonicalAutomationCoreNodes.CreateRegistry().ListNodeTypes(ProfileId)
            .SingleOrDefault(type => type.TypeId == typeId)
            ?? throw new ArgumentException("No registered Space user-message node has this type.", nameof(typeId));
        return new(Guid.NewGuid(), typeId == SourceType ? "Event" : "Logic", typeId switch
        {
            SourceType => "Original user message", TruncateType => "Truncate text", OutputType => "Request text",
            ExtractNumbersType => "Extract numbers", NumbersToTextType => "Render numbers",
            SearchWordsType => "Search words or phrases", MatchesToTextType => "Render matches",
            ParseJsonType => "Parse strict JSON (depth 32)", SelectJsonType => "Select JSON value",
            RenderJsonType => "Render compact JSON", JsonStringType => "Read JSON string",
            RouteWordsType => "Route by words or phrases", RouteJsonBooleanType => "Route by JSON boolean", MergeType => "Merge selected text",
            _ => throw new ArgumentException("This node has no authoring implementation.", nameof(typeId))
        })
        {
            TypeId = type.TypeId, SchemaVersion = type.Version,
            Ports = type.Ports.Select(port => new SpaceLayoutPort(port.Key, PortLabel(typeId, port),
                port.Direction == GraphPortDirection.Input ? SpaceLayoutPortDirection.Input : SpaceLayoutPortDirection.Output,
                port.DataType, port.Multiple)).ToArray(),
            Metadata = typeId switch
            {
                TruncateType => new Dictionary<string, string>(StringComparer.Ordinal) { ["maximumTextElements"] = "1000" },
                ExtractNumbersType => new Dictionary<string, string>(StringComparer.Ordinal) { ["locale"] = "invariant" },
                SelectJsonType or RouteJsonBooleanType => new Dictionary<string, string>(StringComparer.Ordinal) { ["pointer"] = "" },
                SearchWordsType or RouteWordsType => new Dictionary<string, string>(StringComparer.Ordinal)
                    { ["query"] = "word", ["locale"] = "invariant", ["comparison"] = "ordinal", ["boundary"] = "whole-word" },
                _ => new Dictionary<string, string>(StringComparer.Ordinal)
            }
        };
    }
    private static string PortLabel(string typeId, GraphPortTemplate port) => port.Key switch
    {
        "true" => typeId == RouteJsonBooleanType ? "True" : "Match",
        "false" => typeId == RouteJsonBooleanType ? "False" : "No match",
        _ => port.DataType switch { "string" => "Text", NumbersDataType => "Numbers", JsonDataType => "JSON", _ => "Matches" }
    };
    private static GraphNodeType Type(string id, string schema, IReadOnlyList<GraphPortTemplate> ports)
    {
        using var document = JsonDocument.Parse(schema);
        return new(id, 1, "spaces", CapabilityId, document.RootElement.Clone(), ports);
    }
    private static IEnumerable<AutomationNodeGraphBinding> Bindings() => Types().Select(type =>
        new AutomationNodeGraphBinding(type.TypeId, type.Version, type.AppId, type.CapabilityId,
            entity => entity is null, node => new(node.NodeId, Category, null, node.TypeId,
                node.Configuration.EnumerateObject().ToDictionary(value => value.Name,
                    value => value.Value.ToString(), StringComparer.Ordinal))
            {
                Ports = node.Ports.Select(port => new AutomationGraphPortDefinition(port.Key, port.Key,
                    port.Direction == GraphPortDirection.Input ? AutomationGraphPortDirection.Input : AutomationGraphPortDirection.Output,
                    port.DataType, port.Multiple)).ToArray()
            })
        {
            OutputBranches = type.TypeId is RouteWordsType or RouteJsonBooleanType
                ? new Dictionary<string, string>(StringComparer.Ordinal) { ["true"] = "true", ["false"] = "false" }
                : new Dictionary<string, string>(StringComparer.Ordinal)
        });

    public static SpaceUserMessageGraph Compile(Guid spaceId, long spaceRevision, SpaceLayoutDocument layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (spaceId == Guid.Empty || spaceRevision < 1 || layout.GraphId == Guid.Empty || layout.Revision < 1 ||
            layout.SchemaVersion != SpaceLayoutDocument.CurrentSchemaVersion || layout.Nodes is null || layout.Edges is null ||
            layout.Nodes.Count is < 2 or > SpaceLayoutDocument.MaximumNodes || layout.Edges.Count > SpaceLayoutDocument.MaximumEdges ||
            layout.Edges.Any(edge => edge is null || edge.Metadata is null || edge.Metadata.Count != 0 || !string.IsNullOrEmpty(edge.Branch)) ||
            layout.Nodes.Any(node => node is null || node.Ports is null || node.Ports.Count > 3 || node.Metadata is null || node.Metadata.Count > 4))
            throw Invalid("The Space graph needs bounded canonical identities and a supported schema.");
        // Snapshot only executable data. Unrelated editor comments/geometry are not copied
        // into a request, and bounds are checked before cloning editable collections.
        layout = layout with
        {
            Nodes = layout.Nodes.Select(node => node with
            { Ports = node.Ports.ToArray(), Metadata = node.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) }).ToArray(),
            Edges = layout.Edges.ToArray()
        };
        var schemas = CanonicalAutomationCoreNodes.CreateRegistry();
        var registered = schemas.ListNodeTypes(ProfileId);
        var nodes = new List<GraphNode>();
        foreach (var node in layout.Nodes)
        {
            if (node is null || node.Ports is null || node.Metadata is null ||
                node.Ports.Any(port => port is null || !Enum.IsDefined(port.Direction))) throw Invalid("A Space graph node is incomplete.");
            var type = registered.SingleOrDefault(type => type.TypeId == node.TypeId && type.Version == node.SchemaVersion)
                ?? throw Invalid("A Space graph node has no supported registered user-message implementation.");
            JsonElement configuration;
            if (node.TypeId == TruncateType)
            {
                if (node.Metadata.Count != 1 || !node.Metadata.TryGetValue("maximumTextElements", out var text) ||
                    !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var maximum) || maximum is < 1 or > 65536)
                    throw Invalid("Truncate requires an explicit maximumTextElements between 1 and 65536.");
                configuration = JsonSerializer.SerializeToElement(new { maximumTextElements = maximum });
            }
            else if (node.TypeId == ExtractNumbersType)
            {
                if (node.Metadata.Count != 1 || !node.Metadata.TryGetValue("locale", out var locale) ||
                    locale is not ("invariant" or "en-US" or "de-DE" or "fr-FR"))
                    throw Invalid("Numeric extraction requires an explicit supported locale: invariant, en-US, de-DE or fr-FR.");
                configuration = JsonSerializer.SerializeToElement(node.Metadata);
            }
            else if (node.TypeId is SelectJsonType or RouteJsonBooleanType)
            {
                if (node.Metadata.Count != 1 || !node.Metadata.TryGetValue("pointer", out var pointer) || !IsSupportedJsonPointer(pointer))
                    throw Invalid("JSON selection or routing requires one RFC 6901 pointer of at most 512 characters; empty selects the root, and only ~0/~1 escapes are supported.");
                configuration = JsonSerializer.SerializeToElement(node.Metadata);
            }
            else if (node.TypeId is SearchWordsType or RouteWordsType)
            {
                if (node.Metadata.Count != 4 || !node.Metadata.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query) || query.Length > 256 ||
                    !node.Metadata.TryGetValue("locale", out var locale) || locale != "invariant" ||
                    !node.Metadata.TryGetValue("comparison", out var comparison) || comparison is not ("ordinal" or "ordinal-ignore-case") ||
                    !node.Metadata.TryGetValue("boundary", out var boundary) || boundary is not ("substring" or "whole-word"))
                    throw Invalid("Search requires a literal query (1–256 characters), invariant locale, ordinal/ordinal-ignore-case comparison and substring/whole-word boundary.");
                configuration = JsonSerializer.SerializeToElement(node.Metadata);
            }
            else
            {
                if (node.Metadata.Count != 0) throw Invalid("This user-message node does not accept configuration fields.");
                configuration = JsonSerializer.SerializeToElement(new { });
            }
            nodes.Add(new(node.Id, type.TypeId, type.Version, type.AppId, type.CapabilityId, null, configuration,
                node.Ports.Select(port => new GraphPort(PortId(node.Id, port.Id), port.Id,
                    port.Direction == SpaceLayoutPortDirection.Input ? GraphPortDirection.Input : GraphPortDirection.Output,
                    port.DataType, port.AllowsMultipleConnections)).ToArray()));
        }
        if (nodes.Count(node => node.TypeId == SourceType) != 1 || nodes.Count(node => node.TypeId == OutputType) != 1 ||
            nodes.Select(node => node.NodeId).Distinct().Count() != nodes.Count || nodes.Any(node => node.NodeId == Guid.Empty))
            throw Invalid("A user-message graph requires exactly one Source and one Output with unique node identities.");
        var graph = new GraphDocument(GraphDocument.CurrentSchemaVersion, layout.GraphId, layout.Revision,
            ProfileId, GraphRevisionState.Active, nodes, layout.Edges.Select(edge => new GraphConnection(edge.Id,
                PortId(edge.FromNodeId, edge.FromPortId), PortId(edge.ToNodeId, edge.ToPortId))).ToArray());
        AutomationGraphProjection projection;
        try { projection = new NodeGraphAutomationAdapter(schemas, Bindings()).Compile(graph); }
        catch (Exception cause) when (cause is InvalidDataException or ArgumentException)
        { throw Invalid("The shared graph validator rejected the typed connections or configuration.", cause); }
        // One complete chain or one bounded conditional diamond. The trusted route
        // port binding supplies branch labels; editable edges cannot invent routing.
        // Degree constraints plus shared acyclic validation guarantee every branch
        // rejoins the one merge before the one Output, with no orphan work or bypass.
        var source = nodes.Single(node => node.TypeId == SourceType).NodeId;
        var output = nodes.Single(node => node.TypeId == OutputType).NodeId;
        var routes = nodes.Where(node => node.TypeId is RouteWordsType or RouteJsonBooleanType).ToArray();
        var merges = nodes.Where(node => node.TypeId == MergeType).ToArray();
        if (routes.Length > 1 || merges.Length != routes.Length ||
            layout.Edges.Count != nodes.Count - 1 + routes.Length || nodes.Any(node =>
                layout.Edges.Count(edge => edge.ToNodeId == node.NodeId) != (node.NodeId == source ? 0 : node.TypeId == MergeType ? 2 : 1) ||
                layout.Edges.Count(edge => edge.FromNodeId == node.NodeId) != (node.NodeId == output ? 0 : node.TypeId is RouteWordsType or RouteJsonBooleanType ? 2 : 1)) ||
            routes.Any(route => layout.Edges.Where(edge => edge.FromNodeId == route.NodeId).Select(edge => edge.FromPortId)
                .Order(StringComparer.Ordinal).SequenceEqual(new[] { "false", "true" }) == false))
            throw Invalid("User-message nodes require one complete chain or one complete acyclic two-branch route/merge diamond.");
        var runner = new AutomationGraphRunner([new SpaceUserMessageGraphExecutor("")]);
        if (runner.Validate(projection.Definition).Count != 0) throw Invalid("The shared graph executor rejected this graph.");
        return new(spaceId, spaceRevision, projection, nodes.ToDictionary(node => node.NodeId, node => node.TypeId), output);
    }
    internal static bool IsSupportedJsonPointer(string? pointer)
    {
        if (pointer is null || pointer.Length > 512 || pointer.Length > 0 && pointer[0] != '/') return false;
        for (var index = 0; index < pointer.Length; index++)
            if (pointer[index] == '~' && (++index == pointer.Length || pointer[index] is not ('0' or '1'))) return false;
        return true;
    }
    private static Guid PortId(Guid nodeId, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw Invalid("A stable port key is required.");
        // Persisted node identity plus persisted schema key defines a stable canonical port.
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(nodeId.ToString("D") + ":" + key));
        return new Guid(bytes.AsSpan(0, 16));
    }
    public async Task<SpaceUserMessageGraphResult> ExecuteAsync(ChatMessage input, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Id == Guid.Empty || input.ConversationId == Guid.Empty || input.Role != MessageRole.User || input.Content is null || input.Content.Length > 65536)
            throw Invalid("The graph requires the bounded actual original user message.");
        var run = await new AutomationGraphRunner([new SpaceUserMessageGraphExecutor(input.Content)])
            .RunAsync(_projection.Definition, AutomationGraphRunMode.Real, token).ConfigureAwait(false);
        var output = run.Trace.SingleOrDefault(item => item.NodeId == _output);
        if (!run.Succeeded || output is not { Status: AutomationGraphTraceStatus.Succeeded, Output: not null })
        {
            var failed = run.Trace.FirstOrDefault(item => item.Status == AutomationGraphTraceStatus.Failed);
            // Node failure messages can contain arbitrary runtime/input text in the
            // shared engine. Return an actionable static explanation for our JSON
            // family without copying sample values or private field names into errors.
            throw Invalid(failed is null ? "The shared user-message graph did not produce its declared Output."
                : _types[failed.NodeId] switch
                {
                    ParseJsonType => "Parse JSON requires strict valid JSON, unique decoded property names, no comments/trailing commas, at most 32 nested containers and a typed result within 262144 UTF-8 bytes.",
                    RouteJsonBooleanType => "Route by JSON boolean requires one present true/false JSON value at its exact pointer; strings, numbers, null and missing values are not coerced.",
                    MergeType => "Merge selected text requires exactly one active typed value; zero or multiple active inputs cannot produce a request.",
                    SelectJsonType => "Select JSON requires one present value: check exact field names and existing zero-based array indices without leading zeros.",
                    JsonStringType => "Read JSON string requires an actual string. Use Render JSON for objects, arrays, numbers, booleans or null.",
                    RenderJsonType => "Render JSON could not produce supported request text within the 131072-character output bound.",
                    _ => "The shared user-message graph did not produce its declared Output."
                });
        }
        var portValues = run.Trace.Where(item => item.TypedOutputs is not null).ToDictionary(item => item.NodeId,
            item => (IReadOnlyDictionary<string, AutomationGraphValue>)new ReadOnlyDictionary<string, AutomationGraphValue>(
                item.TypedOutputs!.ToDictionary(pair => pair.Key, pair => pair.Value with { JsonValue = pair.Value.JsonValue?.Clone() }, StringComparer.Ordinal)));
        var values = portValues.Where(item => item.Value.ContainsKey("out")).ToDictionary(item => item.Key, item => item.Value["out"]);
        var trace = run.Trace.Select(item =>
        {
            var value = portValues.GetValueOrDefault(item.NodeId)?.Values.SingleOrDefault();
            return new SpaceUserMessageGraphTrace(item.NodeId, _types[item.NodeId], item.Status.ToString(),
                item.Status == AutomationGraphTraceStatus.Skipped ? 0 : item.Inputs?.Values.SingleOrDefault() is { } before ? TextElements(before) : TextElements(input.Content),
                TextElements(item.Output ?? ""), value?.DataType ?? "string", value?.JsonValue is { } json ? Digest(json.GetRawText()) : null,
                HasFlag(value, "moreResultsOmitted"), HasFlag(value, "hasUncertainty")) { SelectedBranch = item.Branch };
        }).ToArray();
        return new(SpaceId, SpaceRevision, GraphId, GraphRevision, input.Id, output.Output, Digest(input.Content),
            Digest(output.Output), trace.Where(item => item.TypeId == TruncateType).Sum(item => Math.Max(0, item.InputTextElements - item.OutputTextElements)), Array.AsReadOnly(trace))
        { TypedNodeOutputs = new ReadOnlyDictionary<Guid, AutomationGraphValue>(values),
            TypedNodePortOutputs = new ReadOnlyDictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>(portValues) };
    }
    private static bool HasFlag(AutomationGraphValue? value, string property) => value?.JsonValue is { ValueKind: JsonValueKind.Object } json &&
        json.TryGetProperty(property, out var flag) && flag.ValueKind == JsonValueKind.True;
    internal static int TextElements(string text) => StringInfo.ParseCombiningCharacters(text).Length;
    private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static SpaceRequestPolicyBlockedException Invalid(string message, Exception? cause = null) =>
        new("SPACE_USER_MESSAGE_GRAPH_INVALID", cause is null ? message : message + " " + cause.Message);
}

public sealed record SpaceUserMessageGraphTrace(Guid NodeId, string TypeId, string State, int InputTextElements, int OutputTextElements,
    string OutputDataType = "string", string? OutputSha256 = null, bool MoreResultsOmitted = false, bool HasUncertainty = false)
{
    public string? SelectedBranch { get; init; }
}
public sealed record SpaceUserMessageGraphResult(Guid SpaceId, long SpaceRevision, Guid GraphId, long GraphRevision,
    Guid UserMessageId, string DerivedText, string InputSha256, string OutputSha256, int OmittedTextElements,
    IReadOnlyList<SpaceUserMessageGraphTrace> Trace)
{
    /// <summary>Inspectable pure derived values for explicit previews. These can contain
    /// user data and are deliberately excluded from the content-free execution trace.</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>> TypedNodePortOutputs { get; init; } =
        new ReadOnlyDictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>(new Dictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>());
    public IReadOnlyDictionary<Guid, AutomationGraphValue> TypedNodeOutputs { get; init; } =
        new ReadOnlyDictionary<Guid, AutomationGraphValue>(new Dictionary<Guid, AutomationGraphValue>());
}
