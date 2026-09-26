using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Reads and writes the versioned native .9to1w document package.
/// </summary>
public interface IWriteNativeDocumentPackageStore
{
    Task<WriteNativeDocumentPackageResult<NotesDocument>> OpenAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);

    Task<WriteNativeDocumentPackageResult<string>> SaveAsync(
        NotesDocument document,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

public sealed record WriteNativeDocumentPackageResult<T>(
    T? Value,
    WriteNativeDocumentPackageError? Error)
{
    public bool IsSuccess => Error is null && Value is not null;

    public static WriteNativeDocumentPackageResult<T> Success(T value) => new(value, null);

    public static WriteNativeDocumentPackageResult<T> Failure(WriteNativeDocumentPackageError error) =>
        new(default, error);
}

/// <summary>
/// Stable failure information suitable for the Write UI and app API boundary.
/// </summary>
public sealed record WriteNativeDocumentPackageError(
    WriteNativeDocumentPackageErrorCode Code,
    string Message,
    string Target,
    bool Recoverable,
    bool Retryable);

public enum WriteNativeDocumentPackageErrorCode
{
    InvalidPath,
    DocumentNotFound,
    UnsupportedDocumentVersion,
    ImportUnsupported,
    ImportLossy,
    ExportUnsupported,
    ExportLossy,
    InvalidPackage,
    IntegrityCheckFailed,
    UnsafePackageEntry,
    PackageTooLarge,
    DocumentIdMismatch,
    ProviderUnavailable,
    PermissionDenied,
    SaveFailed
}

/// <summary>
/// Internal Write-owned metadata used to retain package parts and unknown optional
/// manifest properties while the document is saved through the existing repository.
/// </summary>
public static class WriteNativeDocumentPackageMetadata
{
    public const string Extension = ".9to1w";
    public const string PreservedPackageStateKey = "write.native-package.state.v1";
}
