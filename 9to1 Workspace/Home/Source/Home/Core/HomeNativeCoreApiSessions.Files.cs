using System.Runtime.ExceptionServices;
namespace HavenOS.Home.Core;
public sealed partial class HomeNativeCoreApiSessions
{
    private readonly Lazy<IHomeNativeFilesDomainOwner?> _originalFilesOwner;
    public IHomeCoreService? CreateOriginalFilesCoreService() =>
        _originalFilesOwner.Value is { } original ? new HomeNativeFilesCoreService(original) : null;
    private async Task<HomeNativeFilesReply> InvokeOriginalFilesAsync(Context context,
        HomeNativeFilesRequest request, CancellationToken caller)
    {
        HomeNativeFilesProtocol.Validate(request);
        var captured = request with { };
        CancellationTokenSource? linked = null;
        var entered = false;
        HomeNativeFilesReply? reply = null;
        Exception? primary = null;
        List<Exception> cleanup = [];
        try
        {
            DemandIssued(context);
            linked = CancellationTokenSource.CreateLinkedTokenSource(caller, context.Lifetime);
            await context.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
            entered = true;
            DemandIssued(context);
            if (!await CurrentAsync(context, linked.Token).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The original installed Files connection retired.");
            if (context.Peer.AppId != HomeNativeFilesActionPolicies.TargetAppId ||
                !context.Peer.AllowedServiceIds.Contains(HomeNativeFilesActionPolicies.RequiredInstalledServiceId))
                reply = new("Denied", "FilesServiceUndeclared", "The original installed app did not declare the Files domain service.");
            else if (_originalFilesOwner.Value is not { } originalOwner)
                reply = new("Unavailable", "FilesDomainUnavailable", "The canonical Home Files owner is not configured.");
            else
            {
                context.OriginalFilesOwner ??= originalOwner;
                if (!ReferenceEquals(context.OriginalFilesOwner, originalOwner))
                    throw new UnauthorizedAccessException("The original Files owner changed.");
                context.OriginalFilesConnection ??= new(this, context, context.Actor, context.Peer,
                    context.SessionId, context.Lifetime);
                var originalConnection = context.OriginalFilesConnection;
                if (!originalConnection.IsOriginalIssuer(this) || !originalConnection.IsOriginalContext(context))
                    throw new UnauthorizedAccessException("The original Files connection pairing changed.");
                reply = await originalOwner.InvokeOriginalAsync(originalConnection, captured, linked.Token).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The original Files owner returned no reply.");
            }
            if (!await CurrentAsync(context, linked.Token).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The original installed Files caller changed during dispatch.");
            linked.Token.ThrowIfCancellationRequested();
            context.OriginalFilesReply = reply;
        }
        catch (Exception error) { primary = error; }
        finally
        {
            if (entered) try { context.Gate.Release(); } catch (Exception error) { Add(cleanup, error, primary); }
            if (linked is not null) try { linked.Dispose(); } catch (Exception error) { Add(cleanup, error, primary); }
        }
        ThrowFiles(primary, cleanup);
        return reply!;
    }
    private async ValueTask DemandOriginalFilesReplyCurrentAsync(Context context,
        HomeNativeFilesReply originalReply, CancellationToken caller)
    {
        CancellationTokenSource? linked = null;
        var entered = false;
        Exception? primary = null;
        List<Exception> cleanup = [];
        try
        {
            DemandIssued(context);
            linked = CancellationTokenSource.CreateLinkedTokenSource(caller, context.Lifetime);
            await context.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
            entered = true;
            DemandIssued(context);
            if (!ReferenceEquals(context.OriginalFilesReply, originalReply) ||
                !await CurrentAsync(context, linked.Token).ConfigureAwait(false))
                throw new UnauthorizedAccessException("Retain this original Files reply and installed connection.");
            if (context.OriginalFilesOwner is { } owner && context.OriginalFilesConnection is { } connection)
                await owner.DemandOriginalReplyCurrentAsync(connection, originalReply, linked.Token).ConfigureAwait(false);
            else if (originalReply.Page is not null || originalReply.State is not ("Unavailable" or "Denied"))
                throw new UnauthorizedAccessException("An unavailable owner cannot issue a Files page.");
            DemandIssued(context);
            linked.Token.ThrowIfCancellationRequested();
        }
        catch (Exception error) { primary = error; }
        finally
        {
            if (entered) try { context.Gate.Release(); } catch (Exception error) { Add(cleanup, error, primary); }
            if (linked is not null) try { linked.Dispose(); } catch (Exception error) { Add(cleanup, error, primary); }
        }
        ThrowFiles(primary, cleanup);
    }
    private void DemandIssued(Context context)
    {
        if (Volatile.Read(ref _closing) || context.Closed || context.Lifetime.IsCancellationRequested ||
            !context.Lease.IsHeld || !_sessions.TryGetValue(context.Id, out var original) || !ReferenceEquals(original, context))
            throw new UnauthorizedAccessException("The original Home Files session retired.");
    }
    private static void ThrowFiles(Exception? primary, List<Exception> cleanup)
    {
        if (primary is null && cleanup.Count == 1) ExceptionDispatchInfo.Capture(cleanup[0]).Throw();
        Throw(primary, cleanup);
    }
    public sealed partial class Session
    {
        public Task<HomeNativeFilesReply> InvokeOriginalFilesAsync(HomeNativeFilesRequest originalRequest,
            CancellationToken token = default) => _issuer.InvokeOriginalFilesAsync(_context, originalRequest, token);
        internal ValueTask DemandOriginalFilesReplyCurrentAsync(HomeNativeFilesReply originalReply,
            CancellationToken token) => _issuer.DemandOriginalFilesReplyCurrentAsync(_context, originalReply, token);
    }
}
