namespace NineToOne.Web.Write;

/// <summary>Browser file chooser/download brokerage; paths are ephemeral codec staging only.</summary>
public interface IWriteBrowserPackageBroker
{
    Task<WriteStagedPackage?> PickImportAsync(CancellationToken cancellationToken);
    Task<WriteStagedPackage> StageExportAsync(string suggestedFilename, CancellationToken cancellationToken);
    Task PublishAsync(WriteStagedPackage package, CancellationToken cancellationToken);
}

/// <summary>Owns a disposable copy of package bytes, never a canonical artifact/File identity.</summary>
public sealed class WriteStagedPackage(string path, string downloadName, Func<ValueTask> cleanup) : IAsyncDisposable
{
    public string Path { get; } = path;
    public string DownloadName { get; } = downloadName;
    private bool _disposed;
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await cleanup();
    }
}
