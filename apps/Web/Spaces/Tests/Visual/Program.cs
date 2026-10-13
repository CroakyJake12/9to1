using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using HavenOS.Apps.Spaces;
using HavenOS.Apps.Spaces.Chat;
using HavenOS.Home.Core;
using NineToOne.Web.Spaces;
using NineToOne.Web.Spaces.Chat;

if (args.Length == 0 || args[0] != "--visual-test-fixture")
{
    Console.Error.WriteLine("This isolated automated fixture requires --visual-test-fixture. It is NOT LIVE ACCOUNT OR MODEL OUTPUT and must never be used for functional acceptance.");
    Environment.ExitCode = 2;
    return;
}
args = args[1..];
Console.Error.WriteLine("VISUAL TEST FIXTURE — NOT LIVE ACCOUNT OR MODEL OUTPUT. Use the labelled HTML report for review; raw comparison PNGs are internal test evidence only.");
if (args.Length is < 3 or > 5) throw new ArgumentException("Supply repository root, output directory and visual-only JSON fixture path, optional capture width (1530 or 390), and optional canonical appearance.");
var repository = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var fixturePath = Path.GetFullPath(args[2]);
var captureWidth = args.Length >= 4 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 1530;
if (captureWidth is not (1530 or 390)) throw new ArgumentOutOfRangeException(nameof(captureWidth));
var appearance = CuiAppearance.Dark;
if (args.Length == 5 && (!Enum.TryParse(args[4], false, out appearance) || !Enum.IsDefined(appearance)))
    throw new ArgumentException("Appearance must be SuperBright, Bright, Dark or SuperDark.");
Directory.CreateDirectory(output);
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-GB");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
var input = JsonSerializer.Deserialize<RestaurantVisualInput>(File.ReadAllText(fixturePath), new JsonSerializerOptions(JsonSerializerDefaults.Web))
    ?? throw new InvalidDataException("No visual-only fixture input.");
var sourcePaths = new[] {
    "apps/Web/Spaces/BrowserSpacesFeature.cs", "apps/Web/BrowserCanonicalIcons.cs",
    "apps/Web/BrowserActionAvailability.cs", "apps/Web/BrowserShellActions.cs", "apps/Web/BrowserApplication.cs",
    "9to1 Workspace/Spaces/Source/Chat/ChatSpaceContracts.cs", "9to1 Workspace/Spaces/Source/Chat/ChatSpaceController.cs",
    "9to1 Workspace/Spaces/Source/Chat/ChatSpaceProjection.cs", "9to1 Workspace/Spaces/UI/Chat/Runtime/ChatSpaceCuiRuntime.cs",
    "9to1 Workspace/Spaces/UI/Chat/Runtime/SpacesShellCuiRuntime.cs", "framework/CUI/Runtime/CuiControlLoader.cs",
    "framework/CUI/Runtime/CuiMarkdownView.cs", "framework/CUI/Runtime/CuiNativeHost.cs",
    "framework/CUI/Runtime/CuiInteractionReveal.cs", "framework/CUI/Runtime/CuiControlRegistry.cs",
    "framework/CUI/Runtime/CuiPropertyPresentation.cs", "9to1 Workspace/shared/src/Haven.UI/Drawing/HavenIconCatalog.cs"
}.Select(path => Path.Combine(repository, path))
 .Concat(Directory.GetFiles(Path.Combine(repository, "apps/Web/Spaces/Chat"), "BrowserChatSession*.cs"))
 .Concat(Directory.GetFiles(Path.Combine(repository, "9to1 Workspace/Spaces/UI/Shell"), "*", SearchOption.TopDirectoryOnly))
 .Concat(Directory.GetFiles(Path.Combine(repository, "9to1 Workspace/Spaces/UI/Chat"), "*", SearchOption.TopDirectoryOnly))
 .Concat(Directory.GetFiles(Path.Combine(repository, "framework/CUI/Themes"), "*.cs"))
 .Concat(Directory.GetFiles(Path.Combine(repository, "apps/Web/Spaces/Tests/Visual"), "*", SearchOption.TopDirectoryOnly))
 .Append(fixturePath).Where(File.Exists).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
var hashes = sourcePaths.ToDictionary(path => Path.GetRelativePath(repository, path), Hash);
var checks = new List<object>();
var observations = new List<object>();
var revealObservations = new List<object>();
var failures = new List<string>();
void Check(bool condition, string detail)
{ checks.Add(new { passed = condition, detail }); if (!condition) failures.Add(detail); }
var backend = new RestaurantVisualBackend(input);
await using var headless = HeadlessUnitTestSession.StartNew(typeof(BrowserVisualApplication));
await headless.Dispatch<bool>(async () =>
{
    await using var feature = new BrowserSpacesFeature(
        () => throw new InvalidOperationException("Visual Chat must not substitute Spaces configuration for the product."),
        () => { }, Present, _ => throw new InvalidOperationException("No external route is implemented by this visual-only host."), () => backend);
    var ready = await feature.OpenAsync(new(HomeFeatureRouteIds.Spaces));
    if (!ready.Succeeded || ready.ViewState is null) throw new InvalidOperationException("The actual browser product route failed: " + ready.Message);
    var surface = feature.Render(ready.ViewState);
    if (surface.Bindings is not BrowserChatSession chat || surface.Actions is not BrowserChatSession || !ReferenceEquals(surface.Bindings, surface.Actions))
        throw new InvalidOperationException("The route did not issue the same actual BrowserChatSession for binding and action ownership.");
    var registry = surface.ControlRegistry ?? throw new InvalidOperationException("The product's actual control registry is missing.");
    NineToOne.Web.BrowserCanonicalIcons.Register(registry);
    using var theme = new CuiThemePreferenceContext(new VisualThemePreference(appearance));
    using var loader = new CuiControlLoader(registry);
    loader.SetSurface("Spaces", HomeFeatureRouteIds.Spaces); loader.SetThemePreferenceContext(theme);
    var hostActions = new NineToOne.Web.BrowserShellActions(surface.Actions,
        () => throw new InvalidOperationException("No external navigation is implemented in this visual-only host."));
    loader.SetBindingContext(surface.Bindings); loader.SetActionDispatcher(hostActions);
    var loaded = loader.TryLoad(surface.Document);
    if (loaded.Root is not { } root || loaded.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
        throw new InvalidOperationException("Actual product CUI load failed: " + string.Join(Environment.NewLine, loaded.Diagnostics));
    loader.WireBindings(root);
    using var availability = NineToOne.Web.BrowserActionAvailability.Observe(root, loader, surface.Document, hostActions, surface.Bindings);
    using var attachment = surface.Attach?.Invoke(root);
    // Same viewport ownership wrapper as BrowserApplication mounts for canonical pages.
    var viewport = new ScrollViewer { Content = root,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    var window = new Window { Width = 1530, Height = 946, Content = viewport };
    window.Show(); Pump();
    surface.Admission!.Accept(); Pump();
    try
    {
        var recent = root.GetVisualDescendants().OfType<Button>().Single(button =>
            button.Name == "sidebar-entry-open" && AutomationProperties.GetName(button) == input.ConversationTitle);
        Check(recent.IsEffectivelyVisible && recent.IsEffectivelyEnabled, "Actual recent-chat button is visible and enabled.");
        recent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await loader.WhenActionsIdleAsync(); Pump();
        if (captureWidth != 1530) { window.Width = captureWidth; Pump(); }
        Check(backend.Calls.Count(call => call == nameof(IChatSpaceBackend.GetConversationAsync)) == 1,
            "Actual recent-chat action invokes canonical controller/backend conversation loading once.");
        Check(chat.TryGetValue("chat.messages", out var messages) && messages is IReadOnlyList<ChatSpaceMessage> { Count: 2 },
            "Actual BrowserChatSession contains two messages projected by the canonical Chat controller.");
        Check(chat.TryGetValue("composer.text", out var draft) && Equals(draft, input.Draft), "Canonical saved draft is loaded into the actual owner.");
        var markdown = root.GetVisualDescendants().OfType<CuiMarkdownView>().ToArray();
        Check(markdown.Any(view => view.Text == input.UserMessage) && markdown.Any(view => view.Text == input.AssistantMessage),
            "Original shared Markdown controls render both exact canonical messages.");
        var messageOwners = root.GetVisualDescendants().OfType<Control>().Where(control => control.Name == "chat-message").ToArray();
        var messageStrips = messageOwners.Select(owner => owner.GetVisualDescendants().OfType<Control>()
            .Single(control => control.Name == "chat-message-actions")).ToArray();
        Check(messageOwners.Length == 2 && messageStrips.Length == 2,
            "Each actual repeated message retains its own original action strip.");
        var originalFocus = window.FocusManager?.GetFocusedElement();
        window.FocusManager?.Focus(null); window.MouseMove(new(-10, -10)); Pump();
        var ownerGeometry = messageOwners.Select(control => (control.Bounds, control.TranslatePoint(default, window))).ToArray();
        var stripGeometry = messageStrips.Select(control => (control.Bounds, control.TranslatePoint(default, window))).ToArray();
        ObserveReveal("resting");
        Check(messageStrips.All(strip => strip.Opacity == 0 && !strip.IsHitTestVisible),
            "Original message actions rest transparent and cannot receive pointer hits; original controls remain mounted.");
        var originalCopy = messageStrips[0].GetVisualDescendants().OfType<Button>().First(button =>
            button.Tag is string action && (surface.Document.Actions.TryGetValue(action, out var definition) ? definition.Command : action) == "chat.message-copy");
        using (var restFrame = window.CaptureRenderedFrame())
        {
            var hiddenHit = window.InputHitTest(Centre(originalCopy)) as Visual;
            Check(!ReferenceEquals(hiddenHit, originalCopy) && !(hiddenHit is not null && originalCopy.IsVisualAncestorOf(hiddenHit)),
                "Resting original Copy button is absent from the actual compositor hit target.");
        }
        var firstBody = markdown.Single(view => view.Text == input.UserMessage);
        window.MouseMove(Centre(firstBody)); Pump();
        ObserveReveal("pointer-over-first-message");
        Check(messageStrips[0].Opacity > 0 && messageStrips[0].IsHitTestVisible && messageStrips[1].Opacity == 0,
            "Real pointer entry reveals only that repeated message's original actions.");
        var bodyBottom = firstBody.TranslatePoint(new Point(0, firstBody.Bounds.Height), window)!.Value.Y;
        var stripTop = messageStrips[0].TranslatePoint(default, window)!.Value.Y;
        var copyCentre = Centre(originalCopy);
        foreach (var point in new[] { new Point(copyCentre.X, bodyBottom - 1),
            new Point(copyCentre.X, (bodyBottom + stripTop) / 2),
            new Point(copyCentre.X, stripTop + 1), copyCentre })
        {
            window.MouseMove(point); Pump();
            using var trajectoryFrame = window.CaptureRenderedFrame();
            Check(messageStrips[0].Opacity > 0 && messageStrips[0].IsHitTestVisible,
                $"Original pointer trajectory stays revealed through body/gap/action at{point.X:F1},{point.Y:F1}.");
        }
        var copyHit = window.InputHitTest(copyCentre) as Visual;
        Check(ReferenceEquals(copyHit, originalCopy) || copyHit is not null && originalCopy.IsVisualAncestorOf(copyHit),
            "Original available Copy remains an actual hit target after pointer crosses its owner's gap.");
        CheckGeometry("pointer reveal");
        using (var hoverFrame = window.CaptureRenderedFrame())
            hoverFrame?.Save(Path.Combine(output, $"actual-browser-chat-actions-hover-{captureWidth}x946.png"));
        window.MouseMove(new(-10, -10)); Pump();
        Check(messageStrips.All(strip => strip.Opacity == 0), "Real pointer exit returns unfocused message actions to rest.");
        loader.FindAuthoredControl<Button>(root, "spaces-account").Focus(NavigationMethod.Pointer);
        for (var tabs = 0; tabs < 128 && !originalCopy.IsFocused; tabs++)
        {
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Pump();
        }
        Check(originalCopy.IsFocused, "Physical Tab traversal reaches the same original available Copy action.");
        window.MouseMove(new(-10, -10)); Pump();
        ObserveReveal("keyboard-focus-first-message-action");
        Check(originalCopy.IsFocused && messageStrips[0].Opacity > 0 && messageStrips[0].IsHitTestVisible && messageStrips[1].Opacity == 0,
            "Original keyboard focus retains the correct actions after pointer exit without revealing its sibling.");
        CheckGeometry("keyboard reveal");
        using (var focusedFrame = window.CaptureRenderedFrame())
            focusedFrame?.Save(Path.Combine(output, $"actual-browser-chat-actions-focused-{captureWidth}x946.png"));
        window.FocusManager?.Focus(originalFocus); window.MouseMove(new(-10, -10)); Pump();
        ObserveReveal("restored-original-focus");
        Check(messageStrips.All(strip => strip.Opacity == 0), "Original focus restoration returns both strips to the resting capture state.");
        CheckGeometry("focus restoration");
        var disclaimer = loader.FindAuthoredControl<TextBlock>(root, "chat-disclaimer");
        Check(disclaimer.Text == "AI can make mistakes. Double-check important information and tasks." &&
            disclaimer.FontSize == 14 && disclaimer.FontStyle == Avalonia.Media.FontStyle.Italic,
            "Actual shared footer contains the exact approved disclaimer in canonical14px italic text.");
        var composerForDisclaimer = loader.FindAuthoredControl<Control>(root, "floating-composer");
        Check(disclaimer.TranslatePoint(default, window)!.Value.Y >=
            composerForDisclaimer.TranslatePoint(default, window)!.Value.Y + composerForDisclaimer.Bounds.Height,
            "Approved disclaimer stays below the actual composer without overlap.");
        var contrastSurface = loader.FindAuthoredControl<Control>(root, "conversation-surface");
        if (disclaimer.Foreground is Avalonia.Media.ISolidColorBrush foreground &&
            contrastSurface.TryFindResource("CuiWorkspaceBrush", out var actualBackground) && actualBackground is Avalonia.Media.ISolidColorBrush background)
        {
            var contrast = Contrast(foreground.Color, background.Color);
            observations.Add(new { id = "disclaimer-contrast", foreground = foreground.Color.ToString(), background = background.Color.ToString(), ratio = contrast });
            Check(contrast >= 4.5, "Actual Professional disclaimer colours provide at least4.5:1 small-text contrast.");
        }
        else Check(false, "Actual disclaimer foreground/background must be observable for contrast verification.");
        Check(chat.TryGetValue("chat.status", out var idleStatus) && Equals(idleStatus, string.Empty),
            "Original successfully loaded idle status is not shown as a generic ready message.");
        void ObserveReveal(string state) => revealObservations.Add(new { state,
            messages = messageStrips.Select((strip, index) => new { index, strip.Opacity, strip.IsHitTestVisible,
                ownerBounds = messageOwners[index].Bounds.ToString(), stripBounds = strip.Bounds.ToString(),
                messageOwners[index].IsKeyboardFocusWithin, messageOwners[index].IsPointerOver }).ToArray() });
        void CheckGeometry(string phase) => Check(messageOwners.Select((control, index) =>
            ownerGeometry[index] == (control.Bounds, control.TranslatePoint(default, window)) &&
            stripGeometry[index] == (messageStrips[index].Bounds, messageStrips[index].TranslatePoint(default, window))).All(equal => equal),
            "Original message and action bounds do not move during " + phase + ".");
        static double Contrast(Avalonia.Media.Color foreground, Avalonia.Media.Color background)
        {
            static double Channel(double value) => value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
            static double Luminance(double r, double g, double b) => .2126 * Channel(r) + .7152 * Channel(g) + .0722 * Channel(b);
            var alpha = foreground.A / 255d;
            var backgroundLuminance = Luminance(background.R / 255d, background.G / 255d, background.B / 255d);
            var foregroundLuminance = Luminance((foreground.R * alpha + background.R * (1 - alpha)) / 255d,
                (foreground.G * alpha + background.G * (1 - alpha)) / 255d, (foreground.B * alpha + background.B * (1 - alpha)) / 255d);
            return (Math.Max(foregroundLuminance, backgroundLuminance) + .05) / (Math.Min(foregroundLuminance, backgroundLuminance) + .05);
        }
        Point Centre(Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        var messageActions = new[] { "chat.message-copy", "chat.message-edit", "chat.message-branch", "chat.message-regenerate", "chat.message-forget", "chat.message-delete" }
            .Select(command => new {
                command, ownerAvailable = hostActions.IsActionAvailable(command) == true,
                buttons = root.GetVisualDescendants().OfType<Button>().Where(button => button.Tag is string action &&
                    (surface.Document.Actions.TryGetValue(action, out var definition) ? definition.Command : action) == command)
                    .Select(button => new { visible = button.IsEffectivelyVisible, enabled = button.IsEffectivelyEnabled }).ToArray()
            }).ToArray();
        foreach (var action in messageActions)
        {
            Check(action.buttons.Length > 0, "Original message action buttons exist: " + action.command);
            Check(action.buttons.All(button => button.enabled == action.ownerAvailable), "Exact production host availability restricts actual buttons: " + action.command);
        }
        var header = loader.FindAuthoredControl<Control>(root, "spaces-header");
        var workspace = loader.FindAuthoredControl<Control>(root, "spaces-workspace-slot");
        var headerOrigin = header.TranslatePoint(default, window)!.Value;
        var workspaceOrigin = workspace.TranslatePoint(default, window)!.Value;
        Check(workspaceOrigin.Y >= headerOrigin.Y + header.Bounds.Height - 1, "Actual workspace does not geometrically cover the header.");
        foreach (var id in new[] { "space-rail", "context-sidebar", "spaces-header", "spaces-workspace-slot", "spaces-model", "spaces-account", "chat-transcript", "floating-composer", "composer-input", "chat-approval", "send", "chat-disclaimer" })
        {
            var control = loader.FindAuthoredControl<Control>(root, id);
            if (id == "context-sidebar" && captureWidth == 390)
            {
                var mounted = control.TranslatePoint(default, window) is not null;
                observations.Add(new { id, mounted, visible = control.IsEffectivelyVisible, enabled = control.IsEffectivelyEnabled });
                Check(!mounted || !control.IsEffectivelyVisible, "Compact contextual sidebar is intentionally collapsed by the real session.");
                continue;
            }
            Record(id, control);
            var position = control.TranslatePoint(default, window)!.Value;
            Check(control.Bounds.Width > 0 && control.Bounds.Height > 0 && control.IsEffectivelyVisible, "Visible measured control: " + id);
            Check(position.X >= -1 && position.X + control.Bounds.Width <= captureWidth + 1 && position.Y >= -1 && position.Y + control.Bounds.Height <= 947,
                $"Control inside{captureWidth}x946: " + id);
        }
        // Commit the real resized compositor frame before querying its hit-test scene.
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No real Skia capture returned.");
        foreach (var id in new[] { "spaces-model", "spaces-account", "composer-input", "send" })
        {
            var control = loader.FindAuthoredControl<Control>(root, id);
            var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            var hit = window.InputHitTest(point) as Visual;
            Check(ReferenceEquals(hit, control) || hit is not null && control.IsVisualAncestorOf(hit), "Unoccluded actual centre hit target: " + id);
        }
        foreach (var view in markdown) Record("actual-markdown:" + view.Text, view);
        foreach (var control in root.GetVisualDescendants().OfType<TextBlock>().Where(control =>
            control.Text is "You" or "Qwen" or "Searching for restaurants" or "Creating a Shortlist")) Record("semantic-text:" + control.Text, control);
        Check(frame.PixelSize.Width == captureWidth && frame.PixelSize.Height == 946, $"Exact actual capture pixels{captureWidth}x946.");
        frame.Save(Path.Combine(output, $"actual-browser-chat-restaurant-{captureWidth}x946.png"));
        var unconnected = new[] { "chat.choose-type", "chat.choose-approval", "nav.new-group", "nav.specialists", "nav.configure", "shell.search", "shell.notifications" }
            .Select(action => new { action, available = chat.IsActionAvailable(action) }).ToArray();
        var actualController = typeof(BrowserChatSession).GetField("_controller", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(chat);
        Check(actualController?.GetType() == typeof(ChatSpaceController), "Actual BrowserChatSession owns an actual canonical ChatSpaceController instance.");
        var data = new {
            scope = "VISUAL ONLY. Real BrowserSpacesFeature route, BrowserChatSession, ChatSpaceController, exact BrowserShellActions/BrowserActionAvailability host presentation, canonical CUI and Skia. Deterministic backend input; no authenticated account, durable storage, model response, tool execution or full acceptance.",
            verificationEnvironment = "isolated-automated-visual-fixture", fixtureOptIn = true,
            deliveryArtifact = "VISUAL-TEST-FIXTURE-NOT-LIVE.html", rawPngUse = "internal comparison only; never a live-product screenshot",
            fixtureProvenance = input.Provenance, route = ready.ViewState.ViewId,
            bindingOwner = surface.Bindings.GetType().AssemblyQualifiedName, actionOwner = surface.Actions.GetType().AssemblyQualifiedName,
            controllerOwner = actualController?.GetType().AssemblyQualifiedName,
            hostActionOwner = hostActions.GetType().AssemblyQualifiedName, hostAvailability = availability.GetType().AssemblyQualifiedName,
            messageActions, revealObservations,
            theme = "Professional", appearance = appearance.ToString(), width = captureWidth, height = 946,
            backendCalls = backend.Calls, diagnostics = loaded.Diagnostics.Select(d => d.ToString()).ToArray(),
            controls = observations, checks, layoutChecksPassed = failures.Count == 0, failures,
            unsupportedReferenceSemantics = input.UnsupportedReferenceSemantics, unconnectedActions = unconnected,
            approvedReferenceGeometryTranscribedByRoot = new {
                width = 1530, height = 946,
                rail = new { x = 0, width = 70 }, sidebar = new { x = 70, width = 280 },
                workspace = new { x = 350, y = 80, topLeftRadius = 48 },
                composer = new { x = 434, y = 786, width = 1013, height = 107, radius = 32 },
                model = new { x = 1078, y = 19, width = 266, height = 49 }
            },
            sourceHashes = hashes,
            assemblies = new[] { typeof(BrowserChatSession).Assembly, typeof(ChatSpaceController).Assembly, typeof(SpacesShellCuiDocument).Assembly, typeof(CuiControlLoader).Assembly, typeof(VisualThemePreference).Assembly }
                .Distinct().Select(assembly => new { name = assembly.FullName, path = assembly.Location, sha256 = Hash(assembly.Location) }).ToArray()
        };
        File.WriteAllText(Path.Combine(output, "actual-browser-chat-restaurant-evidence.json"), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        var labelledReport = VisualFixtureReport.Write(output, captureWidth, failures.Count == 0);
        Console.WriteLine("Labelled automated visual-fixture report: " + labelledReport);
        Console.WriteLine($"Actual browser-session visual capture: {checks.Count} layout checks, {failures.Count} failures. Scope is VISUAL ONLY; product/functionality acceptance not asserted.");
    }
    finally
    {
        // BrowserApplication retires the availability observer before the loader;
        // otherwise a property reset can re-enter a detached CUI projection.
        availability.Dispose();
        window.Content = null; window.Close();
        loader.Dispose(); await loader.WhenActionsIdleAsync();
        surface.Lifetime?.Dispose();
    }
    return true;
    void Record(string id, Control control)
    {
        var position = control.TranslatePoint(default, window) ?? throw new InvalidOperationException("Detached actual control: " + id);
        observations.Add(new { id, type = control.GetType().FullName, x = position.X, y = position.Y,
            width = control.Bounds.Width, height = control.Bounds.Height, visible = control.IsEffectivelyVisible,
            enabled = control.IsEffectivelyEnabled, accessibleName = AutomationProperties.GetName(control) });
    }
    void Pump() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
}, CancellationToken.None);
var changed = sourcePaths.Where(path => hashes[Path.GetRelativePath(repository, path)] != Hash(path)).ToArray();
if (changed.Length != 0) throw new InvalidOperationException("Source changed during capture: " + string.Join(", ", changed));
if (failures.Count != 0) Environment.ExitCode = 1;
static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
static async Task Present(Action action)
{ if (Dispatcher.UIThread.CheckAccess()) action(); else await Dispatcher.UIThread.InvokeAsync(action); }

public sealed class BrowserVisualApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<BrowserVisualApplication>().UseSkia())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Spaces");
}
internal sealed class VisualThemePreference(CuiAppearance appearance) : ICuiThemePreferenceSource
{
    public CuiThemePreferenceSnapshot Current { get; } = new("Professional", 0, "explicit-visual-only-theme", false, new(), appearance);
    public event EventHandler? Changed { add { } remove { } }
}
