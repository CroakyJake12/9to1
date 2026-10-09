using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace HavenOS.Images;

internal static class PictureVectorFillEditor
{
    private static readonly string[] Fields = ["Kind", "Color", "Opacity", "FillRule"];
    internal static HomeProductivityObject Apply(HomeProductivityObject original, Guid pathId,
        DocumentVectorFill requested, DocumentVectorFillRule rule, IReadOnlySet<string>? changedFields = null)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(requested);
        var fields = changedFields?.ToArray() ?? Fields.ToArray();
        if (fields.Length == 0 || fields.Any(field => !Fields.Contains(field, StringComparer.Ordinal)))
            throw new ArgumentException("Choose supported fill fields.", nameof(changedFields));
        var shape = HomeVectorShapeObjectHandler.ReadCanonical(original.Content, original.ObjectId);
        var selected = shape.Paths.SingleOrDefault(path => path.Id == pathId)
            ?? throw new InvalidOperationException("The original selected shape path is unavailable.");
        var merged = new DocumentVectorFill
        {
            Kind = fields.Contains("Kind") ? requested.Kind : selected.Fill.Kind,
            Color = fields.Contains("Color") ? requested.Color : selected.Fill.Color,
            Opacity = fields.Contains("Opacity") ? requested.Opacity : selected.Fill.Opacity
        };
        var selectedRule = fields.Contains("FillRule") ? rule : selected.FillRule;
        var editor = new DocumentVectorShapeEditor(DocumentVectorShapes.Clone(shape));
        editor.SetPathFill(pathId, merged, selectedRule);
        var edited = editor.Shape.Paths.Single(path => path.Id == pathId);
        var content = JsonNode.Parse(original.Content.GetRawText())!.AsObject();
        var pathJson = content["Paths"]!.AsArray().Single(value => value!["Id"]!.GetValue<Guid>() == pathId)!.AsObject();
        var fillJson = pathJson["Fill"] as JsonObject ?? throw new InvalidDataException("The original retained fill is unavailable.");
        foreach (var field in fields)
        {
            if (field == "FillRule") { pathJson[field] = JsonSerializer.SerializeToNode(edited.FillRule); continue; }
            fillJson[field] = field switch
            {
                "Kind" => JsonSerializer.SerializeToNode(edited.Fill.Kind),
                "Color" => JsonSerializer.SerializeToNode(edited.Fill.Color),
                "Opacity" => JsonSerializer.SerializeToNode(edited.Fill.Opacity),
                _ => throw new InvalidOperationException("Unknown shared fill field.")
            };
        }
        var updated = original with { Content = JsonSerializer.SerializeToElement(content) };
        _ = HomeVectorShapeObjectHandler.ReadCanonical(updated.Content, updated.ObjectId);
        _ = new HomeVectorShapeObjectHandler().Render(updated);
        return updated;
    }
}
