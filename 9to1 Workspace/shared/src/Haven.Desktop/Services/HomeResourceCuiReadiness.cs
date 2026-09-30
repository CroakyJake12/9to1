using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Compatibility name for the canonical shared Home native readiness implementation.</summary>
public sealed class HomeResourceCuiReadiness(HomeCoreRuntime home, IAuthenticatedResourceActorSource actors,
    ResourceAuthorizationService resources, string actionId,
    Func<CancellationToken, ValueTask<IReadOnlyList<ResourceScope>>> currentScopes)
    : HavenOS.Home.NativeUI.HomeResourceCuiReadiness(home, actors, resources, actionId, currentScopes);
