using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Browse;

/// <summary>Pure observation of the SAME live native completed transfer. The
/// content/row values themselves never grant Files READ/WRITE; the real Files
/// composition independently validates its issued owner and current Home scope.</summary>
public interface IBrowseOriginalDownloadContentSource
{
    IBrowserOriginalDownloadContent? ObserveOriginalDownloadContent(BrowserDownloadRecord currentCanonicalRow);
}
