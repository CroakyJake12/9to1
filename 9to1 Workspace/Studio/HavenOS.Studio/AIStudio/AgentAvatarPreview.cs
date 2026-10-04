using HavenOS.Home.Core;
using HavenOS.Images;
using NineToOne.Dulche.Den;

namespace HavenOS.AIStudio;

/// <summary>Authenticated, bounded presentation frames. A decoder session never grants ongoing asset access.</summary>
public sealed class AgentAvatarPreview : IAsyncDisposable
{
    private readonly DenAgentPresentationAssets assets;
    private readonly IPictureSharedRasterDecoder _rasterDecoder;

    public AgentAvatarPreview(DenAgentPresentationAssets assets)
        : this(assets, new PictureGlycinSharedRasterProvider()) { }

    public AgentAvatarPreview(DenAgentPresentationAssets assets, IPictureSharedRasterDecoder rasterDecoder)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(rasterDecoder);
        this.assets = assets;
        _rasterDecoder = rasterDecoder;
    }
    private readonly object _sync = new();
    private long _generation;
    private Lease? _active;
    private Task _cleanup = Task.CompletedTask;
    private bool _disposed;
    public HomeProductivityRasterFrame? Frame { get; private set; }
    public string AccessibleName { get; private set; } = "Agent avatar preview";
    public bool IsAnimating { get; private set; }
    public long DelayMicroseconds { get; private set; }
    public event EventHandler? Changed;

    public void Clear() => Reset();

    private long Reset(Lease? expected = null)
    {
        Lease? prior; long generation;
        lock (_sync)
        {
            if (expected is not null && !ReferenceEquals(_active, expected)) return -1;
            generation = ++_generation; prior = _active; _active = null;
            Frame = null; IsAnimating = false; DelayMicroseconds = 0;
        }
        Retire(prior);
        Changed?.Invoke(this, EventArgs.Empty);
        return generation;
    }

    public async Task LoadAsync(string namespaceId, AgentPresentationFrame presentation, CancellationToken cancellationToken, bool loopAnimation = true)
    {
        var generation = Reset();
        lock (_sync) ObjectDisposedException.ThrowIf(_disposed, this);
        var acquired = await assets.ReadAsync(namespaceId, presentation.AgentId, presentation.AssetReference,
            presentation.DefinitionRevision, cancellationToken).ConfigureAwait(false);
        Lease? owned = null;
        var published = false;
        try
        {
            await assets.ValidateAsync(namespaceId, presentation.AgentId, presentation.DefinitionRevision, acquired, cancellationToken).ConfigureAwait(false);
            var decoder = await Task.Run(() => _rasterDecoder.OpenFrames(acquired.Content, loopAnimation, cancellationToken), cancellationToken).ConfigureAwait(false);
            owned = new(namespaceId, presentation, acquired, decoder);
            var decoded = await ReadFrameAsync(owned, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The avatar asset contains no frame.");
            lock (_sync)
            {
                if (generation != _generation || _disposed) return;
                AccessibleName = presentation.AccessibleName;
                Frame = decoded.Raster;
                DelayMicroseconds = decoded.DelayMicroseconds;
                IsAnimating = presentation.Animated && decoded.DelayMicroseconds > 0;
                if (IsAnimating) { _active = owned; owned = null; published = true; }
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            if (owned is not null) await owned.DisposeAsync().ConfigureAwait(false);
            else if (!published) Array.Clear(acquired.Content);
        }
    }

    public async Task<bool> AdvanceAsync(CancellationToken cancellationToken = default)
    {
        Lease? lease; long generation;
        lock (_sync) { lease = _active; generation = _generation; }
        if (lease is null) return false;
        try
        {
            var next = await ReadFrameAsync(lease, cancellationToken).ConfigureAwait(false);
            var ended = false;
            lock (_sync)
            {
                if (_disposed || generation != _generation || !ReferenceEquals(_active, lease)) return false;
                if (next is null || next.DelayMicroseconds <= 0)
                {
                    _active = null; IsAnimating = false; DelayMicroseconds = 0; ended = true;
                }
                if (next is not null)
                {
                    Frame = next.Raster;
                    if (!ended) DelayMicroseconds = next.DelayMicroseconds;
                }
            }
            if (ended) Retire(lease);
            Changed?.Invoke(this, EventArgs.Empty);
            return next is not null;
        }
        catch
        {
            Reset(lease);
            throw;
        }
    }

    private async Task<PictureSharedAnimationFrame?> ReadFrameAsync(Lease lease, CancellationToken cancellationToken)
    {
        await lease.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(lease.Retired, lease);
            await assets.ValidateAsync(lease.NamespaceId, lease.Presentation.AgentId, lease.Presentation.DefinitionRevision, lease.Bytes, cancellationToken).ConfigureAwait(false);
            var frame = await Task.Run(() => lease.Decoder.TryNextFrame(cancellationToken), cancellationToken).ConfigureAwait(false);
            await assets.ValidateAsync(lease.NamespaceId, lease.Presentation.AgentId, lease.Presentation.DefinitionRevision, lease.Bytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return frame;
        }
        finally { lease.Gate.Release(); }
    }

    private void Retire(Lease? lease)
    {
        if (lease is null) return;
        var cleanup = lease.DisposeAsync().AsTask();
        lock (_sync) _cleanup = Task.WhenAll(_cleanup, cleanup);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync) _disposed = true;
        Clear();
        Task cleanup;
        lock (_sync) cleanup = _cleanup;
        await cleanup.ConfigureAwait(false);
    }

    private sealed class Lease(string namespaceId, AgentPresentationFrame presentation, AgentPresentationAssetBytes bytes,
        IPictureSharedRasterFrameSession decoder) : IAsyncDisposable
    {
        public string NamespaceId { get; } = namespaceId;
        public AgentPresentationFrame Presentation { get; } = presentation;
        public AgentPresentationAssetBytes Bytes { get; } = bytes;
        public IPictureSharedRasterFrameSession Decoder { get; } = decoder;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        private readonly object _disposalSync = new();
        private Task? _disposal;
        private int _retired;
        public bool Retired => Volatile.Read(ref _retired) != 0;
        public ValueTask DisposeAsync()
        {
            lock (_disposalSync)
            {
                Volatile.Write(ref _retired, 1);
                return new(_disposal ??= DisposeCoreAsync());
            }
        }
        private async Task DisposeCoreAsync()
        {
            // The selected provider closes the same original decoder. Glycin can
            // interrupt its donor; Skia joins its synchronous call without claiming
            // interruption. Keep that close off the UI and before the owner gate.
            try { await Task.Run(Decoder.Dispose).ConfigureAwait(false); }
            finally
            {
                await Gate.WaitAsync().ConfigureAwait(false);
                try { Array.Clear(Bytes.Content); }
                finally { Gate.Release(); }
            }
        }
    }
}
