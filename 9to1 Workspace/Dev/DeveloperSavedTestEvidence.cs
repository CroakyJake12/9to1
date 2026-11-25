using System.Globalization;
using System.Text;

namespace HavenOS.Apps.Dev;

/// <summary>A saved report is evidence from its reported run, never the current command's inferred status.</summary>
public sealed record DeveloperSavedTestEvidence(string Summary, string Details)
{
    public static DeveloperSavedTestEvidence FromReport(DeveloperTrxTestReport report)
    {
        const int limit = 200;
        var head = $"Saved TRX artifact · Run {report.RunId:D} · Provider result: {OneLine(report.ReportedRunOutcome, 128)}";
        var groups = report.Tests.GroupBy(row => row.RawOutcome, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
        var counts = groups.Take(16).Select(group => OneLine(group.Key, 128) + ": " +
            group.Count().ToString(CultureInfo.InvariantCulture)).ToList();
        if (groups.Length > 16)
            counts.Add("Other reported outcomes: " + groups.Skip(16).Sum(group => group.Count()).ToString(CultureInfo.InvariantCulture));
        var summary = head + ". " + (report.Tests.Count == 0 ? "No individual test outcomes were recorded."
            : string.Join(" · ", counts) + ".") + " This is not a verification of the current command or source revision.";
        // Stable partition: keep the provider's order within each group, but don't
        // let a long list of passing tests hide a recorded failure behind the cap.
        var ordered = report.Tests.Where(row => row.IsFailure).Concat(report.Tests.Where(row => !row.IsFailure));
        var details = ordered.Take(limit).Select(Describe).ToArray();
        return new(summary, string.Join(Environment.NewLine, details) +
            (report.Tests.Count > limit ? Environment.NewLine +
                $"Showing the first {limit} of {report.Tests.Count} recorded tests, with reported failures first." : ""));
    }

    private static string Describe(DeveloperRecordedTest row)
    {
        var reported = OneLine(row.RawOutcome, 128) + "  " + OneLine(row.Name, 240);
        if (!row.IsFailure || string.IsNullOrWhiteSpace(row.Message)) return reported;
        // Only the first recorded diagnostic line, never a stack trace, process
        // log, attachment or an inferred explanation.
        var message = row.Message.AsSpan().TrimStart();
        var newline = message.IndexOfAny('\r', '\n');
        var line = newline < 0 ? message : message[..newline];
        return reported + " · " + OneLine(line.TrimEnd(), 360);
    }

    private static string OneLine(ReadOnlySpan<char> value, int limit)
    {
        var count = Math.Min(value.Length, limit);
        if (count < value.Length && char.IsHighSurrogate(value[count - 1]) && char.IsLowSurrogate(value[count])) count--;
        var result = new StringBuilder(count + 1);
        foreach (var character in value[..count])
            result.Append(char.IsControl(character) ? ' ' : character);
        if (count < value.Length) result.Append('…');
        return result.ToString();
    }
}
