namespace HavenOS.Home.Core;

/// <summary>Typed first-run onboarding steps. Home only ever shows these when a fresh
/// profile has no persisted completion record; returning profiles skip the whole flow.</summary>
public enum HomeOnboardingStep
{
    Welcome,
    SignIn,
    DiscoverModels,
    DownloadModels,
    Complete,
}

/// <summary>Immutable per-profile completion record persisted atomically in the Home state
/// store. Versioned so future onboarding schema migrations can be applied explicitly. The
/// profile id is the canonical opaque string profile identity (never a parsed Guid); identity
/// comparisons are ordinal over the exact string.</summary>
public sealed record HomeOnboardingCompletion(
    int SchemaVersion,
    string ProfileId,
    DateTimeOffset CompletedAtUtc,
    bool SignedIn,
    bool ModelEligible,
    string? RecommendationManifestRevision)
{
    public const int CurrentSchemaVersion = 1;
}

/// <summary>Classified result of reading a profile's onboarding completion record. Differentiates
/// a genuine fresh profile from a corrupt/incompatible/conflicting record so the flow never treats
/// damaged state as fresh and never silently overwrites it.</summary>
public enum HomeOnboardingReadState
{
    NotCompleted,
    Completed,
    Corrupt,
    IncompatibleSchema,
    Conflict,
    Unavailable,
}

public sealed record HomeOnboardingCompletionRead(
    HomeOnboardingReadState State,
    HomeOnboardingCompletion? Completion = null,
    string? Code = null,
    string? Message = null)
{
    public bool IsCompleted => State == HomeOnboardingReadState.Completed;
}

/// <summary>A selectable local-model recommendation card for the Download Models? step.</summary>
public sealed record HomeOnboardingModelRecommendation(
    string ProviderId,
    string ModelId,
    string DisplayName,
    string HuggingFaceLocation,
    string? QuantizationOrFormat,
    long? ContextLimit,
    string? SourceUri);

/// <summary>Versioned recommendation manifest retrieved from under 9to1.uk. The list is never
/// hard-coded into the client; an unavailable manifest degrades to an honest unavailable state.</summary>
public sealed record HomeOnboardingRecommendationManifest(
    string Revision,
    IReadOnlyList<HomeOnboardingModelRecommendation> Recommendations);

/// <summary>Snapshot of the onboarding flow projected for the authored CUI surface.</summary>
public sealed record HomeOnboardingSnapshot(
    bool Completed,
    HomeOnboardingStep Step,
    bool SignInAvailable,
    bool? EligibilityChecked,
    bool EligibleModelAvailable,
    bool? ModelInstallStarted,
    IReadOnlyList<HomeOnboardingModelRecommendation> Recommendations,
    bool? ManifestAvailable,
    string Status,
    string? ErrorCode,
    string? ErrorMessage,
    HomeOnboardingDownloadState? Download = null)
{
    public static HomeOnboardingSnapshot WelcomeInitial(bool signInAvailable) => new(
        Completed: false,
        Step: HomeOnboardingStep.Welcome,
        SignInAvailable: signInAvailable,
        EligibilityChecked: null,
        EligibleModelAvailable: false,
        ModelInstallStarted: null,
        Recommendations: [],
        ManifestAvailable: null,
        Status: "Welcome to 9to1",
        ErrorCode: null,
        ErrorMessage: null);
}

/// <summary>Honest download state contract. A network/endpoint-blocked install always reports
/// <see cref="HomeOnboardingDownloadStateCode.Blocked"/> and never presents as complete until a real
/// verified install completes.</summary>
public enum HomeOnboardingDownloadStateCode
{
    Idle,
    Blocked,
    Downloading,
    Completed,
    Failed,
}

public sealed record HomeOnboardingDownloadState(
    HomeOnboardingDownloadStateCode Code,
    double? Progress,
    string? Message,
    string? RecommendationKey = null);
