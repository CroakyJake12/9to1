using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

public enum HomeUcmlAccessOutcome { BrokerAllowed, DispatchValidated, Exported, Denied }

/// <summary>Content-free UCML access observations in the existing Home permission audit.
/// Composition and original input identity are checked; an audit event grants no access.</summary>
public sealed class HomeUcmlAccessAuditOwner
{
    private readonly HomePermissionTrustService _permissions;
    private readonly IChatOriginalPersistentMemorySource _memory;
    internal HomeLocalProfileIdentity Profiles { get; }
    public HomeUcmlAccessAuditOwner(IHomeCoreStateStore actualState, HomeLocalProfileIdentity actualProfiles,
        HomePermissionTrustService actualPermissions, IChatOriginalPersistentMemorySource actualMemory)
    {
        ArgumentNullException.ThrowIfNull(actualState); ArgumentNullException.ThrowIfNull(actualProfiles);
        ArgumentNullException.ThrowIfNull(actualPermissions); ArgumentNullException.ThrowIfNull(actualMemory);
        if (!actualProfiles.IsBoundToStore(actualState) || !actualPermissions.IsBoundToStore(actualState))
            throw new UnauthorizedAccessException("UCML access audit requires the SAME Home state, profile and permission owner.");
        Profiles = actualProfiles; _permissions = actualPermissions; _memory = actualMemory;
    }
    public bool HasOriginalComposition(IChatOriginalPersistentMemorySource memory, HomeLocalProfileIdentity profiles) =>
        ReferenceEquals(memory, _memory) && ReferenceEquals(profiles, Profiles);
    internal bool HasOriginalPermissionOwner(HomePermissionTrustService permissions) => ReferenceEquals(_permissions, permissions);
    internal void DemandOriginal(IChatOriginalPersistentMemorySource memory, IChatOriginalPersistentMemoryInput? input,
        HomeUcmlAccessOutcome outcome, IReadOnlyList<Guid> records)
    {
        if (!ReferenceEquals(memory, _memory) || !Enum.IsDefined(outcome) || records.Count > 8 || records.Any(id => id == Guid.Empty) ||
            records.Distinct().Count() != records.Count || outcome == HomeUcmlAccessOutcome.Denied && records.Count != 0 ||
            (input is null ? outcome != HomeUcmlAccessOutcome.Denied : !_memory.IsIssuedOriginalInput(input)))
            throw new UnauthorizedAccessException("Only the SAME original UCML source may record bounded content-free access observations.");
    }
    public Task RecordWithinOriginalSourceAsync(IChatOriginalPersistentMemorySource memory,
        IChatOriginalPersistentMemoryInput? input, AuthenticatedResourceActor? expectedActor,
        Guid requestId, HomeUcmlAccessOutcome outcome, IReadOnlyList<Guid> records,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var frozen = Array.AsReadOnly(records.ToArray());
        DemandOriginal(memory, input, outcome, frozen);
        if (requestId == Guid.Empty || input is not null && expectedActor is null)
            throw new UnauthorizedAccessException("An original request and authenticated actor observation are required.");
        return _permissions.RecordOriginalUcmlAccessWithinSourceAsync(this, memory, input,
            expectedActor, requestId, outcome, frozen, scope, retain, token);
    }
}
