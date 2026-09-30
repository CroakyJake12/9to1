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
        if (stroke.Transform != new CanvasTransform())
            throw new NotSupportedException("The shared native ink renderer requires the owning transformed-stroke renderer for nonidentity transforms.");
        var style = Style(stroke);
        using var engine = RnoteCanvasEngine.Create();
        engine.DrawStroke(stroke.Samples.Select(sample => new RnotePointerSample(sample.X, sample.Y, sample.Pressure, sample.TiltX, sample.TiltY)).ToArray(), style);
        // Match save/reopen donor numeric precision rather than displaying a transient higher-precision path.
        using var durable = RnoteCanvasEngine.Open(engine.Save());
        var vector = durable.Render();
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
        CanvasInkStroke stroke;
        try { stroke = value.Content.Deserialize<CanvasInkStroke>() ?? throw new InvalidDataException("Shared ink content is empty."); }
        catch (JsonException error) { throw new InvalidDataException("The shared canonical stroke is malformed.", error); }
        if (stroke.StrokeId != value.ObjectId || stroke.LayerId == Guid.Empty || stroke.RevisionId == Guid.Empty || stroke.Samples is null ||
            stroke.Samples.Count > RnoteCanvasEngine.MaximumStrokeSamples || stroke.Samples.Any(sample => sample is null) ||
            stroke.Transform is null || stroke.ResolvedBrushProperties is null || stroke.ResolvedBrushProperties.EngineParameters is null)
            throw new InvalidDataException("Shared ink identity and structure must match the canonical stroke.");
        RnoteCanvasEngine.CaptureSamples(stroke.Samples.Select(sample => new RnotePointerSample(sample.X, sample.Y, sample.Pressure, sample.TiltX, sample.TiltY)).ToArray());
        Style(stroke).ValidateAndResolve();
        return stroke;
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
