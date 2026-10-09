using Haven.Application;
using System.Diagnostics.CodeAnalysis;

namespace HavenOS.Home.Core;

public sealed partial class HomeColdProjectReadReconciliation
{
    /// <summary>Historical SAME-issuer cleanup observation for releasing caller custody.
    /// Creates no input, resource permission, restored attempt or execution grant.
    /// Pending/failed/foreign close tasks never qualify, including a successfully
    /// completed caller projection which is not this input's actual cached close.</summary>
    public bool IsClosedOwnedOriginalProjectInput(ITaskRunColdOriginalProjectInput input, Task sameOriginalClose)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(sameOriginalClose);
        Work actual;
        lock (_gate)
        {
            if (input is not Work candidate || candidate.Material is not null || candidate._command is not null ||
                !_issued.TryGetValue(candidate, out _)) return false;
            actual = candidate;
        }
        return ReferenceEquals(actual.ObserveSuccessfullySettledOriginalInputClose(), sameOriginalClose);
    }

    /// <summary>Reveals only an already successful SAME cached original close.
    /// This never invokes close to discover whether canonical business closed it.</summary>
    public bool TryObserveClosedOwnedOriginalProjectInput(ITaskRunColdOriginalProjectInput input,
        [NotNullWhen(true)] out Task? sameActualOriginalClose)
    {
        ArgumentNullException.ThrowIfNull(input); sameActualOriginalClose = null;
        Work actual;
        lock (_gate)
        {
            if (input is not Work candidate || candidate.Material is not null || candidate._command is not null ||
                !_issued.TryGetValue(candidate, out _)) return false;
            actual = candidate;
        }
        sameActualOriginalClose = actual.ObserveSuccessfullySettledOriginalInputClose();
        return sameActualOriginalClose is not null;
    }

    private sealed partial class Work
    {
        internal Task? ObserveSuccessfullySettledOriginalInputClose()
        {
            lock (_gate)
                return _close?.IsCompletedSuccessfully == true &&
                    Driver?.IsCompletedSuccessfully == true && _validations.All(raw => raw.IsCompletedSuccessfully) &&
                    _finiteCalls.All(raw => raw.IsCompletedSuccessfully) && _contexts.All(context => context.SuccessfullySettled)
                    ? _close : null;
        }
    }
}
