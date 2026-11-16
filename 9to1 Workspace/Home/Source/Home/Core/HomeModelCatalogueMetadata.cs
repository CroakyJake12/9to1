using Dulche.Runtime;
using Haven.Application;
using NineToOne.Dulche.Den;

namespace HavenOS.Home.Core;

/// <summary>Trusted owner binding between provider identity and its authorized Den
/// metadata. A null provider revision is exact/unversioned, never a wildcard.
/// Den record IDs and provider model IDs are deliberately distinct.</summary>
public sealed record HomeModelCatalogueMetadataBinding(
    ModelIdentity ProviderIdentity, string BindingRevision, ModelRecord Record);

public interface IHomeModelCatalogueMetadataSource
{
    Task<HomeModelCatalogueMetadataBinding?> FindAsync(AuthenticatedResourceActor actor,
        ModelIdentity exactProviderIdentity, CancellationToken cancellationToken);
}

/// <summary>Deployment/release policy, not editable model or job input.</summary>
public sealed record HomeModelFamilyReleaseContext(
    ModelFamilyCatalogueAudience Audience = ModelFamilyCatalogueAudience.Internal,
    ModelFamilyCataloguePhase Phase = ModelFamilyCataloguePhase.PlannedLocalRelease,
    bool AllowPublicQwen38ForSignificantQwen4Regression = false);

/// <summary>Presentation facts only. No paths, secrets, configuration overrides,
/// registration authority, installed-state or hardware-fit claims.</summary>
public sealed record HomeModelPickerPresentation(
    string DenModelId, string BindingRevision, string? ExternalModelId, string? Source,
    string? Format, string? Quantisation, string ArtifactRevision,
    ModelFamilyCatalogueLabel FamilyLabel, ModelFamilyRecommendation Recommendation,
    ModelFamilyPublicCatalogueDisposition PublicDisposition, string? IntendedFamilyLabel,
    ModelResourceClass ResourceClass, long? KnownStorageBytes, long? KnownHardwareMemoryBytes,
    PortableMetric StorageMetric, PortableMetric HardwareMemoryMetric);

/// <summary>Facts reported by the already-authorized provider catalogue, not
/// verified artifact/Den evidence, measured memory needs or publisher lineage.</summary>
public sealed record HomeModelProviderReportedMetadata(long? ReportedSizeBytes, string? ReportedQuantisation)
{
    public static HomeModelProviderReportedMetadata From(long sizeBytes, string? quantisation)
    {
        var quant = quantisation?.Trim();
        // A quantization descriptor is a short label, not a URL/path or freeform
        // provider response. Preserve valid reported spelling, infer nothing.
        if (string.IsNullOrEmpty(quant) || quant.Length > 64 ||
            quant.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-' or '.' or ' ')))
            quant = null;
        return new(sizeBytes > 0 ? sizeBytes : null, quant);
    }
}

public static class HomeModelCatalogueMetadataProjection
{
    public static HomeModelPickerPresentation? Project(ModelIdentity expected,
        HomeModelCatalogueMetadataBinding? binding, HomeModelFamilyReleaseContext context)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(context);
        if (binding is null) return null;
        if (binding.ProviderIdentity != expected || binding.Record is not { } record ||
            string.IsNullOrWhiteSpace(record.Id) || string.IsNullOrWhiteSpace(record.ArtifactRevision) ||
            string.IsNullOrWhiteSpace(binding.BindingRevision) || binding.BindingRevision.Length > 256 ||
            binding.BindingRevision.Any(char.IsControl))
            throw new InvalidDataException("The metadata owner returned a mismatched or incomplete exact model binding.");
        if (record.Provider is { } provider && !string.Equals(provider, expected.ProviderId, StringComparison.Ordinal))
            throw new InvalidDataException("The Den metadata names another provider.");
        if (expected.ArtifactRevision is { } revision && !string.Equals(revision, record.ArtifactRevision, StringComparison.Ordinal))
            throw new InvalidDataException("The Den metadata names another artifact revision.");
        record.OfficialArtifact?.ValidateForModel(record);
        var policy = ModelFamilyPolicy.Evaluate(record, context.Audience, context.Phase,
            context.AllowPublicQwen38ForSignificantQwen4Regression);
        // ModelRecord.Source is unconstrained descriptive text, not a public URI
        // or publisher-identity contract. It can contain paths/credentials. Omit
        // it; only the separately bound external identity crosses this boundary.
        return new(record.Id, binding.BindingRevision, policy.ExternalModelId, null,
            record.Format, record.Quantisation, record.ArtifactRevision, policy.Label, policy.Recommendation,
            policy.PublicCatalogueDisposition, policy.IntendedFamilyLabel, policy.ResourceClass,
            policy.KnownStorageBytes, policy.KnownHardwareMemoryBytes, record.StorageBytes, record.HardwareMemoryBytes);
    }
}
