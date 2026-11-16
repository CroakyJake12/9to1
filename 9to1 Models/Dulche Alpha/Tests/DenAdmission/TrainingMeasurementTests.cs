using NineToOne.Dulche.Training;

namespace Dulche.Den.Tests;

public sealed class TrainingMeasurementTests
{
    private static readonly TrainingMeasurementConfiguration Configuration = new(
        new string('a', 64), new string('b', 64), "cpu-and-approved-gpu", "reference-runtime-v1",
        "float32", "float32", "same-streamed-weights", 128, 2, "after-model-load-through-completion");

    private static TrainingMemoryObservation Observation(MemoryMeasurementPhase phase, long? bytes = 100) => new(
        Guid.NewGuid(), phase, MeasurementEvidence.Measured, MeasuredRunOutcome.Completed, Configuration, bytes, bytes, bytes is null ? null : bytes + 900,
        5000, 10000, 2000, 3000, 4000, 5000, 1000, 2,
        phase == MemoryMeasurementPhase.Training ? TrainingUpdateSemantics.BlockCoordinate : null);

    [Fact]
    public void ComparableMeasurementsKeepGpuRatioAndRelocatedStateSeparate()
    {
        var result = TrainingMemoryComparison.Compare(Observation(MemoryMeasurementPhase.Inference), Observation(MemoryMeasurementPhase.Training, 150));
        Assert.Equal(VramComparisonKind.Comparable, result.Kind);
        Assert.Equal(1.5, result.AllocatedVramRatio);
        Assert.Equal(1.05, result.ProcessVramRatio);
        Assert.True(result.CanCompareToMemoryResearchTarget);
        Assert.Empty(result.Differences);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(null)]
    public void CpuOrZeroBaselinesNeverProduceAnInferenceLevelRatio(long? bytes)
    {
        var result = TrainingMemoryComparison.Compare(Observation(MemoryMeasurementPhase.Inference, bytes), Observation(MemoryMeasurementPhase.Training));
        Assert.Equal(VramComparisonKind.Unavailable, result.Kind);
        Assert.Null(result.AllocatedVramRatio);
        Assert.False(result.CanCompareToMemoryResearchTarget);
    }

    [Fact]
    public void QuantizedInferenceAndFullPrecisionTrainingRemainASeparateReference()
    {
        var baseline = Observation(MemoryMeasurementPhase.Inference) with { Configuration = Configuration with { WeightPrecision = "NF4" } };
        var result = TrainingMemoryComparison.Compare(baseline, Observation(MemoryMeasurementPhase.Training));
        Assert.Equal(1.0, result.AllocatedVramRatio);
        Assert.Equal(VramComparisonKind.CrossConfigurationReference, result.Kind);
        Assert.Contains("weight precision", result.Differences);
        Assert.False(result.CanCompareToMemoryResearchTarget);
    }

    [Fact]
    public void MissingOffloadCountersCannotSatisfyTheMemoryResearchComparison()
    {
        var candidate = Observation(MemoryMeasurementPhase.Training) with { StorageReadBytes = null };
        var result = TrainingMemoryComparison.Compare(Observation(MemoryMeasurementPhase.Inference), candidate);
        Assert.NotNull(result.AllocatedVramRatio);
        Assert.False(result.HasCompleteRelocatedStateMeasurements);
        Assert.False(result.CanCompareToMemoryResearchTarget);
    }

    [Fact]
    public void FailedRunsAndZeroGpuTrainingNeverProveTheGpuResearchTarget()
    {
        var baseline = Observation(MemoryMeasurementPhase.Inference);
        var failed = Observation(MemoryMeasurementPhase.Training) with { Outcome = MeasuredRunOutcome.OutOfMemory };
        Assert.Null(TrainingMemoryComparison.Compare(baseline, failed).AllocatedVramRatio);
        Assert.Null(failed.MeasuredTokensPerSecond);
        Assert.Null(TrainingMemoryComparison.Compare(baseline, Observation(MemoryMeasurementPhase.Training, 0)).AllocatedVramRatio);
        Assert.Null((failed with { Outcome = MeasuredRunOutcome.Completed, ElapsedSeconds = double.Epsilon }).MeasuredTokensPerSecond);
    }

    [Fact]
    public void ComputePrecisionIsComparedAndDigestCaseDoesNotInventADifferentModel()
    {
        var baseline = Observation(MemoryMeasurementPhase.Inference);
        var changed = Observation(MemoryMeasurementPhase.Training) with { Configuration = Configuration with { ComputePrecision = "bfloat16", BaseModelSha256 = new string('A', 64) } };
        Assert.Equal(new[] { "compute precision" }, TrainingMemoryComparison.Compare(baseline, changed).Differences);
    }

    [Fact]
    public void EstimatesAndChangedConditionsCannotBecomeMeasuredEvidence()
    {
        var candidate = Observation(MemoryMeasurementPhase.Training) with { Evidence = MeasurementEvidence.Estimated };
        Assert.Null(TrainingMemoryComparison.Compare(Observation(MemoryMeasurementPhase.Inference), candidate).AllocatedVramRatio);
        Assert.Null(candidate.MeasuredTokensPerSecond);
        candidate = Observation(MemoryMeasurementPhase.Training) with { Configuration = Configuration with { ContextTokens = 256, WeightResidency = "cpu-offloaded" } };
        var result = TrainingMemoryComparison.Compare(Observation(MemoryMeasurementPhase.Inference), candidate);
        Assert.Equal(new[] { "weight residency", "context length" }, result.Differences);
        Assert.False(result.CanCompareToMemoryResearchTarget);
    }

    [Fact]
    public void MalformedFactsAndMislabelledPhasesAreRejected()
    {
        var inference = Observation(MemoryMeasurementPhase.Inference);
        var training = Observation(MemoryMeasurementPhase.Training);
        Assert.Throws<ArgumentException>(() => TrainingMemoryComparison.Compare(training, inference));
        Assert.Throws<ArgumentException>(() => TrainingMemoryComparison.Validate(training with { PeakGpuAllocatedBytes = -1 }));
        Assert.Throws<ArgumentException>(() => TrainingMemoryComparison.Validate(training with { ElapsedSeconds = double.NaN }));
        Assert.Throws<ArgumentException>(() => TrainingMemoryComparison.Validate(training with { PeakGpuReservedBytes = 1 }));
        Assert.Throws<ArgumentException>(() => TrainingMemoryComparison.Validate(training with { UpdateSemantics = null }));
        Assert.Throws<ArgumentException>(() => TrainingMemoryComparison.Validate(training with { Configuration = Configuration with { BaseModelSha256 = "unknown" } }));
        Assert.Equal(500.0, training.MeasuredTokensPerSecond);
    }
}
