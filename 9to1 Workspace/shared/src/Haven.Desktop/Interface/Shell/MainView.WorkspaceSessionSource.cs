using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView : IWorkspaceSessionSource
{
    bool IWorkspaceSessionSource.IsDisposed => IsDisposed;
    IReadOnlyList<TabSessionSnapshot> IWorkspaceSessionSource.CreateTabSnapshots() => CreateTabSnapshots();
    IReadOnlyList<TabGroupSnapshot> IWorkspaceSessionSource.CreateGroupSnapshots() => CreateGroupSnapshots();
    IReadOnlyList<WorkspaceWindowSnapshot> IWorkspaceSessionSource.CreateWindowSnapshots(WorkspaceWindowKind kind) =>
        [CreateWindowSnapshot(kind)];
}
