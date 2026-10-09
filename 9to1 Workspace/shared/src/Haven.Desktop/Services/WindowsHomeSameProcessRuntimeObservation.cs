using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>A trusted Home-domain observation of the SAME process-owned runtime.
/// This is separate from installed-peer IPC compatibility and issues no frame,
/// resource, Task, account, permission or execution authority.</summary>
internal static class WindowsHomeSameProcessRuntimeObservation
{
    private static readonly HomeServiceRequirement[] RequiredServices =
        [new("home.core", 1), new("home.state", 1), new("permissions.trust", 1)];

    internal static void DemandOriginalBinding(IServiceProvider provider, HomeNativeWindowsComposition home)
    {
        ArgumentNullException.ThrowIfNull(provider); ArgumentNullException.ThrowIfNull(home);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The same-process Windows Home route requires Windows.");
        _ = provider.RequireOriginalWindowsHomeComponents(home);
        if (!ReferenceEquals(provider.GetRequiredService<HomeCoreRuntime>(), home.Runtime) ||
            !ReferenceEquals(provider.GetRequiredService<HomeCoreApi>(), home.Api) ||
            !ReferenceEquals(provider.GetRequiredService<IHomeCoreApi>(), home.Api) ||
            !ReferenceEquals(provider.GetRequiredService<ITrustedHostPrincipalSource>(),
                home.Services.GetService(typeof(ITrustedHostPrincipalSource))))
            throw new UnauthorizedAccessException("Retain the SAME actual Windows Home runtime, API and OS principal source.");
    }

    // Consumers call this after their own last actor/context/source read. Revalidate
    // the actual Home again at the native publication boundary; an earlier observation
    // never keeps a now-unavailable service Ready or upgrades an unavailable attempt.
    internal static CuiSceneAvailability RevalidateBeforePublication(IServiceProvider provider,
        HomeNativeWindowsComposition home, CuiSceneAvailability observed)
    {
        DemandOriginalBinding(provider, home);
        if (home.OriginalStartTask is not { IsCompletedSuccessfully: true } ||
            home.OriginalCloseTask is not null || home.OriginalProcessRetirementRequestTask is not null)
            throw new ObjectDisposedException("The original Windows Home runtime is no longer current.");
        if (observed.State != CuiSceneAvailabilityState.Ready) return observed;
        return ObserveRequiredServices(home.Runtime.Current);
    }

    private static CuiSceneAvailability ObserveRequiredServices(HomeCoreStateSnapshot snapshot)
    {
        if (snapshot.Revision < 0 || snapshot.Services is null || snapshot.Services.Count > 128 ||
            snapshot.Services.Any(service => service is null || service.Revision < 0 ||
                string.IsNullOrWhiteSpace(service.ServiceId) || !Enum.IsDefined(service.State)) ||
            snapshot.Services.Select(service => service.ServiceId).Distinct(StringComparer.Ordinal).Count() != snapshot.Services.Count)
            throw new InvalidDataException("The actual Home runtime supplied invalid service observations.");
        foreach (var requirement in RequiredServices)
        {
            var service = snapshot.Services.SingleOrDefault(row => row.ServiceId == requirement.ServiceId);
            if (service is null || !requirement.Accepts(service.ContractVersion) ||
                !service.IsAvailable || service.State != HomeServiceLifecycleState.Ready)
                return new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable,
                    "SameProcessHomeServiceUnready", "A required original Home service is unavailable or incompatible.");
        }
        return new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "SameProcessHomeRuntimeCurrent",
            "The original Windows Home runtime and OS-local profile are current.");
    }
    internal static Task<CuiSceneAvailability> CheckAsync(IServiceProvider provider,
        HomeNativeWindowsComposition home, CancellationToken originalAppLifetime,
        CancellationToken originalWindowLifetime, Action<Action> runWithinOriginalSource,
        Action demandOriginalPublication, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(runWithinOriginalSource);
        ArgumentNullException.ThrowIfNull(demandOriginalPublication);
        if (!originalAppLifetime.CanBeCanceled || !originalWindowLifetime.CanBeCanceled)
            throw new ArgumentException("Borrow the actual App and native window work lifetimes.");
        var originals = new CloudflareOriginalTaskLedger();
        originals.BindOriginalOwner(home);
        originals.BindOriginalCallerCallback(runWithinOriginalSource);
        return originals.RunToOriginalSettlementAsync(async () =>
        {
            using var homePhase = CloudflareOriginalExecutionGuard.EnterOriginal(home);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token,
                originalAppLifetime, originalWindowLifetime);
            var currentToken = lifetime.Token;
            void Demand()
            {
                currentToken.ThrowIfCancellationRequested(); demandOriginalPublication();
                DemandOriginalBinding(provider, home);
                if (home.OriginalCloseTask is not null || home.OriginalProcessRetirementRequestTask is not null)
                    throw new ObjectDisposedException("The original Windows Home process is retiring.");
                currentToken.ThrowIfCancellationRequested(); demandOriginalPublication();
            }
            Demand();
            var start = home.OriginalStartTask;
            if (start is null || !start.IsCompletedSuccessfully)
                return new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable,
                    "SameProcessHomeNotStarted", "The original Windows Home services have not completed startup.");

            void OwnSource(Action body) => originals.Invoke(() => { Demand(); body(); return true; });
            void Retain(Task actual) { _ = originals.Track(actual); }
            async Task<AuthenticatedResourceActor> ReadProfileAsync()
            {
                var actor = await originals.AwaitAsync(originals.Invoke(() =>
                    home.Profiles.GetCurrentAsync(OwnSource, Retain, currentToken).AsTask())).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The original OS-local Home profile is unavailable.");
                Demand();
                if (string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.ProfileId) ||
                    string.IsNullOrWhiteSpace(actor.AuthenticationRevision) || actor.AccountId is not null || actor.OrganisationId is not null)
                    throw new UnauthorizedAccessException("The same-process Home route requires its actual local OS profile.");
                return actor;
            }
            var actor = await ReadProfileAsync().ConfigureAwait(false);
            var before = originals.Invoke(() => { Demand(); return home.Runtime.Current; });
            if (await ReadProfileAsync().ConfigureAwait(false) != actor)
                throw new UnauthorizedAccessException("The original Home profile changed during runtime observation.");
            var after = originals.Invoke(() => { Demand(); return home.Runtime.Current; });
            if (before.Revision != after.Revision)
                return new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable,
                    "SameProcessHomeRuntimeChanged", "The original Home service registry changed; refresh its current observation.");
            if (await ReadProfileAsync().ConfigureAwait(false) != actor)
                throw new UnauthorizedAccessException("The original Home profile changed before native publication.");
            Demand();
            if (!ReferenceEquals(start, home.OriginalStartTask) || !start.IsCompletedSuccessfully)
                throw new InvalidOperationException("The original Home startup owner changed.");
            // Re-observe after the final principal read. A known late service change
            // cannot publish the earlier Ready snapshot. There is no cached Ready flag.
            var final = originals.Invoke(() => { Demand(); return home.Runtime.Current; });
            if (final.Revision != after.Revision)
                return new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable,
                    "SameProcessHomeRuntimeChanged", "The original Home service registry changed before publication.");
            Demand();
            return ObserveRequiredServices(final);
        });
    }
}
