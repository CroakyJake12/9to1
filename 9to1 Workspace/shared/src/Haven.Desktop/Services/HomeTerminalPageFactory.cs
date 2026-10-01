using System.Runtime.InteropServices;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Terminal;
using Haven.Infrastructure.Terminal;
using HavenOS.Apps.Terminal;
using HavenOS.Apps.Terminal.NativeUI;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>Composes the actual installed local backend with the current canonical Home graph.</summary>
internal static class HomeTerminalPageFactory
{
    public static async Task<HomeTerminalCuiPage> OpenAsync(IServiceProvider services,
        Func<PermissionMode> commandPermission, Func<string, CancellationToken, Task> reviewPermissions,
        string? initialDirectory, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        token.ThrowIfCancellationRequested();
        var viewport = TerminalViewportCapability.Check();
        if (!viewport.Available) throw new NotSupportedException(viewport.Message);
        var processFactory = new PlatformPtyProcessFactory();
        if (!processFactory.IsSupported)
            throw new NotSupportedException(processFactory.UnavailableReason ?? "The installed interactive terminal backend is unavailable.");
        var actors = services.GetRequiredService<IAuthenticatedResourceActorSource>();
        var authority = new TerminalLocalEnvironmentAuthority(services.GetRequiredService<IHomeCoreStateStore>(), actors,
            services.GetRequiredService<HomeLocalProfileIdentity>());
        var lease = await authority.GetOrCreateAsync(token);
        var environment = new TerminalEnvironmentDescriptor(lease.EnvironmentId, processFactory.AdapterName,
            TerminalEnvironmentKind.LocalHost, "Local terminal", TerminalEnvironmentConnectionState.Ready,
            RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            TerminalEnvironmentCapability.InteractivePty | TerminalEnvironmentCapability.Resize | TerminalEnvironmentCapability.Signals,
            IsElevated: OperatingSystem.IsLinux() && EffectiveUserId() == 0, SecurityContext: lease.Actor.ActorId);
        var sessions = new HomeBoundTerminalSessionFactory(new PtyTerminalSessionFactory(environment, processFactory), authority, lease);
        TerminalAppSurface? surface = null;
        HomeTerminalCuiPage? page = null;
        try
        {
            surface = new(new(sessions, commandPermission, services.GetRequiredService<TerminalCommandActivityHub>(),
                services.GetService<ITerminalAdviceService>(), services.GetRequiredService<ITerminalActionBroker>()), initialDirectory);
            if (surface.InteractiveSession is null)
                throw new NotSupportedException(surface.UnavailableReason ?? "The interactive terminal session is unavailable.");
            page = new(surface, authority, lease, services.GetRequiredService<TerminalOwnedSessionRegistry>(),
                new HomeProfileCuiReadiness(services.GetRequiredService<HomeCoreRuntime>(), actors), reviewPermissions);
            await page.InitializeAsync(token);
            return page;
        }
        catch
        {
            if (page is not null) page.Dispose();
            else surface?.Dispose();
            throw;
        }
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint EffectiveUserId();
}
