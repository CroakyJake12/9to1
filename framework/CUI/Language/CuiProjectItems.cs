namespace CakeOS.Cui.Language;

public enum CuiProjectItemKind
{
    CuiMarkup,
    RejectedLegacyMarkup,
    Other
}

public static class CuiProjectItems
{
    public const string Extension = ".cui";
    public const string MsBuildItemName = "Cui";

    public static CuiProjectItemKind Recognize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            Extension => CuiProjectItemKind.CuiMarkup,
            ".axaml" or ".hui" => CuiProjectItemKind.RejectedLegacyMarkup,
            _ => CuiProjectItemKind.Other
        };
    }

    public static bool IsCui(string path) => Recognize(path) == CuiProjectItemKind.CuiMarkup;

    public static void RequireCui(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!IsCui(path))
            throw new NotSupportedException(
                $"CUI accepts only {Extension} project items. Legacy .axaml and .hui inputs are rejected: '{path}'.");
    }
}
