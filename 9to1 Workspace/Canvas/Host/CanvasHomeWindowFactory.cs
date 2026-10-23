using Haven.Application;
using CakeOS.Cui.Themes;
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
        // The same Home composition owns an explicit Linux process bootstrap as well as
        // the Windows producer. This borrowed-window port issues no installed-app grant.
        if (!(OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) || !_process.CanBeCanceled ||
            !ReferenceEquals(_home.Services.GetService(typeof(HomeCoreRuntime)), _home.Runtime) ||
            !ReferenceEquals(_home.Services.GetService(typeof(IAuthenticatedResourceActorSource)), _home.Profiles) ||
            !ReferenceEquals(_home.Services.GetService(typeof(HomeResourceOperationBroker)), _home.Broker) ||
            !ReferenceEquals(_home.Services.GetService(typeof(NativeFilesWorkspaceService)), _workspaces) ||
            !ReferenceEquals(_home.Services.GetService(typeof(NativeFilesWorkspaceAuthority)), _authority) ||
            !_authority.IsBoundToOriginalComposition(_workspaces, _home.Profiles, _home.Ownership))
            throw new UnauthorizedAccessException("Canvas requires the SAME supplied native Home/Files process composition and lifetime.");
    }
    public CanvasHostWindow CreateOriginalWindow() => new(CreateSurface(_process, null, null));

    /// <summary>Borrow the SAME canonical content inside an existing Home-owned native host.
    /// The callback is its finite original-source scope, not authority to create Home or grants.</summary>
    public CanvasHostSurface CreateOriginalSurface(CancellationToken originalPresentationLifetime,
        Action<Action> runWithinOriginalHostSource, CuiThemePreferenceContext? themePreference = null)
    {
        ArgumentNullException.ThrowIfNull(runWithinOriginalHostSource);
        originalPresentationLifetime.ThrowIfCancellationRequested();
        if (!originalPresentationLifetime.CanBeCanceled)
            throw new UnauthorizedAccessException("Retain the actual Canvas presentation lifetime.");
        return WithinSource(runWithinOriginalHostSource,
            () => CreateSurface(originalPresentationLifetime, runWithinOriginalHostSource, themePreference));
    }
    private CanvasHostSurface CreateSurface(CancellationToken presentation, Action<Action>? source,
        CuiThemePreferenceContext? theme)
    {
        DemandOriginal();
        // Keep the authentic native-engine admission: no fixture, preview or managed substitute.
        using (var native = RnoteCanvasEngine.Create()) { }
        return new CanvasHostSurface(_home.Services, _process, (original, capability, alive, token) =>
        {
            DemandOriginal();
            return _authority.CaptureOriginalCanvasCommitFenceAsync(original, _home.Broker, capability,
                () => !_process.IsCancellationRequested && alive(), token);
        }, source, theme, presentation);
    }
    private static T WithinSource<T>(Action<Action> source, Func<T> actual)
    {
        T result = default!; var invoked = false;
        source(() =>
        {
            if (invoked) throw new InvalidOperationException("A Canvas host source callback may run only once.");
            invoked = true; result = actual();
        });
        if (!invoked) throw new InvalidOperationException("The Canvas host did not invoke its original source callback.");
        return result;
    }
}
