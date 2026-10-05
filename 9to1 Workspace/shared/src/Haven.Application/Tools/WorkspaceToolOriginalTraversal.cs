namespace Haven.Application;

/// <summary>Observed entries from the SAME physically owned original traversal, not access grants.</summary>
public sealed record WorkspaceOriginalFileEntry(string RelativePath, bool IsDirectory, long? ByteLength);
public sealed record WorkspaceOriginalFileMatch(string RelativePath, int LineNumber, string Line);

/// <summary>
/// Additive original-only port on IWorkspaceOriginalInvocation.Tools. A configured producer binds
/// arguments to its SAME captured call and retained physical root/child handles, and joins every
/// actual enumerator/read/cleanup original before outcome publication. Missing/unsupported owners
/// must refuse before traversal; canonical callers never fall back to Directory or legacy search.
/// </summary>
public interface IWorkspaceOriginalTraversalService
{
    Task<IReadOnlyList<WorkspaceOriginalFileEntry>> ListOriginalFilesAsync(
        string root, string path, int maxDepth, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkspaceOriginalFileMatch>> SearchOriginalFilesAsync(
        string root, string path, string query, int maxResults, CancellationToken cancellationToken);
}

/// <summary>A bounded scan ended before full observation. It cannot be reported as successful output.</summary>
public sealed class WorkspaceOriginalTraversalLimitException : InvalidOperationException
{
    public int ScanBudget { get; }
    public WorkspaceOriginalTraversalLimitException(int scanBudget)
        : base("The original traversal reached its configured scan budget before complete observation.")
    {
        if (scanBudget < 1) throw new ArgumentOutOfRangeException(nameof(scanBudget));
        ScanBudget = scanBudget;
    }
}
