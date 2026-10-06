namespace Dulche.Runtime;

/// <summary>Optional genuine held-model hardware observation. Missing facts stay uncalibrated.</summary>
public interface IStrataOriginalHardwareBinding
{ InferenceHardware OriginalHardwareObservation { get; } }

public sealed record InferenceCalibrationKey(InferenceEngine Engine, ModelIdentity Model,
    string ArtifactFingerprint, string HardwareFingerprint, string RuntimeFingerprint, int ContextTokens, string Settings);

/// <summary>Immutable measurement data. Its private actual source must prove reference issuance;
/// a constructed or deserialized copy never qualifies by its numbers or public fields.</summary>
public sealed class InferenceCalibrationReading
{
    internal InferenceCalibrationReading(InferenceCalibrationKey key, StrataGenerationObservation actual)
    { Key=key; Actual=actual; }
    public InferenceCalibrationKey Key { get; }
    public StrataGenerationObservation Actual { get; }
}
public interface IOriginalInferenceCalibrationSource
{
    bool TryObserveOriginalCalibration(ModelIdentity sameModel, out InferenceCalibrationReading? reading);
    bool IsIssuedOriginalCalibration(InferenceCalibrationReading sameOriginal);
}

/// <summary>Optional SAME worker measurement source. Existing engine leases stay unchanged.</summary>
public interface IOriginalInferenceCalibrationLease
{ IOriginalInferenceCalibrationSource? CalibrationSource { get; } }
