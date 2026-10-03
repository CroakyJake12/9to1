using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace HavenOS.AIStudio;

public sealed class StudioNativeApplication : Application
{
    private ServiceProvider? _services;
    private StudioNativeWindow? _window;
    private bool _closing, _closed;
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Studio");
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            services.AddHavenInfrastructure();
            services.AddSingleton(provider => new StudioDenLifetime(provider.GetRequiredService<IAuthenticatedResourceActorSource>(),
                ct => _window?.RetireWorkspaceAsync(ct) ?? Task.CompletedTask));
            services.AddSingleton<IHomeLocalStoreEvidenceProvider>(provider => provider.GetRequiredService<StudioDenLifetime>());
            _services = services.BuildServiceProvider();
            _window = new StudioNativeWindow(_services);
            desktop.MainWindow = _window;
            desktop.ShutdownRequested += async (_, args) =>
            {
                if (_closed) return;
                args.Cancel = true;
                if (_closing) return;
                _closing = true;
                try
                {
                    await _window.RetireWorkspaceAsync(default);
                    if (_services is not null) await _services.DisposeAsync();
                }
                finally { _closed = true; desktop.Shutdown(); }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
