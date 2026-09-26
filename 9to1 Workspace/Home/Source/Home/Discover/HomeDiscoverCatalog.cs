using System.Text.Json;

namespace HavenOS.Home.Discover;

public enum HomeDiscoverCategory { Chat, Image, Voice, Audio, Video }
public enum HomeDiscoverLocality { Unknown, Local, Cloud, Remote }
public enum HomeDiscoverConnectionState { Unknown, Connected, Disconnected, NeedsAuthentication, Unavailable }
public enum HomeDiscoverInstallState { Unknown, Available, Installed, Pulling, Connecting, Connected, Failed }
public enum HomeDiscoverAction { Install, Pull, Connect, OpenInPicker }
public enum HomeDiscoverSourceState { Available, Empty, Partial, Unavailable, Failed }
public enum HomeDiscoverVoiceMode { Conversational, Monologue, LiveListener, LiveTranslate }

public sealed record HomeDiscoverIdentity(string ProviderId, string ModelId, string? ArtifactRevision = null)
{
    // Length-prefix each component so IDs containing separators cannot collide.
    public string StableKey => $"{Part(ProviderId)}{Part(ModelId)}{Part(ArtifactRevision ?? "current")}";

    private static string Part(string value) => $"{value.Length}:{value}";
}

/// <summary>Nullable fields deliberately distinguish unknown metadata from known false/empty values.</summary>
public sealed record HomeDiscoverVoiceCapabilities(
    bool? Conversational,
    bool? Monologue,
    bool? LiveListener,
    bool? LiveTranslate,
    bool? VisualUnderstanding,
    bool? ToolActions,
    bool? Streaming,
    bool? InterruptionAndTurnTaking,
    IReadOnlyList<string>? Languages,
    IReadOnlyList<string>? Accents,
    IReadOnlyList<string>? TranslationLanguagePairs,
    bool? OneWayInterpretation,
    bool? BidirectionalInterpretation,
    bool? MultilingualInterpretation,
    TimeSpan? TypicalLatency);

public sealed record HomeDiscoverVoiceQualityEvidence(
    HomeDiscoverVoiceMode Mode,
    decimal? NormalizedScore,
    string? EvidenceSource,
    DateTimeOffset? MeasuredAtUtc,
    IReadOnlyList<string>? Reasons,
    decimal? SpeechNaturalness = null,
    decimal? RecognitionAccuracy = null,
    decimal? ReasoningQuality = null,
    decimal? InstructionFollowing = null,
    decimal? TurnTaking = null,
    decimal? InterruptionHandling = null,
    decimal? LongFormCoherence = null,
    decimal? AudioContextUnderstanding = null,
    decimal? ToolReliability = null,
    decimal? TranslationQuality = null,
    decimal? OutputSpeechQuality = null,
    decimal? CodeSwitching = null);

public sealed record HomeDiscoverVoiceVariant(string VoiceId, string Name, string? Locale,
    string? SampleReference = null);

public sealed record HomeDiscoverVoiceMetadata(
    HomeDiscoverVoiceCapabilities Capabilities,
    IReadOnlyList<HomeDiscoverVoiceQualityEvidence>? QualityEvidence,
    IReadOnlyList<HomeDiscoverVoiceVariant>? NamedVoices);

public sealed record HomeDiscoverModel(
    HomeDiscoverIdentity Identity,
    string Name,
    string? Origin,
    IReadOnlySet<HomeDiscoverCategory>? Categories,
    IReadOnlySet<string>? Capabilities,
    string? Architecture,
    string? ParameterInformation,
    long? ContextLimit,
    string? QuantizationOrFormat,
    HomeDiscoverLocality Locality,
    HomeDiscoverInstallState InstallState,
    HomeDiscoverConnectionState ConnectionState,
    string? HardwareFitEstimate,
    string? EntitlementClass,
    string? CostClass,
    string? PrivacyClass,
    string? Residency,
    HomeDiscoverVoiceMetadata? Voice,
    IReadOnlySet<HomeDiscoverAction> SupportedActions,
    string? DisplayName = null);

public sealed record HomeDiscoverProviderSnapshot(
    string ProviderId,
    string DisplayName,
    HomeDiscoverConnectionState ConnectionState,
    HomeDiscoverSourceState State,
    string Revision,
    IReadOnlyList<HomeDiscoverModel> Models,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    bool Retryable = false);

public sealed record HomeDiscoverActionRequest(HomeDiscoverIdentity Identity, HomeDiscoverAction Action,
    string IdempotencyKey);

public sealed record HomeDiscoverActionResult(string OperationId, HomeDiscoverIdentity Identity,
    HomeDiscoverAction Action, bool Succeeded, string Code, string Message, bool Retryable,
    bool Recoverable, long? Revision = null);

/// <summary>One configured model/provider catalogue. Provider failures are reported independently.</summary>
public interface IHomeDiscoverCatalogueSource
{
    string ProviderId { get; }
    Task<HomeDiscoverProviderSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
    Task<HomeDiscoverActionResult> ExecuteAsync(HomeDiscoverActionRequest request,
        CancellationToken cancellationToken);
}

public sealed record HomeDiscoverQuery(
    string? SearchText = null,
    HomeDiscoverCategory? Category = null,
    HomeDiscoverLocality? Locality = null,
    HomeDiscoverInstallState? InstallState = null,
    IReadOnlySet<string>? RequiredCapabilities = null,
    IReadOnlySet<string>? ProviderIds = null,
    string? PageToken = null,
    int PageSize = 50)
{
    public HomeDiscoverQuery Validate()
    {
        if (PageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(PageSize), "Page size must be between 1 and 200.");
        if (PageToken is { Length: > 2048 })
            throw new ArgumentException("Page token exceeds the 2048 character limit.", nameof(PageToken));
        return this with { SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim() };
    }
}

public sealed record HomeDiscoverError(string Code, string Message, string Target, bool Retryable,
    bool Recoverable);

public sealed record HomeDiscoverPage(HomeDiscoverSourceState State,
    IReadOnlyList<HomeDiscoverModel> Models,
    IReadOnlyList<HomeDiscoverProviderSnapshot> Providers,
    long TotalCount,
    string SnapshotRevision,
    string? NextPageToken,
    HomeDiscoverError? Error = null);

public sealed record HomeDiscoverComparison(IReadOnlyList<HomeDiscoverModel> Models,
    IReadOnlyList<HomeDiscoverIdentity> MissingIdentities, long Revision);

public sealed record HomeVoiceRecommendation(HomeDiscoverIdentity Identity, string Label,
    decimal Score, string EvidenceSource, DateTimeOffset MeasuredAtUtc, IReadOnlyList<string> Reasons);

/// <summary>
/// Aggregates every configured catalogue source, isolates source failures, preserves
/// provider-scoped identity, and never manufactures absent metadata or quality measurements.
/// </summary>
public sealed class HomeDiscoverCatalog(IEnumerable<IHomeDiscoverCatalogueSource> sources)
{
    private readonly IReadOnlyList<IHomeDiscoverCatalogueSource> _sources = ValidateSources(sources);
    private static readonly JsonSerializerOptions CursorJson = new(JsonSerializerDefaults.Web);

    public async Task<HomeDiscoverPage> SearchAsync(HomeDiscoverQuery query,
        CancellationToken cancellationToken = default)
    {
        var validated = (query ?? throw new ArgumentNullException(nameof(query))).Validate();
        var selectedSources = validated.ProviderIds is { Count: > 0 }
            ? _sources.Where(source => validated.ProviderIds.Contains(source.ProviderId, StringComparer.Ordinal)).ToArray()
            : _sources.ToArray();

        var snapshots = await Task.WhenAll(selectedSources.Select(source => ReadSourceAsync(source, cancellationToken)))
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var snapshotRevision = string.Join("|", snapshots.OrderBy(item => item.ProviderId, StringComparer.Ordinal)
            .Select(item => $"{item.ProviderId}={item.Revision}:{item.State}"));
        var afterKey = DecodeCursor(validated.PageToken, snapshotRevision);
        var matching = snapshots.SelectMany(snapshot => snapshot.Models)
            .Where(model => Matches(model, validated))
            .OrderBy(model => model.Identity.StableKey, StringComparer.Ordinal)
            .ToArray();
        var page = matching.Where(model => afterKey is null ||
                StringComparer.Ordinal.Compare(model.Identity.StableKey, afterKey) > 0)
            .Take(validated.PageSize + 1)
            .ToArray();
        var hasMore = page.Length > validated.PageSize;
        var visible = hasMore ? page[..validated.PageSize] : page;
        var next = hasMore && visible.Length > 0
            ? EncodeCursor(snapshotRevision, visible[^1].Identity.StableKey)
            : null;
        var state = AggregateState(snapshots);
        return new(state, visible, snapshots, matching.LongLength, snapshotRevision, next);
    }

    public async Task<HomeDiscoverComparison> CompareAsync(IReadOnlyList<HomeDiscoverIdentity> identities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identities);
        if (identities.Count is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(identities), "Compare between 1 and 20 models.");
        if (identities.Any(identity => identity is null || string.IsNullOrWhiteSpace(identity.ProviderId) ||
                                       string.IsNullOrWhiteSpace(identity.ModelId)))
            throw new ArgumentException("Every compared model requires stable provider and model IDs.", nameof(identities));
        var requested = identities.DistinctBy(identity => identity.StableKey, StringComparer.Ordinal).ToArray();
        var snapshots = await Task.WhenAll(_sources.Select(source => ReadSourceAsync(source, cancellationToken)))
            .ConfigureAwait(false);
        var revision = ComputeRevision(snapshots);
        var index = snapshots.SelectMany(snapshot => snapshot.Models)
            .ToDictionary(model => model.Identity.StableKey, StringComparer.Ordinal);
        var matches = requested.Where(identity => index.ContainsKey(identity.StableKey))
            .Select(identity => index[identity.StableKey]).ToArray();
        var missing = requested.Where(identity => !index.ContainsKey(identity.StableKey)).ToArray();
        return new(matches, missing, revision);
    }

    public async Task<HomeDiscoverActionResult> ExecuteAsync(HomeDiscoverActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Failed(request, "Discover.IdempotencyKeyRequired", "The action could not be safely identified.");
        var source = _sources.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.ProviderId, request.Identity.ProviderId));
        if (source is null)
            return Failed(request, "Discover.ProviderUnavailable", "The selected model provider is not configured.");

        try
        {
            var snapshot = await ReadSourceAsync(source, cancellationToken).ConfigureAwait(false);
            var model = snapshot.Models.FirstOrDefault(item =>
                StringComparer.Ordinal.Equals(item.Identity.StableKey, request.Identity.StableKey));
            if (model is null || !model.SupportedActions.Contains(request.Action))
                return Failed(request, "Discover.ActionUnsupported", "This action is unavailable for the selected model.");
            var result = await source.ExecuteAsync(request, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The model provider returned no action result.");
            if (result.Identity.StableKey != request.Identity.StableKey || result.Action != request.Action ||
                string.IsNullOrWhiteSpace(result.OperationId) || string.IsNullOrWhiteSpace(result.Code))
                return Failed(request, "Discover.InvalidProviderResult", "The provider returned an invalid action result.");
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Failed(request, "Discover.ProviderOperationFailed",
                "The provider action failed. Refresh the catalogue before trying again.", retryable: true);
        }
    }

    /// <summary>
    /// Returns contextual top voice recommendations only when provider-supplied capability
    /// and quality evidence supports them. Unmeasured dimensions remain absent.
    /// </summary>
    public static IReadOnlyList<HomeVoiceRecommendation> RecommendVoices(
        IEnumerable<HomeDiscoverModel> models,
        HomeDiscoverVoiceMode mode,
        HomeDiscoverLocality? locality = null,
        int limit = 5)
    {
        ArgumentNullException.ThrowIfNull(models);
        if (limit is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(limit));
        var label = mode switch
        {
            HomeDiscoverVoiceMode.Conversational => "Best for Conversational",
            HomeDiscoverVoiceMode.Monologue => "Best for Monologue",
            HomeDiscoverVoiceMode.LiveListener => "Best for Live Listener",
            HomeDiscoverVoiceMode.LiveTranslate => "Best for Live Translate",
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        return models.Where(model => model.Categories?.Contains(HomeDiscoverCategory.Voice) == true)
            .Where(model => locality is null || model.Locality == locality.Value)
            .Where(model => VoiceModeSupport(model.Voice?.Capabilities, mode) == true)
            .SelectMany(model => (model.Voice?.QualityEvidence ?? Array.Empty<HomeDiscoverVoiceQualityEvidence>())
                .Where(evidence => evidence.Mode == mode && evidence.NormalizedScore is not null &&
                                   !string.IsNullOrWhiteSpace(evidence.EvidenceSource) && evidence.MeasuredAtUtc is not null &&
                                   evidence.Reasons?.Any(reason => !string.IsNullOrWhiteSpace(reason)) == true)
                .Select(evidence => new HomeVoiceRecommendation(model.Identity, label,
                    evidence.NormalizedScore!.Value,
                    evidence.EvidenceSource!,
                    evidence.MeasuredAtUtc!.Value,
                    evidence.Reasons?.Where(reason => !string.IsNullOrWhiteSpace(reason)).ToArray() ?? Array.Empty<string>())))
            .OrderByDescending(recommendation => recommendation.Score)
            .ThenBy(recommendation => recommendation.Identity.StableKey, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    private async Task<HomeDiscoverProviderSnapshot> ReadSourceAsync(
        IHomeDiscoverCatalogueSource source,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await source.GetSnapshotAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The provider returned no catalogue snapshot.");
            if (!StringComparer.Ordinal.Equals(result.ProviderId, source.ProviderId))
                throw new InvalidDataException("The provider returned a catalogue for a different provider identity.");
            ArgumentNullException.ThrowIfNull(result.Models);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var model in result.Models)
            {
                if (model is null || string.IsNullOrWhiteSpace(model.Identity.ProviderId) ||
                    string.IsNullOrWhiteSpace(model.Identity.ModelId) || string.IsNullOrWhiteSpace(model.Name) ||
                    !StringComparer.Ordinal.Equals(model.Identity.ProviderId, source.ProviderId) ||
                    model.SupportedActions is null || !ids.Add(model.Identity.StableKey))
                    throw new InvalidDataException("The provider returned an invalid or duplicate model identity.");
                foreach (var quality in model.Voice?.QualityEvidence ?? Array.Empty<HomeDiscoverVoiceQualityEvidence>())
                    if (quality.NormalizedScore is < 0 or > 100)
                        throw new InvalidDataException("Voice quality evidence scores must be between 0 and 100.");
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(source.ProviderId, source.ProviderId, HomeDiscoverConnectionState.Unavailable,
                HomeDiscoverSourceState.Failed, string.Empty, Array.Empty<HomeDiscoverModel>(),
                exception is InvalidDataException ? "Discover.InvalidCatalogue" : "Discover.ProviderUnavailable",
                exception is InvalidDataException ? exception.Message : "The configured model provider could not be queried.",
                Retryable: true);
        }
    }

    private static bool Matches(HomeDiscoverModel model, HomeDiscoverQuery query)
    {
        if (query.Category is { } category && model.Categories?.Contains(category) != true) return false;
        if (query.Locality is { } locality && model.Locality != locality) return false;
        if (query.InstallState is { } installState && model.InstallState != installState) return false;
        if (query.RequiredCapabilities is { Count: > 0 } required &&
            (model.Capabilities is null || !required.All(capability => model.Capabilities.Contains(capability)))) return false;
        if (query.SearchText is not { } search) return true;
        return Contains(model.Name, search) || Contains(model.Identity.ProviderId, search) ||
               Contains(model.Identity.ModelId, search) || Contains(model.DisplayName, search) ||
               Contains(model.Origin, search) || Contains(model.Architecture, search) ||
               Contains(model.ParameterInformation, search) || Contains(model.QuantizationOrFormat, search) ||
               Contains(model.EntitlementClass, search) || Contains(model.CostClass, search) ||
               Contains(model.PrivacyClass, search) || Contains(model.Residency, search) ||
               (model.Capabilities?.Any(value => Contains(value, search)) ?? false) ||
               (model.Categories?.Any(value => Contains(value.ToString(), search)) ?? false);
    }

    private static bool? VoiceModeSupport(HomeDiscoverVoiceCapabilities? capabilities, HomeDiscoverVoiceMode mode) => mode switch
    {
        HomeDiscoverVoiceMode.Conversational => capabilities?.Conversational,
        HomeDiscoverVoiceMode.Monologue => capabilities?.Monologue,
        HomeDiscoverVoiceMode.LiveListener => capabilities?.LiveListener,
        HomeDiscoverVoiceMode.LiveTranslate => capabilities?.LiveTranslate,
        _ => null,
    };

    private static HomeDiscoverSourceState AggregateState(IReadOnlyList<HomeDiscoverProviderSnapshot> providers)
    {
        if (providers.Count == 0 || providers.All(provider => provider.State is HomeDiscoverSourceState.Unavailable or HomeDiscoverSourceState.Failed))
            return HomeDiscoverSourceState.Unavailable;
        if (providers.Any(provider => provider.State is HomeDiscoverSourceState.Unavailable or HomeDiscoverSourceState.Failed or HomeDiscoverSourceState.Partial))
            return HomeDiscoverSourceState.Partial;
        return providers.All(provider => provider.State == HomeDiscoverSourceState.Empty)
            ? HomeDiscoverSourceState.Empty
            : HomeDiscoverSourceState.Available;
    }

    private static IReadOnlyList<IHomeDiscoverCatalogueSource> ValidateSources(
        IEnumerable<IHomeDiscoverCatalogueSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var configured = sources.ToArray();
        if (configured.Any(source => source is null || string.IsNullOrWhiteSpace(source.ProviderId)))
            throw new ArgumentException("Every configured Discover source must have a stable provider ID.", nameof(sources));
        if (configured.Select(source => source.ProviderId).Distinct(StringComparer.Ordinal).Count() != configured.Length)
            throw new ArgumentException("Only one Discover source may be registered for each stable provider ID.", nameof(sources));
        return configured;
    }

    private static string ComputeRevision(IReadOnlyList<HomeDiscoverProviderSnapshot> snapshots) =>
        string.Join("|", snapshots.OrderBy(snapshot => snapshot.ProviderId, StringComparer.Ordinal)
            .Select(snapshot => $"{snapshot.ProviderId}={snapshot.Revision}:{snapshot.State}"));

    private static string? DecodeCursor(string? token, string currentRevision)
    {
        if (token is null) return null;
        try
        {
            var cursor = JsonSerializer.Deserialize<DiscoverCursor>(Convert.FromBase64String(token), CursorJson)
                ?? throw new FormatException();
            if (!StringComparer.Ordinal.Equals(cursor.Revision, currentRevision))
                throw new HomeDiscoverCursorException("Discover.CursorStale", "The provider catalogue changed. Start the search again.");
            return cursor.AfterIdentity;
        }
        catch (HomeDiscoverCursorException) { throw; }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new HomeDiscoverCursorException("Discover.CursorInvalid", "The search cursor is invalid. Start the search again.");
        }
    }

    private static string EncodeCursor(string revision, string afterIdentity) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new DiscoverCursor(revision, afterIdentity), CursorJson));

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private static HomeDiscoverActionResult Failed(HomeDiscoverActionRequest request, string code, string message,
        bool retryable = false) => new(Guid.NewGuid().ToString("N"), request.Identity, request.Action,
        false, code, message, retryable, Recoverable: true);

    private sealed record DiscoverCursor(string Revision, string AfterIdentity);
}

public sealed class HomeDiscoverCursorException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
