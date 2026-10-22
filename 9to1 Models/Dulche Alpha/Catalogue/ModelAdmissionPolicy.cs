using System.Collections.Frozen;

namespace NineToOne.Dulche.Den;

/// <summary>Official immutable original-source identity for an owner-requested model entry (the
/// upstream Safetensors/BF16 source and licence). Metadata only; never claims download, weight
/// custody, installation, runtime support, or execution.</summary>
public sealed record RequestedModelSource(string RepositoryPath, string Revision, string License);

/// <summary>Official published GGUF artifact identity for the pinned requested-model entry. The whole
/// tuple (repo, revision, exact filename, length, SHA-256, quantization) is immutable official
/// metadata and is NOT a guarantee the device can load or run it; a vision projector is deliberately
/// kept separate from any smaller text model.</summary>
public sealed record RequestedModelArtifact(
    string RepoArn,
    string Revision,
    string FileName,
    long LengthBytes,
    string Sha256,
    string Quantization,
    IReadOnlyList<string> OfficialPinnedQuantizations);

/// <summary>Complete recognized profile for one owner-requested model (canonical id + source +
/// pinned artifact + candidate metadata).</summary>
public sealed record RequestedModelProfile(
    string ModelId,
    RequestedModelSource Source,
    RequestedModelArtifact Artifact,
    string WeightFormat,
    string ArchitectureFamily,
    long Bf16Parameters);

/// <summary>Independent admission stages. Recognition, pinned-artifact identity, format admission,
/// source ceiling and device quota are distinct; none imply installation, runtime compatibility,
/// GPU/hardware fit, or initialized availability.</summary>
public enum ModelAdmissionStage
{
    Unknown, Recognized, ArtifactIdentityMismatch, FormatBlocked, SizeAboveCeilingBlocked, QuotaBlocked
}

/// <summary>A typed preflight request supplying the candidate model id and the full artifact identity
/// to compare against the pinned official entry. The caller cannot bypass admission with an arbitrary
/// length: the length must exactly match the pinned official artifact.</summary>
public sealed record ModelPreflightRequest(
    string ModelId,
    string ArtifactRepoArn,
    string ArtifactRevision,
    string ArtifactFileName,
    long LengthBytes,
    string Sha256,
    string Quantization,
    string WeightFormat,
    string ArchitectureFamily,
    long DeviceQuotaBytes);

/// <summary>Truthful typed preflight report. Recognizing an official profile or matching its pinned
/// artifact grants no installation, availability, or execution authority.</summary>
public sealed record ModelPreflightReport(
    string ModelId,
    bool Recognized,
    bool PinnedArtifactMatched,
    bool FormatAdmitted,
    bool WithinSourceCeiling,
    bool WithinDeviceQuota,
    ModelAdmissionStage Stage,
    long DeviceQuotaBytes,
    RequestedModelProfile? Profile);

/// <summary>Recognition/admission policy for the owner-requested models. Entries are immutable official
/// metadata (no weights, no download, no hardware validation). The candidate/engine-native path is a
/// single centralized GGUF with a 64 GiB source ceiling; device-local installation evidence is governed
/// separately by the configured LocalModelRegistry weight quota.</summary>
public static class ModelAdmissionPolicy
{
    /// <summary>Centralized single-GGUF candidate source ceiling (also reflects the selection relay ceiling
    /// and rejects the larger official Ornith text quants).</summary>
    public const long CandidateSourceCeilingBytes = 64L * 1024 * 1024 * 1024;
    private const string Gguf = "GGUF";
    private const string Qwen35Moe = "qwen35moe";

    // Frozen, case-insensitive canonical-id lookup. Official quants are ReadOnlyCollection-backed so a
    // caller cannot cast to a mutable array/list and alter the global registry.
    private static readonly FrozenDictionary<string, RequestedModelProfile> RecognizedProfiles =
        new Dictionary<string, RequestedModelProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["ornith-ai/ornith-1.5-122b"] = new(
                "ornith-ai/Ornith-1.5-122B",
                new("ornith-ai/Ornith-1.5-122B", "7681ef608624e7532d8164128bcffdbdbaf1b1af", "MIT"),
                new(
                    "ornith-ai/Ornith-1.5-122B-GGUF",
                    "8d5893d767c449a4f18dd08f4bb6d0d6d4426175",
                    "Ornith-1.5-122B-Q4_K_M.gguf",
                    75843911648L,
                    "729fe18c2491aaa446074a923e12f184f9f2239ffd5ce3dae47b0ce9cf8df40c",
                    "Q4_K_M",
                    Array.AsReadOnly<string>(["BF16", "Q4_K_M", "Q5_K_M", "Q6_K", "Q8_0"])),
                Gguf, Qwen35Moe, 125086497008L),
            ["internscience/sage-a1.5"] = new(
                "InternScience/Sage-A1.5",
                new("InternScience/Sage-A1.5", "6bc623eacb712d79d27c0913e4816ccd33d41b0a", "Apache-2.0"),
                new(
                    "InternScience/Sage-A1.5-Q4_K_M-GGUF",
                    "656dec1d8d7b74fe5952cb4e83bdc42f6708e52d",
                    "Sage-A1.5-Q4_K_M.gguf",
                    21713463520L,
                    "a64fe0bbf936db3eb54f66f142c2b2eee569ade83673649f549a21daef8ca395",
                    "Q4_K_M",
                    Array.AsReadOnly<string>(["Q4_K_M"])),
                Gguf, Qwen35Moe, 35951822704L),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> RecognizedIds =
        RecognizedProfiles.Values.Select(profile => profile.ModelId).ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The immutable canonical stable IDs of the recognized owner-requested models.</summary>
    public static IReadOnlyCollection<string> RecognizedModelIds => RecognizedIds;

    /// <summary>Evaluate admission for a candidate artifact. Report fields stay separate so callers can
    /// distinguish recognition, pinned-artifact identity, format boundary, source ceiling and quota
    /// without overclaiming.</summary>
    public static ModelPreflightReport Preflight(ModelPreflightRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ModelId is null) throw new ArgumentNullException(nameof(request.ModelId));
        if (request.ModelId.Length == 0) throw new ArgumentException("A model identifier is required.", nameof(request.ModelId));
        if (request.DeviceQuotaBytes < 0) throw new ArgumentOutOfRangeException(nameof(request.DeviceQuotaBytes), "The device quota must be non-negative.");

        var profile = RecognizedProfiles.TryGetValue(request.ModelId, out var match)
            ? match : null;
        if (profile is null)
            return new(
                ModelId: request.ModelId,
                Recognized: false,
                PinnedArtifactMatched: false,
                FormatAdmitted: false,
                WithinSourceCeiling: false,
                WithinDeviceQuota: false,
                Stage: ModelAdmissionStage.Unknown,
                DeviceQuotaBytes: request.DeviceQuotaBytes,
                Profile: null);

        var artifact = profile.Artifact;
        bool identityMatched = string.Equals(request.ArtifactRepoArn, artifact.RepoArn, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(request.ArtifactRevision, artifact.Revision, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(request.ArtifactFileName, artifact.FileName, StringComparison.Ordinal) &&
            request.LengthBytes == artifact.LengthBytes &&
            string.Equals(request.Sha256, artifact.Sha256, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(request.Quantization, artifact.Quantization, StringComparison.OrdinalIgnoreCase);

        if (!identityMatched)
            return new(
                ModelId: request.ModelId,
                Recognized: true,
                PinnedArtifactMatched: false,
                FormatAdmitted: false,
                WithinSourceCeiling: false,
                WithinDeviceQuota: false,
                Stage: ModelAdmissionStage.ArtifactIdentityMismatch,
                DeviceQuotaBytes: request.DeviceQuotaBytes,
                Profile: profile);

        // Format admission is the candidate engine-native boundary (GGUF/qwen35moe). It is distinct
        // from authenticated installed artifact evidence and from requested-model recognition.
        bool formatAdmitted = string.Equals(request.WeightFormat, Gguf, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(request.ArchitectureFamily, Qwen35Moe, StringComparison.OrdinalIgnoreCase);

        bool withinCeiling = artifact.LengthBytes <= CandidateSourceCeilingBytes;
        bool withinQuota = artifact.LengthBytes <= request.DeviceQuotaBytes;

        var stage = !formatAdmitted ? ModelAdmissionStage.FormatBlocked
            : !withinCeiling ? ModelAdmissionStage.SizeAboveCeilingBlocked
            : !withinQuota ? ModelAdmissionStage.QuotaBlocked
            : ModelAdmissionStage.Recognized;

        return new(
            ModelId: request.ModelId,
            Recognized: true,
            PinnedArtifactMatched: true,
            FormatAdmitted: formatAdmitted,
            WithinSourceCeiling: withinCeiling,
            WithinDeviceQuota: withinQuota,
            Stage: stage,
            DeviceQuotaBytes: request.DeviceQuotaBytes,
            Profile: profile);
    }
}
