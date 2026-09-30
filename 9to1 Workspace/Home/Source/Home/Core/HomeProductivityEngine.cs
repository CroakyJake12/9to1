using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HavenOS.Home.Core;

public sealed record HomeProductivityObjectSchema(string ObjectType, int SchemaVersion, bool IsRequired,
    JsonElement Schema, IReadOnlySet<string> Capabilities);
[method: JsonConstructor]
public sealed record HomeProductivityObject(Guid ObjectId, string ObjectType, int SchemaVersion,
    JsonElement Content, JsonElement Formatting, JsonElement Accessibility, IReadOnlyList<string> AssetReferences,
    JsonElement Layout)
{
    [JsonExtensionData] public IDictionary<string, JsonElement>? Extensions { get; init; }
    public HomeProductivityObject(Guid objectId, string objectType, int schemaVersion, JsonElement content,
        JsonElement formatting, JsonElement accessibility, IReadOnlyList<string> assetReferences, JsonElement layout,
        IDictionary<string, JsonElement>? extensions) : this(objectId, objectType, schemaVersion, content, formatting, accessibility, assetReferences, layout)
    { Extensions = extensions; }
}
public sealed record HomeProductivityStyle(string StyleId, int Version, string DisplayName, string Scope,
    JsonElement Properties, JsonElement? AppExtensions = null);
public sealed record HomeProductivityObjectBundle(int FormatVersion, IReadOnlyList<HomeProductivityObject> Objects,
    IReadOnlyList<HomeProductivityStyle> Styles, IReadOnlyList<string> AssetReferences, JsonElement Accessibility,
    JsonElement PortableFallback);
public sealed record HomeProductivityCompatibility(string AppId, string AppVersion, int EngineVersion,
    IReadOnlyList<string> UnsupportedRequiredTypes, bool Compatible, string State);
/// <summary>Preserves the owner's actual revision type; a GUID revision is never hashed into a sequence.</summary>
public sealed record HomeProductivityArtifactRevision(long? Sequence = null, Guid? VersionId = null)
{
    public bool IsValid => (Sequence is >= 0 && VersionId is null) || (Sequence is null && VersionId is { } id && id != Guid.Empty);
}
public sealed record HomeProductivityContext(string AppId, string ArtifactId, long Revision,
    IReadOnlyList<Guid> SelectedObjectIds, IReadOnlySet<string> SupportedObjectTypes)
{
    public HomeProductivityArtifactRevision? ArtifactRevision { get; init; }
    public HomeProductivityArtifactRevision CanonicalRevision => ArtifactRevision ?? new(Revision);
}
public sealed record HomeProductivityAction(string ActionId, int Version, string ObjectType,
    IReadOnlyList<Guid> ObjectIds, JsonElement Arguments, long ExpectedRevision)
{
    public HomeProductivityArtifactRevision? ExpectedArtifactRevision { get; init; }
    public HomeProductivityArtifactRevision CanonicalExpectedRevision => ExpectedArtifactRevision ?? new(ExpectedRevision);
}
public enum HomeProductivityArtifactOutcome { NotExecuted, Committed, Rejected, NeedsRecovery }
public sealed record HomeProductivityActionResult(bool Succeeded, string Code, string Message, long Revision,
    IReadOnlyList<Guid> AffectedObjectIds)
{
    public HomeProductivityArtifactRevision? ArtifactRevision { get; init; }
    public HomeProductivityArtifactOutcome Outcome { get; init; } = HomeProductivityArtifactOutcome.NotExecuted;
}

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
    ValueTask<HomeProductivityActionResult> ApplyActionAsync(HomeProductivityContext context, HomeProductivityAction action, CancellationToken cancellationToken = default);
    HomeProductivityObject CreateObject(string objectType, Guid objectId, JsonElement content);
    HomeProductivityObjectRenderResult RenderObject(HomeProductivityObject value);
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
    private readonly Dictionary<string, IHomeProductivityObjectHandler> _handlers = new(StringComparer.Ordinal);
    private readonly IHomeProductivityArtifactActionProvider[] _artifactActions;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public HomeProductivityEngine(IEnumerable<HomeProductivityObjectSchema>? schemas = null,
        IEnumerable<HomeProductivityStyle>? styles = null,
        IEnumerable<IHomeProductivityObjectHandler>? handlers = null,
        IEnumerable<IHomeProductivityArtifactActionProvider>? artifactActions = null)
    {
        foreach (var type in BuiltInTypes)
            _schemas[type] = new(type, 1, true, JsonDocument.Parse("{}").RootElement.Clone(), FrozenSet<string>.Empty);
        foreach (var schema in schemas ?? []) RegisterSchema(schema);
        foreach (var style in styles ?? []) RegisterStyle(style);
        _artifactActions = (artifactActions ?? []).ToArray();
        RegisterObjectHandler(new HomeParagraphObjectHandler());
        foreach (var handler in handlers ?? []) RegisterObjectHandler(handler);
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
        _schemas[schema.ObjectType] = schema with { Schema = schema.Schema.Clone(),
            Capabilities = schema.Capabilities.ToFrozenSet(StringComparer.Ordinal) };
    }

    public void RegisterStyle(HomeProductivityStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (string.IsNullOrWhiteSpace(style.StyleId) || style.Version < 1 || string.IsNullOrWhiteSpace(style.Scope))
            throw new ArgumentException("Styles need a stable ID, positive version and scope.", nameof(style));
        _styles[style.StyleId] = style with { Properties = style.Properties.Clone(), AppExtensions = style.AppExtensions?.Clone() };
    }

    public HomeProductivityObjectBundle SerializeSelection(HomeProductivityContext context,
        IReadOnlyList<HomeProductivityObject> objects)
    {
        ArgumentNullException.ThrowIfNull(context);
        var selected = context.SelectedObjectIds.ToHashSet();
        var copy = objects.Where(item => selected.Contains(item.ObjectId)).Select(SnapshotObject).ToArray();
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
            if (schema is null || item.SchemaVersion > schema.SchemaVersion || !target.SupportedObjectTypes.Contains(item.ObjectType) ||
                !_handlers.TryGetValue(item.ObjectType, out var handler) || handler.Schema.SchemaVersion != item.SchemaVersion)
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
        return Array.AsReadOnly(objects.Where(item => selected.Contains(item.ObjectId)).Select(SnapshotObject).ToArray());
    }

    public HomeProductivityActionResult ApplyAction(HomeProductivityContext context, HomeProductivityAction action)
    {
        if (action.Version != 1) return new(false, "ActionVersionUnsupported", "The productivity action version is unsupported.", context.Revision, []) { ArtifactRevision = context.CanonicalRevision };
        if (!context.CanonicalRevision.IsValid || !action.CanonicalExpectedRevision.IsValid || action.CanonicalExpectedRevision != context.CanonicalRevision) return new(false, "RevisionConflict", "The artifact changed before this action was applied.", context.Revision, []) { ArtifactRevision = context.CanonicalRevision };
        if (!context.SupportedObjectTypes.Contains(action.ObjectType)) return new(false, "SurfaceObjectTypeUnsupported", "This surface does not support the requested shared object family.", context.Revision, []) { ArtifactRevision = context.CanonicalRevision };
        if (!_schemas.ContainsKey(action.ObjectType)) return new(false, "ObjectTypeUnsupported", "The shared object type is unavailable.", context.Revision, []) { ArtifactRevision = context.CanonicalRevision };
        if (action.ObjectIds.Count == 0 || action.ObjectIds.Distinct().Count() != action.ObjectIds.Count || action.ObjectIds.Any(id => id == Guid.Empty || !context.SelectedObjectIds.Contains(id)))
            return new(false, "SelectionMismatch", "The action must target objects in the current typed selection.", context.Revision, []) { ArtifactRevision = context.CanonicalRevision };
        return new(false, "ArtifactExecutionRequired", "A current authorised artifact transaction is required to apply this shared action.", context.Revision, []) { ArtifactRevision = context.CanonicalRevision };
    }

    public void RegisterObjectHandler(IHomeProductivityObjectHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (_handlers.ContainsKey(handler.Schema.ObjectType)) throw new InvalidOperationException("Shared object types require one canonical semantic handler.");
        RegisterSchema(handler.Schema); _handlers.Add(handler.Schema.ObjectType, handler);
    }
    public HomeProductivityObject CreateObject(string objectType, Guid objectId, JsonElement content) =>
        _handlers.TryGetValue(objectType, out var handler) ? handler.Create(objectId, content) :
            throw new NotSupportedException("The shared object creation provider is unavailable.");
    public HomeProductivityObjectRenderResult RenderObject(HomeProductivityObject value) =>
        _handlers.TryGetValue(value.ObjectType, out var handler) ? handler.Render(value) :
            throw new NotSupportedException("The shared object rendering provider is unavailable; preserve the object and offer its supported embed path.");
    public async ValueTask<HomeProductivityActionResult> ApplyActionAsync(HomeProductivityContext context,
        HomeProductivityAction action, CancellationToken cancellationToken = default)
    {
        // Capture checked targets before awaiting an owner; caller-owned collections cannot change the dispatch.
        context = context with { SelectedObjectIds = Array.AsReadOnly(context.SelectedObjectIds.ToArray()),
            SupportedObjectTypes = context.SupportedObjectTypes.ToFrozenSet(StringComparer.Ordinal) };
        action = action with { ObjectIds = Array.AsReadOnly(action.ObjectIds.ToArray()), Arguments = action.Arguments.Clone() };
        var validation = ApplyAction(context, action);
        if (validation.Code != "ArtifactExecutionRequired") return validation;
        if (!_handlers.TryGetValue(action.ObjectType, out var handler))
            return new(false, "ObjectActionProviderUnavailable", "The shared object action provider is unavailable.", context.Revision, []);
        var descriptors = handler.Actions.Where(descriptor => descriptor.ActionId == action.ActionId && descriptor.Version == action.Version).ToArray();
        if (descriptors.Length != 1 || !NineToOne.Cui.AI.ActionJsonSchemaValidator.Validate(descriptors[0].ArgumentSchema.GetRawText(), action.Arguments, out _))
            return new(false, "ObjectActionInvalid", "The shared typed action or arguments are unsupported.", context.Revision, []);
        var owners = _artifactActions.Where(provider => provider.AppId == context.AppId).ToArray();
        if (owners.Length != 1) return new(false, "ArtifactExecutorUnavailable", "The canonical artifact owner is unavailable.", context.Revision, []);
        var result = await owners[0].ApplyAsync(context, action, source =>
        {
            if (source.ObjectType != action.ObjectType || !action.ObjectIds.Contains(source.ObjectId))
                throw new InvalidDataException("The artifact owner supplied an object outside the checked action targets.");
            var transformed = handler.Transform(source, action);
            if (transformed.ObjectId != source.ObjectId || transformed.ObjectType != source.ObjectType || transformed.SchemaVersion != source.SchemaVersion)
                throw new InvalidDataException("The shared action changed a stable object identity or schema without an explicit conversion.");
            return transformed;
        }, cancellationToken).ConfigureAwait(false);
        var observedTargets = result.AffectedObjectIds?.ToArray() ?? [];
        var validCommit = result.Outcome == HomeProductivityArtifactOutcome.Committed &&
            result.ArtifactRevision is { IsValid: true } observed &&
            (context.CanonicalRevision.VersionId is not null) == (observed.VersionId is not null) &&
            observedTargets.Length == action.ObjectIds.Count && observedTargets.Distinct().Count() == observedTargets.Length &&
            observedTargets.ToHashSet().SetEquals(action.ObjectIds);
        if ((result.Succeeded && !validCommit) || (!result.Succeeded && result.Outcome == HomeProductivityArtifactOutcome.Committed))
            return result with { Succeeded = false, Code = "ArtifactOutcomeNeedsRecovery",
                Message = "The owner reported an inconsistent transaction outcome. Refresh the canonical artifact and its observed revision before further edits.",
                AffectedObjectIds = Array.AsReadOnly(observedTargets), Outcome = HomeProductivityArtifactOutcome.NeedsRecovery };
        result = result with { AffectedObjectIds = Array.AsReadOnly(observedTargets) };
        return result;
    }

    public IReadOnlyList<HomeProductivityObject> Paste(HomeProductivityObjectBundle bundle, HomeProductivityContext target)
    {
        if (!CanPaste(bundle, target, out var code))
            throw new InvalidDataException($"Productivity bundle cannot be pasted: {code}.");
        return Array.AsReadOnly(bundle.Objects.Select(item => SnapshotObject(item) with { ObjectId = Guid.NewGuid() }).ToArray());
    }

    public HomeProductivityObject Convert(HomeProductivityObject source, string targetType, JsonElement options)
    {
        ArgumentNullException.ThrowIfNull(source);
        source = SnapshotObject(source);
        if (!_schemas.TryGetValue(source.ObjectType, out var from) || !_schemas.TryGetValue(targetType, out var to))
            throw new InvalidDataException("Source or target shared object schema is unavailable.");
        if (source.SchemaVersion > from.SchemaVersion) throw new InvalidDataException("Source object schema version is unsupported.");
        if (source.ObjectType == targetType) return source with { SchemaVersion = to.SchemaVersion };
        if (targetType == "artifact.reference")
            return source with { ObjectType = targetType, SchemaVersion = to.SchemaVersion, Content = JsonDocument.Parse(JsonSerializer.Serialize(new { source.ObjectId, source.ObjectType, source.Content }, _json)).RootElement.Clone() };
        throw new InvalidDataException("This object conversion is not registered; content cannot be flattened implicitly.");
    }

    public HomeProductivityCompatibility GetCompatibility(string appId, string appVersion, IEnumerable<string> requiredTypes)
    {
        var unsupported = requiredTypes.Where(type => !_handlers.ContainsKey(type)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
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

    private static HomeProductivityObject SnapshotObject(HomeProductivityObject value) => value with
    {
        Content = value.Content.Clone(), Formatting = value.Formatting.Clone(), Accessibility = value.Accessibility.Clone(),
        Layout = value.Layout.Clone(), AssetReferences = Array.AsReadOnly(value.AssetReferences.ToArray()),
        Extensions = value.Extensions?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal)
    };

    public string SerializeBundle(HomeProductivityObjectBundle bundle) => JsonSerializer.Serialize(bundle, _json);
}

public sealed class HomeProductivityEngineService : IHomeCoreService
{
    public HomeProductivityEngineService(HomeProductivityEngine? engine = null) => Engine = engine ?? new HomeProductivityEngine();
    public HomeProductivityEngine Engine { get; }
    public HomeServiceDescriptor Descriptor { get; } = new("productivity.engine", HomeCoreServiceCatalog.CurrentContractVersion,
        HomeServiceLifecycleState.Stopped, false, "Shared Productivity Engine has not started.");
    public IReadOnlyList<string> Dependencies { get; } = ["home.state", "permissions.trust"];
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Engine.GetCompatibility("home", "1", ["text.paragraph"]).Compatible)
            throw new InvalidOperationException("The canonical shared object handler is unavailable.");
        // Other required families remain individually unavailable until their real handlers are registered.
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
