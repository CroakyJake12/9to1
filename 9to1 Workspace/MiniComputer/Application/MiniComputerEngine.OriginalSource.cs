using System.Runtime.CompilerServices;
using Haven.Application;

namespace HavenOS.Apps.MiniComputer;

/// <summary>Original scoped consumers use the SAME canonical engine, configured
/// catalogue and provider registry. Read never calls inventory refresh, identity-map
/// creation or catalogue persistence. VM state comes from the actual selected provider.</summary>
public sealed partial class MiniComputerEngine
{
    private readonly object _originalGate = new();
    private ICanonicalMiniComputerOperationSource? _originalSource;
    private readonly ConditionalWeakTable<MiniComputerOriginalProviderTarget, OriginalDispatch> _originalTargets = new();
    private sealed class OriginalDispatch(ICanonicalMiniComputerOperationIntent intent)
    { internal ICanonicalMiniComputerOperationIntent Intent { get; } = intent; internal bool Live = true; }

    public bool HasOriginalComposition(IVirtualisationProviderRegistry providers, IMiniComputerCatalogStore store) =>
        ReferenceEquals(_providers, providers) && ReferenceEquals(_store, store);
    public bool HasOriginalOperationSource(ICanonicalMiniComputerOperationSource source)
    { lock (_originalGate) return ReferenceEquals(_originalSource, source); }
    public void BindOriginalOperationSource(ICanonicalMiniComputerOperationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_originalGate)
        {
            if (_originalSource is not null && !ReferenceEquals(_originalSource, source))
                throw new InvalidOperationException("Mini Computer is already bound to its actual operation source.");
            _originalSource = source;
        }
    }

    public async Task<MiniComputerOriginalCatalogObservation> ReadOriginalCatalogWithinSourceAsync(
        ICanonicalMiniComputerProtectedCatalogRead lease, Action<Action> scope,
        Action<Task> retain, CancellationToken token)
    {
        var source = new MiniComputerOriginalInvocation(this, scope, retain);
        MiniComputerOriginalCatalogObservation? result = null;
        using var logical = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        try
        {
            if (_store is not JsonMiniComputerCatalogStore json)
                throw new NotSupportedException("This configured Mini Computer catalogue has no original scoped reader.");
            var actual = await json.ReadOriginalWithinSourceAsync(lease, source, token).ConfigureAwait(false);
            result = new(this, actual.Snapshot, actual.Sha256, lease.OriginalIdentity);
        }
        catch (Exception cause) { source.Remember(cause); }
        await source.CloseAsync().ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("The actual Mini Computer catalogue observation is unavailable.");
    }

    public bool IsIssuedOriginalCatalogObservation(MiniComputerOriginalCatalogObservation observation) =>
        ReferenceEquals(observation.OriginalEngine, this);

    public async Task<MiniComputerOriginalProviderObservation> InvokeOriginalOperationWithinSourceAsync(
        MiniComputerOriginalCatalogObservation sameCatalog,
        ICanonicalMiniComputerOperationIntent sameIntent, Action<Action> scope,
        Action<Task> retain, CancellationToken token)
    {
        var source = new MiniComputerOriginalInvocation(this, scope, retain);
        MiniComputerOriginalProviderTarget? target = null;
        MiniComputerOriginalProviderObservation? result = null;
        using var logical = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        try
        {
            target = source.Invoke(() =>
            {
                ICanonicalMiniComputerOperationSource original;
                lock (_originalGate) original = _originalSource ?? throw new InvalidOperationException("The actual Home/product operation source is not configured.");
                if (!IsIssuedOriginalCatalogObservation(sameCatalog) || !original.IsIssuedOriginalOperationIntent(sameIntent) ||
                    sameIntent.Target.OriginalCatalogSha256 != sameCatalog.Sha256 ||
                    sameIntent.OriginalStoreIdentity != sameCatalog.StoreIdentity)
                    throw new UnauthorizedAccessException("The SAME protected catalogue and source-issued operation are required.");
                var vm = sameCatalog.Snapshot.VirtualMachines.SingleOrDefault(vm => vm.VMID.Value == sameIntent.Target.VirtualMachineId)
                    ?? throw new UnauthorizedAccessException("The selected VM is outside the original catalogue.");
                var provider = _providers.Find(vm.ProviderID)
                    ?? throw new InvalidOperationException("The selected VM's actual provider is unavailable.");
                if (vm.ProviderID.Value != sameIntent.Target.ProviderId || vm.ProviderMachineID != sameIntent.Target.ProviderMachineId ||
                    vm.ConfigurationVersion != sameIntent.Target.ConfigurationVersion || vm.Revision != sameIntent.Target.Revision)
                    throw new UnauthorizedAccessException("The original VM identity or configuration changed.");
                original.DemandOriginalOperation(sameIntent);
                target = new MiniComputerOriginalProviderTarget(this, provider, vm, sameIntent);
                lock (_originalGate) _originalTargets.Add(target, new(sameIntent));
                return target;
            });
            var actualProvider = source.Invoke(() => _providers.Find(target.Original.ProviderID) as IOriginalScopedVirtualisationProvider)
                ?? throw new NotSupportedException("The configured provider has no original scoped VM operation path.");
            result = await source.Read(() => actualProvider.InvokeOriginalTargetWithinSourceAsync(
                target, source.Run, source.Retain, token)).ConfigureAwait(false);
        }
        catch (Exception cause) { source.Remember(cause); }
        finally
        {
            if (target is not null) lock (_originalGate)
                if (_originalTargets.TryGetValue(target, out var dispatch)) dispatch.Live = false;
        }
        await source.CloseAsync().ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("The actual provider supplied no operation observation.");
    }
    internal void DemandOriginalProviderTarget(MiniComputerOriginalProviderTarget target,
        IVirtualisationProvider sameProvider, ICanonicalMiniComputerOperationIntent sameIntent)
    {
        ICanonicalMiniComputerOperationSource source;
        lock (_originalGate)
        {
            if (!_originalTargets.TryGetValue(target, out var dispatch) || !dispatch.Live ||
                !ReferenceEquals(dispatch.Intent, sameIntent) ||
                !ReferenceEquals(_providers.Find(target.Original.ProviderID), sameProvider))
                throw new UnauthorizedAccessException("The original canonical VM dispatch is not live.");
            source = _originalSource ?? throw new UnauthorizedAccessException();
        }
        source.DemandOriginalOperation(sameIntent);
    }
}

public sealed class MiniComputerOriginalCatalogObservation
{
    internal MiniComputerOriginalCatalogObservation(MiniComputerEngine engine, MiniComputerCatalogSnapshot snapshot,
        string sha256, ResourceStoreIdentity identity)
    { OriginalEngine = engine; Snapshot = snapshot; Sha256 = sha256; StoreIdentity = identity; }
    internal MiniComputerEngine OriginalEngine { get; }
    public MiniComputerCatalogSnapshot Snapshot { get; }
    public string Sha256 { get; }
    public ResourceStoreIdentity StoreIdentity { get; }
}
