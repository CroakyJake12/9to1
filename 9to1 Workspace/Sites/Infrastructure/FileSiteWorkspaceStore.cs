using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using HavenOS.Apps.Sites.Domain;

namespace HavenOS.Apps.Sites.Infrastructure;

/// <summary>
/// Stores Sites metadata in the user's canonical Sites directory supplied by Files.
/// The caller must pass a directory returned by the Files integration; this class
/// does not invent a private fallback location.
/// </summary>
public sealed class FileSiteWorkspaceStore
{
    private const int LockWaitLimitSeconds = 20;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProcessLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _indexPath;
    private readonly string _lockPath;
    private readonly SemaphoreSlim _processLock;

    public FileSiteWorkspaceStore(string canonicalSitesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalSitesDirectory);
        var root = Path.GetFullPath(canonicalSitesDirectory);
        if (File.Exists(root))
            throw new ArgumentException("The Files Sites location must be a directory.", nameof(canonicalSitesDirectory));
        _indexPath = Path.Combine(root, ".9to1-sites-index.json");
        _lockPath = _indexPath + ".lock";
        _processLock = ProcessLocks.GetOrAdd(_lockPath, static _ => new SemaphoreSlim(1, 1));
    }

    public Task<T> ReadAsync<T>(Func<SiteWorkspaceSnapshot, T> read, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        return WithLockAsync(async () => read(await ReadStateAsync(cancellationToken).ConfigureAwait(false)), cancellationToken);
    }

    public Task<T> MutateAsync<T>(Func<SiteWorkspaceSnapshot, (SiteWorkspaceSnapshot State, T Result)> mutation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        return WithLockAsync(async () =>
        {
            var current = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
            var (next, result) = mutation(current);
            ValidateState(next);
            await WriteStateAsync(next, cancellationToken).ConfigureAwait(false);
            return result;
        }, cancellationToken);
    }

    private async Task<T> WithLockAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await _processLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var crossProcessLock = await AcquireCrossProcessLockAsync(cancellationToken).ConfigureAwait(false);
            return await action().ConfigureAwait(false);
        }
        finally
        {
            _processLock.Release();
        }
    }

    private async Task<FileStream> AcquireCrossProcessLockAsync(CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var delay = TimeSpan.FromMilliseconds(20);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_indexPath)!);
                return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous);
            }
            catch (IOException) when (DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(LockWaitLimitSeconds))
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 1.7, 350));
            }
            catch (UnauthorizedAccessException ex)
            {
                throw StoreFailure("The canonical Sites directory is not writable.", "SitesStorageUnavailable", ex);
            }
            if (DateTimeOffset.UtcNow - started >= TimeSpan.FromSeconds(LockWaitLimitSeconds))
                throw new SiteOperationException(new SiteApiError("SitesStorageBusy", "Sites metadata is busy in another process. Retry the operation.", "Sites", true, TimeSpan.FromSeconds(1)));
        }
    }

    private async Task<SiteWorkspaceSnapshot> ReadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_indexPath)) return EmptyState();
        try
        {
            await using var stream = new FileStream(_indexPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var state = await JsonSerializer.DeserializeAsync<SiteWorkspaceSnapshot>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (state is null)
                throw StoreFailure("Sites metadata is empty or invalid.", "SitesMetadataInvalid");
            if (state.SchemaVersion != SiteProjectFormat.CurrentSchemaVersion)
                throw StoreFailure($"Sites metadata schema version {state.SchemaVersion} is not supported. The file was preserved.", "SitesSchemaUnsupported");
            ValidateState(state);
            return state;
        }
        catch (JsonException ex)
        {
            throw StoreFailure("Sites metadata could not be read. The original file was preserved.", "SitesMetadataInvalid", ex);
        }
        catch (IOException ex)
        {
            throw StoreFailure("Sites metadata could not be read. Retry after the storage becomes available.", "SitesStorageUnavailable", ex);
        }
    }

    private async Task WriteStateAsync(SiteWorkspaceSnapshot state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_indexPath)!);
        var temporaryPath = _indexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _indexPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw StoreFailure("Sites metadata could not be committed; the previous index remains authoritative.", "SitesStorageUnavailable", ex);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static SiteWorkspaceSnapshot EmptyState() => new(
        SiteProjectFormat.CurrentSchemaVersion,
        Array.Empty<SiteProject>(),
        Array.Empty<SiteDeployment>(),
        Array.Empty<DomainBinding>(),
        Array.Empty<PublicNameVerification>(),
        Array.Empty<DomainOwnershipChallenge>(),
        Array.Empty<SiteSlugReservation>());

    private static void ValidateState(SiteWorkspaceSnapshot state)
    {
        if (state.SchemaVersion != SiteProjectFormat.CurrentSchemaVersion)
            throw StoreFailure("Sites metadata schema version is unsupported; no data was changed.", "SitesSchemaUnsupported");
        EnsureUnique(state.Projects.Select(project => project.SiteId), "SiteID");
        EnsureUnique(state.Projects.Select(project => project.ProjectId), "ProjectID");
        EnsureUnique(state.Deployments.Select(deployment => deployment.DeploymentId), "DeploymentID");
        EnsureUnique(state.Domains.Select(domain => domain.DomainBindingId), "DomainBindingID");
        EnsureUnique(state.NameVerifications.Select(verification => verification.VerificationId), "VerificationID");
        EnsureUnique(state.DomainChallenges.Select(challenge => challenge.ChallengeId), "ChallengeID");
        var activeSlugs = state.SlugReservations.Where(reservation => reservation.ReleasedAt is null).ToArray();
        EnsureUnique(activeSlugs.Select(reservation => reservation.Slug), "active site slug");
        EnsureUnique(activeSlugs.Select(reservation => reservation.SiteId), "active slug per site");
    }

    private static void EnsureUnique<T>(IEnumerable<T> values, string identity)
    {
        var list = values.ToArray();
        if (list.Distinct().Count() != list.Length)
            throw StoreFailure($"Sites metadata contains duplicate {identity} values. The original file was preserved.", "SitesMetadataConflict");
    }

    private static SiteOperationException StoreFailure(string message, string code, Exception? exception = null) =>
        new(new SiteApiError(code, message, "Sites", exception is IOException or UnauthorizedAccessException, Detail: exception?.GetType().Name));

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = false,
            MaxDepth = 64
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
