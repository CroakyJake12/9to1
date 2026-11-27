using Haven.Core;

namespace Haven.Application;

/// <summary>Atomic presentation ordering over the existing canonical app pins. A concurrent pin,
/// unpin or reorder refuses the stale snapshot; it never replaces someone else's membership.</summary>
public interface IOrderedPinRepository : IPinRepository
{
    Task<bool> TryReorderAsync(IReadOnlyList<ModePin> expected, IReadOnlyList<Guid> orderedModeIds,
        CancellationToken cancellationToken, Func<CancellationToken, Task>? admitCommit = null);
    Task<bool> TrySetPinnedAsync(IReadOnlyList<ModePin> expected, Guid modeId, bool pinned,
        CancellationToken cancellationToken, Func<CancellationToken, Task>? admitCommit = null);
}
