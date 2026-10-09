namespace Haven.Application;

/// <summary>Optional pure receipt projection on the SAME root-configured canonical
/// creation producer. It recognizes only its private original intent. Null is valid
/// only for ordinary creation without a Den revision selection. A resumed selection
/// must return its SAME actual Home Den receipt with matching Den ID/current actor.
/// This historical receipt grants no WRITE and replaces no current held-state check.</summary>
public interface ICanonicalProjectTaskContextOriginalDenRevisionOwner
{
    VerifiedResourceStoreOwnership? GetOriginalCreationDenOwnership(
        ICanonicalProjectTaskContextCreationIntent sameOriginalIntent);
}
