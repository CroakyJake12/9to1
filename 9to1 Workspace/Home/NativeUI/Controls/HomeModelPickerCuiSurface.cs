using Avalonia.Controls;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using HavenOS.Home.Core;

namespace HavenOS.Home.NativeUI;

/// <summary>The same Home-owned personal model manager for native Home, OS and Android hosts.</summary>
public sealed class HomeModelPickerCuiSurface : UserControl, IDisposable
{
    private readonly HomeCoreRuntime _runtime;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeModelPickerBindings _bindings;
    private readonly CuiSceneHost _scene = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    public HomeModelPickerCuiSurface(HomeCoreRuntime runtime, HomeLocalProfileIdentity profiles,
        IHomeModelPickerFeatureProvider provider, Func<string, CancellationToken, Task>? openHomePermissions = null)
    {
        _runtime = runtime; _profiles = profiles; _bindings = new(provider, openHomePermissions); Content = _scene;
    }
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        using var stream = typeof(HomeModelPickerCuiSurface).Assembly.GetManifestResourceStream("HavenOS.Home.NativeUI.Resources.Cui.Models.cui")
            ?? throw new InvalidDataException("The shared Home model manager document is missing.");
        using var reader = new StreamReader(stream);
        var available = await _scene.ShowAsync(new("home.models", "Personal AI models", "Home",
            new CuiRichParser().Parse(await reader.ReadToEndAsync(request.Token)), _bindings, _bindings, new Readiness(_runtime, _profiles)), request.Token);
        if (available.State == CuiSceneAvailabilityState.Ready) await _bindings.OpenAsync(request.Token);
    }
    private sealed class Readiness(HomeCoreRuntime runtime, HomeLocalProfileIdentity profiles) : ICuiSceneReadiness
    {
        public async ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken ct)
        {
            var actor = await profiles.GetCurrentAsync(ct);
            if (actor is null) return new(CuiSceneAvailabilityState.Unavailable, "HomeProfileUnavailable", "Open Home to recover this operating-system profile.");
            var snapshot = await runtime.StartAsync(ct);
            foreach (var id in new[] { "home.core", "home.state", "permissions.trust", "models.routes" })
                if (!snapshot.Services.Any(service => service.ServiceId == id && service.IsAvailable &&
                    service.State == HomeServiceLifecycleState.Ready && service.ContractVersion.Major == HomeCoreServiceCatalog.CurrentContractVersion.Major &&
                    service.ContractVersion.Minor >= HomeCoreServiceCatalog.CurrentContractVersion.Minor))
                    return new(CuiSceneAvailabilityState.Unavailable, "HomeModelsUnavailable", "Home model routes are unavailable or need repair.");
            if (actor != await profiles.GetCurrentAsync(ct)) return new(CuiSceneAvailabilityState.Unavailable, "HomeProfileChanged", "The Home profile changed. Reopen Models.");
            return new(CuiSceneAvailabilityState.Ready, "HomeModelsReady", "Home model routes are ready.");
        }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _lifetime.Cancel(); _bindings.Dispose(); _scene.Dispose(); Content = null; _lifetime.Dispose();
    }
}
