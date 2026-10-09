namespace Haven.Application;

/// <summary>Actual platform observation under the borrower's finite source scope.
/// Retains each accepted raw OS/profile task before freshness callbacks can refuse it.
/// Observations remain entrypoint locators, not installation or execution grants.</summary>
public interface IInstalledApplicationOriginalScopedObservationProvider : IInstalledApplicationObservationProvider
{
    ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveWithinOriginalSourceAsync(
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken cancellationToken);
}
