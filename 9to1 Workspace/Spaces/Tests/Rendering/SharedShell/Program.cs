using System.ComponentModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using HavenOS.Apps.Spaces;
using HavenOS.Apps.Spaces.Chat.Rendering;

if (args.Length != 1) throw new ArgumentException("Supply the evidence output directory.");
var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
await using var session = HeadlessUnitTestSession.StartNew(typeof(ShellTestApplication));
await session.Dispatch(async () =>
{
    var registry = ChatSpaceCuiRuntime.CreateRegistry();
    NineToOne.Web.BrowserCanonicalIcons.Register(registry);
    var owner = new ShellFixture();
    using var theme = new CuiThemePreferenceContext(owner);
    using var loader = new CuiControlLoader(registry);
    loader.SetSurface("Spaces", "app.spaces"); loader.SetThemePreferenceContext(theme);
    loader.SetBindingContext(owner); loader.SetActionDispatcher(owner);
    var document = SpacesShellCuiDocument.Load(includeOriginalGeneratedChildren: false);
    var result = loader.TryLoad(document);
    Check(result.Root is not null && !result.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error),
        "Real combined canonical load: " + string.Join(Environment.NewLine, result.Diagnostics));
    var root = result.Root!; loader.WireBindings(root);
    // Attach before mounting: retained authored identity, not visual-template luck.
    using var input = SpacesShellCuiRuntime.Attach(root, owner.SetViewport);
    var window = new Window { Width = 1530, Height = 946, Content = root };
    window.Show(); Pump();
    try
    {
        foreach (var width in new[] { 1530, 800, 390 })
        {
            window.Width = width; window.Height = 946; Pump();
            foreach (var selected in new[] { "Professional", "Glow" })
            {
                owner.SetTheme(selected); Pump();
                var header = loader.FindAuthoredControl<Control>(root, "spaces-header");
                var workspace = loader.FindAuthoredControl<Control>(root, "spaces-workspace-slot");
                var headerOrigin = header.TranslatePoint(default, window)!.Value;
                var workspaceOrigin = workspace.TranslatePoint(default, window)!.Value;
                Check(workspaceOrigin.Y >= headerOrigin.Y + header.Bounds.Height - 1, $"Header must not be occluded: header {header.Bounds}, workspace {workspace.Bounds}, origins {headerOrigin}/{workspaceOrigin}");
                var modelButton = loader.FindAuthoredControl<Button>(root, "spaces-model");
                var modelCentre = modelButton.TranslatePoint(new Point(modelButton.Bounds.Width / 2, modelButton.Bounds.Height / 2), window)!.Value;
                var hit = window.InputHitTest(modelCentre) as Visual;
                Check(ReferenceEquals(hit, modelButton) || hit is not null && modelButton.IsVisualAncestorOf(hit), "Actual model click target is not covered by workspace");
                var records = new List<object>();
                foreach (var id in new[] { "space-rail", "spaces-chat-type", "spaces-model", "spaces-account", "composer-input", "send" })
                {
                    var control = loader.FindAuthoredControl<Control>(root, id);
                    var origin = control.TranslatePoint(default, window) ?? throw new Exception("Detached " + id);
                    Check(control.Bounds.Width > 0 && control.Bounds.Height > 0, "Measured " + id);
                    Check(origin.X >= -1 && origin.X + control.Bounds.Width <= width + 1, "Horizontal bounds " + id);
                    Check(origin.Y >= -1 && origin.Y + control.Bounds.Height <= 947, "Vertical bounds " + id);
                    records.Add(new { id, x = origin.X, y = origin.Y, width = control.Bounds.Width, height = control.Bounds.Height });
                }
                using var frame = window.CaptureRenderedFrame(); Check(frame is not null, "Actual Skia frame");
                frame!.Save(Path.Combine(output, $"controlled-shared-shell-{selected}-{width}x946.png"));
                File.WriteAllText(Path.Combine(output, $"controlled-shared-shell-{selected}-{width}x946.json"), JsonSerializer.Serialize(new
                {
                    scope = "Explicit renderer owner; exact production shared CUI documents; no service or provider acceptance",
                    theme = owner.Current.Name, appearance = owner.Current.Appearance.ToString(), width, height = 946, records
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        var composer = loader.FindAuthoredControl<TextBox>(root, "composer-input");
        composer.Focus(); composer.SelectAll(); window.KeyTextInput("Exact controlled draft");
        Check(Equals(owner.Values["composer.text"], "Exact controlled draft"), "Composer uses actual two-way owner binding");
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await loader.WhenActionsIdleAsync();
        Check(owner.Actions.Count(a => a == "chat.send") == 1, "Enter invokes original send binding once");
        loader.FindAuthoredControl<Button>(root, "reopen-context-sidebar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await loader.WhenActionsIdleAsync(); Pump();
        Check(owner.Drawer, "Same navigation action opens actual compact drawer");
        var sidebar = loader.FindAuthoredControl<Control>(root, "context-sidebar");
        Check(sidebar.IsEffectivelyVisible, "Compact contextual controls are really visible");
        Check(composer.Text == "Exact controlled draft", "Showing contextual sidebar preserves original draft/control");
        Console.WriteLine("PASS shared canonical shell: strict combined load, original input, desktop/compact bounds, Professional/Glow Skia. Explicit renderer fixture; product acceptance NOT RUN.");
    }
    catch (Exception exception) { Console.Error.WriteLine(exception); Environment.ExitCode = 1; throw; }
    finally { window.Content = null; window.Close(); loader.Dispose(); await loader.WhenActionsIdleAsync(); }
    void Pump() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
}, CancellationToken.None);

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

public sealed class ShellTestApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<ShellTestApplication>().UseSkia())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Spaces");
}
internal sealed class ShellFixture : ICuiWritableBindingContext, ICuiActionDispatcher, INotifyPropertyChanged, ICuiThemePreferenceSource
{
    public Dictionary<string, object?> Values { get; } = new(StringComparer.Ordinal);
    public List<string> Actions { get; } = [];
    private bool _compact;
    public bool Drawer { get; private set; }
    public CuiThemePreferenceSnapshot Current { get; private set; } = new("Professional", 0, "explicit-renderer-owner", false, new(), CuiAppearance.Dark);
    public event EventHandler? Changed;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void SetTheme(string name) { Current = Current with { Name = name, Revision = Current.Revision + 1 }; Changed?.Invoke(this, EventArgs.Empty); }
    public ShellFixture()
    {
        foreach (var (key, value) in new (string, object?)[] {
            ("navigation.interactive", true), ("navigation.canManageSpaces", false), ("navigation.canConfigure", false),
            ("navigation.pins", new[] { Row(("id", "chat"), ("name", "Chat"), ("description", "Explicit navigation fixture"), ("selected", true), ("available", true), ("iconSource", "icon:chat")), Row(("id", "study"), ("name", "Study"), ("description", "No Study owner in renderer fixture"), ("selected", false), ("available", false), ("iconSource", "icon:study")) }),
            ("sidebar.name", "Chat"), ("sidebar.query", ""), ("sidebar.newChatLabel", "New Chat"), ("sidebar.canStart", true), ("sidebar.groupsVisible", true), ("sidebar.groupsLabel", "Chat Groups"), ("sidebar.newGroupLabel", "New Chat Group"), ("sidebar.isChat", true), ("sidebar.specialistsAvailable", false), ("sidebar.specialistsDescription", "No service in renderer fixture"), ("sidebar.status", "Explicit renderer fixture"), ("sidebar.sections", Array.Empty<object>()),
            ("chat.mainInteractive", true), ("chat.title", "Controlled renderer transcript"), ("chat.typeLabel", "Normal"), ("chat.locationLabel", "Explicit renderer fixture"), ("chat.modelLabel", "A deliberately long catalogue model name · large context"), ("chat.canChooseType", true), ("chat.canChooseModel", true), ("chat.empty", false), ("chat.hasMessages", true), ("chat.hasIssues", false), ("chat.emptyDescription", ""), ("chat.status", "Explicit renderer fixture. No connected model or stored account."), ("chat.approvalLabel", "Ask for Approval"), ("chat.canChooseApproval", false),
            ("chat.messages", new[] {
                Row(("id", "user-one"), ("author", "You · controlled fixture"), ("content", "Keep the original conversation and navigation owners."), ("alignment", "Right"), ("padding", "16,12"), ("background", Brushes.Transparent), ("accessibleName", "Controlled user message"), ("hasWorkStatus", false), ("workStatus", ""), ("tools", Array.Empty<object>()), ("isAssistant", false), ("canModify", false)),
                Row(("id", "assistant-one"), ("author", "Explicit renderer fixture"), ("content", "This is the **actual shared CUI layout** using controlled text.\n\nIt is not a provider response or product acceptance."), ("alignment", "Left"), ("padding", "0"), ("background", Brushes.Transparent), ("accessibleName", "Controlled assistant message"), ("hasWorkStatus", false), ("workStatus", ""), ("tools", Array.Empty<object>()), ("isAssistant", true), ("canModify", false)) }),
            ("composer.text", ""), ("composer.placeholder", "Message · explicit renderer fixture"), ("composer.canEdit", true), ("composer.canSend", true), ("composer.sending", false), ("composer.sendLabel", "Send controlled message"), ("composer.sendIcon", "icon:arrow-up"), ("composer.sendGlyph", "↑"), ("composer.hasAttachments", false), ("composer.attachments", Array.Empty<object>()),
            ("context.visible", false), ("context.expanded", false), ("context.summary", ""), ("context.entries", Array.Empty<object>()), ("dual.available", false), ("dual.active", false), ("dual.label", ""), ("dual.modelLabel", ""),
            ("catalog.visible", false), ("catalog.query", ""), ("catalog.items", Array.Empty<object>()), ("catalog.status", ""),
            ("dialog.visible", false), ("dialog.title", ""), ("dialog.description", ""), ("dialog.isText", false), ("dialog.canConfirm", false), ("dialog.canInteract", true), ("dialog.text", ""), ("dialog.error", ""), ("dialog.confirmLabel", "Confirm"), ("dialog.choices", Array.Empty<object>()),
            ("navDialog.visible", false), ("navDialog.title", ""), ("navDialog.isText", false), ("navDialog.canConfirm", false), ("navDialog.canInteract", true), ("navDialog.value", ""), ("navDialog.error", ""), ("navDialog.confirmLabel", "Confirm"), ("navDialog.choices", Array.Empty<object>()),
            ("shell.hasUsage", false), ("shell.canOpenUsage", false), ("shell.usageLabel", ""), ("shell.usageDescription", ""), ("shell.canSearch", false), ("shell.canNotify", false), ("shell.canAccount", false)
        }) Values[key] = value;
        ApplyGeometry();
    }
    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] fields) => fields.ToDictionary(f => f.Key, f => f.Value);
    public void SetViewport(double width) { var compact = width < 860; if (compact == _compact) return; _compact = compact; Drawer = false; ApplyGeometry(); }
    private void ApplyGeometry()
    {
        Values["shell.columns"] = _compact ? "70 1fr" : "350 1fr"; Values["shell.rows"] = _compact ? "172 1fr" : "80 1fr";
        Values["shell.navigationWidth"] = _compact && !Drawer ? 70d : 350d; Values["shell.navigationSpan"] = _compact && Drawer ? 2 : 1;
        Values["shell.headerMinHeight"] = _compact ? 172d : 80d;
        Values["shell.headerColumns"] = _compact ? "1fr" : "1fr Auto Auto"; Values["shell.headerRows"] = _compact ? "Auto Auto Auto" : "Auto";
        Values["shell.modelColumn"] = _compact ? 0 : 1; Values["shell.modelRow"] = _compact ? 1 : 0;
        Values["shell.utilityColumn"] = _compact ? 0 : 2; Values["shell.utilityRow"] = _compact ? 2 : 0; Values["shell.utilitySpan"] = 1;
        Values["layout.columns"] = _compact && !Drawer ? "70 0" : "70 280"; Values["layout.sidebarVisible"] = !_compact || Drawer;
        Values["layout.railVisible"] = true; Values["layout.collapsed"] = _compact && !Drawer; Values["layout.canChange"] = true;
        PropertyChanged?.Invoke(this, new(null));
    }
    public bool TryGetValue(string path, out object? value) => Values.TryGetValue(path, out value);
    public bool TrySetValue(string path, object? value) { if (!Values.ContainsKey(path)) return false; Values[path] = value; PropertyChanged?.Invoke(this, new(path)); return true; }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); Actions.Add(command); if (command == "nav.toggle-sidebar") { Drawer = !Drawer; ApplyGeometry(); } return ValueTask.CompletedTask; }
}
