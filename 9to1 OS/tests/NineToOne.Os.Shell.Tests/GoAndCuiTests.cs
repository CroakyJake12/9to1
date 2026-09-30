using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CakeOS.Cui.Runtime;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using NineToOne.Os.Shell;

namespace NineToOne.Os.Shell.Tests;

public sealed class GoAndCuiTests
{
    [Fact]
    public async Task LocalResultsStreamBeforeSlowProviderAndFailureIsIsolated()
    {
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fast = new Provider("fast", null); var slow = new Provider("slow", blocked.Task); var failure = new Provider("failed", Task.FromException(new IOException("provider failure")));
        var service = new GoService([slow, failure, fast], TimeSpan.FromMilliseconds(300));
        var updates = new List<GoUpdate>();
        await foreach (var update in service.QueryAsync(new(""))) updates.Add(update);
        Assert.Single(updates, u => u.Result?.ProviderId == "fast");
        Assert.Contains(updates, u => u.ProviderId == "failed" && u.Failure is not null);
        Assert.Contains(updates, u => u.ProviderId == "slow" && u.Failure == "Provider timed out");
        Assert.DoesNotContain(updates, u => u.ProviderId == "slow" && u.Result is not null);
        blocked.SetResult();
    }
    [Fact]
    public async Task GoActionDispatchesOriginalOwnerReferenceAndRejectsUndeclaredAction()
    {
        var provider = new Provider("owner", null); var service = new GoService([provider]); GoResult? result = null;
        await foreach (var update in service.QueryAsync(new(""))) if (update.Result is { } item) result = item;
        await service.InvokeAsync(result!, "Open"); Assert.Same(result!.Reference, provider.Invoked);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.InvokeAsync(result, "Delete"));
    }
    [Theory]
    [InlineData("[Desktop Entry]\nType=Application\nName=App\nExec=realapp %U", true)]
    [InlineData("[Desktop Entry]\nType=Application\nName=App\nExec=realapp\nHidden=true", false)]
    [InlineData("[Desktop Entry]\nType=Application\nName=App\nName=Ambiguous\nExec=realapp", false)]
    [InlineData("[Desktop Entry]\nType=Link\nName=App\nExec=realapp", false)]
    public void DesktopEntryParserRejectsMaskedAmbiguousAndNonApplicationEntries(string source, bool expected)
    { Assert.Equal(expected, LinuxInstalledApplications.Parse("app.desktop", "/apps/app.desktop", source, "digest") is not null); }
    [Fact]
    public async Task ActualShellCuiRendersAndRepeatOpenActionKeepsCanonicalResultParameter()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(TestApplication));
        await session.Dispatch(() =>
        {
            using var stream = typeof(ShellConfiguration).Assembly.GetManifestResourceStream("NineToOne.Os.Shell.UI.Shell.cui");
            using var reader = new StreamReader(stream!);
            using var bindings = new ShellViewModel();
            var app = new GoResult("test", new("Home", "os.installed-application", Guid.NewGuid().ToString(), "1"), "Native app", "Apps", [new("Open", "Open")]);
            var otherApp = app with { Reference = app.Reference with { Id = Guid.NewGuid().ToString() }, Label = "Other native app" };
            bindings.TrySetValue("Results", new[] { app, otherApp });
            bindings.TrySetValue("Items", new[] { new TaskbarItem(Guid.NewGuid(), TaskbarItemKind.Widget, "Unavailable widget", new("Owner", "widget", "widget-id")) });
            var dispatcher = new Recorder();
            using var loader = new CuiControlLoader(); loader.SetBindingContext(bindings); loader.SetActionDispatcher(dispatcher);
            var (root, diagnostics) = loader.LoadMarkup(reader.ReadToEnd());
            Assert.True(root is not null, string.Join("\n", diagnostics)); Assert.DoesNotContain(diagnostics, d => d.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            loader.WireBindings(root!);
            var queryInput = Traverse(root!).OfType<TextBox>().Single(t => t.Name == "go-query");
            queryInput.Text = "typed native query"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(bindings.TryGetValue("Query", out var typed)); Assert.Equal("typed native query", typed);
            foreach (var input in Traverse(root!).OfType<TextBox>().Where(t => t != queryInput))
            { input.Text = "73"; Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
            foreach (var field in new[] { "Name", "Thickness", "Spacing", "Padding", "Radius", "Opacity" })
            { Assert.True(bindings.TryGetValue(field, out var value)); Assert.Equal("73", value); }
            var button = Traverse(root!).OfType<Button>().Single(b => Equals(b.Content, "Native app"));
            var otherButton = Traverse(root!).OfType<Button>().Single(b => Equals(b.Content, "Other native app"));
            Assert.False(Traverse(root!).OfType<Button>().Single(b => Equals(b.Content, "Unavailable widget")).IsEnabled);
            var window = new Window { Width = 1100, Height = 900, Content = root }; window.Show(); window.UpdateLayout();
            var firstPosition = button.TranslatePoint(default, root!); var secondPosition = otherButton.TranslatePoint(default, root!);
            Assert.NotNull(firstPosition); Assert.NotNull(secondPosition);
            Assert.True(secondPosition!.Value.Y >= firstPosition!.Value.Y + button.Bounds.Height, "Repeated Go app rows must lay out without overlap.");
            Assert.True(button.Bounds.Height > 0, "Native primitive theme templates must give buttons a usable height.");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Open", dispatcher.Command); Assert.Same(app, dispatcher.Parameter);
            otherButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Same(otherApp, dispatcher.Parameter);
            window.Close();
        }, default);
    }
    private static IEnumerable<Control> Traverse(Control root)
    {
        yield return root;
        IEnumerable<Control> children = root switch { Panel p => p.Children, Decorator d when d.Child is not null => [d.Child], ContentControl c when c.Content is Control child => [child], _ => [] };
        foreach (var child in children) foreach (var descendant in Traverse(child)) yield return descendant;
    }
    public sealed class TestApplication : Avalonia.Application
    {
        public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this);
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
    private sealed class Recorder : ICuiActionDispatcher
    {
        public string? Command; public object? Parameter;
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken ct = default) { Command = command; Parameter = parameter; return ValueTask.CompletedTask; }
    }
    private sealed class Provider(string id, Task? barrier) : IGoProvider
    {
        public string ProviderId => id; public GoCanonicalReference? Invoked;
        public async IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, [EnumeratorCancellation] CancellationToken ct)
        {
            if (barrier is not null) await barrier; // deliberately ignores cancellation to verify isolation.
            yield return new(id, new(id, "entity", "canonical-id", "revision"), "Local result", "Apps", [new("Open", "Open")]);
        }
        public Task InvokeAsync(GoCanonicalReference reference, string action, CancellationToken ct) { Invoked = reference; return Task.CompletedTask; }
    }
}
