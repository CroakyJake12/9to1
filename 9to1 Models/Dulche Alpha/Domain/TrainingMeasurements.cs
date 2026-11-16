namespace NineToOne.Dulche.Training;

public enum TrainingUpdateSemantics
{
    FullGradient,
    BlockCoordinate,
    DeferredRecomputedGradient,
    ParameterEfficient,
    Experimental
}

public enum MeasurementEvidence { Unavailable, Estimated, Measured }
public enum MemoryMeasurementPhase { Inference, Training }
public enum MeasuredRunOutcome { Completed, Interrupted, OutOfMemory, Failed }
public enum VramComparisonKind { Unavailable, Comparable, CrossConfigurationReference }

/// <summary>Identity of a measured configuration, not a claim that a device can execute it.</summary>
public sealed record TrainingMeasurementConfiguration(
    string BaseModelSha256,
    string ModelConfigurationSha256,
    string HardwareFingerprint,
    string EngineBuild,
    string WeightPrecision,
    string ComputePrecision,
    string WeightResidency,
    int ContextTokens,
    int BatchSize,
    string MeasurementWindow);

/// <summary>
/// Framework allocation is separate from process/device overhead. A missing GPU
/// measurement is null, including CPU-only runs; it must not become a zero-byte baseline.
/// </summary>
public sealed record TrainingMemoryObservation(
    Guid RunId,
    MemoryMeasurementPhase Phase,
    MeasurementEvidence Evidence,
    MeasuredRunOutcome Outcome,
    TrainingMeasurementConfiguration Configuration,
    long? PeakGpuAllocatedBytes,
    long? PeakGpuReservedBytes,
    long? PeakGpuProcessBytes,
    long? PeakCpuResidentBytes,
    long? PeakDiskBytes,
    long? CpuToGpuBytes,
    long? GpuToCpuBytes,
    long? StorageReadBytes,
    long? StorageWrittenBytes,
    long? ProcessedTokens,
    double? ElapsedSeconds,
    TrainingUpdateSemantics? UpdateSemantics = null)
{
    public double? MeasuredTokensPerSecond => Evidence == MeasurementEvidence.Measured && Outcome == MeasuredRunOutcome.Completed &&
        ProcessedTokens is >= 0 && ElapsedSeconds is > 0 && double.IsFinite(ElapsedSeconds.Value)
        && double.IsFinite(ProcessedTokens.Value / ElapsedSeconds.Value)
            ? ProcessedTokens.Value / ElapsedSeconds.Value : null;
}

public sealed record TrainingVramComparison(
    VramComparisonKind Kind,
    double? AllocatedVramRatio,
    double? ProcessVramRatio,
    IReadOnlyList<string> Differences,
    bool HasCompleteRelocatedStateMeasurements)
{
    // A low ratio is a memory observation, never a quality, throughput, practicality
    // or conventional-gradient-equivalence verdict.
    public bool CanCompareToMemoryResearchTarget => Kind == VramComparisonKind.Comparable &&
        AllocatedVramRatio.HasValue && HasCompleteRelocatedStateMeasurements;
}

public static class TrainingMemoryComparison
{
    public static TrainingVramComparison Compare(TrainingMemoryObservation inference,
        TrainingMemoryObservation training)
    {
        ArgumentNullException.ThrowIfNull(inference);
        ArgumentNullException.ThrowIfNull(training);
        Validate(inference);
        Validate(training);
        if (inference.Phase != MemoryMeasurementPhase.Inference || training.Phase != MemoryMeasurementPhase.Training)
            throw new ArgumentException("An inference baseline and a training observation are required, in that order.");
        var differences = new List<string>();
        var a = inference.Configuration;
        var b = training.Configuration;
        Difference(a.BaseModelSha256.ToLowerInvariant(), b.BaseModelSha256.ToLowerInvariant(), "base model", differences);
        Difference(a.ModelConfigurationSha256.ToLowerInvariant(), b.ModelConfigurationSha256.ToLowerInvariant(), "model configuration", differences);
        Difference(a.HardwareFingerprint, b.HardwareFingerprint, "hardware allocation", differences);
        Difference(a.EngineBuild, b.EngineBuild, "engine build", differences);
        Difference(a.WeightPrecision, b.WeightPrecision, "weight precision", differences);
        Difference(a.ComputePrecision, b.ComputePrecision, "compute precision", differences);
        Difference(a.WeightResidency, b.WeightResidency, "weight residency", differences);
        Difference(a.ContextTokens, b.ContextTokens, "context length", differences);
        Difference(a.BatchSize, b.BatchSize, "batch size", differences);
        Difference(a.MeasurementWindow, b.MeasurementWindow, "measurement window", differences);

        var measured = inference.Evidence == MeasurementEvidence.Measured && training.Evidence == MeasurementEvidence.Measured &&
            inference.Outcome == MeasuredRunOutcome.Completed && training.Outcome == MeasuredRunOutcome.Completed;
        var ratio = measured ? Ratio(inference.PeakGpuAllocatedBytes, training.PeakGpuAllocatedBytes) : null;
        var processRatio = measured ? Ratio(inference.PeakGpuProcessBytes, training.PeakGpuProcessBytes) : null;
        var stateMeasured = measured && CompleteState(inference) && CompleteState(training);
        var kind = ratio is null ? VramComparisonKind.Unavailable : differences.Count == 0
            ? VramComparisonKind.Comparable : VramComparisonKind.CrossConfigurationReference;
        return new(kind, ratio, processRatio, differences.AsReadOnly(), stateMeasured);
    }

    public static void Validate(TrainingMemoryObservation value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(value.Configuration);
        var c = value.Configuration;
        if (value.RunId == Guid.Empty || !Enum.IsDefined(value.Phase) || !Enum.IsDefined(value.Evidence) || !Enum.IsDefined(value.Outcome) ||
            value.UpdateSemantics is { } semantics && !Enum.IsDefined(semantics))
            throw new ArgumentException("The measurement identity or category is invalid.");
        if (!Digest(c.BaseModelSha256) || !Digest(c.ModelConfigurationSha256) ||
            !Label(c.HardwareFingerprint) || !Label(c.EngineBuild) || !Label(c.WeightPrecision) || !Label(c.ComputePrecision) ||
            !Label(c.WeightResidency) || !Label(c.MeasurementWindow) || c.ContextTokens <= 0 || c.BatchSize <= 0)
            throw new ArgumentException("The complete measured configuration is required.");
        if (value.Phase == MemoryMeasurementPhase.Inference && value.UpdateSemantics is not null ||
            value.Phase == MemoryMeasurementPhase.Training && value.UpdateSemantics is null)
            throw new ArgumentException("Only training observations declare update semantics.");
        long?[] sizes = [value.PeakGpuAllocatedBytes, value.PeakGpuReservedBytes, value.PeakGpuProcessBytes,
            value.PeakCpuResidentBytes, value.PeakDiskBytes, value.CpuToGpuBytes, value.GpuToCpuBytes,
            value.StorageReadBytes, value.StorageWrittenBytes, value.ProcessedTokens];
        if (sizes.Any(number => number < 0) || value.ElapsedSeconds is { } seconds && (!double.IsFinite(seconds) || seconds <= 0))
            throw new ArgumentException("Measurements must be finite nonnegative counts with positive elapsed time.");
        if (value.PeakGpuAllocatedBytes is { } allocated && value.PeakGpuReservedBytes is { } reserved && reserved < allocated)
            throw new ArgumentException("Reserved GPU memory cannot be less than allocated memory in the same measurement window.");
        if (value.Evidence == MeasurementEvidence.Unavailable && sizes.Any(number => number.HasValue))
            throw new ArgumentException("Unavailable measurements cannot contain measured or estimated counts.");
    }

    private static bool CompleteState(TrainingMemoryObservation value) => value.PeakCpuResidentBytes.HasValue &&
        value.PeakDiskBytes.HasValue && value.CpuToGpuBytes.HasValue && value.GpuToCpuBytes.HasValue &&
        value.StorageReadBytes.HasValue && value.StorageWrittenBytes.HasValue;
    private static double? Ratio(long? baseline, long? candidate) => baseline is > 0 && candidate is > 0
        ? candidate.Value / (double)baseline.Value : null;
    private static bool Digest(string value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
    private static bool Label(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);
    private static void Difference<T>(T left, T right, string field, List<string> differences)
    { if (!EqualityComparer<T>.Default.Equals(left, right)) differences.Add(field); }
}
