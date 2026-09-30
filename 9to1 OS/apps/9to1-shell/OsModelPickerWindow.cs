using Avalonia.Controls;
using Avalonia.Threading;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace NineToOne.Os.Shell;

/// <summary>Windows over Home's canonical views and existing leased graph; no second authority.</summary>
internal static class OsModelPickerWindow
{
    public static async Task OpenAsync(IServiceProvider services, CancellationToken ct)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            Window? window = null;
            var surface = new HomeModelPickerCuiSurface(services.GetRequiredService<HomeCoreRuntime>(),
                services.GetRequiredService<HomeLocalProfileIdentity>(), services.GetRequiredService<IHomeModelPickerFeatureProvider>(),
                (pending, token) => Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    using var permissions = new HomeApprovalCuiSurface(services.GetRequiredService<HomeCoreRuntime>(),
                        services.GetRequiredService<HomeLocalProfileIdentity>(), services.GetRequiredService<HomePermissionTrustService>());
                    await permissions.InitializeAsync(token);
                    await permissions.FocusRequestAsync(pending, token);
                    var dialog = new Window { Title = "Home permissions", Width = 880, Height = 720, Content = permissions };
                    using var cancel = token.Register(() => Dispatcher.UIThread.Post(dialog.Close));
                    await dialog.ShowDialog(window!);
                }));
            try
            {
                window = new Window { Title = "Personal AI models", Width = 1100, Height = 760, Content = surface };
                window.Closed += (_, _) => surface.Dispose();
                await surface.InitializeAsync(ct);
                ct.ThrowIfCancellationRequested();
                window.Show();
            }
            catch { window?.Close(); surface.Dispose(); throw; }
        });
    }
}
