namespace HavenOS.Apps.Assistants.Memory;

public sealed partial class AssistantMemoryManagementController : IAssistantMemoryWriteObservationSource
{
    public AssistantMemoryWriteObservation ObserveOriginalWrite(AssistantMemoryWritePreview samePreview)
    {
        ArgumentNullException.ThrowIfNull(samePreview);
        if (!ReferenceEquals(samePreview.Issuer, _issuer))
            throw new UnauthorizedAccessException("Observe this presentation's SAME issued memory preview.");
        return _source.ObserveOriginalWriteIntent(samePreview.Intent);
    }
}
