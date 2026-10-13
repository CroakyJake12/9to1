using System.ComponentModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using HavenOS.Apps.Spaces.Chat.Rendering;
using HavenOS.Apps.Spaces.Rendering;

// Explicit test owners exercise the actual public markup/renderer. This is not
// authentication, service integration, persistence, deployment or app acceptance.
if (args.Length != 2) throw new ArgumentException("Supply Spaces directory and evidence output directory.");
var spacesDirectory = Path.GetFullPath(args[0]);
var evidenceDirectory = Path.GetFullPath(args[1]);
Directory.CreateDirectory(evidenceDirectory);
await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesRenderApplication));
await session.Dispatch<bool>(async () =>
{
    foreach (var chat in new[] { false, true })
    {
        var path = Path.Combine(spacesDirectory, chat ? "UI/Chat/ChatSpace.cui" : "UI/Home/SpacesHome.cui");
        var source = File.ReadAllText(path);
        var registry = ChatSpaceCuiRuntime.CreateRegistry();
        var compilation = new CuiCompiler(registry).Compile(source, path,
            new CuiCompilerOptions(new CuiCompilationMetadata("1.0", "1.0", "2.0", ["layout"])));
        Check(compilation.Succeeded, "Strict canonical compilation: " + string.Join("\n", compilation.Diagnostics));
        var parser = new CuiRichParser();
        var document = parser.Parse(source, path);
        Check(!parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error), "Parser diagnostics");
        var owner = Fixtures.Create(chat);
        var themeSource = new ThemeSource();
        using var themes = new CuiThemePreferenceContext(themeSource);
        using var loader = new CuiControlLoader(registry);
        loader.SetSurface("Spaces", "app.spaces");
        loader.SetThemePreferenceContext(themes); loader.SetBindingContext(owner); loader.SetActionDispatcher(owner);
        var loaded = loader.TryLoad(document);
        Check(loaded.Root is not null && !loaded.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error),
            "Canonical render: " + string.Join("\n", loaded.Diagnostics));
        var root = loaded.Root!; loader.WireBindings(root);
        var window = new Window { Width = 1200, Height = 900, Content = root };
        IDisposable? composer = null;
        IDisposable? editor = null;
        var observedWidths = new List<double>();
        try
        {
            window.Show(); Pump(window);
            if (chat) composer = ChatSpaceCuiRuntime.AttachComposer(root);
            else editor = SpacesHomeCuiRuntime.AttachEditor(root, observedWidths.Add);
            foreach (var size in new[] { new Size(1200, 900), new Size(800, 600), new Size(390, 700) })
            {
                window.Width = size.Width; window.Height = size.Height;
                owner.Set("layout.columns", size.Width < 720 ? "0 1fr" : "280 1fr");
                owner.Set("layout.sidebarVisible", size.Width >= 720);
                owner.Set("layout.sidebarHidden", size.Width < 720);
                Pump(window);
                foreach (var theme in new[] { "Professional", "Glow" })
                {
                    themeSource.Change(theme); Pump(window);
                    using var frame = window.CaptureRenderedFrame();
                    Check(frame is not null, "Real rendered frame");
                    frame!.Save(Path.Combine(evidenceDirectory, $"fixture-{(chat ? "chat" : "home")}-{size.Width}-{theme}.png"));
                    foreach (var id in chat ? new[] { "composer-input", "send", "conversation-title" } : new[] { "selected-space-name", "add-context" })
                    {
                        var control = Find<Control>(root, id);
                        var origin = control.TranslatePoint(default, root) ?? throw new Exception("Detached " + id);
                        Check(control.Bounds.Width > 0 && control.Bounds.Height > 0, "Measured " + id);
                        Check(origin.X >= -1 && origin.X + control.Bounds.Width <= root.Bounds.Width + 1, "Horizontal reachability " + id);
                        Check(origin.Y >= -1 && origin.Y + control.Bounds.Height <= root.Bounds.Height + 1, "Vertical reachability " + id);
                    }
                }
            }
            // Reopen desktop: same root/owner, not a second surface.
            window.Width = 1200; window.Height = 900;
            owner.Set("layout.columns", "280 1fr"); owner.Set("layout.sidebarVisible", true); owner.Set("layout.sidebarHidden", false); Pump(window);
            if (chat)
            {
                SelectChoice(window, Find<ComboBox>(root, "model-choice"), 1);
                Check(Equals(owner.Values["composer.selectedModelIndex"], 1), "Real model dropdown writes the owner index");
                SelectChoice(window, Find<ComboBox>(root, "branch-choice"), 1);
                Check(Equals(owner.Values["conversation.selectedBranchIndex"], 1), "Real branch dropdown writes the owner index");
                var input = Find<TextBox>(root, "composer-input"); input.Focus(); input.SelectAll(); window.KeyTextInput("Persistent test draft");
                Check((string?)owner.Values["composer.draft"] == "Persistent test draft", "Actual input writeback");
                input.CaretIndex = 5; input.SelectionStart = 3; input.SelectionEnd = 5;
                var message = All<CuiMarkdownView>(root).Single();
                Check(message.Text.Contains("**fixture**", StringComparison.Ordinal), "Genuine Markdown receives owning message content");
                var priorContent = message.Text;
                themeSource.Change("Professional"); Pump(window);
                Check(ReferenceEquals(input, Find<TextBox>(root, "composer-input")) && input.IsFocused, "Theme retains composer focus/identity");
                Check(input.SelectionStart == 3 && input.SelectionEnd == 5, "Theme retains selection");
                Check(message.Text == priorContent, "Theme does not rewrite message content");
                var codeButtons = All<Button>(message).ToArray();
                Check(codeButtons.Length == 3 && codeButtons.All(button => !button.IsEnabled), "Unconfigured code effects remain disabled");
                var branch = All<Button>(root).Single(button => Equals(button.Content, "Branch"));
                branch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                Check(owner.Actions.Last() == ("chat.branch", Fixtures.MessageId), "Canonical repeated MessageID action");
                var current = Fixtures.Message("Updated **fixture** text");
                owner.Set("conversation.messages", new[] { current }); Pump(window);
                Check(ReferenceEquals(message, All<CuiMarkdownView>(root).Single()), "Message key preserves renderer identity");
                Check(message.Text == "Updated **fixture** text", "Same-key stream content refresh");
                Check(input.Text == "Persistent test draft" && input.IsFocused, "Transcript update retains draft/focus");
                input.CaretIndex = input.Text!.Length; input.SelectionStart = input.CaretIndex; input.SelectionEnd = input.CaretIndex;
                var sends = owner.Actions.Count(action => action.Command == "chat.send");
                Press(window, Key.Enter, RawInputModifiers.Shift); Pump(window);
                Check(input.Text.Contains('\n') && owner.Actions.Count(action => action.Command == "chat.send") == sends, "Shift Enter is newline only");
                Press(window, Key.Enter); await loader.WhenActionsIdleAsync();
                Check(owner.Actions.Count(action => action.Command == "chat.send") == sends + 1, "Enter submits once through real button");
                owner.Set("composer.canSend", false); Press(window, Key.Enter); await loader.WhenActionsIdleAsync();
                Check(owner.Actions.Count(action => action.Command == "chat.send") == sends + 1, "Disabled send stays unavailable");
                composer!.Dispose(); composer = null;
                owner.Set("composer.canSend", true); Press(window, Key.Enter); await loader.WhenActionsIdleAsync();
                Check(owner.Actions.Count(action => action.Command == "chat.send") == sends + 1, "Disposed keyboard owner cannot send");
            }
            else
            {
                var rows = All<Button>(root).Where(button => button.Name == "open-custom-space").ToArray();
                Check(rows.Length == 2, "Both canonical custom Spaces rendered");
                rows[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                Check(owner.Actions.Last() == ("spaces.select", "space-b"), "Canonical repeated SpaceID action");
                owner.Set("spaces.customSpaces", new[] { Fixtures.Space("space-b", "Beta"), Fixtures.Space("space-a", "Alpha") });
                Pump(window);
                var reordered = All<Button>(root).Where(button => button.Name == "open-custom-space").ToArray();
                Check(ReferenceEquals(rows[1], reordered[0]), "Space reorder preserves keyed row identity");
                var search = Find<TextBox>(root, "spaces-search"); search.Focus(); window.KeyTextInput("Beta");
                Check((string?)owner.Values["spaces.query"] == "Beta", "Spaces search writeback");
                Check(!Find<Button>(root, "open-selected-space").IsEnabled, "Unconfigured workspace cannot open");
                var create = Find<Button>(root, "create-space"); create.Focus();
                owner.Set("editor.visible", true); Pump(window);
                var name = Find<TextBox>(root, "space-editor-name");
                Check(name.IsFocused, "Opening the actual editor focuses its name input");
                window.KeyTextInput("Edited fixture");
                Check((string?)owner.Values["editor.name"] == "Edited fixture", "Space name input writeback");
                var confirms = owner.Actions.Count(action => action.Command == "spaces.confirm-edit");
                Press(window, Key.Enter, RawInputModifiers.Control); await loader.WhenActionsIdleAsync();
                Check(owner.Actions.Count(action => action.Command == "spaces.confirm-edit") == confirms + 1, "Control Enter invokes existing editor action once");
                owner.Set("editor.visible", false); Pump(window);
                Check(create.IsFocused, "Closing the editor restores the initiating control");
                owner.Set("editor.visible", true); Pump(window);
                owner.Set("editor.canConfirm", false); Press(window, Key.Enter, RawInputModifiers.Control); await loader.WhenActionsIdleAsync();
                Check(owner.Actions.Count(action => action.Command == "spaces.confirm-edit") == confirms + 1, "Invalid editor cannot save through keyboard");
                Press(window, Key.Escape); await loader.WhenActionsIdleAsync();
                Check(owner.Actions.Last().Command == "spaces.cancel-edit", "Escape invokes existing cancellation action");
                window.Width = 390; window.Height = 700;
                owner.Set("layout.columns", "0 1fr"); owner.Set("layout.sidebarVisible", false); owner.Set("layout.sidebarHidden", true); Pump(window);
                using (var frame = window.CaptureRenderedFrame())
                    (frame ?? throw new Exception("Missing editor frame")).Save(Path.Combine(evidenceDirectory, "fixture-home-editor-390.png"));
                Check(observedWidths.Any(width => Math.Abs(width - 390) < 1) && observedWidths.Any(width => Math.Abs(width - 1200) < 1), "Mounted viewport observations reach the actual owner callback");
                editor!.Dispose(); editor = null;
                var observed = observedWidths.Count; window.Width = 410; Pump(window);
                Check(observedWidths.Count == observed, "Retired editor cannot publish viewport observations");
                var cancels = owner.Actions.Count(action => action.Command == "spaces.cancel-edit");
                Press(window, Key.Escape); await loader.WhenActionsIdleAsync();
                Check(owner.Actions.Count(action => action.Command == "spaces.cancel-edit") == cancels, "Retired editor keyboard owner cannot act");
            }
            Console.WriteLine(JsonSerializer.Serialize(new { surface = chat ? "Chat" : "Manage Spaces", canonicalCompiler = "PASS", rendered = "PASS", fixtureOwners = true, platform = "headless Skia", productAcceptance = "NOT RUN" }));
        }
        finally { editor?.Dispose(); composer?.Dispose(); window.Close(); loader.Dispose(); await loader.WhenActionsIdleAsync(); }
    }
    await CheckBuilder(spacesDirectory, evidenceDirectory);
    return true;
}, CancellationToken.None);
Console.WriteLine("PASS: canonical Spaces markup and actual rendered fixture interaction checks.");

static IEnumerable<T> All<T>(Control root) where T : Control => root.GetVisualDescendants().OfType<T>();
static T Find<T>(Control root, string id) where T : Control => All<T>(root).Single(control => control.Name == id);
static void Pump(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
static void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
{ window.KeyPress(key, modifiers, PhysicalKey.None, null); window.KeyRelease(key, modifiers, PhysicalKey.None, null); }
static void SelectChoice(Window window, ComboBox select, int index)
{
    select.Focus(); select.IsDropDownOpen = true; Pump(window);
    var choice = select.ContainerFromIndex(index) ?? throw new Exception("Dropdown did not materialise a choice");
    var popup = TopLevel.GetTopLevel(choice) ?? throw new Exception("Dropdown is detached");
    popup.UpdateLayout();
    var point = choice.TranslatePoint(new Point(choice.Bounds.Width / 2, choice.Bounds.Height / 2), popup)
        ?? throw new Exception("Dropdown choice is detached");
    popup.MouseDown(point, MouseButton.Left); popup.MouseUp(point, MouseButton.Left); Pump(window);
    Check(select.SelectedIndex == index && !select.IsDropDownOpen, "Real dropdown pointer selection");
}

static async Task CheckBuilder(string spacesDirectory, string evidenceDirectory)
{
    var path = Path.Combine(spacesDirectory, "UI/Builder/SpaceBuilder.cui");
    var source = File.ReadAllText(path);
    var registry = new CuiControlRegistry();
    var compilation = new CuiCompiler(registry).Compile(source, path,
        new CuiCompilerOptions(new CuiCompilationMetadata("1.0", "1.0", "2.0", ["layout"])));
    Check(compilation.Succeeded, "Builder strict compilation: " + string.Join("\n", compilation.Diagnostics));
    var owner = new FixtureOwner();
    foreach (var (name, value) in new (string, object?)[] {
        ("title", "Fixture Space Builder"), ("identity", "Explicit fixture SpaceID; revision 1"),
        ("basic", true), ("advanced", false), ("expert", false), ("documentVisible", true),
        ("graphVisible", false), ("filesVisible", false), ("canEdit", true), ("page", "Details"),
        ("name", "Fixture Space"), ("description", "Explicit fixture configuration"), ("icon", "book"),
        ("instructions", "Fixture instructions for rendering checks"), ("modelId", ""), ("provider", ""), ("modelRequired", ""),
        ("webOptions", new[] { "Inherited", "Disabled", "Allowed" }), ("webIndex", 0), ("sourceOnly", false),
        ("includeSelection", true), ("maximumItems", ""), ("memoryOptions", new[] { "None", "Conversation" }), ("memoryIndex", 0),
        ("bindingOptions", new[] { "Inherited", "Disabled", "Allowed" }), ("bindings", Array.Empty<object>()),
        ("resourceSummary", "No resources in this fixture"), ("resources", Array.Empty<object>()),
        ("hasLegacyLayout", false), ("legacyLayout", ""), ("status", "Explicit renderer fixture; no registry write")
    }) owner.Values["builder." + name] = value;
    var themeSource = new ThemeSource();
    using var themes = new CuiThemePreferenceContext(themeSource);
    using var loader = new CuiControlLoader(registry);
    loader.SetSurface("Spaces", "app.spaces"); loader.SetThemePreferenceContext(themes);
    loader.SetBindingContext(owner); loader.SetActionDispatcher(owner);
    var loaded = loader.LoadMarkup(source);
    Check(loaded.Root is not null && !loaded.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error), "Builder runtime load");
    var root = loaded.Root!; loader.WireBindings(root);
    var window = new Window { Width = 1200, Height = 900, Content = root };
    try
    {
        window.Show(); Pump(window);
        foreach (var mode in new[] { "basic", "advanced", "logic", "files" })
        {
            owner.Set("builder.basic", mode == "basic"); owner.Set("builder.advanced", mode == "advanced");
            owner.Set("builder.expert", mode is "logic" or "files");
            owner.Set("builder.documentVisible", mode is "basic" or "advanced");
            owner.Set("builder.graphVisible", mode == "logic"); owner.Set("builder.filesVisible", mode == "files");
            owner.Set("builder.page", mode == "logic" ? "Harness Logic" : "Files");
            foreach (var size in new[] { new Size(1200, 900), new Size(800, 600), new Size(390, 700) })
            {
                window.Width = size.Width; window.Height = size.Height; Pump(window);
                foreach (var theme in new[] { "Professional", "Glow" })
                {
                    themeSource.Change(theme); Pump(window);
                    using var frame = window.CaptureRenderedFrame();
                    (frame ?? throw new Exception("Missing Builder frame")).Save(Path.Combine(evidenceDirectory, $"fixture-builder-{mode}-{size.Width}-{theme}.png"));
                    foreach (var id in new[] { "builder-basic", "builder-advanced", "builder-expert", "builder-save", "builder-refresh", "builder-discard" })
                    {
                        var control = Find<Control>(root, id);
                        var point = control.TranslatePoint(default, root) ?? throw new Exception("Detached " + id);
                        Check(control.Bounds.Width > 0 && control.Bounds.Height > 0, "Builder measured " + id);
                        Check(point.X >= -1 && point.X + control.Bounds.Width <= root.Bounds.Width + 1, "Builder horizontal reachability " + id);
                        Check(point.Y >= -1 && point.Y + control.Bounds.Height <= root.Bounds.Height + 1, "Builder vertical reachability " + id);
                    }
                }
            }
        }
        owner.Set("builder.expert", false); owner.Set("builder.filesVisible", false); owner.Set("builder.basic", true); owner.Set("builder.documentVisible", true); Pump(window);
        var input = Find<TextBox>(root, "builder-name"); input.Focus(); input.SelectAll(); window.KeyTextInput("Fixture name edit");
        Check((string?)owner.Values["builder.name"] == "Fixture name edit", "Builder actual name input writes owner");
        Find<Button>(root, "builder-save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
        Check(owner.Actions.Last().Command == "builder.save", "Builder Save invokes its owner command");
        Console.WriteLine(JsonSerializer.Serialize(new { surface = "Space Builder", canonicalCompiler = "PASS", rendered = "PASS", fixtureOwners = true, platform = "headless Skia", productAcceptance = "NOT RUN" }));
    }
    finally { window.Close(); loader.Dispose(); await loader.WhenActionsIdleAsync(); }
}

public sealed class SpacesRenderApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<SpacesRenderApplication>().UseSkia())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Spaces");
}
internal sealed class FixtureOwner : ICuiWritableBindingContext, ICuiActionDispatcher, INotifyPropertyChanged
{
    public Dictionary<string, object?> Values { get; } = new(StringComparer.Ordinal);
    public List<(string Command, object? Parameter)> Actions { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Set(string path, object? value) { Values[path] = value; PropertyChanged?.Invoke(this, new(path)); }
    public bool TryGetValue(string path, out object? value) => Values.TryGetValue(path, out value);
    public bool TrySetValue(string path, object? value) { if (!Values.ContainsKey(path)) return false; Set(path, value); return true; }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); Actions.Add((command, parameter)); return ValueTask.CompletedTask; }
}
internal sealed class ThemeSource : ICuiThemePreferenceSource
{
    public CuiThemePreferenceSnapshot Current { get; private set; } = new("Professional", 1, "render-fixture", false, CuiAccessibilitySettings.Default);
    public event EventHandler? Changed;
    public void Change(string name) { Current = Current with { Name = name, Revision = Current.Revision + 1 }; Changed?.Invoke(this, EventArgs.Empty); }
}
internal static class Fixtures
{
    public const string MessageId = "message-fixture-1";
    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] fields) => fields.ToDictionary(field => field.Key, field => field.Value);
    public static Dictionary<string, object?> Space(string id, string name) => Row(("id", id), ("name", name), ("summary", "Explicit test fixture"), ("isCurrent", id == "space-a"), ("isActive", true), ("isArchived", false));
    public static Dictionary<string, object?> Message(string content) => Row(("Id", MessageId), ("SpeakerLabel", "Test assistant"), ("CreatedLabel", "Fixture timestamp"), ("Content", content), ("Attachments", Array.Empty<object>()), ("Citations", Array.Empty<object>()), ("ToolActivities", Array.Empty<object>()), ("CanRegenerate", true));
    public static FixtureOwner Create(bool chat)
    {
        var owner = new FixtureOwner();
        foreach (var (path, value) in new (string, object?)[] {
            ("layout.columns", "280 1fr"), ("layout.sidebarVisible", true), ("layout.sidebarHidden", false), ("status.message", "Explicit test fixture; no service connected"),
            ("spaces.query", ""), ("spaces.showArchived", false),
            ("editor.visible", false), ("editor.title", "Edit Space fixture"), ("editor.name", "Alpha"), ("editor.description", "Fixture description"), ("editor.error", ""), ("editor.canConfirm", true), ("spaces.builtIns", new[] { Space("space-chat", "Chat"), Space("space-study", "Study") }),
            ("spaces.customSpaces", new[] { Space("space-a", "Alpha"), Space("space-b", "Beta") }),
            ("selection.id", "space-a"), ("selection.canOpen", false), ("selection.unavailableReason", "Workspace owner not connected in fixture"),
            ("selection.name", "Alpha"), ("selection.description", "Fixture Space for canonical interface checks"),
            ("selection.contextReferences", new[] { Row(("id", "source-a"), ("label", "Fixture document"), ("permissionLabel", "Read only"), ("indexStateLabel", "Fixture index state")) }),
            ("selection.canEdit", true), ("selection.effectiveModelLabel", "Model unavailable in fixture"), ("selection.webAccessLabel", "No web provider"), ("selection.memoryScopeLabel", "Fixture context"),
            ("selection.capabilities", new[] { Row(("id", "cap-a"), ("label", "Fixture capability label")) }),
            ("recentQuery", ""), ("recentChats", new[] { Row(("Id", "recent-a"), ("Title", "Fixture conversation"), ("UpdatedLabel", "Fixture timestamp"), ("IsCurrent", true)) }),
            ("conversation.title", "Fixture conversation"), ("conversation.currentBranchId", "branch-a"), ("conversation.selectedBranchIndex", 0),
            ("conversation.messages", new[] { Message("A **fixture** response with real Markdown.\n\n```text\nFixture code\n```") }),
            ("composer.selectedModelName", "Fixture model"), ("composer.selectedModelIndex", 0), ("composer.attachments", Array.Empty<object>()), ("composer.draft", ""), ("composer.canSend", true),
            ("modelLabels", new[] { "Fixture model", "Other fixture model" }), ("branchLabels", new[] { "Main", "Fixture branch" })
        }) owner.Values[path] = value;
        return owner;
    }
}
