namespace Haven.Application;

/// <summary>Genuine manual (no-AI) Home source-edit final fence recognition, delegated from the
/// configured authority. Only the genuine Home edit issuer privately recognizes its own vended
/// fences; no caller registrable membership, AlwaysAllow admission or synthetic fence can pass.
/// The implementation lives in the Home assembly, which can name its own private fence type and
/// owner identity; Haven.Application merely consumes this validator through production DI.</summary>
public interface IWorkspaceManualEditFinalFenceValidator
{
    bool IsIssuedOriginal(IWorkspaceToolFinalFence originalFence);
}

/// <summary>Genuine manual (no-AI) Home source-edit final fence. Beyond the shared cheap
/// retirement pin (which holds no Home leases), it exposes the reviewed BEFORE digest binding and
/// a fresh Home commit entry acquisition that is used only immediately before the finite native
/// swap. The entry acquisition holds the real claimed Home completion + state leases through the
/// swap and is then closed independently. The cheap pin permits only anonymous private staging
/// (`DemandOriginalStage`/`RunOriginalStage`); the committed swap alone requires the real held
/// Home entry (`DemandOriginalEffect`/`RunOriginalEffect`).</summary>
public interface IWorkspaceManualEditFinalFence : IWorkspaceToolFinalFence
{
    /// <summary>The reviewed BEFORE SHA-256 of the exact captured source file (lower-case hex).</summary>
    string ReviewedBeforeSha256 { get; }

    /// <summary>Acquires a short genuine Home claimed-actor/scope/profile/completion entry right
    /// before the final native swap. Returns null when the claim is no longer uncompleted or the
    /// configured Home state refuses the entry. The returned disposable is closed independently and
    /// joins its original close.</summary>
    ValueTask<IAsyncDisposable?> AcquireOriginalHomeCommitEntryAsync(CancellationToken cancellationToken);

    /// <summary>Staging guard used only for the anonymous/private pre-commit staging step. It is
    /// satisfied by the cheap retirement pin alone and never requires the held Home entry; a staging
    /// step itself never commits the reviewed file into place.</summary>
    void DemandOriginalStage(string canonicalWorkspaceRoot, WorkspaceToolEffectKind kind,
        string canonicalTarget, string exactContentOrRequestSha256);

    /// <summary>Runs a finite synchronous staging step (temporary/private file, directory
    /// preparation) under the cheap pin only. The final committed swap still requires the real held
    /// Home entry via <see cref="IWorkspaceToolFinalFence.RunOriginalEffect{T}"/>.</summary>
    T RunOriginalStage<T>(string canonicalWorkspaceRoot, WorkspaceToolEffectKind kind,
        string canonicalTarget, string exactContentOrRequestSha256, Func<T> originalNativeStage);
}
