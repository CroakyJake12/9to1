using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Application.Compatibility;

namespace NineToOne.Os.Shell;

/// <summary>Native read-only framework manager on the caller's existing original Home graph.</summary>
public sealed class OsCompatibilityFrameworkWindow : IDisposable
{
    private readonly CancellationTokenSource lifetime;
    private readonly CompatibilityFrameworkChoiceBindings bindings;
    private readonly CuiControlLoader loader;
    private CancellationTokenRegistration cancellation;
    private bool disposed;
    public Window Window { get; }
    private OsCompatibilityFrameworkWindow(Window window, CancellationTokenSource lifetime,
        CompatibilityFrameworkChoiceBindings bindings, CuiControlLoader loader)
    { Window = window; this.lifetime = lifetime; this.bindings = bindings; this.loader = loader; }
    public Task WhenActionsIdleAsync() => loader.WhenActionsIdleAsync();

    public static async Task<OsCompatibilityFrameworkWindow> OpenAsync(Window parent,
        LinuxApplicationLauncher launcher, CompatibilityRoutingService routing,
        IAuthenticatedResourceActorSource actors, ICuiSceneReadiness readiness,
        Guid applicationId, long revision, AuthenticatedResourceActor originalActor,
        CancellationToken hostLifetime, CancellationToken ct = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(parent); ArgumentNullException.ThrowIfNull(originalActor);
        using var initial = CancellationTokenSource.CreateLinkedTokenSource(ct, hostLifetime);
        var token = initial.Token;
        var app = await launcher.ResolveForReadForActorAsync(applicationId, revision, originalActor, token);
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(hostLifetime);
        var bindings = new CompatibilityFrameworkChoiceBindings(routing, actors, applicationId, revision, originalActor, lifetime.Token);
        var loader = new CuiControlLoader();
        var window = new Window { Title = "Application frameworks · 9to1 OS", Width = 780, Height = 620 };
        var owned = new OsCompatibilityFrameworkWindow(window, lifetime, bindings, loader);
        window.Closed += (_, _) => owned.Dispose();
        try
        {
            await bindings.RefreshAsync(token);
            await RequireReadAsync();
            loader.SetBindingContext(bindings);
            loader.SetActionDispatcher(new ReadActions(launcher, applicationId, revision, originalActor, readiness, bindings, window.Close));
            using var stream = typeof(OsCompatibilityFrameworkWindow).Assembly.GetManifestResourceStream("NineToOne.Os.Shell.UI.CompatibilityFrameworks.cui")
                ?? throw new InvalidDataException("The framework manager surface is unavailable.");
            using var reader = new StreamReader(stream);
            var (root, diagnostics) = loader.LoadMarkup(await reader.ReadToEndAsync(token));
            if (root is null || diagnostics.Any(d => d.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("The framework manager surface could not be rendered.");
            loader.WireBindings(root); window.Content = root;
            await RequireReadAsync(); token.ThrowIfCancellationRequested();
            owned.cancellation = hostLifetime.Register(() => Dispatcher.UIThread.Post(window.Close));
            window.Show(parent); window.UpdateLayout();
            return owned;
        }
        catch { window.Close(); owned.Dispose(); throw; }
        async Task RequireReadAsync()
        {
            var ready = await readiness.CheckAsync(token);
            if (ready.State != CuiSceneAvailabilityState.Ready) throw new UnauthorizedAccessException(ready.Message);
            if (await launcher.ResolveForReadForActorAsync(applicationId, revision, originalActor, token) != app)
                throw new UnauthorizedAccessException("The original installed application changed before presentation.");
        }
    }
    private sealed class ReadActions(LinuxApplicationLauncher launcher, Guid id, long revision,
        AuthenticatedResourceActor originalActor, ICuiSceneReadiness readiness, CompatibilityFrameworkChoiceBindings bindings, Action close) : ICuiActionDispatcher
    {
        public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken ct = default)
        {
            try
            {
                var ready = await readiness.CheckAsync(ct);
                if (ready.State != CuiSceneAvailabilityState.Ready) throw new UnauthorizedAccessException(ready.Message);
                await launcher.ResolveForReadForActorAsync(id, revision, originalActor, ct);
            }
            catch { close(); throw; } // Revoked original read must not leave a mounted stale proposal.
            await bindings.DispatchAsync(command, parameter, ct);
        }
    }
    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (disposed) return; disposed = true;
        cancellation.Dispose(); lifetime.Cancel(); bindings.Dispose(); loader.Dispose();
        Window.Content = null; lifetime.Dispose();
    }
}
