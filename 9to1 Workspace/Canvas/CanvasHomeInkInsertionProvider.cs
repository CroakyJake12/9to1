using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Canvas;

/// <summary>
/// One explicitly prepared insertion, bound to an existing issuer-sealed Home capability.
/// The canonical Home transport must construct this owner only after approving the exact
/// CanvasStrokeWriteIntent. Registering this provider grants no additional authority.
/// </summary>
public sealed class CanvasHomeInkInsertionProvider(CanvasFilesArtifactBridge files, CanvasHomeStrokeOperation owner,
    CanvasStrokeWriteIntent intent, HomeResourceExecutionCapability capability) : IHomeProductivityArtifactInsertionProvider
{
    public string AppId => "canvas";
    public ValueTask<HomeProductivityActionResult> ApplyAsync(HomeProductivityContext context, HomeProductivityAction action,
        Func<HomeProductivityObject, HomeProductivityObject> sharedTransformation, CancellationToken cancellationToken)
        => ValueTask.FromResult(Reject(context, "InkUpdateUnavailable", "This owner supports the approved new stroke insertion; existing-stroke changes require their own native transaction."));

    public async ValueTask<HomeProductivityActionResult> InsertAsync(HomeProductivityContext context,
        IReadOnlyList<HomeProductivityObject> objects, string operationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.AppId != AppId || !Guid.TryParse(context.ArtifactId, out var artifactId) || artifactId != intent.ArtifactId ||
            context.CanonicalRevision != new HomeProductivityArtifactRevision(VersionId: intent.ExpectedArtifactRevision) ||
            !Guid.TryParse(operationId, out var operation) || operation != intent.OperationId || objects.Count != 1)
            return Reject(context, "InsertionBindingMismatch", "Insertion must match the approved Canvas artifact, GUID revision and single-stroke operation.");
        var value = objects[0];
        var opened = await files.OpenAsync(intent.FileId, cancellationToken).ConfigureAwait(false);
        if (opened.Artifact.ArtifactId != intent.ArtifactId || opened.Artifact.RevisionId != intent.ExpectedArtifactRevision || opened.CasRevisionId != intent.ExpectedFilesRevision)
            return Reject(context, "RevisionConflict", "The canonical Canvas changed before insertion.");
        if (opened.Artifact.Pages.SelectMany(page => page.Strokes).Any(stroke => stroke.StrokeId == intent.OperationId) ||
            opened.Artifact.Pages.SelectMany(page => page.Objects).Any(item => item.ObjectId == intent.OperationId))
            return Reject(context, "ObjectAlreadyExists", "Insertion cannot replace an existing canonical object.");
        var expected = new CanvasInkStroke
        {
            StrokeId = intent.OperationId, RevisionId = intent.OperationId,
            LayerId = opened.Artifact.Pages[0].LayerOrder[0],
            ToolDefinitionId = intent.Style.Kind == CanvasRnoteInkKind.Solid ? "pen" : "highlighter",
            Samples = intent.Samples.Select(sample => new CanvasStrokeSample(sample.X, sample.Y, sample.Pressure, sample.TiltX, sample.TiltY)).ToList(),
            ResolvedBrushProperties = intent.Style.BrushSnapshot()
        };
        if (value.ObjectId != expected.StrokeId || value.ObjectType != "drawing.ink" || value.SchemaVersion != 1 ||
            !Empty(value.Formatting) || !Empty(value.Layout) || !Empty(value.Accessibility) || value.AssetReferences.Count != 0 ||
            value.Extensions is { Count: > 0 } || !JsonElement.DeepEquals(value.Content, JsonSerializer.SerializeToElement(expected)))
            return Reject(context, "InsertionContentMismatch", "The supplied stroke must exactly match approved ink, target layer and supported properties; unsupported data is preserved by rejecting the insertion.");
        try
        {
            var committed = await owner.ExecuteAsync(intent, capability, cancellationToken).ConfigureAwait(false);
            return new(true, "Committed", "The canonical Canvas stroke is durably committed.", context.Revision, [committed.StrokeId])
            {
                ArtifactRevision = new(VersionId: committed.Artifact.RevisionId),
                Outcome = HomeProductivityArtifactOutcome.Committed
            };
        }
        catch (UnauthorizedAccessException)
        {
            return Reject(context, "PermissionDenied", "Current Home consent, identity or Canvas write authority denied insertion.");
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or OperationCanceledException)
        {
            // A save may have become durable before acknowledgement failed. Do not
            // manufacture an unchanged revision or promise rollback in this case.
            return new(false, "InsertionNeedsRecovery", "The insertion acknowledgement failed; reload the canonical Files revision before retrying.", context.Revision, [])
            { Outcome = HomeProductivityArtifactOutcome.NeedsRecovery };
        }
    }

    private static bool Empty(JsonElement value) => value.ValueKind == JsonValueKind.Object && !value.EnumerateObject().Any();
    private static HomeProductivityActionResult Reject(HomeProductivityContext context, string code, string message) =>
        new(false, code, message, context.Revision, []) { ArtifactRevision = context.CanonicalRevision, Outcome = HomeProductivityArtifactOutcome.Rejected };
}
