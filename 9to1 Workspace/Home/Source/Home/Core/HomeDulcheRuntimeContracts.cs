namespace HavenOS.Home.Core;

/// <summary>Validation helpers for the shared local Dulche runtime. A verified artifact SHA-256
/// is always 64 lowercase hex digits; a string of length 64 that is not hex (or that was never
/// actually matched against the artifact bytes) is NOT verification.</summary>
public static class HomeDulcheHash
{
    public static bool IsValidSha256Hex(string? value)
    {
        if (value is not { Length: 64 }) return false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
        }
        return true;
    }
}

/// <summary>Genuinely verified local model evidence. This is installation evidence only: it is
/// never a permission, an execution grant, or proof that the native backend is running or capable.
/// It is produced only when the recorded artifact hash is a valid SHA-256 that was actually matched
/// by re-hashing the installed artifact bytes.</summary>
public sealed record HomeDulcheModelEvidence(
    string ModelId,
    string ProviderId,
    string ArtifactSha256,
    long SizeBytes,
    DateTimeOffset VerifiedAtUtc)
{
    public bool IsVerifiedArtifact => HomeDulcheHash.IsValidSha256Hex(ArtifactSha256);
}

/// <summary>A single truthful discovery result for the shared Home-owned Dulche runtime. Each axis
/// is an independent, genuinely observed boolean; the lifecycle maps to the canonical
/// <see cref="HomeServiceLifecycleState"/> used by the Home service registry. Verification of one
/// axis never implies another (installed ≠ verified ≠ running ≠ capable ≠ Ready).</summary>
public sealed record HomeDulcheRuntimeObservation(
    HomeServiceLifecycleState Lifecycle,
    bool RuntimeInstalled,
    bool RuntimeVerified,
    bool ModelInstalled,
    bool ModelVerified,
    bool ProcessRunning,
    bool EndpointAvailable,
    bool TransportSupported,
    bool CapabilitySupported,
    string Platform,
    string Architecture,
    string? EligibleModelId,
    IReadOnlyList<string> Deficiencies);

/// <summary>Owns discovery/verification of the shared local Dulche runtime. Reading this surface
/// never starts a process, downloads a model or grants a capability. A platform with no supported
/// adapter remains explicitly unsupported and never reports Ready.</summary>
public interface IHomeDulcheRuntimeObservationSource
{
    Task<HomeDulcheRuntimeObservation> ObserveAsync(CancellationToken cancellationToken);
}

/// <summary>Resolves and verifies the installed runtime package for the current platform. A result
/// is Verified only when the backend executable hash was actually computed and matched a pinned
/// value; missing/altered/incompatible artifacts produce a structured recoverable result.</summary>
public interface IHomeDulcheRuntimePackageSource
{
    ValueTask<HomeDulchePackageResult> ProbeAsync(CancellationToken cancellationToken);
}

public sealed record HomeDulchePackageResult(
    bool Installed,
    bool Verified,
    string? ExecutablePath,
    string? ExecutableSha256,
    IReadOnlyList<string> Deficiencies);

/// <summary>A bounded process/transport owner behind typed OS/package adapters. An adapter may attach
/// to an already-running supported broker (borrowed ownership) or own a process it started; it never
/// fabricates a running observation. A platform with no adapter reports Supported=false.</summary>
public interface IHomeDulcheBrokerAdapter
{
    string Platform { get; }
    bool Supported { get; }
    ValueTask<HomeDulcheBrokerObservation?> ObserveAsync(CancellationToken cancellationToken);
}

/// <summary>Positive current process/endpoint observation. This is only published after the adapter
/// actually observes a live broker process and a healthy endpoint; it is never inferred from the
/// platform name. An AF_UNIX transport records the real socket path in <see cref="SocketPath"/>
/// and leaves <see cref="Endpoint"/> null; an HTTP loopback transport records <c>Endpoint</c>.
/// A single observation may only claim one transport kind.</summary>
public sealed record HomeDulcheBrokerObservation(
    long BrokerPid,
    string EndpointKind,
    Uri? Endpoint,
    DateTimeOffset ObservedAtUtc,
    string? SocketPath = null)
{
    public bool IsUnixSocket => string.Equals(EndpointKind, "unix-socket", StringComparison.Ordinal)
        && SocketPath is { Length: > 0 };
}

/// <summary>Obtains only registered, verified, eligible local model artifacts. Implementations must
/// preserve the canonical model/provider/artifact identity and an explicit selection, and must not
/// fabricate a model or a hash.</summary>
public interface IHomeDulcheModelEvidenceSource
{
    /// <summary>Returns the eligible verified local model for the current route (an explicit
    /// selection when one exists, otherwise an installed eligible default), or null when none is
    /// installed and verified. Never returns unverified or non-hex evidence.</summary>
    ValueTask<HomeDulcheModelEvidence?> FindEligibleAsync(CancellationToken cancellationToken);
}

/// <summary>Typed record of an already-admitted, same-generation generation-capability observation.
/// This is NEVER produced by initiating inference during Home startup; it records an explicit
/// model-use admission that already verified generation capability for the SAME observed broker
/// endpoint/process and SAME verified model. It is bound to the observed <see cref="HomeDulcheBrokerObservation"/>
/// and <see cref="HomeDulcheModelEvidence"/> via the matching model id and provider id. When no such
/// admission observation is supplied, the capability axis stays explicitly blocked (never inferred).</summary>
public sealed record HomeDulcheCapabilityObservation(
    bool GenerationCapable,
    string ProviderId,
    string ModelId,
    DateTimeOffset ObservedAtUtc,
    string Evidence,
    HomeDulcheBrokerObservation? Broker = null,
    string? ModelArtifactSha256 = null)
{
    /// <summary>True only when this admission was recorded for the SAME observed broker process/
    /// endpoint and the SAME verified model artifact as the current observation. Same provider/model
    /// names alone never prove the same generation; an absent genuine broker/artifact binding fails
    /// CLOSED (the capability axis stays blocked).</summary>
    public bool IsSameGeneration(HomeDulcheModelEvidence model, HomeDulcheBrokerObservation? broker)
    {
        if (model is null) return false;
        if (!string.Equals(ProviderId, model.ProviderId, StringComparison.Ordinal)) return false;
        if (!string.Equals(ModelId, model.ModelId, StringComparison.Ordinal)) return false;
        if (!SameBroker(broker)) return false;
        if (ModelArtifactSha256 is null ||
            !string.Equals(ModelArtifactSha256, model.ArtifactSha256, StringComparison.Ordinal))
            return false;
        return true;
    }

    private bool SameBroker(HomeDulcheBrokerObservation? observed)
    {
        if (Broker is null || observed is null) return false;
        if (Broker.BrokerPid != observed.BrokerPid) return false;
        if (!string.Equals(Broker.EndpointKind, observed.EndpointKind, StringComparison.Ordinal)) return false;
        if (Broker.IsUnixSocket || observed.IsUnixSocket)
            return string.Equals(Broker.SocketPath, observed.SocketPath, StringComparison.Ordinal);
        return Broker.Endpoint is not null && Broker.Endpoint.Equals(observed.Endpoint);
    }
}
