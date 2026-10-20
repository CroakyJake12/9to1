namespace HavenOS.Home.Core;

/// <summary>Issues a typed generation-capability receipt ONLY from genuine, same-generation inputs:
/// an actually observed broker process/endpoint generation, a verified model artifact SHA-256, and a
/// current verified generation-capability observation. It never fabricates a capability, a broker,
/// or a hash. When any required evidence is absent the issuer returns null, so the capability axis
/// stays explicitly blocked (Degraded) — it is never inferred from names or platform.</summary>
public interface IHomeDulcheCapabilityReceiptIssuer
{
    /// <summary>Returns a receipt that binds <paramref name="model"/> and <paramref name="broker"/>
    /// (the SAME observed broker generation and verified artifact) to the supplied verified
    /// generation-capability observation, or null when a genuine binding cannot be produced.</summary>
    HomeDulcheCapabilityObservation? Issue(
        HomeDulcheCapabilityObservation? currentCapability,
        HomeDulcheModelEvidence? model,
        HomeDulcheBrokerObservation? broker,
        DateTimeOffset? observedAtUtc = null);
}

/// <summary>Default implementation of <see cref="IHomeDulcheCapabilityReceiptIssuer"/>. The receipt
/// is the typed <see cref="HomeDulcheCapabilityObservation"/> later consumed by the lifecycle owner
/// via <see cref="HomeDulcheCapabilityObservation.IsSameGeneration"/>. The positive production axis
/// stays BLOCKED until a genuine verified generation-capability observation exists; this component
/// is the unblocked issuer/validation contract that binds such an observation when it is real.</summary>
public sealed class HomeDulcheCapabilityReceiptIssuer : IHomeDulcheCapabilityReceiptIssuer
{
    public HomeDulcheCapabilityObservation? Issue(
        HomeDulcheCapabilityObservation? currentCapability,
        HomeDulcheModelEvidence? model,
        HomeDulcheBrokerObservation? broker,
        DateTimeOffset? observedAtUtc = null)
    {
        // Absent startup assets/evidence (no verified model or no observed live broker) can never
        // produce a receipt; the observation stays Degraded.
        if (model is null || broker is null) return null;
        // No verified current generation-capability observation to bind: keep the axis closed.
        if (currentCapability is not { GenerationCapable: true } capability ||
            string.IsNullOrWhiteSpace(capability.Evidence))
        {
            return null;
        }
        // The verified observation must describe the SAME model/provider identity it is bound to.
        if (!string.Equals(capability.ProviderId, model.ProviderId, StringComparison.Ordinal) ||
            !string.Equals(capability.ModelId, model.ModelId, StringComparison.Ordinal))
        {
            return null;
        }
        var receipt = new HomeDulcheCapabilityObservation(
            true,
            model.ProviderId,
            model.ModelId,
            observedAtUtc ?? DateTimeOffset.UtcNow,
            capability.Evidence,
            broker,
            model.ArtifactSha256);
        // Defensive: a receipt that does not verify same-generation against the very data it was
        // bound from must never be issued; the axis stays closed.
        return receipt.IsSameGeneration(model, broker) ? receipt : null;
    }
}
