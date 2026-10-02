using NineToOne.Launcher;

namespace Haven.Android;

/// <summary>Process-local activity-result identity only; the retained private Home snapshot supplies read/edit authority.</summary>
public sealed class AndroidLauncherLayoutDocumentSelections
{
    public const int FirstRequestCode = 12000;
    public const int LastRequestCode = 65000;
    private static int nextRequest = FirstRequestCode - 1;
    private Selection? pending, latest;
    public sealed class Selection
    {
        internal Selection(int code, LauncherSessionSnapshot original, bool export)
        { RequestCode = code; Original = original; Export = export; }
        public int RequestCode { get; }
        public LauncherSessionSnapshot Original { get; }
        public bool Export { get; }
    }
    public Selection Issue(LauncherSessionSnapshot original, bool export)
    {
        ArgumentNullException.ThrowIfNull(original);
        var code = Interlocked.Increment(ref nextRequest);
        if (code is < FirstRequestCode or > LastRequestCode) throw new InvalidOperationException("Launcher document request capacity was reached. Restart the app before selecting another document.");
        var selection = new Selection(code, original, export);
        pending = latest = selection; return selection;
    }
    public Selection? Take(int requestCode)
    {
        if (pending is not { } selection || selection.RequestCode != requestCode) return null;
        pending = null; return selection;
    }
    public bool IsCurrent(Selection selection) => ReferenceEquals(latest, selection);
    public void Cancel() { pending = null; latest = null; }
    public static bool IsDocumentRequest(int requestCode) => requestCode is >= FirstRequestCode and <= LastRequestCode;
}
