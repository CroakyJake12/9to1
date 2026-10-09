namespace HavenOS.Files;

public sealed partial class FilesMaterializationRegistry
{
    public async Task<FilesMaterializedFile?> GetExistingOriginalAttachmentByPathAsync(string observedPath,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        string? path = null;
        FilesOriginalDeveloperTaskSource.Invoke(() => path = ValidateLocalPath(observedPath), scope);
        var state = await FilesOriginalDeveloperTaskSource.ObserveAsync(
            () => _store.ReadExistingWithinOriginalSourceAsync(scope, retain, token), scope, retain).ConfigureAwait(false);
        FilesMaterializedFile? result = null;
        FilesOriginalDeveloperTaskSource.Invoke(() =>
        {
            if (state.ItemsByPath.TryGetValue(PathKey(path!), out var item))
            {
                var actualPath = ValidateLocalPath(item.LocalPath);
                if (!string.Equals(PathKey(actualPath), PathKey(path!), StringComparison.Ordinal))
                    throw new InvalidDataException("The original Files materialization path index changed.");
                result = item;
            }
        }, scope);
        return result;
    }
    public Task? OriginalAttachmentReadsClose => _store.OriginalReadsClose;
    public void ThrowIfOriginalAttachmentReadJoinWouldCycle() => _store.ThrowIfOriginalReadJoinWouldCycle();
    public Task CloseOriginalAttachmentReadsAndDrainAsync() => _store.CloseOriginalReadsAndDrainAsync();
}
