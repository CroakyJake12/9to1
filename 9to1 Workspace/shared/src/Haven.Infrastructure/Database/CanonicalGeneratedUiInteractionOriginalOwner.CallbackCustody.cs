namespace Haven.Infrastructure;

public sealed partial class CanonicalGeneratedUiInteractionOriginalOwner
{
    // Late escaped callbacks may outlive a successfully joined/pruned read source.
    // Retain only their actual causes, without keeping every historical scope alive.
    private readonly List<Exception> _unexpectedOriginalCallbacks = [];
    private void RetainUnexpectedOriginalCallback(Exception cause)
    {
        lock (_gate)
            if (!_unexpectedOriginalCallbacks.Any(prior => ReferenceEquals(prior, cause)))
                _unexpectedOriginalCallbacks.Add(cause);
    }
    private void DemandHealthyOriginalCallbacks()
    {
        Exception[] causes; lock (_gate) causes = _unexpectedOriginalCallbacks.ToArray();
        CanonicalSqliteOriginalStoreOwner.Throw(causes.ToList());
    }
    private CanonicalSqliteOriginalSourceScope CreateOriginalInteractionSource(Action<Action> scope,
        Action<Task> retain, bool productive = true) =>
        new(this, scope, retain, RetainUnexpectedOriginalCallback, productive ? DemandHealthyOriginalCallbacks : null);
}
