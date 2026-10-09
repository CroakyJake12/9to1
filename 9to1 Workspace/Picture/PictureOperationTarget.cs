namespace HavenOS.Images;

/// <summary>A bounded semantic selection issued by the same editing session.
/// This is neither a file permission nor authority to access a different document.</summary>
public sealed class PictureOperationTarget
{
    internal PictureOperationTarget(PictureEditorSession owner, Guid documentId, long revision, int index,
        PictureOperation operation)
    {
        Owner = owner; DocumentId = documentId; Revision = revision; Index = index; Operation = operation;
    }

    internal PictureEditorSession Owner { get; }
    public Guid DocumentId { get; }
    public long Revision { get; }
    public int Index { get; }
    public PictureOperation Operation { get; }
}
