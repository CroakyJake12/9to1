using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Application.Compatibility;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NineToOne.Os.Shell;

/// <summary>Typed original installed selection; only read-only native navigation, never launch or install.</summary>
public interface IInstalledApplicationCompatibilityNavigation
{
    Task OpenForActorAsync(Guid applicationId, long revision, AuthenticatedResourceActor originalActor, CancellationToken ct);
}

public static class OsCompatibilityManagerRegistration
{
    public static IServiceCollection AddOsCompatibilityManager(this IServiceCollection services, CancellationToken hostLifetime, Func<Window>? currentParent = null)
    {
        // A missing runtime owner must remain unavailable. A genuine owning transport may explicitly replace this before composition.
        services.TryAddSingleton<ICompatibilityApplicationOwner>(_ => new UnavailableCompatibilityApplicationOwner());
        services.TryAddSingleton<CompatibilityRoutingService>();
        services.TryAddSingleton<IInstalledApplicationCompatibilityNavigation>(sp => new OsCompatibilityManagerNavigation(
            sp.GetRequiredService<LinuxApplicationLauncher>(), sp.GetRequiredService<CompatibilityRoutingService>(),
            sp.GetRequiredService<IAuthenticatedResourceActorSource>(),
            new HomeProfileCuiReadiness(sp.GetRequiredService<HomeCoreRuntime>(), sp.GetRequiredService<IAuthenticatedResourceActorSource>()),
            currentParent ?? (() => (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
                ?? throw new InvalidOperationException("The current native shell window is unavailable.")), hostLifetime));
        return services;
    }
    private sealed class UnavailableCompatibilityApplicationOwner : ICompatibilityApplicationOwner
    {
        public ValueTask<CompatibilityApplicationObservation?> ObserveAsync(AuthenticatedResourceActor actor,
            InstalledApplicationReference application, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult<CompatibilityApplicationObservation?>(null); }
    }
}

public sealed class OsCompatibilityManagerNavigation(LinuxApplicationLauncher launcher, CompatibilityRoutingService routing,
    IAuthenticatedResourceActorSource actors, ICuiSceneReadiness readiness, Func<Window> currentParent,
    CancellationToken hostLifetime) : IInstalledApplicationCompatibilityNavigation
{
    public async Task OpenForActorAsync(Guid applicationId, long revision, AuthenticatedResourceActor originalActor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(originalActor);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct, hostLifetime);
        // Explicit outer/inner await: UI dispatch completion is not native navigation completion.
        await await Dispatcher.UIThread.InvokeAsync<Task>(() => OpenCoreAsync(request.Token));
        async Task OpenCoreAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var parent = currentParent();
            if (!parent.IsVisible) throw new InvalidOperationException("The current native shell is not visible.");
            await OsCompatibilityFrameworkWindow.OpenAsync(parent, launcher, routing, actors, readiness,
                applicationId, revision, originalActor, hostLifetime, token);
        }
    }
}
