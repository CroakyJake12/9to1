namespace Haven.Application;

/// <summary>Home admits the current actor before calling this profile-scoped model browser.
/// Installation does not select a model, change Dulche, or establish runtime readiness.</summary>
public interface IHuggingFaceModelService
{
    Task<IReadOnlyList<HuggingFaceModelSummary>> SearchAsync(string profileId, string query, CancellationToken cancellationToken);
    Task<HuggingFaceModelDetails> GetDetailsAsync(string profileId, string repositoryId, CancellationToken cancellationToken);
    Task<HuggingFaceInstallResult> InstallAsync(string profileId, HuggingFaceModelDetails model, HuggingFaceModelFile file,
        IProgress<HuggingFaceInstallProgress>? progress, CancellationToken cancellationToken);
    Task<bool> HasTokenAsync(string profileId, CancellationToken cancellationToken);
    Task SaveTokenAsync(string profileId, string token, CancellationToken cancellationToken);
    Task RemoveTokenAsync(string profileId, CancellationToken cancellationToken);
}

public sealed record HuggingFaceModelSummary(string RepositoryId, long Downloads, long Likes, bool Gated);
public sealed record HuggingFaceModelDetails(string RepositoryId, string CommitSha, string License, bool Gated,
    IReadOnlyList<HuggingFaceModelFile> Files);
public sealed record HuggingFaceModelFile(string Path, long Size, string Sha256);
public enum HuggingFaceInstallPhase { Downloading, Validating, Importing, Installed }
public sealed record HuggingFaceInstallProgress(HuggingFaceInstallPhase Phase, long ReceivedBytes, long TotalBytes);
public sealed record HuggingFaceInstallResult(string ModelId, string RepositoryId, string CommitSha, string FilePath,
    string Sha256, long Size, bool RuntimeObserved = false, bool CleanupPending = false);

public enum HuggingFaceModelError
{
    InvalidRequest, Unavailable, AuthenticationRequired, AccessDenied, InvalidMetadata,
    DownloadRejected, IntegrityFailure, ImportFailed, SecureStorageUnavailable
}

/// <summary>Only fixed, safe messages cross this boundary; remote bodies, URLs and underlying
/// transport/credential exceptions are deliberately never attached.</summary>
public sealed class HuggingFaceModelException(HuggingFaceModelError code, string message) : Exception(message)
{
    public HuggingFaceModelError Code { get; } = code;
}
