using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using System.Threading;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Retirement evidence for the bounded paged QueryViewport: on a real frame reload (or loader
/// close), the old viewport is withdrawn so it cannot evaluate its old source, refresh the newer
/// frame, mutate new Items, or dispatch old host actions. The old viewport is retained as a live
/// object reference throughout, and the current frame is asserted to remain fully functional.
/// No Task.Delay polling is used; a queued/reentrant refresh is fenced by the owning frame
/// generation.
/// </summary>
public sealed class CuiQueryViewportRetirementTests
{
    private const string Markup = """
        <Cui>
          <Actions><Action name="go-prev" command="query.previous" /><Action name="go-next" command="query.next" /></Actions>
          <Page>
            <Container id="q1" Type="QueryViewport" Source="{Binding Source}">
              <Container id="grid" Type="Wrap" CellGap="2">
                <Repeat Source="{Binding Items}" As="it" Key="{Binding it}">
                  <Button id="cell" content="{Binding it}" />
                </Repeat>
              </Container>
              <Button id="prev" action="go-prev" content="‹" IsVisible="{Binding HasPrevious}" />
              <Button id="next" action="go-next" content="›" IsVisible="{Binding HasNext}" />
            </Container>
          </Page>
        </Cui>
        """;

    private sealed class PagedListSource : ICuiPagedSource
    {
        private readonly IReadOnlyList<string> _data;
        public int PageSize { get; set; } = 3;
        public int EvaluateCount { get; private set; }
        public PagedListSource(params string[] data) => _data = data;
        public CuiPagedResult Evaluate(string? query, CuiPageCursor? cursor)
        {
            EvaluateCount++;
            var offset = cursor?.Offset ?? 0;
            var window = _data.Skip(offset).Take(PageSize).Select(item => (object)item).ToList();
            var next = offset + window.Count < _data.Count ? new CuiPageCursor(offset + window.Count) : null;
            var prev = offset > 0 ? new CuiPageCursor(Math.Max(0, offset - PageSize)) : null;
            return new CuiPagedResult(window, _data.Count, offset, prev, next);
        }
    }

    private sealed class HostContext : ICuiWritableBindingContext
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
        public void Set(string path, object? value) => _values[path] = value;
        public bool TryGetValue(string path, out object? value) => _values.TryGetValue(path, out value);
        public bool TrySetValue(string path, object? value) { _values[path] = value; return true; }
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    /// <summary>All authored children of a CUI container, recursively. Hidden authored controls are
    /// legitimately retained in the authored ownership contract (they are only pruned from the live
    /// layout/visual tree, not from the container's authored child range), so this walk finds them
    /// where a visual-only lookup cannot.</summary>
    private static IEnumerable<Control> AuthoredDescendants(Control root)
    {
        // Renderer-owned panels/decorators can wrap authored containers. Follow those
        // wrappers as well so retained invisible pager buttons remain discoverable.
        IEnumerable<Control> children = root switch
        {
            CuiContainerControl container => container.AuthoredChildren,
            Panel panel => panel.Children,
            Decorator { Child: Control child } => new[] { child },
            ContentControl { Content: Control child } => new[] { child },
            _ => Array.Empty<Control>(),
        };
        foreach (var child in children)
        {
            yield return child;
            foreach (var descendant in AuthoredDescendants(child)) yield return descendant;
        }
    }

    private static T? Find<T>(Control root, string id) where T : class
    {
        foreach (var candidate in root.GetVisualDescendants().OfType<T>()
            .Concat(AuthoredDescendants(root).OfType<T>()))
            if (candidate is Control c && c.Name == id) return candidate;
        return null;
    }

    private static string[] Cells(Control root) =>
        Find<Control>(root, "grid")!.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Name == "cell").Select(b => (string?)b.Content ?? "").ToArray();

    [Fact]
    public async Task Reload_retires_old_viewport_so_it_cannot_evaluate_or_refresh_the_new_frame()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var host = new HostContext();
            var source = new PagedListSource("0", "1", "2", "3");
            var staleSource = new PagedListSource("x", "y", "z");
            host.Set("Source", source);

            using var loader = new CuiControlLoader();
            loader.SetBindingContext(host);
            var (root1, diagnostics1) = loader.LoadMarkup(Markup);
            Assert.Empty(diagnostics1);
            loader.WireBindings(root1!);

            var window = new Window { Content = root1, Width = 400, Height = 340 };
            window.Show(); Pump();
            Assert.Equal(new[] { "0", "1", "2" }, Cells(root1!));

            // Retain a strong reference to the OLD viewport across the reload.
            var oldViewport = Find<CuiQueryViewport>(root1!, "q1");
            Assert.NotNull(oldViewport);

            // Reload the SAME loader into a new frame generation; the old viewport is withdrawn.
            var (root2, diagnostics2) = loader.LoadMarkup(Markup);
            Assert.Empty(diagnostics2);
            loader.WireBindings(root2!);
            window.Content = root2; Pump();
            Assert.Equal(new[] { "0", "1", "2" }, Cells(root2!));
            var sourceEvalAfterMount = source.EvaluateCount;

            // Drive the retained old viewport through its public surface: re-arming a source and
            // dispatching a pager command. Both must be inert after withdrawal.
            oldViewport!.Source = staleSource;
            await oldViewport.DispatchAsync("query.next", null);
            Assert.Equal(0, staleSource.EvaluateCount);
            Assert.Equal(sourceEvalAfterMount, source.EvaluateCount);

            // The retired old viewport cannot mutate or refresh the new frame.
            Assert.Equal(new[] { "0", "1", "2" }, Cells(root2!));

            // The new frame remains fully functional.
            var next = Find<Button>(root2!, "next")!;
            Assert.True(next.IsVisible);
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await loader.WhenActionsIdleAsync(); Pump();
            Assert.Equal(new[] { "3" }, Cells(root2!));
            Assert.False(Find<Button>(root2!, "next")!.IsVisible);

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Loader_close_retires_old_viewport_so_it_cannot_evaluate_after_disposal()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var host = new HostContext();
            var source = new PagedListSource("0", "1", "2");
            var staleSource = new PagedListSource("x", "y");
            host.Set("Source", source);

            var loader = new CuiControlLoader();
            loader.SetBindingContext(host);
            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);

            var window = new Window { Content = root, Width = 400, Height = 340 };
            window.Show(); Pump();

            var oldViewport = Find<CuiQueryViewport>(root!, "q1");
            Assert.NotNull(oldViewport);
            var sourceEvalBefore = source.EvaluateCount;

            // Close: dispose the loader while the viewport is still retained.
            loader.Dispose();
            await loader.WhenActionsIdleAsync(); Pump();

            // Re-arming a source and dispatching a pager command on the retained old viewport must
            // be inert after withdrawal.
            oldViewport!.Source = staleSource;
            await oldViewport.DispatchAsync("query.next", null);
            Assert.Equal(0, staleSource.EvaluateCount);
            Assert.Equal(sourceEvalBefore, source.EvaluateCount);

            window.Close();
            return true;
        }, CancellationToken.None);
    }
}
