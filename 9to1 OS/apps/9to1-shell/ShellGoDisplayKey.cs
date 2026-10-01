using System.Globalization;
using Haven.Application.Go;

namespace NineToOne.Os.Shell;

/// <summary>Presentation identity only. Provider and canonical owner identity remain authoritative elsewhere.</summary>
public static class ShellGoDisplayKey
{
    public static string Create(GoResult result)
    {
        ArgumentNullException.ThrowIfNull(result); ArgumentNullException.ThrowIfNull(result.Reference);
        var fields = new[] { result.ProviderId, result.Reference.Owner, result.Reference.Kind, result.Reference.Id };
        if (fields.Any(field => string.IsNullOrWhiteSpace(field) || field.Length > 4096))
            throw new ArgumentException("A canonical Go display key needs bounded provider and owner identity.", nameof(result));
        // UTF-16 length prefixes preserve exact field boundaries, including unusual identifiers.
        // No Unicode normalization/replacement; revision updates retain the canonical row.
        return string.Concat(fields.Select(field => field.Length.ToString(CultureInfo.InvariantCulture) + ":" + field));
    }
}
