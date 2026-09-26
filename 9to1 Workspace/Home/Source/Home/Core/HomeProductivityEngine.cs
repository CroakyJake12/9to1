using System.Text.Json;
using System.Text.Json.Serialization;

namespace HavenOS.Home.Core;

public sealed record HomeProductivityObjectSchema(string ObjectType, int SchemaVersion, bool IsRequired,
    JsonElement Schema, IReadOnlySet<string> Capabilities);
public sealed record HomeProductivityObject(Guid ObjectId, string ObjectType, int SchemaVersion,
    JsonElement Content, JsonElement Formatting, JsonElement Accessibility, IReadOnlyList<string> AssetReferences,
    JsonElement Layout, [property: JsonExtensionData] IDictionary<string, JsonElement>? Extensions = null);
public sealed record HomeProductivityStyle(string StyleId, int Version, string DisplayName, string Scope,
    JsonElement Properties, JsonElement? AppExtensions = null);
public sealed record HomeProductivityObjectBundle(int FormatVersion, IReadOnlyList<HomeProductivityObject> Objects,
    IReadOnlyList<HomeProductivityStyle> Styles, IReadOnlyList<string> AssetReferences, JsonElement Accessibility,
    JsonElement PortableFallback);
public sealed record HomeProductivityCompatibility(string AppId, string AppVersion, int EngineVersion,
    IReadOnlyList<string> UnsupportedRequiredTypes, bool Compatible, string State);
public sealed record HomeProductivityContext(string AppId, string ArtifactId, long Revision,
    IReadOnlyList<Guid> SelectedObjectIds, IReadOnlySet<string> SupportedObjectTypes);
public sealed record HomeProductivityAction(string ActionId, int Version, string ObjectType,
    IReadOnlyList<Guid> ObjectIds, JsonElement Arguments, long ExpectedRevision);
public sealed record HomeProductivityActionResult(bool Succeeded, string Code, string Message, long Revision,
    IReadOnlyList<Guid> AffectedObjectIds);

public interface IHomeProductivityEngine
{
    int EngineVersion { get; }
    IReadOnlyList<HomeProductivityObjectSchema> ListObjectTypes();
    HomeProductivityObjectSchema? GetObjectSchema(string objectType);
    IReadOnlyList<HomeProductivityStyle> ListStyles(string? scope = null);
    HomeProductivityStyle? GetStyle(string styleId);
    HomeProductivityObjectBundle SerializeSelection(HomeProductivityContext context, IReadOnlyList<HomeProductivityObject> objects);
    bool CanPaste(HomeProductivityObjectBundle bundle, HomeProductivityContext target, out string code);
    HomeProductivityActionResult ApplyAction(HomeProductivityContext context, HomeProductivityAction action);
    IReadOnlyList<HomeProductivityObject> GetSelection(HomeProductivityContext context, IReadOnlyList<HomeProductivityObject> objects);
    IReadOnlyList<HomeProductivityObject> Paste(HomeProductivityObjectBundle bundle, HomeProductivityContext target);
    HomeProductivityObject Convert(HomeProductivityObject source, string targetType, JsonElement options);
    HomeProductivityCompatibility GetCompatibility(string appId, string appVersion, IEnumerable<string> requiredTypes);
}

/// <summary>Home-authoritative schemas and transferable data for app-neutral productivity objects.</summary>
public sealed class HomeProductivityEngine : IHomeProductivityEngine
{
    public const int CurrentEngineVersion = 1;
    private static readonly string[] BuiltInTypes = ["text.paragraph", "text.heading", "text.list", "text.checklist",
        "code.block", "math.equation", "table", "media.image", "media.object", "drawing.ink", "graph", "artifact.reference"];
    private readonly Dictionary<string, HomeProductivityObjectSchema> _schemas = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HomeProductivityStyle> _styles = new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public HomeProductivityEngine(IEnumerable<HomeProductivityObjectSchema>? schemas = null,
        IEnumerable<HomeProductivityStyle>? styles = null)
    {
        foreach (var type in BuiltInTypes)
            _schemas[type] = new(type, 1, true, JsonDocument.Parse("{}").RootElement.Clone(), new HashSet<string>(StringComparer.Ordinal));
        foreach (var schema in schemas ?? []) RegisterSchema(schema);
        foreach (var style in styles ?? []) RegisterStyle(style);
    }

    public int EngineVersion => CurrentEngineVersion;
    public IReadOnlyList<HomeProductivityObjectSchema> ListObjectTypes() => _schemas.Values.OrderBy(item => item.ObjectType, StringComparer.Ordinal).ToArray();
    public HomeProductivityObjectSchema? GetObjectSchema(string objectType) => _schemas.GetValueOrDefault(objectType);
    public IReadOnlyList<HomeProductivityStyle> ListStyles(string? scope = null) => _styles.Values
        .Where(style => scope is null || style.Scope == scope || style.Scope == "shared")
        .OrderBy(style => style.StyleId, StringComparer.Ordinal).ToArray();
    public HomeProductivityStyle? GetStyle(string styleId) => _styles.GetValueOrDefault(styleId);

    public void RegisterSchema(HomeProductivityObjectSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (string.IsNullOrWhiteSpace(schema.ObjectType) || schema.SchemaVersion < 1)
            throw new ArgumentException("Object schemas need a stable type ID and positive version.", nameof(schema));
        if (_schemas.TryGetValue(schema.ObjectType, out var prior) && schema.SchemaVersion < prior.SchemaVersion)
            throw new InvalidOperationException("Object schema versions cannot move backwards.");
        _schemas[schema.ObjectType] = schema;
    }

    public void RegisterStyle(HomeProductivityStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (string.IsNullOrWhiteSpace(style.StyleId) || style.Version < 1 || string.IsNullOrWhiteSpace(style.Scope))
            throw new ArgumentException("Styles need a stable ID, positive version and scope.", nameof(style));
        _styles[style.StyleId] = style;
    }

    public HomeProductivityObjectBundle SerializeSelection(HomeProductivityContext context,
        IReadOnlyList<HomeProductivityObject> objects)
    {
        ArgumentNullException.ThrowIfNull(context);
        var selected = context.SelectedObjectIds.ToHashSet();
        var copy = objects.Where(item => selected.Contains(item.ObjectId)).ToArray();
        if (copy.Length != selected.Count) throw new InvalidDataException("A selected shared object is missing from the artifact snapshot.");
        var assetRefs = copy.SelectMany(item => item.AssetReferences).Distinct(StringComparer.Ordinal).ToArray();
        return new(1, copy, [], assetRefs, JsonDocument.Parse("{}").RootElement.Clone(),
            JsonDocument.Parse(JsonSerializer.Serialize(new { text = string.Join("\n", copy.Select(item => item.Content.ToString())) }, _json)).RootElement.Clone());
    }

    public bool CanPaste(HomeProductivityObjectBundle bundle, HomeProductivityContext target, out string code)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(target);
        if (bundle.FormatVersion != 1) { code = "BundleVersionUnsupported"; return false; }
        foreach (var item in bundle.Objects)
        {
            var schema = GetObjectSchema(item.ObjectType);
            if (schema is null || item.SchemaVersion > schema.SchemaVersion)
            {
                code = target.SupportedObjectTypes.Contains("artifact.reference") ? "OfferArtifactEmbed" : "RequiredSchemaUnsupported";
                return false;
            }
        }
        code = "CanPaste";
        return true;
    }

    public IReadOnlyList<HomeProductivityObject> GetSelection(HomeProductivityContext context,
        IReadOnlyList<HomeProductivityObject> objects)
    {
        var selected = context.SelectedObjectIds.ToHashSet();
        return objects.Where(item => selected.Contains(item.ObjectId)).ToArray();
    }

    public HomeProductivityActionResult ApplyAction(HomeProductivityContext context, HomeProductivityAction action)
    {
        if (action.Version != 1) return new(false, "ActionVersionUnsupported", "The productivity action version is unsupported.", context.Revision, []);
        if (action.ExpectedRevision != context.Revision) return new(false, "RevisionConflict", "The artifact changed before this action was applied.", context.Revision, []);
        if (!_schemas.ContainsKey(action.ObjectType)) return new(false, "ObjectTypeUnsupported", "The shared object type is unavailable.", context.Revision, []);
        if (action.ObjectIds.Count == 0 || action.ObjectIds.Any(id => !context.SelectedObjectIds.Contains(id)))
            return new(false, "SelectionMismatch", "The action must target objects in the current typed selection.", context.Revision, []);
        return new(true, "Accepted", "The typed action passed Home engine validation for the owning app to apply.", context.Revision + 1, action.ObjectIds);
    }

    public IReadOnlyList<HomeProductivityObject> Paste(HomeProductivityObjectBundle bundle, HomeProductivityContext target)
    {
        if (!CanPaste(bundle, target, out var code))
            throw new InvalidDataException($"Productivity bundle cannot be pasted: {code}.");
        return bundle.Objects.Select(item => item with { ObjectId = Guid.NewGuid() }).ToArray();
    }

    public HomeProductivityObject Convert(HomeProductivityObject source, string targetType, JsonElement options)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!_schemas.TryGetValue(source.ObjectType, out var from) || !_schemas.TryGetValue(targetType, out var to))
            throw new InvalidDataException("Source or target shared object schema is unavailable.");
        if (source.SchemaVersion > from.SchemaVersion) throw new InvalidDataException("Source object schema version is unsupported.");
        if (source.ObjectType == targetType) return source with { SchemaVersion = to.SchemaVersion };
        if (targetType == "artifact.reference")
            return source with { ObjectType = targetType, SchemaVersion = to.SchemaVersion, Content = JsonDocument.Parse(JsonSerializer.Serialize(new { source.ObjectId, source.ObjectType, source.Content }, _json)).RootElement.Clone() };
        throw new InvalidOperationException("This object conversion is not registered; content cannot be flattened implicitly.");
    }

    public HomeProductivityCompatibility GetCompatibility(string appId, string appVersion, IEnumerable<string> requiredTypes)
    {
        var unsupported = requiredTypes.Where(type => !_schemas.ContainsKey(type)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new(appId, appVersion, EngineVersion, unsupported, unsupported.Length == 0,
            unsupported.Length == 0 ? "Compatible" : "NeedsAttention");
    }

    public HomeProductivityObjectBundle ParseBundle(string json)
    {
        var bundle = JsonSerializer.Deserialize<HomeProductivityObjectBundle>(json, _json)
            ?? throw new InvalidDataException("Productivity object bundle is empty.");
        if (bundle.FormatVersion != 1) throw new InvalidDataException("Productivity object bundle version is unsupported.");
        foreach (var item in bundle.Objects)
            if (!_schemas.TryGetValue(item.ObjectType, out var schema) || item.SchemaVersion > schema.SchemaVersion)
                throw new InvalidDataException($"Required productivity object schema '{item.ObjectType}' version {item.SchemaVersion} is unsupported.");
        return bundle;
    }

    public string SerializeBundle(HomeProductivityObjectBundle bundle) => JsonSerializer.Serialize(bundle, _json);
}

public sealed class HomeProductivityEngineService : IHomeCoreService
{
    public HomeProductivityEngineService(HomeProductivityEngine? engine = null) => Engine = engine ?? new HomeProductivityEngine();
    public HomeProductivityEngine Engine { get; }
    public HomeServiceDescriptor Descriptor { get; } = new("productivity.engine", HomeCoreServiceCatalog.CurrentContractVersion,
        HomeServiceLifecycleState.Stopped, false, "Shared Productivity Engine has not started.");
    public IReadOnlyList<string> Dependencies { get; } = ["home.state"];
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
