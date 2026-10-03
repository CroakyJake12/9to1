using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Home.NativeUI;

/// <summary>Current Home profile/service readiness only. This does not grant access to app resources,
/// launch a session or approve an action; each owning surface supplies its separate authority checks.</summary>
public sealed class HomeProfileCuiReadiness : ICuiSceneReadiness
{
    private readonly HomeCoreRuntime _runtime;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly HomeServiceRequirement[] _requirements;
    public HomeProfileCuiReadiness(HomeCoreRuntime runtime, IAuthenticatedResourceActorSource actors,
        IEnumerable<HomeServiceRequirement>? additionalRequirements = null)
    {
        _runtime = runtime; _actors = actors;
        var version = HomeCoreServiceCatalog.CurrentContractVersion;
        _requirements = new[] { "home.core", "home.state", "permissions.trust" }
            .Select(id => new HomeServiceRequirement(id, version.Major, version.Minor))
            .Concat((additionalRequirements ?? []).Take(98)).ToArray();
        if (_requirements.Length > 100 || _requirements.Any(item => item is null || string.IsNullOrWhiteSpace(item.ServiceId)))
            throw new ArgumentException("Home readiness requirements must be bounded and explicit.", nameof(additionalRequirements));
    }
    public async ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken ct)
    {
        try
        {
            var actor = await _actors.GetCurrentAsync(ct).ConfigureAwait(false);
            if (actor is null) return Unavailable("HomeProfileUnavailable", "Open Home to recover this profile.");
            var snapshot = await _runtime.StartAsync(ct).ConfigureAwait(false);
            foreach (var requirement in _requirements)
            {
                var matches = snapshot.Services.Where(item => item.ServiceId == requirement.ServiceId).ToArray();
                if (matches.Length == 0 && !requirement.Required) continue;
                if (matches.Length != 1 || !matches[0].IsAvailable || matches[0].State != HomeServiceLifecycleState.Ready ||
                    !requirement.Accepts(matches[0].ContractVersion))
                    return Unavailable("HomeServicesUnavailable", "Required Home services are unavailable or need recovery.");
            }
            if (actor != await _actors.GetCurrentAsync(ct).ConfigureAwait(false) || _runtime.Current.Revision != snapshot.Revision)
                return Unavailable("HomeProfileChanged", "Home changed while opening this view. Reopen it with the current profile.");
            return new(CuiSceneAvailabilityState.Ready, "HomeProfileReady", "Home is ready for this profile.");
        }
        catch (UnauthorizedAccessException) { return Unavailable("HomeProfileUnavailable", "Current Home profile access is unavailable."); }
        catch (Exception error) when (error is IOException or InvalidDataException)
        { return Unavailable("HomeRecoveryRequired", "Home needs recovery. Existing data was preserved."); }
    }
    private static CuiSceneAvailability Unavailable(string code, string message) => new(CuiSceneAvailabilityState.Unavailable, code, message);
}
