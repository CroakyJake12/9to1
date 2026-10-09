using System.Text.Json;

namespace HavenOS.Files;

public sealed partial class FilesWorkspaceDirectoryResolver
{
    /// <summary>Read an existing uniquely registered personal project, without a prior setup
    /// product or a new registry entry. Both participating read leases close before return.
    /// This metadata observation grants no document READ, native effect or Home admission.</summary>
    public async Task<OriginalExecutionRegistrationSnapshot> ObserveOriginalCurrentProjectRegistrationAsync(
        Guid originalProfileId, string originalAppId, DurableDriveProvider originalProvider,
        Guid originalStoreId, string configuredFilesRoot, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token)
    {
        var errors = new List<Exception>(); Task? actual = null;
        OriginalExecutionRegistrationSnapshot? observed = null; var callbackFailed = false;
        void Invoke(Action callback)
        {
            try { FilesOriginalSourceCallbackScope.Invoke(callback, originalSynchronousScope); }
            catch { callbackFailed = true; throw; }
        }
        try
        {
            Invoke(() =>
            {
                token.ThrowIfCancellationRequested();
                if (originalProfileId == Guid.Empty || originalStoreId == Guid.Empty ||
                    !ReferenceEquals(_profileProviders?.Invoke(originalProfileId), originalProvider) ||
                    !originalAppId.StartsWith("dev.project.", StringComparison.Ordinal))
                    throw new UnauthorizedAccessException("The actual personal Files project/provider is unavailable.");
            });
            var first = await ReadExisting().ConfigureAwait(false);
            var folder = await FilesOriginalDeveloperTaskSource.ObserveAsync(() =>
            {
                var returned = originalProvider.GetForOriginalStoreAsync(originalStoreId, first.Binding.FolderId,
                    token, body => Invoke(body), task => Invoke(() => retainOriginalTask(task)));
                actual = returned; return returned;
            }, body => Invoke(body), task => Invoke(() => retainOriginalTask(task))).ConfigureAwait(false);
            Invoke(() =>
            {
                if (!folder.IsSuccess || folder.Value is not { Kind: HostedItemKind.Folder, IsShared: false } row ||
                    row.OwnerPrincipalId != OwnerId(Guid.Empty, originalProfileId) || row.LocationId != originalProvider.Location.Id)
                    throw new UnauthorizedAccessException("The actual registered project folder has another owner or location.");
            });
            var last = await ReadExisting().ConfigureAwait(false);
            Invoke(() =>
            {
                if (first.Binding != last.Binding || first.StateJson != last.StateJson ||
                    !ReferenceEquals(_profileProviders?.Invoke(originalProfileId), originalProvider))
                    throw new UnauthorizedAccessException("The actual project registration changed during its source observation.");
                token.ThrowIfCancellationRequested(); observed = last;
            });
        }
        catch (Exception error) { CaptureTask(errors, actual, error); }
        if (errors.Count != 0)
        {
            if (actual?.IsCanceled == true && !callbackFailed && errors.All(value => value is OperationCanceledException))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            throw new AggregateException("The current project registration observation/cleanup did not settle.", errors);
        }
        return observed ?? throw new InvalidOperationException("No original current project registration was observed.");

        async Task<OriginalExecutionRegistrationSnapshot> ReadExisting()
        {
            Task<VersionedJsonStateStore<BindingState>.ExistingReadLease>? acquire = null;
            VersionedJsonStateStore<BindingState>.ExistingReadLease? lease = null;
            OriginalExecutionRegistrationSnapshot? result = null; var causes = new List<Exception>();
            var scopeFailed = false;
            try
            {
                try { Invoke(() => { acquire = _store.AcquireExistingReadLeaseAsync(token); actual = acquire; retainOriginalTask(acquire); }); }
                catch (Exception error) { scopeFailed = true; Capture(causes, error); }
                if (acquire is not null)
                    try { lease = await acquire.ConfigureAwait(false); }
                    catch (Exception error) { CaptureTask(causes, acquire, error); }
                if (causes.Count == 0 && lease is not null)
                    Invoke(() =>
                    {
                        var matches = lease.Snapshot.Bindings.Where(value => value.AccountId == Guid.Empty &&
                            value.ProfileId == originalProfileId && value.OwningAppId == originalAppId).Take(2).ToArray();
                        if (matches.Length != 1 || matches[0].LocationId != originalProvider.Location.Id ||
                            !Path.IsPathFullyQualified(matches[0].DirectoryPath) ||
                            !IsCurrentProjectWithin(configuredFilesRoot, matches[0].DirectoryPath) ||
                            !FilesPhysicalDirectory.IsDirectDirectory(matches[0].DirectoryPath))
                            throw new UnauthorizedAccessException("There is no unique actual project beneath the configured personal Files root.");
                        result = new(this, matches[0], _originalExecutionStatePath,
                            JsonSerializer.Serialize(lease.Snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                    });
            }
            catch (Exception error) { CaptureTask(causes, acquire, error); }
            finally
            {
                Task? close = null;
                if (lease is not null)
                {
                    // A fixed privately returned lease is cleanup-owned even if a parent
                    // refuses the next productive callback. Its refusal is still reported.
                    try { Invoke(() => { close = lease.DisposeAsync().AsTask(); retainOriginalTask(close); }); }
                    catch (Exception error) { Capture(causes, error); }
                    if (close is null)
                        try { close = lease.DisposeAsync().AsTask(); }
                        catch (Exception error) { Capture(causes, error); }
                    if (close is not null)
                    {
                        try { Invoke(() => retainOriginalTask(close)); } catch (Exception error) { Capture(causes, error); }
                        try { await close.ConfigureAwait(false); } catch (Exception error) { CaptureTask(causes, close, error); }
                    }
                }
            }
            if (causes.Count != 0)
            {
                if (acquire?.IsCanceled == true && !scopeFailed && !callbackFailed && causes.All(value => value is OperationCanceledException))
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(causes[0]).Throw();
                throw new AggregateException("The exact current registration read lease did not settle.", causes);
            }
            return result ?? throw new UnauthorizedAccessException("No actual current registration row was returned.");
        }
    }
    private static bool IsCurrentProjectWithin(string root, string path)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); path = Path.GetFullPath(path);
        return root != "/" && path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
    private static void Capture(List<Exception> errors, Exception error)
    {
        if (error is AggregateException { InnerExceptions.Count: > 0 } group)
            foreach (var cause in group.InnerExceptions) Capture(errors, cause);
        else if (!errors.Any(value => ReferenceEquals(value, error))) errors.Add(error);
    }
    private static void CaptureTask(List<Exception> errors, Task? actual, Exception observed)
    { Capture(errors, observed); if (actual?.IsFaulted == true) foreach (var cause in actual.Exception!.InnerExceptions) Capture(errors, cause); }
}
