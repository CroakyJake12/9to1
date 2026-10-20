namespace HavenOS.Home.Core;

/// <summary>Issues a typed generation-capability receipt ONLY from genuine owning engine evidence:
/// a current verified generation-capability observation that is itself bound to the SAME observed
/// broker process/endpoint generation and SAME verified model artifact. It never imports an unbound
/// boolean/string into a fresh admission, never rebinds stale evidence to a different broker
/// generation/artifact, and never fabricates a capability, a broker or a hash. When the owning
/// evidence is absent, unbound or no longer same-generation, it returns null so the capability axis
/// stays explicitly blocked (Degraded) — it is never inferred from names or platform.</summary>
public interface IHomeDulcheCapabilityReceiptIssuer
{
    /// <summary>Affirms a verified generation-capability observation that is genuinely bound to the
    /// CURRENT observed broker generation and verified model artifact, returning the SAME original
    /// observation (preserving its custody/timestamp), or null when the evidence cannot be affirmed.</summary>
    HomeDulcheCapabilityObservation? Issue(
        HomeDulcheCapabilityObservation? currentCapability,
        HomeDulcheModelEvidence? model,
        HomeDulcheBrokerObservation? broker);
}

/// <summary>Default implementation of <see cref="IHomeDulcheCapabilityReceiptIssuer"/>.
/// The owning engine evidence IS the supplied capability observation together with its own
/// broker/artifact binding. A restarted broker (new PID) or a replaced artifact makes stale evidence
/// visibly stale instead of being rebound to look current; the original observable timestamp/custody
/// is preserved by returning the same instance. The positive production axis stays BLOCKED until
/// such genuine owned evidence exists; this component is the unblocked issuer/validation contract.</summary>
public sealed class HomeDulcheCapabilityReceiptIssuer : IHomeDulcheCapabilityReceiptIssuer
{
    public HomeDulcheCapabilityObservation? Issue(
        HomeDulcheCapabilityObservation? currentCapability,
        HomeDulcheModelEvidence? model,
        HomeDulcheBrokerObservation? broker)
    {
        // The supplied observation must itself be genuine owning engine evidence: a positive
        // capability that carries a real broker-generation/artifact binding. An unbound bool+string
        // (or missing/empty Evidence) is not owning evidence and fails closed.
        if (currentCapability is not { GenerationCapable: true } capability ||
            string.IsNullOrWhiteSpace(capability.Evidence) ||
            capability.Broker is null ||
            capability.ModelArtifactSha256 is null)
        {
            return null;
        }
        // Observed assets must be present for the current observation to be affirmed.
        if (model is null || broker is null) return null;
        // Fail closed unless the CURRENT observed broker generation and verified model artifact are
        // the SAME generation as the original owning evidence. This rejects stale rebinding after a
        // broker restart (PID change) or a model-artifact replacement, and rejects mismatched
        // provider/model identities. We never copy the boolean/string into a fresh admission and
        // never mint a new timestamp: we affirm the ORIGINAL observation, preserving its custody.
        if (!capability.IsSameGeneration(model, broker)) return null;
        return capability;
    }
}
