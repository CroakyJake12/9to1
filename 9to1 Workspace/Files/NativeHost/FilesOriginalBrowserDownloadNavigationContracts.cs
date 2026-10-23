using Haven.Application;
using HavenOS.Files;

namespace HavenOS.Files.NativeHost;

// The actual desktop owner mounts the canonical Files surface using the SAME
// privately issued page and exact current metadata row. These are observations,
// never a path, launch grant or executable request.
public interface IFilesOriginalBrowserDownloadNavigator
{
    Task RevealOriginalRegisteredDownloadWithinSourceAsync(FilesNativeBrowserService sameBrowser,
        FilesNativeBrowserPage originalPage, HostedItemMetadata originalRow,
        AuthenticatedResourceActor originalActor, Action<Action> scope,
        Action<Task> retain, CancellationToken cancellationToken);
}

// A borrowed route to the SAME canonical Home review owner. An original request
// identifier is display selection only, never consent, a capability or a new
// Files operation. The issuer revalidates the actual content/destination first.
public interface IFilesOriginalBrowserDownloadReviewPresenter
{
    Task OpenOriginalRegistrationReviewWithinSourceAsync(FilesNativeBrowserService sameBrowser,
        FilesNativeBrowserService.BrowserDownloadFilesOwner sameOwner,
        IBrowserOriginalDownloadFilesRegistration originalRegistration,
        AuthenticatedResourceActor originalActor, string originalRequestId,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken);
}
