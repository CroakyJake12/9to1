using System.Runtime.ExceptionServices;
namespace HavenOS.Home.Core;
public sealed partial class HomeNativeCoreApiSessions
{
    private readonly Lazy<IHomeNativeFilesDomainOwner?> _originalFilesOwner;
    public IHomeCoreService? CreateOriginalFilesCoreService() =>
        _originalFilesOwner.Value is { } original ? new HomeNativeFilesCoreService(original,
            _verifier is IHomeNativeFilesInstalledPublicationVerifier) : null;
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
            else if (_verifier is not IHomeNativeFilesInstalledPublicationVerifier ||
                originalOwner is not IHomeNativeFilesPublicationOwner { SupportsOriginalPublication: true })
                reply = new("Unavailable", "FilesPublicationGuardUnavailable",
                    "Genuine retained installed-caller and Files read publication guards are not configured.");
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
    private async Task PublishOriginalFilesReplyAsync(Context context, HomeNativeFilesReply originalReply,
        Func<CancellationToken, Task> originalPhysicalWrite, CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(originalPhysicalWrite);
        CancellationTokenSource? linked = null;
        IHomeNativeFilesPublicationGuard? installedGuard = null, ownerGuard = null;
        HomeNativeFilesOriginalPublicationContext? originalPublication = null;
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
            if (context.OriginalFilesOwner is { } originalOwner &&
                context.OriginalFilesConnection is { } originalConnection)
            {
                if (_verifier is not IHomeNativeFilesInstalledPublicationVerifier installed ||
                    originalOwner is not IHomeNativeFilesPublicationOwner { SupportsOriginalPublication: true } owner)
                    throw new UnauthorizedAccessException("The original Files publication authority is unavailable.");
                // All ordinary owner/current reads complete before either retained guard.
                await owner.DemandOriginalReplyCurrentAsync(originalConnection, originalReply, linked.Token).ConfigureAwait(false);
                // This ordinary issuer check completes BEFORE either guard; held owner callbacks may
                // have retired the actor/installed tuple. It is early refusal, not publication authority.
                if (!await CurrentAsync(context, linked.Token).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The original installed Files caller retired before acquisition.");
                DemandIssued(context);
                linked.Token.ThrowIfCancellationRequested();
                // Existing Files metadata writers enter Files before Home. Do not invert that order.
                ownerGuard = await owner.AcquireOriginalReplyPublicationAsync(originalConnection,
                    originalReply, linked.Token).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("No genuine Files publication transaction was retained.");
                originalPublication = new(this, context, originalConnection, originalReply, ownerGuard,
                    context.Observed, context.Peer);
                installedGuard = await installed.AcquireOriginalFilesPublicationAsync(originalPublication,
                    linked.Token).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("No supported installed publication transaction was retained.");
                if (!originalPublication.IsOriginalBinding(this, context, originalConnection, originalReply, ownerGuard))
                    throw new UnauthorizedAccessException("The original Files publication pairing changed.");
                originalPublication.DemandOriginalOwnerTransaction();
                await Check(installedGuard, linked.Token).ConfigureAwait(false);
                await Check(ownerGuard, linked.Token).ConfigureAwait(false);
                if (cleanup.Count == 0)
                {
                    DemandIssued(context);
                    if (context.Channel.Observe() != context.Observed)
                        throw new UnauthorizedAccessException("The actual original Files process changed.");
                    linked.Token.ThrowIfCancellationRequested();
                    originalPublication.DemandOriginalOwnerTransaction();
                    // Context.Gate and BOTH actual guards remain held through THIS original frame task.
                    await originalPhysicalWrite(linked.Token).ConfigureAwait(false);
                }
            }
            else
            {
                if (originalReply.Page is not null || originalReply.State is not ("Unavailable" or "Denied"))
                    throw new UnauthorizedAccessException("An unavailable owner cannot issue a Files page.");
                DemandIssued(context);
                linked.Token.ThrowIfCancellationRequested();
                await originalPhysicalWrite(linked.Token).ConfigureAwait(false);
            }
        }
        catch (Exception error) { primary = error; }
        finally
        {
            // Checks and every release are independent even after a body/check/write refusal.
            if (installedGuard is not null)
                await Check(installedGuard, context.Lifetime).ConfigureAwait(false);
            if (ownerGuard is not null)
                await Check(ownerGuard, context.Lifetime).ConfigureAwait(false);
            if (originalPublication is not null)
                try { originalPublication.Retire(); } catch (Exception error) { Add(cleanup, error, primary); }
            // Reverse acquisition order, independently, after the SAME physical frame task has settled.
            if (installedGuard is not null)
                try { await installedGuard.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { Add(cleanup, error, primary); }
            if (ownerGuard is not null)
                try { await ownerGuard.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { Add(cleanup, error, primary); }
            if (entered) try { context.Gate.Release(); } catch (Exception error) { Add(cleanup, error, primary); }
            if (linked is not null) try { linked.Dispose(); } catch (Exception error) { Add(cleanup, error, primary); }
        }
        ThrowFiles(primary, cleanup);

        async ValueTask Check(IHomeNativeFilesPublicationGuard guard, CancellationToken token)
        {
            try
            {
                if (!guard.IsHeld) throw new UnauthorizedAccessException("The original publication guard retired.");
                await guard.DemandOriginalCurrentAsync(token).ConfigureAwait(false);
                if (!guard.IsHeld) throw new UnauthorizedAccessException("The original publication guard retired during its check.");
            }
            catch (Exception error) { Add(cleanup, error, primary); }
        }
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
        internal Task PublishOriginalFilesReplyAsync(HomeNativeFilesReply originalReply,
            Func<CancellationToken, Task> originalPhysicalWrite, CancellationToken token) =>
            _issuer.PublishOriginalFilesReplyAsync(_context, originalReply, originalPhysicalWrite, token);
    }
}
