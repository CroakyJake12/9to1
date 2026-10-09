using HavenOS.Home.Core;

namespace HavenOS.Home.PermissionsTrustNotifications;

public sealed partial class HomePermissionTrustService
{
    private readonly object _originalImportPermissionGate = new();
    private readonly List<(Task Actual, HomeOwnershipOriginalSourceCallbacks Source)> _originalImportPermissionSources = [];

    // The SAME permission bodies; the actual store read/write and gate acquisition
    // receive custody. Neither a page nor its callback can supply an action policy.
    public Task<HomePermissionAuthorization> AuthorizeImportWithinOriginalSourceAsync(
        HomePermissionRequestSubmission submission, Action<Action> scope, Action<Task> retain, CancellationToken token, Action<Exception>? retainUnexpectedCallback = null) =>
        RunOriginalImportPermissionAsync(scope, retain, source => AuthorizeOriginalImportCoreAsync(submission, token, source), retainUnexpectedCallback);
    public Task<HomePermissionRequest?> ReadImportRequestWithinOriginalSourceAsync(
        string requestId, Action<Action> scope, Action<Task> retain, CancellationToken token, Action<Exception>? retainUnexpectedCallback = null) =>
        RunOriginalImportPermissionAsync(scope, retain, source => ReadRequestObservationOriginalImportCoreAsync(requestId, token, source), retainUnexpectedCallback);
    public Task<HomePermissionAuthorization> GetImportAuthorizationWithinOriginalSourceAsync(
        string requestId, Action<Action> scope, Action<Task> retain, CancellationToken token, Action<Exception>? retainUnexpectedCallback = null) =>
        RunOriginalImportPermissionAsync(scope, retain, source => GetAuthorizationOriginalImportCoreAsync(requestId, token, source), retainUnexpectedCallback);
    public Task<HomePermissionAuthorization> BeginImportExecutionWithinOriginalSourceAsync(
        string requestId, Action<Action> scope, Action<Task> retain, CancellationToken token, Action<Exception>? retainUnexpectedCallback = null) =>
        RunOriginalImportPermissionAsync(scope, retain, source => BeginExecutionOriginalImportCoreAsync(requestId, token, source), retainUnexpectedCallback);
    public Task<HomePermissionOperationResult> RecordImportExecutionWithinOriginalSourceAsync(
        string requestId, HomeExecutionOutcome outcome, Action<Action> scope, Action<Task> retain, CancellationToken token, Action<Exception>? retainUnexpectedCallback = null) =>
        RunOriginalImportPermissionAsync(scope, retain, source => RecordExecutionOriginalImportCoreAsync(requestId, outcome, token, source), retainUnexpectedCallback);

    private Task<T> RunOriginalImportPermissionAsync<T>(Action<Action> scope, Action<Task> retain,
        Func<HomeOwnershipOriginalSourceCallbacks, Task<T>> body, Action<Exception>? retainUnexpectedCallback = null)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var source = new HomeOwnershipOriginalSourceCallbacks(scope, retain, retainUnexpectedCallback);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<T> actual;
        lock (_originalImportPermissionGate)
        {
            _originalImportPermissionSources.RemoveAll(value =>
            {
                var sameActual = value.Actual;
                if (!sameActual.IsCompletedSuccessfully) return false;
                sameActual.GetAwaiter().GetResult();
                return retainUnexpectedCallback is null ? value.Source.Errors.Length == 0 : value.Source.HasIndependentlyJoinedHealthySources;
            });
            if (_originalImportPermissionSources.Count >= 256)
                throw new InvalidOperationException("Actual unresolved import permission sources remain retained.");
            actual = Drive(start.Task);
            _originalImportPermissionSources.Add((actual, source)); // Before any borrowed callback.
        }
        try { source.Run(() =>
        {
            try { retain(actual); }
            catch (Exception cause) { retainUnexpectedCallback?.Invoke(cause); throw; }
        }); }
        catch { /* Source owns the exact callback failure; the accepted driver reports it. */ }
        finally { start.SetResult(); }
        return actual;
        async Task<T> Drive(Task gate)
        {
            await gate.ConfigureAwait(false);
            if (source.Errors.Length != 0) throw new AggregateException("Actual permission source publication failed.", source.Errors);
            return await body(source).ConfigureAwait(false);
        }
    }

    private async Task WaitOriginalImportPermissionGateAsync(HomeOwnershipOriginalSourceCallbacks? source, CancellationToken token)
    {
        if (source is null) { await _gate.WaitAsync(token).ConfigureAwait(false); return; }
        Task? actual = null;
        try
        {
            await source.ReadAsync(async () =>
            {
                actual = _gate.WaitAsync(token); source.Retain(actual);
                await actual.ConfigureAwait(false); return true;
            }).ConfigureAwait(false);
        }
        catch (Exception primary)
        {
            // A post-callback failure can follow actual semaphore acquisition. Join
            // that single accepted acquisition, then release it; do not reacquire.
            Exception? cleanup = null;
            if (actual?.IsCompletedSuccessfully == true)
                try { source.Run(() => _gate.Release()); } catch (Exception cause) { cleanup = cause; }
            if (cleanup is not null && !ReferenceEquals(cleanup, primary))
                throw new AggregateException("Actual Home permission acquisition and release failed.", primary, cleanup);
            throw;
        }
    }
}
