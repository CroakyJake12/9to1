using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using NineToOne.Os.Shell;

namespace NineToOne.Os.Shell.Tests;

[Collection("Native CUI")]
public sealed class GoKeyboardTests
{
    [Fact]
    public async Task ActualShellQueryEnterUsesBoundInputAndSameSearchCommand()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        await session.Dispatch(() =>
        {
            using var stream = typeof(ShellConfiguration).Assembly.GetManifestResourceStream("NineToOne.Os.Shell.UI.Shell.cui");
            using var reader = new StreamReader(stream!); using var model = new ShellViewModel();
            var actions = new Recorder(model); using var loader = new CuiControlLoader(TaskbarLayerSurface.CreateRegistry(actions));
            loader.SetBindingContext(model); loader.SetActionDispatcher(actions);
            var (root, diagnostics) = loader.LoadMarkup(reader.ReadToEnd());
            Assert.NotNull(root); Assert.DoesNotContain(diagnostics, d => d.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            loader.WireBindings(root!); var window = new Window { Content = root }; window.Show();
            try
            {
                var query = Assert.IsType<GoSearchInput>(Traverse(root!).OfType<TextBox>().Single(t => t.Name == "go-query"));
                query.Text = "keyboard submitted query"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                query.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Control });
                Assert.Empty(actions.Submitted);
                query.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                Assert.Equal("keyboard submitted query", Assert.Single(actions.Submitted));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    [Fact]
    public async Task NewEnterCancelsOlderSubmissionAndDetachCancelsOwningInputLifetime()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        await session.Dispatch(() =>
        {
            var actions = new PendingRecorder(); var input = new GoSearchInput(actions); var window = new Window { Content = input }; window.Show();
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Single(actions.Tokens); Assert.False(actions.Tokens[0].IsCancellationRequested);
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(2, actions.Tokens.Count); Assert.True(actions.Tokens[0].IsCancellationRequested);
            window.Content = null; Assert.True(actions.Tokens[1].IsCancellationRequested); window.Close();
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(2, actions.Tokens.Count);
        }, CancellationToken.None);
    }
    private static IEnumerable<Control> Traverse(Control root)
    {
        yield return root;
        foreach (var child in Avalonia.LogicalTree.LogicalExtensions.GetLogicalChildren(root).OfType<Control>())
            foreach (var descendant in Traverse(child)) yield return descendant;
    }
    private sealed class Recorder(ShellViewModel bindings) : ICuiActionDispatcher
    {
        public List<string> Submitted { get; } = [];
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken ct = default)
        { Assert.Equal("Search", command); Assert.Null(parameter); Assert.True(bindings.TryGetValue("Query", out var text)); Submitted.Add(text?.ToString() ?? ""); return ValueTask.CompletedTask; }
    }
    private sealed class PendingRecorder : ICuiActionDispatcher
    {
        public List<CancellationToken> Tokens { get; } = [];
        public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken ct = default)
        { Assert.Equal("Search", command); Tokens.Add(ct); await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
    }
}
