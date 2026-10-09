using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeDenStoreEvidenceProvider : IHomeOriginalScopedLocalStoreEvidenceProvider
{
    private readonly object _originalEvidenceGate = new();
    private readonly List<OriginalEvidenceRead> _originalEvidenceReads = [];
    private bool _originalEvidenceRetiring;
    private Task? _originalEvidenceClose;
    private Task? _originalEvidenceStoreClose;
    private sealed class OriginalEvidenceRead(HomeOwnershipOriginalSourceCallbacks source)
    {
        internal readonly HomeOwnershipOriginalSourceCallbacks Source = source;
        internal Task<HomeLocalStoreEvidence?> Driver = null!;
    }

    /// <summary>The SAME real provider, actor, Den writer/manifest/content observation
    /// and created-empty evidence. Every raw owning source is retained before the
    /// borrowed callback returns. A callback and a Den identifier grant no ownership.</summary>
    public ValueTask<HomeLocalStoreEvidence?> ReadWithinOriginalSourceAsync(string storeId,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var source = new HomeOwnershipOriginalSourceCallbacks(
            body => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { scope(body); return true; }),
            raw => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { retain(raw); return true; }));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        OriginalEvidenceRead read;
        lock (_originalEvidenceGate)
        {
            ObjectDisposedException.ThrowIf(_disposed || _originalEvidenceRetiring, this);
            _originalEvidenceReads.RemoveAll(value => value.Driver.IsCompletedSuccessfully && value.Source.Errors.Length == 0);
            if (_originalEvidenceReads.Count >= 128)
                throw new InvalidOperationException("Actual unresolved Den evidence sources remain retained for recovery.");
            read = new(source); read.Driver = Drive(start.Task, read); _originalEvidenceReads.Add(read);
        }
        try { source.Run(() => retain(read.Driver)); }
        catch { /* The accepted driver owns publication failure before another source. */ }
        finally { start.SetResult(); }
        return new(read.Driver);

        async Task<HomeLocalStoreEvidence?> Drive(Task gate, OriginalEvidenceRead original)
        {
            await gate.ConfigureAwait(false); using var executing = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            if (original.Source.Errors.Length != 0)
                throw new AggregateException("The actual Den evidence publication failed.", original.Source.Errors);
            var actorSource = _actors as IOriginalScopedResourceActorSource
                ?? throw new InvalidOperationException("The actual Den evidence actor owner has no original-scoped producer.");
            if (original.Source.Invoke(() => _disposed || storeId != Store.Manifest.DenId)) return null;
            if (await original.Source.ReadAsync(() => actorSource.GetCurrentWithinOriginalSourceAsync(
                original.Source.Run, original.Source.Retain, token)).ConfigureAwait(false) != _actor) return null;
            var observation = await original.Source.ReadAsync(() => Store.ObserveOwnershipWithinOriginalSourceAsync(
                original.Source.Run, original.Source.Retain,
                body => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { body(); return true; }), token)).ConfigureAwait(false);
            if (await original.Source.ReadAsync(() => actorSource.GetCurrentWithinOriginalSourceAsync(
                original.Source.Run, original.Source.Retain, token)).ConfigureAwait(false) != _actor || observation.DenId != storeId) return null;
            return original.Source.Invoke(() => new HomeLocalStoreEvidence(ResourceKind, observation.DenId,
                observation.ContentRevision, _created, observation.IsEmpty, true));
        }
    }

    public Task? OriginalClose { get { lock (_originalEvidenceGate) return _originalEvidenceClose; } }
    public void DemandExternalOriginalRetirementJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    public Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalRetirementJoin(); TaskCompletionSource? start = null; Task actual;
        lock (_originalEvidenceGate)
        {
            if (_originalEvidenceClose is null)
            {
                _originalEvidenceRetiring = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _originalEvidenceClose = Close(start.Task, _originalEvidenceReads.ToArray());
            }
            actual = _originalEvidenceClose;
        }
        start?.SetResult(); return actual;
    }
    private async Task Close(Task start, OriginalEvidenceRead[] reads)
    {
        await start.ConfigureAwait(false); List<Exception> errors = [];
        foreach (var read in reads)
            try { await read.Driver.ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(read.Driver.Exception ?? cause); }
        // Borrowers settle first. Retire the SAME actual Den store even if an original
        // observation failed, retain its exact cleanup task, and preserve both causes.
        _disposed = true;
        try
        {
            _originalEvidenceStoreClose = CloudflareOriginalExecutionGuard.InvokeOriginal(this,
                () => Store.DisposeAsync().AsTask());
        }
        catch (Exception cause) { errors.Add(cause); }
        if (_originalEvidenceStoreClose is { } originalClose)
            try { await originalClose.ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(originalClose.Exception ?? cause); }
        if (errors.Count != 0) throw new AggregateException("Actual Den evidence/owning store close failed; source resources remain retained.", errors);
    }
}
