using Haven.Application;

namespace Haven.Infrastructure.Native.Windows;

public sealed partial class NativeWindowsHomeRootHostVerifier : IHomeOriginalScopedNativeSessionHostVerifier
{
    public Task<OriginalHomeHostEndpoint?> ObserveOriginalEndpointWithinSourceAsync(Action<Action> scope,
        Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        return Begin<OriginalHomeHostEndpoint?>(null, null, token, endpoint => endpoint, scope, retain);
    }
    public Task<HomeNativeInstalledPeer?> VerifyHostWithinOriginalSourceAsync(HomeNativeObservedPeer observed,
        HomeNativeSessionHostRequirement requirement, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(observed); ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        return Begin<HomeNativeInstalledPeer?>(observed with { }, requirement with { }, token, endpoint => endpoint?.Host, scope, retain);
    }
    private sealed class OriginalHostSourceScope
    {
        private readonly NativeWindowsHomeRootHostVerifier _owner;
        private readonly Invocation _work;
        private readonly Action<Action> _scope;
        private readonly Action<Task> _retain;
        private readonly object _sync = new();
        private readonly HashSet<Task> _forwarded = new(ReferenceEqualityComparer.Instance);
        internal OriginalHostSourceScope(NativeWindowsHomeRootHostVerifier owner, Invocation work,
            Action<Action> scope, Action<Task> retain)
        { _owner = owner; _work = work; _scope = scope; _retain = retain; }
        internal void Run(Action body)
        {
            _owner.DemandOriginalSourceHealthy();
            var thread = Environment.CurrentManagedThreadId; var accepting = true; var invoked = 0;
            var local = new List<Exception>(); var gate = new object();
            void Keep(Exception cause) { lock (gate) local.Add(cause); _owner.RetainOriginalSourceFailure(_work, cause); }
            void Callback()
            {
                if (!Volatile.Read(ref accepting) || Environment.CurrentManagedThreadId != thread || Interlocked.Increment(ref invoked) != 1)
                {
                    var cause = new InvalidOperationException("The original host source callback is late, repeated or on another thread.");
                    Keep(cause); throw cause;
                }
                try
                {
                    CloudflareOriginalExecutionGuard.InvokeOriginal(_owner, () =>
                    {
                        _owner.DemandOriginalSourceHealthy(); body(); ForwardRaw();
                        _owner.DemandOriginalSourceHealthy(); return true;
                    });
                }
                catch (Exception cause) { Keep(cause); throw; }
            }
            try { CloudflareOriginalExecutionGuard.InvokeOriginal(_owner, () => { _scope(Callback); return true; }); }
            catch (Exception cause) { Keep(cause); }
            finally { Volatile.Write(ref accepting, false); }
            if (Volatile.Read(ref invoked) != 1) Keep(new InvalidOperationException("The original host caller did not invoke its finite body exactly once."));
            Exception[] causes; lock (gate) causes = local.ToArray();
            if (causes.Length != 0) throw new AggregateException("Original host source/caller protocol failed.", causes);
        }
        internal void ForwardRaw()
        {
            foreach (var same in _work.Sources.OriginalTasks)
            {
                lock (_sync) if (!_forwarded.Add(same)) continue;
                try { CloudflareOriginalExecutionGuard.InvokeOriginal(_owner, () => { _retain(same); return true; }); }
                catch (Exception cause) { _owner.RetainOriginalSourceFailure(_work, cause); throw; }
            }
        }
        internal void Publish(Task same)
        {
            try { Run(() => _retain(same)); } catch (Exception cause) { _owner.RetainOriginalSourceFailure(_work, cause); }
        }
        internal void DemandHealthy()
        {
            _owner.DemandOriginalSourceHealthy();
            if (_work.Sources.OriginalErrors.Count != 0)
                throw new AggregateException("Original host publication/source failed.", _work.Sources.OriginalErrors);
        }
    }
}
