using Dulche.Runtime;
using Haven.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure;

/// <summary>Opt-in composition. Reads only explicit process-local configuration; no source,
/// account, context, model availability or capability is inferred from these values.</summary>
public static class LocalLlamaCppRegistration
{
    public static LlamaCppLocalEndpointOptions ReadExplicitEnvironment(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        return new(
            Enabled: bool.TryParse(read("HAVEN_LOCAL_LLAMA_ENABLED"), out var enabled) && enabled,
            SocketPath: read("HAVEN_LOCAL_LLAMA_SOCKET"),
            ExpectedExecutableSha256: read("HAVEN_LOCAL_LLAMA_EXECUTABLE_SHA256"),
            ModelPath: read("HAVEN_LOCAL_LLAMA_MODEL"),
            ExpectedModelSha256: read("HAVEN_LOCAL_LLAMA_MODEL_SHA256"),
            ModelAlias: read("HAVEN_LOCAL_LLAMA_ALIAS"));
    }

    /// <summary>Call once from Root's real AddHavenInfrastructure. Same provider/source and
    /// same Dulche catalogue resolver; all actors, stores, frame owners and policy stay existing.</summary>
    public static IServiceCollection AddHavenObservedLocalLlamaCpp(this IServiceCollection services,
        LlamaCppLocalEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton(options);
        services.AddSingleton<LlamaCppModelProvider>();
        services.AddSingleton<IModelProvider>(provider => provider.GetRequiredService<LlamaCppModelProvider>());
        services.AddSingleton<ILocalModelEndpointObservationSource>(provider => provider.GetRequiredService<LlamaCppModelProvider>());
        services.AddSingleton<ModelRouteResolver>();
        services.AddSingleton<IProviderCatalogueEligibility>(provider => provider.GetRequiredService<ModelRouteResolver>());
        return services;
    }

    /// <summary>Preview composition for the app-relative local chat bootstrap on the confirmed Linux
    /// arm64 target. It reconciles the canonical bundle manifest/receipt and the canonical per-user
    /// modelctl store (no invented schema), feeds the SAME routed provider (LlamaCppModelProvider) and
    /// the SAME owned child-process runtime (LlamaCppRuntime), and exposes an async lifecycle that the
    /// App/Home original lifetime invokes. Discovery/hashing is NEVER performed synchronously during DI
    /// (no GetAwaiter().GetResult()); it runs in StartIfReadyAsync after normal startup. When assets are
    /// absent/unverified the provider/runtime stay disabled (actionable degraded); no fake catalogue or
    /// model is ever registered.</summary>
    public static IServiceCollection AddHavenPreviewLocalLlamaCpp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(_ => new LlamaCppPreviewBootstrap());
        services.AddSingleton<LlamaCppPreviewBootstrapState>();

        // SAME provider/source/catalogue as the explicit-option endpoint path; no duplicate engine id.
        // The provider is constructed (possibly during ConfigureServices registry/authority resolution)
        // with the current state snapshot, but it is handed the shared verified state and binds the
        // verified preview options EXACTLY ONCE before any peer/work/probe use. This is what lets the
        // early-constructed provider observe the post-bootstrap verified state rather than permanently
        // snapshotting the initial disabled default. Explicit-option callers pass no state and are
        // unaffected.
        services.AddSingleton<LlamaCppModelProvider>(provider =>
        {
            var state = provider.GetRequiredService<LlamaCppPreviewBootstrapState>();
            return new LlamaCppModelProvider(
                state.Endpoint,
                provider.GetRequiredService<IProviderConfigurationStore>(),
                state);
        });
        services.AddSingleton<IModelProvider>(provider => provider.GetRequiredService<LlamaCppModelProvider>());
        services.AddSingleton<ILocalModelEndpointObservationSource>(provider => provider.GetRequiredService<LlamaCppModelProvider>());
        services.AddSingleton<ModelRouteResolver>();
        services.AddSingleton<IProviderCatalogueEligibility>(provider => provider.GetRequiredService<ModelRouteResolver>());

        // The owned child runtime is constructed from the verified options only when the lifecycle
        // starts it (async, after discovery). It is registered so the App/Home composition owns the
        // drain; the provider does not consume this instance (it reaches the worker socket directly).
        services.AddSingleton<LlamaCppPreviewLifecycle>();
        return services;
    }
}

/// <summary>Thread-safe holder for the latest verified discovery/options. The provider and the
/// lifecycle read it at resolution time, so no synchronous discovery happens during DI construction.</summary>
public sealed class LlamaCppPreviewBootstrapState
{
    private readonly object _gate = new();
    private LlamaCppPreviewDiscovery _discovery =
        new(false, null, null, null, null, null, null, null, null, []);
    private LlamaCppLocalEndpointOptions _endpoint = new();
    private LlamaCppRuntimeOptions _runtime = new();

    public LlamaCppPreviewDiscovery Discovery { get { lock (_gate) return _discovery; } }
    public LlamaCppLocalEndpointOptions Endpoint { get { lock (_gate) return _endpoint; } }
    public LlamaCppRuntimeOptions Runtime { get { lock (_gate) return _runtime; } }

    public void Apply(LlamaCppPreviewDiscovery discovery, LlamaCppLocalEndpointOptions endpoint,
        LlamaCppRuntimeOptions runtime)
    {
        lock (_gate)
        {
            _discovery = discovery; _endpoint = endpoint; _runtime = runtime;
        }
    }
}

/// <summary>Owns discovery, the verified child startup, retained status/failure and exact drain. A
/// normal App/Home composition root calls <see cref="StartIfReadyAsync"/> after application startup so
/// capability probes (which run inside the routed provider on first use) see a genuinely loaded process.</summary>
public sealed class LlamaCppPreviewLifecycle : IAsyncDisposable
{
    private readonly LlamaCppPreviewBootstrap _bootstrap;
    private readonly LlamaCppPreviewBootstrapState _state;
    private readonly IProviderConfigurationStore _configurationStore;
    private readonly IServiceProvider _services;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _readinessTimeout;
    private readonly TimeSpan _readinessPollInterval;
    private LlamaCppRuntime? _runtime;
    private LlamaCppRuntimeStatus? _startStatus;
    private Exception? _failure;
    private bool _ready;

    private const int DefaultReadinessTimeoutSeconds = 60;
    private const int DefaultReadinessPollMilliseconds = 150;

    public LlamaCppPreviewLifecycle(
        LlamaCppPreviewBootstrap bootstrap,
        LlamaCppPreviewBootstrapState state,
        IProviderConfigurationStore configurationStore,
        IServiceProvider services,
        TimeSpan? readinessTimeout = null,
        TimeSpan? readinessPollInterval = null)
    {
        _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _configurationStore = configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _readinessTimeout = readinessTimeout ?? TimeSpan.FromSeconds(DefaultReadinessTimeoutSeconds);
        _readinessPollInterval = readinessPollInterval ?? TimeSpan.FromMilliseconds(DefaultReadinessPollMilliseconds);
    }

    public LlamaCppPreviewBootstrapState State => _state;

    /// <summary>Last retained start status, or null before start / when unavailable/degraded.</summary>
    public LlamaCppRuntimeStatus? StartStatus => _startStatus;

    /// <summary>Retained startup failure (if any). A genuine failure is never silently retried on a
    /// later call; it stays the SAME observed fault.</summary>
    public Exception? Failure => _failure;

    /// <summary>True only after the owned child started and genuine text/stream/tool capability probes
    /// on the SAME process succeeded. Never set without a real, probed load.</summary>
    public bool Ready => _ready;

    /// <summary>Discovers the canonical assets (async; no DI-time hashing), adopts the bundled default
    /// model on a fresh user store, seeds the legitimate local provider admission under existing
    /// configuration ownership, starts the owned child on the worker socket, and runs genuine
    /// text/stream/tool capability probes on the SAME process before exposing readiness. Unavailable/
    /// unverified assets leave the provider/runtime disabled (actionable degraded); cancellation
    /// propagates and is retained.</summary>
    public async Task StartIfReadyAsync(CancellationToken cancellationToken)
    {
        var acquired = false;
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            if (_runtime is not null || _failure is not null) return;

            var discovery = await _bootstrap.DiscoverAsync(cancellationToken).ConfigureAwait(false);

            // Fresh-user boundary: adopt the bundled default model only when no verified model exists
            // yet. Existing user selection is preserved by the importer (no overwrite).
            if (discovery.ModelPath is null)
            {
                var seed = await _bootstrap.EnsureDefaultModelImportedAsync(cancellationToken).ConfigureAwait(false);
                if (seed.IsImported)
                    discovery = await _bootstrap.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            }

            var endpoint = _bootstrap.BuildEndpointOptions(discovery);
            var runtimeOptions = _bootstrap.BuildRuntimeOptions(discovery);
            _state.Apply(discovery, endpoint, runtimeOptions);
            if (!discovery.Available) { _ready = false; return; } // honest degraded; no fake ready

            // Legitimate local provider admission under existing configuration ownership (respects an
            // existing user configuration, including a disabled one).
            await _bootstrap.EnsureProviderAdmissionAsync(_configurationStore, endpoint, cancellationToken).ConfigureAwait(false);

            var runtime = new LlamaCppRuntime(runtimeOptions);
            try
            {
                var status = await runtime.StartIfAlwaysLoadedAsync(cancellationToken).ConfigureAwait(false);
                _startStatus = status;
                if (!status.Running)
                {
                    await runtime.DisposeAsync().ConfigureAwait(false);
                    _ready = false;
                    return;
                }
                _runtime = runtime;

                // Genuine capability probes on the SAME started process BEFORE readiness, but only
                // AFTER the worker is genuinely ready. A normal model load takes seconds; probing
                // right after Process.Start races a missing socket/503 and would mislabel a healthy
                // start as a failed child disposal.
                var provider = _services.GetRequiredService<LlamaCppModelProvider>();
                await WaitForWorkerReadinessAsync(provider, runtime, endpoint.SocketPath!, cancellationToken).ConfigureAwait(false);
                await provider.ProbeOriginalCapabilitiesAsync(includeAutomaticToolProbe: true, cancellationToken).ConfigureAwait(false);
                _ready = true;
            }
            catch (Exception failure)
            {
                _failure = failure;
                _ready = false;
                await runtime.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch (OperationCanceledException cancellation)
        {
            _failure ??= cancellation;
            throw;
        }
        finally
        {
            if (acquired) _gate.Release();
        }
    }

    /// <summary>Bounded, cancellation-aware readiness wait for the owned worker. It uses the routed
    /// provider's OWNED typed readiness acquisition: an exact "Loading model" 503 and a pre-binding socket
    /// connect while the SAME owned child is still live are reported as benign preparing/Starting values
    /// (never a durable fault), and only a genuinely ready health+model-identity observation returns.
    /// Genuine faults (500/unexpected-503/invalid-peer/IO/foreign-cancel/wrong-peer) propagate and remain
    /// durably retained. Capability probes run after this returns. Process.Start, metadata, state.Enabled
    /// and arbitrary delay are deliberately NOT treated as readiness.</summary>
    private async Task WaitForWorkerReadinessAsync(
        LlamaCppModelProvider provider,
        LlamaCppRuntime runtime,
        string socketPath,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(_readinessTimeout);
        var starting = new OwnedStartReadinessContext(socketPath, () => runtime.Status.Running);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Stop promptly when the owned child actually exits.
            if (!runtime.Status.Running)
                throw new InvalidOperationException("The local llama.cpp worker exited before it became ready.");

            var readiness = await provider.ObserveOriginalEndpointReadinessAsync(cancellationToken, starting).ConfigureAwait(false);
            if (readiness.Ready)
                return; // real socket reachable and the expected model identity is observed

            // Preparing (model still loading / socket not yet bound). Bound the retry by a real deadline
            // so a stalled worker cannot hang startup forever; cancellation and child exit are re-checked
            // next iteration.
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("The local llama.cpp worker did not become ready within the readiness window.");
            await Task.Delay(_readinessPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class OwnedStartReadinessContext(string socketPath, Func<bool> isStarting) : ILlamaCppStartReadinessContext
    {
        public string SocketPath => socketPath;
        public bool IsOwnedChildStarting => isStarting();
    }

    public ValueTask DisposeAsync()
    {
        _gate.Wait();
        try
        {
            var runtime = _runtime; _runtime = null;
            return runtime is not null ? runtime.DisposeAsync() : default;
        }
        finally
        {
            _gate.Release();
        }
    }
}
