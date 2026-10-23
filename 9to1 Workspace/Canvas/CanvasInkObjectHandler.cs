using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Canvas;

/// <summary>
/// Shared drawing.ink semantics over the existing canonical Canvas stroke. ObjectId is
/// StrokeId; this adapter introduces neither an entity graph nor a private ink format.
/// Transform is pure preparation: only an owning artifact transaction can commit it.
/// </summary>
public sealed class CanvasInkObjectHandler : IHomeProductivityObjectHandler, IHomeProductivityObjectCloneHandler
{
    private readonly CanvasRnoteDocument? _renderOwner;
    private readonly Guid _renderRevision;

    public CanvasInkObjectHandler() { }

    /// <summary>Borrow an already-authorised canonical owner for exact selected-stroke rendering.
    /// The caller retains its lifetime and Files/Home read authority. This binding grants no access
    /// and cannot render a transformed proposal or a different owning revision.</summary>
    public CanvasInkObjectHandler(CanvasRnoteDocument renderOwner, Guid expectedArtifactRevision)
    {
        ArgumentNullException.ThrowIfNull(renderOwner);
        if (expectedArtifactRevision == Guid.Empty)
            throw new ArgumentException("An exact owning artifact revision is required.", nameof(expectedArtifactRevision));
        _renderOwner = renderOwner;
        _renderRevision = expectedArtifactRevision;
    }

    private static JsonElement Json(string value) { using var document = JsonDocument.Parse(value); return document.RootElement.Clone(); }
    public HomeProductivityObjectSchema Schema { get; } = new("drawing.ink", 1, true,
        Json("""{"type":"object","required":["StrokeId","LayerId","ToolDefinitionId","Samples","ResolvedBrushProperties","Transform","RevisionId"],"additionalProperties":true}"""),
        new[] { "object.create", "object.render", "ink.color" }.ToFrozenSet(StringComparer.Ordinal));
    public IReadOnlyList<HomeProductivityObjectActionDescriptor> Actions { get; } = [new("ink.color", 1,
        Json("""{"type":"object","properties":{"color":{"type":"string","pattern":"^#[0-9A-Fa-f]{8}$"}},"required":["color"],"additionalProperties":false}"""))];

    public HomeProductivityObject Create(Guid objectId, JsonElement content)
    {
        var result = new HomeProductivityObject(objectId, Schema.ObjectType, Schema.SchemaVersion, content.Clone(), Json("{}"), Json("{}"), [], Json("{}"));
        Read(result);
        return result;
    }

    public HomeProductivityObject CloneForPaste(HomeProductivityObject source, Guid newObjectId)
    {
        Read(source);
        if (newObjectId == Guid.Empty || newObjectId == source.ObjectId)
            throw new ArgumentException("A copied stroke requires a distinct nonempty canonical ID.", nameof(newObjectId));
        var content = JsonNode.Parse(source.Content.GetRawText())!.AsObject();
        content["StrokeId"] = newObjectId;
        content["RevisionId"] = newObjectId;
        // Target LayerId is deliberately not inferred from selection or paths.
        // The destination owner must explicitly validate/rebind that placement.
        var result = source with { ObjectId = newObjectId, Content = JsonSerializer.SerializeToElement(content) };
        Read(result);
        return result;
    }

    public HomeProductivityObject Transform(HomeProductivityObject source, HomeProductivityAction action)
    {
        Read(source);
        if (action.ActionId != "ink.color" || action.Version != 1 || action.ObjectType != Schema.ObjectType ||
            !action.ObjectIds.Contains(source.ObjectId) || action.Arguments.ValueKind != JsonValueKind.Object ||
            action.Arguments.EnumerateObject().Count() != 1 || !action.Arguments.TryGetProperty("color", out var color) || color.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("This shared ink action is unsupported or malformed.");
        var content = JsonNode.Parse(source.Content.GetRawText())!.AsObject();
        content["ResolvedBrushProperties"]!.AsObject()["Color"] = color.GetString();
        var result = source with { Content = JsonSerializer.SerializeToElement(content) };
        Read(result); // validate without dropping unknown fields or minting an owner revision
        return result;
    }

    public HomeProductivityObjectRenderResult Render(HomeProductivityObject source)
    {
        var stroke = Read(source);
        // Split erasing retains Samples as original input provenance. They no longer
        // describe the visible fragment; replaying them would restore erased ink.
        // The generic ABI 3 input port cannot submit an authoritative donor path.
        if (stroke.PathGeometry is not null && _renderOwner is null)
            throw new NotSupportedException("Authoritative stroke path geometry requires the owning path-capable renderer; original input samples cannot substitute for it.");
        if (stroke.Transform != new CanvasTransform())
            throw new NotSupportedException("The shared native ink renderer requires the owning transformed-stroke renderer for nonidentity transforms.");
        if (stroke.Samples.Count < 2 && _renderOwner is null)
            throw new NotSupportedException("This native stroke input port requires two samples; the canonical single-sample stroke is retained for an owning dot-capable renderer.");
        RnoteRenderFrame vector;
        if (_renderOwner is { } owner)
        {
            var artifact = owner.Snapshot;
            if (artifact.RevisionId != _renderRevision)
                throw new InvalidOperationException("Shared ink rendering targets a stale owning artifact revision.");
            var canonical = artifact.Pages.SelectMany(page => page.Strokes).SingleOrDefault(item => item.StrokeId == source.ObjectId);
            if (canonical is null || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(canonical), source.Content))
                throw new InvalidOperationException("Shared ink rendering must match the exact canonical owning stroke content.");
            // Export rechecks the revision under the owner's lock. It selects this
            // canonical native identity only, preserving exact erased/smoothed paths.
            var selected = owner.ExportCanonicalStrokeSelection([source.ObjectId], _renderRevision);
            using var durable = RnoteCanvasEngine.Open(selected);
            vector = durable.Render();
        }
        else
        {
            using var engine = RnoteCanvasEngine.Create();
            engine.DrawStroke(stroke.Samples.Select(sample => new RnotePointerSample(sample.X, sample.Y, sample.Pressure, sample.TiltX, sample.TiltY)).ToArray(), Style(stroke));
            // Match save/reopen donor numeric precision rather than displaying a transient higher-precision path.
            using var durable = RnoteCanvasEngine.Open(engine.Save());
            vector = durable.Render();
        }
        var pixels = CanvasSvgRasterizer.Render(vector.Svg, 512, 512);
        var id = "ink-" + source.ObjectId.ToString("N");
        var unsupported = new List<string>();
        if (stroke.Samples.Any(sample => sample.TiltX != 0 || sample.TiltY != 0)) unsupported.Add("content.Samples.tilt (retained; current donor brush does not use tilt)");
        if (stroke.ExtensionData is not null) unsupported.AddRange(stroke.ExtensionData.Keys.Select(key => "content." + key));
        if (stroke.ResolvedBrushProperties.ExtensionData is not null) unsupported.AddRange(stroke.ResolvedBrushProperties.ExtensionData.Keys.Select(key => "content.ResolvedBrushProperties." + key));
        unsupported.AddRange(source.Content.GetProperty("Transform").EnumerateObject()
            .Where(property => property.Name is not ("TranslateX" or "TranslateY" or "ScaleX" or "ScaleY" or "RotationDegrees"))
            .Select(property => "content.Transform." + property.Name));
        unsupported.AddRange(source.Formatting.EnumerateObject().Select(property => "formatting." + property.Name));
        unsupported.AddRange(source.Layout.EnumerateObject().Select(property => "layout." + property.Name));
        return new($"<Cui><Object Type=\"spe.raster\" id=\"{id}\" /></Cui>", unsupported)
        {
            RasterBindings = [new HomeProductivityRasterBinding(id, source.ObjectId,
                new HomeProductivityRasterFrame(pixels.Width, pixels.Height, pixels.Stride, pixels.PremultipliedBgra))]
        };
    }

    private CanvasInkStroke Read(HomeProductivityObject value)
    {
        if (value.ObjectId == Guid.Empty || value.ObjectType != Schema.ObjectType || value.SchemaVersion != Schema.SchemaVersion ||
            value.Content.ValueKind != JsonValueKind.Object || value.Formatting.ValueKind != JsonValueKind.Object || value.Layout.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Shared ink requires the canonical drawing.ink schema and stable stroke identity.");
        RequireUniqueFields(value.Content);
        foreach (var name in new[] { "ResolvedBrushProperties", "Transform" })
            if (value.Content.TryGetProperty(name, out var fields)) RequireUniqueFields(fields);
        if (value.Content.TryGetProperty("Samples", out var samples) && samples.ValueKind == JsonValueKind.Array)
            foreach (var sample in samples.EnumerateArray()) RequireUniqueFields(sample);
        CanvasInkStroke stroke;
        try { stroke = value.Content.Deserialize<CanvasInkStroke>() ?? throw new InvalidDataException("Shared ink content is empty."); }
        catch (JsonException error) { throw new InvalidDataException("The shared canonical stroke is malformed.", error); }
        if (stroke.StrokeId != value.ObjectId || stroke.LayerId == Guid.Empty || stroke.RevisionId == Guid.Empty || stroke.Samples is null ||
            stroke.Samples.Count is < 1 or > RnoteCanvasEngine.MaximumStrokeSamples || stroke.Samples.Any(sample => sample is null ||
                !double.IsFinite(sample.X) || !double.IsFinite(sample.Y) || !double.IsFinite(sample.Pressure) || sample.Pressure is < 0 or > 1 ||
                !double.IsFinite(sample.TiltX) || !double.IsFinite(sample.TiltY) || sample.TimestampMilliseconds < 0) ||
            stroke.Transform is null || stroke.ResolvedBrushProperties is null || stroke.ResolvedBrushProperties.EngineParameters is null)
            throw new InvalidDataException("Shared ink identity and structure must match the canonical stroke.");
        var transform = stroke.Transform;
        if (!double.IsFinite(transform.TranslateX) || !double.IsFinite(transform.TranslateY) ||
            !double.IsFinite(transform.ScaleX) || !double.IsFinite(transform.ScaleY) || transform.ScaleX == 0 || transform.ScaleY == 0 ||
            !double.IsFinite(transform.RotationDegrees))
            throw new InvalidDataException("Shared ink requires finite canonical transforms with nonzero scales.");
        Style(stroke).ValidateAndResolve();
        return stroke;
    }

    private static void RequireUniqueFields(JsonElement value)
    {
        // Canonical persistence reads typed fields case-insensitively. A duplicate
        // must not acquire a different meaning after shared transfer or reopening.
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Select(property => property.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.EnumerateObject().Count())
            throw new InvalidDataException("Shared ink fields must be unambiguous canonical objects.");
    }

    private static CanvasRnoteInkStyle Style(CanvasInkStroke stroke)
    {
        var brush = stroke.ResolvedBrushProperties;
        if (brush.EngineId != "rnote" || !brush.EngineParameters.TryGetValue("brushStyle", out var style) || style.ValueKind != JsonValueKind.String ||
            brush.EngineParameters.Keys.Any(key => key != "brushStyle"))
            throw new NotSupportedException("Shared ink retains these brush properties but this native renderer cannot reproduce them.");
        var kind = style.GetString() switch { "solid" => CanvasRnoteInkKind.Solid, "marker" => CanvasRnoteInkKind.Marker,
            _ => throw new NotSupportedException("The requested donor brush is unavailable in this renderer.") };
        if (stroke.ToolDefinitionId != (kind == CanvasRnoteInkKind.Solid ? "pen" : "highlighter"))
            throw new InvalidDataException("Canonical stroke tool and resolved donor brush disagree.");
        return new(kind, brush.Color, brush.BaseWidth, brush.Opacity);
    }
}
