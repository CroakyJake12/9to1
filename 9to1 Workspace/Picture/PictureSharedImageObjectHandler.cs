using System.Collections.Frozen;
using System.Text.Json;
using HavenOS.Home.Core;
using NineToOne.Cui.AI;

namespace HavenOS.Images;

/// <summary>Read-only media.image semantics over a bounded batch of freshly prepared owning Picture scopes. No synchronous asset reads or authority cache.</summary>
public sealed class PictureSharedImageObjectHandler
    : IHomeProductivityObjectHandler, IHomeProductivityObjectCloneHandler
{
    public const int MaximumPreparedImages = 256;
    private readonly FrozenDictionary<Guid, PreparedPictureSharedImage> _prepared;
    public PictureSharedImageObjectHandler(PreparedPictureSharedImage prepared) : this(new[] { prepared }) { }
    /// <summary>Caller owns these scoped preparations and must revalidate all sources before presenting a composite.
    /// This lookup contains detached presentation references, never a grant or an editable object graph.</summary>
    public PictureSharedImageObjectHandler(IEnumerable<PreparedPictureSharedImage> prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var captured = new Dictionary<Guid, PreparedPictureSharedImage>();
        long bytes = 0;
        foreach (var item in prepared)
        {
            if (item is null || captured.Count == MaximumPreparedImages || item.Reference.DocumentId == Guid.Empty ||
                !captured.TryAdd(item.Reference.DocumentId, item))
                throw new ArgumentException("Prepared images require bounded, unique canonical document identities.", nameof(prepared));
            var frame = item.Frame;
            bytes = checked(bytes + (long)frame.Stride * frame.Height);
            if (bytes > PictureGlycinDecoder.MaximumBufferBytes)
                throw new ArgumentException("The prepared image batch exceeds the owning raster materialization budget.", nameof(prepared));
        }
        if (captured.Count == 0) throw new ArgumentException("At least one prepared image is required.", nameof(prepared));
        _prepared = captured.ToFrozenDictionary();
    }
    private PreparedPictureSharedImage Find(Guid objectId)
    {
        if (!_prepared.TryGetValue(objectId, out var prepared))
            throw new InvalidDataException("This canonical image was not prepared in the current owner scope.");
        _ = prepared.Frame;
        return prepared;
    }
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static JsonElement Json(string text) { using var parsed = JsonDocument.Parse(text); return parsed.RootElement.Clone(); }
    public HomeProductivityObjectSchema Schema { get; } = new("media.image", 1, true,
        Json("""{"type":"object","required":["backingFileId","documentId","documentRevision","sourceAsset"],"additionalProperties":true}"""),
        new[] { "object.create", "object.render" }.ToFrozenSet(StringComparer.Ordinal));
    public IReadOnlyList<HomeProductivityObjectActionDescriptor> Actions { get; } = [];
    public JsonElement ReferenceContent => _prepared.Count == 1 ? ReferenceContentFor(_prepared.Keys.Single())
        : throw new InvalidOperationException("A batch requires the explicit canonical image identity.");
    public JsonElement ReferenceContentFor(Guid documentId) => JsonSerializer.SerializeToElement(Find(documentId).Reference, JsonOptions);
    public HomeProductivityObject Create(Guid objectId, JsonElement content)
    {
        var prepared = Find(objectId);
        var value = new HomeProductivityObject(objectId, Schema.ObjectType, Schema.SchemaVersion, content.Clone(), Json("{}"), Json("{}"), [prepared.Reference.SourceAsset.AssetId.ToString("D")], Json("{}"));
        Validate(value); return value;
    }
    public HomeProductivityObject Transform(HomeProductivityObject source, HomeProductivityAction action)
    { Validate(source); throw new NotSupportedException("Shared image editing requires the owning Picture revision transaction."); }
    public HomeProductivityObject CloneForPaste(HomeProductivityObject source, Guid newObjectId)
    { Validate(source); throw new NotSupportedException("A new image identity requires the owning Picture clone transaction."); }
    public HomeProductivityObjectRenderResult Render(HomeProductivityObject source)
    {
        var prepared = Validate(source);
        var id = "image-" + source.ObjectId.ToString("N");
        var unsupported = source.Content.EnumerateObject()
            .Where(item => item.Name is not ("backingFileId" or "documentId" or "documentRevision" or "sourceAsset"))
            .Select(item => "content." + item.Name)
            .Concat(source.Content.GetProperty("sourceAsset").EnumerateObject()
                .Where(item => item.Name is not ("fileId" or "revisionId" or "contentHash" or "sizeBytes" or "assetId"))
                .Select(item => "content.sourceAsset." + item.Name))
            .Concat(source.Formatting.EnumerateObject().Select(item => "formatting." + item.Name))
            .Concat(source.Layout.EnumerateObject().Select(item => "layout." + item.Name)).ToArray();
        return new($"<Cui><Object Type=\"spe.raster\" id=\"{id}\" /></Cui>", unsupported)
        { RasterBindings = [new(id, source.ObjectId, prepared.Frame)] };
    }
    private PreparedPictureSharedImage Validate(HomeProductivityObject value)
    {
        var prepared = Find(value.ObjectId);
        _ = prepared.Frame; // A disposed owning preparation cannot be reused as an ongoing asset grant.
        if (value.ObjectId != prepared.Reference.DocumentId || value.ObjectType != Schema.ObjectType || value.SchemaVersion != Schema.SchemaVersion ||
            !ActionJsonSchemaValidator.Validate(Schema.Schema.GetRawText(), value.Content, out _) || value.Formatting.ValueKind != JsonValueKind.Object || value.Layout.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Shared image schema or canonical document identity does not match the prepared owner scope.");
        PictureSharedImageReference? reference;
        try { reference = value.Content.Deserialize<PictureSharedImageReference>(JsonOptions); }
        catch (JsonException exception) { throw new InvalidDataException("The owning Picture image reference is malformed.", exception); }
        if (reference != prepared.Reference)
            throw new InvalidDataException("The shared image reference differs from the exact prepared Picture and raw source revision.");
        return prepared;
    }
}
