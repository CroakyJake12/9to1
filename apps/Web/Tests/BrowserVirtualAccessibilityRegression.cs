using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Automation.Peers;
using CakeOS.Cui.Runtime;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using NineToOne.Web;

await using var session = HeadlessUnitTestSession.StartNew(typeof(VirtualAccessibilityApplication));
await session.Dispatch(() =>
{
    var checks = 0;
    void Check(string name, bool condition)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine($"PASS: {name}"); ++checks;
    }
    var group = new Haven.UI.Components.Container();
    var detached = new Haven.UI.Components.Input { Name = "detachable", Text = "preserved" };
    var hidden = new Haven.UI.Components.Input { Name = "hideable", Text = "preserved" };
    group.Add(detached); group.Add(hidden);
    var scene = new HavenSceneControl(new HavenAvaloniaImageResolver(), new HavenAvaloniaNativeControlResolver(), () => true)
        { Root = group, Platform = HavenPlatform.Unknown };
    var window = new Window { Content = new Border { Padding = new Thickness(13, 17, 11, 9), Child = scene }, Width = 400, Height = 300 };
    window.Show();
    try
    {
        window.UpdateLayout();
        var nativeNames = ControlAutomationPeer.CreatePeerForElement(scene).GetChildren()
            .ToDictionary(peer => peer.GetAutomationId()!, peer => peer.GetName());
        Avalonia.Automation.AutomationProperties.SetName(scene, "Parent control label only");
        var bridge = new BrowserAccessibilityBridge(); bridge.Bind(scene);
        using var snapshot = JsonDocument.Parse(bridge.ReadSnapshot());
        string Id(string name) => snapshot.RootElement.GetProperty("elements").EnumerateArray()
            .Single(element => element.GetProperty("automationId").GetString() == name).GetProperty("id").GetString()!;
        var detachedId = Id("detachable"); var hiddenId = Id("hideable");
        Check("Authored shared-owner name cannot replace actual virtual child names", snapshot.RootElement.GetProperty("elements").EnumerateArray()
            .Where(element => nativeNames.ContainsKey(element.GetProperty("automationId").GetString()!))
            .All(element => element.GetProperty("name").GetString() == nativeNames[element.GetProperty("automationId").GetString()!]
                && element.GetProperty("name").GetString() != "Parent control label only"));
        var nativePeer = ControlAutomationPeer.CreatePeerForElement(scene).GetChildren()
            .Single(peer => peer.GetAutomationId() == "detachable");
        var bounds = nativePeer.GetBoundingRectangle();
        var projectedBounds = snapshot.RootElement.GetProperty("elements").EnumerateArray()
            .Single(element => element.GetProperty("id").GetString() == detachedId).GetProperty("bounds");
        Check("Virtual peer geometry comes from actual translated top-level bounds", projectedBounds.GetProperty("x").GetDouble() == bounds.X
            && projectedBounds.GetProperty("y").GetDouble() == bounds.Y && projectedBounds.GetProperty("width").GetDouble() == bounds.Width
            && projectedBounds.GetProperty("height").GetDouble() == bounds.Height && bounds.X >= 13 && bounds.Y >= 17);
        Check("Actual Haven virtual input provider is available while attached", bridge.Perform(detachedId, "value", "updated") && detached.Text == "updated");
        group.Remove(detached);
        // No caller-driven snapshot refresh occurs between detach/hide and use.
        Check("Detached virtual child cannot mutate through shared visible Control owner", !bridge.Perform(detachedId, "value", "late") && detached.Text == "updated");
        hidden.SetValue(HavenProperties.Visibility, HavenVisibility.Hidden);
        Check("Hidden virtual child cannot mutate through shared visible Control owner", !bridge.Perform(hiddenId, "value", "late") && hidden.Text == "preserved");
        bridge.Clear();
        Console.WriteLine($"{checks} actual retained-owner virtual-peer unit checks passed; not browser or Write editor acceptance.");
    }
    finally { window.Content = null; scene.Root = null; window.Close(); }
}, CancellationToken.None);

public sealed class VirtualAccessibilityApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<VirtualAccessibilityApplication>().UseSkia())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    public override void Initialize()
    {
        CuiNativeHost.InitialisePrimitiveTheme(this, "Home");
        NineToOne.Web.Write.WriteRetainedSceneResources.Register(this);
    }
}
