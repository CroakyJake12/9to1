using System.Runtime.ExceptionServices;
using Haven.Application;
using NineToOne.Dulche.Den;

namespace HavenOS.Home.Core;

public sealed partial class HomePersonalDenFactory
{
    /// <summary>Same canonical Open, supplied store and Home ownership. It creates/imports
    /// nothing. Original callbacks and raw stages are retained before post-scope guards;
    /// the SAME original pre-effect refusal remains independently observable.</summary>
    public Task<HomePersonalDenSession> OpenWithinOriginalSourceAsync(Action<Action> caller,
        Action<Task> retainOriginalTask, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(caller); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        var invocation = new OriginalDenInvocation();
        var sources = new ScopedOpenSources(this, caller, retainOriginalTask);
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actual = OpenScopedPublishedAsync(begin.Task, invocation, sources, token);
        _originalDenInvocations.Add(actual, invocation); // Before any borrowed callback.
        RetainOriginalDenSessionTask(invocation, actual);
        try { sources.Publish(actual); } catch (Exception cause) { sources.Add(cause); }
        finally { begin.SetResult(); }
        return actual;
    }
    private async Task<HomePersonalDenSession> OpenScopedPublishedAsync(Task begin,
        OriginalDenInvocation invocation, ScopedOpenSources sources, CancellationToken token)
    {
        await begin.ConfigureAwait(false);
        using var original = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        HomePersonalDenSession? result = null; Exception? primary = null;
        try
        {
            sources.DemandHealthy();
            var profileSource = actors as IOriginalScopedResourceActorSource
                ?? throw new InvalidOperationException("The actual configured Home Den actor source has no scoped original producer.");
            var originalOwnership = ownership as IResourceStoreOriginalScopedOwnershipAuthority
                ?? throw new InvalidOperationException("The actual Home Den receipt owner has no scoped original producer.");
            var actor = await sources.Read(() => profileSource.GetCurrentWithinOriginalSourceAsync(
                sources.Scope, sources.Retain, token)).ConfigureAwait(false)
                ?? throw RetainOriginalPreEffectRefusal(invocation, "A current Home actor is required.");
            if (actor.AccountId is not null || actor.OrganisationId is not null)
                throw RetainOriginalPreEffectRefusal(invocation, "Personal Den access cannot infer account or organisation permissions.");
            var current = await sources.Read(() => provider.Store.ReadAuthoritySnapshotWithinOriginalSourceAsync(
                sources.Scope, sources.Retain, sources.CleanupScope, token)).ConfigureAwait(false);
            var binding = await sources.Read(() => originalOwnership.GetVerifiedWithinOriginalSourceAsync("den", current.DenId,
                sources.Scope, sources.Retain, token).AsTask()).ConfigureAwait(false);
            if (binding is null || binding.Receipt is null || binding.ResourceKind != "den" || binding.StoreId != current.DenId ||
                binding.ProfileId != actor.ProfileId || !await sources.Read(() => originalOwnership.IsCurrentWithinOriginalSourceAsync(
                    binding, actor, sources.Scope, sources.Retain, token).AsTask()).ConfigureAwait(false))
                throw RetainOriginalPreEffectRefusal(invocation, "This Den requires a current verified Home ownership binding.");
            result = sources.Invoke(() => CaptureOriginalDenSession(invocation, actor, current.DenId, binding,
                new DulcheDen(provider.Store, new PersonalPolicy(provider.Store, ownership, actor, binding), actor.ActorId)));
        }
        catch (Exception cause) { primary = cause; }
        await sources.Settle(invocation, primary).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("No canonical Home Den session was returned.");
    }
    private sealed class ScopedOpenSources
    {
        private readonly HomePersonalDenFactory _owner; private readonly Action<Task> _retain;
        private readonly object _gate = new(); private readonly List<Task> _raw = []; private readonly List<Exception> _errors = [];
        private readonly HomeOwnershipOriginalSourceCallbacks _protocol;
        internal ScopedOpenSources(HomePersonalDenFactory owner, Action<Action> caller, Action<Task> retain)
        {
            _owner = owner; _retain = retain;
            // Physical marker surrounds the entire borrowed scope. Restoring a captured
            // ExecutionContext inside that callback cannot erase the actual source frame.
            _protocol = new(body => Physical(() => { caller(body); return true; }), Retain);
        }
        internal T Physical<T>(Func<T> body) => CloudflareOriginalExecutionGuard.InvokeOriginal(_owner, body);
        internal void Scope(Action body) => Physical(() => { _protocol.Run(body); return true; });
        internal void CleanupScope(Action body) => Physical(() => { body(); return true; });
        internal void Add(Exception cause) { lock (_gate) if (!_errors.Any(value => ReferenceEquals(value, cause))) _errors.Add(cause); }
        internal void DemandHealthy()
        {
            Exception[] errors; lock (_gate) errors = _errors.Concat(_protocol.Errors).Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
            if (errors.Length != 0) throw new AggregateException("Prior actual Den Open source/callback failure refuses another factory.", errors);
        }
        internal T Invoke<T>(Func<T> factory) { DemandHealthy(); return _protocol.Invoke(factory); }
        internal Task<T> Read<T>(Func<Task<T>> factory) { DemandHealthy(); return _protocol.ReadAsync(factory); }
        internal void Publish(Task driver) => Scope(() => _retain(driver)); // Never a child of its own ledger.
        internal void Retain(Task raw)
        {
            lock (_gate) if (!_raw.Any(value => ReferenceEquals(value, raw))) _raw.Add(raw);
            Physical(() => { _retain(raw); return true; });
        }
        private void Capture(Task raw, Exception cause)
        { if (raw.Exception is { } group) foreach (var direct in group.InnerExceptions) Add(direct); else Add(cause); }
        internal async Task Settle(OriginalDenInvocation invocation, Exception? primary)
        {
            Task[] tasks; lock (_gate) tasks = _raw.ToArray();
            foreach (var raw in tasks) try { await raw.ConfigureAwait(false); } catch (Exception cause) { Capture(raw, cause); }
            foreach (var cause in _protocol.Errors) Add(cause);
            Exception[] errors; lock (_gate) errors = _errors.ToArray();
            // A sole refusal created by this exact invocation, with every actual raw stage
            // healthy, retains its original direct identity for existing canonical observers.
            if (primary is not null && ReferenceEquals(primary, invocation.PreEffectRefusal) && errors.Length == 0 && tasks.All(raw => raw.IsCompletedSuccessfully))
                ExceptionDispatchInfo.Capture(primary).Throw();
            if (primary is not null) Add(primary);
            lock (_gate) errors = _errors.ToArray();
            // Cancellation and borrowed scope failures remain distinct raw occurrences;
            // exception class/token equality alone cannot acknowledge an independent OCE.
            if (errors.Length != 0) throw new AggregateException("Actual canonical Home Den Open source/raw custody failed.", errors);
        }
    }
}
