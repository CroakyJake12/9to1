using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Productivity.NativeUI;

/// <summary>Native raster/vector projection of the existing canonical Canvas object/layer graph.
/// Original raster frames are supplied by the owning source session. This class owns no files,
/// graph mutations, history, selection, stroke engine, text engine or Home access grant.</summary>
public static class SharedVisualCompositionRenderer
{
    private sealed record ObjectDrawing(CanvasRect Placement, Bitmap? Raster, SharedVectorDrawing? Vector);

    public static RenderTargetBitmap Render(CanvasArtifact originalGraph, Guid pageId, PixelSize outputSize,
        IReadOnlyDictionary<Guid, Bitmap> originalRasterFrames, out IReadOnlyList<string> retainedUnsupportedProperties)
    {
        ArgumentNullException.ThrowIfNull(originalGraph); ArgumentNullException.ThrowIfNull(originalRasterFrames);
        if (outputSize.Width is < 1 or > 32768 || outputSize.Height is < 1 or > 32768 ||
            (long)outputSize.Width * outputSize.Height > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(outputSize));
        // Use the canonical codec to capture/validate exact graph identities and order.
        var graph = CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(originalGraph));
        var page = graph.Pages.SingleOrDefault(page => page.PageId == pageId)
            ?? throw new InvalidDataException("The original canonical visual page is unavailable.");
        var bounds = page.Bounds ?? throw new NotSupportedException("An infinite canonical page requires its owning viewport bounds before native raster projection.");
        if (page.Background.Kind != "solid" || page.Background.PatternId is not null)
            throw new NotSupportedException("This native projection requires a supported solid canonical page background; pattern content is preserved.");
        if (page.Strokes.Count > 0)
            throw new NotSupportedException("Structured ink requires the same owning Rnote renderer; retained strokes were not replaced by a private approximation.");
        var objects = page.Objects.ToDictionary(item => item.ObjectId);
        var layers = page.Layers.ToDictionary(layer => layer.LayerId);
        var prepared = new List<ObjectDrawing>();
        var unsupported = new List<string>();
        var vectorHandler = new HomeVectorShapeObjectHandler(); // Existing pure canonical semantics; not a Home engine.
        foreach (var layerId in page.LayerOrder)
        {
            var layer = layers[layerId];
            foreach (var objectId in page.ObjectOrder)
            {
                var item = objects[objectId];
                if (item.LayerId != layerId) continue;
                if (item.Transform.ScaleX != 1 || item.Transform.ScaleY != 1 || item.Transform.RotationDegrees != 0 || item.Style.Properties.Count != 0)
                    throw new NotSupportedException("This shared visual projection requires supported canonical placement; retained transforms/styles were not silently dropped.");
                var placement = item.Geometry with { X = item.Geometry.X + item.Transform.TranslateX, Y = item.Geometry.Y + item.Transform.TranslateY };
                ObjectDrawing drawing;
                if (item.ObjectTypeId == "media.image")
                {
                    if (!originalRasterFrames.TryGetValue(item.ObjectId, out var original) || original is null)
                        throw new InvalidDataException("LinkedAssetUnavailable: this raster object has no owning original frame.");
                    drawing = new(placement, original, null);
                }
                else if (item.ObjectTypeId == "drawing.vector")
                {
                    var shared = item.SharedPayload?.Deserialize<HomeProductivityObject>()
                        ?? throw new InvalidDataException("The vector object has no retained shared payload.");
                    if (shared.ObjectId != item.ObjectId || shared.ObjectType != item.ObjectTypeId)
                        throw new InvalidDataException("The canonical graph and original shared vector identities disagree.");
                    var rendered = vectorHandler.Render(shared);
                    var binding = rendered.VectorBindings.Single();
                    if (binding.ObjectId != item.ObjectId)
                        throw new InvalidDataException("The shared renderer returned a different object identity.");
                    unsupported.AddRange(rendered.RetainedUnsupportedProperties.Select(property => item.ObjectId.ToString("D") + ": " + property));
                    drawing = new(placement, null, new SharedVectorDrawing(binding));
                }
                else throw new NotSupportedException("This shared visual renderer does not yet render the retained object family: " + item.ObjectTypeId);
                if (layer.IsVisible) prepared.Add(drawing);
            }
        }
        var result = new RenderTargetBitmap(outputSize, new Vector(96, 96));
        try
        {
            using var context = result.CreateDrawingContext();
            using var quality = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality });
            // Page viewport mapping is separate from element placement and the
            // shape's own canonical transform. Identity output stays unchanged.
            var sx = outputSize.Width / bounds.Width; var sy = outputSize.Height / bounds.Height;
            using var projection = context.PushTransform(new Matrix(sx, 0, 0, sy, -bounds.X * sx, -bounds.Y * sy));
            context.DrawRectangle(new SolidColorBrush(Color.Parse(page.Background.Color)), null,
                new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            foreach (var drawing in prepared)
            {
                var target = new Rect(drawing.Placement.X, drawing.Placement.Y, drawing.Placement.Width, drawing.Placement.Height);
                if (drawing.Raster is { } raster) context.DrawImage(raster, new Rect(raster.Size), target);
                else drawing.Vector!.Draw(context, target);
            }
            retainedUnsupportedProperties = unsupported.AsReadOnly();
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
