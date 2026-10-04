using System.Globalization;

namespace Dulche.Runtime.Translate;

/// <summary>Resolves language codes and names without silently selecting among locale ambiguities.</summary>
public sealed class CanonicalLanguageResolver : ILanguageResolver
{
    private static readonly IReadOnlyList<TranslationLanguage> Cultures = CultureInfo
        .GetCultures(CultureTypes.AllCultures)
        .Where(culture => !string.IsNullOrWhiteSpace(culture.Name))
        .GroupBy(culture => culture.Name, StringComparer.OrdinalIgnoreCase)
        .Select(group => ToLanguage(group.First()))
        .OrderBy(language => language.Code, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public LanguageResolution Resolve(string? languageLabel, bool allowAutoDetect = false)
    {
        if (string.IsNullOrWhiteSpace(languageLabel)) return Invalid(languageLabel);
        var input = languageLabel.Trim();
        if (input.Equals("auto", StringComparison.OrdinalIgnoreCase)
            || input.Equals("auto-detect", StringComparison.OrdinalIgnoreCase)
            || input.Equals("auto detect", StringComparison.OrdinalIgnoreCase))
        {
            return allowAutoDetect
                ? new(LanguageResolutionState.Resolved, new("auto", "Auto Detect"), [], null)
                : Invalid(input);
        }

        var normalizedCode = input.Replace('_', '-');
        try
        {
            var culture = CultureInfo.GetCultureInfo(normalizedCode);
            if (!string.IsNullOrWhiteSpace(culture.Name))
            {
                var language = ToLanguage(culture);
                return new(LanguageResolutionState.Resolved, language, [], null);
            }
        }
        catch (CultureNotFoundException) { }

        var matches = Cultures.Where(culture =>
                culture.DisplayName.Equals(input, StringComparison.OrdinalIgnoreCase)
                || culture.Code.Equals(input, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(culture => culture.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (matches.Length == 1)
            return new(LanguageResolutionState.Resolved, matches[0], [], null);
        if (matches.Length > 1)
            return new(LanguageResolutionState.Ambiguous, null, matches,
                new(DulcheErrorCode.LanguageAmbiguous, $"'{input}' can refer to more than one language or locale. Choose a canonical language/locale code.", input, false,
                    Details: new Dictionary<string, string> { ["candidates"] = string.Join(',', matches.Select(match => match.Code)) }));

        return new(LanguageResolutionState.Unsupported, null, [],
            new(DulcheErrorCode.LanguageUnsupported, $"'{input}' is not a supported language or locale identifier.", input, false));
    }

    private static TranslationLanguage ToLanguage(CultureInfo culture)
    {
        var displayName = culture.IsNeutralCulture ? culture.EnglishName : $"{culture.EnglishName} ({culture.Name})";
        return new(culture.Name, displayName);
    }

    private static LanguageResolution Invalid(string? value) => new(
        LanguageResolutionState.Invalid,
        null,
        [],
        new(DulcheErrorCode.InvalidArgument, "A language or locale is required.", value ?? "language", false));
}

public sealed class UnknownTranslationCapabilityCatalog : ITranslationCapabilityCatalog
{
    public TranslationCapabilityState GetState(string providerId, string sourceLanguage, string targetLanguage, TranslationMediaKind? modality = null)
        => TranslationCapabilityState.Unknown;
}
