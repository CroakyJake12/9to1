using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

// This is a real source-referenced CUI layout regression, not a browser, service,
// authenticated Home, or deployed acceptance test. The input must be supplied
// explicitly, so the same unchanged assertions can test an owner proposal.
if (args.Length != 1) throw new ArgumentException("Supply the exact Home.cui path.");
var sourcePath = Path.GetFullPath(args[0]);
var source = File.ReadAllText(sourcePath);
await using var session = HeadlessUnitTestSession.StartNew(typeof(HomeLayoutApplication));
var failed = await session.Dispatch(() =>
{
    var parser = new CuiRichParser();
    var document = parser.Parse(source, sourcePath);
    if (parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
        throw new InvalidOperationException("Actual Home CUI did not parse.");
    var bindings = new CuiViewModel();
    bindings.Set("IsDashboard", true);
    bindings.Set("IsLibrary", false);
    bindings.Set("IsEvents", false);
    using var loader = new CuiControlLoader();
    loader.SetBindingContext(bindings);
    loader.SetActionDispatcher(bindings);
    var (root, diagnostics) = loader.TryLoad(document);
    if (root is null || diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
        throw new InvalidOperationException("Actual Home CUI did not lower.");
    loader.WireBindings(root);
    var scroll = new ScrollViewer { Content = root };
    var window = new Window { Width = 1440, Height = 954, Content = scroll };
    window.Show();
    try
    {
        // Force the actual pinned layout/render pipeline to create templates and
        // measure font content before comparing real control coordinates.
        using var frame = window.CaptureRenderedFrame();
        if (frame is null) throw new InvalidOperationException("No rendered frame.");
        var order = new[] { "welcome-heading", "welcome-copy", "dashboard-brief", "home-hero",
            "operation-status", "navigation-status", "app-shortcuts", "upcoming-events", "dashboard-layout-controls" };
        var controls = root.GetVisualDescendants().OfType<Control>().Prepend(root)
            .Where(c => c.Name is not null && order.Contains(c.Name, StringComparer.Ordinal))
            .ToDictionary(c => c.Name!, StringComparer.Ordinal);
        var bounds = order.Select(id =>
        {
            var control = controls[id];
            var origin = control.TranslatePoint(default, root)
                ?? throw new InvalidOperationException($"Detached control: {id}");
            return new { id, x = origin.X, y = origin.Y, width = control.Bounds.Width, height = control.Bounds.Height };
        }).ToArray();
        var checks = bounds.Zip(bounds.Skip(1), (before, after) => new
        {
            name = $"{after.id} follows {before.id}",
            passed = after.y + 0.01 >= before.y + before.height,
            beforeBottom = before.y + before.height,
            afterTop = after.y
        }).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new { sourcePath, bounds, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Count(check => !check.passed);
    }
    finally { window.Close(); }
}, CancellationToken.None);
if (failed != 0) throw new InvalidOperationException($"Home sequential layout failed {failed} assertions.");
Console.WriteLine("PASS: actual Home dashboard sections have distinct sequential bounds.");

public sealed class HomeLayoutApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<HomeLayoutApplication>().UseSkia())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Home");
}
