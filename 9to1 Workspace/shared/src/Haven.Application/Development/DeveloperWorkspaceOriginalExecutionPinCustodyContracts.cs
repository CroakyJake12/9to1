namespace Haven.Application;

/// <summary>Optional historical cleanup recognition on the SAME configured pin issuer.
/// This callback-free query recognizes only its retained private binding/pin references,
/// including after retirement or close. It does not authorize use, establish currentness,
/// admit a copied binding, or certify cleanup. Live IsIssued and Demand remain mandatory
/// before effects; the actual returned close Task must still be independently joined.</summary>
public interface IDeveloperWorkspaceOriginalExecutionPinCustodySource
{
    bool IsOwnedOriginalExecutionPin(IDeveloperWorkspaceOriginalExecutionBinding sameBinding,
        IDeveloperWorkspaceOriginalExecutionCommitPin samePin);
}
