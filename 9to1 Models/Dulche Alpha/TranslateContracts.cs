using Haven.Core;

namespace Dulche.Runtime.Translate;

public enum TranslationVariantStatus { Pending, Translating, Completed, Failed, Conflict, Cancelled }
public enum TranslationJobStatus { Preparing, Translating, ReviewAvailable, Applying, Exporting, Completed, Paused, Failed, Cancelled }
public enum TranslationContentKind { HumanLanguage, Code, Command, Url, Formula, Identifier, Markup, Number, Unknown }
public enum TranslationPrivacyPolicy { FollowRoute, LocalOnly, CloudAllowed }
public enum TranslationCapabilityState { Unknown, Supported, Unsupported }
public enum LanguageResolutionState { Resolved, Ambiguous, Unsupported, Invalid }
public enum GlossaryTermPolicy { Preferred, Required, DoNotTranslate }
public enum TranslationMediaKind { Image, Pdf, Audio, Video }

public sealed record TranslationLanguage(string Code, string DisplayName);
public sealed record LanguageResolution(LanguageResolutionState State, TranslationLanguage? Language, IReadOnlyList<TranslationLanguage> Candidates, DulcheError? Error);

public sealed record TranslationArtifactReference(string AppKey, string ArtifactId, string? Revision = null, string? ObjectId = null);
public sealed record TranslationSourceSegment(string SegmentId, string Text, TranslationContentKind ContentKind = TranslationContentKind.HumanLanguage, string? ObjectId = null, string? Revision = null);
public sealed record TranslationSource(
    string? Text,
    string? SourceLanguage,
    TranslationArtifactReference? Artifact = null,
    IReadOnlyList<TranslationSourceSegment>? Segments = null,
    string? Format = null,
    string? MediaReference = null,
    TranslationMediaKind? MediaKind = null,
    string? ExtractionNotice = null)
{
    public IReadOnlyList<TranslationSourceSegment> EffectiveSegments => Segments is { Count: > 0 }
        ? Segments
        : string.IsNullOrEmpty(Text) ? [] : [new("text:0", Text, TranslationContentKind.HumanLanguage)];
}

public sealed record TranslationSegment(
    string SegmentId,
    string SourceText,
    string TargetText,
    TranslationContentKind ContentKind,
    string? ObjectId = null,
    string? SourceRevision = null,
    string? SourceHash = null,
    bool WasManuallyEdited = false,
    string? Conflict = null);

public sealed record TranslationSegmentMap(string TranslationVariantId, IReadOnlyList<TranslationSegment> Segments, DateTimeOffset UpdatedAt);

public sealed record TranslationProvenance(
    string? SourceArtifactId,
    string? SourceObjectId,
    string? SourceRevision,
    string? SourceLanguage,
    string TargetLanguage,
    string TranslationSetId,
    string TranslationVariantId,
    IReadOnlyList<string> GlossaryIds,
    string? ModelRouteId,
    string? ProviderId,
    string? ModelId,
    TranslationPrivacyPolicy PrivacyPolicy,
    DateTimeOffset TranslatedAt,
    string? SourceFingerprint = null);

public sealed record TranslationVariant(
    string TranslationVariantId,
    string TranslationSetId,
    TranslationLanguage TargetLanguage,
    TranslationVariantStatus Status,
    string? TranslatedContent,
    string? CanonicalOutputReference,
    long Revision,
    TranslationProvenance? Provenance,
    IReadOnlyList<TranslationSegment> Segments,
    IReadOnlyList<DulcheError> Errors,
    IReadOnlyList<string> Warnings,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    bool IsManuallyEdited = false);

public sealed record TranslationSet(
    string TranslationSetId,
    TranslationSource Source,
    TranslationLanguage? SourceLanguage,
    string? Instruction,
    IReadOnlyList<string> GlossaryIds,
    TranslationPrivacyPolicy PrivacyPolicy,
    string? ModelRouteId,
    long Revision,
    IReadOnlyList<TranslationVariant> Variants,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    IReadOnlyList<string>? Warnings = null);

public sealed record TranslationGlossaryEntry(
    string SourceTerm,
    GlossaryTermPolicy Policy,
    IReadOnlyDictionary<string, string>? TargetTerms = null,
    string? Notes = null,
    string? Context = null,
    IReadOnlySet<string>? Applicability = null);

public sealed record TranslationGlossary(
    string GlossaryId,
    string Name,
    string? Description,
    long Revision,
    IReadOnlyList<TranslationGlossaryEntry> Entries,
    IReadOnlySet<string>? Applicability = null,
    DateTimeOffset CreatedAt = default,
    DateTimeOffset ModifiedAt = default);

public sealed record TranslationGlossaryPatch(
    string? Name = null,
    string? Description = null,
    IReadOnlyList<TranslationGlossaryEntry>? Entries = null,
    IReadOnlySet<string>? Applicability = null);

public sealed record ScopedTranslationGlossary(string Name, IReadOnlyList<TranslationGlossaryEntry> Entries, IReadOnlySet<string>? Applicability = null);

public sealed record TranslationOptions(
    string? SourceLanguage = null,
    string? Instruction = null,
    IReadOnlyList<string>? GlossaryIds = null,
    IReadOnlyList<ScopedTranslationGlossary>? RequestGlossaries = null,
    TranslationPrivacyPolicy PrivacyPolicy = TranslationPrivacyPolicy.FollowRoute,
    string? ModelRouteId = null,
    string? SelectedModelKey = null,
    string? CallerId = null,
    string? IdempotencyKey = null,
    string? ConversationId = null,
    IReadOnlySet<string>? RequiredCapabilities = null);

public sealed record TranslationJobConfiguration(
    TranslationSource Source,
    IReadOnlyList<string> TargetLanguages,
    TranslationOptions? Options = null,
    string? IdempotencyKey = null,
    string? CallerId = null);

public sealed record TranslationJob(
    string TranslationJobId,
    string? IdempotencyKey,
    TranslationJobStatus Status,
    string? TranslationSetId,
    TranslationJobConfiguration Configuration,
    IReadOnlyList<string> CompletedVariantIds,
    IReadOnlyList<string> FailedTargets,
    long Revision,
    int CompletedUnits,
    int TotalUnits,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt);

public sealed record TranslationTargetResult(string TargetLanguage, string? TranslationVariantId, DulcheError? Error);
public sealed record TranslationActionResult(string TranslationSetId, IReadOnlyList<TranslationTargetResult> Targets, bool Succeeded, DulcheError? Error = null);
public sealed record TranslationSourceRevisionResult(string TranslationSetId, string? PreviousRevision, string? CurrentRevision, bool IsStale, IReadOnlyList<string> ChangedSegmentIds, DulcheError? Error = null);
public sealed record TranslationUpdateResult(TranslationSet? Set, IReadOnlyList<string> UpdatedSegmentIds, IReadOnlyList<string> PreservedSegmentIds, IReadOnlyList<string> ConflictedSegmentIds, DulcheError? Error = null);
public sealed record TranslationMediaExtraction(TranslationSource Source, bool LayoutPreserved, IReadOnlyList<string> Warnings);
public sealed record TranslationDetection(string LanguageName, string? LanguageCode, double? Confidence, IReadOnlyList<string> Alternatives);

public interface ILanguageResolver
{
    LanguageResolution Resolve(string? languageLabel, bool allowAutoDetect = false);
}

public interface ITranslationCapabilityCatalog
{
    TranslationCapabilityState GetState(string providerId, string sourceLanguage, string targetLanguage, TranslationMediaKind? modality = null);
}

public interface ITranslationArtifactAdapter
{
    Task<OperationResult<TranslationSource>> ReadAsync(TranslationArtifactReference reference, DulcheCaller caller, CancellationToken cancellationToken);
    Task<OperationResult<TranslationSource>> ReadCurrentAsync(TranslationArtifactReference reference, DulcheCaller caller, CancellationToken cancellationToken);
    Task<OperationResult<string>> CreateDerivativeAsync(TranslationArtifactReference source, TranslationVariant variant, string idempotencyKey, DulcheCaller caller, CancellationToken cancellationToken);
}

public interface ITranslationMediaAdapter
{
    Task<OperationResult<TranslationMediaExtraction>> ExtractAsync(string sourceReference, TranslationMediaKind kind, IReadOnlyList<string> targetLanguages, DulcheCaller caller, CancellationToken cancellationToken);
}

public interface ITranslationRepository
{
    Task<T> ReadAsync<T>(Func<TranslationDatabaseState, T> read, CancellationToken cancellationToken);
    Task<T> UpdateAsync<T>(Func<TranslationDatabaseState, (TranslationDatabaseState State, T Result)> update, CancellationToken cancellationToken);
}

public sealed record TranslationDatabaseState(
    int SchemaVersion,
    IReadOnlyList<TranslationSet> Sets,
    IReadOnlyList<TranslationGlossary> Glossaries,
    IReadOnlyList<TranslationJob> Jobs)
{
    public static TranslationDatabaseState Empty { get; } = new(1, [], [], []);
}

