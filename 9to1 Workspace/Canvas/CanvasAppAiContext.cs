using System.Text.Json;
using Haven.Application;
using NineToOne.Cui.AI;

namespace HavenOS.Apps.Canvas;

/// <summary>Captured by the trusted host from the current canonical Files-backed document, not model arguments.</summary>
public sealed record CanvasAiTarget(CanvasArtifact Artifact, Guid FileId, Guid FilesRevisionId, Guid ActivePageId,
    IReadOnlyList<Guid> SelectedIds, string CurrentTool, bool HostAllowsWrites, CanvasRnoteInkStyle? InkStyle = null);

/// <summary>Permission-filtered semantic context for the shared Dulche coordinator; never exports donor blobs or renders.</summary>
public sealed class CanvasAppAiContext(
    Func<CancellationToken, ValueTask<CanvasAiTarget?>> currentTarget,
    ResourceAuthorizationService authorization) : IAppAiContext
{
    public async ValueTask<AppAiContextSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        var target = await currentTarget(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No canonical Files-backed Canvas target is active.");
        if (target.FileId == Guid.Empty || target.FilesRevisionId == Guid.Empty)
            throw new UnauthorizedAccessException("Canvas AI requires a committed canonical Files target.");
        var scope = new ResourceScope("files.item", target.FileId.ToString(), target.FilesRevisionId.ToString(), ResourceAccess.Read);
        var actor = await authorization.AuthorizeAsync("canvas.file.open", [scope], cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Canvas AI cannot inspect this Files artifact.");
        var artifact = CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(target.Artifact));
        var page = artifact.Pages.SingleOrDefault(candidate => candidate.PageId == target.ActivePageId)
            ?? throw new InvalidOperationException("The active Canvas page no longer exists.");
        var ids = page.Objects.Select(item => item.ObjectId).Concat(page.Strokes.Select(item => item.StrokeId)).ToHashSet();
        var selectionIds = target.SelectedIds.Distinct().ToArray();
        if (selectionIds.Any(id => !ids.Contains(id)))
            throw new InvalidOperationException("Canvas AI selection does not belong to the active page.");
        target.InkStyle?.ValidateAndResolve();
        // Linked shared-owner contents need that owner's separate permission
        // check. Spatial wrappers and stable references remain inspectable;
        // donor state and arbitrary extensions are never sent to the model.
        var semantic = JsonSerializer.SerializeToElement(new
        {
            artifact.ArtifactId, artifact.RevisionId, artifact.CanvasMode,
            PageId = page.PageId, PageRevisionId = page.RevisionId, page.Bounds,
            Layers = page.Layers.Select(layer => new { layer.LayerId, layer.Name, layer.IsVisible, layer.IsLocked, layer.RevisionId }),
            Objects = page.Objects.Select(item => new
            {
                item.ObjectId, item.ObjectTypeId, item.LayerId, item.Geometry, item.Transform,
                Accessibility = new { item.Accessibility.Name, item.Accessibility.Description, item.Accessibility.AltText },
                item.RevisionId, item.SharedObjectRef,
                SemanticContentStatus = item.SharedObjectRef is null ? "Shared semantic content not exposed by this adapter" : "Linked-owner content requires separate permission",
            }),
            Strokes = page.Strokes.Select(stroke => new
            {
                stroke.StrokeId, stroke.LayerId, stroke.RevisionId, stroke.Transform,
                SampleBounds = new
                {
                    X = stroke.Samples.Min(sample => sample.X), Y = stroke.Samples.Min(sample => sample.Y),
                    Width = stroke.Samples.Max(sample => sample.X) - stroke.Samples.Min(sample => sample.X),
                    Height = stroke.Samples.Max(sample => sample.Y) - stroke.Samples.Min(sample => sample.Y),
                },
                SampleCount = stroke.Samples.Count,
                CanonicalRepresentation = "Structured ink; handwriting is not authoritative text",
            }),
            SelectedIds = selectionIds, target.CurrentTool,
            InkOptions = target.InkStyle is null ? null : new
            {
                EngineId = "rnote", Kind = target.InkStyle.Kind.ToString(),
                target.InkStyle.Color, target.InkStyle.BaseWidth, target.InkStyle.Opacity,
                Scope = "Transient editor preference; applies to new strokes only",
            },
        });
        var current = await currentTarget(cancellationToken).ConfigureAwait(false);
        if (current is null || current.FileId != target.FileId || current.FilesRevisionId != target.FilesRevisionId ||
            current.Artifact.ArtifactId != artifact.ArtifactId || current.Artifact.RevisionId != artifact.RevisionId ||
            current.ActivePageId != target.ActivePageId || current.CurrentTool != target.CurrentTool ||
            current.InkStyle != target.InkStyle ||
            current.HostAllowsWrites != target.HostAllowsWrites || !current.SelectedIds.Distinct().SequenceEqual(selectionIds) ||
            await authorization.AuthorizeAsync("canvas.file.open", [scope], cancellationToken).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Canvas target, revision or resource authority changed during context capture.");
        return new("canvas", "Canvas.Workspace", artifact.ArtifactId.ToString(),
            $"{artifact.DisplayName}: active page with {page.Objects.Count} objects and {page.Strokes.Count} structured strokes.",
            selectionIds.Length == 0 ? null : new("canvas-entities", $"{selectionIds.Length} selected", JsonSerializer.SerializeToElement(selectionIds)),
            new Dictionary<string, JsonElement> { ["Canvas"] = semantic }, AppAiDataSensitivity.UserContent,
            DateTimeOffset.UtcNow, artifact.RevisionId.ToString(), selectionIds.Select(id => id.ToString()).ToArray(),
            target.HostAllowsWrites ? "Editable; AI mutations require separate Home-brokered typed actions" : "ReadOnly");
    }
}
