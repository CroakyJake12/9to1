using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

// Additive trusted Agent binding to the SAME private accepted Core connection.
// No socket reader, held lease acquisition, installation DTO or second connection registry.
public sealed partial class HomeNativeCoreApiSessions
{
    internal async Task<AgentConnection> BindOriginalAgentAsync(Session sameSession, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(sameSession);
        return await sameSession.BindAgentAsync(this, token).ConfigureAwait(false);
    }

    private async Task<AgentConnection> BindAgentContextAsync(Context sameContext, CancellationToken token)
    {
        if (!_sessions.TryGetValue(sameContext.Id, out var held) || !ReferenceEquals(held, sameContext) ||
            !held.Peer.AllowedServiceIds.Contains("home.agent") || !await CurrentAsync(held, token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("This same installed connection did not declare live Home Agent execution.");
        return new AgentConnection(this, held);
    }
    public sealed partial class Session
    {
        internal Task<AgentConnection> BindAgentAsync(HomeNativeCoreApiSessions expectedOwner, CancellationToken token) =>
            ReferenceEquals(expectedOwner, _issuer) ? _issuer.BindAgentContextAsync(_context, token) :
                Task.FromException<AgentConnection>(new UnauthorizedAccessException("The Core session belongs to another issuer."));
    }

    internal sealed class AgentConnection
    {
        private readonly HomeNativeCoreApiSessions _owner;
        private readonly Context _original;
        internal AgentConnection(HomeNativeCoreApiSessions owner, object original)
        { _owner = owner; _original = original as Context ?? throw new UnauthorizedAccessException("An original private Core context is required."); }
        internal HomeNativeSessionLease Lease => _original.Lease;
        internal CancellationToken Lifetime => _original.Lifetime;
        internal AuthenticatedResourceActor Actor => _original.Actor;
        internal HomePermissionCallerIdentity Caller => _original.PermissionCaller;
        internal async Task DemandCurrentAsync(CancellationToken token)
        {
            if (!_owner._sessions.TryGetValue(_original.Id, out var held) || !ReferenceEquals(held, _original) ||
                !_original.Peer.AllowedServiceIds.Contains("home.agent") ||
                !await _owner.CurrentAsync(_original, token).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The same installed Agent connection, actor or held Home lease retired.");
        }
        internal async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken caller)
        {
            List<Exception> errors = [];
            CancellationTokenSource? linked = null;
            bool entered = false;
            T? result = default;
            try
            {
                linked = CancellationTokenSource.CreateLinkedTokenSource(caller, Lifetime);
                await _original.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
                entered = true;
                await DemandCurrentAsync(linked.Token).ConfigureAwait(false);
                result = await action(linked.Token).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                Exception retained = error;
                // Normalize only this exact inner linked cancellation to the supplied original
                // caller token when both are requested. The exact inner failure remains the cause.
                if (error is OperationCanceledException cancelled && linked is not null &&
                    caller.IsCancellationRequested && linked.IsCancellationRequested &&
                    cancelled.CancellationToken == linked.Token)
                    retained = new OperationCanceledException(cancelled.Message, cancelled, caller);
                Add(errors, retained, null);
            }
            finally
            {
                if (entered) try { _original.Gate.Release(); } catch (Exception error) { Add(errors, error, null); }
                if (linked is not null) try { linked.Dispose(); } catch (Exception error) { Add(errors, error, null); }
            }
            // A single original failure must keep its shape and identity for the owning drain.
            if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            Throw(null, errors);
            return result!;
        }
    }
}
