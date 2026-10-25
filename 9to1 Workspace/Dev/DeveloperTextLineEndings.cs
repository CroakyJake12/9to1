using System.Text;

namespace HavenOS.Apps.Dev;

public enum DeveloperTextLineEndingTarget { Lf, CrLf }
public sealed record DeveloperTextLineEndingInfo(int CrLfCount, int LfCount, int CrCount, bool EndsInNewline)
{
    public int TotalBreaks => CrLfCount + LfCount + CrCount;
    public bool IsMixed => (CrLfCount > 0 ? 1 : 0) + (LfCount > 0 ? 1 : 0) + (CrCount > 0 ? 1 : 0) > 1;
}
public sealed record DeveloperTextLineEndingChange(string Text, int ChangedBreaks,
    DeveloperTextLineEndingInfo Before, DeveloperTextLineEndingInfo After);

/// <summary>Pure CR/LF inspection/conversion over the complete authorised draft. Every other
/// UTF-16 unit is preserved. This observes text conventions, never original byte encoding.</summary>
public static class DeveloperTextLineEndings
{
    public const int MaximumCharacters = DeveloperSourceReviewSession.MaximumTextCharacters;

    public static DeveloperOperationResult<DeveloperTextLineEndingInfo> Inspect(string text, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(text); token.ThrowIfCancellationRequested();
        if (text.Length > MaximumCharacters || text.IndexOf('\0') >= 0)
            return Invalid<DeveloperTextLineEndingInfo>("Line-ending controls support complete text drafts up to 2,000,000 characters.");
        var crlf = 0; var lf = 0; var cr = 0; var start = 0;
        while (start < text.Length)
        {
            token.ThrowIfCancellationRequested();
            var next = text.AsSpan(start).IndexOfAny('\r', '\n');
            if (next < 0) break;
            var index = start + next;
            if (text[index] == '\n') { lf++; start = index + 1; }
            else if (index + 1 < text.Length && text[index + 1] == '\n') { crlf++; start = index + 2; }
            else { cr++; start = index + 1; }
        }
        token.ThrowIfCancellationRequested();
        return DeveloperOperationResult<DeveloperTextLineEndingInfo>.Success(new(crlf, lf, cr,
            text.Length != 0 && text[^1] is '\r' or '\n'));
    }

    public static DeveloperOperationResult<DeveloperTextLineEndingChange> Normalize(string text, DeveloperTextLineEndingTarget target,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(text); token.ThrowIfCancellationRequested();
        if (target is not (DeveloperTextLineEndingTarget.Lf or DeveloperTextLineEndingTarget.CrLf))
            return Invalid<DeveloperTextLineEndingChange>("Choose LF or CRLF line endings.");
        var inspected = Inspect(text, token);
        if (!inspected.Succeeded || inspected.Value is null)
            return new(inspected.RequestId, null, inspected.Error);
        var before = inspected.Value;
        var changed = target == DeveloperTextLineEndingTarget.Lf ? before.CrLfCount + before.CrCount : before.LfCount + before.CrCount;
        if (changed == 0) return DeveloperOperationResult<DeveloperTextLineEndingChange>.Success(new(text, 0, before, before));
        var length = target == DeveloperTextLineEndingTarget.Lf ? (long)text.Length - before.CrLfCount
            : (long)text.Length + before.LfCount + before.CrCount;
        if (length > MaximumCharacters)
            return Invalid<DeveloperTextLineEndingChange>("The converted draft would exceed 2,000,000 characters. The draft is unchanged.");
        var newline = target == DeveloperTextLineEndingTarget.Lf ? "\n" : "\r\n";
        var output = new StringBuilder((int)length); var start = 0;
        while (start < text.Length)
        {
            token.ThrowIfCancellationRequested();
            var next = text.AsSpan(start).IndexOfAny('\r', '\n');
            if (next < 0) { output.Append(text.AsSpan(start)); break; }
            var index = start + next;
            output.Append(text.AsSpan(start, next)); output.Append(newline);
            start = index + (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1);
        }
        token.ThrowIfCancellationRequested();
        DeveloperTextLineEndingInfo after = target == DeveloperTextLineEndingTarget.Lf ? new(0, before.TotalBreaks, 0, before.EndsInNewline)
            : new(before.TotalBreaks, 0, 0, before.EndsInNewline);
        return DeveloperOperationResult<DeveloperTextLineEndingChange>.Success(new(output.ToString(), changed, before, after));
    }

    private static DeveloperOperationResult<T> Invalid<T>(string message) =>
        DeveloperOperationResult<T>.Failure(DeveloperOperationErrorCode.InvalidInput, message, "draft-line-endings");
}
