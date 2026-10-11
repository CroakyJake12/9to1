namespace CakeOS.Cui.Language;

public enum CuiProjectItemKind
{
    CuiMarkup,
    ThemeMarkup,
    PrefabMarkup,
    RejectedLegacyMarkup,
    Other
}

public static class CuiProjectItems
{
    public const string Extension = ".cui";
    public const string ThemeExtension = ".cui-theme";
    public const string PrefabExtension = ".cui-prefab";
    public const string MsBuildItemName = "Cui";

    public static CuiProjectItemKind Recognize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            Extension => CuiProjectItemKind.CuiMarkup,
            ThemeExtension => CuiProjectItemKind.ThemeMarkup,
            PrefabExtension => CuiProjectItemKind.PrefabMarkup,
            ".axaml" or ".hui" => CuiProjectItemKind.RejectedLegacyMarkup,
            _ => CuiProjectItemKind.Other
        };
    }

    public static bool IsCui(string path) => Recognize(path) == CuiProjectItemKind.CuiMarkup;

    public static bool IsCuiSourceFamily(string path) =>
        Recognize(path) is CuiProjectItemKind.CuiMarkup or CuiProjectItemKind.ThemeMarkup or CuiProjectItemKind.PrefabMarkup;

    public static void RequireCui(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!IsCuiSourceFamily(path))
            throw new NotSupportedException(
                $"CUI accepts only {Extension}, {ThemeExtension} and {PrefabExtension} project items. Legacy .axaml and .hui inputs are rejected: '{path}'.");
    }
}
