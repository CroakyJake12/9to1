using HavenOS.Apps.Browse;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Browser;

public sealed partial class BrowserPage
{
    private BrowseOwnedToolsScene? _ownedTools;
    internal bool IsOwnedToolsClosed => _disposed;
    internal void MountOwnedTools(BrowseOwnedToolsScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (_disposed || _ownedTools is not null)
        {
            scene.Dispose();
            throw new InvalidOperationException("The original browser tools mount is closed or already bound.");
        }
        _ownedTools = scene;
        // Preserve the native web surface in row 3; tools have their own bounded region.
        _havenScene.Root.Rows = "Auto Auto Auto 1fr Auto 240px";
        scene.Root.SetValue(HavenProperties.Row, 5);
        scene.Root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        scene.Root.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        _havenScene.Root.Add(scene.Root);
    }
    internal Task WhenOwnedToolActionsIdleAsync() => _ownedTools?.WhenActionsIdleAsync() ?? Task.CompletedTask;
}
