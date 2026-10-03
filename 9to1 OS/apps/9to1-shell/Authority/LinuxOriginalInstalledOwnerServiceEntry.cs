using System.Net.Sockets;
using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
namespace NineToOne.Os.Shell.Authority;

// Explicit owner-only entry AFTER actual early controlled-child privilege boundary.
// Trusted Main supplies the genuine root-client factory; wire/configuration cannot supply it.
internal static class LinuxOriginalInstalledOwnerServiceEntry
{
    internal static async Task RunAsync(string protectedRootSocket, string routedWidgetSocket,
        Func<string, IAuthenticatedResourceActorSource, CancellationToken,
            Task<ILinuxOriginalInstalledOwnerAdministratorClient?>> connectOriginalAdministrator,
        CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() || !Path.IsPathFullyQualified(protectedRootSocket) ||
            Path.GetFullPath(protectedRootSocket) != protectedRootSocket ||
            !LinuxRootOwnedFiles.DirectoryImmutable(Path.GetDirectoryName(protectedRootSocket)!) ||
            !Path.IsPathFullyQualified(routedWidgetSocket) || Path.GetFullPath(routedWidgetSocket) != routedWidgetSocket)
            throw new UnauthorizedAccessException("Explicit original administrator and routing endpoints required.");
        ArgumentNullException.ThrowIfNull(connectOriginalAdministrator);
        var services = new ServiceCollection();
        services.AddHavenInfrastructure();
        services.AddSingleton<IInstalledApplicationObservationProvider, LinuxInstalledApplications>();
        using var provider = services.BuildServiceProvider();
        var actors = provider.GetRequiredService<IAuthenticatedResourceActorSource>();
        var registry = provider.GetRequiredService<IInstalledApplicationRegistry>();
        var principals = provider.GetRequiredService<ITrustedHostPrincipalSource>();
        // This process never composes OsSessionHome, acquires a Home lease, or starts GUI services.
        await using var administrator = await connectOriginalAdministrator(protectedRootSocket, actors, ct)
            .ConfigureAwait(false) ?? throw new UnauthorizedAccessException("Genuine original root owner admission unavailable.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, administrator.OriginalLifetime);
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(routedWidgetSocket), deadline.Token).ConfigureAwait(false);
        }
        // Routing is not authority. The connection independently checks actual kernel peer
        // equals SAME privately issued Home context and genuine installed-host tuple.
        var startup = await LinuxOriginalInstalledOwnerStartup.ConnectAsync(socket, administrator, actors,
            principals, administrator.CreateInstalledOwnerVerifier(registry), administrator.CreateOriginalHomeHostVerifier(),
            new HomeNativeSessionHostRequirement("os.shell", "desktop:9to1-os-shell.desktop"), registry, lifetime.Token)
            .ConfigureAwait(false) ?? throw new UnauthorizedAccessException("Original installed owner backend unavailable.");
        Exception? primary = null;
        try { await startup.ServeCapturesAsync().ConfigureAwait(false); }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            try { await startup.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanupFailure) when (primary is not null)
            { throw new AggregateException("Original owner serve and exact cleanup both failed.", primary, cleanupFailure); }
        }
    }
}
