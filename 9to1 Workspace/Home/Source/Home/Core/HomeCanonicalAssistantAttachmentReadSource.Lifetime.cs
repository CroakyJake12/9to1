using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
namespace HavenOS.Home.Core;
public sealed partial class HomeCanonicalAssistantAttachmentReadSource
{
    private sealed partial class Read
    {
        public Task? OriginalClose { get { lock (_sync) return _close; } }
        public void DemandExternalOriginalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        internal bool ObserveHealthyClose()
        {
            Task? actual; lock (_sync) actual = _close;
            if (actual?.IsCompletedSuccessfully != true) return false;
            actual.GetAwaiter().GetResult(); return true;
        }
        internal Task<bool> Withdraw()
        {
            TaskCompletionSource? start = null; Task<bool> actual;
            lock (_sync)
            {
                if (_withdrawal is not null) return _withdrawal;
                var source = _withdrawalSources = Context(body => body(), _ => { }); _contexts.Add(source);
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                actual = _withdrawal = WithdrawBody(start.Task, source);
            }
            start.SetResult(); return actual;
        }
        private async Task<bool> WithdrawBody(Task start, HomeOwnershipOriginalSourceCallbacks source)
        {
            await start.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var prepared = await source.ReadAsync(() => _prepared.Task).ConfigureAwait(false);
            if (prepared is null) return false;
            if (!ReferenceEquals(prepared, _preparedTask) || _review is null)
                throw new InvalidOperationException("The actual attachment READ prepared-review source changed.");
            await source.ReadAsync(() => prepared).ConfigureAwait(false);
            lock (_sync) if (_capabilityStarting || _capability is not null || _declined is not null) return false;
            return await source.ReadAsync(() => owner._broker.WithdrawOriginalAttachmentReadWithinSourceAsync(
                _review, source.Run, source.Retain, CancellationToken.None)).ConfigureAwait(false);
        }
        private async Task DemandNotWithdrawn()
        {
            Task<bool>? raw; lock (_sync) raw = _withdrawal;
            if (raw is null) return;
            bool withdrawn;
            try { withdrawn = await raw.ConfigureAwait(false); }
            catch (Exception cause) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(raw.Exception ?? cause).Throw(); throw; }
            if (!withdrawn) return;
            if (_withdrawalSources is null || _withdrawalSources.Errors.Length != 0)
                throw new InvalidOperationException("The original attachment READ withdrawal has no healthy acknowledgment.");
            _declined ??= new UnauthorizedAccessException("The owning process withdrew this pending attachment READ before content access.");
            throw _declined;
        }
        public Task CloseAndDrainOriginalAsync()
        {
            DemandExternalOriginalJoin(); TaskCompletionSource? start = null; Task result;
            lock (_sync)
            {
                if (_close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = CloseBody(start.Task); }
                result = _close;
            }
            start?.SetResult(); return result;
        }
        public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
        private async Task CloseBody(Task start)
        {
            await start.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var errors = new List<Exception>(); Task? withdrawal = null;
            try { withdrawal = Withdraw(); await withdrawal.ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(withdrawal?.Exception ?? cause); }
            try { await Acquisition.ConfigureAwait(false); }
            catch (Exception cause) { if (!IsKnownRefusal(Acquisition)) errors.Add(Acquisition.Exception ?? cause); }
            Task[] operations; lock (_sync) operations = _operations.ToArray();
            foreach (var raw in operations)
                try { await raw.ConfigureAwait(false); } catch (Exception cause) { errors.Add(raw.Exception ?? cause); }
            HomeOwnershipOriginalSourceCallbacks[] contexts; lock (_sync) contexts = _contexts.ToArray();
            foreach (var source in contexts)
                foreach (var cause in source.Errors) if (!errors.Any(prior => ReferenceEquals(prior, cause))) errors.Add(cause);
            // Separate cleanup callback scope can release/audit already acquired Home
            // resources even when the original caller scope has faulted or retired.
            var cleanup = Context(body => body(), _ => { }); lock (_sync) _contexts.Add(cleanup);
            try
            {
                if (_capability is not null)
                {
                    HomePermissionOperationResult result;
                    if (_capability.IsUncompletedClaim(owner._broker))
                        result = await cleanup.ReadAsync(() => owner._broker.CompleteSetupExecutionWithinOriginalSourceAsync(_capability,
                            new(errors.Count == 0 ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed,
                                errors.Count == 0 ? "ATTACHMENT_READ_CLOSED" : "ATTACHMENT_READ_UNCONFIRMED",
                                "The original selected-file read has finished. Attachment import is separately reviewed.", []),
                            cleanup.Run, cleanup.Retain, CancellationToken.None)).ConfigureAwait(false);
                    else result = await cleanup.ReadAsync(() => owner._broker.AbortUnclaimedSetupExecutionWithinOriginalSourceAsync(_capability,
                        cleanup.Run, cleanup.Retain, CancellationToken.None)).ConfigureAwait(false);
                    if (!result.Succeeded) throw new InvalidOperationException("The actual attachment READ completion was not acknowledged: " + result.Code);
                }
                else if (_review is not null && !IsKnownRefusal(Acquisition) &&
                    !(_withdrawal?.IsCompletedSuccessfully == true && _withdrawal.GetAwaiter().GetResult()))
                {
                    var retired = await cleanup.ReadAsync(() => owner._broker.RetireSetupPreparedReviewWithinOriginalSourceAsync(_review,
                        cleanup.Run, cleanup.Retain, CancellationToken.None)).ConfigureAwait(false);
                    if (!retired) throw new InvalidOperationException("The actual attachment READ review is still unresolved.");
                }
            }
            catch (Exception cause) { errors.Add(cause); }
            foreach (var cause in cleanup.Errors) if (!errors.Any(prior => ReferenceEquals(prior, cause))) errors.Add(cause);
            if (errors.Count != 0) throw new AggregateException("The original attachment READ or its independent completion did not close healthy.", errors);
        }
    }
}
