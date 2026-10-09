using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using HavenOS.Files;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    internal Task RevealOriginalRegisteredBrowserDownloadAsync(FilesNativeBrowserService sameBrowser,
        FilesNativeBrowserPage originalPage, HostedItemMetadata originalRow,
        AuthenticatedResourceActor originalActor, Action<Action> scope, Action<Task> retain,
        CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return _originalShellWork.RunAsync(async original =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, original.Token);
            var currentToken = linked.Token;
            var route = _originalFilesRoute ?? throw new UnauthorizedAccessException("Open Files through its genuine native Home route.");
            if (_originalFilesPublicationCheck is not null)
                throw new InvalidOperationException("The original Files tab publication is already active.");
            original.DemandPublication();
            var actual = AcquireOriginalShellSynchronous(original, () =>
                route.RevealOriginalRegisteredDownloadAsync(sameBrowser, originalPage,
                    originalRow, originalActor, scope, retain, currentToken));
            var surface = await original.AwaitAsync(actual);
            original.DemandPublication();
            currentToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_originalFilesRoute, route))
                throw new UnauthorizedAccessException("The native Files route changed during navigation.");
            var retainedTabs = OpenTabs.Where(tab => tab.Key.Equals("files-native", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (retainedTabs.Length > 1 || retainedTabs.Any(tab => !ReferenceEquals(tab.Page, surface)))
                throw new UnauthorizedAccessException("Retain the SAME canonical Files tab and surface.");
            _originalFilesSurface = surface;
            _originalFilesPublicationCheck = () =>
            {
                original.DemandPublication();
                currentToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                if (!ReferenceEquals(_originalFilesRoute, route) || !ReferenceEquals(_originalFilesSurface, surface))
                    throw new UnauthorizedAccessException("The original Files selection publication retired.");
                route.CheckPublicationCurrent(surface);
            };
            try
            {
                AcquireOriginalShellSynchronous(original, () =>
                {
                    scope(() => route.PublishOriginalTabMutation(surface, () =>
                    {
                        CheckPendingOriginalFilesPublication();
                        AddOrSelectTab("files-native", "Files", surface, false, HavenSurface.Files, forceNewTab: true);
                        CheckOriginalFilesWindowPublicationCurrent();
                    }));
                    return true;
                });
            }
            finally { _originalFilesPublicationCheck = null; }
        });
    }
}
