#if !ANDROID
using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Trusted platform composition must supply this actual package producer.
/// Neither implementing the interface nor a registry row grants installation proof.
/// The producer binds its protected Spaces package/publisher mapping, current OS
/// installation and integrity evidence to the SAME original platform inventory.</summary>
internal interface IOriginalAssistantSpacesPackageObservationOwner
{
    HomeInstalledApplicationRegistry OriginalRegistry { get; }
    IInstalledApplicationObservationProvider OriginalInventory { get; }
    Task<InstalledApplicationReference?> ResolveOriginalSpacesAsync(AuthenticatedResourceActor actualActor,
        IReadOnlyList<InstalledApplicationReference> actualInventory, Action<Action> originalScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    Task DemandOriginalCurrentAsync(InstalledApplicationReference sameObservedSpaces,
        AuthenticatedResourceActor actualActor, Action<Action> originalScope,
        Action<Task> retainOriginalTask, CancellationToken token);
}

internal sealed class OriginalAssistantsDependencyStatus
{
    private readonly OriginalAssistantSpacesDependencyOwner? _issuer;
    internal string Code { get; }
    internal string Message { get; }
    internal InstalledApplicationReference? OriginalInstalledSpaces { get; }
    internal bool IsObserved => _issuer is not null && OriginalInstalledSpaces is not null;
    private OriginalAssistantsDependencyStatus(OriginalAssistantSpacesDependencyOwner? issuer,
        string code, string message, InstalledApplicationReference? original)
    { _issuer = issuer; Code = code; Message = message; OriginalInstalledSpaces = original; }
    internal static OriginalAssistantsDependencyStatus SetupRequired(string code = "SpacesInstallationUnavailable") =>
        new(null, code, "Open Home to install or repair Spaces, then return to Assistants. Your existing Assistants and conversations are preserved.", null);
    internal static OriginalAssistantsDependencyStatus Observed(OriginalAssistantSpacesDependencyOwner issuer,
        InstalledApplicationReference actual) => new(issuer, "SpacesDependencyObserved", "The current original Spaces dependency was observed.", actual);
    internal bool IsIssuedBy(OriginalAssistantSpacesDependencyOwner issuer) => ReferenceEquals(_issuer, issuer);
}

/// <summary>Finite observation over SAME Home actor registry and actual package producer.
/// Construction performs no discovery/installation. This owner has no default producer,
/// path/label match, package record fallback or installation mutation.</summary>
internal sealed class OriginalAssistantSpacesDependencyOwner
{
    private readonly HomeNativeWindowsComposition _home;
    private readonly HomeInstalledApplicationRegistry _registry;
    private readonly IInstalledApplicationObservationProvider _inventory;
    private readonly IOriginalAssistantSpacesPackageObservationOwner _packages;
    private readonly IInstalledApplicationObservationProvider[] _originalInventories;
    internal HomeNativeWindowsComposition OriginalHome => _home;

    internal OriginalAssistantSpacesDependencyOwner(HomeNativeWindowsComposition sameHome,
        HomeInstalledApplicationRegistry sameRegistry, IInstalledApplicationObservationProvider originalInventory,
        IOriginalAssistantSpacesPackageObservationOwner originalPackages)
    {
        _home = sameHome ?? throw new ArgumentNullException(nameof(sameHome));
        _registry = sameRegistry ?? throw new ArgumentNullException(nameof(sameRegistry));
        _inventory = originalInventory ?? throw new ArgumentNullException(nameof(originalInventory));
        _packages = originalPackages ?? throw new ArgumentNullException(nameof(originalPackages));
        if (!ReferenceEquals(_packages.OriginalRegistry, _registry) ||
            !ReferenceEquals(_packages.OriginalInventory, _inventory) ||
            !ReferenceEquals(_home.Services.GetService(typeof(IInstalledApplicationRegistry)), _registry) ||
            !ReferenceEquals(_home.Services.GetService(typeof(IInstalledApplicationOriginalActorRegistry)), _registry) ||
            !ReferenceEquals(_home.Services.GetService(typeof(IOriginalAssistantSpacesPackageObservationOwner)), _packages) ||
            _home.Services.GetService(typeof(IEnumerable<IInstalledApplicationObservationProvider>)) is not
                IEnumerable<IInstalledApplicationObservationProvider> originals ||
            !originals.Any(actual => ReferenceEquals(actual, _inventory)))
            throw new UnauthorizedAccessException("Use the SAME configured Home installed registry, real platform inventory and protected package producer.");
        _originalInventories = originals.ToArray();
    }

    internal Task<OriginalAssistantsDependencyStatus> ObserveOriginalAsync(AuthenticatedResourceActor actor,
        Action<Action> originalScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(actor); ArgumentNullException.ThrowIfNull(originalScope);
        ArgumentNullException.ThrowIfNull(retainOriginalTask);
        var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(this);
        sources.BindOriginalCallerCallback(originalScope);
        return sources.RunToOriginalSettlementAsync(async () =>
        {
            void DemandCurrent()
            {
                token.ThrowIfCancellationRequested();
                if (_home.OriginalStartTask?.IsCompletedSuccessfully != true || _home.OriginalCloseTask is not null ||
                    _home.OriginalProcessRetirementRequestTask is not null)
                    throw new ObjectDisposedException("The actual Home dependency observer is retiring.");
            }
            void Scope(Action body) => sources.Invoke(() => { DemandCurrent(); body(); return true; });
            void Retain(Task actual) { _ = sources.Track(actual); retainOriginalTask(actual); }
            async Task DemandActorAsync()
            {
                var actual = sources.Invoke(() => _home.Profiles.GetCurrentAsync(Scope, Retain, token).AsTask());
                Retain(actual);
                if (await sources.AwaitAsync(actual).ConfigureAwait(false) != actor)
                    throw new UnauthorizedAccessException("The original Spaces dependency actor changed.");
                DemandCurrent();
            }
            DemandCurrent();
            // The current canonical registry has neither this original child-source
            // contract nor its constructor-tuple proof. Do not promote its durable
            // index, outer facade Task or DI identity into installed dependency proof.
            if ((object)_registry is not IInstalledApplicationOriginalScopedActorRegistry originalRegistry ||
                !sources.Invoke(() => originalRegistry.HasOriginalComposition(_home.StateStore, _home.Profiles, _originalInventories)))
                return OriginalAssistantsDependencyStatus.SetupRequired("SpacesInstallationObserverUnavailable");
            await DemandActorAsync().ConfigureAwait(false);
            var refresh = sources.Invoke(() => originalRegistry
                .RefreshForActorWithinOriginalSourceAsync(actor, Scope, Retain, token).AsTask());
            Retain(refresh);
            var observed = await sources.AwaitAsync(refresh).ConfigureAwait(false);
            await DemandActorAsync().ConfigureAwait(false);
            var selection = sources.Invoke(() => _packages.ResolveOriginalSpacesAsync(actor, observed, Scope, Retain, token));
            Retain(selection);
            var spaces = await sources.AwaitAsync(selection).ConfigureAwait(false);
            await DemandActorAsync().ConfigureAwait(false);
            if (spaces is null) return OriginalAssistantsDependencyStatus.SetupRequired();
            if (!observed.Any(actual => ReferenceEquals(actual, spaces)) ||
                spaces.HomeProfileId != actor.ProfileId || spaces.ProviderId != _inventory.ProviderId ||
                !spaces.Enabled || !spaces.ProfileAccessible)
                throw new UnauthorizedAccessException("The actual package producer did not select its SAME current actor-bound platform observation.");
            var witness = sources.Invoke(() => _packages.DemandOriginalCurrentAsync(spaces, actor, Scope, Retain, token));
            Retain(witness); await sources.AwaitAsync(witness).ConfigureAwait(false);
            var resolve = sources.Invoke(() => originalRegistry
                .ResolveLaunchForActorWithinOriginalSourceAsync(spaces.ApplicationId, spaces.Revision, actor, Scope, Retain, token).AsTask());
            Retain(resolve);
            var current = await sources.AwaitAsync(resolve).ConfigureAwait(false);
            await DemandActorAsync().ConfigureAwait(false);
            if (current != spaces) return OriginalAssistantsDependencyStatus.SetupRequired("SpacesInstallationChanged");
            // Re-run genuine package currentness AFTER registry's refreshed OS
            // observation. This is sequential current observation, never a grant.
            var finalWitness = sources.Invoke(() => _packages.DemandOriginalCurrentAsync(spaces, actor, Scope, Retain, token));
            Retain(finalWitness); await sources.AwaitAsync(finalWitness).ConfigureAwait(false);
            await DemandActorAsync().ConfigureAwait(false);
            return OriginalAssistantsDependencyStatus.Observed(this, spaces);
        });
    }

    internal void DemandExternalOriginalRetirementJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
}

#endif
