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
            [new("in", GraphPortDirection.Input, MatchesDataType), new("out", GraphPortDirection.Output, "string")])
    ];
    public static GraphCapabilityProfile Profile() => new(ProfileId,
        new HashSet<string>([SourceType, TruncateType, OutputType, ExtractNumbersType, NumbersToTextType, SearchWordsType, MatchesToTextType], StringComparer.Ordinal),
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
            _ => throw new ArgumentException("This node has no authoring implementation.", nameof(typeId))
        })
        {
            TypeId = type.TypeId, SchemaVersion = type.Version,
            Ports = type.Ports.Select(port => new SpaceLayoutPort(port.Key, port.DataType == "string" ? "Text" : port.DataType == NumbersDataType ? "Numbers" : "Matches",
                port.Direction == GraphPortDirection.Input ? SpaceLayoutPortDirection.Input : SpaceLayoutPortDirection.Output,
                port.DataType, port.Multiple)).ToArray(),
            Metadata = typeId switch
            {
                TruncateType => new Dictionary<string, string>(StringComparer.Ordinal) { ["maximumTextElements"] = "1000" },
                ExtractNumbersType => new Dictionary<string, string>(StringComparer.Ordinal) { ["locale"] = "invariant" },
                SearchWordsType => new Dictionary<string, string>(StringComparer.Ordinal)
                    { ["query"] = "word", ["locale"] = "invariant", ["comparison"] = "ordinal", ["boundary"] = "whole-word" },
                _ => new Dictionary<string, string>(StringComparer.Ordinal)
            }
        };
    }
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
            }));

    public static SpaceUserMessageGraph Compile(Guid spaceId, long spaceRevision, SpaceLayoutDocument layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (spaceId == Guid.Empty || spaceRevision < 1 || layout.GraphId == Guid.Empty || layout.Revision < 1 ||
            layout.SchemaVersion != SpaceLayoutDocument.CurrentSchemaVersion || layout.Nodes is null || layout.Edges is null ||
            layout.Nodes.Count is < 2 or > SpaceLayoutDocument.MaximumNodes || layout.Edges.Count > SpaceLayoutDocument.MaximumEdges ||
            layout.Edges.Any(edge => edge is null || edge.Metadata is null || edge.Metadata.Count != 0 || !string.IsNullOrEmpty(edge.Branch)) ||
            layout.Nodes.Any(node => node is null || node.Ports is null || node.Ports.Count > 2 || node.Metadata is null || node.Metadata.Count > 4))
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
            else if (node.TypeId == SearchWordsType)
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
        // This first executable family is deliberately a single fully connected chain. No
        // discarded branch, orphan, cycle or disconnected output can look like success.
        var source = nodes.Single(node => node.TypeId == SourceType).NodeId;
        var output = nodes.Single(node => node.TypeId == OutputType).NodeId;
        if (layout.Edges.Any(edge => !string.IsNullOrEmpty(edge.Branch) || edge.Metadata is null || edge.Metadata.Count != 0) ||
            layout.Edges.Count != nodes.Count - 1 || nodes.Any(node =>
                layout.Edges.Count(edge => edge.ToNodeId == node.NodeId) != (node.NodeId == source ? 0 : 1) ||
                layout.Edges.Count(edge => edge.FromNodeId == node.NodeId) != (node.NodeId == output ? 0 : 1)))
            throw Invalid("User-message nodes must form one complete Source-to-Output chain.");
        var runner = new AutomationGraphRunner([new SpaceUserMessageGraphExecutor("")]);
        if (runner.Validate(projection.Definition).Count != 0) throw Invalid("The shared graph executor rejected this graph.");
        return new(spaceId, spaceRevision, projection, nodes.ToDictionary(node => node.NodeId, node => node.TypeId), output);
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
            throw Invalid("The shared user-message graph did not produce its declared Output.");
        var values = run.Trace.Where(item => item.TypedOutputs?.ContainsKey("out") == true)
            .ToDictionary(item => item.NodeId, item => item.TypedOutputs!["out"] with { JsonValue = item.TypedOutputs["out"].JsonValue?.Clone() });
        var trace = run.Trace.Select(item => new SpaceUserMessageGraphTrace(item.NodeId, _types[item.NodeId],
            item.Status.ToString(), item.Inputs?.Values.SingleOrDefault() is { } before ? TextElements(before) : TextElements(input.Content),
            TextElements(item.Output ?? ""), values.GetValueOrDefault(item.NodeId)?.DataType ?? "string",
            values.GetValueOrDefault(item.NodeId)?.JsonValue is { } value ? Digest(value.GetRawText()) : null,
            HasFlag(values.GetValueOrDefault(item.NodeId), "moreResultsOmitted"), HasFlag(values.GetValueOrDefault(item.NodeId), "hasUncertainty"))).ToArray();
        return new(SpaceId, SpaceRevision, GraphId, GraphRevision, input.Id, output.Output, Digest(input.Content),
            Digest(output.Output), trace.Where(item => item.TypeId == TruncateType).Sum(item => Math.Max(0, item.InputTextElements - item.OutputTextElements)), Array.AsReadOnly(trace))
        { TypedNodeOutputs = new ReadOnlyDictionary<Guid, AutomationGraphValue>(values) };
    }
    private static bool HasFlag(AutomationGraphValue? value, string property) => value?.JsonValue is { ValueKind: JsonValueKind.Object } json &&
        json.TryGetProperty(property, out var flag) && flag.ValueKind == JsonValueKind.True;
    internal static int TextElements(string text) => StringInfo.ParseCombiningCharacters(text).Length;
    private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static SpaceRequestPolicyBlockedException Invalid(string message, Exception? cause = null) =>
        new("SPACE_USER_MESSAGE_GRAPH_INVALID", cause is null ? message : message + " " + cause.Message);
}

public sealed record SpaceUserMessageGraphTrace(Guid NodeId, string TypeId, string State, int InputTextElements, int OutputTextElements,
    string OutputDataType = "string", string? OutputSha256 = null, bool MoreResultsOmitted = false, bool HasUncertainty = false);
public sealed record SpaceUserMessageGraphResult(Guid SpaceId, long SpaceRevision, Guid GraphId, long GraphRevision,
    Guid UserMessageId, string DerivedText, string InputSha256, string OutputSha256, int OmittedTextElements,
    IReadOnlyList<SpaceUserMessageGraphTrace> Trace)
{
    /// <summary>Inspectable pure derived values for explicit previews. These can contain
    /// user data and are deliberately excluded from the content-free execution trace.</summary>
    public IReadOnlyDictionary<Guid, AutomationGraphValue> TypedNodeOutputs { get; init; } =
        new ReadOnlyDictionary<Guid, AutomationGraphValue>(new Dictionary<Guid, AutomationGraphValue>());
}
