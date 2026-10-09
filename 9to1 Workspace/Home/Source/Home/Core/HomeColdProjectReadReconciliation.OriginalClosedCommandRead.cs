using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeColdProjectReadReconciliation
{
    /// <summary>Historical custody only. This recognizes the SAME privately issued
    /// command Work and its cached owning close, including preparation, validations,
    /// finite calls, source contexts and original native close proofs. It performs no
    /// IO, admission, disposal probe, reopen or resource/Dev effect.</summary>
    public bool IsClosedOwnedOriginalCommandRead(IDeveloperOriginalProjectCommandRead sameCommand,
        Task sameActualOriginalClose)
    {
        ArgumentNullException.ThrowIfNull(sameActualOriginalClose);
        Work original;
        lock (_gate)
        {
            if (sameCommand is not CommandRead actual || !ReferenceEquals(actual.Owner, this) ||
                !ReferenceEquals(actual.Original._command, actual) || !_issued.TryGetValue(actual.Original, out _)) return false;
            original = actual.Original;
        }
        // Historical issuer reference is retained; no owner lock spans Work cleanup.
        return original.IsClosedOwnedCommandCohort(sameActualOriginalClose);
    }

    private sealed partial class Work
    {
        internal bool IsClosedOwnedCommandCohort(Task sameActualClose)
        {
            lock (_gate) return _command is not null && !_commandFailed && ReferenceEquals(_close, sameActualClose) &&
                CanPruneSuccessfulClose && _commandNatives.All(value => value.Capture.IsCompletedSuccessfully);
        }
    }
}
