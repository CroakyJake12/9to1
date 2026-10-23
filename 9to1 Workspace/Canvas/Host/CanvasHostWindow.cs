using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Canvas;

/// <summary>Thin OS window adapter around the SAME Canvas content used in Haven.
/// Document state, authored CUI navigation, engine and durable operations belong to OriginalSurface.</summary>
public sealed class CanvasHostWindow : Window
{
    private bool _closeAccepted;
    public CanvasHostSurface OriginalSurface { get; }
    public CanvasHostWindow(IServiceProvider services) : this(services, default, null) { }
    internal CanvasHostWindow(IServiceProvider services, CancellationToken originalProcess,
        Func<NativeFilesWorkspace, HomeResourceExecutionCapability, Func<bool>, CancellationToken, ValueTask<HomeClaimedResourceCommitFence>>? captureHomeFence)
        : this(new CanvasHostSurface(services, originalProcess, captureHomeFence)) { }
    internal CanvasHostWindow(CanvasHostSurface surface)
    {
        OriginalSurface = surface ?? throw new ArgumentNullException(nameof(surface));
        Title = "Canvas"; Width = 1200; Height = 800; MinWidth = 800; MinHeight = 560;
        Content = surface;
        Opened += (_, _) => { _ = surface.ObserveStatusOriginalAsync(surface.InitializeAsync()); };
        Activated += (_, _) => { _ = surface.ObserveStatusOriginalAsync(surface.RefreshOnActivationAsync()); };
        Closing += (_, args) =>
        {
            if (_closeAccepted) return;
            args.Cancel = true; _ = CloseFromWindowAsync();
        };
    }
    public Task Initialization => OriginalSurface.Initialization;
    public CanvasOriginalFilesCommit? OriginalFilesAcknowledgement => OriginalSurface.OriginalFilesAcknowledgement;
    public Task? OriginalCloseTask => OriginalSurface.OriginalCloseTask;
    public Task CloseAndDrainAsync() => OriginalSurface.CloseAndDrainAsync();
    internal Task? LatestOriginalCommand => OriginalSurface.LatestOriginalCommand;
    internal int RetainedReadProofCount => OriginalSurface.RetainedReadProofCount;
    internal Exception? RecoveredNoEffectCloseFailure => OriginalSurface.RecoveredNoEffectCloseFailure;
    internal IReadOnlyList<Exception>? ObserveOwnedFailureCauses(Exception wrapper) => OriginalSurface.ObserveOwnedFailureCauses(wrapper);
    internal bool CanCompleteKnownNoEffectClose(Task actual, Exception observed) => OriginalSurface.CanCompleteKnownNoEffectClose(actual, observed);
    internal static Task InitializeOwnedSurfaceAsync(CanvasNativeCuiSurface surface, CancellationToken token)
        => CanvasHostSurface.InitializeOwnedSurfaceAsync(surface, token);
    private async Task CloseFromWindowAsync()
    {
        var actual = CloseAndDrainAsync();
        try { await actual; }
        catch (Exception error)
        {
            if (!OriginalSurface.RetainKnownNoEffectWindowClose(actual, error))
            { OriginalSurface.PresentCloseFailure((Exception?)actual.Exception ?? error); return; }
        }
        await Dispatcher.UIThread.InvokeAsync(() => { _closeAccepted = true; Close(); });
    }
}
