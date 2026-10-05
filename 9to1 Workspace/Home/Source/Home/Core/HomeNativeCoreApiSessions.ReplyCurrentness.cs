using System.Runtime.ExceptionServices;

namespace HavenOS.Home.Core;

// Post-serialization identity/currentness fence only. This grants no action and never reads the socket.
public sealed partial class HomeNativeCoreApiSessions
{
    private async ValueTask DemandOriginalPublicationCurrentAsync(Context context, CancellationToken caller)
    {
        CancellationTokenSource? linked = null;
        bool entered = false;
        Exception? primary = null;
        List<Exception> cleanup = [];
        try
        {
            if (!_sessions.TryGetValue(context.Id, out var retained) || !ReferenceEquals(retained, context) || context.Closed)
                throw new UnauthorizedAccessException("The original Home reply session retired.");
            linked = CancellationTokenSource.CreateLinkedTokenSource(caller, context.Lifetime);
            await context.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
            entered = true;
            if (!_sessions.TryGetValue(context.Id, out retained) || !ReferenceEquals(retained, context) ||
                !await CurrentAsync(context, linked.Token).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The original Home actor, installation or lease retired before reply publication.");
        }
        catch (Exception error) { primary = error; }
        finally
        {
            if (entered) try { context.Gate.Release(); } catch (Exception error) { Add(cleanup, error, primary); }
            if (linked is not null) try { linked.Dispose(); } catch (Exception error) { Add(cleanup, error, primary); }
        }
        if (primary is not null && cleanup.Count == 0) ExceptionDispatchInfo.Capture(primary).Throw();
        if (primary is null && cleanup.Count == 1) ExceptionDispatchInfo.Capture(cleanup[0]).Throw();
        Throw(primary, cleanup);
    }

    public sealed partial class Session
    {
        // Home's owning transport calls this after bounded serialization, immediately before its original write.
        // The exact retained Context/Gate keeps every awaited actor/verifier read in issuer-wide drain custody.
        // Gate releases when this check returns. The transport must retain/drain its SAME write Task;
        // a later revocation may race with bytes already admitted to that write.
        internal ValueTask DemandOriginalCurrentAsync(CancellationToken token = default) =>
            _issuer.DemandOriginalPublicationCurrentAsync(_context, token);
    }
}

