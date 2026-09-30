using System.Collections.Frozen;
using System.Text.Json;
using HavenOS.Home.Core;
using NineToOne.Cui.AI;

namespace HavenOS.Images;

/// <summary>Read-only media.image semantics over one freshly prepared owning Picture scope. No synchronous asset reads or authority cache.</summary>
public sealed class PictureSharedImageObjectHandler(PreparedPictureSharedImage prepared)
    : IHomeProductivityObjectHandler, IHomeProductivityObjectCloneHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static JsonElement Json(string text) { using var parsed = JsonDocument.Parse(text); return parsed.RootElement.Clone(); }
    public HomeProductivityObjectSchema Schema { get; } = new("media.image", 1, true,
        Json("""{"type":"object","required":["backingFileId","documentId","documentRevision","sourceAsset"],"additionalProperties":true}"""),
        new[] { "object.create", "object.render" }.ToFrozenSet(StringComparer.Ordinal));
    public IReadOnlyList<HomeProductivityObjectActionDescriptor> Actions { get; } = [];
    public JsonElement ReferenceContent => JsonSerializer.SerializeToElement(prepared.Reference, JsonOptions);
    public HomeProductivityObject Create(Guid objectId, JsonElement content)
    {
        var value = new HomeProductivityObject(objectId, Schema.ObjectType, Schema.SchemaVersion, content.Clone(), Json("{}"), Json("{}"), [prepared.Reference.SourceAsset.AssetId.ToString("D")], Json("{}"));
        Validate(value); return value;
    }
    public HomeProductivityObject Transform(HomeProductivityObject source, HomeProductivityAction action)
    { Validate(source); throw new NotSupportedException("Shared image editing requires the owning Picture revision transaction."); }
    public HomeProductivityObject CloneForPaste(HomeProductivityObject source, Guid newObjectId)
    { Validate(source); throw new NotSupportedException("A new image identity requires the owning Picture clone transaction."); }
    public HomeProductivityObjectRenderResult Render(HomeProductivityObject source)
    {
        Validate(source);
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
    private void Validate(HomeProductivityObject value)
    {
        _ = prepared.Frame; // A disposed owning preparation cannot be reused as an ongoing asset grant.
        if (value.ObjectId != prepared.Reference.DocumentId || value.ObjectType != Schema.ObjectType || value.SchemaVersion != Schema.SchemaVersion ||
            !ActionJsonSchemaValidator.Validate(Schema.Schema.GetRawText(), value.Content, out _) || value.Formatting.ValueKind != JsonValueKind.Object || value.Layout.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Shared image schema or canonical document identity does not match the prepared owner scope.");
        PictureSharedImageReference? reference;
        try { reference = value.Content.Deserialize<PictureSharedImageReference>(JsonOptions); }
        catch (JsonException exception) { throw new InvalidDataException("The owning Picture image reference is malformed.", exception); }
        if (reference != prepared.Reference)
            throw new InvalidDataException("The shared image reference differs from the exact prepared Picture and raw source revision.");
    }
}
