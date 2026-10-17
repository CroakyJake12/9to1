using Haven.Application;
using System.Threading;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class MemorySettingsStore : IVersionedSettingsStore
{
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_values.TryGetValue(key, out var value) ? (T?)value : null);
    }

    public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _values.Remove(key);
        return Task.CompletedTask;
    }

    public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<SettingsImportResult> ImportAsync(
        SettingsExportManifest manifest,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>
/// A settings store that delays the initial read of the Space registry until an external
/// task completes. Used to test that the controller does not let a stale async load overwrite
/// newer UI state.
/// </summary>
public sealed class DelayedMemorySettingsStore : IVersionedSettingsStore
{
    private readonly Task _delay;
    private readonly IVersionedSettingsStore? _inner;
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
    private int _delayedReads;

    public DelayedMemorySettingsStore(Task delay, IVersionedSettingsStore? inner = null)
    {
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        _inner = inner;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (key == "spaces.registry" && Interlocked.Increment(ref _delayedReads) == 1)
        {
            // Use a new cancellation-aware delay so the test can advance by releasing the task.
            var registration = cancellationToken.Register(() => _ = Task.Run(() => { }));
            await _delay.WaitAsync(cancellationToken).ConfigureAwait(false);
            registration.Dispose();
        }
        if (_inner is not null)
            return await _inner.GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        return _values.TryGetValue(key, out var value) ? (T?)value : null;
    }

    public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_inner is not null) return _inner.SetAsync(key, value, cancellationToken);
        _values[key] = value!;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_inner is not null) return _inner.RemoveAsync(key, cancellationToken);
        _values.Remove(key);
        return Task.CompletedTask;
    }

    public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<SettingsImportResult> ImportAsync(
        SettingsExportManifest manifest,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}
