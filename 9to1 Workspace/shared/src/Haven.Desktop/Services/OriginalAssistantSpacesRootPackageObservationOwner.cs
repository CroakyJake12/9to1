#if !ANDROID
using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Infrastructure.Native.Windows;
using HavenOS.Home.Core;
namespace Haven.Desktop.Services;

/// <summary>Dedicated Spaces dependency producer over actual protected single-installer
/// activation. Canonical index rows remain locators; each success also holds a private
/// source-issued fresh root/publisher/payload witness. No default installed bootstrap.</summary>
internal sealed class OriginalAssistantSpacesRootPackageObservationOwner :
    IOriginalAssistantSpacesPackageObservationOwner, IAsyncDisposable
{
    private readonly NativeWindowsHomeRootClient _activation;
    public HomeInstalledApplicationRegistry OriginalRegistry { get; }
    public IInstalledApplicationObservationProvider OriginalInventory => _activation;
    private readonly ConditionalWeakTable<InstalledApplicationReference, NativeWindowsHomeRootClient.PackageObservation> _issued = new();
    private readonly object _gate = new(); private readonly List<Invocation> _accepted = [];
    private bool _retiring; private Task? _close;
    private sealed class Invocation(CloudflareOriginalTaskLedger source)
    {
        internal readonly CloudflareOriginalTaskLedger Source = source;
        internal Task Driver = null!;
        internal bool IsHealthy => Driver.IsCompletedSuccessfully && Source.OriginalErrors.Count == 0 && Source.OriginalTasks.All(value => value.IsCompletedSuccessfully);
    }
    internal OriginalAssistantSpacesRootPackageObservationOwner(HomeInstalledApplicationRegistry sameRegistry,
        NativeWindowsHomeRootClient actualActivation, HomeLocalProfileIdentity actualProfiles,
        FileHomeCoreStateStore actualRegistryStore)
    {
        ArgumentNullException.ThrowIfNull(actualActivation); ArgumentNullException.ThrowIfNull(sameRegistry);
        if (!actualActivation.HasOriginalHomeInventory(actualRegistryStore, actualProfiles, sameRegistry) ||
            !sameRegistry.HasOriginalComposition(actualRegistryStore, actualProfiles, [actualActivation]))
            throw new UnauthorizedAccessException("The actual canonical Spaces activation and SAME index/profile/inventory composition are required.");
        OriginalRegistry = sameRegistry; _activation = actualActivation;
    }
    public Task<InstalledApplicationReference?> ResolveOriginalSpacesAsync(AuthenticatedResourceActor actor,
        IReadOnlyList<InstalledApplicationReference> inventory, Action<Action> scope, Action<Task> retain, CancellationToken token)
        => Admit(scope, retain, async source =>
        {
            var actual = await Read(source, retain, () => _activation.ObserveOriginalPackageWithinSourceAsync(actor, "spaces",
                body => source.Invoke(() => { body(); return true; }), raw => Keep(source, retain, raw), token)).ConfigureAwait(false);
            if (actual is null) return null;
            if (!_activation.IsIssuedOriginalPackageObservation(actual) || actual.Actor != actor || actual.AppId != "spaces")
                throw new UnauthorizedAccessException("The actual protected source issued another product or actor.");
            var candidates = source.Invoke(() => inventory.Take(100001).Where(value => value.HomeProfileId == actor.ProfileId &&
                value.ProviderId == _activation.ProviderId && value.PlatformProfileId == actual.OsPrincipal &&
                value.OsApplicationId == "9to1.package:" + actual.PackageId && value.Entrypoint == actual.Entrypoint &&
                value.Version == actual.Version && value.StableLaunchIdentity == actual.AppId && value.Enabled && value.ProfileAccessible).ToArray());
            if (candidates.Length == 0) return null;
            var selected = candidates.Length == 1 ? candidates[0] : throw new UnauthorizedAccessException("Canonical Spaces index has ambiguous original activation locators.");
            await Read(source, retain, () => _activation.DemandOriginalPackageCurrentWithinSourceAsync(actual,
                body => source.Invoke(() => { body(); return true; }), child => Keep(source, retain, child), token)).ConfigureAwait(false);
            source.Invoke(() => { _issued.Remove(selected); _issued.Add(selected, actual); return true; }); return selected;
        });
    public Task DemandOriginalCurrentAsync(InstalledApplicationReference same, AuthenticatedResourceActor actor,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
        => Admit(scope, retain, async source =>
        {
            if (!_issued.TryGetValue(same, out var original) || original.Actor != actor || same.HomeProfileId != actor.ProfileId)
                throw new UnauthorizedAccessException("The SAME privately selected Spaces index/source pair is required.");
            await Read(source, retain, () => _activation.DemandOriginalPackageCurrentWithinSourceAsync(original,
                body => source.Invoke(() => { body(); return true; }), child => Keep(source, retain, child), token)).ConfigureAwait(false);
            return true;
        });
    private Task<T> Admit<T>(Action<Action> scope, Action<Task> retain, Func<CloudflareOriginalTaskLedger, Task<T>> body)
    {
        TaskCompletionSource start; Invocation invocation; Task<T> actual;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this); _accepted.RemoveAll(value => value.IsHealthy);
            if (_accepted.Count >= 128) throw new InvalidOperationException("Unresolved actual Spaces dependency sources must remain retained.");
            var source = new CloudflareOriginalTaskLedger(); source.BindOriginalOwner(this);
            source.BindOriginalCallerCallback(borrowed => WithinBorrowed(source, scope, borrowed));
            invocation = new(source); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = Drive(start.Task, source, body); invocation.Driver = actual; _accepted.Add(invocation);
        }
        try { invocation.Source.Invoke(() => { retain(actual); return true; }); }
        catch (Exception cause) { invocation.Source.Retain(cause); }
        finally { start.SetResult(); }
        return actual;
    }
    private async Task<T> Drive<T>(Task start, CloudflareOriginalTaskLedger source, Func<CloudflareOriginalTaskLedger, Task<T>> body)
    {
        await start.ConfigureAwait(false); using var original = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        T result = default!;
        try { result = await body(source).ConfigureAwait(false); } catch (Exception cause) { source.Retain(cause); }
        await source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (source.OriginalErrors.Count != 0) throw new AggregateException("Actual scoped Spaces producer failed.", source.OriginalErrors);
        return result;
    }
    private void WithinBorrowed(CloudflareOriginalTaskLedger source, Action<Action> scope, Action body)
    {
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            var thread = Environment.CurrentManagedThreadId; var active = 1; var used = 0;
            try
            {
                scope(() =>
                {
                    try
                    {
                        if (active != 1 || thread != Environment.CurrentManagedThreadId || Interlocked.Exchange(ref used, 1) != 0)
                            throw new InvalidOperationException("Actual Spaces source callback is inactive, foreign-thread or consumed.");
                        if (source.OriginalErrors.Count != 0) throw new AggregateException("Prior actual Spaces source failed.", source.OriginalErrors);
                        body();
                    }
                    catch (Exception cause) { source.Retain(cause); throw; }
                });
                if (used != 1) throw new InvalidOperationException("The actual Spaces callback was not invoked.");
            }
            finally { Interlocked.Exchange(ref active, 0); }
            return true;
        });
    }
    private static void Keep(CloudflareOriginalTaskLedger source, Action<Task> retain, Task actual) { source.Track(actual); retain(actual); }
    private static async Task<T> Read<T>(CloudflareOriginalTaskLedger source, Action<Task> retain, Func<Task<T>> factory)
    {
        Task<T>? actual = null; Exception? scope = null;
        try { source.Invoke(() => { actual = factory(); Keep(source, retain, actual); return true; }); }
        catch (Exception cause) { source.Retain(cause); scope = cause; }
        T result = default!; if (actual is not null) result = await source.AwaitAsync(actual).ConfigureAwait(false);
        if (scope is not null) throw scope; return actual is null ? throw new InvalidOperationException("No actual dependency source Task returned.") : result;
    }
    private static async Task Read(CloudflareOriginalTaskLedger source, Action<Task> retain, Func<Task> factory)
    {
        Task? actual = null;
        try { source.Invoke(() => { actual = factory(); Keep(source, retain, actual); return true; }); }
        catch (Exception cause) { source.Retain(cause); }
        if (actual is not null)
            try { await source.AwaitAsync(actual).ConfigureAwait(false); } catch (Exception cause) { source.Capture(actual, cause); }
        if (source.OriginalErrors.Count != 0) throw new AggregateException("Actual Spaces source/raw callback failed.", source.OriginalErrors);
        if (actual is null) throw new InvalidOperationException("No actual Spaces currentness source Task returned.");
    }
    internal Task? OriginalClose { get { lock (_gate) return _close; } }
    internal void DemandExternalOriginalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    internal Task CloseAndDrainAsync()
    {
        DemandExternalOriginalJoin(); TaskCompletionSource? start = null; Task actual;
        lock (_gate)
        {
            if (_close is null) { _retiring = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Close(start.Task, _accepted.ToArray()); }
            actual = _close;
        }
        start?.SetResult(); return actual;
    }
    private static async Task Close(Task start, Invocation[] original)
    {
        await start.ConfigureAwait(false); var errors = new List<Exception>();
        foreach (var invocation in original) try { await invocation.Driver.ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(invocation.Driver.Exception ?? cause); }
        if (errors.Count != 0) throw new AggregateException("Actual Spaces dependency observation failed; sources retained.", errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
#endif
