using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Automation.Peers;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using NineToOne.Web;

await using var session = HeadlessUnitTestSession.StartNew(typeof(AccessibilityTestApplication));
await session.Dispatch(() =>
{
    var checks = 0;
    void Check(string name, bool condition)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine($"PASS: {name}");
        ++checks;
    }
    var model = new CuiViewModel();
    var invokes = 0;
    model.On("Run", _ => ++invokes);
    model.SetActionAvailability("Run", true);
    using var loader = new CuiControlLoader();
    loader.SetBindingContext(model);
    loader.SetActionDispatcher(model);
    var document = new CuiRichParser().Parse("""
        <Cui><Actions><Action name="Alias" command="Run" /></Actions><StackPanel><Button id="run" content="Native action" action="Run" />
        <TextBox id="edit" accessible-name="Native value" text="original" />
        <TextBlock text="Actual native text" />
        <Button id="authored-disabled" content="Unavailable owning state" action="Run" IsEnabled="false" />
        <Button id="alias" content="Aliased native action" action="Alias" />
        </StackPanel></Cui>
        """);
    var (root, diagnostics) = loader.TryLoad(document);
    if (root is not StackPanel stack || diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
        throw new InvalidOperationException("Real CUI fixture failed to lower.");
    loader.WireBindings(root);
    var button = (Button)stack.Children[0];
    var input = (TextBox)stack.Children[1];
    var authoredDisabled = (Button)stack.Children[3];
    var alias = (Button)stack.Children[4];
    BrowserActionAvailability.ApplyInitial(authoredDisabled, loader, document, model);
    BrowserActionAvailability.ApplyInitial(alias, loader, document, model);
    Check("Available action does not overwrite authored disabled state", !authoredDisabled.IsEnabled);
    Check("Actual typed alias command determines availability", alias.IsEnabled && (string?)alias.Tag == "Alias");
    stack.Children.Add(new CheckBox { Content = "Unsupported toggle" });
    stack.Children.Add(new TextBox { Text = "private-password", PasswordChar = '*' });
    using var namedLoader = new CuiControlLoader();
    var namedDocument = new CuiRichParser().Parse("""
        <Cui><StackPanel>
          <TextBlock id="authored-account" text="synthetic-account-value" accessible-name="Canonical account ID" />
          <TextBlock id="authored-session" text="synthetic-session-value" accessible-name="Canonical session ID" />
          <TextBlock id="authored-revision" text="7" accessible-name="Current profile revision" />
          <TextBlock id="blank-authored" text="Native text fallback" />
        </StackPanel></Cui>
        """);
    var (namedRoot, namedDiagnostics) = namedLoader.TryLoad(namedDocument);
    if (namedRoot is not StackPanel namedStack || namedDiagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
        throw new InvalidOperationException("Named native CUI fixture failed to lower.");
    Avalonia.Automation.AutomationProperties.SetName(namedStack.Children[3], " ");
    stack.Children.Add(namedRoot);
    var window = new Window { Content = root, Width = 400, Height = 300 };
    window.Show();
    try
    {
        using var frame = window.CaptureRenderedFrame();
        var bridge = new BrowserAccessibilityBridge();
        bridge.Bind(root);
        using var snapshot = JsonDocument.Parse(bridge.ReadSnapshot());
        var elements = snapshot.RootElement.GetProperty("elements").EnumerateArray().ToArray();
        var action = elements.Single(e => e.GetProperty("automationId").GetString() == "run");
        var edit = elements.Single(e => e.GetProperty("automationId").GetString() == "edit");
        var actionId = action.GetProperty("id").GetString()!;
        var editId = edit.GetProperty("id").GetString()!;
        bool MatchesBounds(JsonElement json, Rect bounds) => json.ValueKind == JsonValueKind.Object
            && json.GetProperty("x").GetDouble() == bounds.X && json.GetProperty("y").GetDouble() == bounds.Y
            && json.GetProperty("width").GetDouble() == bounds.Width && json.GetProperty("height").GetDouble() == bounds.Height;
        var viewport = snapshot.RootElement.GetProperty("viewport");
        Check("Viewport uses actual logical client size and native render scaling", viewport.GetProperty("width").GetDouble() == window.ClientSize.Width
            && viewport.GetProperty("height").GetDouble() == window.ClientSize.Height
            && viewport.GetProperty("renderScaling").GetDouble() == window.RenderScaling);
        Check("Supported bounds are actual native logical top-level geometry", MatchesBounds(action.GetProperty("bounds"),
            ControlAutomationPeer.CreatePeerForElement(button).GetBoundingRectangle()));
        var unsupportedToggle = snapshot.RootElement.GetProperty("unsupportedPeers").EnumerateArray()
            .Single(e => e.GetProperty("controlType").GetString() == "CheckBox");
        Check("Unsupported peer diagnostic retains native geometry without advertising actions or values",
            MatchesBounds(unsupportedToggle.GetProperty("bounds"), ControlAutomationPeer.CreatePeerForElement(stack.Children[5]).GetBoundingRectangle())
            && !unsupportedToggle.TryGetProperty("id", out _) && !unsupportedToggle.TryGetProperty("value", out _)
            && !unsupportedToggle.TryGetProperty("role", out _) && !unsupportedToggle.TryGetProperty("enabled", out _));
        Check("Names and roles come from real native automation peers", action.GetProperty("name").GetString() == "Native action" && action.GetProperty("role").GetString() == "button" && edit.GetProperty("name").GetString() == "Native value");
        Check("Actual native text is projected", elements.Any(e => e.GetProperty("name").GetString() == "Actual native text"));
        JsonElement Named(string id) => elements.Single(e => e.GetProperty("automationId").GetString() == id);
        Check("Actual CUI TextBlock authored account name overrides displayed private value", Named("authored-account").GetProperty("name").GetString() == "Canonical account ID"
            && ((TextBlock)namedStack.Children[0]).Text == "synthetic-account-value");
        Check("Actual CUI TextBlock authored session name is retained", Named("authored-session").GetProperty("name").GetString() == "Canonical session ID");
        Check("Actual CUI TextBlock authored revision name is retained", Named("authored-revision").GetProperty("name").GetString() == "Current profile revision");
        Check("Whitespace authored name falls back to actual native text peer", Named("blank-authored").GetProperty("name").GetString() == "Native text fallback");
        Check("Named text still exposes only native text semantics", Named("authored-account").GetProperty("role").GetString() == "text"
            && Named("authored-account").GetProperty("value").ValueKind == JsonValueKind.Null
            && !bridge.Perform(Named("authored-account").GetProperty("id").GetString()!, "invoke", null)
            && !bridge.Perform(Named("authored-account").GetProperty("id").GetString()!, "value", "not-written"));
        Check("Invoke calls actual button provider and wired CUI action once", bridge.Perform(actionId, "invoke", null) && invokes == 1);
        button.IsEnabled = false;
        Check("Changed native disabled state rejects previously enabled ID", !bridge.Perform(actionId, "invoke", null) && invokes == 1);
        using var disabled = JsonDocument.Parse(bridge.ReadSnapshot());
        Check("Snapshot reflects actual changed disabled state", !disabled.RootElement.GetProperty("elements").EnumerateArray().Single(e => e.GetProperty("id").GetString() == actionId).GetProperty("enabled").GetBoolean());
        Check("Value provider changes actual native TextBox", bridge.Perform(editId, "value", "updated") && input.Text == "updated");
        input.IsReadOnly = true;
        Check("Changed native readonly state rejects writes", !bridge.Perform(editId, "value", "rejected") && input.Text == "updated");
        Check("Focus uses actual native peer", bridge.Perform(editId, "focus", null) && input.IsFocused);
        using var focused = JsonDocument.Parse(bridge.ReadSnapshot());
        Check("Snapshot reports the actual current native focused peer", focused.RootElement.GetProperty("elements").EnumerateArray().Single(e => e.GetProperty("id").GetString() == editId).GetProperty("focused").GetBoolean());
        input.Focusable = false;
        Check("Changed native focusability rejects focus", !bridge.Perform(editId, "focus", null));
        Check("Unsupported provider roles are explicit", snapshot.RootElement.GetProperty("unsupported").EnumerateArray().Any(e => e.GetString() == "CheckBox"));
        Check("Password values are not projected", !bridge.ReadSnapshot().Contains("private-password", StringComparison.Ordinal));
        var aliasId = elements.Single(e => e.GetProperty("automationId").GetString() == "alias").GetProperty("id").GetString()!;
        Check("Alias invokes the actual loader-resolved command once", bridge.Perform(aliasId, "invoke", null) && invokes == 2);
        model.SetActionAvailability("Run", false);
        BrowserActionAvailability.ApplyInitial(alias, loader, document, model);
        Check("Denied actual alias command narrows enabled state", !alias.IsEnabled && !bridge.Perform(aliasId, "invoke", null) && invokes == 2);
        bridge.Bind(new StackPanel());
        Check("New render generation rejects stale IDs", !bridge.Perform(actionId, "invoke", null) && !bridge.Perform(editId, "value", "stale") && input.Text == "updated");
        bridge.Bind(new TextBox { Text = "Detached control" });
        using var unattached = JsonDocument.Parse(bridge.ReadSnapshot());
        Check("Unattached native geometry is explicitly unavailable", unattached.RootElement.GetProperty("viewport").ValueKind == JsonValueKind.Null
            && unattached.RootElement.GetProperty("elements")[0].GetProperty("bounds").ValueKind == JsonValueKind.Null);
        bridge.Clear();
        using var reset = JsonDocument.Parse(bridge.ReadSnapshot());
        Check("Private reset clears semantics and rejects prior controls", reset.RootElement.GetProperty("elements").GetArrayLength() == 0 && !bridge.Perform(editId, "focus", null));
        Console.WriteLine($"{checks} source-linked native peer checks passed; this is not actual browser accessibility acceptance.");
    }
    finally { window.Close(); }
}, CancellationToken.None);

public sealed class AccessibilityTestApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<AccessibilityTestApplication>().UseSkia())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Home");
}
