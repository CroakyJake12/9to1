namespace HavenOS.Apps.Browse;

/// <summary>
/// A ref-counted handle on a shared Chromium connection for one canonical profile.
/// Disposal returns the handle to the pool, which terminates the owned process
/// only after the final attached tab has retired.
/// </summary>
internal sealed class ChromiumLease : IAsyncDisposable
{
    private readonly ChromiumProfilePool _pool;
    private readonly string _profile;
    private readonly IChromiumEndpoint _connection;
    private bool _disposed;

    internal ChromiumLease(ChromiumProfilePool pool, string profile, IChromiumEndpoint connection)
    {
        _pool = pool;
        _profile = profile;
        _connection = connection;
    }

    internal IChromiumEndpoint Connection => _connection;

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        return _pool.ReleaseAsync(_profile);
    }
}

/// <summary>
/// Shares one owned Chromium process across every standard tab that uses the same
/// canonical engine-isolated profile directory. Tab targets are distinct CDP page
/// targets inside that process, so closing one tab never kills another. Each
/// private tab already owns a distinct canonical profile root and therefore gets
/// its own process. The last lease release closes the process.
/// </summary>
internal sealed class ChromiumProfilePool
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Func<string, CancellationToken, Task<IChromiumEndpoint>> _connect;

    internal ChromiumProfilePool(Func<string, CancellationToken, Task<IChromiumEndpoint>>? connect = null)
    {
        _connect = connect ?? ((profile, token) => ChromiumConnection.StartAsync(profile, ChromiumLocator.Instance.Find() ?? throw new PlatformNotSupportedException("Installed Chromium was not found on this Linux system."), token));
    }

    private sealed class Entry
    {
        public int RefCount;
        public Task<IChromiumEndpoint>? Connecting;
    }

    public async Task<ChromiumLease> AcquireAsync(string profileDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profileDirectory)) throw new ArgumentException("A canonical engine profile is required.", nameof(profileDirectory));
        var normalized = Path.GetFullPath(profileDirectory);
        Task<IChromiumEndpoint> connecting;
        lock (_gate)
        {
            if (!_entries.TryGetValue(normalized, out var entry))
            {
                entry = new Entry();
                _entries[normalized] = entry;
            }
            entry.RefCount++;
            connecting = entry.Connecting ??= _connect(normalized, cancellationToken);
        }

        try
        {
            var endpoint = await connecting.ConfigureAwait(false);
            return new ChromiumLease(this, normalized, endpoint);
        }
        catch
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(normalized, out var entry) && ReferenceEquals(entry.Connecting, connecting))
                {
                    entry.RefCount--;
                    if (entry.RefCount <= 0) _entries.Remove(normalized);
                }
            }
            throw;
        }
    }

    internal async ValueTask ReleaseAsync(string profile)
    {
        IChromiumEndpoint? toClose = null;
        lock (_gate)
        {
            if (_entries.TryGetValue(profile, out var entry))
            {
                entry.RefCount--;
                if (entry.RefCount <= 0)
                {
                    _entries.Remove(profile);
                    if (entry.Connecting is { IsCompletedSuccessfully: true }) toClose = entry.Connecting.Result;
                }
            }
        }
        if (toClose is not null) await toClose.DisposeAsync().ConfigureAwait(false);
    }
}
