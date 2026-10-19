/*
 * FILE DOCUMENTATION
 * Where: Browse/Host.Tests/SpacesSidebarContributionRouteTests.cs, the real Haven host route test lane.
 * What: Proves an extension-contributed Space sidebar element resolves from the Space's actually
 *       attached extensions and opens its target through the host's existing typed navigation contract.
 * Why: Specification 4763-4866 requires the main Space sidebar to be customisable through stable
 *       section/item identities and for an element to open its declared target. The projection and the
 *       activation must be real, not a placeholder row.
 * Maintenance: Uses the real SpaceRegistry, a real ExtensionManager over a controlled extension
 *       repository, and the real Spaces session. No product surface is stubbed to obtain a pass.
 */

using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Spaces;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Spaces.Home;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

public sealed class SpacesSidebarContributionRouteTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(20);

    private static InstalledExtensionPackage DeclaredPackage(params ExtensionSidebarContribution[] contributions)
    {
        var manifest = new ExtensionPackageManifest("sidebar.demo", "/extensions/sidebar.demo", "Sidebar Demo",
            ExtensionPackageType.Plugin, "1.0.0", ">=1.0.0", "Declares Space sidebar elements", "9to1", "9to1",
            null, null, ExtensionPermission.None, [], [], [], null)
        { SidebarContributions = contributions };
        return new InstalledExtensionPackage(Guid.NewGuid(), Guid.NewGuid(), manifest, "/extensions/sidebar.demo",
            ExtensionPermission.None, ExtensionInstallState.Installed, true, false, "sha256:demo",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    }

    /// <summary>An element declared by an extension attached to the selected Space opens its target
    /// through the host's existing typed navigation intent, on the same tab.</summary>
    [AvaloniaFact]
    public async Task An_attached_extensions_sidebar_element_opens_through_the_host_navigation_contract()
    {
        var package = DeclaredPackage(new ExtensionSidebarContribution("tool-panel", "Tool panel", "tool",
            ExtensionSidebarTargetKind.Page, "https://example.com/tool"));
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, allowConversationWrites: true,
            extensionPackages: [package]);
        var space = await rig.Spaces.CreateAsync("Extension Space");
        await rig.Spaces.UpdateAsync(space with
        {
            CapabilityBindings = [new SpaceCapabilityBinding("sidebar.demo", SpaceCapabilityKind.Plugin,
                SpaceCapabilityOverride.Inherit)]
        }, CancellationToken.None);

        var tabId = Guid.NewGuid();
        await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
        var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(actor);
        var intents = new List<BrowseNavigationIntent>();
        var session = rig.Session("9to1://spaces", tabId: tabId, profileId: actor.ProfileId,
            queueNavigation: (intent, _) => { intents.Add(intent); return true; });
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);
            await spaces.Controller.SelectSpaceAsync(space.Id);
            await WaitForProjectionAsync(spaces, "spaces.sidebarItems", items => Count(items) == 1);

            await spaces.DispatchAsync("spaces.open-sidebar-item", "tool-panel").AsTask().WaitAsync(Bound);

            var intent = Assert.Single(intents);
            Assert.Equal(tabId, intent.SourceTabId);
            Assert.Equal("https://example.com/tool", intent.Address.Value);
            Assert.False(intent.OpenInNewTab);
        }
        finally { try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch { } }
    }

    /// <summary>Per-Space visibility is the attachment itself: a Space that has not attached the
    /// extension shows nothing even though the package is installed, and disabling the binding hides the
    /// element without dropping the attachment.</summary>
    [AvaloniaFact]
    public async Task Sidebar_elements_follow_the_selected_Space_attachment_and_override()
    {
        var package = DeclaredPackage(new ExtensionSidebarContribution("tool-panel", "Tool panel", "tool",
            ExtensionSidebarTargetKind.Page, "https://example.com/tool"));
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, extensionPackages: [package]);        var attached = await rig.Spaces.CreateAsync("Attached Space");
        var unattached = await rig.Spaces.CreateAsync("Unattached Space");
        await rig.Spaces.UpdateAsync(attached with
        {
            CapabilityBindings = [new SpaceCapabilityBinding("sidebar.demo", SpaceCapabilityKind.Plugin,
                SpaceCapabilityOverride.Disabled)]
        }, CancellationToken.None);

        await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
        var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(actor);
        var intents = new List<BrowseNavigationIntent>();
        var session = rig.Session("9to1://spaces", profileId: actor.ProfileId,
            queueNavigation: (intent, _) => { intents.Add(intent); return true; });
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);

            await spaces.Controller.SelectSpaceAsync(unattached.Id);
            await WaitForProjectionAsync(spaces, "spaces.sidebarItems", items => Count(items) == 0);

            await spaces.Controller.SelectSpaceAsync(attached.Id);
            await WaitForProjectionAsync(spaces, "spaces.sidebarItems", items => Count(items) == 0);

            // The disabled binding must refuse rather than open, and must not reach the host at all.
            await spaces.DispatchAsync("spaces.open-sidebar-item", "tool-panel").AsTask().WaitAsync(Bound);
            Assert.Empty(intents);
            Assert.Contains("no longer attached", spaces.Controller.State.StatusMessage, StringComparison.Ordinal);
        }
        finally { try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch { } }
    }

    private static int Count(object? value) => value is System.Collections.IEnumerable items
        ? items.Cast<object>().Count() : 0;

    private static async Task WaitForProjectionAsync(SpacesHomePage page, string path, Predicate<object?> ready)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (page.TryGetValue(path, out var value) && ready(value)) return;
            await Task.Delay(50);
        }
        Assert.Fail($"The authored {path} projection never became ready.");
    }
}
