using System.ComponentModel;
using Avalonia.Headless.XUnit;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeUiTests
{
    [AvaloniaFact]
    public async Task Command_context_refuses_active_self_join_but_completed_context_allows_external_retirement()
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths());
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        ExecutionContext? captured = null;
        var activeRefusals = 0;
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            workspace.PropertyChanged += InspectCommand;
            await Click(window, workspace, Find(window, "BrowseNewTabButton"));
            workspace.PropertyChanged -= InspectCommand;
            Assert.True(activeRefusals > 0); Assert.NotNull(captured);
            // The dispatcher can retain a captured command ExecutionContext
            // after its source task completed. That is no longer a self-join.
            Task? close = null;
            ExecutionContext.Run(captured!, _ => { window.Close(); close = window.OriginalClose; }, null);
            Assert.NotNull(close); await Observe(close!, window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
        void InspectCommand(object? sender, PropertyChangedEventArgs args)
        {
            // This notification comes from the command's finally block, before
            // its original task becomes terminal and after its work is applied.
            if (workspace.OriginalCommand is { IsCompleted: false } && chrome.State.Tabs.Count == 2 &&
                workspace.IsActionAvailable("9to1.Browse.NewTab") == true)
            {
                captured = ExecutionContext.Capture();
                Assert.Throws<InvalidOperationException>(() => workspace.DisposeAsync());
                Assert.Null(workspace.OriginalClose); activeRefusals++;
            }
        }
    }
}
