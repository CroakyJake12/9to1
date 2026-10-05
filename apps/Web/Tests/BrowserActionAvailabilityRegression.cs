using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using NineToOne.Web;

var checks = 0;
void Check(string name, bool condition)
{
    if (!condition) throw new InvalidOperationException($"FAIL {name}");
    ++checks; Console.WriteLine($"PASS {name}");
}
await using var session = HeadlessUnitTestSession.StartNew(typeof(AvailabilityApplication));
await session.Dispatch(() =>
{
    var model = new CuiViewModel();
    model.Set("Enabled", false); model.SetActionAvailability("Unavailable", false);
    var document = new CuiRichParser().Parse("""
        <Cui><StackPanel>
          <Button id="live" action="Unavailable" IsEnabled="{Binding Enabled}" content="Unavailable native action" />
          <Button id="unbound" action="Unavailable" content="Owner default enabled" />
          <Button id="literal-disabled" action="Unavailable" IsEnabled="false" content="Owner disabled" />
        </StackPanel></Cui>
        """);
    using var loader = new CuiControlLoader(); loader.SetBindingContext(model); loader.SetActionDispatcher(model);
    var (root, diagnostics) = loader.TryLoad(document);
    if (root is not Panel panel || diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
        throw new InvalidOperationException("Real owner CUI did not lower.");
    loader.WireBindings(panel);
    var buttons = panel.GetLogicalDescendants().OfType<Button>().ToDictionary(button => button.Name!);
    var live = buttons["live"]; var unbound = buttons["unbound"]; var literal = buttons["literal-disabled"];
    using var observation = BrowserActionAvailability.Observe(panel, loader, document, model, model);
    var revision = 0;
    void Availability(bool available)
    {
        model.SetActionAvailability("Unavailable", available);
        model.Set("CapabilityRevision", ++revision); // Actual existing context notification.
    }
    Check("Initial unavailable capability narrows owner enabled state", !live.IsEnabled && !unbound.IsEnabled && !literal.IsEnabled);
    Check("Actual native peer explains unavailable capability", ControlAutomationPeer.CreatePeerForElement(live).GetHelpText()?.Contains("available account service") == true);
    model.Set("Enabled", true);
    // Same genuine native binding refresh that failed in the preserved red fixture.
    Check("Unavailable capability remains disabled after owning binding refresh", !live.IsEnabled);
    Availability(true);
    Check("Capability availability restores owner-bound true", live.IsEnabled);
    Check("Available native peer removes obsolete unavailable explanation", string.IsNullOrEmpty(ControlAutomationPeer.CreatePeerForElement(live).GetHelpText()));
    Check("Capability availability restores unbound owner default true", unbound.IsEnabled);
    Check("Capability availability preserves authored literal false", !literal.IsEnabled);
    Availability(false);
    Check("Capability revocation disables live and unbound native buttons", !live.IsEnabled && !unbound.IsEnabled);
    model.Set("Enabled", false); // Native false->false emits no native property change.
    Availability(true);
    Check("Equal native false binding assignment preserves latest owner false", !live.IsEnabled && unbound.IsEnabled);
    model.Set("Enabled", true);
    Check("Actual notified owner true enables only available action", live.IsEnabled);
    Availability(false);
    unbound.IsEnabled = true;
    Check("Later actual native enabled assignment is synchronously capability-clamped", !unbound.IsEnabled);
    Availability(true);
    Check("Captured native authored true survives capability clamp", unbound.IsEnabled);
    unbound.IsEnabled = false;
    model.Set("CapabilityRevision", ++revision);
    Check("Context refresh preserves latest actual native authored false", !unbound.IsEnabled);
    unbound.IsEnabled = true;
    Availability(false);
    unbound.IsEnabled = false; // Equal effective false still writes the real owner value below restriction.
    Availability(true);
    Check("Equal false unbound owner assignment survives capability restoration", !unbound.IsEnabled);
    unbound.IsEnabled = true;
    Check("Unbound owner true remains editable after capability restoration", unbound.IsEnabled);
    Availability(false);
    using (unbound.SetValue(Control.IsEnabledProperty, true, BindingPriority.Animation))
        Check("Later native priority assignment cannot enable unavailable action", !unbound.IsEnabled);
    Availability(true);
    unbound.IsEnabled = false;
    model.SetActionAvailability("Unavailable", false);
    observation.Refresh();
    Check("Explicit surface refresh rereads silent actual capability change", !live.IsEnabled);
    model.SetActionAvailability("Unavailable", true);
    observation.Refresh();
    Check("Explicit surface refresh restores latest owning binding", live.IsEnabled && !unbound.IsEnabled);
    panel.Children.Remove(live);
    observation.Refresh();
    model.SetActionAvailability("Unavailable", false);
    model.Set("CapabilityRevision", ++revision);
    Check("Removed native controls are no longer owned by surface observer", live.IsEnabled);
    live.IsEnabled = false; live.IsEnabled = true;
    Check("Removed native controls unsubscribe native property guard", live.IsEnabled);
    panel.Children.Add(live);
    observation.Refresh();
    Check("Reattached actual control gets current surface capability guard", !live.IsEnabled);
    observation.Dispose();
    Check("Disposal removes active restriction and restores actual owner true", live.IsEnabled);
    Check("Disposal preserves actual authored literal false", !literal.IsEnabled);
    Availability(false);
    Check("Disposed observation ignores future context notifications", live.IsEnabled);
    live.IsEnabled = false; live.IsEnabled = true;
    Check("Disposed observation removes native property guard", live.IsEnabled);
    observation.Refresh();
    Check("Disposed explicit refresh is inert", live.IsEnabled);
}, CancellationToken.None);
Console.WriteLine($"{checks}/{checks} actual native availability checks PASS.");

public sealed class AvailabilityApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<AvailabilityApplication>().UseSkia())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Home");
}
