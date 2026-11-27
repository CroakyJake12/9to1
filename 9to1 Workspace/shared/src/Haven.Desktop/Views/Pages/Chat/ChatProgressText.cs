namespace Haven.Desktop.Views.Pages.Chat;

internal static class ChatProgressText
{
    // Provider reasoning remains owned by the response stream. Public progress is
    // an observed elapsed label; actual tool receipts have their own disclosure.
    public static string Format(bool isStreaming, long elapsedSeconds, string? detail = null)
    {
        var elapsed = Math.Max(0, elapsedSeconds);
        var duration = elapsed == 1 ? "1 second" : $"{elapsed} seconds";
        var label = isStreaming
            ? elapsed == 0 ? "Working…" : $"Working for {duration}"
            : $"Worked for {duration}";
        return label;
    }
}
