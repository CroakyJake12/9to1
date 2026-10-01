using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application.Go;
using NineToOne.Os.Shell;

namespace NineToOne.Os.Shell.Tests;

[Collection("Native CUI")]
public sealed class GoDisplayNativeTests
{
    [Fact]
    public async Task ActualGoCuiKeepsCollidingIdsAsDistinctCanonicalRowsAndParameters()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        await session.Dispatch(() =>
        {
            var file = new GoResult("provider", new("Files", "file", "same-id", "1"), "File row", "Files", [new("Open", "Open")]);
            var project = file with { Label = "Project row", Reference = file.Reference with { Owner = "Projects", Kind = "project" } };
            var other = file with { Label = "Other provider row", ProviderId = "other-provider" };
            using var bindings = new ShellViewModel(); bindings.TrySetValue("Results", new[] { file, project, other });
            var dispatcher = new Recorder(); using var loader = new CuiControlLoader(TaskbarLayerSurface.CreateRegistry(dispatcher));
            loader.SetBindingContext(bindings); loader.SetActionDispatcher(dispatcher);
            using var stream = typeof(ShellConfiguration).Assembly.GetManifestResourceStream("NineToOne.Os.Shell.UI.Shell.cui");
            using var reader = new StreamReader(stream!); var (root, diagnostics) = loader.LoadMarkup(reader.ReadToEnd());
            Assert.NotNull(root); Assert.DoesNotContain(diagnostics, d => d.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            loader.WireBindings(root!);
            foreach (var canonical in new[] { file, project, other })
            {
                var button = Assert.Single(Traverse(root!).OfType<Button>(), b => Equals(b.Content, canonical.Label));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("Open", dispatcher.Command); Assert.Same(canonical, dispatcher.Parameter);
            }
        }, CancellationToken.None);
    }
    private static IEnumerable<Control> Traverse(Control root)
    {
        yield return root;
        foreach (var child in root.GetLogicalChildren().OfType<Control>())
            foreach (var descendant in Traverse(child)) yield return descendant;
    }
    private sealed class Recorder : ICuiActionDispatcher
    {
        public string? Command; public object? Parameter;
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken ct = default)
        { Command = command; Parameter = parameter; return ValueTask.CompletedTask; }
    }
}
