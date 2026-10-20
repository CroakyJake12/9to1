using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;
using System.Threading;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Focused regressions for the source-driven choose-once (Selection="RandomOnce") local variable.
/// The selected item is chosen exactly once at mount from the host binding candidate pool, retained
/// across ordinary refresh, and isolated per loader instance. An empty pool fails strictly.
/// </summary>
public sealed class CuiLocalStateRandomSelectRuntimeTests
{
    private const string Markup = """
        <Cui>
          <Variable Type="string" ID="Chosen" Source="{Binding Candidates}" Selection="RandomOnce"/>
          <Container Type="Vertical Stack">
            <Button id="show" content="{Binding Chosen}" />
          </Container>
        </Cui>
        """;

    private static CuiViewModel Host(params string[] candidates)
    {
        var host = new CuiViewModel();
        host.Set("Candidates", candidates);
        return host;
    }

    [Fact]
    public async Task RandomOnce_chooses_from_pool_via_injected_chooser_and_survives_refresh()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(Host("😀", "😎", "🎯"));
            loader.RandomSelectionOverride = list => list[0];

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Empty(diagnostics);
            var surface = Assert.IsType<CuiContainerControl>(root!);
            loader.WireBindings(surface);

            var window = new Window { Content = root, Width = 400, Height = 200 };
            window.Show(); Pump();
            try
            {
                var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "show");
                Assert.Equal("😀", Assert.IsType<string>(button.Content));

                // Ordinary binding refresh must NOT resample the retained selection.
                loader.RefreshBindings(); Pump();
                Assert.Equal("😀", Assert.IsType<string>(button.Content));

                // A different chooser policy is still honored only at mount; refresh keeps it.
                loader.RandomSelectionOverride = list => list[list.Count - 1];
                loader.RefreshBindings(); Pump();
                Assert.Equal("😀", Assert.IsType<string>(button.Content));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_instances_isolate_across_two_loaders()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loaderA = new CuiControlLoader();
            using var loaderB = new CuiControlLoader();
            loaderA.SetSurface("Imagine"); loaderB.SetSurface("Imagine");
            loaderA.SetAppearance(CuiAppearance.Dark); loaderB.SetAppearance(CuiAppearance.Dark);
            loaderA.SetBindingContext(Host("A1", "A2"));
            loaderB.SetBindingContext(Host("B1", "B2"));
            loaderA.RandomSelectionOverride = list => list[0];
            loaderB.RandomSelectionOverride = list => list[list.Count - 1];

            var (rootA, diagA) = loaderA.LoadMarkup(Markup);
            var (rootB, diagB) = loaderB.LoadMarkup(Markup);
            Assert.Empty(diagA); Assert.Empty(diagB);
            loaderA.WireBindings(Assert.IsType<CuiContainerControl>(rootA!));
            loaderB.WireBindings(Assert.IsType<CuiContainerControl>(rootB!));

            var windowA = new Window { Content = rootA, Width = 400, Height = 200 };
            var windowB = new Window { Content = rootB, Width = 400, Height = 200 };
            windowA.Show(); windowB.Show(); Pump();
            try
            {
                var buttonA = Assert.Single(windowA.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "show");
                var buttonB = Assert.Single(windowB.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "show");
                Assert.Equal("A1", Assert.IsType<string>(buttonA.Content));
                Assert.Equal("B2", Assert.IsType<string>(buttonB.Content));
            }
            finally { windowA.Close(); windowB.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_empty_pool_fails_strictly()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(Host());

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR071");
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_missing_binding_context_fails_strictly()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR071");
            return true;
        }, CancellationToken.None);
    }

    private static CuiViewModel HostValue(object? value)
    {
        var host = new CuiViewModel();
        host.Set("Candidates", value);
        return host;
    }

    [Fact]
    public async Task RandomOnce_scalar_string_pool_is_rejected_strictly()
    {
        // A plain string is IEnumerable<char>; accepting it would split an emoji into UTF-16
        // code units. It must be rejected as a scalar, never enumerated/filtered.
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(HostValue("\U0001F600\U0001F603"));

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR072");
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_int_array_pool_is_rejected_strictly()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(HostValue(new[] { 1, 2, 3 }));

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR072");
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_mixed_nonstring_enumerable_pool_is_rejected_strictly()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(HostValue(new object[] { "a", 1 }));

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR072");
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_null_entry_in_typed_pool_is_rejected_strictly()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(HostValue(new string[] { "\U0001F600", null! }));

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR072");
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_empty_entry_in_typed_pool_is_rejected_strictly()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(HostValue(new[] { "" }));

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR072");
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_unknown_selection_is_rejected_at_runtime_load()
    {
        const string unknownSelectionMarkup = """
            <Cui>
              <Variable Type="string" ID="Chosen" Source="{Binding Candidates}" Selection="Toss"/>
              <Container Type="Vertical Stack"><Button content="{Binding Chosen}" /></Container>
            </Cui>
            """;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(Host("A", "B"));

            var (root, diagnostics) = loader.LoadMarkup(unknownSelectionMarkup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR074");
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_bool_type_is_rejected_at_runtime_load()
    {
        const string boolSelectionMarkup = """
            <Cui>
              <Variable Type="bool" ID="Chosen" Source="{Binding Candidates}" Selection="RandomOnce"/>
              <Container Type="Vertical Stack"><Button content="{Binding Chosen}" /></Container>
            </Cui>
            """;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(Host("A", "B"));

            var (root, diagnostics) = loader.LoadMarkup(boolSelectionMarkup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR075");
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_literal_initializer_is_rejected_at_runtime_load()
    {
        const string literalMarkup = """
            <Cui>
              <Variable Type="string" ID="Chosen" Source="{Binding Candidates}" Selection="RandomOnce">😀</Variable>
              <Container Type="Vertical Stack"><Button content="{Binding Chosen}" /></Container>
            </Cui>
            """;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(Host("A", "B"));

            var (root, diagnostics) = loader.LoadMarkup(literalMarkup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR076");
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_valid_existing_tree_survives_invalid_reload()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(Host("A", "B"));
            loader.RandomSelectionOverride = list => list[0];

            // First mount succeeds and is wired/visible.
            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.NotNull(root); Assert.Empty(diagnostics);
            var surface = Assert.IsType<CuiContainerControl>(root!);
            loader.WireBindings(surface);
            var window = new Window { Content = root, Width = 400, Height = 200 };
            window.Show(); Pump();
            try
            {
                var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "show");
                Assert.Equal("A", Assert.IsType<string>(button.Content));

                // A strict-invalid reload (scalar string pool) must fail BEFORE retiring the
                // valid tree, leaving the prior input/action/bindings operational.
                loader.SetBindingContext(HostValue("😀"));
                var (root2, diagnostics2) = loader.LoadMarkup(Markup);
                Assert.Null(root2);
                Assert.Contains(diagnostics2, diagnostic => diagnostic.Code == "CUIR072");

                Assert.True(button.IsVisible);
                Assert.Equal("A", Assert.IsType<string>(button.Content));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_chooser_reentrant_reload_is_rejected_by_generation_guard()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(Host("A", "B"));
            // The deterministic chooser synchronously reloads once (mounts replacement B) and then
            // returns a candidate; the stale outer attempt must be rejected, not committed.
            var reentered = false;
            loader.RandomSelectionOverride = list =>
            {
                if (!reentered)
                {
                    reentered = true;
                    loader.LoadMarkup(Markup);
                }
                return list[0];
            };

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR073");
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_source_getter_reentrant_reload_is_rejected_by_generation_guard()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            // The source getter synchronously reloads (mounts replacement B) before returning the
            // pool; the stale outer attempt must be rejected, not committed.
            var reentrant = new ReentrantBindingContext(loader, new[] { "A", "B" });
            loader.SetBindingContext(reentrant);

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR073");
            return true;
        }, CancellationToken.None);
    }

    private sealed class ReentrantBindingContext : ICuiBindingContext
    {
        private readonly CuiControlLoader _loader;
        private readonly string[] _pool;
        private bool _reentered;
        public ReentrantBindingContext(CuiControlLoader loader, string[] pool)
        {
            _loader = loader;
            _pool = pool;
        }
        public bool TryGetValue(string path, out object? value)
        {
            if (path == "Candidates" && !_reentered)
            {
                _reentered = true;
                _loader.LoadMarkup(Markup);
            }
            value = _pool;
            return true;
        }
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();
}
