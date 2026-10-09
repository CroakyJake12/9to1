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
