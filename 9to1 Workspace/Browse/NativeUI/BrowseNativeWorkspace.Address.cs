using Haven.Core;
using Haven.Browser;

namespace HavenOS.Apps.Browse;

public sealed partial class BrowseNativeWorkspace
{
    private enum OmniboxMode { Home, Files, Web }
    private OmniboxMode _omniboxMode = OmniboxMode.Web;
    private bool CanSearchLocal => _hostContext?.IsEmbedded == true && _snapshot.SelectedTab.Privacy == BrowserTabPrivacy.Standard;
    private string OmniboxModeLabel => _omniboxMode switch { OmniboxMode.Home => "9to1", OmniboxMode.Files => "Files", _ => "Web" };

    private Task<BrowseChromeSnapshot> NavigateOmniboxAsync(CancellationToken token)
    {
        var text = _address.Trim();
        // Explicit routes retain their existing exact admission policy, including
        // refusing malformed privileged routes rather than sending them to search.
        if (_omniboxMode == OmniboxMode.Web || AppContentAddress.IsReserved(text) ||
            text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            return _chrome.NavigateAsync(text, token);
        if (!CanSearchLocal) throw new InvalidOperationException("9to1 and Files search require the current standard Home profile. Select Web for this tab.");
        return _chrome.OpenContentAsync(BrowseOmniboxSearchRoute.Create(text, _omniboxMode == OmniboxMode.Files),
            openInNewTab: false, cancellationToken: token);
    }

    private void ApplyOmniboxLocation(BrowseTabSnapshot tab)
    {
        if (BrowseOmniboxSearchRoute.TryParse(tab.Address, out var text, out var filesOnly))
        { _address = text; _omniboxMode = filesOnly ? OmniboxMode.Files : OmniboxMode.Home; }
        else
        {
            _address = tab.Address.ToString();
            if (tab.Privacy != BrowserTabPrivacy.Standard || _hostContext?.IsEmbedded != true)
                _omniboxMode = OmniboxMode.Web;
        }
    }
}
