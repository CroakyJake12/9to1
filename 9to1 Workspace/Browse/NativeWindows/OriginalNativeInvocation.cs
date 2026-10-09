namespace HavenOS.Apps.Browse;

// Owner-only markers: no authority, task substitute or public scope constructor.
// A synchronous physical marker survives a restored ExecutionContext; logical
// drivers flow through actual asynchronous callbacks and expire at settlement.
internal static class OriginalNativeInvocation
{
    private sealed class Marker(object owner, Marker? parent)
    { internal object Owner { get; } = owner; internal Marker? Parent { get; } = parent; internal volatile bool Active = true; }
    private static readonly AsyncLocal<Marker?> Logical = new();
    [ThreadStatic] private static Marker? Physical;
    internal static IDisposable EnterDriver(object owner) => new Scope(owner, physical: false);
    internal static IDisposable EnterExternal(object owner) => new Scope(owner, physical: true);
    internal static T Acquire<T>(object owner, Func<T> acquire)
    { using var invocation = EnterExternal(owner); return acquire(); }
    internal static void DemandExternalJoin(object owner)
    {
        for (var marker = Logical.Value; marker is not null; marker = marker.Parent)
            if (marker.Active && ReferenceEquals(marker.Owner, owner)) throw new InvalidOperationException("The original native invocation cannot join its own retirement.");
        for (var marker = Physical; marker is not null; marker = marker.Parent)
            if (marker.Active && ReferenceEquals(marker.Owner, owner)) throw new InvalidOperationException("The physical native invocation cannot join its own retirement.");
    }
    private sealed class Scope : IDisposable
    {
        private readonly Marker? _priorLogical, _priorPhysical;
        private readonly Marker _logical;
        private readonly Marker? _physical;
        private bool _disposed;
        internal Scope(object owner, bool physical)
        {
            _priorLogical = Logical.Value; _logical = new(owner, _priorLogical); Logical.Value = _logical;
            _priorPhysical = Physical;
            if (physical) { _physical = new(owner, _priorPhysical); Physical = _physical; }
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true; _logical.Active = false;
            if (ReferenceEquals(Logical.Value, _logical)) Logical.Value = _priorLogical;
            if (_physical is not null)
            {
                _physical.Active = false;
                if (!ReferenceEquals(Physical, _physical)) throw new InvalidOperationException("The original physical native invocation order changed.");
                Physical = _priorPhysical;
            }
        }
    }
}
