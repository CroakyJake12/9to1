using System.Collections.Concurrent;
using System.Text.Json;

namespace HavenOS.Apps.Dev;

public interface IDeveloperWorkspaceStore
{
    Task<DeveloperOperationResult<DeveloperWorkspace>> CreateAsync(DeveloperWorkspace workspace, CancellationToken cancellationToken = default);
    Task<DeveloperOperationResult<DeveloperWorkspace>> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<DeveloperOperationResult<DeveloperWorkspace>> SaveAsync(DeveloperWorkspace workspace, long expectedRevision, CancellationToken cancellationToken = default);
}

/// <summary>
/// Versioned, atomic Dev workspace persistence. The host supplies its application-data root; the store never
/// writes workspace metadata into source folders and does not persist secrets or mutable display-name identity.
/// </summary>
public sealed class FileDeveloperWorkspaceStore : IDeveloperWorkspaceStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _workspaceDirectory;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public FileDeveloperWorkspaceStore(string applicationDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataRoot);
        _workspaceDirectory = Path.Combine(Path.GetFullPath(applicationDataRoot), "workspaces");
    }

    public async Task<DeveloperOperationResult<DeveloperWorkspace>> CreateAsync(
        DeveloperWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (workspace.Validate() is { } validationError)
            return DeveloperOperationResult<DeveloperWorkspace>.Failure(
                DeveloperOperationErrorCode.InvalidInput, validationError, workspace.WorkspaceId.ToString("D"));

        var acquired = await AcquireWriteLockAsync(workspace.WorkspaceId, cancellationToken).ConfigureAwait(false);
        if (acquired.Error is not null) return DeveloperOperationResult<DeveloperWorkspace>.Failure(
            acquired.Error.Code, acquired.Error.Message, acquired.Error.TargetId,
            acquired.Error.Recoverable, acquired.Error.Retryable);
        await using var writeLock = acquired.Lock!;

        try
        {
            var path = GetWorkspacePath(workspace.WorkspaceId);
            if (File.Exists(path))
                return DeveloperOperationResult<DeveloperWorkspace>.Failure(
                    DeveloperOperationErrorCode.RevisionConflict,
                    "A workspace with this stable ID already exists.", workspace.WorkspaceId.ToString("D"));

            var created = workspace with { Revision = 1, ModifiedAt = workspace.CreatedAt };
            await WriteAsync(path, created, cancellationToken).ConfigureAwait(false);
            return DeveloperOperationResult<DeveloperWorkspace>.Success(created);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return StorageFailure<DeveloperWorkspace>(workspace.WorkspaceId, exception);
        }
    }

    public async Task<DeveloperOperationResult<DeveloperWorkspace>> GetAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty)
            return DeveloperOperationResult<DeveloperWorkspace>.Failure(
                DeveloperOperationErrorCode.InvalidInput, "WorkspaceID cannot be empty.", string.Empty);

        try
        {
            var path = GetWorkspacePath(workspaceId);
            if (!File.Exists(path))
                return DeveloperOperationResult<DeveloperWorkspace>.Failure(
                    DeveloperOperationErrorCode.WorkspaceNotFound,
                    "The requested workspace does not exist.", workspaceId.ToString("D"));

            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(input, cancellationToken: cancellationToken).ConfigureAwait(false);
            var version = ReadSchemaVersion(document.RootElement);
            if (version != CurrentSchemaVersion)
                return DeveloperOperationResult<DeveloperWorkspace>.Failure(
                    DeveloperOperationErrorCode.UnsupportedSchemaVersion,
                    $"Workspace schema version {version} is not supported by this Dev build.", workspaceId.ToString("D"));

            var envelope = document.RootElement.Deserialize<WorkspaceDocument>(JsonOptions);
            if (envelope?.Workspace is null)
                return DeveloperOperationResult<DeveloperWorkspace>.Failure(
                    DeveloperOperationErrorCode.InvalidStoredData,
                    "The workspace document does not contain a workspace.", workspaceId.ToString("D"));
            if (envelope.Workspace.WorkspaceId != workspaceId)
                return DeveloperOperationResult<DeveloperWorkspace>.Failure(
                    DeveloperOperationErrorCode.InvalidStoredData,
                    "The stored workspace identity does not match its storage key.", workspaceId.ToString("D"));
            if (envelope.Workspace.Validate() is { } error)
                return DeveloperOperationResult<DeveloperWorkspace>.Failure(
                    DeveloperOperationErrorCode.InvalidStoredData, error, workspaceId.ToString("D"));

            return DeveloperOperationResult<DeveloperWorkspace>.Success(envelope.Workspace);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (FileNotFoundException)
        {
            return DeveloperOperationResult<DeveloperWorkspace>.Failure(
                DeveloperOperationErrorCode.WorkspaceNotFound,
                "The requested workspace does not exist.", workspaceId.ToString("D"));
        }
        catch (JsonException)
        {
            return DeveloperOperationResult<DeveloperWorkspace>.Failure(
                DeveloperOperationErrorCode.InvalidStoredData,
                "The workspace file is malformed and was left unchanged.", workspaceId.ToString("D"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return StorageFailure<DeveloperWorkspace>(workspaceId, exception);
        }
    }

    public async Task<DeveloperOperationResult<DeveloperWorkspace>> SaveAsync(
        DeveloperWorkspace workspace,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (workspace.Validate() is { } validationError)
            return DeveloperOperationResult<DeveloperWorkspace>.Failure(
                DeveloperOperationErrorCode.InvalidInput, validationError, workspace.WorkspaceId.ToString("D"));
        if (expectedRevision < 1)
            return DeveloperOperationResult<DeveloperWorkspace>.Failure(
                DeveloperOperationErrorCode.InvalidInput, "Expected revision must be positive.", workspace.WorkspaceId.ToString("D"));

        var acquired = await AcquireWriteLockAsync(workspace.WorkspaceId, cancellationToken).ConfigureAwait(false);
        if (acquired.Error is not null) return DeveloperOperationResult<DeveloperWorkspace>.Failure(
            acquired.Error.Code, acquired.Error.Message, acquired.Error.TargetId,
            acquired.Error.Recoverable, acquired.Error.Retryable);
        await using var writeLock = acquired.Lock!;

        try
        {
            var path = GetWorkspacePath(workspace.WorkspaceId);
            if (!File.Exists(path))
                return DeveloperOperationResult<DeveloperWorkspace>.Failure(
                    DeveloperOperationErrorCode.WorkspaceNotFound,
                    "The requested workspace does not exist.", workspace.WorkspaceId.ToString("D"));

            var currentResult = await GetAsync(workspace.WorkspaceId, cancellationToken).ConfigureAwait(false);
            if (!currentResult.Succeeded) return currentResult;
            var current = currentResult.Value!;
            if (current.Revision != expectedRevision || workspace.CreatedAt != current.CreatedAt)
                return DeveloperOperationResult<DeveloperWorkspace>.Failure(
                    DeveloperOperationErrorCode.RevisionConflict,
                    "Workspace state changed since it was read. Reload before applying this update.",
                    workspace.WorkspaceId.ToString("D"), retryable: true);

            var updated = workspace with { Revision = checked(current.Revision + 1), CreatedAt = current.CreatedAt, ModifiedAt = DateTimeOffset.UtcNow };
            if (updated.Validate() is { } updatedValidation)
                return DeveloperOperationResult<DeveloperWorkspace>.Failure(
                    DeveloperOperationErrorCode.InvalidInput, updatedValidation, workspace.WorkspaceId.ToString("D"));
            await WriteAsync(path, updated, cancellationToken).ConfigureAwait(false);
            return DeveloperOperationResult<DeveloperWorkspace>.Success(updated);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or OverflowException)
        {
            return StorageFailure<DeveloperWorkspace>(workspace.WorkspaceId, exception);
        }
    }

    private async Task<(IAsyncDisposable? Lock, DeveloperOperationError? Error)> AcquireWriteLockAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (workspaceId == Guid.Empty)
            return (null, new DeveloperOperationError(DeveloperOperationErrorCode.InvalidInput,
                "WorkspaceID cannot be empty.", string.Empty, true, false));

        var processLock = _locks.GetOrAdd(workspaceId, static _ => new SemaphoreSlim(1, 1));
        await processLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_workspaceDirectory);
            var lockPath = Path.Combine(_workspaceDirectory, workspaceId.ToString("N") + ".lock");
            var fileLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                1, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            return (new ReleasingFileLock(fileLock, processLock), null);
        }
        catch (IOException)
        {
            processLock.Release();
            return (null, new DeveloperOperationError(DeveloperOperationErrorCode.RevisionConflict,
                "Another Dev process is updating this workspace. Reload and retry.", workspaceId.ToString("D"), true, true));
        }
        catch (UnauthorizedAccessException exception)
        {
            processLock.Release();
            return (null, new DeveloperOperationError(DeveloperOperationErrorCode.StorageUnavailable,
                SafeMessage(exception), workspaceId.ToString("D"), true, false));
        }
    }

    private string GetWorkspacePath(Guid workspaceId) =>
        Path.Combine(_workspaceDirectory, workspaceId.ToString("N") + ".json");

    private static async Task WriteAsync(string path, DeveloperWorkspace workspace, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new WorkspaceDocument(CurrentSchemaVersion, workspace), JsonOptions);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static int ReadSchemaVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return -1;
        foreach (var property in root.EnumerateObject())
            if (string.Equals(property.Name, nameof(WorkspaceDocument.SchemaVersion), StringComparison.OrdinalIgnoreCase) &&
                property.Value.TryGetInt32(out var version)) return version;
        return -1;
    }

    private static DeveloperOperationResult<T> StorageFailure<T>(Guid workspaceId, Exception exception) =>
        DeveloperOperationResult<T>.Failure(DeveloperOperationErrorCode.StorageUnavailable,
            SafeMessage(exception), workspaceId.ToString("D"), recoverable: true, retryable: exception is IOException);

    private static string SafeMessage(Exception exception) =>
        exception is UnauthorizedAccessException ? "Dev cannot access its workspace-data location." :
        "Dev could not read or atomically save workspace state.";

    private sealed record WorkspaceDocument(int SchemaVersion, DeveloperWorkspace Workspace);

    private sealed class ReleasingFileLock(FileStream file, SemaphoreSlim processLock) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await file.DisposeAsync().ConfigureAwait(false);
            processLock.Release();
        }
    }
}
