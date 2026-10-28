using System.Collections.Concurrent;

namespace CakeOS.Apps.Boards.Contract;

/// <summary>Serializes local canonical-head checks and writes across sessions and processes.</summary>
public sealed class LocalBoardWriteLease : IDisposable
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate;
    private readonly FileStream _file;
    private readonly string _recoveryPath;
    private int _disposed;
    internal bool IsHeldFor(string path) => Volatile.Read(ref _disposed) == 0 &&
        string.Equals(_recoveryPath, Path.GetFullPath(path), OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private LocalBoardWriteLease(SemaphoreSlim gate, FileStream file, string recoveryPath) { _gate = gate; _file = file; _recoveryPath = Path.GetFullPath(recoveryPath); }

    public static async Task<LocalBoardWriteLease> AcquireAsync(string recoveryPath, CancellationToken token)
    {
        var path = Path.GetFullPath(recoveryPath) + ".write-lock";
        var gate = Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    // Keep the inode after release. Unlinking a live lock would let another
                    // process lock a different file at this same path.
                    var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    return new LocalBoardWriteLease(gate, file, recoveryPath);
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                { await Task.Delay(25, token).ConfigureAwait(false); }
            }
        }
        catch { gate.Release(); throw; }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _file.Dispose(); _gate.Release();
    }
}
