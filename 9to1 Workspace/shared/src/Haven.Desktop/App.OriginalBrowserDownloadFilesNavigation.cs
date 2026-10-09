#if !ANDROID
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop;

public sealed partial class App
{
    private OriginalBrowserDownloadFilesNavigator? _actualBrowserDownloadFilesNavigator;
    private FilesNativeBrowserService? _actualBrowserDownloadFilesBrowser;

    private void RetainOriginalBrowserDownloadFilesNavigator(MainView actualShell, FilesNativeBrowserService actualBrowser)
    {
        if (!ReferenceEquals(_actualSameProcessNativeShell, actualShell) ||
            !ReferenceEquals(_actualOriginalNativeDevelopmentFiles, actualBrowser) ||
            _actualSameProcessNativeWindow is not { } window || !ReferenceEquals(window.DataContext, actualShell) ||
            _actualBrowserDownloadFilesNavigator is not null || actualShell.IsDisposed)
            throw new UnauthorizedAccessException("Retain the actual native Home shell and SAME configured Files owner.");
        // A pure borrowed navigation adapter; this opens no store, process or view.
        _actualBrowserDownloadFilesBrowser = actualBrowser;
        _actualBrowserDownloadFilesNavigator = new(actualShell, actualBrowser);
    }

    private IFilesOriginalBrowserDownloadNavigator GetOriginalBrowserDownloadFilesNavigator(FilesNativeBrowserService sameBrowser)
    {
        if (!ReferenceEquals(_actualBrowserDownloadFilesBrowser, sameBrowser) ||
            _actualSameProcessNativeShell is not { IsDisposed: false } shell ||
            _actualSameProcessNativeWindow is not { } window || !ReferenceEquals(window.DataContext, shell))
            throw new UnauthorizedAccessException("The genuine native Files shell is not yet available or has retired.");
        return _actualBrowserDownloadFilesNavigator
            ?? throw new UnauthorizedAccessException("The original canonical Files navigation adapter is unavailable.");
    }
}
#endif
