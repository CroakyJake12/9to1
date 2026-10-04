using System.Security.Cryptography;
using System.Text;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Accepts already-structured learning results from authorised 9to1 surfaces. The service applies
/// the user's consent and contributor policy before writing locally; it never stores raw activity.
/// </summary>
public sealed class BackgroundLearningCaptureService(
    IBackgroundLearningScheduler scheduler,
    IPrivacyPreferenceStore privacy,
    IKnowledgeLibrary knowledge) : IBackgroundLearningCaptureService
{
    public async Task<KnowledgeRecord> CaptureAsync(
        BackgroundLearningContribution contribution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        if (string.IsNullOrWhiteSpace(contribution.Topic)) throw new ArgumentException("A knowledge topic is required.", nameof(contribution));
        if (string.IsNullOrWhiteSpace(contribution.Title)) throw new ArgumentException("A knowledge title is required.", nameof(contribution));
        if (string.IsNullOrWhiteSpace(contribution.LearnedValue)) throw new ArgumentException("A structured learned value is required.", nameof(contribution));
        if (string.IsNullOrWhiteSpace(contribution.Scope)) throw new ArgumentException("A knowledge scope is required.", nameof(contribution));
        if (string.IsNullOrWhiteSpace(contribution.LearnedBecause)) throw new ArgumentException("Learning provenance is required.", nameof(contribution));
        if (contribution.Sources is null) throw new ArgumentException("Knowledge source metadata cannot be null.", nameof(contribution));
        if (contribution.Confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(contribution));
        if (!Enum.IsDefined(contribution.Category) || !Enum.IsDefined(contribution.Freshness))
            throw new ArgumentOutOfRangeException(nameof(contribution));
        if (contribution.Category is KnowledgeCategory.LearnMe or KnowledgeCategory.ApiBank)
            throw new InvalidOperationException("Persistent Memory and API Bank are not Background Learning categories.");
        if (contribution.PrivacyClass == KnowledgePrivacyClass.NeverLearn)
            throw new InvalidOperationException("Never Learn data cannot enter Background Learning.");

        var protectedValues = new List<string?>
        {
            contribution.Topic, contribution.Title, contribution.LearnedValue, contribution.IndexedText,
            contribution.LearnedBecause, contribution.Scope
        };
        foreach (var source in contribution.Sources)
        {
            if (source is null) throw new ArgumentException("Knowledge source metadata cannot contain null entries.", nameof(contribution));
            protectedValues.AddRange([source.Title, source.SourceId, source.Url, source.Publisher]);
        }
        KnowledgeContentSafety.ThrowIfContainsSecret(protectedValues.ToArray());

        var prefs = privacy.Current;
        if (!prefs.BackgroundLearningEnabled ||
            prefs.BackgroundLearningPolicy?.Allows(contribution.AppId, contribution.ProjectId) != true ||
            !await scheduler.CanAcceptContributionAsync(
                contribution.Category, contribution.AppId, contribution.ProjectId, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Background Learning consent or contributor policy does not allow this contribution.");

        var bankId = (Guid?)null;
        if (contribution.CreateOrUseKnowledgeBank)
        {
            if (contribution.Sources.Count == 0)
                throw new InvalidOperationException("Knowledge Bank reference entries require source provenance.");
            var now = DateTimeOffset.UtcNow;
            var bank = await knowledge.CreateBankAsync(
                new KnowledgeBank(
                    StableId("bank", contribution.Topic, contribution.Scope),
                    contribution.Topic.Trim(),
                    string.IsNullOrWhiteSpace(contribution.KnowledgeBankTitle)
                        ? contribution.Topic.Trim()
                        : contribution.KnowledgeBankTitle.Trim(),
                    contribution.Scope.Trim(),
                    true,
                    "local",
                    "disabled",
                    now,
                    now),
                cancellationToken).ConfigureAwait(false);
            if (!bank.IsEnabled)
                throw new InvalidOperationException("This Knowledge Bank is disabled for new learning.");
            bankId = bank.Id;
        }

        var timestamp = DateTimeOffset.UtcNow;
        var record = new KnowledgeRecord(
            StableId(
                "entry",
                ((int)contribution.Category).ToString(System.Globalization.CultureInfo.InvariantCulture),
                contribution.Scope,
                contribution.AppId ?? string.Empty,
                contribution.ProjectId ?? string.Empty,
                contribution.AgentId ?? string.Empty,
                contribution.Topic,
                contribution.Title,
                contribution.LearnedValue),
            contribution.Category,
            contribution.Topic.Trim(),
            contribution.Title.Trim(),
            contribution.LearnedValue.Trim(),
            contribution.PrivacyClass,
            contribution.Confidence,
            false,
            timestamp,
            timestamp,
            contribution.ExpiresAt,
            contribution.LearnedBecause,
            contribution.Sources,
            contribution.Freshness,
            timestamp,
            contribution.Scope.Trim(),
            KnowledgeRecordStatus.Active,
            KnowledgeOrigin.Inferred,
            KnowledgeBankId: bankId,
            LastReinforcedAt: timestamp,
            AppId: contribution.AppId,
            ProjectId: contribution.ProjectId,
            AgentId: contribution.AgentId);

        var indexedText = string.IsNullOrWhiteSpace(contribution.IndexedText)
            ? contribution.LearnedValue
            : contribution.IndexedText;
        return await knowledge.UpsertAsync(record, indexedText, cancellationToken).ConfigureAwait(false);
    }

    private static Guid StableId(string purpose, params string[] values)
    {
        var canonical = purpose + "\0" + string.Join("\0", values.Select(static value => value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant()));
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return new Guid(digest.AsSpan(0, 16));
    }
}
