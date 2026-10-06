using Haven.Application;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesDeveloperOriginalSetupScopeSource
{
    internal Task RevalidateOriginalDestinationWithinSourceAsync(Destination destination,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
        => StartWithinOriginalSource(async sources =>
        {
            sources.Invoke(() => { RequireDestination(destination); return true; });
            var current = await sources.Observe(() => workspaces.GetOriginalCurrentWithinSourceAsync(
                destination.OriginalStoreId, sources, token)).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The current original destination is unavailable.");
            sources.Invoke(() =>
            {
                if (!SameWorkspace(destination.Workspace, current) || Hash(current.Configuration) != destination.ConfigurationDigest)
                    throw new UnauthorizedAccessException("The original destination actor/configuration changed.");
                return true;
            });
            var row = await sources.Observe(() => current.Provider.GetForOriginalStoreAsync(
                destination.OriginalStoreId, destination.Folder.Id, token, sources.OriginalSynchronousScope, sources.RetainOriginalTask)).ConfigureAwait(false);
            sources.Invoke(() =>
            {
                if (!row.IsSuccess || row.Value != destination.Folder)
                    throw new UnauthorizedAccessException("The actual destination folder changed.");
                RequireFolder(current, row.Value!); return true;
            });
            var after = await sources.Observe(() => workspaces.GetOriginalCurrentWithinSourceAsync(
                destination.OriginalStoreId, sources, token)).ConfigureAwait(false);
            sources.Invoke(() =>
            {
                if (after is null || !SameWorkspace(current, after))
                    throw new UnauthorizedAccessException("The destination changed during the original folder read.");
                token.ThrowIfCancellationRequested(); RequireDestination(destination); return true;
            });
            return true;
        }, originalSynchronousScope, retainOriginalTask);

    private Task<T> StartWithinOriginalSource<T>(Func<FilesOriginalReadSourceScope, Task<T>> body,
        Action<Action> parentScope, Action<Task> parentRetain)
    {
        ArgumentNullException.ThrowIfNull(parentScope); ArgumentNullException.ThrowIfNull(parentRetain);
        TaskCompletionSource start; Task<T> driver; FilesOriginalParentOperation pair;
        lock (_gate)
        {
            if (_retiring) throw new InvalidOperationException("The original destination source is retiring.");
            _originals.RemoveWhere(value => value.Healthy && value.Driver.IsCompletedSuccessfully);
            if (_originals.Count >= 128) throw new InvalidOperationException("Original destination custody is full.");
            var original = new Original(_executing.Value);
            pair = new(Scope, actual => { lock (_gate) original.Sources.Add(actual); }, parentScope, parentRetain);
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            driver = Run(start.Task, original, _ => { pair.DemandPublication(); return body(pair.Sources); });
            original.Driver = driver; _originals.Add(original);
        }
        pair.Publish(driver); start.TrySetResult(); return driver;
    }
}
