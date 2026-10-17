using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>A presentation's contribution to the existing canonical workspace
/// session. Capture is synchronous on the UI thread and confers no authority to
/// reopen an app or resource. The coordinator owns all repository writes.</summary>
internal interface IWorkspaceSessionSource
{
    bool IsDisposed { get; }
    IReadOnlyList<TabSessionSnapshot> CreateTabSnapshots();
    IReadOnlyList<TabGroupSnapshot> CreateGroupSnapshots();
    IReadOnlyList<WorkspaceWindowSnapshot> CreateWindowSnapshots(WorkspaceWindowKind kind);
}
