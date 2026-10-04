using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using HavenOS.Home.Core;
using NineToOne.Web.Services;

namespace NineToOne.Web.Accounts;

/// <summary>Account presentation within the existing Home Settings route; transport remains the real acknowledged service client.</summary>
public sealed class AccountSettingsFeature : IHomeFeatureRouteHandler, IDisposable, IBrowserPrivateContextParticipant
{
    public const string ViewId = "home.settings.account";
    public string RouteId => HomeFeatureRouteIds.Settings;
    private readonly CuiDocument _document;
    private readonly IAccountBrowserTransport _transport;
    private readonly Func<Action, Task> _present;
    private readonly Func<CancellationToken, Task>? _signIn;
    private readonly ConcurrentDictionary<string, PendingSurface> _pending = new(StringComparer.Ordinal);
    private volatile bool _disposed;
    private readonly object _ownership = new();
    // Every binding is enrolled BEFORE its first service call and remains in the
    // ledger after Render consumes its lease or IDisposable fences its view.
    private readonly HashSet<AccountBrowserBindings> _issuedBindings = [];
    private readonly HashSet<PendingSurface> _issuedLeases = [];
    private readonly HashSet<Task> _issuedFactories = [];
    private Task? _cleanup;
    public IReadOnlyList<Task> BrokerOwnedContinuations
    { get { lock (_ownership) return _issuedBindings.SelectMany(binding => binding.BrokerOwnedContinuations).ToArray(); } }
    public bool HasOutstandingBrokerWork => BrokerOwnedContinuations.Any(task => !task.IsCompleted);

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

    public Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request,
        CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<HomeFeatureNavigationResult> completion;
        AccountBrowserBindings bindings;
        lock (_ownership)
        {
            if (_disposed) return Task.FromResult(new HomeFeatureNavigationResult(false,
                "HomeServiceUnavailable", "Account settings are unavailable.", request));
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            bindings = new(_transport, _present, _signIn);
            _issuedBindings.Add(bindings);
            _issuedFactories.Add(completion.Task);
        }
        _ = SettleFactoryAsync(request, cancellationToken, bindings, completion);
        return completion.Task;
    }
    private async Task SettleFactoryAsync(HomeFeatureNavigationRequest request, CancellationToken cancellationToken,
        AccountBrowserBindings bindings, TaskCompletionSource<HomeFeatureNavigationResult> completion)
    {
        try { completion.TrySetResult(await OpenCoreAsync(request, cancellationToken, bindings)); }
        catch (OperationCanceledException error) { completion.TrySetCanceled(error.CancellationToken); }
        catch (Exception error) { completion.TrySetException(error); }
        finally { lock (_ownership) _issuedFactories.Remove(completion.Task); }
    }
    private async Task<HomeFeatureNavigationResult> OpenCoreAsync(HomeFeatureNavigationRequest request,
        CancellationToken cancellationToken, AccountBrowserBindings bindings)
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
        try
        {
            await bindings.DispatchAsync("Refresh", null, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) { bindings.Dispose(); return new(false, "HomeServiceUnavailable", "Account settings are unavailable.", request); }
            // This is a transient presentation lease, never a canonical account/object identifier or authentication revision.
            var presentationId = Guid.NewGuid().ToString("N");
            var pending = new PendingSurface(bindings);
            lock (_ownership)
            {
                if (_disposed) { bindings.RevokePrivateContext(); return new(false, "HomeServiceUnavailable", "Account settings are unavailable.", request); }
                _issuedLeases.Add(pending);
                if (!_pending.TryAdd(presentationId, pending)) throw new InvalidOperationException("Duplicate presentation lease.");
            }
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
    public void RevokePrivateContext()
    {
        lock (_ownership)
        {
            if (_disposed) return;
            _disposed = true;
            _pending.Clear(); // Detach all leases WITHOUT registration/cancellation callbacks.
            foreach (var binding in _issuedBindings) binding.RevokePrivateContext();
        }
    }
    public void Dispose() { RevokePrivateContext(); _ = BeginCleanup(); }
    public ValueTask DisposeAsync() { RevokePrivateContext(); return new(BeginCleanup()); }
    private Task BeginCleanup()
    {
        TaskCompletionSource completion;
        Task[] factories;
        AccountBrowserBindings[] bindings;
        PendingSurface[] leases;
        lock (_ownership)
        {
            if (_cleanup is not null) return _cleanup;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _cleanup = completion.Task;
            factories = _issuedFactories.ToArray();
            bindings = _issuedBindings.ToArray();
            leases = _issuedLeases.ToArray();
        }
        _ = DrainAsync(factories, bindings, leases, completion);
        return completion.Task;
    }
    private static async Task DrainAsync(Task[] factories, AccountBrowserBindings[] bindings,
        PendingSurface[] leases, TaskCompletionSource completion)
    {
        var errors = new List<Exception>();
        foreach (var lease in leases) { try { lease.Detach(); } catch (Exception error) { errors.Add(error); } }
        var drains = bindings.Select(binding => binding.DisposeAsync().AsTask()).Concat(factories).ToArray();
        try { await Task.WhenAll(drains); }
        catch
        {
            foreach (var task in drains)
                if (task.Exception is { } failure) errors.AddRange(failure.InnerExceptions);
            // Cancelled factories are settled; they cannot reopen old state.
        }
        if (errors.Count > 0) completion.TrySetException(new AggregateException("Revoked account owner cleanup failed.", errors));
        else completion.TrySetResult();
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
