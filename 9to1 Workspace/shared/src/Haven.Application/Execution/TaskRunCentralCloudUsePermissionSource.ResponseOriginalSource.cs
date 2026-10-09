using System.Runtime.ExceptionServices;
namespace Haven.Application;

public sealed partial class TaskRunCentralCloudUsePermissionSource
{
    private sealed partial class Lease : ITaskRunOriginalScopedCloudUsePermissionLease
    {
        public async ValueTask RevalidateWithinOriginalSourceAsync(Action<Action> originalSynchronousScope,
            Action<Task> retainOriginalTask, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
            lock (_sync) ObjectDisposedException.ThrowIf(_closed, this);
            var sources = new TaskRunOriginalResponseSourceCallbacks(originalSynchronousScope, retainOriginalTask);
            Exception? primary = null;
            try
            {
                var current = await sources.ReadAsync(() => _source._actors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false);
                var expected = new AuthenticatedResourceActor(_owner.ActorId, _owner.ProfileId,
                    _owner.AccountId, _owner.OrganisationId, _owner.AuthenticationRevision);
                if (current != expected) throw new UnauthorizedAccessException("The actual task actor changed before remote revalidation.");
                sources.Invoke(() => { _source.DemandPermission(Scope, _reason, _owner, _candidate); return 0; });
                token.ThrowIfCancellationRequested();
                lock (_sync) ObjectDisposedException.ThrowIf(_closed, this);
            }
            catch (Exception error) { primary = error; sources.Add(error); }
            finally
            {
                foreach (var raw in sources.Originals.ToArray())
                    try { await raw.ConfigureAwait(false); } catch (Exception error) { sources.AddTask(raw, error); }
            }
            if (primary is OperationCanceledException && !sources.Originals.Any(raw => raw.IsFaulted) &&
                sources.Errors.All(error => error is OperationCanceledException))
                ExceptionDispatchInfo.Capture(primary).Throw();
            sources.Throw();
        }
    }
}
