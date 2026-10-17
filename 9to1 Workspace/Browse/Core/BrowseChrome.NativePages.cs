using Haven.Browser;
using Haven.Core;

namespace HavenOS.Apps.Browse;

public sealed partial class BrowseChrome
{
    /// <summary>Opens an actual built-in CUI page in a canonical tab, retaining
    /// existing page renderers and their original download sources in other tabs.</summary>
    public async Task<BrowseChromeSnapshot> OpenNativePageAsync(BrowserNativePage page, CancellationToken token = default)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        var address = AppContentAddress.Parse(BrowserNativePages.Address(page).AbsoluteUri);
        var privacy = SelectedRuntime().Privacy;
        var existing = _tabs.FirstOrDefault(tab => tab.NativePage == page && tab.Privacy == privacy);
        var tab = existing ?? await AddRuntimeAsync(Guid.NewGuid(), BrowserNativePages.Title(page), address,
            privacy, string.Empty, token).ConfigureAwait(false);
        _selectedTabId = tab.Id;
        await SaveTabsAsync(token).ConfigureAwait(false);
        _status = tab.Status;
        return Publish();
    }

    private async Task<BrowseChromeSnapshot> NavigateNativePageAsync(BrowserNativePage page, CancellationToken token)
    {
        var tab = SelectedRuntime();
        tab.NavigationCancellation?.Cancel();
        ++tab.NavigationGeneration;
        if (!await PrepareContentCloseAsync(tab, token).ConfigureAwait(false)) return Publish();
        await ReleaseContentAsync(tab).ConfigureAwait(false);
        ApplyNativePage(tab, page);
        await SaveTabsAsync(token).ConfigureAwait(false);
        _status = tab.Status;
        return Publish();
    }

    private static void ApplyNativePage(TabRuntime tab, BrowserNativePage page)
    {
        tab.NativePage = page;
        tab.Address = AppContentAddress.Parse(BrowserNativePages.Address(page).AbsoluteUri);
        tab.Title = BrowserNativePages.Title(page);
        tab.IconKey = "browse";
        tab.EngineState = BrowseEngineState.NativePage;
        tab.CanGoBack = false; tab.CanGoForward = false; tab.IsLoading = false;
        tab.FindQuery = string.Empty; tab.ZoomPercent = 100; tab.HasOriginalCommittedPage = false;
        tab.Status = tab.Title + " · native Browse page";
    }

    private async Task LeaveNativePageAsync(TabRuntime tab, Uri address, BrowseEngineKind engine, CancellationToken token)
    {
        // Acquire the replacement before retiring the live app. A refused engine
        // factory leaves the original native view and pending work usable.
        IBrowseEngineTab? replacement = null;
        if (IsEngineAvailable(engine))
        {
            replacement = await CreateEngineHostAsync(tab, address, engine, token).ConfigureAwait(false);
            tab.PendingHosts.Add(replacement);
        }
        try
        {
            await ReleaseContentAsync(tab, preservePendingHosts: true).ConfigureAwait(false);
            tab.NativePage = BrowserNativePage.None; tab.Address = AppContentAddress.Parse(address.AbsoluteUri);
            tab.Title = address.Host; tab.IconKey = "web"; tab.Engine = engine;
            if (replacement is not null)
            { AttachAcquiredHost(tab, replacement); tab.PendingHosts.Remove(replacement); }
            else { tab.EngineState = BrowseEngineState.Unsupported; tab.Status = EngineUnavailableReason(engine); }
        }
        catch
        {
            if (tab.Host is not null) { tab.EngineState = BrowseEngineState.Crashed; tab.Status = "The tab engine could not finish starting."; }
            throw;
        }
    }
}
