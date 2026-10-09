using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace HavenOS.Images;

internal static class PictureVectorStrokeEditor
{
    private static readonly string[] Fields = ["Enabled", "Color", "Width", "Opacity", "Cap", "Join"];

    internal static HomeProductivityObject Apply(HomeProductivityObject original, Guid pathId,
        DocumentVectorStroke requested, IReadOnlySet<string>? changedFields = null)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(requested);
        var fields = changedFields?.ToArray() ?? Fields.ToArray();
        if (fields.Length == 0 || fields.Any(field => !Fields.Contains(field, StringComparer.Ordinal)))
            throw new ArgumentException("Choose supported stroke fields.", nameof(changedFields));
        var shape = HomeVectorShapeObjectHandler.ReadCanonical(original.Content, original.ObjectId);
        var selected = shape.Paths.SingleOrDefault(path => path.Id == pathId)
            ?? throw new InvalidOperationException("The original selected shape path is unavailable.");
        // Untouched fields come from the current original, including full
        // floating-point precision and styles restored by a separate Undo.
        var merged = new DocumentVectorStroke
        {
            Enabled = fields.Contains("Enabled") ? requested.Enabled : selected.Stroke.Enabled,
            Color = fields.Contains("Color") ? requested.Color : selected.Stroke.Color,
            Width = fields.Contains("Width") ? requested.Width : selected.Stroke.Width,
            Opacity = fields.Contains("Opacity") ? requested.Opacity : selected.Stroke.Opacity,
            Cap = fields.Contains("Cap") ? requested.Cap : selected.Stroke.Cap,
            Join = fields.Contains("Join") ? requested.Join : selected.Stroke.Join
        };
        var editor = new DocumentVectorShapeEditor(DocumentVectorShapes.Clone(shape));
        editor.SetPathStroke(pathId, merged);
        var edited = editor.Shape.Paths.Single(path => path.Id == pathId).Stroke;
        var content = JsonNode.Parse(original.Content.GetRawText())!.AsObject();
        var pathJson = content["Paths"]!.AsArray().Single(value => value!["Id"]!.GetValue<Guid>() == pathId)!.AsObject();
        var strokeJson = pathJson["Stroke"] as JsonObject ?? throw new InvalidDataException("The original retained stroke is unavailable.");
        foreach (var field in fields)
            strokeJson[field] = field switch
            {
                "Enabled" => JsonSerializer.SerializeToNode(edited.Enabled),
                "Color" => JsonSerializer.SerializeToNode(edited.Color),
                "Width" => JsonSerializer.SerializeToNode(edited.Width),
                "Opacity" => JsonSerializer.SerializeToNode(edited.Opacity),
                "Cap" => JsonSerializer.SerializeToNode(edited.Cap),
                "Join" => JsonSerializer.SerializeToNode(edited.Join),
                _ => throw new InvalidOperationException("Unknown shared stroke field.")
            };
        var updated = original with { Content = JsonSerializer.SerializeToElement(content) };
        _ = HomeVectorShapeObjectHandler.ReadCanonical(updated.Content, updated.ObjectId);
        _ = new HomeVectorShapeObjectHandler().Render(updated);
        return updated;
    }
}
