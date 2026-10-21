using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace HavenOS.Home.Core;

/// <summary>Returns a versioned recommendation manifest from under 9to1.uk. A source that is
/// not wired (or a failed retrieval) must return an honest unavailable result rather than a
/// fabricated recommendation list.</summary>
public interface IHomeOnboardingRecommendationSource
{
    Task<HomeCoreOperationResult<HomeOnboardingRecommendationManifest>> GetRecommendationsAsync(
        CancellationToken cancellationToken);
}

/// <summary>Launches the system browser for the 9to1.uk/auth sign-in flow. When no launcher is
/// available the flow must surface an honest scoped unavailable state rather than minting auth.</summary>
public interface IHomeOnboardingBrowserLauncher
{
    Task<bool> LaunchAuthAsync(CancellationToken cancellationToken);
}

/// <summary>Typed backend for the 9to1 first-run welcome flow. Completion records are partitioned by
/// the actual profile stable ID (never a shared device-canonical payload): each record is read and
/// written under a profile-scoped record id, and reads verify the payload's profile id, schema and
/// revision. Corrupt/incompatible/conflicting records are reported as structured recoverable states
/// and are never treated as fresh or silently overwritten. No completion is reported unless the
/// write actually commits.</summary>
public sealed class HomeOnboardingService(
    IHomeCoreStateStore stateStore,
    IModelProviderRegistry? modelRegistry = null,
    IHomeOnboardingRecommendationSource? recommendationSource = null,
    IHomeOnboardingBrowserLauncher? browserLauncher = null,
    TimeProvider? clock = null,
    IHomeDulcheModelEvidenceSource? modelEvidenceSource = null)
{
    private const string RecordType = "home.onboarding";
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private static string RecordIdFor(string profileId) => "home.onboarding." + profileId;

    private static bool IsRealProfile(string profileId) => !string.IsNullOrWhiteSpace(profileId);

    /// <summary>Opens the first-run flow for a fresh profile. Returns the initial snapshot.</summary>
    public HomeOnboardingSnapshot Open() => HomeOnboardingSnapshot.WelcomeInitial(SignInAvailable);

    public bool SignInAvailable => browserLauncher is not null;

    /// <summary>Reads the profile-specific completion record with structured classification. The
    /// record id is scoped to the real profile stable ID, never a shared device record. Identity
    /// comparison over the profile id is ordinal and exact.</summary>
    public async Task<HomeOnboardingCompletionRead> ReadCompletionAsync(
        string profileId, CancellationToken cancellationToken = default)
    {
        if (!IsRealProfile(profileId))
            return new(HomeOnboardingReadState.Unavailable, Code: "ProfileUnavailable", Message: "The local profile id is not ready.");
        var read = await stateStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess)
            return new(HomeOnboardingReadState.Unavailable, Code: read.Failure?.Code.ToString() ?? "HomeStateUnavailable",
                Message: read.Failure?.Message ?? "Home state is unavailable.");
        var record = read.State!.Records.FirstOrDefault(item => item.RecordId == RecordIdFor(profileId));
        if (record is null) return new(HomeOnboardingReadState.NotCompleted);
        if (record.RecordType != RecordType || record.Scope != HomeDataScope.DeviceLocal ||
            record.Authority != HomeRecordAuthority.LocalCanonical)
            return new(HomeOnboardingReadState.Conflict, Code: "HomeStateScopeUnsupported",
                Message: "Onboarding completion record has an unsupported scope/authority.");
        if (record.SchemaVersion != HomeOnboardingCompletion.CurrentSchemaVersion)
            return new(HomeOnboardingReadState.IncompatibleSchema, Code: "OnboardingIncompatibleSchema",
                Message: "Onboarding completion schema is newer than supported; preserve it for migration.");
        try
        {
            var completion = record.Payload.Deserialize<HomeOnboardingCompletion>();
            if (completion is null)
                return new(HomeOnboardingReadState.Corrupt, Code: "OnboardingCorrupt",
                    Message: "Onboarding completion payload could not be decoded.");
            if (completion.SchemaVersion != HomeOnboardingCompletion.CurrentSchemaVersion ||
                !string.Equals(completion.ProfileId, profileId, StringComparison.Ordinal))
                return new(HomeOnboardingReadState.Corrupt, Code: "OnboardingMismatch",
                    Message: "Onboarding completion payload does not match this profile.");
            return new(HomeOnboardingReadState.Completed, completion);
        }
        catch (JsonException exception)
        {
            return new(HomeOnboardingReadState.Corrupt, Code: "OnboardingCorrupt",
                Message: $"Onboarding completion payload is corrupt ({exception.GetType().Name}).");
        }
    }

    /// <summary>True when the current profile already completed the flow. Never reads a shared
    /// device-canonical record; the profile id is the partition key.</summary>
    public async Task<bool> IsCompletedAsync(string profileId, CancellationToken cancellationToken = default) =>
        (await ReadCompletionAsync(profileId, cancellationToken).ConfigureAwait(false)).IsCompleted;

    /// <summary>Determines whether at least one eligible, actually-installed local model is available.
    /// Uses the real registry and treats the installed/ready evidence as the source of truth; never a
    /// hard-coded model id or provider claim as an eligibility rule.</summary>
    public async Task<bool> HasEligibleLocalModelAsync(CancellationToken cancellationToken = default)
    {
        // Preferred evidence: the shared verified Dulche model store. Its packaged evidence source
        // only reports a model when the recorded manifest is valid, the blob is a regular file of
        // the recorded size, and its bytes re-hash to the manifest's SHA-256 (installation evidence,
        // never a permission or execution grant). This is independent of any warmed provider list.
        if (modelEvidenceSource is not null)
        {
            try
            {
                var verified = await modelEvidenceSource.FindEligibleAsync(cancellationToken).ConfigureAwait(false);
                if (verified is not null) return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Verification I/O failures degrade honestly: fall through to the provider warm-up
                // list rather than fabricating eligibility or aborting the flow.
            }
        }

        // Secondary warm-up evidence: a provider reports the model locally and the model itself is
        // present and advertises core text (basic preview). No capability is granted beyond what is
        // advertised; this alone is not the verified installed-artifact evidence above.
        if (modelRegistry is null) return false;
        var policy = new ModelCataloguePolicy(AllowLocal: true, AllowRemote: false);
        IReadOnlyList<ProviderModelDescriptor> models;
        try
        {
            models = await modelRegistry.GetModelsAsync(policy, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
        return models.Any(model => model.IsLocal && model.Supports(ToolCapability.Text) &&
            !string.IsNullOrWhiteSpace(model.Name));
    }

    /// <summary>Returns up to three preferred recommendations when an eligible local model is absent.</summary>
    public async Task<HomeCoreOperationResult<HomeOnboardingRecommendationManifest>> GetRecommendationsAsync(
        CancellationToken cancellationToken = default)
    {
        if (recommendationSource is null)
            return new(false, "ManifestUnavailable", "The recommendation manifest source is not configured for this build.");
        return await recommendationSource.GetRecommendationsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Attempts to open the system browser for 9to1.uk/auth. Returns false (and does not mint
    /// auth) when no verified launcher is available.</summary>
    public async Task<bool> BeginSignInAsync(CancellationToken cancellationToken = default)
    {
        if (browserLauncher is null) return false;
        return await browserLauncher.LaunchAuthAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Persists completion atomically/versioned under the profile-scoped record id. A
    /// corrupt/incompatible/conflicting existing record is preserved and reported as a retryable
    /// non-success so the flow never overwrites damaged state or reports false completion.</summary>
    public async Task<HomeCoreOperationResult<HomeOnboardingCompletion>> PersistCompletionAsync(
        string profileId, bool signedIn, bool modelEligible, string? manifestRevision,
        CancellationToken cancellationToken = default)
    {
        if (!IsRealProfile(profileId)) throw new ArgumentException("A real profile id is required.", nameof(profileId));

        var existingRead = await ReadCompletionAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (existingRead.State is HomeOnboardingReadState.Corrupt or HomeOnboardingReadState.IncompatibleSchema
            or HomeOnboardingReadState.Conflict or HomeOnboardingReadState.Unavailable)
            return new(false, existingRead.Code ?? "OnboardingStateDamaged",
                existingRead.Message ?? "Onboarding completion state is damaged and was preserved for recovery.");
        // Semantic idempotency: an already-completed profile must not write again, so a replayed
        // completion is a no-op success (the existing durable record is returned untouched).
        if (existingRead.State is HomeOnboardingReadState.Completed && existingRead.Completion is { } existingDurable)
            return new(true, "Succeeded", "First-run flow completed for this profile.", existingDurable);

        var completion = new HomeOnboardingCompletion(
            HomeOnboardingCompletion.CurrentSchemaVersion, profileId, _clock.GetUtcNow(),
            signedIn, modelEligible, manifestRevision);

        var read = await stateStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess)
            return new(false, read.Failure?.Code.ToString() ?? "HomeStateUnavailable",
                read.Failure?.Message ?? "Home state is unavailable.", null, read.Failure?.Retryable ?? true);

        var recordId = RecordIdFor(profileId);
        var existing = read.State!.Records.FirstOrDefault(item => item.RecordId == recordId);
        var expectedRevision = existing?.Revision ?? 0;
        var record = new HomeCoreStateRecord(recordId, RecordType, HomeOnboardingCompletion.CurrentSchemaVersion,
            HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical, 0,
            JsonSerializer.SerializeToElement(completion));
        var write = await stateStore.WriteAsync(record, expectedRevision, cancellationToken).ConfigureAwait(false);
        return write.IsSuccess
            ? new(true, "Succeeded", "First-run flow completed for this profile.", completion, false, write.State!.Revision)
            : new(false, write.Failure?.Code.ToString() ?? "HomeStateWriteFailed",
                write.Failure?.Message ?? "Home could not persist the onboarding completion.",
                null, write.Failure?.Retryable ?? true);
    }

    /// <summary>Renders a human-readable summary line for status surfaces.</summary>
    public static string Summarize(HomeOnboardingCompletion completion) =>
        $"{completion.CompletedAtUtc:g} · signed-in={(completion.SignedIn ? "yes" : "no")} · " +
        $"local-model-eligible={(completion.ModelEligible ? "yes" : "no")}";
}
