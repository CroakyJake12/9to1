namespace HavenOS.Images;

public enum PictureCompositionTargetKind { Layer, Vector }

/// <summary>Opaque selection issued by the same current Picture session. This
/// identity/revision receipt does not grant storage, Home or project access.</summary>
public sealed class PictureCompositionTarget
{
    internal PictureEditorSession Owner { get; }
    public Guid DocumentId { get; }
    public long DocumentRevision { get; }
    public Guid GraphRevision { get; }
    public Guid TargetId { get; }
    public PictureCompositionTargetKind Kind { get; }
    internal PictureCompositionTarget(PictureEditorSession owner, Guid documentId, long documentRevision,
        Guid graphRevision, Guid targetId, PictureCompositionTargetKind kind)
    { Owner = owner; DocumentId = documentId; DocumentRevision = documentRevision;
      GraphRevision = graphRevision; TargetId = targetId; Kind = kind; }
}

/// <summary>Exact path selection within the original source-issued vector.</summary>
public sealed class PictureVectorPathTarget
{
    public PictureCompositionTarget Vector { get; }
    public Guid PathId { get; }
    internal PictureVectorPathTarget(PictureCompositionTarget vector, Guid pathId) { Vector = vector; PathId = pathId; }
}

/// <summary>Exact retained node within a canonical path and subpath. Issued by
/// the current Picture owner, never a free-form identifier or permission grant.</summary>
public sealed class PictureVectorNodeTarget
{
    public PictureCompositionTarget Vector { get; }
    public Guid PathId { get; }
    public Guid SubpathId { get; }
    public Guid NodeId { get; }
    internal PictureVectorNodeTarget(PictureCompositionTarget vector, Guid pathId, Guid subpathId, Guid nodeId)
    { Vector = vector; PathId = pathId; SubpathId = subpathId; NodeId = nodeId; }
}
