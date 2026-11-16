namespace NineToOne.Dulche.Den;

public enum ModelFamilyCatalogueAudience { Internal, Public }
public enum ModelFamilyCataloguePhase { PlannedLocalRelease, SupportedLocalRelease, PublicReady }
public enum ModelFamilyCatalogueLabel { Unchanged, TemporaryDevelopmentFallbackForPlannedQwen4, Legacy }
public enum ModelFamilyRecommendation { Unchanged, TemporaryDevelopmentFallback, LegacyOption, Excluded, ExplicitSignificantRegressionFallback }
public enum ModelFamilyPublicCatalogueDisposition { Unchanged, Excluded, IncludedForExplicitSignificantRegressionFallback }
public enum ModelResourceClass { Unknown, Small, Medium, Large, Huge }

/// <summary>
/// Data-only model-family projection. It preserves the source row's exact identity and facts; it does not
/// represent authorization, installation, runtime availability, engine compatibility, or hardware fit.
/// </summary>
public sealed record ModelFamilyPolicyResult(
    string ModelId,
    string DisplayName,
    string? Source,
    string? ExternalModelId,
    ModelFamilyCatalogueAudience Audience,
    ModelFamilyCataloguePhase Phase,
    ModelFamilyCatalogueLabel Label,
    ModelFamilyRecommendation Recommendation,
    ModelFamilyPublicCatalogueDisposition PublicCatalogueDisposition,
    ModelResourceClass ResourceClass,
    long? KnownStorageBytes,
    long? KnownHardwareMemoryBytes,
    string? IntendedFamilyLabel);

/// <summary>
/// Applies the owner-directed Qwen family catalogue transition to existing Den model records only.
/// Classification uses the official external model ID where present; architecture and display names
/// are deliberately not treated as identity. Qwen4's planned label is an intended family, never a
/// claim that the fallback is Qwen4 or that a local Qwen4 checkpoint exists.
/// </summary>
public static class ModelFamilyPolicy
{
    private const string Qwen38ExternalModelId = "Qwen/Qwen3.8-Flash-Next";
    private const string Qwen36ExternalModelId = "Qwen/Qwen3.6-35B-A3B";
    private const long SmallMaximumBytes = 8L * 1024 * 1024 * 1024;
    private const long MediumMaximumBytes = 32L * 1024 * 1024 * 1024;
    private const long LargeMaximumBytes = 128L * 1024 * 1024 * 1024;

    /// <summary>
    /// Evaluates presentation/recommendation treatment without changing the record. The public fallback
    /// flag must only be set by a caller holding separate evidence of a significant Qwen4 regression
    /// and a completed license review; this method validates neither condition nor grants publication.
    /// </summary>
    public static ModelFamilyPolicyResult Evaluate(
        ModelRecord model,
        ModelFamilyCatalogueAudience audience,
        ModelFamilyCataloguePhase phase,
        bool allowPublicQwen38ForSignificantQwen4Regression = false)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!Enum.IsDefined(audience)) throw new ArgumentOutOfRangeException(nameof(audience));
        if (!Enum.IsDefined(phase)) throw new ArgumentOutOfRangeException(nameof(phase));

        var externalModelId = model.OfficialArtifact?.ExternalModelId;
        var isQwen38Fallback = IsExternalId(externalModelId, Qwen38ExternalModelId);
        var isQwen36Excluded = IsExternalId(externalModelId, Qwen36ExternalModelId);
        var label = ModelFamilyCatalogueLabel.Unchanged;
        var recommendation = ModelFamilyRecommendation.Unchanged;
        var publicDisposition = ModelFamilyPublicCatalogueDisposition.Unchanged;
        string? intendedFamilyLabel = null;

        if (isQwen38Fallback)
        {
            if (phase == ModelFamilyCataloguePhase.PlannedLocalRelease)
            {
                label = ModelFamilyCatalogueLabel.TemporaryDevelopmentFallbackForPlannedQwen4;
                intendedFamilyLabel = "Qwen4 (planned local family)";
                recommendation = ModelFamilyRecommendation.TemporaryDevelopmentFallback;
            }
            else
            {
                label = ModelFamilyCatalogueLabel.Legacy;
                recommendation = ModelFamilyRecommendation.LegacyOption;
            }

            if (audience == ModelFamilyCatalogueAudience.Public && phase == ModelFamilyCataloguePhase.PublicReady)
            {
                publicDisposition = allowPublicQwen38ForSignificantQwen4Regression
                    ? ModelFamilyPublicCatalogueDisposition.IncludedForExplicitSignificantRegressionFallback
                    : ModelFamilyPublicCatalogueDisposition.Excluded;
                recommendation = allowPublicQwen38ForSignificantQwen4Regression
                    ? ModelFamilyRecommendation.ExplicitSignificantRegressionFallback
                    : ModelFamilyRecommendation.Excluded;
            }
        }
        else if (isQwen36Excluded)
        {
            // This affects recommendations only. Existing rows, installed files and manual compatibility
            // remain outside this projection and are not edited or removed.
            recommendation = ModelFamilyRecommendation.Excluded;
        }

        var storageBytes = ReadMeasuredBytes(model.StorageBytes)
            ?? PositiveBytes(model.OfficialArtifact?.LengthBytes);
        var hardwareMemoryBytes = ReadMeasuredBytes(model.HardwareMemoryBytes);
        var resourceClass = ClassifyResource(storageBytes, hardwareMemoryBytes);

        return new ModelFamilyPolicyResult(
            model.Id,
            model.DisplayName,
            model.Source,
            externalModelId,
            audience,
            phase,
            label,
            recommendation,
            publicDisposition,
            resourceClass,
            storageBytes,
            hardwareMemoryBytes,
            intendedFamilyLabel);
    }

    private static bool IsExternalId(string? actualExternalModelId, string expected) =>
        string.Equals(actualExternalModelId, expected, StringComparison.OrdinalIgnoreCase);

    private static long? ReadMeasuredBytes(PortableMetric metric)
    {
        if (metric.Availability != MetricAvailability.Measured || metric.Value is not { } value ||
            value <= 0 || value != decimal.Truncate(value) || value > long.MaxValue)
            return null;
        return decimal.ToInt64(value);
    }

    private static long? PositiveBytes(long? value) => value is > 0 ? value : null;

    private static ModelResourceClass ClassifyResource(long? storageBytes, long? hardwareMemoryBytes)
    {
        var knownBytes = new[] { storageBytes, hardwareMemoryBytes }.Where(value => value.HasValue)
            .Select(value => value!.Value).DefaultIfEmpty().Max();
        if (knownBytes <= 0) return ModelResourceClass.Unknown;
        if (knownBytes <= SmallMaximumBytes) return ModelResourceClass.Small;
        if (knownBytes <= MediumMaximumBytes) return ModelResourceClass.Medium;
        if (knownBytes <= LargeMaximumBytes) return ModelResourceClass.Large;
        return ModelResourceClass.Huge;
    }
}
