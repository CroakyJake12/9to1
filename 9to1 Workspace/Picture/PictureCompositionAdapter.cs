using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace HavenOS.Images;

/// <summary>Picture product adapter over the SAME canonical shared visual graph and transactions.
/// Graph bytes, request actors and semantic targets do not grant Files/Home access.</summary>
public static class PictureCompositionAdapter
{
    public const int MaximumCompositionBytes = 16 * 1024 * 1024;
    public static CanvasArtifact Read(PictureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.CompositionState is not { } bytes) return Create(document);
        if (bytes.Length is < 1 or > MaximumCompositionBytes) throw new InvalidDataException("The Picture composition exceeds its supported graph budget.");
        var graph = CanvasArtifactCodec.Deserialize(bytes);
        if (graph.ArtifactId != document.DocumentId || graph.Pages.Count != 1 || graph.PageOrder.Count != 1 || graph.PageOrder[0] != graph.Pages[0].PageId)
            throw new InvalidDataException("The Picture document and canonical composition identities disagree.");
        var source = SourceObject(graph);
        if (source.SharedPayload is not { ValueKind: JsonValueKind.Object } payload ||
            payload.GetProperty("sourcePath").GetString() != document.SourcePath ||
            payload.GetProperty("sourceRevision").GetString() != document.SourceRevision ||
            payload.GetProperty("fileId").GetString() != document.FileId)
            throw new InvalidDataException("The canonical raster element does not refer to this retained Picture source.");
        return graph;
    }

    public static void Validate(PictureDocument document)
    {
        try { _ = Read(document); }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or CanvasArtifactFormatException)
        { throw new InvalidDataException("The retained Picture composition graph is invalid.", error); }
    }

    public static CanvasObject SourceObject(CanvasArtifact graph) => graph.Pages.Single().Objects
        .Single(item => item.ObjectTypeId == "media.image");

    private static CanvasArtifact Create(PictureDocument document)
    {
        var width = document.InitialCanvasWidth ?? document.CanvasWidth;
        var height = document.InitialCanvasHeight ?? document.CanvasHeight;
        var graph = CanvasArtifact.Create(document.DisplayName, CanvasDocumentMode.Paged);
        graph.ArtifactId = document.DocumentId;
        var page = graph.Pages[0];
        var layer = page.Layers[0] with { Name = "Source image" };
        var source = new CanvasObject
        {
            ObjectTypeId = "media.image", LayerId = layer.LayerId, Geometry = new(0, 0, width, height),
            Accessibility = new() { Name = "Original raster source" },
            SharedPayload = JsonSerializer.SerializeToElement(new { fileId = document.FileId, sourcePath = document.SourcePath,
                sourceRevision = document.SourceRevision })
        };
        graph.Pages[0] = page with { Bounds = new(0, 0, width, height), Layers = [layer], Objects = [source], ObjectOrder = [source.ObjectId],
            Background = new() { Color = "#00000000" } };
        return graph;
    }

    public static byte[] AddVector(PictureDocument document, HomeProductivityObject originalSharedVector, Guid? destinationLayer = null)
    {
        ArgumentNullException.ThrowIfNull(originalSharedVector);
        if (originalSharedVector.ObjectType != "drawing.vector" || originalSharedVector.SchemaVersion != 1)
            throw new NotSupportedException("This Picture composition slice accepts canonical shared vector objects.");
        var shape = HomeVectorShapeObjectHandler.ReadCanonical(originalSharedVector.Content, originalSharedVector.ObjectId);
        _ = new HomeVectorShapeObjectHandler().Render(originalSharedVector); // Reject unsupported rendering before graph mutation.
        var graph = Read(document);
        var session = new CanvasArtifactSession(graph);
        var page = graph.Pages[0];
        var sourceLayer = SourceObject(graph).LayerId;
        var layer = destinationLayer is { } chosen
            ? page.Layers.Single(layer => layer.LayerId == chosen)
            : page.Layers.FirstOrDefault(layer => layer.LayerId != sourceLayer && !layer.IsLocked);
        if (layer is null)
        {
            Demand(session.CreateLayer(Request(session), page.PageId, "Objects"));
            graph = session.GetArtifactSnapshot(); page = graph.Pages[0];
            layer = page.Layers.Single(layer => layer.LayerId != sourceLayer && !layer.IsLocked);
        }
        var originalWidth = document.InitialCanvasWidth ?? document.CanvasWidth;
        var originalHeight = document.InitialCanvasHeight ?? document.CanvasHeight;
        var width = Math.Min(originalWidth * .6, shape.ViewBox.Width);
        var height = Math.Min(originalHeight * .6, shape.ViewBox.Height);
        var added = new CanvasObject
        {
            ObjectId = originalSharedVector.ObjectId, ObjectTypeId = originalSharedVector.ObjectType, LayerId = layer.LayerId,
            Geometry = new((originalWidth - width) / 2, (originalHeight - height) / 2, width, height),
            Accessibility = new() { Name = shape.Name, Description = shape.AccessibilityDescription },
            SharedPayload = JsonSerializer.SerializeToElement(originalSharedVector)
        };
        Demand(session.InsertSharedObjects(Request(session), page.PageId, [added]));
        return Encode(session.GetArtifactSnapshot());
    }

    public static byte[]? Mutate(PictureDocument document, Guid expectedGraphRevision,
        Func<CanvasArtifactSession, Guid, CanvasApiResult<CanvasMutationResult>> mutation)
    {
        var graph = Read(document);
        if (graph.RevisionId != expectedGraphRevision) throw new InvalidOperationException("RevisionConflict: select this current composition again.");
        var session = new CanvasArtifactSession(graph);
        var originalRevision = session.CurrentRevisionId;
        Demand(mutation(session, graph.Pages[0].PageId));
        return session.CurrentRevisionId == originalRevision ? document.CompositionState : Encode(session.GetArtifactSnapshot());
    }

    public static HomeProductivityObject ReadVector(CanvasObject item)
    {
        if (item.ObjectTypeId != "drawing.vector") throw new NotSupportedException("Select an editable shared vector object.");
        var shared = item.SharedPayload?.Deserialize<HomeProductivityObject>() ?? throw new InvalidDataException("The shared vector payload is unavailable.");
        if (shared.ObjectId != item.ObjectId || shared.ObjectType != item.ObjectTypeId)
            throw new InvalidDataException("The shared object identity disagrees with its canonical graph element.");
        _ = HomeVectorShapeObjectHandler.ReadCanonical(shared.Content, shared.ObjectId);
        return shared;
    }

    public static byte[] CopyForDocument(PictureDocument source, byte[] bytes, Guid copyDocumentId)
    {
        var graph = Read(source);
        if (!bytes.AsSpan().SequenceEqual(source.CompositionState))
            throw new InvalidOperationException("The copy must use this same retained composition source.");
        graph.ArtifactId = copyDocumentId;
        var page = graph.Pages[0];
        var raster = SourceObject(graph);
        graph.Pages[0] = page with { Objects = page.Objects.Select(item => item.ObjectId == raster.ObjectId
            ? item with { SharedPayload = JsonSerializer.SerializeToElement(new { fileId = (string?)null,
                sourcePath = source.SourcePath, sourceRevision = source.SourceRevision }) } : item).ToList() };
        graph.SemanticHistory = null; // Source history refers to its own artifact; copy begins its own branch.
        return Encode(graph);
    }

    public static CanvasMutationRequest Request(CanvasArtifactSession session) =>
        new(session.CurrentRevisionId, Guid.NewGuid(), new("picture.native", "Picture native edit"));

    private static byte[] Encode(CanvasArtifact graph)
    {
        var bytes = CanvasArtifactCodec.Serialize(graph);
        if (bytes.Length > MaximumCompositionBytes) throw new InvalidDataException("The Picture composition exceeds its supported graph budget.");
        return bytes;
    }

    private static void Demand(CanvasApiResult<CanvasMutationResult> result)
    {
        if (!result.IsSuccess) throw new InvalidOperationException(result.Error!.Code + ": " + result.Error.Message);
    }
}
