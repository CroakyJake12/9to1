using HavenOS.Files;

namespace HavenOS.Apps.Canvas;

/// <summary>Exact displayed Files/artifact revision plus a trusted host's operation-request port.
/// No actor or capability is accepted here; the actual Home owner must authorise and commit each request.</summary>
public sealed record CanvasNativeInkContext(HostedItemId FileId, FilesRevisionId FilesRevision,
    Guid ArtifactId, Guid ArtifactRevision, Func<bool> IsAvailable,
    Func<CanvasStrokeWriteIntent, CancellationToken, Task> RequestOperation,Guid ExpectedStoreId=default);
