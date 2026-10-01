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
    JsonElement Properties, JsonElement? AppExtensions = null)
{
    public IReadOnlyList<string> BasedOnStyleIds { get; init; } = [];
    public HomeProductivityStyleSource? Source { get; init; }
}
public sealed record HomeProductivityStyleSource(string AppId, string ArtifactId, Guid FileId, Guid FilesRevisionId, string OwningRevision);
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
    HomeProductivityStyle? GetStyle(string styleId, string scope);
    HomeProductivityObjectBundle SerializeSelection(HomeProductivityContext context, IReadOnlyList<HomeProductivityObject> objects);
    bool CanPaste(HomeProductivityObjectBundle bundle, HomeProductivityContext target, out string code);
    HomeProductivityActionResult ApplyAction(HomeProductivityContext context, HomeProductivityAction action);
    ValueTask<HomeProductivityActionResult> ApplyActionAsync(HomeProductivityContext context, HomeProductivityAction action, CancellationToken cancellationToken = default);
    ValueTask<HomeProductivityActionResult> InsertObjectsAsync(HomeProductivityContext context,
        IReadOnlyList<HomeProductivityObject> objects, string operationId, CancellationToken cancellationToken = default);
    HomeProductivityObject CreateObject(string objectType, Guid objectId, JsonElement content);
    HomeProductivityObject CreateObject(string objectType, Guid objectId, JsonElement content, int schemaVersion);
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
    private readonly Dictionary<(string Scope, string Id), HomeProductivityStyle> _styles = new();
    private readonly Dictionary<(string Type, int Version), IHomeProductivityObjectHandler> _handlers = new();
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
        RegisterObjectHandler(new HomeVectorShapeObjectHandler());
        RegisterObjectHandler(new HomeEquationObjectHandler());
        foreach (var type in new[] { "text.paragraph", "text.heading", "text.list", "text.checklist", "code.block", "table" })
            RegisterObjectHandler(new HomeNotesObjectHandler(type));
        foreach (var handler in handlers ?? []) RegisterObjectHandler(handler);
    }

    public int EngineVersion => CurrentEngineVersion;
    public IReadOnlyList<HomeProductivityObjectSchema> ListObjectTypes() => _schemas.Values.OrderBy(item => item.ObjectType, StringComparer.Ordinal).ToArray();
    public HomeProductivityObjectSchema? GetObjectSchema(string objectType) => _schemas.GetValueOrDefault(objectType);
    public IReadOnlyList<HomeProductivityStyle> ListStyles(string? scope = null) => _styles.Values
        .Where(style => scope is null || style.Scope == scope || style.Scope == "shared")
        .OrderBy(style => style.StyleId, StringComparer.Ordinal).ToArray();
    public HomeProductivityStyle? GetStyle(string styleId)
    {
        var matches = _styles.Values.Where(style => style.StyleId == styleId).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    public static string StyleScope(HomeProductivityContext context) => JsonSerializer.Serialize(new[] { context.AppId, context.ArtifactId });
    public HomeProductivityStyle? GetStyle(string styleId, string scope) =>
        _styles.GetValueOrDefault((scope, styleId)) ?? _styles.GetValueOrDefault(("shared", styleId));

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
        if (style.BasedOnStyleIds is null || style.BasedOnStyleIds.Any(string.IsNullOrWhiteSpace) || style.BasedOnStyleIds.Count > 500)
            throw new ArgumentException("Style dependencies must be explicit and bounded.", nameof(style));
        _styles[(style.Scope, style.StyleId)] = style with { Properties = style.Properties.Clone(), AppExtensions = style.AppExtensions?.Clone(),
            BasedOnStyleIds = Array.AsReadOnly(style.BasedOnStyleIds.Distinct(StringComparer.Ordinal).ToArray()) };
    }

    public HomeProductivityObjectBundle SerializeSelection(HomeProductivityContext context,
        IReadOnlyList<HomeProductivityObject> objects)
    {
        ArgumentNullException.ThrowIfNull(context);
        var selected = context.SelectedObjectIds.ToHashSet();
        var copy = objects.Where(item => selected.Contains(item.ObjectId)).Select(SnapshotObject).ToArray();
        if (copy.Length != selected.Count) throw new InvalidDataException("A selected shared object is missing from the artifact snapshot.");
        var assetRefs = copy.SelectMany(item => item.AssetReferences).Distinct(StringComparer.Ordinal).ToArray();
        return new(1, copy, CaptureStyles(context, copy), assetRefs, JsonDocument.Parse("{}").RootElement.Clone(),
            JsonDocument.Parse(JsonSerializer.Serialize(new { text = string.Join("\n", copy.Select(item => item.Content.ToString())) }, _json)).RootElement.Clone());
    }

    private IReadOnlyList<HomeProductivityStyle> CaptureStyles(HomeProductivityContext context, IReadOnlyList<HomeProductivityObject> objects)
    {
        var captured = new Dictionary<string, HomeProductivityStyle>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (captured.ContainsKey(id)) return;
            if (captured.Count + visiting.Count >= 500 || !visiting.Add(id))
                throw new InvalidDataException("Shared style dependencies are cyclic or exceed the supported bound.");
            var style = GetStyle(id, StyleScope(context))
                ?? throw new InvalidDataException($"The canonical definition for referenced style '{id}' is unavailable; copy cannot omit it.");
            foreach (var dependency in style.BasedOnStyleIds) Visit(dependency);
            visiting.Remove(id);
            captured.Add(id, style with { Properties = style.Properties.Clone(), AppExtensions = style.AppExtensions?.Clone(),
                BasedOnStyleIds = Array.AsReadOnly(style.BasedOnStyleIds.ToArray()) });
        }
        foreach (var item in objects)
            if (_handlers.TryGetValue((item.ObjectType, item.SchemaVersion), out var handler))
                foreach (var id in handler.GetReferencedStyleIds(item)) Visit(id);
        return Array.AsReadOnly(captured.Values.OrderBy(style => style.StyleId, StringComparer.Ordinal).ToArray());
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
                !_handlers.TryGetValue((item.ObjectType, item.SchemaVersion), out var handler) || handler.Schema.SchemaVersion != item.SchemaVersion)
            {
                code = target.SupportedObjectTypes.Contains("artifact.reference") ? "OfferArtifactEmbed" : "RequiredSchemaUnsupported";
                return false;
            }
        }
        if (bundle.Styles is null || bundle.Styles.Count > 500 || bundle.Styles.Any(style => style is null || string.IsNullOrWhiteSpace(style.StyleId) || style.Version < 1 || string.IsNullOrWhiteSpace(style.Scope)) ||
            bundle.Styles.Select(style => style.StyleId).Distinct(StringComparer.Ordinal).Count() != bundle.Styles.Count)
        { code = "StyleBundleInvalid"; return false; }
        var incoming = bundle.Styles.ToDictionary(style => style.StyleId, StringComparer.Ordinal);
        foreach (var item in bundle.Objects)
            foreach (var id in _handlers[(item.ObjectType, item.SchemaVersion)].GetReferencedStyleIds(item))
                if (!incoming.ContainsKey(id)) { code = "RequiredStyleMissing"; return false; }
        foreach (var style in bundle.Styles)
            if (style.BasedOnStyleIds is null || style.BasedOnStyleIds.Count > 500 ||
                style.BasedOnStyleIds.Any(id => string.IsNullOrWhiteSpace(id) || !incoming.ContainsKey(id)))
            { code = "RequiredStyleMissing"; return false; }
        var visited = new Dictionary<string, int>(StringComparer.Ordinal);
        bool Acyclic(string id)
        {
            if (visited.TryGetValue(id, out var state)) return state == 2;
            visited[id] = 1;
            foreach (var dependency in incoming[id].BasedOnStyleIds) if (!Acyclic(dependency)) return false;
            visited[id] = 2; return true;
        }
        if (incoming.Keys.Any(id => !Acyclic(id))) { code = "StyleDependencyCycle"; return false; }
        foreach (var style in bundle.Styles)
        {
            var targetStyle = GetStyle(style.StyleId, StyleScope(target));
            if (targetStyle is null) { code = "StyleImportRequired"; return false; }
            if (targetStyle.Version != style.Version || !JsonElement.DeepEquals(targetStyle.Properties, style.Properties) ||
                targetStyle.BasedOnStyleIds.Count != style.BasedOnStyleIds.Count ||
                !targetStyle.BasedOnStyleIds.ToHashSet(StringComparer.Ordinal).SetEquals(style.BasedOnStyleIds) ||
                targetStyle.AppExtensions.HasValue != style.AppExtensions.HasValue ||
                (targetStyle.AppExtensions is { } targetExtensions && !JsonElement.DeepEquals(targetExtensions, style.AppExtensions!.Value)))
            { code = "StyleDefinitionConflict"; return false; }
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
        var key = (handler.Schema.ObjectType, handler.Schema.SchemaVersion);
        if (_handlers.ContainsKey(key)) throw new InvalidOperationException("Each shared object schema version requires one canonical semantic handler.");
        if (!_schemas.TryGetValue(key.ObjectType, out var latest) || latest.SchemaVersion <= key.SchemaVersion) RegisterSchema(handler.Schema);
        _handlers.Add(key, handler);
    }
    public HomeProductivityObject CreateObject(string objectType, Guid objectId, JsonElement content) =>
        CreateObject(objectType, objectId, content, 1);
    public HomeProductivityObject CreateObject(string objectType, Guid objectId, JsonElement content, int schemaVersion) =>
        _handlers.TryGetValue((objectType, schemaVersion), out var handler) ? handler.Create(objectId, content) :
            throw new NotSupportedException("The shared object creation provider is unavailable.");
    public HomeProductivityObjectRenderResult RenderObject(HomeProductivityObject value) =>
        _handlers.TryGetValue((value.ObjectType, value.SchemaVersion), out var handler) ? handler.Render(value) :
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
        var candidates = _handlers.Where(pair => pair.Key.Type == action.ObjectType).Select(pair => pair.Value).ToArray();
        if (candidates.Length == 0)
            return new(false, "ObjectActionProviderUnavailable", "The shared object action provider is unavailable.", context.Revision, []);
        var descriptors = candidates.SelectMany(handler => handler.Actions).Where(descriptor => descriptor.ActionId == action.ActionId && descriptor.Version == action.Version).ToArray();
        if (!descriptors.Any(descriptor => NineToOne.Cui.AI.ActionJsonSchemaValidator.Validate(descriptor.ArgumentSchema.GetRawText(), action.Arguments, out _)))
            return new(false, "ObjectActionInvalid", "The shared typed action or arguments are unsupported.", context.Revision, []);
        var owners = _artifactActions.Where(provider => provider.AppId == context.AppId).ToArray();
        if (owners.Length != 1) return new(false, "ArtifactExecutorUnavailable", "The canonical artifact owner is unavailable.", context.Revision, []);
        var result = await owners[0].ApplyAsync(context, action, source =>
        {
            if (source.ObjectType != action.ObjectType || !action.ObjectIds.Contains(source.ObjectId))
                throw new InvalidDataException("The artifact owner supplied an object outside the checked action targets.");
            if (!_handlers.TryGetValue((source.ObjectType, source.SchemaVersion), out var handler))
                throw new InvalidDataException("The owning object schema has no registered action handler.");
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

    public async ValueTask<HomeProductivityActionResult> InsertObjectsAsync(HomeProductivityContext context,
        IReadOnlyList<HomeProductivityObject> objects, string operationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.CanonicalRevision.IsValid || string.IsNullOrWhiteSpace(operationId) || objects is null || objects.Count is < 1 or > 10000 ||
            objects.Any(item => item is null || item.ObjectId == Guid.Empty) || objects.Select(item => item.ObjectId).Distinct().Count() != objects.Count)
            return new(false, "InsertionInvalid", "Insertion requires unique canonical object IDs, an operation ID and current artifact revision.", context.Revision, [])
                { ArtifactRevision = context.CanonicalRevision };
        context = context with { SelectedObjectIds = Array.AsReadOnly(context.SelectedObjectIds.ToArray()),
            SupportedObjectTypes = context.SupportedObjectTypes.ToFrozenSet(StringComparer.Ordinal) };
        var captured = Array.AsReadOnly(objects.Select(SnapshotObject).ToArray());
        foreach (var item in captured)
        {
            if (!context.SupportedObjectTypes.Contains(item.ObjectType) || !_handlers.TryGetValue((item.ObjectType, item.SchemaVersion), out var handler) ||
                item.SchemaVersion != handler.Schema.SchemaVersion ||
                !NineToOne.Cui.AI.ActionJsonSchemaValidator.Validate(handler.Schema.Schema.GetRawText(), item.Content, out _))
                return new(false, "InsertionUnsupported", "The destination cannot insert this shared object schema.", context.Revision, [])
                    { ArtifactRevision = context.CanonicalRevision };
            try { _ = handler.Create(item.ObjectId, item.Content); }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or NotSupportedException or JsonException)
            {
                return new(false, "InsertionInvalid", "The shared object handler rejected the canonical content.", context.Revision, [])
                    { ArtifactRevision = context.CanonicalRevision };
            }
        }
        var owners = _artifactActions.Where(provider => provider.AppId == context.AppId).ToArray();
        if (owners.Length != 1 || owners[0] is not IHomeProductivityArtifactInsertionProvider owner)
            return new(false, "ArtifactInsertionUnavailable", "The canonical owner does not expose shared object insertion.", context.Revision, [])
                { ArtifactRevision = context.CanonicalRevision };
        var result = await owner.InsertAsync(context, captured, operationId, cancellationToken).ConfigureAwait(false);
        var observedTargets = result.AffectedObjectIds?.ToArray() ?? [];
        var validCommit = result.Outcome == HomeProductivityArtifactOutcome.Committed &&
            result.ArtifactRevision is { IsValid: true } observed &&
            (context.CanonicalRevision.VersionId is not null) == (observed.VersionId is not null) &&
            observedTargets.Length == captured.Count && observedTargets.Distinct().Count() == observedTargets.Length &&
            observedTargets.ToHashSet().SetEquals(captured.Select(item => item.ObjectId));
        if ((result.Succeeded && !validCommit) || (!result.Succeeded && result.Outcome == HomeProductivityArtifactOutcome.Committed))
            return result with { Succeeded = false, Code = "ArtifactOutcomeNeedsRecovery",
                Message = "The owner insertion acknowledgement is inconsistent. Refresh the canonical artifact before further edits.",
                AffectedObjectIds = Array.AsReadOnly(observedTargets), Outcome = HomeProductivityArtifactOutcome.NeedsRecovery };
        return result with { AffectedObjectIds = Array.AsReadOnly(observedTargets) };
    }

    public IReadOnlyList<HomeProductivityObject> Paste(HomeProductivityObjectBundle bundle, HomeProductivityContext target)
    {
        if (!CanPaste(bundle, target, out var code))
            throw new InvalidDataException($"Productivity bundle cannot be pasted: {code}.");
        return Array.AsReadOnly(bundle.Objects.Select(item =>
        {
            var captured = SnapshotObject(item);
            var newId = Guid.NewGuid();
            if (_handlers.GetValueOrDefault((item.ObjectType, item.SchemaVersion)) is IHomeProductivityObjectCloneHandler cloner)
            {
                var cloned = cloner.CloneForPaste(captured, newId);
                if (cloned.ObjectId != newId || cloned.ObjectType != item.ObjectType || cloned.SchemaVersion != item.SchemaVersion)
                    throw new InvalidDataException("Shared paste changed an identity or schema outside its declared clone operation.");
                return SnapshotObject(cloned);
            }
            if (item.ObjectType is "drawing.ink" or "graph")
                throw new InvalidDataException("This object requires its canonical identity clone handler before paste.");
            return captured with { ObjectId = newId };
        }).ToArray());
    }

    public HomeProductivityObject Convert(HomeProductivityObject source, string targetType, JsonElement options)
    {
        ArgumentNullException.ThrowIfNull(source);
        source = SnapshotObject(source);
        if (!_schemas.TryGetValue(source.ObjectType, out var from) || !_schemas.ContainsKey(targetType))
            throw new InvalidDataException("Source or target shared object schema is unavailable.");
        if (source.SchemaVersion > from.SchemaVersion) throw new InvalidDataException("Source object schema version is unsupported.");
        if (source.ObjectType == targetType) return source; // Identity conversion never silently upgrades an incompatible content schema.
        if (targetType == "artifact.reference")
            throw new InvalidDataException("An artifact reference requires an existing canonical owning reference; an unattached object cannot synthesize one.");
        throw new InvalidDataException("This object conversion is not registered; content cannot be flattened implicitly.");
    }

    public HomeProductivityCompatibility GetCompatibility(string appId, string appVersion, IEnumerable<string> requiredTypes)
    {
        var unsupported = requiredTypes.Where(type => !_handlers.Keys.Any(key => key.Type == type)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
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
