using Dulche.Runtime;
using Haven.Core;
using NineToOne.Dulche.Den;

namespace HavenOS.Home;

public enum HomeModelProviderAvailability
{
    Unknown,
    Available,
    Unavailable,
}

/// <summary>Live provider facts are kept separate from canonical catalogue declarations.</summary>
public sealed record HomeModelProviderFacts(
    HomeModelProviderAvailability Availability,
    bool? IsLocal,
    IReadOnlyList<string>? Capabilities,
    int? ContextWindow,
    int? MaximumOutputTokens,
    ModelState? LifecycleState);

/// <summary>
/// Immutable presentation projection of a canonical Dulche model record. It keeps stable
/// identity and declared metadata separate from transient provider availability.
/// </summary>
public sealed record HomeModelCatalogueEntry(
    string ModelId,
    ModelIdentity? Identity,
    string DisplayName,
    IReadOnlyList<string> Aliases,
    string? ProviderId,
    string? Source,
    string? Family,
    string? Format,
    string? Quantisation,
    IReadOnlyList<string> Modalities,
    IReadOnlyList<string> DeclaredCapabilities,
    long? ContextLimit,
    long? OutputLimit,
    IReadOnlyList<string> ReasoningLevels,
    PortableMetric Parameters,
    PortableMetric ActiveParameters,
    PortableMetric StorageBytes,
    PortableMetric HardwareMemoryBytes,
    HomeModelProviderFacts ProviderFacts,
    string? ProviderName = null,
    string? PrivacyResidency = null,
    string? LifecycleLabel = null)
{
    public static HomeModelCatalogueEntry From(HomeModelPickerCatalogueEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.ProviderId) || string.IsNullOrWhiteSpace(entry.ModelId))
            throw new ArgumentException("A catalogue provider and stable model ID are required.", nameof(entry));

        return new HomeModelCatalogueEntry(
            entry.ModelId,
            new ModelIdentity(entry.ProviderId, entry.ModelId, entry.ArtifactRevision),
            entry.DisplayName,
            string.IsNullOrWhiteSpace(entry.Alias) ? Array.Empty<string>() : [entry.Alias],
            entry.ProviderId,
            null,
            null,
            null,
            null,
            Array.Empty<string>(),
            Array.AsReadOnly(entry.Capabilities?.Order(StringComparer.OrdinalIgnoreCase).ToArray() ?? []),
            entry.ContextWindow,
            null,
            Array.Empty<string>(),
            PortableMetric.Empty(),
            PortableMetric.Empty(),
            PortableMetric.Empty(),
            PortableMetric.Empty(),
            new HomeModelProviderFacts(
                HomeModelProviderAvailability.Unknown,
                entry.IsLocal,
                entry.Capabilities?.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                entry.ContextWindow,
                null,
                null),
            entry.ProviderName,
            entry.PrivacyResidency,
            entry.LifecycleState);
    }

    public static HomeModelCatalogueEntry From(ModelRecord model, HomeModelProviderFacts? providerFacts = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(model.Id))
            throw new ArgumentException("A canonical Dulche ModelID is required.", nameof(model));

        var providerId = string.IsNullOrWhiteSpace(model.Provider) ? null : model.Provider;
        var identity = providerId is null
            ? null
            : new ModelIdentity(providerId, model.Id, model.ArtifactRevision);

        return new HomeModelCatalogueEntry(
            model.Id,
            identity,
            model.DisplayName,
            Copy(model.Aliases),
            providerId,
            model.Source,
            model.Family,
            model.Format,
            model.Quantisation,
            Copy(model.Modalities),
            Copy(model.Capabilities),
            model.ContextLimit,
            model.OutputLimit,
            Copy(model.ReasoningLevels),
            model.Parameters,
            model.ActiveMoeParameters,
            model.StorageBytes,
            model.HardwareMemoryBytes,
            providerFacts ?? new HomeModelProviderFacts(
                HomeModelProviderAvailability.Unknown,
                null,
                null,
                null,
                null,
                null));
    }

    public bool MatchesQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var term = query.Trim();
        return Contains(DisplayName, term)
            || Contains(ModelId, term)
            || Contains(ProviderId, term)
            || Contains(Source, term)
            || Contains(Family, term)
            || Contains(Format, term)
            || Contains(Quantisation, term)
            || Aliases.Any(alias => Contains(alias, term))
            || Modalities.Any(value => Contains(value, term))
            || DeclaredCapabilities.Any(value => Contains(value, term))
            || ReasoningLevels.Any(value => Contains(value, term));
    }

    public bool HasDeclaredCapability(string capability) =>
        !string.IsNullOrWhiteSpace(capability)
        && DeclaredCapabilities.Any(value => value.Equals(capability, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> Copy(IReadOnlyList<string>? values) =>
        Array.AsReadOnly(values?.ToArray() ?? []);

    private static bool Contains(string? value, string term) =>
        value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
}
