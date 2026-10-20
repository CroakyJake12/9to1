using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;
using System.Collections;
using System.Collections.Generic;
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

    /// <summary>Markup with a real Input bound to a draft plus an authored action button, used to
    /// prove an invalid reload leaves the prior real input editable and the action operational.</summary>
    private const string OperationalMarkup = """
        <Cui>
          <Variable Type="string" ID="Chosen" Source="{Binding Candidates}" Selection="RandomOnce"/>
          <Container Type="Vertical Stack">
            <Input id="input" Type="Text" Value="{Binding Draft}" />
            <Button id="act" action="do-it" content="Go" />
            <Button id="show" content="{Binding Chosen}" />
          </Container>
        </Cui>
        """;

    private static (CuiViewModel Model, List<string> Actions) OperationalHost(params string[] candidates)
    {
        var model = new CuiViewModel();
        model.Set("Candidates", candidates);
        model.Set("Draft", "draft");
        var actions = new List<string>();
        model.On("do-it", _ => actions.Add("do-it"));
        return (model, actions);
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
    public async Task RandomOnce_bool_with_source_but_no_selection_is_rejected()
    {
        // A bool literal must not silently ignore a Source presented without a Selection: the
        // combination is a malformed new-initializer shape and must be rejected before any draw or
        // lineage retirement (CUIR077).
        const string boolSourceMarkup = """
            <Cui>
              <Variable Type="bool" ID="Flag" Source="{Binding Candidates}">true</Variable>
              <Container Type="Vertical Stack"><Button content="{Binding Flag}" /></Container>
            </Cui>
            """;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(Host("A", "B"));

            var (root, diagnostics) = loader.LoadMarkup(boolSourceMarkup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR077");
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
            Control? rootB = null;
            loader.RandomSelectionOverride = list =>
            {
                if (!reentered)
                {
                    reentered = true;
                    var (b, diagB) = loader.LoadMarkup(Markup);
                    rootB = b;
                }
                return list[0];
            };

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR073");

            // The replacement B root mounted by the reentrant load survives and stays operational:
            // its real button renders the B-frame selection and remains visible/interactive.
            Assert.NotNull(rootB);
            var surfaceB = Assert.IsType<CuiContainerControl>(rootB!);
            loader.WireBindings(surfaceB);
            var window = new Window { Content = rootB, Width = 400, Height = 200 };
            window.Show(); Pump();
            try
            {
                var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "show");
                Assert.Equal("A", Assert.IsType<string>(button.Content));
                Assert.True(button.IsVisible);
                Assert.True(button.IsEnabled);
            }
            finally { window.Close(); }
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
            loader.RandomSelectionOverride = candidates => candidates[0];
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            // The source getter synchronously reloads (mounts replacement B) before returning the
            // pool; the stale outer attempt must be rejected, not committed.
            var reentrant = new ReentrantBindingContext(loader, new[] { "A", "B" });
            loader.SetBindingContext(reentrant);

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR073");

            // The replacement B root survives and remains operational (its button renders/works).
            Assert.NotNull(reentrant.RootB);
            var surfaceB = Assert.IsType<CuiContainerControl>(reentrant.RootB!);
            loader.WireBindings(surfaceB);
            var window = new Window { Content = reentrant.RootB, Width = 400, Height = 200 };
            window.Show(); Pump();
            try
            {
                var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "show");
                Assert.Equal("A", Assert.IsType<string>(button.Content));
                Assert.True(button.IsVisible);
            }
            finally { window.Close(); }
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
        public Control? RootB { get; private set; }
        public bool TryGetValue(string path, out object? value)
        {
            if (path == "Candidates" && !_reentered)
            {
                _reentered = true;
                var (b, _) = _loader.LoadMarkup(Markup);
                RootB = b;
            }
            value = _pool;
            return true;
        }
    }

    [Fact]
    public async Task RandomOnce_invalid_reload_leaves_real_input_editable_and_button_action_operational()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var (model, actions) = OperationalHost("A", "B");
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(model);
            loader.SetActionDispatcher(model);
            loader.RandomSelectionOverride = list => list[0];

            var (root, diagnostics) = loader.LoadMarkup(OperationalMarkup);
            Assert.NotNull(root); Assert.Empty(diagnostics);
            var surface = Assert.IsType<CuiContainerControl>(root!);
            loader.WireBindings(surface);
            var window = new Window { Content = root, Width = 400, Height = 200 };
            window.Show(); Pump();
            try
            {
                var input = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), b => AutomationProperties.GetAutomationId(b) == "input");
                var act = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "act");
                var show = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "show");
                Assert.Equal("A", Assert.IsType<string>(show.Content));

                // Real input editable: editing round-trips to the bound model draft; the authored
                // button action dispatches through the real action dispatcher.
                input.Text = "edited"; Pump();
                Assert.Equal("edited", model.Get("Draft"));
                act.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync();
                Assert.Contains("do-it", actions);

                // Strict-invalid reload (scalar pool) fails BEFORE retiring the tree: the old real
                // input stays editable, the draft binding stays live and the button action works.
                model.Set("Candidates", "\U0001F600"); // scalar pool -> CUIR072
                var (root2, diagnostics2) = loader.LoadMarkup(OperationalMarkup);
                Assert.Null(root2);
                Assert.Contains(diagnostics2, diagnostic => diagnostic.Code == "CUIR072");

                Assert.True(input.IsVisible);
                input.Text = "still-editing"; Pump();
                Assert.Equal("still-editing", model.Get("Draft"));
                actions.Clear();
                act.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync();
                Assert.Contains("do-it", actions);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_late_invalid_declaration_zero_draws()
    {
        // A document whose FIRST RandomOnce variable is valid but whose LATER declaration is
        // malformed (a string Source without a Selection) must be rejected WITHOUT invoking the
        // chooser even once: pass 1 validates every declaration before any draw.
        const string lateInvalidMarkup = """
            <Cui>
              <Variable Type="string" ID="First" Source="{Binding Candidates}" Selection="RandomOnce"/>
              <Variable Type="string" ID="Second" Source="{Binding Candidates}"/>
              <Container Type="Vertical Stack"><Button content="{Binding First}" /></Container>
            </Cui>
            """;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(Host("A", "B"));
            var draws = 0;
            loader.RandomSelectionOverride = list => { draws++; return list[0]; };

            var (root, diagnostics) = loader.LoadMarkup(lateInvalidMarkup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR077");
            Assert.Equal(0, draws); // no draw happened before the later invalid declaration is found
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_chooser_invalid_result_is_rejected_and_preserves_prior_tree()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(Host("A", "B"));
            loader.RandomSelectionOverride = list => list[0];

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

                // Chooser returning a candidate NOT in the snapshotted pool is rejected, and the
                // valid mounted tree remains operational.
                loader.RandomSelectionOverride = list => "Z";
                var (root2, diagnostics2) = loader.LoadMarkup(Markup);
                Assert.Null(root2);
                Assert.Contains(diagnostics2, diagnostic => diagnostic.Code == "CUIR081");
                Assert.Equal("A", Assert.IsType<string>(button.Content));

                // Empty chooser result likewise fails strictly and preserves the mounted tree.
                loader.RandomSelectionOverride = list => "";
                var (root3, diagnostics3) = loader.LoadMarkup(Markup);
                Assert.Null(root3);
                Assert.Contains(diagnostics3, diagnostic => diagnostic.Code == "CUIR080");
                Assert.Equal("A", Assert.IsType<string>(button.Content));
                Assert.True(button.IsVisible);
                Assert.True(button.IsEnabled);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_pool_copy_isolates_from_host_list_mutation()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            var pool = new List<string> { "A", "B" };
            var host = new CuiViewModel();
            host.Set("Candidates", pool);
            loader.SetBindingContext(host);
            loader.RandomSelectionOverride = list => list[0];

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

                // Mutate the LIVE host list after mount: the frame's owned snapshot copy is
                // unaffected, so ordinary refresh retains the chosen item and performs no redraw.
                pool.Clear(); pool.Add("Y");
                host.Set("Candidates", pool);
                loader.RefreshBindings(); Pump();
                Assert.Equal("A", Assert.IsType<string>(button.Content));
            }
            finally { window.Close(); }

            // A FRESH mount after mutation observes the NEW pool, proving the earlier frame kept its
            // own copy rather than the live host list.
            using var loader2 = new CuiControlLoader();
            loader2.SetSurface("Imagine");
            loader2.SetAppearance(CuiAppearance.Dark);
            loader2.SetBindingContext(host);
            loader2.RandomSelectionOverride = list => list[0];
            var (root2, diagnostics2) = loader2.LoadMarkup(Markup);
            Assert.NotNull(root2); Assert.Empty(diagnostics2);
            var surface2 = Assert.IsType<CuiContainerControl>(root2!);
            loader2.WireBindings(surface2);
            var window2 = new Window { Content = root2, Width = 400, Height = 200 };
            window2.Show(); Pump();
            try
            {
                var button2 = Assert.Single(window2.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "show");
                Assert.Equal("Y", Assert.IsType<string>(button2.Content));
            }
            finally { window2.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_exactly_one_draw_per_mounted_frame_zero_on_refresh()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(Host("A", "B"));
            var draws = 0;
            loader.RandomSelectionOverride = list => { draws++; return list[0]; };

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.NotNull(root); Assert.Empty(diagnostics);
            Assert.Equal(1, draws); // exactly one draw for the mounted frame

            var surface = Assert.IsType<CuiContainerControl>(root!);
            loader.WireBindings(surface);
            var window = new Window { Content = root, Width = 400, Height = 200 };
            window.Show(); Pump();
            try
            {
                var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "show");
                Assert.Equal("A", Assert.IsType<string>(button.Content));

                // Ordinary refresh never redraws; the mounted choice is stable.
                loader.RefreshBindings(); Pump();
                loader.RefreshBindings(); Pump();
                Assert.Equal("A", Assert.IsType<string>(button.Content));
                Assert.Equal(1, draws);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_chooser_dispose_is_rejected_by_generation_guard()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(Host("A", "B"));
            var disposed = false;
            loader.RandomSelectionOverride = list =>
            {
                if (!disposed)
                {
                    disposed = true;
                    loader.Dispose();
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
    public async Task RandomOnce_pool_count_callback_reentrant_reload_leaves_operational_B_editable()
    {
        // A host Count callback that synchronously reloads an operational replacement B must abandon
        // the stale outer attempt (CUIR073) and never read another Count/index/enumerator/chooser on
        // the old attempt. B's real Input stays editable with a live draft binding and its authored
        // action dispatches exactly once through the real dispatcher.
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var (model, actions) = OperationalHost("A", "B");
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(model);
            loader.SetActionDispatcher(model);
            var draws = 0;
            loader.RandomSelectionOverride = list => { draws++; return list[0]; };
            var pool = new ReentrantReloadList(loader, new[] { "A", "B" });
            model.Set("Candidates", pool);

            var (root, diagnostics) = loader.LoadMarkup(OperationalMarkup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR073");
            // Only the replacement B mount drew; the abandoned outer attempt reached no chooser.
            Assert.Equal(1, draws);
            // No enumerator was ever called; the snapshot is purely indexed and single-pass.
            Assert.Equal(0, pool.EnumeratorCalls);

            Assert.NotNull(pool.RootB);
            var surfaceB = Assert.IsType<CuiContainerControl>(pool.RootB!);
            loader.WireBindings(surfaceB);
            var window = new Window { Content = pool.RootB, Width = 400, Height = 200 };
            window.Show(); Pump();
            try
            {
                var input = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), b => AutomationProperties.GetAutomationId(b) == "input");
                var act = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "act");

                input.Text = "edited"; Pump();
                Assert.Equal("edited", model.Get("Draft"));
                actions.Clear();
                act.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync();
                Assert.Contains("do-it", actions);

                // The host pool was touched only by the single-pass ordered snapshot: one outer Count
                // read (which abandoned) plus B's own mount's Count + two indexed reads. Interacting
                // with B's real input/action triggers no further host pool callbacks.
                Assert.Equal(2, pool.CountReads);
                Assert.Equal(2, pool.IndexReads);
                Assert.Equal(0, pool.EnumeratorCalls);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_pool_count_callback_disposes_loader_is_rejected()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            var pool = new DisposingList(loader, new[] { "A", "B" });
            var host = new CuiViewModel();
            host.Set("Candidates", pool);
            loader.SetBindingContext(host);

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR073");
            // Dispose during a Count read abandons the attempt and reads nothing further.
            Assert.Equal(0, pool.IndexReads);
            Assert.Equal(0, pool.EnumeratorCalls);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_throwing_enumerator_list_is_never_enumerated()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            var pool = new ThrowingEnumeratorList(new[] { "\U0001F600", "B" });
            var host = new CuiViewModel();
            host.Set("Candidates", pool);
            loader.SetBindingContext(host);
            loader.RandomSelectionOverride = list => list[0];

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.NotNull(root); Assert.Empty(diagnostics);
            var surface = Assert.IsType<CuiContainerControl>(root!);
            loader.WireBindings(surface);
            var window = new Window { Content = root, Width = 400, Height = 200 };
            window.Show(); Pump();
            try
            {
                var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "show");
                Assert.Equal("\U0001F600", Assert.IsType<string>(button.Content));
                Assert.Equal(0, pool.EnumeratorCalls);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_indexed_values_take_precedence_over_enumerated()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            // Enumerator yields Z/Y; indexer yields the true pool A/B. Only the indexed snapshot may
            // participate and its validated copied values must match the indexer exactly.
            var pool = new DivergeList(new[] { "A", "B" }, new[] { "Z", "Y" });
            var host = new CuiViewModel();
            host.Set("Candidates", pool);
            loader.SetBindingContext(host);
            loader.RandomSelectionOverride = list => list[0];

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
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_chooser_mutation_attempt_cannot_bypass_membership()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(Host("A", "B"));
            loader.RandomSelectionOverride = list => list[0];

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

                // A chooser that tries to mutate the pool (cast to mutable array/IList) must fail to
                // reach storage; returning an out-of-pool value is rejected against the immutable
                // original contents (CUIR081) and the mounted tree stays operational.
                loader.RandomSelectionOverride = list =>
                {
                    if (list is string[] arr) arr[0] = "Z";
                    else if (list is IList mutable) mutable[0] = "Z";
                    return "Z";
                };
                var (root2, diagnostics2) = loader.LoadMarkup(Markup);
                Assert.Null(root2);
                Assert.Contains(diagnostics2, diagnostic => diagnostic.Code == "CUIR081");
                Assert.Equal("A", Assert.IsType<string>(button.Content));
                Assert.True(button.IsVisible);
                Assert.True(button.IsEnabled);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_host_list_mutation_during_chooser_admission_is_isolated()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            var pool = new List<string> { "A", "B" };
            var host = new CuiViewModel();
            host.Set("Candidates", pool);
            loader.SetBindingContext(host);
            loader.RandomSelectionOverride = list =>
            {
                // Mutate the LIVE host pool during admission; the owned snapshot must isolate it.
                pool[0] = "HACKED";
                return list[0];
            };

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.NotNull(root); Assert.Empty(diagnostics);
            var surface = Assert.IsType<CuiContainerControl>(root!);
            loader.WireBindings(surface);
            var window = new Window { Content = root, Width = 400, Height = 200 };
            window.Show(); Pump();
            try
            {
                var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "show");
                // The frame's owned snapshot retained the original "A", unaffected by the mid-admission
                // host mutation.
                Assert.Equal("A", Assert.IsType<string>(button.Content));
                loader.RefreshBindings(); Pump();
                Assert.Equal("A", Assert.IsType<string>(button.Content));
            }
            finally { window.Close(); }

            // A FRESH mount observes the mutated host pool.
            using var loader2 = new CuiControlLoader();
            loader2.SetSurface("Imagine");
            loader2.SetAppearance(CuiAppearance.Dark);
            loader2.SetBindingContext(host);
            loader2.RandomSelectionOverride = list => list[0];
            var (root2, diagnostics2) = loader2.LoadMarkup(Markup);
            Assert.NotNull(root2); Assert.Empty(diagnostics2);
            var surface2 = Assert.IsType<CuiContainerControl>(root2!);
            loader2.WireBindings(surface2);
            var window2 = new Window { Content = root2, Width = 400, Height = 200 };
            window2.Show(); Pump();
            try
            {
                var button2 = Assert.Single(window2.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "show");
                Assert.Equal("HACKED", Assert.IsType<string>(button2.Content));
            }
            finally { window2.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_exact_count_and_index_read_counts()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            var pool = new CountingList(new[] { "A", "B", "C" });
            var host = new CuiViewModel();
            host.Set("Candidates", pool);
            loader.SetBindingContext(host);
            loader.RandomSelectionOverride = list => list[0];

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.NotNull(root); Assert.Empty(diagnostics);
            Assert.Equal(1, pool.CountReads);
            Assert.Equal(3, pool.IndexReads);
            Assert.Equal(0, pool.EnumeratorCalls);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_retains_full_unicode_sequences_verbatim()
    {
        // A ZWJ-composed sequence (woman technologist) and a VS16+ZWJ sequence (rainbow flag) must
        // survive the ordered snapshot byte-for-byte; normalization belongs only to catalogue
        // matching, never to the retained chosen sequence.
        var zwj = "\U0001F469\u200D\U0001F4BB";         // woman technologist
        var vs16 = "\U0001F3F3\uFE0F\u200D\U0001F308"; // rainbow flag (VS16 + ZWJ)
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            var pool = new List<string> { zwj, vs16, "A" };
            var host = new CuiViewModel();
            host.Set("Candidates", pool);
            loader.SetBindingContext(host);
            loader.RandomSelectionOverride = list => list[0];

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.NotNull(root); Assert.Empty(diagnostics);
            var surface = Assert.IsType<CuiContainerControl>(root!);
            loader.WireBindings(surface);
            var window = new Window { Content = root, Width = 400, Height = 200 };
            window.Show(); Pump();
            try
            {
                var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "show");
                Assert.Equal(zwj, Assert.IsType<string>(button.Content));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    /// <summary>Host typed pool whose Count read synchronously reloads an operational replacement B
    /// exactly once, letting a test prove the abandoned outer attempt performs no further reads.</summary>
    private sealed class ReentrantReloadList : IReadOnlyList<string>
    {
        private readonly CuiControlLoader _loader;
        private readonly string[] _pool;
        private bool _reloaded;
        public ReentrantReloadList(CuiControlLoader loader, string[] pool)
        {
            _loader = loader;
            _pool = pool;
        }
        public Control? RootB { get; private set; }
        public int CountReads;
        public int IndexReads;
        public int EnumeratorCalls;
        public int Count
        {
            get
            {
                CountReads++;
                if (!_reloaded)
                {
                    _reloaded = true;
                    var (b, _) = _loader.LoadMarkup(OperationalMarkup);
                    RootB = b;
                }
                return _pool.Length;
            }
        }
        public string this[int index] { get { IndexReads++; return _pool[index]; } }
        public IEnumerator<string> GetEnumerator() { EnumeratorCalls++; return ((IEnumerable<string>)_pool).GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Host typed pool whose Count read disposes the loader once.</summary>
    private sealed class DisposingList : IReadOnlyList<string>
    {
        private readonly CuiControlLoader _loader;
        private readonly string[] _pool;
        private bool _disposed;
        public DisposingList(CuiControlLoader loader, string[] pool)
        {
            _loader = loader;
            _pool = pool;
        }
        public int CountReads;
        public int IndexReads;
        public int EnumeratorCalls;
        public int Count
        {
            get
            {
                CountReads++;
                if (!_disposed)
                {
                    _disposed = true;
                    _loader.Dispose();
                }
                return _pool.Length;
            }
        }
        public string this[int index] { get { IndexReads++; return _pool[index]; } }
        public IEnumerator<string> GetEnumerator() { EnumeratorCalls++; return ((IEnumerable<string>)_pool).GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>An indexable typed list whose enumerator always throws; proving it is never called
    /// verifies the snapshot reads only the finite ordered indices.</summary>
    private sealed class ThrowingEnumeratorList : IReadOnlyList<string>
    {
        private readonly string[] _pool;
        public ThrowingEnumeratorList(string[] pool) => _pool = pool;
        public int EnumeratorCalls;
        public int Count => _pool.Length;
        public string this[int index] => _pool[index];
        public IEnumerator<string> GetEnumerator()
        {
            EnumeratorCalls++;
            throw new InvalidOperationException("enumerator must never be called");
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>A typed list whose enumerator disagrees with its indexer, proving the snapshot uses
    /// only the finite ordered indexed values (never enumeration).</summary>
    private sealed class DivergeList : IReadOnlyList<string>
    {
        private readonly string[] _indexed;
        private readonly string[] _enumerated;
        public DivergeList(string[] indexed, string[] enumerated)
        {
            _indexed = indexed;
            _enumerated = enumerated;
        }
        public int Count => _indexed.Length;
        public string this[int index] => _indexed[index];
        public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_enumerated).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Counts every host Count/index read to assert exact single-pass use.</summary>
    private sealed class CountingList : IReadOnlyList<string>
    {
        private readonly string[] _pool;
        public CountingList(string[] pool) => _pool = pool;
        public int CountReads;
        public int IndexReads;
        public int EnumeratorCalls;
        public int Count { get { CountReads++; return _pool.Length; } }
        public string this[int index] { get { IndexReads++; return _pool[index]; } }
        public IEnumerator<string> GetEnumerator() { EnumeratorCalls++; return ((IEnumerable<string>)_pool).GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();
}
