using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
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
        <Cui>
          <Actions>
            <Action name="Alias" command="Run" />
          </Actions>
          <Page>
            <Container Type="Vertical Stack">
              <Button id="run" content="Native action" action="Run" />
              <Input id="edit" accessible-name="Native value" Type="Text" Value="original" />
              <Text text="Actual native text" />
              <Button id="authored-disabled" content="Unavailable owning state" action="Run" Interactive="false" />
              <Button id="alias" content="Aliased native action" action="Alias" />
            </Container>
          </Page>
        </Cui>
        """);
    var (root, diagnostics) = loader.TryLoad(document);
    if (root is null || diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
        throw new InvalidOperationException("Real CUI fixture failed to lower.");
    var container = root.GetLogicalDescendants().OfType<CuiContainerControl>().Single();
    var stack = (StackPanel)container.Layout;
    loader.WireBindings(root);
    using var availability = BrowserActionAvailability.Observe(root, loader, document, model, model);
    var button = (Button)container.AuthoredChildren[0];
    var input = (TextBox)container.AuthoredChildren[1];
    var authoredDisabled = (Button)container.AuthoredChildren[3];
    var alias = (Button)container.AuthoredChildren[4];
    Check("Available action does not overwrite authored disabled state", !authoredDisabled.IsEnabled);
    Check("Actual typed alias command determines availability", alias.IsEnabled && (string?)alias.Tag == "Alias");
    stack.Children.Add(new RadioButton { Content = "Unsupported radio" });
    var checkBox = new CheckBox { Content = "Actual native toggle" };
    stack.Children.Add(checkBox);
    stack.Children.Add(new TextBox { Text = "private-password", PasswordChar = '*' });
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
            .Single(e => e.GetProperty("controlType").GetString() == "RadioButton");
        Check("Unsupported peer diagnostic retains native geometry without advertising actions or values",
            MatchesBounds(unsupportedToggle.GetProperty("bounds"), ControlAutomationPeer.CreatePeerForElement(stack.Children[5]).GetBoundingRectangle())
            && !unsupportedToggle.TryGetProperty("id", out _) && !unsupportedToggle.TryGetProperty("value", out _)
            && !unsupportedToggle.TryGetProperty("role", out _) && !unsupportedToggle.TryGetProperty("enabled", out _));
        Check("Names and roles come from real native automation peers", action.GetProperty("name").GetString() == "Native action" && action.GetProperty("role").GetString() == "button" && edit.GetProperty("name").GetString() == "Native value");
        Check("Actual native text is projected", elements.Any(e => e.GetProperty("name").GetString() == "Actual native text"));
        Check("Invoke calls actual button provider and wired CUI action once", bridge.Perform(actionId, "invoke", null) && invokes == 1);
        // CUI owns enabled-state projection; change that authority rather than
        // a lower-priority backend value which the appearance fence rejects.
        CuiPropertyPresentation.Set(button, "Interactive", false, CuiPropertyLevel.Overlay, "Accessibility regression owner");
        Check("Changed native disabled state rejects previously enabled ID", !bridge.Perform(actionId, "invoke", null) && invokes == 1);
        using var disabled = JsonDocument.Parse(bridge.ReadSnapshot());
        Check("Snapshot reflects actual changed disabled state", !disabled.RootElement.GetProperty("elements").EnumerateArray().Single(e => e.GetProperty("id").GetString() == actionId).GetProperty("enabled").GetBoolean());
        Check("Value provider changes actual native TextBox", bridge.Perform(editId, "value", "updated") && input.Text == "updated");
        input.IsReadOnly = true;
        Check("Changed native readonly state rejects writes", !bridge.Perform(editId, "value", "rejected") && input.Text == "updated");
        Check("Focus uses actual native peer", bridge.Perform(editId, "focus", null) && input.IsFocused);
        using var focused = JsonDocument.Parse(bridge.ReadSnapshot());
        Check("Snapshot reports the actual current native focused peer", focused.RootElement.GetProperty("elements").EnumerateArray().Single(e => e.GetProperty("id").GetString() == editId).GetProperty("focused").GetBoolean());
        CuiPropertyPresentation.Set(input, "Focusable", false, CuiPropertyLevel.Overlay, "Accessibility regression owner");
        Check("Changed native focusability rejects focus", !bridge.Perform(editId, "focus", null));
        Check("Unsupported provider roles are explicit", snapshot.RootElement.GetProperty("unsupported").EnumerateArray().Any(e => e.GetString() == "RadioButton"));
        var toggle = elements.Single(e => e.GetProperty("role").GetString() == "checkbox");
        var toggleId = toggle.GetProperty("id").GetString()!;
        Check("Native checkbox state is exposed", toggle.GetProperty("value").GetString() == "false");
        Check("Toggle invokes the real native provider", bridge.Perform(toggleId, "toggle", null) && checkBox.IsChecked is true);
        CuiPropertyPresentation.Set(checkBox, "IsEnabled", false, CuiPropertyLevel.Overlay, "Accessibility regression owner");
        Check("Disabled native checkbox rejects toggling", !bridge.Perform(toggleId, "toggle", null) && checkBox.IsChecked is true);
        Check("Password values are not projected", !bridge.ReadSnapshot().Contains("private-password", StringComparison.Ordinal));
        var aliasId = elements.Single(e => e.GetProperty("automationId").GetString() == "alias").GetProperty("id").GetString()!;
        Check("Alias invokes the actual loader-resolved command once", bridge.Perform(aliasId, "invoke", null) && invokes == 2);
        model.SetActionAvailability("Run", false);
        availability.Refresh();
        Check("Denied actual alias command narrows enabled state", !alias.IsEnabled && !bridge.Perform(aliasId, "invoke", null) && invokes == 2);
        var menu = new Menu();
        var file = new CuiMenuItem { Header = "File" };
        var save = new CuiMenuItem { Header = "Save document" };
        var menuInvokes = 0; save.Click += (_, _) => ++menuInvokes;
        file.Items.Add(save); menu.Items.Add(file); stack.Children.Add(menu);
        var choices = new ComboBox { ItemsSource = new[] { "First", "Second" }, SelectedIndex = 0 };
        Avalonia.Automation.AutomationProperties.SetName(choices, "Layout choices"); stack.Children.Add(choices);
        var range = new Slider { Minimum = 0, Maximum = 100, Value = 50 };
        Avalonia.Automation.AutomationProperties.SetName(range, "Object opacity"); stack.Children.Add(range);
        window.Height = 700; window.UpdateLayout();
        JsonElement Current(string name)
        {
            using var state = JsonDocument.Parse(bridge.ReadSnapshot());
            return state.RootElement.GetProperty("elements").EnumerateArray().Single(element => element.GetProperty("name").GetString() == name).Clone();
        }
        string CurrentId(string name) => Current(name).GetProperty("id").GetString()!;
        Check("Native menu is projected through its real opening provider", Current("File").GetProperty("role").GetString() == "menuitem"
            && bridge.Perform(CurrentId("File"), "expand", null) && file.IsSubMenuOpen);
        window.UpdateLayout();
        var saveId = CurrentId("Save document");
        Check("Open native menu item invokes its routed command", bridge.Perform(saveId, "invoke", null));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Check("Menu provider invokes exactly once", menuInvokes == 1);
        file.IsSubMenuOpen = false; window.UpdateLayout();
        Check("Closed popup does not retain an invocable menu child", !bridge.Perform(saveId, "invoke", null));
        var contextMenu = new ContextMenu();
        var contextItem = new CuiMenuItem { Header = "Context action" };
        var contextInvokes = 0; contextItem.Click += (_, _) => ++contextInvokes;
        contextMenu.Items.Add(contextItem); input.ContextMenu = contextMenu;
        contextMenu.Open(input); window.UpdateLayout();
        var contextId = CurrentId("Context action");
        Check("Open context menu exposes its real provider outside the main visual tree", Current("Context action").GetProperty("role").GetString() == "menuitem"
            && bridge.Perform(contextId, "invoke", null));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Check("Context provider invokes the real menu command once", contextInvokes == 1);
        contextMenu.Close(); window.UpdateLayout();
        Check("Closed context menu rejects its previously exposed provider", !bridge.Perform(contextId, "invoke", null));
        Check("Combo box exposes actual selection and expansion", Current("Layout choices").GetProperty("value").GetString() == "First"
            && bridge.Perform(CurrentId("Layout choices"), "expand", null) && choices.IsDropDownOpen);
        window.UpdateLayout();
        Check("Actual option provider selects the native choice", bridge.Perform(CurrentId("Second"), "select", null) && choices.SelectedIndex == 1);
        Check("Read-only combo value provider is not advertised as a text editor", !bridge.Perform(CurrentId("Layout choices"), "value", "unsupported"));
        bridge.Perform(CurrentId("Layout choices"), "collapse", null);
        Check("Range edits call the actual bounded native provider", bridge.Perform(CurrentId("Object opacity"), "value", "75") && range.Value == 75
            && !bridge.Perform(CurrentId("Object opacity"), "value", "101") && range.Value == 75);
        CuiPropertyPresentation.Set(range, "IsEnabled", false, CuiPropertyLevel.Overlay, "Accessibility regression owner");
        Check("Disabled native range rejects a stale edit", !bridge.Perform(CurrentId("Object opacity"), "value", "20") && range.Value == 75);
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
