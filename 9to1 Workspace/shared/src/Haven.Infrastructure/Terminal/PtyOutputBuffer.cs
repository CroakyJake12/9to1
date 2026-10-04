using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("HavenOS.Terminal.Specs")]

namespace Haven.Infrastructure.Terminal;

/// <summary>
/// Preserves output produced while a PTY process is being created and before its first consumer
/// can subscribe. After the first subscription, the event follows ordinary no-subscriber behavior.
/// </summary>
internal sealed class PtyOutputBuffer
{
    private const int MaximumPendingBytes = 4 * 1024 * 1024;
    private readonly object _sync = new();
    private readonly Queue<PendingOutput> _pending = new();
    private EventHandler<PtyOutputChunk>? _handlers;
    private bool _hasSubscriber;
    private bool _isDraining;
    private int _pendingBytes;

    /// <summary>Bytes dropped from the pre-subscription queue after its memory bound was reached.</summary>
    public long DroppedPendingBytes { get; private set; }

    public event EventHandler<PtyOutputChunk>? OutputReceived
    {
        add
        {
            if (value is null) return;

            var shouldDrain = false;
            lock (_sync)
            {
                _handlers += value;
                _hasSubscriber = true;
                if (!_isDraining && _pending.Count > 0)
                {
                    _isDraining = true;
                    shouldDrain = true;
                }
            }

            if (shouldDrain) Drain();
        }
        remove
        {
            if (value is null) return;
            lock (_sync) _handlers -= value;
        }
    }

    public void Publish(object sender, PtyOutputChunk output)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(output);

        var shouldDrain = false;
        lock (_sync)
        {
            if (_handlers is null && _hasSubscriber) return;

            var outputBytes = output.Bytes.Length;
            while (_pendingBytes + outputBytes > MaximumPendingBytes && _pending.Count > 0)
            {
                var discarded = _pending.Dequeue();
                _pendingBytes -= discarded.Chunk.Bytes.Length;
                DroppedPendingBytes += discarded.Chunk.Bytes.Length;
            }

            if (outputBytes > MaximumPendingBytes)
            {
                var skip = outputBytes - MaximumPendingBytes;
                output = new PtyOutputChunk(output.Bytes[^MaximumPendingBytes..]);
                outputBytes = output.Bytes.Length;
                DroppedPendingBytes += skip;
            }

            _pending.Enqueue(new PendingOutput(sender, output));
            _pendingBytes += outputBytes;
            if (_handlers is not null && !_isDraining)
            {
                _isDraining = true;
                shouldDrain = true;
            }
        }

        if (shouldDrain) Drain();
    }

    private void Drain()
    {
        while (true)
        {
            PendingOutput output;
            EventHandler<PtyOutputChunk>? handlers;
            lock (_sync)
            {
                if (_pending.Count == 0)
                {
                    _isDraining = false;
                    return;
                }

                output = _pending.Dequeue();
                _pendingBytes -= output.Chunk.Bytes.Length;
                handlers = _handlers;
            }

            if (handlers is null) continue;
            foreach (EventHandler<PtyOutputChunk> handler in handlers.GetInvocationList())
            {
                try { handler(output.Sender, output.Chunk); }
                catch { /* Presentation callbacks cannot terminate process I/O ownership. */ }
            }
        }
    }

    private sealed record PendingOutput(object Sender, PtyOutputChunk Chunk);
}
