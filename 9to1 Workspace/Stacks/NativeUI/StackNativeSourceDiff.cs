using System.Text;
using Haven.Infrastructure;

namespace HavenOS.Apps.Stacks.NativeUI;

/// <summary>A source view over the same domain resources. Binary/unknown text is never decoded as a substitute.</summary>
public sealed record StackNativeSourceDiff(string? BeforeText, string? CurrentText,
    IReadOnlyList<SourceTextDiffLine> Lines, bool IsText, string Description)
{
    public const int MaximumSourceBytes = 2 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static StackNativeSourceDiff Read(StackResource? before, StackResource? current)
    {
        if (before?.IsBinary == true || current?.IsBinary == true)
            return new(null, null, [], false, "Binary file. Text editing and line diffs are unavailable.");
        if (before?.Content.Length > MaximumSourceBytes || current?.Content.Length > MaximumSourceBytes)
            return new(null, null, [], false, "This file exceeds the text editing limit.");
        try
        {
            var left = before is null ? null : Utf8.GetString(before.Content);
            var right = current is null ? null : Utf8.GetString(current.Content);
            if (left?.Contains('\0') == true || right?.Contains('\0') == true)
                return new(null, null, [], false, "This file contains binary data. Its bytes are preserved.");
            return new(left, right, SharedSourceTextDiff.Compare(left, right), true,
                before is null ? "Added file" : current is null ? "Deleted file" : "Source compared with this domain's inherited base");
        }
        catch (ArgumentOutOfRangeException)
        { return new(null, null, [], false, "This file exceeds the line diff limit. Its bytes are preserved."); }
        catch (DecoderFallbackException)
        { return new(null, null, [], false, "The source is not UTF-8 text. Its bytes are preserved."); }
    }
    internal static bool CanEncode(string text)
    {
        try { return Utf8.GetByteCount(text) <= MaximumSourceBytes; }
        catch (EncoderFallbackException) { return false; }
    }
    internal static byte[] Encode(string text)
    {
        var bytes = Utf8.GetBytes(text);
        if (bytes.Length > MaximumSourceBytes) throw new ArgumentOutOfRangeException(nameof(text), "Text exceeds the editing limit.");
        return bytes;
    }
}
