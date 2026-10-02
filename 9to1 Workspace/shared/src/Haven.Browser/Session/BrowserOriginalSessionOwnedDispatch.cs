namespace Haven.Browser;

/// <summary>Direct evaluation in the SAME actual owning native turn. It must not enqueue
/// another browser turn. Text/results are observations, never authority or successful effects.</summary>
public interface IBrowserNativeEntryObservation
{
    Task<string?> EvaluateObservationAsync(string script, CancellationToken cancellationToken);
}
public interface IBrowserOwnedScriptDispatchAdmission
{
    ValueTask<IBrowserScriptDispatchLease?> AcquireAsync(IBrowserNativeEntryObservation entry,
        CancellationToken cancellationToken);
}
public interface IBrowserScriptDispatchLease : IAsyncDisposable
{
    ValueTask<bool> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>Optional actual owning adapter. One serialized owning turn directly observes
/// binding, acquires/checks retained admission, emits and AWAITS the actual native evaluation
/// Task, then disposes admission. No cancel-wait shortcut or boolean-admission fallback.
/// This holds local admission through native evaluation, not page Promise/external effects.</summary>
/// <summary>Opaque actual native document provenance; not authority, page metadata or a StoreUUID.</summary>
public interface IBrowserNativeDocumentSelection { }

public interface IOriginalSessionOwnedBrowserHost : IOriginalSessionGuardedBrowserHost
{
    // Legacy implementers do not supply original document provenance and cannot admit effects.
    IBrowserNativeDocumentSelection? CaptureDocumentSelection() => null;
    Task<string?> ExecuteOwnedScriptAsync(IBrowserNativeDocumentSelection originalDocument,
        string script, IBrowserOwnedScriptDispatchAdmission admission, CancellationToken cancellationToken)
        => throw new NotSupportedException("Actual original native document admission unavailable.");
    Task<string?> ExecuteOwnedScriptAsync(string script, IBrowserOwnedScriptDispatchAdmission admission,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Original native document selection required.");
}

public sealed partial class BrowserSessionService
{
    public async Task<string> ExecuteOriginalSessionOwnedScriptAsync(IOriginalBrowserSessionSelection selected,
        string script, IBrowserOwnedScriptDispatchAdmission admission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(admission);
        if (selected is not OriginalSelection selection || !IsOriginalCurrent(selection))
            throw new UnauthorizedAccessException("Private original browser session required.");
        if (selection.Host is not IOriginalSessionOwnedBrowserHost actual)
            throw new NotSupportedException("Actual retained native entry admission is unavailable.");
        if (selection.Document is null)
            throw new NotSupportedException("Original native document provenance unavailable.");
        return UnwrapJavaScriptString(await actual.ExecuteOwnedScriptAsync(selection.Document, script,
            new OwnedAdmission(this, selection, admission), cancellationToken).ConfigureAwait(false));
    }
    private sealed class OwnedAdmission(BrowserSessionService issuer, OriginalSelection selection,
        IBrowserOwnedScriptDispatchAdmission admission) : IBrowserOwnedScriptDispatchAdmission
    {
        public async ValueTask<IBrowserScriptDispatchLease?> AcquireAsync(IBrowserNativeEntryObservation entry, CancellationToken token)
        {
            if (!issuer.IsOriginalCurrent(selection)) return null;
            var lease = await admission.AcquireAsync(new OriginalEntry(issuer, selection, entry), token).ConfigureAwait(false);
            if (lease is null) return null;
            if (!issuer.IsOriginalCurrent(selection)) { await lease.DisposeAsync().ConfigureAwait(false); return null; }
            return new OriginalLease(issuer, selection, lease);
        }
    }
    private sealed class OriginalEntry(BrowserSessionService issuer, OriginalSelection selection,
        IBrowserNativeEntryObservation actual) : IBrowserNativeEntryObservation
    {
        public async Task<string?> EvaluateObservationAsync(string script, CancellationToken token)
        {
            if (!issuer.IsOriginalCurrent(selection)) throw new UnauthorizedAccessException("Original entry changed before observation.");
            var result = await actual.EvaluateObservationAsync(script, token).ConfigureAwait(false);
            if (!issuer.IsOriginalCurrent(selection)) throw new UnauthorizedAccessException("Original entry changed during observation.");
            return result;
        }
    }
    private sealed class OriginalLease(BrowserSessionService issuer, OriginalSelection selection,
        IBrowserScriptDispatchLease actual) : IBrowserScriptDispatchLease
    {
        private int _disposed;
        public async ValueTask<bool> CheckAsync(CancellationToken token)
        {
            if (Volatile.Read(ref _disposed) != 0 || !issuer.IsOriginalCurrent(selection)) return false;
            var allowed = await actual.CheckAsync(token).ConfigureAwait(false);
            return allowed && Volatile.Read(ref _disposed) == 0 && issuer.IsOriginalCurrent(selection);
        }
        public async ValueTask DisposeAsync()
        { if (Interlocked.Exchange(ref _disposed, 1) == 0) await actual.DisposeAsync().ConfigureAwait(false); }
    }
}
