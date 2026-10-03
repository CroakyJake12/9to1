using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Application.Compatibility;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using Microsoft.Extensions.DependencyInjection;

namespace NineToOne.Os.Shell;

/// <summary>Navigation-only OS presentation on the existing leased Home graph. Never installs or executes a package.</summary>
public sealed class OsCompatibilityPackageWindow(IServiceProvider services, CancellationToken hostLifetime) : ICompatibilityPackageOpenHandler
{
    public async Task OpenAsync(CompatibilityPackageSource selection, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.StoreId == Guid.Empty) throw new InvalidOperationException("Select this package again from its current Files store.");
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var actors = services.GetRequiredService<IAuthenticatedResourceActorSource>();
            if (await actors.GetCurrentAsync(ct) != selection.ObservedActor)
                throw new UnauthorizedAccessException("Home changed; select the application package again.");
            var main = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
                ?? throw new InvalidOperationException("The native shell is unavailable.");
            // The Files owner is mandatory: no raw-path reader or private substitute is composed here.
            var inspector = services.GetRequiredService<CompatibilityPackageInspector>();
            using var stream = typeof(OsCompatibilityPackageWindow).Assembly.GetManifestResourceStream("NineToOne.Os.Shell.UI.CompatibilityPackage.cui")
                ?? throw new InvalidDataException("The package surface is unavailable.");
            using var reader = new StreamReader(stream);
            var document = new CuiRichParser().Parse(await reader.ReadToEndAsync(ct));
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(hostLifetime);
            using var initial = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
            var window = new Window { Title = "Applications · 9to1 OS", Width = 720, Height = 520 };
            var scene = new CuiSceneHost();
            var bindings = new OsCompatibilityPackageBindings(inspector, actors, selection,
                () => Dispatcher.UIThread.Post(window.Close), lifetime.Token);
            window.Content = scene;
            CancellationTokenRegistration cancel = default;
            var disposed = false;
            void DisposeOwned()
            {
                if (disposed) return; disposed = true;
                cancel.Dispose(); lifetime.Cancel(); bindings.Dispose(); scene.Dispose(); window.Content = null; lifetime.Dispose();
            }
            window.Closed += (_, _) => DisposeOwned();
            window.Deactivated += (_, _) => bindings.SetActive(false);
            window.Activated += (_, _) => bindings.SetActive(true);
            try
            {
                await bindings.RefreshAsync(initial.Token);
                var readiness = new HomeProfileCuiReadiness(services.GetRequiredService<HomeCoreRuntime>(), actors);
                var ready = await scene.ShowAsync(new(ShellSemanticFeatureProvider.AppId, "Application package", "os.compatibility.package",
                    document, bindings, bindings, readiness), initial.Token);
                if (ready.State != CuiSceneAvailabilityState.Ready) throw new InvalidOperationException(ready.Message);
                if (await actors.GetCurrentAsync(initial.Token) != selection.ObservedActor)
                    throw new UnauthorizedAccessException("Home changed; select the application package again.");
                cancel = hostLifetime.Register(() => Dispatcher.UIThread.Post(window.Close));
                window.Show(main);
            }
            catch { window.Close(); DisposeOwned(); throw; }
        });
    }
}

public sealed class OsCompatibilityPackageBindings(CompatibilityPackageInspector inspector,
    IAuthenticatedResourceActorSource actors, CompatibilityPackageSource expected,
    Action close, CancellationToken lifetime) : ICuiBindingContext, ICuiActionDispatcher, INotifyPropertyChanged, IDisposable
{
    private readonly CuiViewModel _bindings = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;
    private bool _active = true;
    private long _generation;
    public event PropertyChangedEventHandler? PropertyChanged
    { add => _bindings.PropertyChanged += value; remove => _bindings.PropertyChanged -= value; }
    public bool TryGetValue(string path, out object? value) => _bindings.TryGetValue(path, out value);
    public void Clear()
    { _bindings.Set("Name", string.Empty); _bindings.Set("Format", string.Empty); _bindings.Set("Architecture", string.Empty); _bindings.Set("DeclaredIdentity", string.Empty); _bindings.Set("Status", string.Empty); _bindings.Set("PackageState", "Unchecked"); }
    public void Refresh() => _ = RefreshSafelyAsync();
    public void SetActive(bool active)
    {
        if (_disposed || active == _active) return;
        _active = active; _generation++;
        if (active) Refresh(); else Clear();
    }
    private async Task RefreshSafelyAsync()
    {
        try { await RefreshAsync(lifetime); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch { await Dispatcher.UIThread.InvokeAsync(() => { Clear(); close(); }); }
    }
    public async Task RefreshAsync(CancellationToken ct)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime);
        await _gate.WaitAsync(request.Token);
        var generation = _generation;
        try
        {
            if (_disposed) return;
            _bindings.Set("CanRefresh", false); _bindings.Set("PackageState", "Checking"); _bindings.Set("Status", "Checking this selected Files revision…");
            if (await actors.GetCurrentAsync(request.Token) != expected.ObservedActor)
                throw new UnauthorizedAccessException("The original Home session changed.");
            var result = await inspector.InspectAsync(expected.StoreId, expected.ObservedActor, expected.FileId, expected.ContentRevision, request.Token);
            if (expected.StoreId == Guid.Empty || result.StoreId != expected.StoreId || result.FileId != expected.FileId || result.ContentRevision != expected.ContentRevision ||
                result.MetadataRevision != expected.MetadataRevision || result.Name != expected.Name || result.Length != expected.Length ||
                !result.Sha256.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase) ||
                await actors.GetCurrentAsync(request.Token) != expected.ObservedActor)
                throw new UnauthorizedAccessException("The selected package or Home session changed. Select it again in Files.");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || !_active || generation != _generation || request.IsCancellationRequested) return;
                _bindings.Set("Name", result.Name); _bindings.Set("Format", result.Format switch { "windows-exe" => "Windows application", "windows-msi" => "Windows installer", _ => "Android application" });
                _bindings.Set("Architecture", result.Architectures.Count == 0 ? "Architecture: not resolved" : (result.Format == "android-apk" ? "Declared ABIs: " : "Architecture: ") + string.Join(", ", result.Architectures));
                _bindings.Set("DeclaredIdentity", result.DeclaredApplicationIdentity is { } id ? "Declared application: " + id : "Application identity: not verified");
                _bindings.Set("PackageState", "Inspected");
                _bindings.Set("Status", "The selected Files revision was checked. Publisher trust and installation permission remain separate.");
            });
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException)
        {
            // A format diagnosis is not a trust/launch grant. Clear all previously inspected fields.
            // Foreign actor/owner access failures continue to close the original selection below.
            try
            {
                if (await actors.GetCurrentAsync(request.Token) != expected.ObservedActor)
                    throw new UnauthorizedAccessException("The original Home session changed.");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_disposed || !_active || generation != _generation || request.IsCancellationRequested) return;
                    Clear(); _bindings.Set("PackageState", error is NotSupportedException ? "Unsupported" : "Invalid");
                    _bindings.Set("Status", error is NotSupportedException
                        ? "This package format is unsupported. Close this view and choose a supported EXE, MSI or APK in Files."
                        : "This package could not be inspected safely. Close this view and choose an intact package in Files. Existing files were preserved.");
                });
            }
            catch { await Dispatcher.UIThread.InvokeAsync(() => { Clear(); close(); }); throw; }
        }
        catch { await Dispatcher.UIThread.InvokeAsync(() => { Clear(); close(); }); throw; }
        finally { if (!_disposed) _bindings.Set("CanRefresh", true); _gate.Release(); }
    }
    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken ct = default)
    {
        if (_disposed) return;
        if (command == "Close") { Clear(); close(); }
        else if (command == "Refresh") await RefreshAsync(ct);
    }
    public void Dispose() { if (_disposed) return; _disposed = true; Clear(); }
}
