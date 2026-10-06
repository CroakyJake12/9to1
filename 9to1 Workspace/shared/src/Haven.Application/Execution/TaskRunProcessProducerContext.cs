namespace Haven.Application;

/// <summary>Only live original-source custody. Detached descendants see the same retired marker,
/// and restored ExecutionContext cannot bypass a synchronous physical source ancestor.</summary>
internal static class TaskRunProcessProducerContext
{
    private sealed class Original(object owner, Original? parent)
    {
        internal readonly object Owner = owner;
        internal readonly Original? Parent = parent;
        internal volatile bool Active = true;
    }
    private static readonly AsyncLocal<Original?> Current = new();
    [ThreadStatic] private static Original? Physical;

    internal static IDisposable EnterAsync(object owner)
    {
        var previous = Current.Value;
        var original = new Original(owner, previous);
        Current.Value = original;
        return new Scope(() => { original.Active = false; Current.Value = previous; });
    }

    internal static T Invoke<T>(object? owner, Func<T> source)
    {
        if (owner is null) return source();
        var previous = Physical;
        var original = new Original(owner, previous);
        Physical = original;
        try { return source(); }
        finally { original.Active = false; Physical = previous; }
    }

    internal static void DemandExternalJoin(object owner)
    {
        void Check(Original? original)
        {
            for (; original is not null; original = original.Parent)
                if (original.Active && ReferenceEquals(original.Owner, owner))
                    throw new InvalidOperationException("An actual Agent source callback cannot join its own process retirement.");
        }
        Check(Current.Value); Check(Physical);
    }

    private sealed class Scope(Action close) : IDisposable
    {
        public void Dispose() => close();
    }
}
