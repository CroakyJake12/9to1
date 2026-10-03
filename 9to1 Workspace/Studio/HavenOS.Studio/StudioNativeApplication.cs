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
    private StudioOriginalTaskDrain? _shutdown;
    private Task? _originalHostClose;
    private bool _disposingAfterOriginalDrains;

    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Studio");
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            services.AddHavenInfrastructure();
            services.AddSingleton(provider => new StudioDenLifetime(provider.GetRequiredService<IAuthenticatedResourceActorSource>(),
                ct => RetireSelectedContextAsync(provider, ct)));
            services.AddSingleton<IHomeLocalStoreEvidenceProvider>(provider => provider.GetRequiredService<StudioDenLifetime>());
            services.AddTransient<ICanonicalAgentBuilderAdapter>(provider => new DenCanonicalAgentBuilderAdapter(
                ct => provider.GetRequiredService<StudioDenLifetime>().OpenBoundSessionAsync(
                    provider.GetRequiredService<IResourceStoreOwnershipReceiptAuthority>(), ct)));
            // Ordinary Studio authoring cannot acquire Home's owning lease or issue execution authority.
            // Only an already registered trusted/remote host may supply this cleanup-only port.
            _services = services.BuildServiceProvider();
            _window = new StudioNativeWindow(_services);
            _window.ConfigureShutdownRequest(() => RequestShutdownAsync(desktop));
            desktop.MainWindow = _window;
            desktop.ShutdownRequested += (_, args) =>
            {
                args.Cancel = true;
                _ = RequestShutdownAsync(desktop);
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
    private Task RetireSelectedContextAsync(IServiceProvider provider, CancellationToken ct)
    {
        if (_disposingAfterOriginalDrains)
        {
            if (_window?.OriginalShutdownTasksCapturedAndSettled != true ||
                _originalHostClose is { IsCompleted: false })
                throw new InvalidOperationException("The original Studio/host tasks have not settled; retain the selected Den.");
            // Global drain has already attempted the same context and retained any cleanup failures.
            // Do not repeat a faulting retirement and prevent disposal of an already-drained borrowed Store.
            return Task.CompletedTask;
        }
        return StudioOriginalTaskDrain.RunIndependentAsync([
            () => _window?.RetireSelectedContextAsync(ct) ?? Task.CompletedTask,
            () => provider.GetService<IHomeAgentExecutionHost>()?.RetireCurrentContextAsync() ?? Task.CompletedTask]);
    }
    public Task RequestShutdownAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _shutdown ??= new StudioOriginalTaskDrain(() => { },
            [
                () => _window?.PrepareShutdownAsync(),
                () =>
                {
                    var originalHost = _services?.GetService<IHomeAgentExecutionHost>();
                    _originalHostClose = originalHost?.CloseAndDrainAsync();
                    return _originalHostClose;
                }
            ], () => { },
            [
                async () =>
                {
                    if (_shutdown?.OriginalTasksCapturedAndSettled != true ||
                        _window?.OriginalShutdownTasksCapturedAndSettled != true ||
                        _originalHostClose is { IsCompleted: false })
                        throw new InvalidOperationException("Original task capture or settlement is unproved; retain the provider.");
                    _disposingAfterOriginalDrains = true;
                    if (_services is not null) await _services.DisposeAsync();
                },
                () =>
                {
                    _window?.AllowPreparedClose();
                    desktop.Shutdown();
                    return Task.CompletedTask;
                }
            ]);
        return _shutdown.CloseAndDrainAsync();
    }
    /// <summary>Called only after the native loop returned. Never block a stopped dispatcher.</summary>
    public void RequireOriginalShutdownSettled()
    {
        if (_shutdown is null)
            throw new InvalidOperationException("The native loop ended without owned Studio shutdown.");
        var original = _shutdown.CloseAndDrainAsync();
        if (!original.IsCompleted)
            throw new InvalidOperationException("The native loop ended with original Studio shutdown work pending.");
        original.GetAwaiter().GetResult();
    }
}
