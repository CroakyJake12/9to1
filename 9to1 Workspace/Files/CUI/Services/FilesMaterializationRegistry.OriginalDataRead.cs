namespace HavenOS.Files;

public sealed partial class FilesMaterializationRegistry
{
    public async Task<FilesMaterializedFile?> GetExistingOriginalDataByItemIdWithinSourceAsync(
        HostedItemId itemId, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var state = await FilesOriginalDeveloperTaskSource.ObserveAsync(
            () => _store.ReadExistingWithinOriginalSourceAsync(scope, retain, token), scope, retain).ConfigureAwait(false);
        FilesMaterializedFile? result = null;
        FilesOriginalDeveloperTaskSource.Invoke(() =>
        {
            token.ThrowIfCancellationRequested();
            var matches = state.ItemsByPath.Where(pair => pair.Value.ItemId == itemId).Take(2).ToArray();
            if (matches.Length > 1) throw new InvalidDataException("The Data file has ambiguous existing materializations.");
            if (matches.Length == 0) return;
            var actual = matches[0]; var path = ValidateLocalPath(actual.Value.LocalPath);
            if (!string.Equals(actual.Key, PathKey(path), StringComparison.Ordinal))
                throw new InvalidDataException("The existing Files materialization index changed.");
            result = actual.Value;
        }, scope);
        return result;
    }
    public Task? OriginalDataReadsClose => _store.OriginalReadsClose;
    public void ThrowIfOriginalDataReadJoinWouldCycle() => _store.ThrowIfOriginalReadJoinWouldCycle();
    public Task CloseOriginalDataReadsAndDrainAsync() => _store.CloseOriginalReadsAndDrainAsync();
}
