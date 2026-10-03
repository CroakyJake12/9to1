using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using HavenOS.Home.Core;
using NineToOne.Web.Services;

namespace NineToOne.Web.Accounts;

/// <summary>Account presentation within the existing Home Settings route; transport remains the real acknowledged service client.</summary>
public sealed class AccountSettingsFeature : IHomeFeatureRouteHandler, IDisposable
{
    public const string ViewId = "home.settings.account";
    public string RouteId => HomeFeatureRouteIds.Settings;
    private readonly CuiDocument _document;
    private readonly IAccountBrowserTransport _transport;
    private readonly Func<Action, Task> _present;
    private readonly Func<CancellationToken, Task>? _signIn;
    private readonly ConcurrentDictionary<string, PendingSurface> _pending = new(StringComparer.Ordinal);
    private volatile bool _disposed;

    public AccountSettingsFeature(CuiDocument document, IAccountBrowserTransport transport,
        Func<Action, Task> present, Func<CancellationToken, Task>? signIn = null)
    { _document = document; _transport = transport; _present = present; _signIn = signIn; }

    [SupportedOSPlatform("browser")]
    public static AccountSettingsFeature CreateForBrowser(Func<Action, Task> present,
        Func<CancellationToken, Task>? signIn = null)
    {
        using var stream = typeof(AccountSettingsFeature).Assembly.GetManifestResourceStream("NineToOne.Web.Accounts.cui")
            ?? throw new InvalidOperationException("The account CUI resource is unavailable.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "Accounts.cui");
        if (parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidOperationException("The account CUI resource is incompatible.");
        return new(document, new BrowserAccountTransport(), present, signIn);
    }

    public async Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed || request.RouteId != RouteId)
            return new(false, "HomeServiceUnavailable", "Account settings are unavailable.", request);
        // The acknowledged Home Settings navigation is the untargeted owner route.
        // No targeted account ID, action, model-picker context or nested deep-link contract exists here.
        // Preserve unsupported requests instead of displaying a different surface or executing an action.
        if (request.EntityType is not null || request.EntityId is not null || request.Action is not null ||
            request.ModelPickerTarget is not null || request.DeepLink is not null)
            return new(false, "HomeFeatureTargetUnsupported", "This account settings target is unavailable. Your link has been preserved.", request);
        var bindings = new AccountBrowserBindings(_transport, _present, _signIn);
        try
        {
            await bindings.DispatchAsync("Refresh", null, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) { bindings.Dispose(); return new(false, "HomeServiceUnavailable", "Account settings are unavailable.", request); }
            // This is a transient presentation lease, never a canonical account/object identifier or authentication revision.
            var presentationId = Guid.NewGuid().ToString("N");
            var pending = new PendingSurface(bindings);
            if (!_pending.TryAdd(presentationId, pending)) throw new InvalidOperationException("Duplicate presentation lease.");
            pending.Attach(cancellationToken, () => RemovePending(presentationId));
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) { RemovePending(presentationId); return new(false, "HomeServiceUnavailable", "Account settings are unavailable.", request); }
            return new(true, "AccountSurfaceReady", "Account settings presentation ready.", request,
                ViewState: new(RouteId, ViewId, 0, JsonSerializer.SerializeToElement(new { presentationId })));
        }
        catch { bindings.Dispose(); throw; }
    }

    public BrowserCuiSurface Render(HomeFeatureViewState state)
    {
        if (_disposed || state.RouteId != RouteId || state.ViewId != ViewId || state.State.ValueKind != JsonValueKind.Object ||
            !state.State.TryGetProperty("presentationId", out var id) || id.ValueKind != JsonValueKind.String ||
            !_pending.TryRemove(id.GetString()!, out var pending))
            throw new InvalidOperationException("The account presentation is unavailable or has already been consumed.");
        pending.Detach();
        return new(_document, pending.Bindings, pending.Bindings, pending.Bindings);
    }

    private void RemovePending(string id)
    { if (_pending.TryRemove(id, out var pending)) { pending.Detach(); pending.Bindings.Dispose(); } }
    public void Dispose()
    {
        _disposed = true;
        foreach (var id in _pending.Keys) RemovePending(id);
    }

    private sealed class PendingSurface(AccountBrowserBindings bindings)
    {
        private readonly object _sync = new();
        private CancellationTokenRegistration _registration;
        private bool _detached;
        public AccountBrowserBindings Bindings { get; } = bindings;
        public void Attach(CancellationToken token, Action cancel)
        {
            var registration = token.Register(cancel);
            lock (_sync)
            {
                if (_detached) registration.Unregister();
                else _registration = registration;
            }
        }
        public void Detach()
        {
            CancellationTokenRegistration registration;
            lock (_sync) { _detached = true; registration = _registration; _registration = default; }
            registration.Unregister();
        }
    }
}
