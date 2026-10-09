namespace Haven.Application;

/// <summary>Original lifetime dependency guard only; this creates no task, actor, policy or service grant.</summary>
public static class CloudflareOriginalExecutionGuard
{
    private sealed class Phase(object owner, Phase? parent)
    { internal object Owner { get; } = owner; internal Phase? Parent { get; } = parent; internal volatile bool Live = true; }
    private static readonly AsyncLocal<Phase?> Current = new();
    [ThreadStatic] private static Stack<object>? _physical;
    public static IDisposable EnterOriginal(object owner)
    {
        var prior = Current.Value; var phase = new Phase(owner, prior); Current.Value = phase;
        return new Exit(() => { phase.Live = false; Current.Value = prior; });
    }
    public static T InvokeOriginal<T>(object owner, Func<T> finite)
    {
        (_physical ??= new()).Push(owner);
        try { return finite(); } finally { _physical.Pop(); }
    }
    public static void DemandExternalJoin(object owner)
    {
        if (_physical?.Any(x => ReferenceEquals(x, owner)) == true) throw new InvalidOperationException("An original Cloudflare callback cannot join its own cleanup.");
        for (var phase = Current.Value; phase is not null; phase = phase.Parent)
            if (phase.Live && ReferenceEquals(phase.Owner, owner)) throw new InvalidOperationException("A live original Cloudflare ancestor cannot join its own cleanup.");
    }
    private sealed class Exit(Action release) : IDisposable
    { private int _closed; public void Dispose() { if (Interlocked.Exchange(ref _closed, 1) == 0) release(); } }
}
