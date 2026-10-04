using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Infrastructure;
using Haven.UI;
using NineToOne.Web.Write;

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_DATA_DIR")))
    throw new ArgumentException("Set a fresh isolated HAVEN_DATA_DIR.");
await using var session = HeadlessUnitTestSession.StartNew(typeof(WriteSpaceApplication));
await session.Dispatch(async () =>
{
    var checks = 0;
    void Check(string name, bool condition)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("PASS: " + name); ++checks;
    }
    using var host = new WriteRetainedSceneControl(() => true);
    var scene = (HavenSceneControl)typeof(WriteRetainedSceneControl).GetField("_scene", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
    var window = new Window { Content = host, Width = 1100, Height = 850 };
    var inputRoot = (IInputRoot)typeof(TopLevel).GetProperty("InputRoot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
    var keyboard = AvaloniaLocator.Current.GetRequiredService<IKeyboardDevice>();
    var input = AvaloniaLocator.Current.GetRequiredService<IInputManager>();
    ulong timestamp = 0;
    var observations = new List<object>();
    (bool Down, bool Up, bool Pressed, bool Text) RawStroke(Key key, PhysicalKey physical, string symbol, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var down = new RawKeyEventArgs(keyboard, ++timestamp, inputRoot, RawKeyEventType.KeyDown, key, modifiers, physical, symbol);
        input.ProcessInput(down);
        var pressed = scene.Root!.State.HasFlag(HavenElementState.Pressed);
        // Exact owning BrowserInputHandler.OnKeyDown admission condition. Native
        // event routing/scene/router/editor are genuine; browser class is not run.
        var emitted = !down.Handled && symbol.Length == 1;
        if (emitted) input.ProcessInput(new RawTextInputEventArgs(keyboard, ++timestamp, inputRoot, symbol));
        var up = new RawKeyEventArgs(keyboard, ++timestamp, inputRoot, RawKeyEventType.KeyUp, key, modifiers, physical, symbol);
        input.ProcessInput(up);
        observations.Add(new { key = key.ToString(), symbol, downHandled = down.Handled, upHandled = up.Handled,
            pressedAfterDown = pressed, textEventEmitted = emitted, pressedAfterUp = scene.Root.State.HasFlag(HavenElementState.Pressed) });
        return (down.Handled, up.Handled, pressed, emitted);
    }
    host.SetInputAllowed(true);
    window.Show();
    try
    {
        // Genuine existing button retains its own pressed/invoke ownership.
        var button = new Haven.UI.Components.Button { Content = "Actual button" };
        var invokes = 0; button.Invoked += (_, _) => ++invokes;
        scene.Root = button; window.UpdateLayout();
        Check("Actual native Button receives focus", scene.FocusElement(button));
        var buttonSpace = RawStroke(Key.Space, PhysicalKey.Space, " ");
        Check("Actual Button Space invokes once and consumes key without text", buttonSpace.Down && buttonSpace.Up && buttonSpace.Pressed && !buttonSpace.Text && invokes == 1);
        RawStroke(Key.Enter, PhysicalKey.Enter, "Enter");
        Check("Actual Button Enter invokes exactly once more", invokes == 2);
        Check("Actual Button releases pressed state", !button.State.HasFlag(HavenElementState.Pressed));

        var ordinary = new Haven.UI.Components.Input { Text = "" };
        scene.Root = ordinary; window.UpdateLayout();
        Check("Actual ordinary Input receives native focus", scene.FocusElement(ordinary));
        var ordinarySpace = RawStroke(Key.Space, PhysicalKey.Space, " ");
        Check("Actual ordinary Input admits one Space text event", !ordinarySpace.Down && ordinarySpace.Text && ordinary.Text == " ");

        var consumed = new ConsumedSpaceProbe();
        scene.Root = consumed; window.UpdateLayout();
        Check("Custom owning keyboard handler receives native focus", scene.FocusElement(consumed));
        var consumedSpace = RawStroke(Key.Space, PhysicalKey.Space, " ");
        Check("Custom true Space handler retains ownership and receives no duplicate text", consumedSpace.Down && !consumedSpace.Text && consumed.Keys == 1 && consumed.Texts == 0);

        var document = NotesDocument.Create("Raw native spaced document");
        var editor = new WriteDocumentEditor(document);
        host.SetInputAllowed(true); host.SetEditor(editor);
        using (var frame = window.CaptureRenderedFrame()) Check("Actual owning Write scene renders with native resources", frame is not null);
        Check("Actual owning Write surface receives native focus", scene.FocusElement(scene.Root!));
        var spaces = new List<(bool Down, bool Up, bool Pressed, bool Text)>();
        foreach (var ch in "Alpha beta gamma")
        {
            var upper = char.ToUpperInvariant(ch).ToString();
            var stroke = ch == ' ' ? RawStroke(Key.Space, PhysicalKey.Space, " ")
                : RawStroke(Enum.Parse<Key>(upper), Enum.Parse<PhysicalKey>(upper), ch.ToString(), char.IsUpper(ch) ? RawInputModifiers.Shift : RawInputModifiers.None);
            if (ch == ' ') spaces.Add(stroke);
        }
        var first = document.Sections[0].Pages[0].Blocks[0];
        var authoredText = string.Concat(first.Runs.Select(run => run.Text));
        var paths = new AppPaths();
        var repository = new NotesRepository(paths, new NotesDocumentValidator(), new ProductionDiagnostics(paths));
        var receipt = await repository.SaveAsync(document, "Actual per-character native keyboard fixture", default);
        var reloaded = await repository.LoadAsync(document.Id, default) ?? throw new InvalidOperationException("Actual repository did not reload document.");
        var loadedBlock = reloaded.Sections[0].Pages[0].Blocks[0];
        File.WriteAllText(Path.Combine(paths.DataDirectory, "raw-key-diagnostics.json"), JsonSerializer.Serialize(new
        {
            documentId = document.Id, authoredText, savedText = string.Concat(loadedBlock.Runs.Select(run => run.Text)),
            receipt.Version, observations, spaces = spaces.Select(value => new { value.Down, value.Up, value.Pressed, value.Text }),
            uiAssembly = typeof(HavenInputRouter).Assembly.Location, sceneAssembly = typeof(WriteRetainedSceneControl).Assembly.Location
        }, new JsonSerializerOptions { WriteIndented = true }));
        Check("Actual per-character native Write input preserves all spaces", authoredText == "Alpha beta gamma");
        Check("Write Space stays unhandled and emits exactly one native text event", spaces.Count == 2 && spaces.All(value => !value.Down && !value.Up && !value.Pressed && value.Text));
        Check("Real repository preserves exact per-character text", string.Concat(loadedBlock.Runs.Select(run => run.Text)) == "Alpha beta gamma");
        Check("Real repository preserves owner document and structured identities", reloaded.Id == document.Id
            && reloaded.Sections[0].Id == document.Sections[0].Id && reloaded.Sections[0].Pages[0].Id == document.Sections[0].Pages[0].Id
            && loadedBlock.Id == first.Id && loadedBlock.Runs.Select(run => run.Id).SequenceEqual(first.Runs.Select(run => run.Id)));
        Check("Actual durable receipt matches reloaded canonical revision", receipt.Version == reloaded.Version && receipt.Version > 0);
        Console.WriteLine($"{checks} actual native raw-key/space ownership checks PASS; raw browser text-admission branch is source-reviewed fixture logic, not browser acceptance.");
    }
    finally { window.Content = null; scene.Root = null; window.Close(); }
    return checks;
}, CancellationToken.None);

public sealed class WriteSpaceApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<WriteSpaceApplication>().UseSkia())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    public override void Initialize()
    {
        CuiNativeHost.InitialisePrimitiveTheme(this, "Home");
        WriteRetainedSceneResources.Register(this);
    }
}

internal sealed class ConsumedSpaceProbe : HavenElement, IHavenKeyboardInputTarget, IHavenTextInputTarget
{
    public ConsumedSpaceProbe() { Accessibility.Focusable = true; Accessibility.Role = HavenAccessibleRole.Input; }
    public int Keys { get; private set; }
    public int Texts { get; private set; }
    public bool KeyDown(HavenKeyInput input) { if (input.Key != HavenKey.Space) return false; ++Keys; return true; }
    public bool KeyUp(HavenKeyInput input) => input.Key == HavenKey.Space;
    public bool TextInput(string? text) { ++Texts; return true; }
}
