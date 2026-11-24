using System.Globalization;

namespace HavenOS.Apps.Dev;

/// <summary>Projects retained preview evidence without conflating it with the current draft's result.</summary>
public static class DeveloperPreviewEvidence
{
    public static string Describe(string attemptStatus, string? lastValidPath, long lastValidRevision,
        bool lastValidIsMounted, bool matchesCurrentDraftAndAttempt)
    {
        if (lastValidPath is null) return attemptStatus;
        var source = lastValidPath + " · draft revision " + lastValidRevision.ToString(CultureInfo.InvariantCulture);
        if (!lastValidIsMounted)
            return attemptStatus + " Last valid preview is not currently visible (" + source + "). Preview this draft again.";
        return matchesCurrentDraftAndAttempt
            ? attemptStatus + " Showing " + source + "."
            : attemptStatus + " Last valid (stale): " + source + ". Not a preview of the current draft.";
    }
}
