namespace Haven.Application;

/// <summary>Reasoning/thinking mode for a llama.cpp chat template. Unset keeps the engine
/// default (template/auto-detected) so explicit callers are unaffected.</summary>
public enum LlamaCppReasoningMode
{
    Unset,
    Auto,
    On,
    Off
}

/// <summary>
/// Opt-in configuration for a Haven-owned llama.cpp server process.
/// The runtime is disabled by default and never modifies machine-wide services.
/// </summary>
public sealed record LlamaCppRuntimeOptions(
    bool Enabled = false,
    string? ExecutablePath = null,
    string? ModelPath = null,
    bool UseUnifiedCli = false,
    int Port = 18080,
    int ContextSize = 32768,
    int ParallelRequests = 1,
    bool AlwaysLoaded = false,
    string? ModelAlias = null,
    string? SocketPath = null,
    bool? UseJinja = null,
    LlamaCppReasoningMode Reasoning = LlamaCppReasoningMode.Unset);

/// <summary>
/// Reports the bounded state of the Haven-owned llama.cpp child process.
/// </summary>
public sealed record LlamaCppRuntimeStatus(
    bool Enabled,
    bool Running,
    bool AlwaysLoaded,
    Uri? Endpoint,
    int? ProcessId = null,
    string? Detail = null);

/// <summary>
/// Owns only a llama.cpp process started by Haven. It does not manage OS services,
/// drivers, power plans, process priorities, or unrelated model runtimes.
/// </summary>
public interface ILlamaCppRuntime : IAsyncDisposable
{
    LlamaCppRuntimeOptions Options { get; }
    LlamaCppRuntimeStatus Status { get; }
    Task<LlamaCppRuntimeStatus> StartIfAlwaysLoadedAsync(CancellationToken cancellationToken);
    Task<LlamaCppRuntimeStatus> EnsureStartedAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
