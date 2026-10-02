namespace Haven.Browser;

/// <summary>Opaque original host/session provenance; never Home authority or a profile StoreUUID.</summary>
public interface IOriginalBrowserSessionSelection : IDisposable { }

/// <summary>Home composition supplies its private original-principal admission. This callback
/// is required at the actual serialized webview execution entry, not before posting to UI.</summary>
public interface IBrowserScriptDispatchAdmission
{
    ValueTask<bool> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>Optional real host capability. Implementations must call admission in the same
/// serialized owning UI execution pipeline immediately before script emission. Cancellation,
/// denial or missing capability emits no script. No fallback to unguarded ExecuteScriptAsync.</summary>
public interface IOriginalSessionGuardedBrowserHost : IEmbeddedBrowserHost
{
    Task<string?> ExecuteScriptGuardedAsync(string script, IBrowserScriptDispatchAdmission admission,
        CancellationToken cancellationToken);
}

public sealed partial class BrowserSessionService
{
    private long _ownedSessionGeneration;
    private int _ownedSessionDisposed;
    private sealed class OriginalSelection(BrowserSessionService issuer, IEmbeddedBrowserHost host, long generation, IBrowserNativeDocumentSelection? document)
        : IOriginalBrowserSessionSelection
    {
        public BrowserSessionService Issuer { get; } = issuer;
        public IEmbeddedBrowserHost Host { get; } = host;
        public long Generation { get; } = generation;
        public IBrowserNativeDocumentSelection? Document { get; } = document;
        private int _revoked;
        public bool Revoked => Volatile.Read(ref _revoked) != 0;
        public void Dispose() => Interlocked.Exchange(ref _revoked, 1);
    }
    public IOriginalBrowserSessionSelection CaptureOriginalSession()
    {
        var generation = Interlocked.Read(ref _ownedSessionGeneration);
        var host = _host ?? throw new InvalidOperationException("Original interactive host required.");
        // Capture the actual host-issued document before any discovery/display await.
        var document = (host as IOriginalSessionOwnedBrowserHost)?.CaptureDocumentSelection();
        var selection = new OriginalSelection(this, host, generation, document);
        if (!IsOriginalCurrent(selection)) throw new UnauthorizedAccessException("Browser session changed during selection.");
        return selection;
    }
    private bool IsOriginalCurrent(OriginalSelection selection) =>
        ReferenceEquals(selection.Issuer, this) && ReferenceEquals(selection.Host, _host)
        && selection.Generation == Interlocked.Read(ref _ownedSessionGeneration)
        && !selection.Revoked && Volatile.Read(ref _ownedSessionDisposed) == 0;

    public async Task<string> ExecuteOriginalSessionScriptAsync(IOriginalBrowserSessionSelection selected,
        string script, IBrowserScriptDispatchAdmission originalPrincipalAdmission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalPrincipalAdmission);
        if (selected is not OriginalSelection selection || !IsOriginalCurrent(selection))
            throw new UnauthorizedAccessException("Original private browser session required.");
        if (selection.Host is not IOriginalSessionGuardedBrowserHost actual)
            throw new NotSupportedException("Actual webview final dispatch admission is unavailable.");
        // The actual selected host owns the final emission boundary. Never use the ambient _host.
        return UnwrapJavaScriptString(await actual.ExecuteScriptGuardedAsync(script,
            new OriginalAdmission(this, selection, originalPrincipalAdmission), cancellationToken).ConfigureAwait(false));
    }
    private sealed class OriginalAdmission(BrowserSessionService issuer, OriginalSelection selection,
        IBrowserScriptDispatchAdmission originalPrincipal) : IBrowserScriptDispatchAdmission
    {
        public async ValueTask<bool> CheckAsync(CancellationToken token)
        {
            if (!issuer.IsOriginalCurrent(selection)) return false;
            var current = await originalPrincipal.CheckAsync(token).ConfigureAwait(false);
            return current && issuer.IsOriginalCurrent(selection);
        }
    }
}
