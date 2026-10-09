namespace HavenOS.Files.NativeHost;

/// <summary>One borrowed original source pair. This carries lifetime custody only; it owns
/// no actor, permission, registry or effect. Parent publication precedes the start gate.</summary>
internal sealed class FilesOriginalParentOperation(Action<Action> ownScope, Action<Task> ownRetain,
    Action<Action> parentScope, Action<Task> parentRetain)
{
    private readonly List<Exception> _publicationCauses = [];
    internal FilesOriginalReadSourceScope Sources { get; } = new(
        callback => ownScope(() => FilesOriginalSourceCallbackScope.Invoke(callback, parentScope)),
        actual =>
        {
            ownRetain(actual);
            ownScope(() => FilesOriginalSourceCallbackScope.Invoke(() => parentRetain(actual), parentScope));
        });
    internal void Publish(Task actual)
    {
        // Driver is already retained on its own original. Enrolling it there again would
        // make that driver join itself. Only the borrowing parent receives this reference.
        try { ownScope(() => FilesOriginalSourceCallbackScope.Invoke(() => parentRetain(actual), parentScope)); }
        catch (Exception error) { _publicationCauses.Add(error); }
    }
    internal void DemandPublication()
    {
        if (_publicationCauses.Count != 0)
            throw new AggregateException("The actual original driver publication failed.", _publicationCauses);
    }
}
