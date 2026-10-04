using Avalonia.Threading;
using Haven.Core;
using Haven.Desktop.Services;
using HavenOS.Files.NativeUI;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private NativeFilesDesktopRoute? _originalFilesRoute;
    private Task? _originalFilesClose;
    private FilesNativeBrowserSurface? _originalFilesSurface;
    private Action? _originalFilesPublicationCheck;

    // Called only by the native owning window composition, never a serialized app route or DTO.
    internal void AttachOriginalFilesRoute(NativeFilesDesktopRoute originalRoute,
        IServiceProvider originalHomeProvider)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(originalRoute);
        if (!originalRoute.IsOriginalProvider(originalHomeProvider) || _originalFilesRoute is not null)
            throw new UnauthorizedAccessException("The original native Files route is unavailable.");
        _originalFilesRoute = originalRoute;
    }

    internal async Task OpenFilesAsync(bool openInNewTab = false, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_originalFilesPublicationCheck is not null)
            throw new InvalidOperationException("The original Files tab publication is already active.");
        if (openInNewTab)
            throw new NotSupportedException("A second Files window requires a separate original native owner scope.");
        var original = _originalFilesRoute
            ?? throw new UnauthorizedAccessException("Files requires the genuine native Home startup scope.");
        var surface = await original.OpenAsync(cancellationToken);
        await original.RevalidateBeforePublicationAsync(surface, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!ReferenceEquals(_originalFilesRoute, original))
            throw new UnauthorizedAccessException("The original Files owner changed before tab publication.");
        original.CheckPublicationCurrent(surface);
        // A single retained Control belongs to one canonical tab in this window.
        var retainedTabs = OpenTabs.Where(tab => tab.Key.Equals("files-native", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (retainedTabs.Length > 1 || retainedTabs.Any(tab => !ReferenceEquals(tab.Page, surface)))
            throw new UnauthorizedAccessException("Retain the SAME original Files tab and surface.");
        _originalFilesSurface = surface;
        _originalFilesPublicationCheck = () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (!ReferenceEquals(_originalFilesRoute, original) || !ReferenceEquals(_originalFilesSurface, surface))
                throw new UnauthorizedAccessException("The original Files tab publication retired.");
            original.CheckPublicationCurrent(surface);
        };
        try
        {
            original.PublishOriginalTabMutation(surface, () =>
            {
                CheckPendingOriginalFilesPublication();
                AddOrSelectTab("files-native", "Files", surface, false, HavenSurface.Files, forceNewTab: true);
                CheckOriginalFilesWindowPublicationCurrent();
            });
        }
        finally { _originalFilesPublicationCheck = null; }
    }

    internal async Task RevalidateOriginalFilesWindowPublicationAsync(CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        var original = _originalFilesRoute
            ?? throw new UnauthorizedAccessException("The original Files route is unavailable.");
        var surface = _originalFilesSurface
            ?? throw new UnauthorizedAccessException("The original Files view is unavailable.");
        await original.RevalidateBeforePublicationAsync(surface, cancellationToken);
        CheckOriginalFilesWindowPublicationCurrent();
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal void CheckOriginalFilesWindowPublicationCurrent()
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var original = _originalFilesRoute
            ?? throw new UnauthorizedAccessException("The original Files route is unavailable.");
        var surface = _originalFilesSurface
            ?? throw new UnauthorizedAccessException("The original Files view is unavailable.");
        var tab = SelectedTab;
        if (CurrentSurface != HavenSurface.Files || tab is null || tab.Key != "files-native"
            || !ReferenceEquals(tab.Page, surface))
            throw new UnauthorizedAccessException("Retain the exact Files page for native window publication.");
        original.CheckPublicationCurrent(surface);
    }

    // Active only during this SAME Files publication. Other native routes have no added callback.
    private void CheckPendingOriginalFilesPublication() => _originalFilesPublicationCheck?.Invoke();

    internal Task BeginOriginalFilesClose()
        => _originalFilesClose ??= _originalFilesRoute?.CloseAndDrainAsync() ?? Task.CompletedTask;

    internal Task CloseOriginalFilesAndDrainAsync() => BeginOriginalFilesClose();
}
