namespace HavenOS.Files;

public sealed partial class FilesWorkspaceDirectoryResolver
{
    /// <summary>Trusted reviewed original Dev setup only, create-only. The owning producer
    /// holds genuine Home completion/local entry plus its SAME native directory identity through
    /// this original result/cleanup. Public IDs/path/callbacks are not grants. Provider metadata
    /// is observed before the binding lease; final checks never reacquire Home or Files/provider.
    /// This is participating-host currentness, not cross-file CAS against arbitrary external writers.</summary>
    public async Task<FilesResult<FilesWorkspaceDirectoryBinding>> RegisterOriginalDeveloperProfileAsync(
        Guid originalProfileId, Guid originalProjectId, HostedItemId originalFolderId,
        FilesRevisionId originalFolderRevision, string actualDirectoryPath,
        DurableDriveProvider originalProvider, Guid originalStoreId,
        FilesCommitAuthorityGuard originalHomeAuthority,
        Func<CancellationToken, ValueTask<bool>> isOriginalNativeDirectoryCurrent,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalProvider); ArgumentNullException.ThrowIfNull(originalHomeAuthority);
        ArgumentNullException.ThrowIfNull(isOriginalNativeDirectoryCurrent); ArgumentNullException.ThrowIfNull(originalSynchronousScope);
        ArgumentNullException.ThrowIfNull(retainOriginalTask);
        if (originalProfileId == Guid.Empty || originalProjectId == Guid.Empty || originalFolderId.Value == Guid.Empty ||
            originalFolderRevision.Value == Guid.Empty || originalStoreId == Guid.Empty ||
            originalHomeAuthority.ActorId != OwnerId(Guid.Empty, originalProfileId) || !Path.IsPathFullyQualified(actualDirectoryPath))
            throw new UnauthorizedAccessException("Retain the once-created Dev identities and genuine held local original authority.");
        var directory = Path.GetFullPath(actualDirectoryPath);
        var app = "dev.project." + originalProjectId.ToString("N");
        bool SameProvider() => Invoke(() => ReferenceEquals(_profileProviders?.Invoke(originalProfileId), originalProvider));
        if (!SameProvider()) throw new UnauthorizedAccessException("The genuine configured original Files provider changed.");
        var current = await Observe(() => originalProvider.GetForOriginalStoreAsync(originalStoreId, originalFolderId, cancellationToken)).ConfigureAwait(false);
        if (!SameProvider() || !current.IsSuccess || current.Value is not { Kind: HostedItemKind.Folder } folder ||
            folder.CurrentRevisionId != originalFolderRevision || folder.LocationId != originalProvider.Location.Id ||
            folder.OwnerPrincipalId != originalHomeAuthority.ActorId)
            throw new UnauthorizedAccessException("The actual original canonical Dev project folder changed.");
        await ValidateFinal(cancellationToken).ConfigureAwait(false);
        var binding = new FilesWorkspaceDirectoryBinding(Guid.Empty, originalFolderId, originalProvider.Location.Id,
            directory, app, DateTimeOffset.UtcNow, originalProfileId);
        await Observe(() => _store.UpdateAsync(state =>
        {
            // No substitute app/folder/path binding and no silent replay of a recorded setup step.
            if (state.Bindings.Any(value => value.OwningAppId == app || value.FolderId == originalFolderId ||
                string.Equals(Path.GetFullPath(value.DirectoryPath), directory,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                throw new IOException("This original app/folder/path is already registered; inspect its retained outcome without repeating setup.");
            return new BindingState([.. state.Bindings, binding]);
        }, ValidateFinal, cancellationToken)).ConfigureAwait(false);
        return FilesResult<FilesWorkspaceDirectoryBinding>.Success(binding);

        async ValueTask ValidateFinal(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!SameProvider() || !await Observe(() => isOriginalNativeDirectoryCurrent(ct).AsTask()).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The original held native directory/provider changed before registration.");
            // SAME already-held Home entry; actual readUnlocked/profile check may perform I/O.
            // The owning predicate must not reacquire Home/completion/resources or read Files.
            await ObserveVoid(() => originalHomeAuthority.ValidateAsync(ct).AsTask()).ConfigureAwait(false);
            if (!SameProvider()) throw new UnauthorizedAccessException("The original directory provider changed before publication.");
            ct.ThrowIfCancellationRequested();
        }
        T Invoke<T>(Func<T> source)
        {
            T result = default!; Exception? cause = null;
            try { originalSynchronousScope(() => { try { result = source(); } catch (Exception error) { cause = error; } }); }
            catch (Exception error) { cause = cause is null ? error : new AggregateException(cause, error); }
            if (cause is OperationCanceledException stopped) throw new AggregateException("Direct original setup callback returned no canceled original Task.", stopped);
            if (cause is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cause).Throw();
            return result;
        }
        async Task<T> Observe<T>(Func<Task<T>> source)
        {
            Task<T>? actual = null; var errors = new List<Exception>(); T value = default!;
            try { Invoke(() => { actual = source() ?? throw new InvalidOperationException("Original directory source returned no Task."); retainOriginalTask(actual); return true; }); }
            catch (Exception cause) { errors.Add(cause); }
            if (actual is not null)
                try { value = await actual.ConfigureAwait(false); }
                catch (Exception cause)
                {
                    if (actual.IsCanceled && errors.Count == 0) throw;
                    errors.AddRange(actual.Exception?.InnerExceptions ?? new[] { cause }.AsEnumerable());
                }
            if (errors.Count != 0) throw new AggregateException("Actual original directory observation/commit or source enrollment failed; outcome may be unknown.", errors);
            return value;
        }
        async Task ObserveVoid(Func<Task> source)
        {
            Task? actual = null; var errors = new List<Exception>();
            try { Invoke(() => { actual = source() ?? throw new InvalidOperationException("Original directory source returned no Task."); retainOriginalTask(actual); return true; }); }
            catch (Exception cause) { errors.Add(cause); }
            if (actual is not null)
                try { await actual.ConfigureAwait(false); }
                catch (Exception cause)
                {
                    if (actual.IsCanceled && errors.Count == 0) throw;
                    errors.AddRange(actual.Exception?.InnerExceptions ?? new[] { cause }.AsEnumerable());
                }
            if (errors.Count != 0) throw new AggregateException("Actual original directory validation/enrollment failed; outcome may be unknown.", errors);
        }
    }
}
