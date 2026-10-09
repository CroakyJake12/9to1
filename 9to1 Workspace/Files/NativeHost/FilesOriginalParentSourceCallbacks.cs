namespace HavenOS.Files.NativeHost;

/// <summary>The SAME caller's raw read custody, using the maintained Files finite parent
/// protocol helper. No separate owner, registry, actor, permission or grant.</summary>
internal static class FilesOriginalParentSourceCallbacks
{
    internal static FilesOriginalReadSourceScope Create(Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope);
        ArgumentNullException.ThrowIfNull(retainOriginalTask);
        return new(callback => FilesOriginalSourceCallbackScope.Invoke(callback, originalSynchronousScope), retainOriginalTask);
    }
}
