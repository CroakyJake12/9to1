using System.Runtime.CompilerServices;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Detached wire observation. Authority remains the privately issued
/// observation and actual signed, controlled Home process/channel, never these fields.</summary>
public sealed record HomeNativeWindowsListeningLease(string ProfileId, Guid LeaseIdentity, string PipeName);

public sealed partial class HomeNativeWindowsComposition
{
    private readonly ConditionalWeakTable<OriginalRootListeningObservation, RootListeningWork> _rootListeningIssued = new();
    private readonly List<RootListeningWork> _rootListeningOriginals = [];
    private sealed class RootListeningWork(CloudflareOriginalTaskLedger source)
    {
        internal readonly CloudflareOriginalTaskLedger Source = source;
        internal Task<OriginalRootListeningObservation> Driver = null!;
        internal bool Healthy => Driver.IsCompletedSuccessfully && Source.OriginalErrors.Count == 0 && Source.OriginalTasks.All(raw => raw.IsCompletedSuccessfully);
    }
    public sealed class OriginalRootListeningObservation
    {
        internal readonly HomeNativeWindowsComposition Issuer;
        internal OriginalRootListeningObservation(HomeNativeWindowsComposition issuer, AuthenticatedResourceActor actor, HomeNativeWindowsListeningLease actual)
        { Issuer = issuer; Actor = actor; ActualLease = actual; }
        public AuthenticatedResourceActor Actor { get; }
        public HomeNativeWindowsListeningLease ActualLease { get; }
    }
    public bool IsIssuedOriginalRootListeningObservation(OriginalRootListeningObservation same) => same is not null &&
        ReferenceEquals(same.Issuer, this) && _rootListeningIssued.TryGetValue(same, out var work) && work.Healthy &&
        ReferenceEquals(work.Driver.Result, same);
    /// <summary>Pure current owner/lease check. Actor and package authority are
    /// independently observed by the caller; this never performs startup or IO.</summary>
    public bool IsCurrentOriginalRootListeningObservation(OriginalRootListeningObservation same)
    {
        if (!IsIssuedOriginalRootListeningObservation(same)) return false;
        HomeNativeWindowsBootstrap? actual;
        lock (_sync)
        {
            if (_closing || _originalProcessRetiring || _start?.IsCompletedSuccessfully != true || _bootstrap is null) return false;
            actual = _bootstrap;
        }
        try { return actual.CaptureOriginalListeningLease(new(Services, Runtime, Profiles)) == same.ActualLease; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ObjectDisposedException) { return false; }
    }
    /// <summary>No startup/lease acquisition occurs here. The original Home must
    /// already own its real successful accepting host. Root borrows this finite read.</summary>
    public Task<OriginalRootListeningObservation> ObserveOriginalRootListeningWithinSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var source = new CloudflareOriginalTaskLedger(); source.BindOriginalOwner(this);
        source.BindOriginalCallerCallback(body => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId;
            var errors = new List<Exception>();
            void Keep(Exception cause) { lock (errors) errors.Add(cause); source.Retain(cause); }
            try
            {
                try
                {
                    scope(() =>
                    {
                        if (Volatile.Read(ref active) != 1 || thread != Environment.CurrentManagedThreadId || Interlocked.Exchange(ref used, 1) != 0)
                        { var cause = new InvalidOperationException("Actual Home listener callback is inactive, foreign-thread or repeated."); Keep(cause); throw cause; }
                        try { body(); } catch (Exception cause) { Keep(cause); throw; }
                    });
                    if (used != 1) Keep(new InvalidOperationException("The actual Home listener callback was omitted."));
                }
                catch (Exception cause) { Keep(cause); }
            }
            finally { Volatile.Write(ref active, 0); }
            Exception[] all; lock (errors) all = errors.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
            if (all.Length != 0) throw new AggregateException("Original Home listener callback/body/protocol failed.", all);
            return true;
        }));
        var work = new RootListeningWork(source); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing || _originalProcessRetiring, this);
            _rootListeningOriginals.RemoveAll(old => old.Healthy);
            if (_rootListeningOriginals.Count >= 128) throw new InvalidOperationException("Unresolved actual Home listener reads remain retained.");
            work.Driver = Observe(start.Task); _rootListeningOriginals.Add(work);
            RetainOriginalHomeProcessSource(work.Driver);
        }
        try { source.Invoke(() => { retain(work.Driver); return true; }); } catch (Exception cause) { source.Retain(cause); }
        finally { start.SetResult(); }
        return work.Driver;

        async Task<OriginalRootListeningObservation> Observe(Task gate)
        {
            await gate.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            OriginalRootListeningObservation? result = null;
            try
            {
                HomeNativeWindowsBootstrap? actualBootstrap = null;
                source.Invoke(() =>
                {
                    lock (_sync)
                    {
                        if (_closing || _originalProcessRetiring || _start?.IsCompletedSuccessfully != true || _bootstrap is null)
                            throw new UnauthorizedAccessException("The actual successful Home process bootstrap is required.");
                        actualBootstrap = _bootstrap;
                    }
                    return true;
                });
                var actor = await Read(() => Profiles.GetCurrentWithinOriginalSourceAsync(Run, Keep, token)).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The actual Home profile is unavailable.");
                var first = source.Invoke(() => actualBootstrap!.CaptureOriginalListeningLease(new(Services, Runtime, Profiles)));
                if (actor.ProfileId != first.ProfileId || actor.OrganisationId is not null || first.LeaseIdentity == Guid.Empty)
                    throw new UnauthorizedAccessException("The actual Home lease does not belong to its current local actor.");
                if (actor != await Read(() => Profiles.GetCurrentWithinOriginalSourceAsync(Run, Keep, token)).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The actual Home actor changed while observing its listener.");
                source.Invoke(() =>
                {
                    lock (_sync)
                    {
                        if (_closing || _originalProcessRetiring || !ReferenceEquals(_bootstrap, actualBootstrap))
                            throw new UnauthorizedAccessException("The original Home process retired during listener observation.");
                    }
                    token.ThrowIfCancellationRequested();
                    if (actualBootstrap!.CaptureOriginalListeningLease(new(Services, Runtime, Profiles)) != first)
                        throw new UnauthorizedAccessException("The actual Home listening lease changed.");
                    result = new(this, actor, first); return true;
                });
            }
            catch (Exception cause) { source.Retain(cause); }
            await source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (source.OriginalErrors.Count != 0) throw new AggregateException("Actual Home listener/lease observation failed.", source.OriginalErrors);
            _rootListeningIssued.Add(result!, work); return result!;
        }
        void Run(Action body) => source.Invoke(() => { body(); return true; });
        void Keep(Task raw) { _ = source.Track(raw); retain(raw); }
        async Task<T> Read<T>(Func<Task<T>> acquire)
        {
            Task<T>? raw = null; Exception? publication = null;
            try { source.Invoke(() => { raw = acquire(); Keep(raw); return true; }); }
            catch (Exception cause) { source.Retain(cause); publication = cause; }
            T value = default!; if (raw is not null) value = await source.AwaitAsync(raw).ConfigureAwait(false);
            if (publication is not null) throw publication;
            return raw is null ? throw new InvalidOperationException("The actual Home listener source returned no original Task.") : value;
        }
    }
    private async Task JoinOriginalRootListeningAsync(List<Exception> failures)
    {
        RootListeningWork[] actuals; lock (_sync) actuals = _rootListeningOriginals.ToArray();
        foreach (var work in actuals)
        {
            try { await work.Driver.ConfigureAwait(false); }
            catch (Exception cause)
            {
                work.Source.Capture(work.Driver, cause);
                foreach (var exact in work.Source.OriginalErrors)
                    if (!failures.Any(old => ReferenceEquals(old, exact))) failures.Add(exact);
            }
            await work.Source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            foreach (var exact in work.Source.OriginalErrors)
                if (!failures.Any(old => ReferenceEquals(old, exact))) failures.Add(exact);
        }
    }

}
