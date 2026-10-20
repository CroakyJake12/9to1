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
/// Generic runtime evidence for the bounded paged QueryViewport scope (the shared, app-agnostic
/// runtime contract used by the chat-composer emoji picker). A non-emoji typed source and
/// unrelated binding/action names prove the contract is generic: typed source admission, query
/// reset/paging boundaries, per-instance isolation, live source replacement, authored numeric/empty
/// labels, bounded realized windows, and exact forwarding of non-pager commands to the host with
/// original fault propagation.
/// </summary>
public sealed class CuiQueryViewportRuntimeTests
{
    private const string PagerMarkup = """
        <Cui>
          <Actions>
            <Action name="go-prev" command="query.previous" />
            <Action name="go-next" command="query.next" />
            <Action name="app-do" command="do-thing" />
          </Actions>
          <Page>
            <Container id="root" Type="Vertical Stack">
              <Container id="q1" Type="QueryViewport" Source="{Binding Source}">
                <Input id="search" Type="Text" Value="{Binding Query}" />
                <Container id="grid" Type="Wrap" CellGap="2">
                  <Repeat Source="{Binding Items}" As="it" Key="{Binding it}">
                    <Button id="cell" content="{Binding it}" />
                  </Repeat>
                </Container>
                <Button id="prev" action="go-prev" content="‹" IsVisible="{Binding HasPrevious}" />
                <Button id="next" action="go-next" content="›" IsVisible="{Binding HasNext}" />
                <Text id="start" text="{Binding PageStart}" />
                <Text id="end" text="{Binding PageEnd}" />
                <Text id="total" text="{Binding TotalCount}" />
                <Text id="empty" text="{Binding IsEmpty}" />
                <Button id="app" action="app-do" content="Go" />
              </Container>
            </Container>
          </Page>
        </Cui>
        """;

    /// <summary>Typed data-only port for a plain word list. Opaque cursor tokens, bounded window,
    /// truthful totals and optional substring query filtering (used to exercise query reset).</summary>
    private sealed class PagedListSource : ICuiPagedSource
    {
        private readonly IReadOnlyList<string> _data;
        public int PageSize { get; set; } = 3;
        public int EvaluateCount { get; private set; }
        public PagedListSource(params string[] data) => _data = data;

        public CuiPagedResult Evaluate(string? query, CuiPageCursor? cursor)
        {
            EvaluateCount++;
            var all = string.IsNullOrEmpty(query)
                ? _data
                : _data.Where(item => item.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            var offset = cursor?.Offset ?? 0;
            var window = all.Skip(offset).Take(PageSize).Select(item => (object)item).ToList();
            var next = offset + window.Count < all.Count ? new CuiPageCursor(offset + window.Count) : null;
            var prev = offset > 0 ? new CuiPageCursor(Math.Max(0, offset - PageSize)) : null;
            return new CuiPagedResult(window, all.Count, offset, prev, next);
        }
    }

    private sealed class HostContext : ICuiWritableBindingContext
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
        public void Set(string path, object? value) => _values[path] = value;
        public bool TryGetValue(string path, out object? value) => _values.TryGetValue(path, out value);
        public bool TrySetValue(string path, object? value) { _values[path] = value; return true; }
    }

    private sealed class RecordingDispatcher : ICuiActionDispatcher
    {
        public List<string> Commands { get; } = [];
        public TaskCompletionSource? Held { get; set; }
        public Exception? Fault { get; set; }
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            if (Fault is not null) return ValueTask.FromException(Fault);
            if (Held is not null) return new ValueTask(Held.Task);
            return ValueTask.CompletedTask;
        }
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static T? Find<T>(Control root, string id) where T : class =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control is Control c && c.Name == id);

    [Fact]
    public async Task Generic_query_viewport_publishes_bounded_window_numeric_labels_and_empty_state()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var host = new HostContext();
            host.Set("Source", new PagedListSource("0", "1", "2", "3", "4", "5", "6", "7"));
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(host);
            var (root, diagnostics) = loader.LoadMarkup(PagerMarkup);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Content = root, Width = 400, Height = 340 };
            window.Show(); Pump();
            try
            {
                var grid = Find<Control>(root!, "grid");
                Assert.NotNull(grid);
                var cells = () => grid!.GetVisualDescendants().OfType<Button>()
                    .Where(b => b.Name == "cell").Select(b => (string?)b.Content).ToArray();

                // First window: items 0,1,2 with truthful total/range metadata.
                Assert.Equal(new[] { "0", "1", "2" }, cells());
                Assert.Equal("1", Find<TextBlock>(root!, "start")!.Text);
                Assert.Equal("3", Find<TextBlock>(root!, "end")!.Text);
                Assert.Equal("8", Find<TextBlock>(root!, "total")!.Text);
                Assert.Equal("False", Find<TextBlock>(root!, "empty")!.Text);
                Assert.False(Find<Button>(root!, "prev")!.IsVisible);
                Assert.True(Find<Button>(root!, "next")!.IsVisible);

                // Query filter with no matches presents an empty, bounded window.
                var search = Find<TextBox>(root!, "search");
                search!.Text = "zzz-none"; Pump();
                Assert.Empty(cells());
                Assert.Equal("True", Find<TextBlock>(root!, "empty")!.Text);

                // Clearing the query restores the full catalogue first window.
                search.Text = ""; Pump();
                Assert.Equal(new[] { "0", "1", "2" }, cells());
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Paging_routes_generic_commands_through_owning_viewport_and_stops_at_boundaries()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var host = new HostContext();
            host.Set("Source", new PagedListSource("0", "1", "2", "3", "4", "5", "6", "7"));
            var dispatcher = new RecordingDispatcher();
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(host); loader.SetActionDispatcher(dispatcher);
            var (root, diagnostics) = loader.LoadMarkup(PagerMarkup);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Content = root, Width = 400, Height = 340 };
            window.Show(); Pump();
            try
            {
                var grid = Find<Control>(root!, "grid");
                var cells = () => grid!.GetVisualDescendants().OfType<Button>()
                    .Where(b => b.Name == "cell").Select(b => (string?)b.Content).ToArray();
                var prev = Find<Button>(root!, "prev")!;
                var next = Find<Button>(root!, "next")!;

                Assert.False(prev.IsVisible);
                Assert.True(next.IsVisible);
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync(); Pump();
                Assert.Equal(new[] { "3", "4", "5" }, cells());
                Assert.True(prev.IsVisible); Assert.True(next.IsVisible);

                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync(); Pump();
                Assert.Equal(new[] { "6", "7" }, cells());
                Assert.True(prev.IsVisible); Assert.False(next.IsVisible);

                // Exhausted Next stays on the final page and never falls through to the host.
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync(); Pump();
                Assert.Equal(new[] { "6", "7" }, cells());
                Assert.DoesNotContain("query.next", dispatcher.Commands);

                prev.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync(); Pump();
                Assert.Equal(new[] { "3", "4", "5" }, cells());
                Assert.DoesNotContain("query.previous", dispatcher.Commands);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Two_independent_viewport_instances_isolate_sources_and_cursors()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            const string markup = """
                <Cui>
                  <Actions><Action name="next" command="query.next" /></Actions>
                  <Page><Container id="root" Type="Vertical Stack">
                    <Container id="q1" Type="QueryViewport" Source="{Binding SourceA}">
                      <Container id="g1" Type="Wrap"><Repeat Source="{Binding Items}" As="it" Key="{Binding it}"><Button id="cell" content="{Binding it}"/></Repeat></Container>
                      <Button id="n1" action="next" content="›" IsVisible="{Binding HasNext}"/>
                    </Container>
                    <Container id="q2" Type="QueryViewport" Source="{Binding SourceB}">
                      <Container id="g2" Type="Wrap"><Repeat Source="{Binding Items}" As="it" Key="{Binding it}"><Button id="cell" content="{Binding it}"/></Repeat></Container>
                      <Button id="n2" action="next" content="›" IsVisible="{Binding HasNext}"/>
                    </Container>
                  </Container></Page>
                </Cui>
                """;
            var host = new HostContext();
            var sourceA = new PagedListSource("a0", "a1", "a2", "a3");
            var sourceB = new PagedListSource("b0", "b1", "b2");
            host.Set("SourceA", sourceA); host.Set("SourceB", sourceB);
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(host);
            var (root, diagnostics) = loader.LoadMarkup(markup);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Content = root, Width = 500, Height = 400 };
            window.Show(); Pump();
            try
            {
                var cellsOf = (string id) => Find<Control>(root!, id)!.GetVisualDescendants().OfType<Button>()
                    .Where(b => b.Name == "cell").Select(b => (string?)b.Content).ToArray();

                Assert.Equal(new[] { "a0", "a1", "a2" }, cellsOf("g1"));
                Assert.Equal(new[] { "b0", "b1", "b2" }, cellsOf("g2"));

                // Advancing q1 must not alter q2's window or cursor.
                Find<Button>(root!, "n1")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync(); Pump();
                Assert.Equal(new[] { "a3" }, cellsOf("g1"));
                Assert.Equal(new[] { "b0", "b1", "b2" }, cellsOf("g2"));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Live_source_replacement_refreshes_the_bounded_window()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var host = new HostContext();
            var sourceA = new PagedListSource("0", "1", "2");
            var sourceB = new PagedListSource("x", "y", "z");
            host.Set("Source", sourceA);
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(host);
            var (root, diagnostics) = loader.LoadMarkup(PagerMarkup);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Content = root, Width = 400, Height = 340 };
            window.Show(); Pump();
            try
            {
                var grid = Find<Control>(root!, "grid");
                var cells = () => grid!.GetVisualDescendants().OfType<Button>()
                    .Where(b => b.Name == "cell").Select(b => (string?)b.Content).ToArray();

                Assert.Equal(new[] { "0", "1", "2" }, cells());
                var aBeforeSwap = sourceA.EvaluateCount;
                var bBeforeSwap = sourceB.EvaluateCount;
                Assert.Equal(0, bBeforeSwap);

                // Live source replacement through the ordinary typed binding path.
                host.Set("Source", sourceB);
                loader.RefreshBindings(); Pump();
                Assert.Equal(new[] { "x", "y", "z" }, cells());
                Assert.Equal(bBeforeSwap + 1, sourceB.EvaluateCount);
                Assert.Equal(aBeforeSwap, sourceA.EvaluateCount);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Unrelated_non_pager_action_forwards_to_host_and_fault_propagates()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var host = new HostContext();
            host.Set("Source", new PagedListSource("0", "1", "2"));
            var dispatcher = new RecordingDispatcher();
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(host); loader.SetActionDispatcher(dispatcher);
            var (root, diagnostics) = loader.LoadMarkup(PagerMarkup);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Content = root, Width = 400, Height = 340 };
            window.Show(); Pump();
            try
            {
                var app = Find<Button>(root!, "app")!;

                // Forwarding: the non-pager command reaches the host dispatcher unchanged.
                app.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync(); Pump();
                Assert.Contains("do-thing", dispatcher.Commands);

                // Fault propagation: a faulting host dispatcher surfaces as an action failure and is
                // never swallowed by the viewport. The retained pipeline is expected to fault, so
                // observe it without letting the aggregate escape the test body.
                dispatcher.Fault = new InvalidOperationException("boom");
                CuiActionFailure? failure = null;
                loader.ActionFailed += (_, f) => failure = f;
                app.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Record.ExceptionAsync(() => loader.WhenActionsIdleAsync());
                Pump();
                Assert.NotNull(failure);
                Assert.Equal("CUIA_FAILED", failure!.Code);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }
}
