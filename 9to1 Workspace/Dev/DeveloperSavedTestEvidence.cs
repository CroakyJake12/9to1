using System.Globalization;

namespace HavenOS.Apps.Dev;

/// <summary>A saved report is evidence from its reported run, never the current command's inferred status.</summary>
public sealed record DeveloperSavedTestEvidence(string Summary, string Details)
{
    public static DeveloperSavedTestEvidence FromReport(DeveloperTrxTestReport report)
    {
        const int limit = 200;
        var head = $"Saved TRX artifact · Run {report.RunId:D} · Provider result: {report.ReportedRunOutcome}";
        var groups = report.Tests.GroupBy(row => row.RawOutcome, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
        var counts = groups.Take(16).Select(group => OneLine(group.Key) + ": " +
            group.Count().ToString(CultureInfo.InvariantCulture)).ToList();
        if (groups.Length > 16)
            counts.Add("Other reported outcomes: " + groups.Skip(16).Sum(group => group.Count()).ToString(CultureInfo.InvariantCulture));
        var summary = head + ". " + (report.Tests.Count == 0 ? "No individual test outcomes were recorded."
            : string.Join(" · ", counts) + ".") + " This is not a verification of the current command or source revision.";
        var details = report.Tests.Take(limit).Select(row => OneLine(row.RawOutcome) + "  " +
            OneLine(row.Name)).ToArray();
        return new(summary, string.Join(Environment.NewLine, details) +
            (report.Tests.Count > limit ? Environment.NewLine + $"Showing the first {limit} of {report.Tests.Count} recorded tests." : ""));
    }

    private static string OneLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}
