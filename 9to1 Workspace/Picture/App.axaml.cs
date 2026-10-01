using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Haven.Infrastructure;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace HavenOS.Images;

public sealed partial class App : Application
{
    private ServiceProvider? _services;
    private bool _shutdownApproved;
    private bool _shutdownInProgress;
    public override void Initialize() => CakeOS.Cui.Runtime.CuiNativeHost.InitialisePrimitiveTheme(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            services.AddHavenInfrastructure();
            services.AddFilesNativeHost();
            services.AddSingleton<IHomeActionPolicySource, PictureNativeActionPolicies>();
            _services = services.BuildServiceProvider();
            desktop.MainWindow = new MainWindow(_services);
            desktop.ShutdownRequested += async (_, args) =>
            {
                if (_shutdownApproved) return;
                args.Cancel = true;
                if (_shutdownInProgress) return;
                _shutdownInProgress = true;
                var owned = _services; _services = null;
                try { if (owned is not null) await owned.DisposeAsync(); }
                finally { _shutdownApproved = true; desktop.Shutdown(); }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
