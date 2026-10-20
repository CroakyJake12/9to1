using System.Reflection;
using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Haven.Browser;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

/// <summary>
/// Verifies the unfocused readable location contract: web addresses keep their exact canonical
/// text, the first-party location derives from genuinely admitted content-session metadata
/// (never a GUID route leak or an invented name), and the raw writable Address port is never
/// replaced by the friendly presentation value.
/// </summary>
public sealed class BrowseAddressDisplayTests
{
    [AvaloniaFact]
    public async Task AddressDisplay_keeps_canonical_web_address_and_raw_port()
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths());
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        workspace.ObserveReadiness(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "address-display", "Ready"));
        try
        {
            const string web = "https://example.com/path?q=1";
            await chrome.NavigateAsync(web, CancellationToken.None);
            Assert.True(workspace.TryGetValue("Address", out var address));
            Assert.Equal(web, address);
            // Unfocused web location is the exact canonical address, never a friendly rewrite.
            Assert.True(workspace.TryGetValue("AddressDisplay", out var display));
            Assert.Equal(web, display);

            // A draft edit still drives the raw writable port; presentation never corrupts it.
            Assert.True(workspace.TrySetValue("Address", "https://draft.example/"));
            Assert.True(workspace.TryGetValue("Address", out var edited));
            Assert.Equal("https://draft.example/", edited);
        }
        finally { await workspace.DisposeAsync().AsTask(); }
    }

    [AvaloniaFact]
    public void First_party_location_uses_admitted_title_without_guid_leak()
    {
        var workspace = CreateWorkspace();
        var webTab = Tab("https://example.com/a", "Example", "https://example.com/a");
        Assert.Equal("https://example.com/a", CallResolveAddressDisplay(workspace, webTab));

        // Admitted first-party content-session title is shown; the raw GUID route is never leaked.
        var spaceChat = Tab("9to1://spaces/selected/9a6b41a2-0000-0000-0000-000000000001/chat/00000000-0000-0000-0000-000000000002",
            "New conversation", "9to1://spaces");
        Assert.Equal("New conversation", CallResolveAddressDisplay(workspace, spaceChat));

        // A genuine Spaces session titled "Spaces" is shown (not rejected as a placeholder), and
        // never the GUID-carrying raw address.
        var genuineSpaces = Tab("9to1://spaces/selected/9a6b41a2-0000-0000-0000-000000000001/chat/00000000-0000-0000-0000-000000000002",
            "Spaces", "9to1://spaces");
        Assert.Equal("Spaces", CallResolveAddressDisplay(workspace, genuineSpaces));

        // No genuine admitted distinct title: fall back to the surface address, never a GUID leak.
        var noTitle = Tab("9to1://spaces/selected/9a6b41a2-0000-0000-0000-000000000001", "", "9to1://spaces");
        Assert.Equal("9to1://spaces", CallResolveAddressDisplay(workspace, noTitle));
    }

    private static BrowseNativeWorkspace CreateWorkspace()
    {
        // A lightweight workspace used only to host the presentation logic; the chrome fixture is
        // real but the snapshots are supplied directly to the deterministic resolver.
        var chrome = BrowseChrome.CreateAsync(new Paths()).GetAwaiter().GetResult();
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        workspace.ObserveReadiness(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "address-display", "Ready"));
        return workspace;
    }

    private static BrowseTabSnapshot Tab(string address, string title, string raw)
    {
        _ = raw;
        return new BrowseTabSnapshot(
            Guid.NewGuid(), title, AppContentAddress.Parse(address),
            BrowserTabPrivacy.Standard, BrowseEngineState.Ready, false, false, false, string.Empty);
    }

    private static string CallResolveAddressDisplay(BrowseNativeWorkspace workspace, BrowseTabSnapshot tab)
    {
        var method = typeof(BrowseNativeWorkspace)
            .GetMethod("ResolveAddressDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (string)method.Invoke(workspace, [tab])!;
    }

    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "browse-address-display-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
}
