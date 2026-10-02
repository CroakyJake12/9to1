using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Canvas;

public sealed record CanvasCreationTarget(HostedItemId FolderId, FilesRevisionId? ExpectedFolderRevision)
{
    public Guid ExpectedStoreId { get; init; }
}

/// <summary>Captured new document and configured destination, never caller-supplied actor authority.</summary>
public sealed class CanvasCreateIntent
{
    private readonly byte[] _artifact;
    private readonly JsonElement _arguments;
    private CanvasCreateIntent(CanvasArtifact artifact, byte[] bytes, CanvasCreationTarget target)
    {
        _artifact = bytes; Target = target; ArtifactId = artifact.ArtifactId;
        _arguments = JsonSerializer.SerializeToElement(new
        {
            operation = "artifact.create", artifactId = artifact.ArtifactId, revisionId = artifact.RevisionId,
            displayName = artifact.DisplayName, canvasMode = artifact.CanvasMode.ToString(),
            folderId = target.FolderId.Value, expectedStoreId = target.ExpectedStoreId, expectedFolderRevision = target.ExpectedFolderRevision?.Value,
            contentHash = Convert.ToHexString(SHA256.HashData(bytes)), sizeBytes = bytes.Length
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Scopes = Array.AsReadOnly(new[] { new ResourceScope("files.item", target.FolderId.ToString(),
            target.ExpectedFolderRevision?.ToString() ?? "uncommitted", ResourceAccess.Write) });
    }
    public const string TargetAppId = "canvas";
    public const string ActionId = "canvas.file.create";
    public Guid ArtifactId { get; }
    public CanvasCreationTarget Target { get; }
    public JsonElement Arguments => _arguments.Clone();
    public IReadOnlyList<ResourceScope> Scopes { get; }
    internal CanvasArtifact ReadCapturedArtifact() => CanvasArtifactCodec.Deserialize(_artifact);
    public static CanvasCreateIntent Capture(CanvasArtifact artifact, CanvasCreationTarget target)
    {
        ArgumentNullException.ThrowIfNull(artifact); ArgumentNullException.ThrowIfNull(target);
        if (target.ExpectedStoreId == Guid.Empty || target.FolderId.Value == Guid.Empty || target.ExpectedFolderRevision?.Value == Guid.Empty)
            throw new ArgumentException("Canvas creation requires the exact configured destination revision.");
        var bytes = CanvasArtifactCodec.Serialize(artifact);
        var captured = CanvasArtifactCodec.Deserialize(bytes);
        return new(captured, bytes, target);
    }
}

public sealed record CanvasHomeCreateCommit(HostedItemId FileId, CanvasArtifact Artifact, FilesRevision FilesRevision);

public sealed class CanvasHomeCreateOperation(CanvasFilesArtifactBridge files, HomeResourceOperationBroker home,
    IAuthenticatedResourceActorSource actors)
{
    public async Task<CanvasHomeCreateCommit> ExecuteAsync(CanvasCreateIntent intent,
        HomeResourceExecutionCapability capability, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No current Home actor is available.");
        var artifact = intent.ReadCapturedArtifact();
        var claimed = await home.ClaimExecutionAsync(capability, CanvasCreateIntent.TargetAppId,
            CanvasCreateIntent.ActionId, intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false);
        if (claimed is null || claimed != actor)
            throw new UnauthorizedAccessException("Home did not grant this exact Canvas creation.");
        var committed = await files.CreateAsync(artifact, intent.Target, claimed, cancellationToken).ConfigureAwait(false);
        return new(committed.FileId, artifact, committed.Revision);
    }
}
