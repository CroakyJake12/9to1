using System.Text.Json;

namespace HavenOS.Files;

public sealed partial class FilesWorkspaceDirectoryResolver
{
    private readonly string _originalExecutionStatePath;

    /// <summary>Current registration observation only. The short participating read lease
    /// closes before return; it is never held through Home. The kernel separately retains
    /// and validates the actual native registration document inode/version and full state.</summary>
    public sealed class OriginalExecutionRegistrationSnapshot
    {
        internal OriginalExecutionRegistrationSnapshot(FilesWorkspaceDirectoryResolver owner,
            FilesWorkspaceDirectoryBinding binding, string statePath, string stateJson)
        { Owner = owner; Binding = binding; StatePath = statePath; StateJson = stateJson; }
        internal FilesWorkspaceDirectoryResolver Owner { get; }
        public FilesWorkspaceDirectoryBinding Binding { get; }
        public string StatePath { get; }
        public string StateJson { get; }
    }

    public async Task<OriginalExecutionRegistrationSnapshot> ObserveOriginalExecutionRegistrationAsync(
        Guid originalProfileId, string originalAppId, HostedItemId originalFolderId,
        DurableDriveProvider originalProvider, Guid originalStoreId, string originalRoot,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken cancellationToken)
    {
        VersionedJsonStateStore<BindingState>.ExistingReadLease? lease = null;
        OriginalExecutionRegistrationSnapshot? result = null; var errors = new List<Exception>();
        Task? actual = null;
        void Invoke(Action callback) => FilesOriginalDeveloperTaskSource.Invoke(callback, originalSynchronousScope);
        try
        {
            Invoke(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (originalProfileId == Guid.Empty || originalStoreId == Guid.Empty ||
                    !ReferenceEquals(_profileProviders?.Invoke(originalProfileId), originalProvider))
                    throw new UnauthorizedAccessException("The original configured personal Files provider is unavailable.");
            });
            var folder = await FilesOriginalDeveloperTaskSource.ObserveAsync(
                () =>
                {
                    var returned = originalProvider.GetForOriginalStoreAsync(originalStoreId, originalFolderId, cancellationToken,
                        originalSynchronousScope, retainOriginalTask);
                    actual = returned; return returned;
                }, originalSynchronousScope, retainOriginalTask).ConfigureAwait(false);
            if (!folder.IsSuccess || folder.Value is not { Kind: HostedItemKind.Folder } row || row.IsShared ||
                row.OwnerPrincipalId != OwnerId(Guid.Empty, originalProfileId) || row.LocationId != originalProvider.Location.Id)
                throw new UnauthorizedAccessException("The registered project folder no longer has the original personal owner.");
            Task<VersionedJsonStateStore<BindingState>.ExistingReadLease>? acquire = null;
            try { Invoke(() => { acquire = _store.AcquireExistingReadLeaseAsync(cancellationToken); actual = acquire; retainOriginalTask(acquire); }); }
            catch (Exception error) { Append(errors, error); }
            if (acquire is not null)
                try { lease = await acquire.ConfigureAwait(false); }
                catch (Exception error) { AppendTask(errors, acquire, error); }
            if (errors.Count == 0 && lease is not null)
                Invoke(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(_profileProviders?.Invoke(originalProfileId), originalProvider))
                        throw new UnauthorizedAccessException("The actual provider changed during registration observation.");
                    var matches = lease.Snapshot.Bindings.Where(value => value.AccountId == Guid.Empty &&
                        value.ProfileId == originalProfileId && value.OwningAppId == originalAppId).Take(2).ToArray();
                    if (matches.Length != 1 || matches[0].FolderId != originalFolderId ||
                        matches[0].LocationId != originalProvider.Location.Id || matches[0].DirectoryPath != originalRoot ||
                        !FilesPhysicalDirectory.IsDirectDirectory(originalRoot))
                        throw new UnauthorizedAccessException("No unique unchanged actual project registration exists.");
                    result = new(this, matches[0], _originalExecutionStatePath,
                        JsonSerializer.Serialize(lease.Snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                });
        }
        catch (Exception error) { AppendTask(errors, actual, error); }
        finally
        {
            Task? close = null;
            if (lease is not null)
                try { Invoke(() => { close = lease.DisposeAsync().AsTask(); retainOriginalTask(close); }); }
                catch (Exception error) { Append(errors, error); }
            if (close is not null)
                try { await close.ConfigureAwait(false); }
                catch (Exception error) { AppendTask(errors, close, error); }
        }
        if (errors.Count != 0)
        {
            if (actual?.IsCanceled == true && errors.All(value => value is OperationCanceledException))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            throw new AggregateException("Actual registration observation/lease cleanup did not complete cleanly.", errors);
        }
        return result ?? throw new InvalidOperationException("The actual registration scope returned no observation.");
    }

    private static void Append(List<Exception> errors, Exception error)
    {
        if (error is AggregateException group) foreach (var cause in group.InnerExceptions) Append(errors, cause);
        else if (!errors.Any(value => ReferenceEquals(value, error))) errors.Add(error);
    }
    private static void AppendTask(List<Exception> errors, Task? actual, Exception caught)
    { if (actual?.IsFaulted == true) foreach (var cause in actual.Exception!.InnerExceptions) Append(errors, cause); else Append(errors, caught); }
}
