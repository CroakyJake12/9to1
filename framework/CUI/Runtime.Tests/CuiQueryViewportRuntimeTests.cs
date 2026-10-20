using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using System.ComponentModel;
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

    private sealed class NotifyingHost : ICuiWritableBindingContext, INotifyPropertyChanged
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Set(string path, object? value) => _values[path] = value;
        public void SetNotify(string path, object? value)
        {
            _values[path] = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(path));
        }
        public bool TryGetValue(string path, out object? value) => _values.TryGetValue(path, out value);
        public bool TrySetValue(string path, object? value) { _values[path] = value; return true; }
    }

    /// <summary>A typed source that swaps the host source to <paramref name="next"/> on its first
    /// evaluation, synchronously, to deterministically force a nested replacement while the outer
    /// evaluation is still in flight.</summary>
    private sealed class ReplacingSource : ICuiPagedSource
    {
        private readonly NotifyingHost _host;
        private readonly ICuiPagedSource _next;
        private bool _swapped;
        private int _evaluations;
        public int Evaluations => _evaluations;
        public ReplacingSource(NotifyingHost host, ICuiPagedSource next) { _host = host; _next = next; }
        public CuiPagedResult Evaluate(string? query, CuiPageCursor? cursor)
        {
            _evaluations++;
            if (!_swapped)
            {
                _swapped = true;
                _host.SetNotify("Source", _next);
            }
            return new CuiPagedResult(new object[] { $"tmp{_evaluations}" }, 1, 0, null, null);
        }
    }

    /// <summary>A typed paged source whose contents can be revised in place. <see cref="Revision"/>
    /// is an opaque stamp incremented on every in-place update, giving the runtime an external
    /// invalidation path to re-evaluate the CURRENT source without replacing it.</summary>
    private sealed class MutableListSource : ICuiMutablePagedSource
    {
        private List<string> _data;
        public int PageSize { get; set; } = 3;
        public int Revision { get; private set; }
        public int EvaluateCount { get; private set; }
        public MutableListSource(params string[] data) => _data = data.ToList();
        public void Update(params string[] data) { _data = data.ToList(); Revision++; }
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

    /// <summary>A typed source that, on the first evaluation for a specific query, synchronously
    /// changes the QUERY on the same viewport (a same-source query reentry). A newer inner result is
    /// published while the outer evaluation is still in flight; the outer stale window must never
    /// overwrite it.</summary>
    private sealed class ReentrantQuerySource : ICuiPagedSource
    {
        private readonly string[] _data;
        private bool _flipped;
        public CuiQueryViewport? Viewport { get; set; }
        public int EvaluateCount { get; private set; }
        public ReentrantQuerySource(params string[] data) => _data = data;
        public CuiPagedResult Evaluate(string? query, CuiPageCursor? cursor)
        {
            EvaluateCount++;
            if (!_flipped && string.Equals(query, "zzz", StringComparison.Ordinal))
            {
                _flipped = true;
                Viewport?.TrySetValue("Query", "new");
            }
            var filtered = string.IsNullOrEmpty(query)
                ? _data
                : _data.Where(item => item.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            return new CuiPagedResult(filtered.Select(item => (object)item).ToList(), filtered.Length, 0, null, null);
        }
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
                      <Container id="g1" Type="Wrap"><Repeat id="r1" Source="{Binding Items}" As="it" Key="{Binding it}"><Button id="cellA" content="{Binding it}"/></Repeat></Container>
                      <Button id="n1" action="next" content="›" IsVisible="{Binding HasNext}"/>
                    </Container>
                    <Container id="q2" Type="QueryViewport" Source="{Binding SourceB}">
                      <Container id="g2" Type="Wrap"><Repeat id="r2" Source="{Binding Items}" As="it" Key="{Binding it}"><Button id="cellB" content="{Binding it}"/></Repeat></Container>
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
                    .Where(b => b.Name is "cellA" or "cellB").Select(b => (string?)b.Content).ToArray();

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

    [Fact]
    public async Task Same_source_repeated_binding_application_does_not_recursively_evaluate()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var host = new HostContext();
            var source = new PagedListSource("0", "1", "2");
            host.Set("Source", source);
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(host);
            var (root, diagnostics) = loader.LoadMarkup(PagerMarkup);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Content = root, Width = 400, Height = 340 };
            window.Show(); Pump();
            try
            {
                Assert.Equal(new[] { "0", "1", "2" }, Cells(root!));
                var before = source.EvaluateCount;

                // Re-applying the SAME typed source reference through the ordinary host refresh path
                // must be idempotent: no re-evaluation, no re-publication feedback, no recursion.
                loader.RefreshBindings(); Pump();
                loader.RefreshBindings(); Pump();
                loader.RefreshBindings(); Pump();

                Assert.Equal(before, source.EvaluateCount);
                Assert.Equal(new[] { "0", "1", "2" }, Cells(root!));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Reentrant_source_replacement_during_refresh_settles_boundedly_without_stale_publication()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var host = new NotifyingHost();
            var sourceA = new PagedListSource("a0", "a1", "a2");
            var sourceC = new PagedListSource("c0", "c1", "c2");
            var replacing = new ReplacingSource(host, sourceC);
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
                Assert.Equal(new[] { "a0", "a1", "a2" }, Cells(root!));

                // Replace with a source that swaps the host source to C DURING its own evaluation.
                // The nested replacement must settle boundedly (each source evaluated exactly once)
                // and the final window must be C's, with no stale publication from the interrupted
                // intermediate producer.
                host.SetNotify("Source", replacing);
                loader.RefreshBindings(); Pump();

                Assert.Equal(new[] { "c0", "c1", "c2" }, Cells(root!));
                Assert.Equal(1, replacing.Evaluations);
                Assert.Equal(1, sourceC.EvaluateCount);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Query_pager_and_explicit_same_port_source_update_remain_functional_after_live_replacement()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var host = new HostContext();
            var sourceA = new PagedListSource("0", "1", "2");
            var sourceB = new PagedListSource("10", "11", "12", "13", "14");
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
                Assert.Equal(new[] { "0", "1", "2" }, Cells(root!));
                var next = Find<Button>(root!, "next")!;
                var search = Find<TextBox>(root!, "search");

                // Next begins HIDDEN for A (3 items, no second page). Its authored owner/handler
                // must already be truthfully wired at mount, so it remains functional when B makes
                // paging available without re-finding/recreating it. This is the exact captured
                // button identity exercised below.
                Assert.False(next.IsVisible);
                Assert.True(loader.Inspect(next)!.ActionsWired);

                // Explicit same-port external update (live source replacement).
                host.Set("Source", sourceB);
                loader.RefreshBindings(); Pump();
                Assert.Equal(new[] { "10", "11", "12" }, Cells(root!));

                // Pager movement on the NEW source.
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync(); Pump();
                Assert.Equal(new[] { "13", "14" }, Cells(root!));

                // Query filtering on the NEW source.
                search!.Text = "14"; Pump();
                Assert.Equal(new[] { "14" }, Cells(root!));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task No_global_dispatcher_authored_next_previous_button_clicks_work_before_and_after_reload()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var host = new HostContext();
            host.Set("Source", new PagedListSource("0", "1", "2", "3"));
            // No SetActionDispatcher: pager actions must resolve through the owning QueryViewport scope.
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(host);
            var (root1, diagnostics1) = loader.LoadMarkup(PagerMarkup);
            Assert.Empty(diagnostics1);
            loader.WireBindings(root1!);
            var window = new Window { Content = root1, Width = 400, Height = 340 };
            window.Show(); Pump();
            try
            {
                // BEFORE reload: a real authored Next click dispatches through the scoped viewport.
                var prev = Find<Button>(root1!, "prev")!;
                var next = Find<Button>(root1!, "next")!;
                Assert.False(prev.IsVisible);
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync(); Pump();
                Assert.Equal(new[] { "3" }, Cells(root1!));

                // RELOAD into a new frame generation on the same loader.
                var (root2, diagnostics2) = loader.LoadMarkup(PagerMarkup);
                Assert.Empty(diagnostics2);
                loader.WireBindings(root2!);
                window.Content = root2; Pump();
                var prev2 = Find<Button>(root2!, "prev")!;
                var next2 = Find<Button>(root2!, "next")!;
                Assert.False(prev2.IsVisible);
                // The reloaded Prev starts hidden/detached; it must already be wired with a valid
                // authored owner, so it keeps working once the Next click makes it visible.
                Assert.True(loader.Inspect(prev2)!.ActionsWired);
                next2.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync(); Pump();
                Assert.Equal(new[] { "3" }, Cells(root2!));
                prev2.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync(); Pump();
                Assert.Equal(new[] { "0", "1", "2" }, Cells(root2!));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Same_port_mutable_content_revision_re_evaluates_current_source_without_replacement()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var host = new HostContext();
            var source = new MutableListSource("0", "1", "2");
            host.Set("Source", source);
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(host);
            var (root, diagnostics) = loader.LoadMarkup(PagerMarkup);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Content = root, Width = 400, Height = 340 };
            window.Show(); Pump();
            try
            {
                Assert.Equal(new[] { "0", "1", "2" }, Cells(root!));
                var before = source.EvaluateCount;

                // In-place content revision on the SAME source reference, then the loader's ordinary
                // refresh path (no source replacement, no host notification needed). This must
                // re-evaluate the current source exactly once and publish the revised window without
                // producer feedback or duplicate evaluation.
                source.Update("a", "b", "c", "d", "e");
                loader.RefreshBindings(); Pump();

                Assert.Equal(new[] { "a", "b", "c" }, Cells(root!));
                Assert.Equal("5", Find<TextBlock>(root!, "total")!.Text);
                Assert.Equal(before + 1, source.EvaluateCount);

                // A repeated refresh with an unchanged revision stays idempotent.
                loader.RefreshBindings(); Pump();
                Assert.Equal(before + 1, source.EvaluateCount);
                Assert.Equal(new[] { "a", "b", "c" }, Cells(root!));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Same_source_query_reentry_during_evaluate_publishes_newer_window_not_stale_outer()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var host = new HostContext();
            var source = new ReentrantQuerySource("alpha", "beta", "new1", "new2");
            host.Set("Source", source);
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(host);
            var (root, diagnostics) = loader.LoadMarkup(PagerMarkup);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Content = root, Width = 400, Height = 340 };
            window.Show(); Pump();
            try
            {
                Assert.Equal(new[] { "alpha", "beta", "new1", "new2" }, Cells(root!));

                // Arm the source with the real owning viewport so its evaluation can reenter the
                // same viewport's query while an evaluation is still in flight.
                source.Viewport = Find<CuiQueryViewport>(root!, "q1");

                // A genuine authored query change drives a fresh evaluation. The source reacts by
                // changing the query again DURING that evaluation, so a newer inner window is
                // published first. The stale outer window (the empty "zzz" result) must not
                // overwrite the newer inner publication.
                var search = Find<TextBox>(root!, "search")!;
                search.Text = "zzz"; Pump();

                Assert.Equal(new[] { "new1", "new2" }, Cells(root!));
                Assert.Equal(3, source.EvaluateCount); // "" (mount), then "zzz", then nested "new"
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Unrelated_missing_handler_remains_truthful()
    {
        const string MissingHandlerMarkup = """
            <Cui>
              <Actions><Action name="ghost" command="not-a-real-command" /></Actions>
              <Page><Container id="root" Type="Vertical Stack">
                <Button id="ghost" action="ghost" content="Ghost" />
              </Container></Page>
            </Cui>
            """;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var loader = new CuiControlLoader();
            loader.SetBindingContext(new HostContext());
            var (root, diagnostics) = loader.LoadMarkup(MissingHandlerMarkup);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Content = root, Width = 200, Height = 100 };
            window.Show(); Pump();
            try
            {
                var ghost = Find<Button>(root!, "ghost")!;
                // No global dispatcher, no owning QueryViewport scope, not a local toggle: the
                // action is genuinely unwired and remains truthful (never broadly accepted).
                Assert.False(loader.Inspect(ghost)!.ActionsWired);

                CuiActionFailure? failure = null;
                loader.ActionFailed += (_, f) => failure = f;
                ghost.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync(); Pump();
                Assert.Null(failure);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }
}
