using Haven.Application;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Canvas;

/// <summary>Borrow an owning window inside the SAME genuine Home process. No standalone app,
/// installed identity, IPC authorization, profile, Home store or Files provider is fabricated.</summary>
public sealed class CanvasHomeWindowFactory
{
    private readonly HomeNativeWindowsComposition _home;
    private readonly NativeFilesWorkspaceService _workspaces;
    private readonly NativeFilesWorkspaceAuthority _authority;
    private readonly CancellationToken _process;
    public CanvasHomeWindowFactory(HomeNativeWindowsComposition sameHome,
        NativeFilesWorkspaceService sameFilesWorkspaces, NativeFilesWorkspaceAuthority sameFilesAuthority,
        CancellationToken originalHomeProcessLifetime)
    {
        _home = sameHome ?? throw new ArgumentNullException(nameof(sameHome));
        _workspaces = sameFilesWorkspaces ?? throw new ArgumentNullException(nameof(sameFilesWorkspaces));
        _authority = sameFilesAuthority ?? throw new ArgumentNullException(nameof(sameFilesAuthority));
        _process = originalHomeProcessLifetime;
        DemandOriginal();
    }
    private void DemandOriginal()
    {
        _process.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || !_process.CanBeCanceled ||
            !ReferenceEquals(_home.Services.GetService(typeof(HomeCoreRuntime)), _home.Runtime) ||
            !ReferenceEquals(_home.Services.GetService(typeof(IAuthenticatedResourceActorSource)), _home.Profiles) ||
            !ReferenceEquals(_home.Services.GetService(typeof(HomeResourceOperationBroker)), _home.Broker) ||
            !ReferenceEquals(_home.Services.GetService(typeof(NativeFilesWorkspaceService)), _workspaces) ||
            !ReferenceEquals(_home.Services.GetService(typeof(NativeFilesWorkspaceAuthority)), _authority) ||
            !_authority.IsBoundToOriginalComposition(_workspaces, _home.Profiles, _home.Ownership))
            throw new UnauthorizedAccessException("Canvas requires the SAME supplied native Home/Files process composition and lifetime.");
    }
    public CanvasHostWindow CreateOriginalWindow()
    {
        DemandOriginal();
        using (var native = RnoteCanvasEngine.Create()) { }
        return new CanvasHostWindow(_home.Services, _process, (original, capability, alive, token) =>
        {
            DemandOriginal();
            return _authority.CaptureOriginalCanvasCommitFenceAsync(original, _home.Broker, capability,
                () => !_process.IsCancellationRequested && alive(), token);
        });
    }
}
