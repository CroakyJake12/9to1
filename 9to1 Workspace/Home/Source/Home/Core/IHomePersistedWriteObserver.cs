namespace HavenOS.Home.Core;

/// <summary>Internal actual-persistence fixture observer. Called only after successful physical
/// publication and after FileHome locks are released, before the write result reaches its owner.
/// Gets descriptive ID/revision only; never an actor, permission, outcome grant or alternate store.
/// Throwing simulates loss of the genuine publication acknowledgement. Caller-owned service locks
/// may remain held: do not reenter the same permission/owner operation from this callback.</summary>
internal interface IHomePersistedWriteObserver
{
    Task OnPersistedWriteAsync(string recordId, long actualRecordRevision, CancellationToken cancellationToken);
}
